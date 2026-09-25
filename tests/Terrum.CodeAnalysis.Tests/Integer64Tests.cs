// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class Integer64Tests
{
    [Theory]
    [InlineData("return 9223372036854775807i64 == int64.MaxValue && -9223372036854775808i64 == int64.MinValue;", true)]
    [InlineData("return 18446744073709551615u64 == uint64.MaxValue && 0u64 == uint64.MinValue;", true)]
    [InlineData("return 9223372036854775807i64 > -1i64 && 18446744073709551615u64 > 9223372036854775807u64;", true)]
    [InlineData("return (9223372036854775806i64 + 1i64) == int64.MaxValue;", true)]
    [InlineData("return (uint64.MaxValue - 1u64) == 18446744073709551614u64;", true)]
    [InlineData("return (-3000000000i64 * 3i64) / 3i64 == -3000000000i64;", true)]
    [InlineData("return (uint64.MaxValue / 3u64) * 3u64 == uint64.MaxValue;", true)]
    [InlineData("return -9223372036854775808i64 % -1i64 == 0i64;", true)]
    [InlineData("return -11i64 / 3i64 == -3i64 && -11i64 % 3i64 == -2i64;", true)]
    [InlineData("return (~0u64 ^ uint64.MaxValue) == 0u64 && (1u64 | 2u64) == 3u64;", true)]
    [InlineData("return (int64)2147483648u32 == 2147483648i64;", true)]
    [InlineData("return (int64)-2147483648i32 == -2147483648i64;", true)]
    [InlineData("return (uint64)uint32.MaxValue == 4294967295u64;", true)]
    [InlineData("return (int32)2147483647i64 == int32.MaxValue;", true)]
    [InlineData("return (uint32)4294967295u64 == uint32.MaxValue;", true)]
    [InlineData("return (char16)65535u64 == char16.MaxValue;", true)]
    public async Task WideValuesAgreeAcrossAotAndCliJitAsync(string body, bool expected)
    {
        var source = "int32 Main() { if (Check()) { return 42; } return 1; } bool Check() { " + body + " }";
        await ExecuteBothAsync(Compile(source), expected ? 42 : 1).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("int32 Main() { int64 x = int64.MaxValue + 1i64; return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { uint64 x = uint64.MaxValue + 1u64; return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { int64 x = int64.MinValue - 1i64; return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { uint64 x = 0u64 - 1u64; return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { int64 x = 3037000500i64 * 3037000500i64; return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { uint64 x = uint64.MaxValue * 2u64; return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { int64 x = int64.MinValue / -1i64; return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { uint64 x = 1u64 / 0u64; return 0; }", unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { int64 x = -int64.MinValue; return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { int32 x = (int32)2147483648i64; return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { int64 x = (int64)9223372036854775808u64; return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { uint64 x = (uint64)-1i64; return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { uint32 x = (uint32)4294967296u64; return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { char16 x = (char16)65536u64; return 0; }", unchecked((int)0xE0540001))]
    public async Task WideFailuresUseCheckedRuntimePolicyAsync(string source, int expected)
    {
        await ExecuteBothAsync(Compile(source), expected).ConfigureAwait(true);
    }

    [Fact]
    public async Task WideArgumentsNestedFieldsOutputsAndRecursionPreserveBitsAsync()
    {
        var program = Compile("""
            struct Inner {
                public mut int64 Value;
                public Inner(int64 value) { Value = value; }
            }
            struct Pair {
                public mut Inner Signed;
                public uint64 Unsigned;
                public Pair(int64 signed, uint64 unsigned) {
                    Signed = new Inner(signed); Unsigned = unsigned;
                }
            }
            int64 Sum(int64 a, int64 b, int64 c, int64 d, int64 e, int64 f) {
                return a + b + c + d + e + f;
            }
            int64 Recurse(int64 n) {
                if (n == 0i64) { return 32i64; }
                return Recurse(n - 1i64);
            }
            bool Try(bool success, out Pair value) {
                if (!success) { return false; }
                value = new Pair(1i64, uint64.MaxValue);
                value.Signed.Value = 10i64;
                return true;
            }
            bool Forward(bool success, out Pair value) { return Try(success, out value); }
            int32 Main() {
                require Forward(true, out var value);
                if (Forward(false, out var ignored)) { return 1; }
                Pair copy = value;
                mut Pair replacement = value;
                replacement.Signed.Value = 2i64;
                if (copy.Signed.Value != 10i64 || replacement.Signed.Value != 2i64
                    || value.Unsigned != uint64.MaxValue) { return 2; }
                if (Sum(1i64, 2i64, 3i64, 4i64, 0i64, Recurse(3i64)) != 42i64) { return 3; }
                return 42;
            }
            """);
        Assert.Equal(16, program.Structures[1].Size);
        await ExecuteBothAsync(program, 42).ConfigureAwait(true);
    }

    [Fact]
    public async Task ScalarWideOutputForwardingPublishesOnlyOnSuccessAsync()
    {
        await ExecuteBothAsync(Compile("""
            bool Try(bool success, out int64 signed, out uint64 unsigned) {
                if (!success) { return false; }
                signed = int64.MinValue;
                unsigned = uint64.MaxValue;
                return true;
            }
            bool Forward(bool success, out int64 signed, out uint64 unsigned) {
                return Try(success, out signed, out unsigned);
            }
            int32 Main() {
                mut int64 signed = 0i64;
                mut uint64 unsigned = 0u64;
                require Forward(true, out signed, out unsigned);
                if (signed != int64.MinValue || unsigned != uint64.MaxValue) { return 2; }
                if (Forward(false, out signed, out unsigned)) { return 1; }
                require Forward(true, out signed, out unsigned);
                return 42;
            }
            """), 42).ConfigureAwait(true);
    }

    [Fact]
    public async Task PublishedWideIntegerSampleExecutesAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var source = await File.ReadAllTextAsync(Path.Combine(root, "samples", "native", "integers64.terrum")).ConfigureAwait(true);
        await ExecuteBothAsync(Compile(source), 42).ConfigureAwait(true);
    }

    [Fact]
    public async Task WideLiteralPayloadsAndFieldContractsSurviveModuleLinkingAsync()
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public struct Wide {
                public int64 Signed;
                public uint64 Unsigned;
                public Wide() { Signed = int64.MinValue; Unsigned = uint64.MaxValue; }
            }
            public Wide Make() { return new Wide(); }
            """));
        var producer = ModuleCompiler.CreateProducer(CompileLibrary(library), ["Make"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumer = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                Wide value = Make();
                if (value.Signed != -9223372036854775808i64
                    || value.Unsigned != 18446744073709551615u64) { return 1; }
                return 42;
            }
            """), reference);
        Assert.False(consumer.HasErrors, string.Join(Environment.NewLine, consumer.Diagnostics));
        var artifact = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumer.Program), producer);
        await ExecuteBothAsync(ModuleCompiler.Link(artifact, producer), 42).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("int64 Main() { return 0i64; }")]
    [InlineData("int32 Main() { int64 value = 1i32; return 0; }")]
    [InlineData("int32 Main() { uint64 value = -1i64; return 0; }")]
    [InlineData("int32 Main() { int64 value = 9223372036854775808i64; return 0; }")]
    [InlineData("int32 Main() { uint64 value = 18446744073709551616u64; return 0; }")]
    [InlineData("int32 Main() { int64 value = 18446744073709551615; return 0; }")]
    [InlineData("int32 Main() { bool value = (bool)1i64; return 0; }")]
    [InlineData("int32 Main() { int64 value = 1f64; return 0; }")]
    public void WideInvalidSourcesAreDiagnosed(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void ForgedNarrowLiteralPayloadIsRejectedBeforeEmission()
    {
        var program = Compile("int32 Main() { return 42; }");
        var function = program.Functions[0];
        var result = Assert.IsType<BoundReturn>(function.Body.Statements[0]);
        BoundExpression[] expressions = [
            new BoundConstant((long)int.MaxValue + 1, PrimitiveType.Int32, result.Span),
            new BoundCast(new BoundConstant(1, PrimitiveType.Bool, result.Span), PrimitiveType.Int32, result.Span),
        ];
        foreach (var expression in expressions)
        {
            var forged = function with {
                Body = function.Body with {
                    Statements = function.Body.Statements.SetItem(0, result with { Value = expression }),
                },
            };
            _ = Assert.Throws<ArgumentException>(() =>
                NativeEmitter.EmitCode(program with { Functions = program.Functions.SetItem(0, forged) },
                    NativeRuntimePolicy.FailFast));
        }
    }

    [Fact]
    public void UnsignedLiteralPayloadsRetainTheirExactBits()
    {
        var program = Compile("""
            int32 Main() {
                uint64 large = 18446744073709551615u64;
                int64 negative = -9223372036854775808i64;
                uint32 previous = 4294967295u32;
                return 0;
            }
            """);
        var statements = program.Functions[0].Body.Statements;
        Assert.Equal(-1L, Assert.IsType<BoundConstant>(Assert.IsType<BoundLocal>(statements[0]).Initializer).Value);
        Assert.Equal(long.MinValue, Assert.IsType<BoundConstant>(Assert.IsType<BoundLocal>(statements[1]).Initializer).Value);
        Assert.Equal(-1L, Assert.IsType<BoundConstant>(Assert.IsType<BoundLocal>(statements[2]).Initializer).Value);
    }

    [Theory]
    [InlineData(PrimitiveType.Int64)]
    [InlineData(PrimitiveType.UInt64)]
    public void WideHostBufferElementsRemainOutsideTheFourByteHostBufferContract(PrimitiveType elementType)
    {
        var program = Compile("int32 Main() { return 0; }");
        var malformed = program with {
            Buffers = [new HostBuffer("wide", elementType, [0], IsWritable: true)],
        };
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(malformed, NativeRuntimePolicy.FailFast));
    }

    private static BoundProgram Compile(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    private static async Task ExecuteBothAsync(BoundProgram program, int expected)
    {
        var prefix = Path.Combine(AppContext.BaseDirectory, $"integer64-{Guid.NewGuid():N}");
        var executable = prefix + ".exe";
        var consumer = prefix + ".consumer.trm";
        var producer = prefix + ".producer.trm";
        try
        {
            await File.WriteAllBytesAsync(executable, NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast)).ConfigureAwait(true);
            await RunAsync(executable, [], expected).ConfigureAwait(true);
            var unused = Compilation.CreateLibrary(SyntaxTree.Parse("public int32 Unused() { return 0; }"));
            var producerBytes = ModuleCompiler.CreateProducer(CompileLibrary(unused), ["Unused"]);
            await File.WriteAllBytesAsync(producer, producerBytes).ConfigureAwait(true);
            await File.WriteAllBytesAsync(consumer, ModuleCompiler.CreateConsumer(program, producerBytes)).ConfigureAwait(true);
            var framework = new DirectoryInfo(AppContext.BaseDirectory);
            var configuration = framework.Parent?.Name ?? throw new InvalidOperationException("Missing configuration.");
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
            var cli = Path.Combine(root, "artifacts", "bin", "sources", "terrumc", configuration, framework.Name, "terrumc.dll");
            await RunAsync("dotnet", [cli, "run-module", consumer, producer, "--runtime", "fail-fast"], expected).ConfigureAwait(true);
        }
        finally
        {
            File.Delete(executable);
            File.Delete(consumer);
            File.Delete(producer);
        }
    }

    private static BoundProgram CompileLibrary(Compilation compilation)
    {
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    private static async Task RunAsync(string executable, ImmutableArray<string> arguments, int expected)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start native worker.");
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().ConfigureAwait(true);
            throw;
        }
        Assert.Equal(expected, process.ExitCode);
        Assert.Empty(await error.ConfigureAwait(true));
    }
}
