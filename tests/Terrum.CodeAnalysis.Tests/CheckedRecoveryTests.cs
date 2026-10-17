// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class CheckedRecoveryTests
{
    [TestCase("int32 Main() { try { require false; } catch { return 1; } return 0; }",BoundRuntimePolicy.CheckedExceptions,"TRM3021")]
    [TestCase("int32 Main() { unsafe { try { require false; } catch { return 1; } } return 0; }",BoundRuntimePolicy.FailFast,"TRM3022")]
    [TestCase("int32 Main() { unsafe { try { require false; } catch { return 1; } finally { require false; } } return 0; }",BoundRuntimePolicy.CheckedExceptions,"TRM3017")]
    [TestCase("bool Take(out int32 x) { unsafe { try { require false; x = 1; } catch { return x == 1; } } return false; } int32 Main() { return 0; }",BoundRuntimePolicy.CheckedExceptions,"TRM3016")]
    [TestCase("bool Take(out int32 x) { unsafe { try { x = 1; require false; } catch { Clear(out x); return x == 1; } } return false; } bool Clear(out int32 x) { return false; } int32 Main() { return 0; }",BoundRuntimePolicy.CheckedExceptions,"TRM3016")]
    [TestCase("bool Take(out int32 x) { unsafe { try { require Set(out x); require false; } catch { return x == 1; } } return false; } bool Set(out int32 x) { x = 1; return true; } int32 Main() { return 0; }",BoundRuntimePolicy.CheckedExceptions,"TRM3016")]
    [TestCase("""
        [NoThrow] void Fill(out int32 value, int32 ignored) { value = 42; }
        int32 Fail() { require false; return 0; }
        int32 Main() {
            mut int32 value = 7;
            unsafe { try { Fill(out value, Fail()); } catch { return value; } }
            return 0;
        }
        """,BoundRuntimePolicy.CheckedExceptions,"TRM3016")]
    [TestCase("""
        [NoThrow] void Fill(int32 ignored, out int32 value) { value = 42; }
        int32 Fail() { require false; return 0; }
        int32 Main() {
            mut int32 value = 7;
            unsafe { try { Fill(value: out value, ignored: Fail()); } catch { return value; } }
            return 0;
        }
        """,BoundRuntimePolicy.CheckedExceptions,"TRM3016")]
    [TestCase("int32 Main() { unsafe { try { require false; } catch { return 42; } } try { return 1; } finally { require false; } }",BoundRuntimePolicy.CheckedExceptions,"TRM3017")]
    [TestCase("bool Take(out int32 x, int32 divisor) { try { x = 10 / divisor; return true; } finally { Observe(x); } } [NoThrow] void Observe(int32 x) {} int32 Main() { return 0; }",BoundRuntimePolicy.CheckedExceptions,"TRM3016")]
    [TestCase("bool Take(out int32 x, int32 divisor) { unsafe { try { require false; } catch { x = 10 / divisor; return true; } finally { Observe(x); } } } [NoThrow] void Observe(int32 x) {} int32 Main() { return 0; }",BoundRuntimePolicy.CheckedExceptions,"TRM3016")]
    [TestCase("""
        struct Pair { public int32 Value; public Pair(int32 value) { Value = value; } }
        int32 Fail(out Pair result) { result = new Pair(42); require false; return 0; }
        int32 Main() {
            mut Pair result = new Pair(7);
            unsafe { try { Fail(out result); } catch { return result.Value; } }
            return 0;
        }
        """,BoundRuntimePolicy.CheckedExceptions,"TRM3016")]
    public void RecoveryRequiresPolicyAndFailureEdgeInitialization(string source, BoundRuntimePolicy policy, string diagnostic)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), policy);
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,item => item.Code == diagnostic),Is.True);
    }

    [Test]
    public void ValidRecoveryRetainsStructuredIrButCannotBeEmittedAsFailFast()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    try { require false; } catch { return 42; }
                }
                return 0;
            }
            """), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        var region = ((global::System.Func<BoundUnsafe>)(() => { var __nunitActual1 = program.Functions[0].Body.Statements[0]; Assert.That(__nunitActual1, Is.TypeOf<BoundUnsafe>()); return (BoundUnsafe)__nunitActual1!; }))();
        _ = ((global::System.Func<BoundTryCatch>)(() => { var __nunitActual2 = region.Body.Statements[0]; Assert.That(__nunitActual2, Is.TypeOf<BoundTryCatch>()); return (BoundTryCatch)__nunitActual2!; }))();
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast))!;
    }

    [Test]
    public void CheckedRegionsAndPolicySurviveSourceIndependentModuleLinking()
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Compute(int32 divisor) {
                unsafe {
                    try { return 12 / divisor; } catch { return 5; }
                }
            }
            """), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(provider.HasErrors, Is.False, string.Join(Environment.NewLine, provider.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = provider.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))(), ["Compute"]);
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.That(reference.RuntimePolicy, Is.EqualTo(BoundRuntimePolicy.CheckedExceptions));
        var mismatch = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Compute(3); }"), reference);
        Assert.That(global::System.Linq.Enumerable.Any(mismatch.Diagnostics,item => item.Code == "TRM3022"),Is.True);
        var client = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Compute(3); }"),
            reference, BoundRuntimePolicy.CheckedExceptions);
        Assert.That(client.HasErrors, Is.False, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = client.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(linked.RuntimePolicy, Is.EqualTo(BoundRuntimePolicy.CheckedExceptions));
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,function => function.Body.Statements[0] is BoundUnsafe {
            Body.Statements: [BoundTryCatch],
        }),Is.True);
    }
    [TestCase("bool Take(out int32 x) { unsafe { try { x = 1; require false; } catch { return x == 1; } } return false; } int32 Main() { return 0; }")]
    [TestCase("bool Take(out int32 x) { unsafe { try { try { require false; } finally { x = 1; } } catch { return x == 1; } } return false; } int32 Main() { return 0; }")]
    public void FailureEdgesIncludeOnlyCompletedWritesAndUnwoundCleanup(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public async Task PersistedCheckedSemanticModulesExecuteInBoundedWorkerAsync()
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Divide(int32 divisor) { return 42 / divisor; }
            """), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(provider.HasErrors, Is.False, string.Join(Environment.NewLine, provider.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = provider.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))(), ["Divide"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var client = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe { try { return Divide(0); } catch { return 42; } }
            }
            """), reference, BoundRuntimePolicy.CheckedExceptions);
        Assert.That(client.HasErrors, Is.False, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = client.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))(), producer);
        var directory = Path.Combine(TestArtifactPaths.DirectoryPath, $"checked-module-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(directory);
        try
        {
            var consumerPath = Path.Combine(directory, "consumer.trm");
            var producerPath = Path.Combine(directory, "producer.trm");
            await File.WriteAllBytesAsync(consumerPath, consumer);
            await File.WriteAllBytesAsync(producerPath, producer);

            var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
            var configuration = frameworkDirectory.Parent?.Name
                ?? throw new InvalidOperationException("Missing configuration directory.");
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
            var cli = Path.Combine(root, "artifacts", "bin", "sources", "terrumc", configuration,
                frameworkDirectory.Name, "terrumc.dll");
            var start = new ProcessStartInfo("dotnet") {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in new[] {
                cli, "run-module", consumerPath, producerPath, "--runtime", "checked-exceptions",
            })
            {
                start.ArgumentList.Add(argument);
            }
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start CLI.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            }
            catch (TimeoutException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw;
            }
            Assert.That(process.ExitCode, Is.EqualTo(42));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task NativePackagesCatchFailuresAcrossAnImportedFunctionAsync()
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Divide(int32 divisor) { return 42 / divisor; }
            """), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(provider.HasErrors, Is.False, string.Join(Environment.NewLine, provider.Diagnostics));
        var producer = NativeModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = provider.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))(),
            ["Divide"], "Checked", NativeRuntimePolicy.CheckedExceptions);
        var reference = NativeModuleCompiler.CreateReference(producer);
        var client = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    try { return Divide(0); } catch { return 42; }
                }
            }
            """), reference, BoundRuntimePolicy.CheckedExceptions);
        Assert.That(client.HasErrors, Is.False, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = client.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))(),
            producer, NativeRuntimePolicy.CheckedExceptions);
        NativeModuleCompiler.Validate(consumer, producer);
        Assert.That(await NativeModuleCompiler.RunAsync(consumer, producer, TimeSpan.FromSeconds(10)), Is.EqualTo(42));

        var replacement = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Divide(int32 divisor) {
                if (divisor == 0) { return 43; }
                return 42 / divisor;
            }
            """), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(replacement.HasErrors, Is.False, string.Join(Environment.NewLine, replacement.Diagnostics));
        var replacedProducer = NativeModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = replacement.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))(),
            ["Divide"], "Checked", NativeRuntimePolicy.CheckedExceptions);
        NativeModuleCompiler.Validate(consumer, replacedProducer);
        Assert.That(await NativeModuleCompiler.RunAsync(consumer, replacedProducer, TimeSpan.FromSeconds(10),
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(43));

        var failFastProvider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Divide(int32 divisor) { return 42 / divisor; }
            """));
        var failFast = NativeModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = failFastProvider.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))(),
            ["Divide"], "Checked", NativeRuntimePolicy.FailFast);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.Validate(consumer, failFast))!;
        _ = (await Assert.ThrowsAsync<ModuleContractException>(() => NativeModuleCompiler.RunAsync(consumer, producer,
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast)))!;
    }
    [TestCase("body")]
    [TestCase("catch")]
    [TestCase("finally")]
    [TestCase("unsafe")]
    [TestCase("policy")]
    public void MalformedPublicRecoveryIrIsRejectedBeforeEmission(string variant)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    try { require false; } catch { return 42; }
                }
                return 0;
            }
            """), BoundRuntimePolicy.CheckedExceptions);
        var valid = ((global::System.Func<BoundProgram>)(() => { var __nunitActual11 = compilation.Program; Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual11!; }))();
        var function = valid.Functions[valid.EntryPoint];
        var unsafeBlock = ((global::System.Func<BoundUnsafe>)(() => { var __nunitActual12 = function.Body.Statements[0]; Assert.That(__nunitActual12, Is.TypeOf<BoundUnsafe>()); return (BoundUnsafe)__nunitActual12!; }))();
        var recovery = ((global::System.Func<BoundTryCatch>)(() => { var __nunitActual13 = unsafeBlock.Body.Statements[0]; Assert.That(__nunitActual13, Is.TypeOf<BoundTryCatch>()); return (BoundTryCatch)__nunitActual13!; }))();
        var broken = variant switch {
            "body" => recovery with { Body = null! },
            "catch" => recovery with { Catch = null! },
            "finally" => recovery with {
                Finally = new BoundBlock([
                new BoundRequire(new BoundConstant(0, PrimitiveType.Bool, default), default),
            ], default)
            },
            _ => recovery,
        };
        var replaced = valid with {
            Functions = valid.Functions.SetItem(valid.EntryPoint, function with {
                Body = function.Body with {
                    Statements = variant == "unsafe" ? [broken] : [
                        unsafeBlock with { Body = unsafeBlock.Body with { Statements = [broken] } },
                    ],
                },
            }),
            RuntimePolicy = variant == "policy" ? (BoundRuntimePolicy)127 : valid.RuntimePolicy,
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(replaced))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(replaced, NativeRuntimePolicy.CheckedExceptions))!;
    }
    [TestCase("marker",unchecked((int)0xE0540092))]
    [TestCase("version",unchecked((int)0xE0540092))]
    [TestCase("reason",unchecked((int)0xE0540092))]
    [TestCase("parameter-count",unchecked((int)0xE0540092))]
    [TestCase("continuable",unchecked((int)0xE0540092))]
    [TestCase("code",unchecked((int)0xE0550092))]
    public async Task ForeignOrMalformedFailureIdentityIsNotCaughtAsync(string variant, int expected)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe { try { return 1 / 0; } catch { return 42; } }
            }
            """), BoundRuntimePolicy.CheckedExceptions);
        var image = NativeEmitter.Emit(((global::System.Func<BoundProgram>)(() => { var __nunitActual14 = compilation.Program; Assert.That(__nunitActual14, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual14!; }))(),
            NativeRuntimePolicy.CheckedExceptions);
        var marker = BitConverter.GetBytes(0x54455252554D0001L);
        var first = image.AsSpan().IndexOf(marker);
        Assert.That(first >= 0, Is.True);
        var second = image.AsSpan((first + marker.Length)..).IndexOf(marker);
        Assert.That(second >= 0, Is.True);
        var helper = first + marker.Length + second;
        var offset = variant switch {
            "marker" => helper,
            "version" => Find(image, [0x48, 0xC7, 0x44, 0x24, 0x28, 0x01, 0x00, 0x00, 0x00], helper) + 5,
            "reason" => Find(image, [0x48, 0xC7, 0x44, 0x24, 0x30, 0x02, 0x00, 0x00, 0x00], helper) + 5,
            "parameter-count" => Find(image, [0x41, 0xB8, 0x03, 0x00, 0x00, 0x00], helper) + 2,
            "continuable" => Find(image, [0xBA, 0x01, 0x00, 0x00, 0x00], helper) + 1,
            "code" => Find(image, [0xB9, 0x92, 0x00, 0x54, 0xE0], helper) + 3,
            _ => throw new InvalidOperationException("Unknown exception record mutation."),
        };
        image[offset] = variant switch {
            "version" => 2,
            "reason" => 5,
            "parameter-count" => 2,
            "continuable" => 0,
            "code" => 0x55,
            _ => (byte)(image[offset] ^ 1),
        };
        var executable = Path.Combine(TestArtifactPaths.DirectoryPath, $"checked-identity-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(executable, image);
            using var process = Process.Start(new ProcessStartInfo(executable) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start native worker.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw;
            }
            Assert.That(process.ExitCode, Is.EqualTo(expected));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }

        static int Find(byte[] bytes, byte[] needle, int start)
        {
            var relative = bytes.AsSpan(start..).IndexOf(needle);
            Assert.That(relative >= 0, Is.True);
            return start + relative;
        }
    }
    [TestCase("scope-count")]
    [TestCase("scope-range")]
    [TestCase("cleanup-target")]
    public void TamperedCheckedNativeScopesAreRejected(string variant)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
                public int32 Divide(int32 divisor) {
                    unsafe { try { return 42 / divisor; } catch { return 42; } }
                }
                """), BoundRuntimePolicy.CheckedExceptions);
        var original = NativeModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual15 = compilation.Program; Assert.That(__nunitActual15, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual15!; }))(),
            ["Divide"], "Scoped", NativeRuntimePolicy.CheckedExceptions);
        var changed = (byte[])original.Clone();
        var peImage = 18 + changed[7] + changed[8] + 2
            + BinaryPrimitives.ReadInt32LittleEndian(changed.AsSpan(10, 4));
        var pe = BinaryPrimitives.ReadInt32LittleEndian(changed.AsSpan(peImage + 0x3C, 4));
        var xdata = -1;
        for (var index = 0; index < 5; index++)
        {
            var section = peImage + pe + 24 + 0xF0 + (index * 40);
            if (changed.AsSpan(section, 6).SequenceEqual(".xdata"u8))
            {
                xdata = peImage + BinaryPrimitives.ReadInt32LittleEndian(changed.AsSpan(section + 20, 4));
                break;
            }
        }
        Assert.That(xdata > 0, Is.True);
        var source = xdata + (4 * 8);
        Assert.That(changed[source], Is.EqualTo((byte)0x19));
        var count = source + 12 + 4;
        var scope = source + 12 + 8;
        switch (variant)
        {
            case "scope-count":
                BinaryPrimitives.WriteInt32LittleEndian(changed.AsSpan(count, 4), 100_001);
                break;
            case "scope-range":
                BinaryPrimitives.WriteInt32LittleEndian(changed.AsSpan(scope, 4), int.MaxValue);
                break;
            case "cleanup-target":
                BinaryPrimitives.WriteInt32LittleEndian(changed.AsSpan(scope + 12, 4), 0);
                break;
            default:
                throw new InvalidOperationException("Unknown scope mutation.");
        }
        SHA256.HashData(changed.AsSpan(0, changed.Length - 32)).CopyTo(changed, changed.Length - 32);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(changed))!;
    }
}
