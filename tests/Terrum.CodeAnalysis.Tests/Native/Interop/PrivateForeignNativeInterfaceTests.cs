// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Native.Interop;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class PrivateForeignNativeInterfaceTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeFniTryRead(IntPtr receiver, IntPtr output);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeFniDispatch(IntPtr receiver);

    [Test]
    public async Task PrivateFniNativeOutputDispatchPublishesReturnedAggregateAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "terrum/private-semantic-111-foreign-native-interfaces-1";
        const string providerIdentity = "PrivateFniRuntimeProvider";
        const string ownerKey = "abi.iprobe";
        const string memberKey = "abi.iprobe.try-read";
        var providerSource = SyntaxTree.Parse("""
            using Terrum.Interop;
            public int32 Identity(int32 value) { return value; }
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                [param(value): NativeOutput(
                    When: NativeOutputCondition.NonZero)]
                public unsafe int32 TryRead(write ptr<int32> value);
            }
            """);
        var (sourceNativeRegistration, sourceOutputRegistration)
            = NativeOutputCoreRegistrationTests
                .CreateCombinedRegistrations(
                    includeForeignNativeInterface: true);
        var moduleBindings = CoreProviderModuleBindings.Create(
            providerIdentity,
            sourceNativeRegistration.ModuleBindings.Providers,
            sourceNativeRegistration.ModuleBindings.Declarations);
        var nativeRegistration = NativeCoreRegistrationSet.Create(
            moduleBindings, sourceNativeRegistration.CallingConventions,
            sourceNativeRegistration.ResolutionModes,
            sourceNativeRegistration.ResolutionPolicies,
            sourceNativeRegistration.NativeImport,
            sourceNativeRegistration.NativeCallable,
            sourceNativeRegistration.NativeCallConv,
            sourceNativeRegistration.NativeExport,
            sourceNativeRegistration.ForeignNativeInterface);
        var nativeOutputRegistration
            = NativeOutputCoreRegistrationSet.Create(moduleBindings,
                sourceOutputRegistration.OutputConditions,
                sourceOutputRegistration.NativeOutput);
        var sourceLayoutRegistration
            = NativeCoreRegistrationTests.CreateLayoutRegistration(
                sourceNativeRegistration);
        var layoutRegistration = LayoutCoreRegistrationSet.Create(
            moduleBindings, sourceLayoutRegistration.LayoutKinds,
            sourceLayoutRegistration.Layout,
            sourceLayoutRegistration.Blittable);
        var options = new CompilationOptions {
            NativeCoreRegistration = nativeRegistration,
            LayoutCoreRegistration = layoutRegistration,
            NativeOutputCoreRegistration =
                nativeOutputRegistration,
        };
        var providerCompilation
            = Compilation.CreateLibrary(providerSource, options);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program!;
        var providerInputs = PrivateModuleIdentityInputs
            .CreateFromSelectors(providerProgram, providerIdentity, [], [],
                [], [], [
                    new PrivateForeignInterfaceDefinitionAssignment(0,
                        ownerKey, true, [
                            new PrivateForeignInterfaceMemberDefinitionAssignment(
                                0, memberKey),
                        ]),
                ], []);
        var producer = ModuleCompiler.CreateProducerWithSchema(
            providerProgram, ["Identity"], schemaIdentity, providerInputs);
        var reference = ModuleCompiler.CreateReferenceWithSchema(producer,
            schemaIdentity);
        var consumerSource = SyntaxTree.Parse("""
            int32 Main() { return 0; }
            public unsafe int32 Dispatch(ptr<void> receiver) {
                init int32 value;
                unsafe {
                    int32 status = IProbe.TryRead(receiver, &init value);
                    if (status != 0) { return value; }
                }
                return 0;
            }
            """);
        var consumerCompilation = Compilation.Create(consumerSource,
            reference, new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
            });
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program!;
        var consumerInputs = PrivateModuleIdentityInputs
            .CreateFromSelectors(consumerProgram,
                "PrivateFniRuntimeConsumer", [], [], [], [], [], [
                    new PrivateForeignInterfaceImportAssignment(0,
                        "consumer.iprobe", providerIdentity, ownerKey, [
                            new PrivateForeignInterfaceMemberImportAssignment(
                                0, "consumer.iprobe.try-read", memberKey),
                        ]),
                ]);
        var consumer = ModuleCompiler.CreateConsumerWithSchema(
            consumerProgram, producer, schemaIdentity, consumerInputs);
        var linked = ModuleCompiler.Link(consumer, producer);
        var libraryProgram = linked with { EntryPoint = -1 };

        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-private-fni-runtime-");
        var libraryPath = Path.Combine(directory, "consumer.dll");
        var library = IntPtr.Zero;
        var vtable = IntPtr.Zero;
        var receiver = IntPtr.Zero;
        try
        {
            await File.WriteAllBytesAsync(libraryPath,
                NativeEmitter.EmitLibrary(libraryProgram, ["Dispatch"],
                    NativeRuntimePolicy.FailFast)).ConfigureAwait(true);
            library = NativeLibrary.Load(libraryPath);
            vtable = Marshal.AllocHGlobal(IntPtr.Size);
            receiver = Marshal.AllocHGlobal(IntPtr.Size);
            var callback = new NativeFniTryRead(PublishOutput);
            Marshal.WriteIntPtr(vtable,
                Marshal.GetFunctionPointerForDelegate(callback));
            Marshal.WriteIntPtr(receiver, vtable);
            var dispatch = Marshal.GetDelegateForFunctionPointer<
                NativeFniDispatch>(NativeLibrary.GetExport(library,
                    "Dispatch"));
            Assert.That(dispatch(receiver), Is.EqualTo(42));
            GC.KeepAlive(callback);
        }
        finally
        {
            if (library != IntPtr.Zero)
            {
                NativeLibrary.Free(library);
            }
            if (receiver != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(receiver);
            }
            if (vtable != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(vtable);
            }
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    private static int PublishOutput(IntPtr receiver, IntPtr output)
    {
        _ = receiver;
        Marshal.WriteInt32(output, 42);
        return 1;
    }
}
