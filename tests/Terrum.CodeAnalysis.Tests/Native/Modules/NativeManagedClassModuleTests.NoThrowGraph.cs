using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Tests.Native.Modules;

internal sealed partial class NativeManagedClassModuleTests
{
    [Test]
    public async Task CheckedGraphFailuresCanUnwindIntoGeneratedCallersAsync()
    {
        const NativeRuntimePolicy policy = NativeRuntimePolicy.CheckedExceptions;
        var leaf = NativeModuleCompiler.CreateProducer(
            Compile("public int32 Succeed() { return 0; }", policy),
            ["Succeed"], "SuccessProvider", policy);
        var provider = NativeModuleCompiler.CreateGraphProducer(Compile(
            "public int32 Fail() { Succeed(); require false; return 0; }", policy,
            NativeModuleCompiler.CreateGraphReference([leaf])),
            ["Fail"], "FailureProvider", [leaf], policy);
        var consumer = NativeModuleCompiler.CreateGraphConsumer(Compile("""
            int32 Main() {
                unsafe {
                    try { return Fail(); }
                    catch { return 42; }
                }
            }
            """, policy, NativeModuleCompiler.CreateGraphReference([provider]), library: false),
            [provider], policy);

        Assert.That(await NativeModuleCompiler.RunGraphAsync(consumer, [provider, leaf],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public async Task CheckedNativeModuleFailuresCanUnwindIntoGeneratedCallersAsync()
    {
        const NativeRuntimePolicy policy = NativeRuntimePolicy.CheckedExceptions;
        var provider = NativeModuleCompiler.CreateProducer(
            Compile("public int32 Fail() { require false; return 0; }", policy),
            ["Fail"], "FailureProvider", policy);
        var consumer = NativeModuleCompiler.CreateConsumer(Compile("""
            int32 Main() {
                unsafe {
                    try { return Fail(); }
                    catch { return 42; }
                }
            }
            """, policy, NativeModuleCompiler.CreateReference(provider), library: false),
            provider, policy);

        Assert.That(await NativeModuleCompiler.RunAsync(consumer, provider,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true), Is.EqualTo(42));
    }
}
