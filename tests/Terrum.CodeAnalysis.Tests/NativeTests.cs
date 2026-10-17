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
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeTests
{
    [TestCase("require true; return 7;",7)]
    [TestCase("require false; return 7;",unchecked((int)0xE0540003))]
    [TestCase("require (int32(1) < 2); return 8;",8)]
    [TestCase("require true || (int32(1) / 0 == 0); return 9;",9)]
    [TestCase("require false && (int32(1) / 0 == 0); return 9;",unchecked((int)0xE0540003))]
    [TestCase("require int32(1) / 0 == 0; return 9;",unchecked((int)0xE0540002))]
    public async Task ExecutesRequirementsWithExplicitFailurePolicyAsync(string body, int expected)
    {
        Assert.That(await ExecuteAsync(Emit("int32 Main() { " + body + " }")).ConfigureAwait(true), Is.EqualTo(expected));
    }
    [TestCase("int32 Main() { return 42; }",42)]
    [TestCase("void Main() { }",0)]
    [TestCase("int32 Main() { return -2147483648; }",int.MinValue)]
    [TestCase("int32 Main() { return 2147483647; }",int.MaxValue)]
    [TestCase("int32 Main() { return 7 + 3 * 5 - 4; }",18)]
    [TestCase("int32 Main() { return -7 / 3; }",-2)]
    [TestCase("int32 Main() { return -7 % 3; }",-1)]
    [TestCase("int32 Main() { return -2147483648 % -1; }",0)]
    [TestCase("int32 Main() { return ~(3 | 8) ^ (6 & 7); }",-14)]
    [TestCase("int32 Main() { mut int32 n = 0; mut int32 sum = 0; while (n < 10) { sum = sum + n; n = n + 1; } return sum; }",45)]
    [TestCase("int32 Main() { return Factorial(6); } int32 Factorial(int32 n) { if (n <= 1) { return 1; } return n * Factorial(n - 1); }",720)]
    [TestCase("int32 Main() { return Fib(10); } int32 Fib(int32 n) { if (n < 2) { return n; } return Fib(n - 1) + Fib(n - 2); }",55)]
    [TestCase("int32 Main() { return Mix(1, 2, 3, 4, 5, 6); } int32 Mix(int32 a, int32 b, int32 c, int32 d, int32 e, int32 f) { return a + b * 2 + c * 3 + d * 4 + e * 5 + f * 6; }",91)]
    [TestCase("int32 Main() { return Mix(Id(1), Id(2), Id(3), Id(4), Id(5), Mix(1,2,3,4,5,6)); } int32 Id(int32 x) { return x; } int32 Mix(int32 a, int32 b, int32 c, int32 d, int32 e, int32 f) { return a + b + c + d + e + f; }",36)]
    [TestCase("int32 Main() { if (false && Fail()) { return 1; } if (true || Fail()) { return 8; } return 2; } bool Fail() { int32 value = 1 / 0; return value == 0; }",8)]
    [TestCase("int32 Main() { bool a = true; bool b = false; if ((a & !b) ^ b) { return 11; } return 2; }",11)]
    [TestCase("int32 Main() { if (int32(-2147483648) < 2147483647 && int32(2147483647) >= -2147483648) { return 9; } return 2; }",9)]
    [TestCase("int32 Main() { NoResult(); return 6; } void NoResult() { return (); }",6)]
    [TestCase("void Main() { return Fail(); } void Fail() { int32 value = 1 / 0; }",unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { mut int32 n = 0; while (n < 4) { int32 value = n * 2; n = n + 1; if (value == 6) { return value; } } return 0; }",6)]
    [TestCase("int32 Main() { if (Equal(2, 2)) { return 1; } else { return 0; } } bool Equal(int32 a, int32 b) { return a == b; }",1)]
    [TestCase("int32 Main() { mut int32 n = 0; while (n < 3) { require Valid(n); n = n + 1; } return n; } bool Valid(int32 n) { return n >= 0; }",3)]
    [TestCase("int32 Main() { mut int32 n = 0; do { n = n + 1; if (n < 2) { continue; } } while (n < 3); return n + 39; }",42)]
    [TestCase("int32 Main() { mut int32 n = 0; outer: while (true) { do { n = n + 42; break outer; } while (true); } return n; }",42)]
    public async Task ExecutesNativeFunctionsAndStructuredControlFlowAsync(string source, int expected)
    {
        Assert.That(await ExecuteAsync(Emit(source)), Is.EqualTo(expected));
    }
    [TestCase("2147483647 + 1",unchecked((int)0xE0540001))]
    [TestCase("-2147483648 - 1",unchecked((int)0xE0540001))]
    [TestCase("50000 * 50000",unchecked((int)0xE0540001))]
    [TestCase("-(-2147483648)",unchecked((int)0xE0540001))]
    [TestCase("-2147483648 / -1",unchecked((int)0xE0540001))]
    [TestCase("1 / 0",unchecked((int)0xE0540002))]
    [TestCase("1 % 0",unchecked((int)0xE0540002))]
    [TestCase("(1 / 0) + (2147483647 + 1)",unchecked((int)0xE0540002))]
    [TestCase("(2147483647 + 1) + (1 / 0)",unchecked((int)0xE0540001))]
    public async Task RuntimeFailuresHaveExplicitProfileExitCodesAsync(string expression, int expected)
    {
        Assert.That(await ExecuteAsync(Emit("int32 Main() { return " + expression + "; }")), Is.EqualTo(expected));
    }

    [Test]
    public async Task ArgumentsEvaluateOnceInSourceOrderAsync()
    {
        const string Source = "int32 Main() { return F(1 / 0, 2147483647 + 1); } int32 F(int32 a, int32 b) { return a + b; }";
        Assert.That(await ExecuteAsync(Emit(Source)), Is.EqualTo(unchecked((int)0xE0540002)));
    }
    [TestCase("factorial.terrum",720)]
    [TestCase("control-flow.terrum",45)]
    [TestCase("stack-arguments.terrum",91)]
    [TestCase("requirements.terrum",10)]
    [TestCase("for-loops.terrum",285)]
    [TestCase("no-throw.terrum",42)]
    [TestCase("no-return.terrum",42)]
    public async Task PublishedNativeExamplesExecuteAsync(string name, int expected)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var source = await File.ReadAllTextAsync(Path.Combine(root, "samples", "native", name)).ConfigureAwait(true);
        Assert.That(await ExecuteAsync(Emit(source)).ConfigureAwait(true), Is.EqualTo(expected));
    }

    [Test]
    public async Task SelfHostAdditionPredicateAgreesWithWidenedBoundaryArithmeticAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var source = new StringBuilder(await File.ReadAllTextAsync(SelfHostPaths.ResolveFile(root, "IntegerFacts.terrum")).ConfigureAwait(true));
        _ = source.Append("int32 Main() { const bool staticOverflow = !CanAddInt32(2147483647, 1); "
            + "if (!staticOverflow || CanAddInt32(2147483647, 1)) { return 2; }");
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
        Assert.That(await ExecuteAsync(Emit(source.ToString())).ConfigureAwait(true), Is.EqualTo(0));
    }

    [Test]
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
        Assert.That(await ExecuteAsync(Emit(source.ToString())).ConfigureAwait(true), Is.EqualTo(0));
    }

    [Test]
    public void PeIsDeterministicNativeX64WithImportsAndUnwindData()
    {
        const string Source = "int32 Main() { return F(3); } int32 F(int32 x) { return x + 2; }";
        var image = Emit(Source);
        Assert.That(Emit(Source), Is.EqualTo(image));
        using var stream = new MemoryStream(image);
        using var reader = new PEReader(stream);
        Assert.That(reader.HasMetadata, Is.False);
        Assert.That(reader.PEHeaders.CoffHeader.Machine, Is.EqualTo(Machine.Amd64));
        var header = ((global::System.Func<PEHeader>)(() => { var __nunitActual0 = reader.PEHeaders.PEHeader; Assert.That(__nunitActual0, Is.TypeOf<PEHeader>()); return (PEHeader)__nunitActual0!; }))();
        Assert.That(header.Magic, Is.EqualTo(PEMagic.PE32Plus));
        Assert.That(header.Subsystem, Is.EqualTo(Subsystem.WindowsCui));
        Assert.That(header.AddressOfEntryPoint, Is.Not.EqualTo(0));
        Assert.That(header.ImportTableDirectory.RelativeVirtualAddress, Is.Not.EqualTo(0));
        Assert.That(header.ExceptionTableDirectory.Size >= 24, Is.True);
        Assert.That(header.CorHeaderTableDirectory.Size, Is.EqualTo(0));
        Assert.That((header.DllCharacteristics & DllCharacteristics.NxCompatible) != 0, Is.True);
        {var __nunitItems0 = reader.PEHeaders.SectionHeaders;        Assert.Multiple(() => {
            foreach (var __nunitItem1 in __nunitItems0)
            {
                Assert.That((__nunitItem1.SectionCharacteristics & (SectionCharacteristics.MemWrite | SectionCharacteristics.MemExecute))
                == (SectionCharacteristics.MemWrite | SectionCharacteristics.MemExecute), Is.False);
            }
        });}
    }

    [Test]
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
            _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(program with { Functions = [invalid] }, NativeRuntimePolicy.FailFast))!;
        }

        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(program with { EntryPoint = -1 }, NativeRuntimePolicy.FailFast))!;
    }

    private static BoundProgram GetProgram(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));

        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
    }

    private static byte[] Emit(string source) => NativeEmitter.Emit(GetProgram(source), NativeRuntimePolicy.FailFast);

    private static async Task<int> ExecuteAsync(byte[] image)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True, "Native execution tests require Windows x64.");
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-native-{Guid.NewGuid():N}.exe");

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
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }
}
