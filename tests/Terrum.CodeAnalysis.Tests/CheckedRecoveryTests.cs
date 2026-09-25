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
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class CheckedRecoveryTests
{
    [Theory]
    [InlineData("int32 Main() { try { require false; } catch { return 1; } return 0; }", BoundRuntimePolicy.CheckedExceptions, "TRM3021")]
    [InlineData("int32 Main() { unsafe { try { require false; } catch { return 1; } } return 0; }", BoundRuntimePolicy.FailFast, "TRM3022")]
    [InlineData("int32 Main() { unsafe { try { require false; } catch { return 1; } finally { require false; } } return 0; }", BoundRuntimePolicy.CheckedExceptions, "TRM3017")]
    [InlineData("bool Take(out int32 x) { unsafe { try { require false; x = 1; } catch { return x == 1; } } return false; } int32 Main() { return 0; }", BoundRuntimePolicy.CheckedExceptions, "TRM3016")]
    [InlineData("bool Take(out int32 x) { unsafe { try { x = 1; require false; } catch { Clear(out x); return x == 1; } } return false; } bool Clear(out int32 x) { return false; } int32 Main() { return 0; }", BoundRuntimePolicy.CheckedExceptions, "TRM3016")]
    [InlineData("bool Take(out int32 x) { unsafe { try { require Set(out x); require false; } catch { return x == 1; } } return false; } bool Set(out int32 x) { x = 1; return true; } int32 Main() { return 0; }", BoundRuntimePolicy.CheckedExceptions, "TRM3016")]
    [InlineData("int32 Main() { unsafe { try { require false; } catch { return 42; } } try { return 1; } finally { require false; } }", BoundRuntimePolicy.CheckedExceptions, "TRM3017")]
    [InlineData("bool Take(out int32 x, int32 divisor) { try { x = 10 / divisor; return true; } finally { Observe(x); } } [NoThrow] void Observe(int32 x) {} int32 Main() { return 0; }", BoundRuntimePolicy.CheckedExceptions, "TRM3016")]
    [InlineData("bool Take(out int32 x, int32 divisor) { unsafe { try { require false; } catch { x = 10 / divisor; return true; } finally { Observe(x); } } } [NoThrow] void Observe(int32 x) {} int32 Main() { return 0; }", BoundRuntimePolicy.CheckedExceptions, "TRM3016")]
    public void RecoveryRequiresPolicyAndFailureEdgeInitialization(string source, BoundRuntimePolicy policy, string diagnostic)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), policy);
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, item => item.Code == diagnostic);
    }

    [Fact]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var region = Assert.IsType<BoundUnsafe>(program.Functions[0].Body.Statements[0]);
        _ = Assert.IsType<BoundTryCatch>(region.Body.Statements[0]);
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void CheckedRegionsAndPolicySurviveSourceIndependentModuleLinking()
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Compute(int32 divisor) {
                unsafe {
                    try { return 12 / divisor; } catch { return 5; }
                }
            }
            """), BoundRuntimePolicy.CheckedExceptions);
        Assert.False(provider.HasErrors, string.Join(Environment.NewLine, provider.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(provider.Program), ["Compute"]);
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.Equal(BoundRuntimePolicy.CheckedExceptions, reference.RuntimePolicy);
        var mismatch = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Compute(3); }"), reference);
        Assert.Contains(mismatch.Diagnostics, item => item.Code == "TRM3022");
        var client = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Compute(3); }"),
            reference, BoundRuntimePolicy.CheckedExceptions);
        Assert.False(client.HasErrors, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(client.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Equal(BoundRuntimePolicy.CheckedExceptions, linked.RuntimePolicy);
        Assert.Contains(linked.Functions, function => function.Body.Statements[0] is BoundUnsafe {
            Body.Statements: [BoundTryCatch],
        });
    }

    [Theory]
    [InlineData("bool Take(out int32 x) { unsafe { try { x = 1; require false; } catch { return x == 1; } } return false; } int32 Main() { return 0; }")]
    [InlineData("bool Take(out int32 x) { unsafe { try { try { require false; } finally { x = 1; } } catch { return x == 1; } } return false; } int32 Main() { return 0; }")]
    public void FailureEdgesIncludeOnlyCompletedWritesAndUnwoundCleanup(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.CheckedExceptions);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Fact]
    public async Task PersistedCheckedSemanticModulesExecuteInBoundedWorkerAsync()
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Divide(int32 divisor) { return 42 / divisor; }
            """), BoundRuntimePolicy.CheckedExceptions);
        Assert.False(provider.HasErrors, string.Join(Environment.NewLine, provider.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(provider.Program), ["Divide"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var client = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe { try { return Divide(0); } catch { return 42; } }
            }
            """), reference, BoundRuntimePolicy.CheckedExceptions);
        Assert.False(client.HasErrors, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(client.Program), producer);
        var directory = Path.Combine(AppContext.BaseDirectory, $"checked-module-{Guid.NewGuid():N}");
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
            Assert.Equal(42, process.ExitCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task NativePackagesCatchFailuresAcrossAnImportedFunctionAsync()
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Divide(int32 divisor) { return 42 / divisor; }
            """), BoundRuntimePolicy.CheckedExceptions);
        Assert.False(provider.HasErrors, string.Join(Environment.NewLine, provider.Diagnostics));
        var producer = NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(provider.Program),
            ["Divide"], "Checked", NativeRuntimePolicy.CheckedExceptions);
        var reference = NativeModuleCompiler.CreateReference(producer);
        var client = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    try { return Divide(0); } catch { return 42; }
                }
            }
            """), reference, BoundRuntimePolicy.CheckedExceptions);
        Assert.False(client.HasErrors, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(client.Program),
            producer, NativeRuntimePolicy.CheckedExceptions);
        NativeModuleCompiler.Validate(consumer, producer);
        Assert.Equal(42, await NativeModuleCompiler.RunAsync(consumer, producer, TimeSpan.FromSeconds(10)));

        var replacement = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Divide(int32 divisor) {
                if (divisor == 0) { return 43; }
                return 42 / divisor;
            }
            """), BoundRuntimePolicy.CheckedExceptions);
        Assert.False(replacement.HasErrors, string.Join(Environment.NewLine, replacement.Diagnostics));
        var replacedProducer = NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(replacement.Program),
            ["Divide"], "Checked", NativeRuntimePolicy.CheckedExceptions);
        NativeModuleCompiler.Validate(consumer, replacedProducer);
        Assert.Equal(43, await NativeModuleCompiler.RunAsync(consumer, replacedProducer, TimeSpan.FromSeconds(10),
            NativeRuntimePolicy.CheckedExceptions));

        var failFastProvider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Divide(int32 divisor) { return 42 / divisor; }
            """));
        var failFast = NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(failFastProvider.Program),
            ["Divide"], "Checked", NativeRuntimePolicy.FailFast);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.Validate(consumer, failFast));
        _ = await Assert.ThrowsAsync<ModuleContractException>(() => NativeModuleCompiler.RunAsync(consumer, producer,
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData("body")]
    [InlineData("catch")]
    [InlineData("finally")]
    [InlineData("unsafe")]
    [InlineData("policy")]
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
        var valid = Assert.IsType<BoundProgram>(compilation.Program);
        var function = valid.Functions[valid.EntryPoint];
        var unsafeBlock = Assert.IsType<BoundUnsafe>(function.Body.Statements[0]);
        var recovery = Assert.IsType<BoundTryCatch>(unsafeBlock.Body.Statements[0]);
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
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(replaced));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(replaced, NativeRuntimePolicy.CheckedExceptions));
    }

    [Theory]
    [InlineData("marker", unchecked((int)0xE0540092))]
    [InlineData("version", unchecked((int)0xE0540092))]
    [InlineData("reason", unchecked((int)0xE0540092))]
    [InlineData("parameter-count", unchecked((int)0xE0540092))]
    [InlineData("continuable", unchecked((int)0xE0540092))]
    [InlineData("code", unchecked((int)0xE0550092))]
    public async Task ForeignOrMalformedFailureIdentityIsNotCaughtAsync(string variant, int expected)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe { try { return 1 / 0; } catch { return 42; } }
            }
            """), BoundRuntimePolicy.CheckedExceptions);
        var image = NativeEmitter.Emit(Assert.IsType<BoundProgram>(compilation.Program),
            NativeRuntimePolicy.CheckedExceptions);
        var marker = BitConverter.GetBytes(0x54455252554D0001L);
        var first = image.AsSpan().IndexOf(marker);
        Assert.True(first >= 0);
        var second = image.AsSpan((first + marker.Length)..).IndexOf(marker);
        Assert.True(second >= 0);
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
        var executable = Path.Combine(AppContext.BaseDirectory, $"checked-identity-{Guid.NewGuid():N}.exe");
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
            Assert.Equal(expected, process.ExitCode);
        }
        finally
        {
            File.Delete(executable);
        }

        static int Find(byte[] bytes, byte[] needle, int start)
        {
            var relative = bytes.AsSpan(start..).IndexOf(needle);
            Assert.True(relative >= 0);
            return start + relative;
        }
    }

    [Theory]
    [InlineData("scope-count")]
    [InlineData("scope-range")]
    [InlineData("cleanup-target")]
    public void TamperedCheckedNativeScopesAreRejected(string variant)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
                public int32 Divide(int32 divisor) {
                    unsafe { try { return 42 / divisor; } catch { return 42; } }
                }
                """), BoundRuntimePolicy.CheckedExceptions);
        var original = NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program),
            ["Divide"], "Scoped", NativeRuntimePolicy.CheckedExceptions);
        var changed = (byte[])original.Clone();
        var peImage = 18 + changed[7] + changed[8] + BinaryPrimitives.ReadInt32LittleEndian(changed.AsSpan(10, 4));
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
        Assert.True(xdata > 0);
        var source = xdata + (4 * 8);
        Assert.Equal((byte)0x19, changed[source]);
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
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(changed));
    }
}
