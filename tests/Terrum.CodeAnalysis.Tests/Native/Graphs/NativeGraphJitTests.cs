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
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Native.Graphs;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class NativeGraphJitTests
{
    private static string CompilerApphost()
    {
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        return Path.Combine(root, "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
    }

    [Test]
    public async Task RejectsUnboundedOrNullInputsBeforeSnapshotAllocationAsync()
    {
        const int MaximumArtifactBytes = 16 * 1024 * 1024;
        const NativeRuntimePolicy Policy = NativeRuntimePolicy.FailFast;
        var timeout = TimeSpan.FromSeconds(10);
        var root = new byte[51];
        var provider = new byte[1];
        _ = (await Assert.ThrowsAsync<ModuleContractException>(() =>
            NativeModuleCompiler.RunGraphJitAsync(root, [], "missing.dll", timeout, Policy)).ConfigureAwait(false))!;
        _ = (await Assert.ThrowsAsync<ArgumentException>(() =>
            NativeModuleCompiler.RunGraphJitAsync(root, new byte[1][], "missing.dll", timeout, Policy)).ConfigureAwait(false))!;
        _ = (await Assert.ThrowsAsync<ModuleFormatException>(() =>
            NativeModuleCompiler.RunGraphJitAsync(new byte[MaximumArtifactBytes + 1],
                [provider], "missing.dll", timeout, Policy)).ConfigureAwait(false))!;
        _ = (await Assert.ThrowsAsync<ModuleFormatException>(() =>
            NativeModuleCompiler.RunGraphJitAsync(root,
                [new byte[MaximumArtifactBytes + 1]], "missing.dll", timeout, Policy)).ConfigureAwait(false))!;
    }

    [Test]
    public async Task GraphJitExecutesDirectSystem32ImportsAndPointerInputsAlongsideProviderImportsAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const NativeRuntimePolicy Policy = NativeRuntimePolicy.FailFast;
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""

            [Terrum.Interop.NativeCallable]
            public int32 ProviderCallback(int32 value) { return value; }

            public int32 ProviderValue() { return 42; }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions(
                (BoundRuntimePolicy)Policy));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(providerCompilation.Program!,
            ["ProviderCallback", "ProviderValue"], "GraphJitProvider", Policy);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("kernel32.dll", "GetCurrentThreadId")]

            extern unsafe uint32 GetCurrentThreadId();
            [Terrum.Interop.NativeImport("kernel32.dll", "CompareStringOrdinal")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(3, Read, NoEscape, 1)]
            extern int32 CompareStringOrdinal(ptr<char16> first, int32 firstLength,
                ptr<char16> second, int32 secondLength, int32 ignoreCase);

            int32 Main() {
                char16 first = 'A';
                char16 second = 'A';
                unsafe {
                    if (GetCurrentThreadId() == 0) { return -1; }
                    if (CompareStringOrdinal(&first, 1, &second, 1, 0) != 2) { return -2; }
                    [Terrum.Interop.NativeCallConv(
                        Terrum.Interop.NativeCallingConvention.Unmanaged)]
                    fnptr<int32, int32> callback = &ProviderCallback;
                    if (callback(42) != 42) { return -3; }
                }
                return ProviderValue();
            }
            """), NativeModuleCompiler.CreateGraphReference([provider]),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions(
                (BoundRuntimePolicy)Policy));
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateGraphConsumer(consumerCompilation.Program!,
            [provider], Policy);

        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(consumer, [provider],
            CompilerApphost(), TimeSpan.FromSeconds(15), Policy).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public async Task GraphJitExecutesAddressOnlyProviderImportAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const NativeRuntimePolicy Policy = NativeRuntimePolicy.FailFast;
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""

            [Terrum.Interop.NativeCallable]
            public int32 ProviderCallback(int32 value) { return value; }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions(
                (BoundRuntimePolicy)Policy));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(providerCompilation.Program!,
            ["ProviderCallback"], "GraphJitAddressOnlyProvider", Policy);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    [Terrum.Interop.NativeCallConv(
                        Terrum.Interop.NativeCallingConvention.Unmanaged)]
                    fnptr<int32, int32> callback = &ProviderCallback;
                    return callback(42) == 42 ? 0 : -1;
                }
            }
            """), NativeModuleCompiler.CreateGraphReference([provider]),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions(
                (BoundRuntimePolicy)Policy));
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateGraphConsumer(consumerCompilation.Program!,
            [provider], Policy);

        NativeModuleCompiler.ValidateGraph(consumer, [provider]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(consumer, [provider],
            TimeSpan.FromSeconds(15), Policy).ConfigureAwait(true), Is.Zero);
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(consumer, [provider],
            CompilerApphost(), TimeSpan.FromSeconds(15), Policy).ConfigureAwait(true), Is.Zero);
    }

    [Test]
    public void Revision92RejectsNativeCallableCaptureDependencies()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [param(output): Captures(nameof(value))]
            [Terrum.Interop.NativeCallable]
            public int32 FailWithPointer(write ptr<ptr<void>> output, ptr<void> value) {
                unsafe { *output = value; }
                return -1;
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = compilation.Program!.Functions.Single(static candidate =>
            candidate.Name == "FailWithPointer");
        Assert.That(function.DependencyContracts.Any(static dependency =>
            dependency.Relation == BoundDependencyRelation.Captures), Is.True);

        var exception = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.CreateProducer(compilation.Program!,
                ["FailWithPointer"], "GraphJitFailedOutputProvider", NativeRuntimePolicy.FailFast));
        Assert.That(exception!.Message, Does.Contain("complete function-pointer invocation contracts"));
    }
}
