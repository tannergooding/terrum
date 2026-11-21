// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

internal sealed class TrRmNativeParameterAggregateBackingTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePair
    {
        public int First;
        public int Second;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ProbeDelegate(nint process, NativePair value,
        nuint bytesToRead, nint bytesRead);

    [Test]
    public async Task StandardNativeOutputUsesAggregateNativeParameterBackingAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-addresses-1";
        const string moduleIdentity = "native-output-aggregate-backing";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Pair {
                public int32 First;
                public int32 Second;
            }
            [param(buffer): DependsOn(nameof(baseAddress))]
            [Terrum.Interop.NativeImport("kernel32.dll",
                "ReadProcessMemory")]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(5, Write, NoEscape, 1)]
            extern unsafe int32 ReadProcessMemory(
                ptr<void> process, ptr<Pair> baseAddress,
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<Pair> buffer, nuint bytesToRead,
                write ptr<nuint> bytesRead);

            public unsafe int32 Probe(ptr<void> process, Pair value,
                nuint bytesToRead, write ptr<nuint> bytesRead) {
                mut Pair backing = value;
                init mut Pair buffer;
                unsafe {
                    return ReadProcessMemory(process, &backing,
                        &init buffer, bytesToRead, bytesRead);
                }
            }
            """), NativeOutputCoreRegistrationTests
                .CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity,
            moduleTargetRequirement: 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity + "-reemitted", schemaIdentity,
            moduleTargetRequirement: 1);
        var reemittedIdentity = moduleIdentity + "-reemitted";
        var reemittedInput = new TrRmStandardArtifactInput(
            schemaIdentity, reemitted);
        _ = TrRmExecutableCompiler.LoadStandard(reemittedInput,
            reemittedIdentity, []);
        var image = NativeModuleCompiler.EmitStandardLibrary(
            reemittedInput, reemittedIdentity, [], ["Probe"],
            NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-nativeparameter-aggregate-backing-");
        try
        {
            var imagePath = Path.Combine(directory, "library.dll");
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            var library = NativeLibrary.Load(imagePath);
            nint bytesRead = 0;
            try
            {
                bytesRead = Marshal.AllocHGlobal(sizeof(long));
                Marshal.WriteInt64(bytesRead, 0);
                var function = Marshal.GetDelegateForFunctionPointer<
                    ProbeDelegate>(NativeLibrary.GetExport(library, "Probe"));
                Assert.That(function(new IntPtr(-1), new NativePair {
                    First = 11,
                    Second = 22,
                }, (nuint)Marshal.SizeOf<NativePair>(), bytesRead),
                    Is.EqualTo(1));
                Assert.That(Marshal.ReadInt64(bytesRead), Is.EqualTo(8));
            }
            finally
            {
                if (bytesRead != 0)
                {
                    Marshal.FreeHGlobal(bytesRead);
                }
                NativeLibrary.Free(library);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public void StandardNativeOutputReemits16ByteAggregateNativeParameterBacking()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-addresses-1";
        const string moduleIdentity = "native-output-aggregate-backing-16";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Quad {
                public int32 First;
                public int32 Second;
                public int32 Third;
                public int32 Fourth;
            }
            [param(buffer): DependsOn(nameof(baseAddress))]
            [Terrum.Interop.NativeImport("kernel32.dll",
                "ReadProcessMemory")]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(5, Write, NoEscape, 1)]
            extern unsafe int32 ReadProcessMemory(
                ptr<void> process, ptr<Quad> baseAddress,
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<Quad> buffer, nuint bytesToRead,
                write ptr<nuint> bytesRead);

            public unsafe int32 Probe(ptr<void> process, Quad value,
                nuint bytesToRead, write ptr<nuint> bytesRead) {
                mut Quad backing = value;
                init mut Quad buffer;
                unsafe {
                    return ReadProcessMemory(process, &backing,
                        &init buffer, bytesToRead, bytesRead);
                }
            }
            """), NativeOutputCoreRegistrationTests
                .CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(program.Structures.Single(
            static structure => structure.Name == "Quad").Size,
            Is.EqualTo(16));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            program, moduleIdentity, schemaIdentity,
            moduleTargetRequirement: 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var caller = loaded.Functions.Single(static function =>
            function.Name == "Probe");
        BoundNativeStorageAddress? aggregateBacking = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body,
            expression =>
            {
                if (expression is BoundNativeStorageAddress address)
                {
                    aggregateBacking = address;
                }
            }), Is.True);
        Assert.That(aggregateBacking, Is.Not.Null);
        Assert.That(aggregateBacking!.ElementCount, Is.EqualTo(1UL));
        Assert.That(aggregateBacking.ElementStride, Is.EqualTo(16UL));
        Assert.That(aggregateBacking.ByteExtent, Is.EqualTo(16UL));
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, moduleTargetRequirement: 1);
        Assert.That(reemitted, Is.EqualTo(artifact));
        var reemittedInput = new TrRmStandardArtifactInput(schemaIdentity,
            reemitted);
        _ = TrRmExecutableCompiler.LoadStandard(reemittedInput,
            moduleIdentity, []);
        Assert.That(NativeModuleCompiler.EmitStandardLibrary(
                reemittedInput, moduleIdentity, [], ["Probe"],
                NativeRuntimePolicy.FailFast),
            Is.Not.Empty);
    }
}
