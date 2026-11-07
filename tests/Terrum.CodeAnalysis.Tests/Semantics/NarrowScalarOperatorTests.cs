// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class NarrowScalarOperatorTests
{
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void ReleaseProfileExecutesExactWidthIntegerOperators(BoundRuntimePolicy runtimePolicy)
    {
        var program = Compile("""
            int32 Main() {
                int8 signedWrap = int8(127) + int8(1);
                uint8 unsignedWrap = uint8(255) + uint8(1);
                int16 signedProduct = int16(0x7FFF) * int16(2);
                uint16 unsignedProduct = uint16(65535) * uint16(2);
                int8 complement = ~int8(0x0F);
                int8 signedShift = int8(64) << uint8(1);
                int8 arithmeticShift = int8(-3) >> int16(1);
                int8 logicalShift = int8(-3) >>> uint64(1);
                int8 quotient = int8(-7) / int8(3);
                int8 remainder = int8(-7) % int8(3);
                int8 minimumRemainder = int8(-128) % int8(-1);
                uint8 bitwise = uint8(0xAA) ^ uint8(0xFF);
                uint16 wideShift = uint16(1) << uint64(15);
                bool ordered = char8(0xFF) > char8(1)
                    && char8(0xFF) == char8(0xFF)
                    && char32(0xFFFF_FFFF) > char32(1)
                    && char32(1) != char32(2)
                    && rune(0x10_FFFF) > rune(0)
                    && rune(0) == rune(0);
                if (signedWrap != int8(-128) || unsignedWrap != 0
                    || signedProduct != int16(-2) || unsignedProduct != uint16(65534)
                    || complement != int8(-16) || signedShift != int8(-128)
                    || arithmeticShift != int8(-2) || logicalShift != int8(126)
                    || quotient != int8(-2) || remainder != int8(-1)
                    || minimumRemainder != 0 || bitwise != uint8(85)
                    || wideShift != uint16(32768) || !ordered) {
                    return 0;
                }
                return 42;
            }
            """, SourceArithmeticProfile.Release, runtimePolicy);

        var nativePolicy = runtimePolicy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        Assert.That(NativeJit.Execute(program, nativePolicy), Is.EqualTo(42));
    }

    [Test]
    public void DevelopmentProfileChecksNarrowOverflowWithoutCommittingUpdates()
    {
        var program = Compile("""
            int32 Main() {
                mut int8 signed = int8(127);
                mut uint8 unsigned = uint8(255);
                mut int16 signedWord = int16(-32768);
                mut uint16 unsignedWord = uint16(0);
                mut int32 failures = 0;
                unsafe {
                    try { signed++; } catch { if (signed != int8(127)) { return 1; } failures += 1; }
                    try { unsigned += uint8(1); } catch { if (unsigned != uint8(255)) { return 2; } failures += 1; }
                    try { signedWord--; } catch { if (signedWord != int16(-32768)) { return 3; } failures += 1; }
                    try { unsignedWord--; } catch { if (unsignedWord != uint16(0)) { return 4; } failures += 1; }
                    try { int8 overflow = -int8(-128); } catch { failures += 1; }
                    try { uint16 overflow = -uint16(1); } catch { failures += 1; }
                    try { int8 invalidCount = int8(1) << uint64(0x1_0000_0001); }
                    catch { failures += 1; }
                }
                return failures == 7 ? 42 : 0;
            }
            """, SourceArithmeticProfile.Development, BoundRuntimePolicy.CheckedExceptions);

        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }

    [Test]
    public void RequiredConstEvalUsesNarrowWidthAndProfile()
    {
        const string source = """
            [ConstEval] int8 Add(int8 left, int8 right) { return left + right; }
            int32 Main() {
                const int8 result = Add(int8(127), int8(1));
                return result == int8(-128) ? 42 : 0;
            }
            """;
        var release = Compile(source, SourceArithmeticProfile.Release, BoundRuntimePolicy.FailFast);
        Assert.That(NativeJit.Execute(release, NativeRuntimePolicy.FailFast), Is.EqualTo(42));

        var development = Compilation.Create(SyntaxTree.Parse(source), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.CheckedExceptions,
        });
        Assert.That(global::System.Linq.Enumerable.Any(development.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3024"
                && diagnostic.Message.Contains("checked arithmetic", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void StaticProofUsesNarrowIntegerRanges()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                int8 value = int8(0);
                static require value >= int8(-128);
                static require value <= int8(127);
                uint8 byteValue = uint8(0);
                static require byteValue <= uint8(255);
                return 42;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [TestCase(SourceArithmeticProfile.Development, true)]
    [TestCase(SourceArithmeticProfile.Release, false)]
    public void NoFailureProofUsesNarrowArithmeticPolicy(SourceArithmeticProfile profile, bool expectFailure)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "int8 Add(int8 left, int8 right) { return left + right; }"),
            new CompilationOptions { SourceArithmeticProfile = profile });
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = BoundProgramTestHelpers.MarkNoFailureProof(compilation.Program!);
        if (expectFailure)
        {
            _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(program));
        }
        else
        {
            SemanticVerifier.VerifyLibrary(program);
        }
    }

    [Test]
    public void PrimitiveBindingRejectsPromotionAndCodeUnitArithmetic()
    {
        foreach (var source in new[] {
            "int32 Main() { int8 value = int8(1) + int16(1); return 0; }",
            "int32 Main() { bool value = int8(1) == int16(1); return 0; }",
            "int32 Main() { char32 value = char32(1) + char32(2); return 0; }",
        })
        {
            var compilation = Compilation.Create(SyntaxTree.Parse(source));
            Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,
                static diagnostic => diagnostic.Code == "TRM3007"), Is.True, source);
        }
    }

    [Test]
    public void NarrowStructFieldUpdatesPreserveAdjacentFields()
    {
        var program = Compile("""
            [Layout(LayoutKind.Sequential)] struct Pair {
                mut int8 First;
                mut int8 Second;
                public Pair(int8 first, int8 second) { First = first; Second = second; }
                public mut int32 Advance() {
                    First++;
                    Second += int8(2);
                    return First == int8(-128) && Second == int8(6) ? 42 : 0;
                }
            }
            int32 Main() {
                mut Pair pair = new Pair(int8(127), int8(4));
                return pair.Advance();
            }
            """, SourceArithmeticProfile.Release, BoundRuntimePolicy.CheckedExceptions,
            LayoutCoreRegistrationTestData.Create());

        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ReleaseNarrowArithmeticExecutesFromAotImageAsync(NativeRuntimePolicy nativePolicy)
    {
        Assume.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var runtimePolicy = nativePolicy == NativeRuntimePolicy.FailFast
            ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
        var program = Compile("""
            int32 Main() {
                mut int8 value = int8(127);
                value++;
                int16 product = int16(0x7FFF) * int16(2);
                return value == int8(-128) && product == int16(-2) ? 42 : 0;
            }
            """, SourceArithmeticProfile.Release, runtimePolicy);
        var executable = Path.Combine(TestArtifactPaths.DirectoryPath, $"narrow-operators-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(executable, NativeEmitter.Emit(program, nativePolicy))
                .ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(executable) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start the native worker.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
                await process.WaitForExitAsync().ConfigureAwait(true);
                throw;
            }
            Assert.That(process.ExitCode, Is.EqualTo(42));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    private static BoundProgram Compile(string source, SourceArithmeticProfile profile,
        BoundRuntimePolicy runtimePolicy,
        LayoutCoreRegistrationSet? layoutCoreRegistration = null)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), new CompilationOptions {
            SourceArithmeticProfile = profile,
            RuntimePolicy = runtimePolicy,
            LayoutCoreRegistration = layoutCoreRegistration,
        });
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
        return compilation.Program!;
    }
}
