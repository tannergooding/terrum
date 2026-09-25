// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeTests
{
    [Theory]
    [InlineData("require true; return 7;", 7)]
    [InlineData("require false; return 7;", unchecked((int)0xE0540003))]
    [InlineData("require (1i32 < 2); return 8;", 8)]
    [InlineData("require true || (1i32 / 0 == 0); return 9;", 9)]
    [InlineData("require false && (1i32 / 0 == 0); return 9;", unchecked((int)0xE0540003))]
    [InlineData("require 1i32 / 0 == 0; return 9;", unchecked((int)0xE0540002))]
    public async Task ExecutesRequirementsWithExplicitFailurePolicyAsync(string body, int expected)
    {
        Assert.Equal(expected, await ExecuteAsync(Emit("int32 Main() { " + body + " }")).ConfigureAwait(true));
    }

    [Theory]
    [InlineData("int32 Main() { return 42; }", 42)]
    [InlineData("void Main() { }", 0)]
    [InlineData("int32 Main() { return -2147483648; }", int.MinValue)]
    [InlineData("int32 Main() { return 2147483647; }", int.MaxValue)]
    [InlineData("int32 Main() { return 7 + 3 * 5 - 4; }", 18)]
    [InlineData("int32 Main() { return -7 / 3; }", -2)]
    [InlineData("int32 Main() { return -7 % 3; }", -1)]
    [InlineData("int32 Main() { return -2147483648 % -1; }", 0)]
    [InlineData("int32 Main() { return ~(3 | 8) ^ (6 & 7); }", -14)]
    [InlineData("int32 Main() { mut int32 n = 0; mut int32 sum = 0; while (n < 10) { sum = sum + n; n = n + 1; } return sum; }", 45)]
    [InlineData("int32 Main() { return Factorial(6); } int32 Factorial(int32 n) { if (n <= 1) { return 1; } return n * Factorial(n - 1); }", 720)]
    [InlineData("int32 Main() { return Fib(10); } int32 Fib(int32 n) { if (n < 2) { return n; } return Fib(n - 1) + Fib(n - 2); }", 55)]
    [InlineData("int32 Main() { return Mix(1, 2, 3, 4, 5, 6); } int32 Mix(int32 a, int32 b, int32 c, int32 d, int32 e, int32 f) { return a + b * 2 + c * 3 + d * 4 + e * 5 + f * 6; }", 91)]
    [InlineData("int32 Main() { return Mix(Id(1), Id(2), Id(3), Id(4), Id(5), Mix(1,2,3,4,5,6)); } int32 Id(int32 x) { return x; } int32 Mix(int32 a, int32 b, int32 c, int32 d, int32 e, int32 f) { return a+b+c+d+e+f; }", 36)]
    [InlineData("int32 Main() { if (false && Fail()) { return 1; } if (true || Fail()) { return 8; } return 2; } bool Fail() { int32 value = 1 / 0; return value == 0; }", 8)]
    [InlineData("int32 Main() { bool a = true; bool b = false; if ((a & !b) ^ b) { return 11; } return 2; }", 11)]
    [InlineData("int32 Main() { if (-2147483648i32 < 2147483647 && 2147483647i32 >= -2147483648) { return 9; } return 2; }", 9)]
    [InlineData("int32 Main() { NoResult(); return 6; } void NoResult() { return (); }", 6)]
    [InlineData("void Main() { return Fail(); } void Fail() { int32 value = 1 / 0; }", unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { mut int32 n = 0; while (n < 4) { int32 value = n * 2; n = n + 1; if (value == 6) { return value; } } return 0; }", 6)]
    [InlineData("int32 Main() { if (Equal(2, 2)) { return 1; } else { return 0; } } bool Equal(int32 a, int32 b) { return a == b; }", 1)]
    [InlineData("int32 Main() { mut int32 n = 0; while (n < 3) { require Valid(n); n = n + 1; } return n; } bool Valid(int32 n) { return n >= 0; }", 3)]
    public async Task ExecutesNativeFunctionsAndStructuredControlFlowAsync(string source, int expected)
    {
        Assert.Equal(expected, await ExecuteAsync(Emit(source)));
    }

    [Theory]
    [InlineData("2147483647 + 1", unchecked((int)0xE0540001))]
    [InlineData("-2147483648 - 1", unchecked((int)0xE0540001))]
    [InlineData("50000 * 50000", unchecked((int)0xE0540001))]
    [InlineData("-(-2147483648)", unchecked((int)0xE0540001))]
    [InlineData("-2147483648 / -1", unchecked((int)0xE0540001))]
    [InlineData("1 / 0", unchecked((int)0xE0540002))]
    [InlineData("1 % 0", unchecked((int)0xE0540002))]
    [InlineData("(1 / 0) + (2147483647 + 1)", unchecked((int)0xE0540002))]
    [InlineData("(2147483647 + 1) + (1 / 0)", unchecked((int)0xE0540001))]
    public async Task RuntimeFailuresHaveExplicitProfileExitCodesAsync(string expression, int expected)
    {
        Assert.Equal(expected, await ExecuteAsync(Emit("int32 Main() { return " + expression + "; }")));
    }

    [Fact]
    public async Task ArgumentsEvaluateOnceInSourceOrderAsync()
    {
        const string Source = "int32 Main() { return F(1 / 0, 2147483647 + 1); } int32 F(int32 a, int32 b) { return a + b; }";
        Assert.Equal(unchecked((int)0xE0540002), await ExecuteAsync(Emit(Source)));
    }

    [Theory]
    [InlineData("factorial.terrum", 720)]
    [InlineData("control-flow.terrum", 45)]
    [InlineData("stack-arguments.terrum", 91)]
    [InlineData("requirements.terrum", 10)]
    [InlineData("for-loops.terrum", 285)]
    [InlineData("no-throw.terrum", 42)]
    public async Task PublishedNativeExamplesExecuteAsync(string name, int expected)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var source = await File.ReadAllTextAsync(Path.Combine(root, "samples", "native", name)).ConfigureAwait(true);
        Assert.Equal(expected, await ExecuteAsync(Emit(source)).ConfigureAwait(true));
    }

    [Fact]
    public async Task SelfHostAdditionPredicateAgreesWithWidenedBoundaryArithmeticAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var source = new StringBuilder(await File.ReadAllTextAsync(Path.Combine(root, "self-host", "IntegerFacts.terrum")).ConfigureAwait(true));
        _ = source.Append("int32 Main() {");
        int[] values = [int.MinValue, int.MinValue + 1, -65536, -1, 0, 1, 65536, int.MaxValue - 1, int.MaxValue];

        foreach (var left in values)
        {
            foreach (var right in values)
            {
                var sum = (long)left + right;
                var expected = sum is >= int.MinValue and <= int.MaxValue ? "true" : "false";
                _ = source.Append(FormattableString.Invariant(
                    $"if (CanAddInt32({left}, {right}) != {expected}) {{ return 1; }}"));
            }
        }

        _ = source.Append("return 0; }");
        Assert.Equal(0, await ExecuteAsync(Emit(source.ToString())).ConfigureAwait(true));
    }

    [Fact]
    public async Task SignedComparisonsAgreeAtBoundariesAsync()
    {
        var source = new StringBuilder("bool Less(int32 a, int32 b) { return a < b; } "
            + "bool Greater(int32 a, int32 b) { return a > b; } "
            + "bool Equal(int32 a, int32 b) { return a == b; } int32 Main() {");
        int[] values = [int.MinValue, -1, 0, 1, int.MaxValue];

        foreach (var left in values)
        {
            foreach (var right in values)
            {
                var less = left < right ? "true" : "false";
                var greater = left > right ? "true" : "false";
                var equal = left == right ? "true" : "false";
                _ = source.Append(FormattableString.Invariant(
                    $"if (Less({left}, {right}) != {less} || Greater({left}, {right}) != {greater} || Equal({left}, {right}) != {equal}) {{ return 1; }}"));
            }
        }

        _ = source.Append("return 0; }");
        Assert.Equal(0, await ExecuteAsync(Emit(source.ToString())).ConfigureAwait(true));
    }

    [Fact]
    public void PeIsDeterministicNativeX64WithImportsAndUnwindData()
    {
        const string Source = "int32 Main() { return F(3); } int32 F(int32 x) { return x + 2; }";
        var image = Emit(Source);
        Assert.Equal(image, Emit(Source));
        using var stream = new MemoryStream(image);
        using var reader = new PEReader(stream);
        Assert.False(reader.HasMetadata);
        Assert.Equal(Machine.Amd64, reader.PEHeaders.CoffHeader.Machine);
        var header = Assert.IsType<PEHeader>(reader.PEHeaders.PEHeader);
        Assert.Equal(PEMagic.PE32Plus, header.Magic);
        Assert.Equal(Subsystem.WindowsCui, header.Subsystem);
        Assert.NotEqual(0, header.AddressOfEntryPoint);
        Assert.NotEqual(0, header.ImportTableDirectory.RelativeVirtualAddress);
        Assert.True(header.ExceptionTableDirectory.Size >= 24);
        Assert.Equal(0, header.CorHeaderTableDirectory.Size);
        Assert.True((header.DllCharacteristics & DllCharacteristics.NxCompatible) != 0);
        Assert.All(reader.PEHeaders.SectionHeaders, section =>
            Assert.False((section.SectionCharacteristics & (SectionCharacteristics.MemWrite | SectionCharacteristics.MemExecute))
                == (SectionCharacteristics.MemWrite | SectionCharacteristics.MemExecute)));
    }

    [Fact]
    public void ForgedIrIsVerifiedBeforeEmission()
    {
        var program = GetProgram("int32 Main() { return 1; }");
        var function = program.Functions[0];
        var span = function.Span;
        var wrongReturn = function with { Body = new BoundBlock([new BoundReturn(new BoundConstant(1, PrimitiveType.Bool, span), span)], span) };
        var uninitialized = function with {
            Variables = [new BoundVariable("vacant", PrimitiveType.Int32, true, span)],
            Body = new BoundBlock([new BoundReturn(new BoundRead(0, PrimitiveType.Int32, span), span)], span),
        };
        var invalidBoolean = function with {
            Body = new BoundBlock([new BoundIf(new BoundConstant(2, PrimitiveType.Bool, span),
                new BoundReturn(new BoundConstant(1, PrimitiveType.Int32, span), span),
                new BoundReturn(new BoundConstant(0, PrimitiveType.Int32, span), span), span)], span),
        };
        var invalidRequirement = function with {
            Body = new BoundBlock([new BoundRequire(new BoundConstant(1, PrimitiveType.Int32, span), span),
                new BoundReturn(new BoundConstant(0, PrimitiveType.Int32, span), span)], span),
        };

        foreach (var invalid in new[] { wrongReturn, uninitialized, invalidBoolean, invalidRequirement })
        {
            _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(program with { Functions = [invalid] }, NativeRuntimePolicy.FailFast));
        }

        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(program with { EntryPoint = -1 }, NativeRuntimePolicy.FailFast));
    }

    private static BoundProgram GetProgram(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));

        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    private static byte[] Emit(string source) => NativeEmitter.Emit(GetProgram(source), NativeRuntimePolicy.FailFast);

    private static async Task<int> ExecuteAsync(byte[] image)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "Native execution tests require Windows x64.");
        var path = Path.Combine(Path.GetTempPath(), $"terrum-native-{Guid.NewGuid():N}.exe");

        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true })
                ?? throw new InvalidOperationException("Native process did not start.");

            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                throw;
            }

            return process.ExitCode;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
