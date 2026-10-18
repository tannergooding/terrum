// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

using Terrum.CodeAnalysis.Tests.Modules.TrRm;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int TrRmAdd(int left, int right);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate long TrRmIdentity(long value);

    [Test]
    public async Task PersistedTrRmRunsAndBuildsWithoutProducerSourceAsync()
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-consumer-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Main() { mut int32 x = 20; x = x + 22; return x; }"),
                "compile-trrm", "-", "--identity", "consumer", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.That(compileError, Is.Empty);
            Assert.That(File.Exists(artifact), Is.True);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran == 42, Is.True, $"{runOutput}{Environment.NewLine}{runError}");
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(42));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task PersistedInitOutputCallsRunAndBuildWithoutProducerSourceAsync()
    {
        const string source = """
            void Fill(int32 value, init ref<int32> result) {
                result = value;
            }
            int32 Observe(bool value) { return value ? 42 : 0; }
            int32 ObserveOne(bool value) { return value ? 1 : 0; }
            bool Enabled() { return true; }
            float64 Scale() { return float64(2.5); }
            int32 One32() { return 1; }
            int64 FortyTwo64() { return int64(42); }
            int64 NegativeFive() { return int64(-5); }
            int64 Two64() { return int64(2); }
            int64 MinusTwo64() { return int64(-2); }
            int64 MinusOne64() { return int64(-1); }
            int64 Zero64() { return int64(0); }
            int64 Minimum64() { return int64.MinValue; }
            int32 Main() {
                init int32 result;
                Fill(Observe(Enabled() == true) + Observe(Scale() < float64(3.5))
                    + Observe(Scale() * (float64(3.5) - Scale()) == Scale())
                    + Observe(NegativeFive() / Two64() == MinusTwo64())
                    + Observe(NegativeFive() % Two64() == MinusOne64())
                    + Observe(Minimum64() % MinusOne64() == Zero64())
                    + ObserveOne(int32(FortyTwo64()) == 42)
                    + ObserveOne(float32(One32()) == float32(1.0)), init result);
                return result;
            }
            """;
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-init-output-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity",
                "init-output", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.That(compileError, Is.Empty);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran, Is.EqualTo(254));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(254));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task PersistedInitOutputSupportsMixedScalarReferentsAsync()
    {
        const string source = """
            uint32 Fill(bool enabled, init ref<bool> enabledResult,
                char16 character, init ref<char16> characterResult,
                uint64 count, init ref<uint64> countResult,
                float64 amount, init ref<float64> amountResult) {
                enabledResult = enabled;
                characterResult = character;
                countResult = count;
                amountResult = amount;
                return uint32.MaxValue;
            }
            int32 Check(bool enabled, char16 character, uint64 count, float64 amount,
                uint32 returned, int32 conditionalFalse, int32 conditionalTrue) {
                return enabled && character == char16(65) && count == uint64.MaxValue
                    && amount == float64(2.5) && returned == uint32.MaxValue
                    && conditionalFalse == 0 && conditionalTrue == 42 ? 42 : 0;
            }
            int32 CheckConditional(bool flag, char16 marker, int32 result,
                uint32 smallCount, int64 signedCount, uint64 wideCount,
                float32 ratio, float64 amount) {
                return !flag && marker == char16(65) && result == 0
                    && smallCount == uint32.MaxValue && signedCount == int64.MinValue
                    && wideCount == 0 && ratio == float32(1.5)
                    && amount == 0.0 ? 42 : 1;
            }
            bool TryFill(bool success, init ref<bool> flag,
                init ref<char16> marker, init ref<int32> result,
                init ref<uint32> smallCount, init ref<int64> signedCount,
                init ref<uint64> wideCount, init ref<float32> ratio,
                init ref<float64> conditionalAmount) {
                flag = false;
                marker = char16(65);
                result = 0;
                smallCount = uint32.MaxValue;
                signedCount = int64.MinValue;
                wideCount = 0;
                ratio = float32(1.5);
                conditionalAmount = 0.0;
                return success;
            }
            int32 RunConditional(bool success) {
                init bool flag;
                init char16 marker;
                init int32 result;
                init uint32 smallCount;
                init int64 signedCount;
                init uint64 wideCount;
                init float32 ratio;
                init float64 amount;
                if (TryFill(success, init flag, init marker, init result,
                    init smallCount, init signedCount, init wideCount, init ratio,
                    init amount)) {
                    return CheckConditional(flag, marker, result, smallCount,
                        signedCount, wideCount, ratio, amount);
                } else {
                    return 0;
                }
            }
            int32 Main() {
                init bool enabled;
                init char16 character;
                init uint64 count;
                init float64 amount;
                uint32 returned = Fill(true, init enabled, char16(65), init character,
                    uint64.MaxValue, init count, float64(2.5), init amount);
                int32 conditionalFalse = RunConditional(false);
                int32 conditionalTrue = RunConditional(true);
                return Check(enabled, character, count, amount, returned,
                    conditionalFalse, conditionalTrue);
            }
            """;
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-init-output-scalars-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity",
                "init-output-scalars", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.That(compiled, Is.EqualTo(0));
            Assert.That(compileError, Is.Empty);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran, Is.EqualTo(42));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built, Is.EqualTo(0));
            Assert.That(buildError, Is.Empty);
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(42));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }
    [TestCase(true)]
    [TestCase(false)]
    public async Task PersistedInitOutputConditionalFailurePreservesFailFastStatusAsync(
        bool failOnTrue)
    {
        var condition = failOnTrue ? "true" : "false";
        var trueArm = failOnTrue ? "require false;" : "return 42;";
        var falseArm = failOnTrue ? "return 42;" : "require false;";
        var source = $$"""
            bool Select(init ref<int32> result) {
                result = 7;
                return {{condition}};
            }
            int32 Main() {
                init int32 result;
                if (Select(init result)) {
                    {{trueArm}}
                } else {
                    {{falseArm}}
                }
            }
            """;
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-init-output-conditional-failure-{failOnTrue}-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity",
                "init-output-conditional-failure", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.That(compileError, Is.Empty);

            var (jitted, jitOutput, jitError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "jit", "-", "--runtime", "fail-fast", "--json");
            Assert.That(jitted, Is.EqualTo(unchecked((int)0xE0540003)));
            Assert.That(jitOutput, Is.Empty);
            Assert.That(jitError, Is.Empty);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran, Is.EqualTo(unchecked((int)0xE0540003)));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(unchecked((int)0xE0540003)));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }
    [TestCase(true,true)]
    [TestCase(false,false)]
    public async Task PersistedInitOutputConditionalValueCallPreservesFailFastStatusAsync(
        bool zeroArgumentCall, bool failOnTrue)
    {
        var selected = failOnTrue ? "true" : "false";
        var signature = zeroArgumentCall ? "()" : "(bool selected)";
        var returnExpression = zeroArgumentCall ? selected : "selected";
        var invocation = zeroArgumentCall ? "Select()" : $"Select({selected})";
        var trueArm = failOnTrue ? "require false;" : "return initialized;";
        var falseArm = failOnTrue ? "return initialized;" : "require false;";
        const int expected = unchecked((int)0xE0540003);
        var source = $$"""
            bool Select{{signature}} { return {{returnExpression}}; }
            int32 Main() {
                init int32 initialized;
                init int32 vacant;
                initialized = 42;
                if ({{invocation}}) {
                    {{trueArm}}
                } else {
                    {{falseArm}}
                }
            }
            """;
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-init-output-value-condition-{zeroArgumentCall}-{failOnTrue}-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity",
                "init-output-value-condition", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.That(compileError, Is.Empty);

            var (jitted, jitOutput, jitError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "jit", "-", "--runtime", "fail-fast", "--json");
            Assert.That(jitted, Is.EqualTo(expected));
            Assert.That(jitOutput, Is.Empty);
            Assert.That(jitError, Is.Empty);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran, Is.EqualTo(expected));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(expected));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task PersistedInitOutputSignedNegationPreservesOverflowStatusAsync()
    {
        const string source = """
            void Fill(int32 value, init ref<int32> result) { result = value; }
            int32 Main() {
                init int32 result;
                Fill(-int32.MinValue, init result);
                return result;
            }
            """;
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-init-negate-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity",
                "init-output-negate", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.That(compileError, Is.Empty);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran, Is.EqualTo(unchecked((int)0xE0540001)));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);
        }
        finally
        {
            File.Delete(artifact);
        }
    }

    [Test]
    public async Task PersistedInitOutputIntegerConversionPreservesOverflowStatusAsync()
    {
        const string source = """
            void Fill(int32 value, init ref<int32> result) { result = value; }
            uint64 Maximum() { return uint64.MaxValue; }
            int32 Main() {
                init int32 result;
                Fill(int32(Maximum()), init result);
                return result;
            }
            """;
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-init-convert-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity",
                "init-output-convert", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.That(compileError, Is.Empty);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran, Is.EqualTo(unchecked((int)0xE0540001)));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);
        }
        finally
        {
            File.Delete(artifact);
        }
    }

    [Test]
    public async Task PersistedInitOutputFloatToIntegerConversionPreservesOverflowStatusAsync()
    {
        const string source = """
            void Fill(int32 value, init ref<int32> result) { result = value; }
            float64 TooLarge() { return float64(4294967296.0); }
            int32 Main() {
                init int32 result;
                Fill(int32(TooLarge()), init result);
                return result;
            }
            """;
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-init-float-convert-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity",
                "init-output-float-convert", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.That(compileError, Is.Empty);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran, Is.EqualTo(unchecked((int)0xE0540001)));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);
        }
        finally
        {
            File.Delete(artifact);
        }
    }

    [Test]
    public async Task PersistedNestedAcyclicBreaksRunAndBuildWithoutProducerSourceAsync()
    {
        const string source = """
            int32 Main() {
                mut int32 result = 0;
                do {
                    do {
                        result = 40;
                        break;
                    } while (Fail());
                    result += 2;
                    break;
                } while (Fail());
                return result;
            }
            bool Fail() { require false; return false; }
            """;
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-nested-acyclic-breaks-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity", "nested-acyclic-breaks",
                "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.That(compileError, Is.Empty);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran == 42, Is.True, $"{runOutput}{Environment.NewLine}{runError}");
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(buildError, Is.Empty);
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(42));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task PersistedUnsafeCallBeforeAcyclicReturnKeepsFailureStatusAsync()
    {
        const string source = """
            unsafe void Danger() { require false; }
            int32 Main() {
                unsafe {
                    do {
                        Danger();
                        return 42;
                    } while (false);
                }
            }
            """;
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-unsafe-acyclic-prefix-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity", "unsafe-acyclic-prefix",
                "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran, Is.EqualTo(unchecked((int)0xE0540003)));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(unchecked((int)0xE0540003)));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task PersistedDirectCallsAgreeAcrossJitAndExecutableAsync()
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-calls-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            const string source = """
                int32 Main() { return Recursive(5); }
                int32 Recursive(int32 n) {
                    if (n == 0) { return 1; }
                    else { return n * Recursive(n - 1); }
                }
                """;
            var (compiled, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "recursive-calls", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{output}{Environment.NewLine}{error}");
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran == 120, Is.True, $"{runOutput}{Environment.NewLine}{runError}");
            var (built, buildOutput, buildError) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(120));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task PersistedNoThrowAndDoesNotReturnEffectsAreAuditedAsync()
    {
        const string source = """
            [NoThrow] int32 Identity(int32 value) { return value; }
            [NoThrow][DoesNotReturn] void Never() { while (true) {} }
            [NoThrow][DoesNotReturn] void Stop() { Never(); }
            int32 Main() { return Identity(42); }
            """;
        var artifact = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-effects-{Guid.NewGuid():N}.trrm");
        try
        {
            var (compiled, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "effects", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{output}{Environment.NewLine}{error}");
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran == 42, Is.True, $"{runOutput}{Environment.NewLine}{runError}");
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);
        }
        finally
        {
            File.Delete(artifact);
        }
    }

    [Test]
    public async Task PersistedNonReturningCallKeepsRequirementFailureStatusAsync()
    {
        const string source = "[DoesNotReturn] void Stop() { require false; } int32 Main() { Stop(); }";
        var artifact = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-no-return-{Guid.NewGuid():N}.trrm");
        try
        {
            var (compiled, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "no-return", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{output}{Environment.NewLine}{error}");
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran, Is.EqualTo(unchecked((int)0xE0540003)));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);
        }
        finally
        {
            File.Delete(artifact);
        }
    }

    [Test]
    public async Task IndexedFailureReasonsMapToTheExistingNativeStatusesAsync()
    {
        var artifact = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-failure-reasons-{Guid.NewGuid():N}.trrm");
        try
        {
            var (compiled, output, error) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Main() { require false; return 0; }"),
                "compile-trrm", "-", "--identity", "failure-reasons", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{output}{Environment.NewLine}{error}");
            var artifactBytes = await File.ReadAllBytesAsync(artifact);
            var reasonOffset = IndexedFailureReasonOffset(artifactBytes);
            foreach (var (reason, expected) in new[]
            {
                (1, unchecked((int)0xE0540003)),
                (2, unchecked((int)0xE0540001)),
                (3, unchecked((int)0xE0540002)),
                (4, unchecked((int)0xE0540001)),
            })
            {
                artifactBytes[reasonOffset] = (byte)reason;
                await File.WriteAllBytesAsync(artifact, artifactBytes);
                var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                    "--runtime", "fail-fast", "--json");
                Assert.That(ran, Is.EqualTo(expected));
                Assert.That(runOutput, Is.Empty);
                Assert.That(runError, Is.Empty);
            }
        }
        finally
        {
            File.Delete(artifact);
        }
    }

    private static int IndexedFailureReasonOffset(byte[] artifact)
    {
        var sectionCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(artifact.AsSpan(12));
        for (var section = 0; section < sectionCount; section++)
        {
            var entry = artifact.AsSpan(32 + section * 16);
            if (BinaryPrimitives.ReadUInt16LittleEndian(entry) != 0x20)
            {
                continue;
            }
            var rowCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var rowStart = offset + (rowCount + 1) * sizeof(uint);
            var rowLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(
                artifact.AsSpan(offset + rowCount * sizeof(uint)));
            var rowEnd = rowStart + rowLength;
            var marker = Array.LastIndexOf(artifact, (byte)0x87, rowEnd - 1, rowEnd - rowStart);
            if (marker < rowStart || artifact[marker + 1] != 1)
            {
                throw new InvalidOperationException("Indexed failure terminator was not found.");
            }
            return marker + 1;
        }
        throw new InvalidOperationException("TRRM Body section was not found.");
    }

    [Test]
    public async Task PersistedLibraryExportCanCallPrivateSixArgumentHelperAsync()
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-calls-library-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var library = prefix + ".dll";
        try
        {
            const string source = """
                public int32 Add(int32 first, int32 second) {
                    return Helper(first, second, 3, 4, 5, 6);
                }
                int32 Helper(int32 a, int32 b, int32 c, int32 d, int32 e, int32 f) {
                    return a + b + c + d + e + f;
                }
                """;
            var (compiled, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "export-calls", "-o", artifact,
                "--runtime", "fail-fast", "--library", "--json");
            Assert.That(compiled == 0, Is.True, $"{output}{Environment.NewLine}{error}");
            var (built, buildOutput, buildError) = await RunAsync([],
                "build-trrm-library", artifact, "--export", "Add", "-o", library,
                "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{buildOutput}{Environment.NewLine}{buildError}");
            var handle = NativeLibrary.Load(library);
            try
            {
                var add = Marshal.GetDelegateForFunctionPointer<TrRmAdd>(
                    NativeLibrary.GetExport(handle, "Add"));
                Assert.That(add(10, 13), Is.EqualTo(41));
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(library);
        }
    }
    [Test]
    public async Task PersistedTrRmLibraryPublishesTwoIndependentNativeExportsAsync()
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-library-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var library = prefix + ".dll";
        try
        {
            const string source = """
                public int32 Add(int32 left, int32 right) { return left + right; }
                public int64 Identity(int64 value) { return value; }
                """;
            var (compiled, stdout, stderr) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "two-exports", "-o", artifact,
                "--runtime", "fail-fast", "--library", "--json");
            Assert.That(compiled == 0, Is.True, $"{stdout}{Environment.NewLine}{stderr}");
            Assert.That(stderr, Is.Empty);
            var (built, output, error) = await RunAsync([], "build-trrm-library", artifact,
                "--export", "Add", "--export", "Identity", "-o", library,
                "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{output}{Environment.NewLine}{error}");
            Assert.That(error, Is.Empty);
            var handle = NativeLibrary.Load(library);
            try
            {
                var add = Marshal.GetDelegateForFunctionPointer<TrRmAdd>(
                    NativeLibrary.GetExport(handle, "Add"));
                var identity = Marshal.GetDelegateForFunctionPointer<TrRmIdentity>(
                    NativeLibrary.GetExport(handle, "Identity"));
                Assert.That(add(20, 22), Is.EqualTo(42));
                Assert.That(identity(long.MinValue), Is.EqualTo(long.MinValue));
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(library);
        }
    }
    [TestCase("int32 Main() { return 2147483647 + 1; }",unchecked((int)0xE0540001))]
    [TestCase("int32 Main() { return 1 / 0; }",unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { return int32(uint32.MaxValue); }",unchecked((int)0xE0540001))]
    [TestCase("int32 Main() { _ = int32(1) / 0; return 42; }",unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { int32 x = 1 / 0; return 42; }",unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { mut int32 x = int32.MaxValue; do { x += 1; break; } while (false); return x; }",unchecked((int)0xE0540001))]
    [TestCase("int32 Main() { mut int32 value = 42; mut int32 divisor = 0; do { value /= divisor; break; } while (false); return value; }",unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { do { int32 value = Fail(); break; } while (false); return 42; } int32 Fail() { require false; return 0; }",unchecked((int)0xE0540003))]
    [TestCase("int32 Main() { do { require false; return 42; } while (false); }",unchecked((int)0xE0540003))]
    [TestCase("int32 Main() { do { Fail(); break; } while (false); return 42; } void Fail() { require false; }",unchecked((int)0xE0540003))]
    [TestCase("int32 Main() { do { Fail(); } while (false); return 42; } void Fail() { require false; }",unchecked((int)0xE0540003))]
    [TestCase("int32 Main() { for (mut int32 i = 0; true; i = 1 / 0) { i = int32.MaxValue + 1; break; } return 42; }",unchecked((int)0xE0540001))]
    [TestCase("int32 Main() { return (1 / 0) + (int32.MaxValue + 1); }",unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { return (int32.MaxValue + 1) + (1 / 0); }",unchecked((int)0xE0540001))]
    [TestCase("int32 Main() { Fail(); return 42; } void Fail() { int32 x = 1 / 0; }",unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { _ = Fail(); return 42; } int32 Fail() { return 1 / 0; }",unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { return F(1 / 0, int32.MaxValue + 1); } int32 F(int32 a, int32 b) { return 42; }",unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { return F(int32.MaxValue + 1, 1 / 0); } int32 F(int32 a, int32 b) { return 42; }",unchecked((int)0xE0540001))]
    public async Task TrRmCheckedFailureKeepsFailFastPolicyAfterPersistenceAsync(string source, int failure)
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-failure-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var executable = prefix + ".exe";
        try
        {
            var (compiled, stdout, stderr) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "failure", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.That(compiled == 0, Is.True, $"{stdout}{Environment.NewLine}{stderr}");
            Assert.That(stderr, Is.Empty);
            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran == failure, Is.True, $"Expected {failure:X8}, received {ran:X8}: {runOutput}{Environment.NewLine}{runError}");
            var (built, output, error) = await RunAsync([], "build-trrm", artifact,
                "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(built == 0, Is.True, $"{output}{Environment.NewLine}{error}");
            Assert.That(await ExecuteTrRmImageAsync(executable), Is.EqualTo(failure));
        }
        finally
        {
            File.Delete(artifact);
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    private static async Task<int> ExecuteTrRmImageAsync(string executable)
    {
        using var process = Process.Start(new ProcessStartInfo(executable) {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("Executable was not started.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(await stdout, Is.Empty);
        Assert.That(await stderr, Is.Empty);
        return process.ExitCode;
    }
    [TestCase("compile-trrm")]
    [TestCase("run-trrm")]
    [TestCase("build-trrm")]
    [TestCase("build-trrm-library")]
    public async Task TrRmRejectsCheckedExceptionsAtEveryExecutionBoundaryAsync(string command)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-policy-{Guid.NewGuid():N}.trrm");
        var arguments = command switch {
            "compile-trrm" => new[] { command, "-", "--identity", "policy", "-o", path, "--runtime", "checked-exceptions", "--json" },
            "run-trrm" => [command, path, "--runtime", "checked-exceptions", "--json"],
            "build-trrm" => [command, path, "-o", Path.ChangeExtension(path, ".exe"), "--runtime", "checked-exceptions", "--json"],
            _ => [command, path, "--export", "Value", "-o", Path.ChangeExtension(path, ".dll"), "--runtime", "checked-exceptions", "--json"],
        };
        var (exit, stdout, stderr) = await RunAsync(
            Encoding.UTF8.GetBytes("int32 Main() { return 42; }"), arguments);
        Assert.That(exit == 1, Is.True, $"{stdout}{Environment.NewLine}{stderr}");
        Assert.That(stderr, Is.Empty);
        using var result = JsonDocument.Parse(stdout);
        Assert.That(result.RootElement.GetProperty("diagnostics")[0]
            .GetProperty("code").GetString(), Is.EqualTo("TRM5001"));
        Assert.That(result.RootElement.GetProperty("diagnostics")[0]
            .GetProperty("message").GetString(), Does.Contain("fail-fast"));
    }
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 2) { int32 inner = x; init int32 vacant; x = inner + 1; } return x; }")]
    [TestCase("int32 Main() { mut int32 x = 0; if (x == 0) { int32 inner = x; init int32 vacant; x = inner + 1; } return x; }")]
    public async Task TrRmSourceRejectionDoesNotOverwritePreviousArtifactAsync(string source)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-reject-{Guid.NewGuid():N}.trrm");
        try
        {
            await File.WriteAllBytesAsync(path, [0xAA, 0x55]);
            var (exit, stdout, stderr) = await RunAsync(
                Encoding.UTF8.GetBytes(source),
                "compile-trrm", "-", "--identity", "reject", "-o", path,
                "--runtime", "fail-fast", "--json");
            Assert.That(exit == 1, Is.True, $"{stdout}{Environment.NewLine}{stderr}");
            Assert.That(stderr, Is.Empty);
            Assert.That(stdout, Does.Contain("TRM5001"));
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(new byte[] { 0xAA, 0x55 }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task TrRmIndependentBytesExecuteInFreshProcessAsync()
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-independent-{Guid.NewGuid():N}.trrm");
        try
        {
            byte[] body = [4, 1, 1, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1];
            await File.WriteAllBytesAsync(path, TrRmExecutionAuditTests.Artifact(body));
            var (exit, stdout, stderr) = await RunAsync([], "run-trrm", path, "--runtime", "fail-fast", "--json");
            Assert.That(exit == 42, Is.True, $"{stdout}{Environment.NewLine}{stderr}");
            Assert.That(stdout, Is.Empty);
            Assert.That(stderr, Is.Empty);
        }
        finally
        {
            File.Delete(path);
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task TrRmInvalidEffectsPreserveNativeOutputAndRejectExecutionAsync(bool library)
    {
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-invalid-effects-{Guid.NewGuid():N}");
        var artifact = prefix + ".trrm";
        var output = prefix + (library ? ".dll" : ".exe");
        try
        {
            byte[] body = [4, 1, 1, 0, 0, 0, 3,
                1, 1, 1, 20, 1, 2, 1, 22, 0x15, 3, 1, 1, 2, 0x83, 3];
            await File.WriteAllBytesAsync(artifact,
                TrRmExecutionAuditTests.Artifact(body, entry: !library, effect: 1, exported: library));
            await File.WriteAllBytesAsync(output, [0xAA, 0x55]);
            var arguments = library
                ? new[] { "build-trrm-library", artifact, "--export", "Main", "-o", output, "--runtime", "fail-fast", "--json" }
                : ["build-trrm", artifact, "-o", output, "--runtime", "fail-fast", "--json"];
            var (exit, stdout, stderr) = await RunAsync([], arguments);
            Assert.That(exit == 1, Is.True, $"{stdout}{Environment.NewLine}{stderr}");
            Assert.That(stderr, Is.Empty);
            Assert.That(stdout, Does.Contain("TRM5001"));
            Assert.That(await File.ReadAllBytesAsync(output), Is.EqualTo(new byte[] { 0xAA, 0x55 }));
            var (run, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(run == 1, Is.True, $"{runOutput}{Environment.NewLine}{runError}");
            Assert.That(runOutput, Does.Contain("TRM5001"));
        }
        finally
        {
            File.Delete(artifact);
            File.Delete(output);
        }
    }

    [Test]
    public async Task TrRmMissingInputHasInputErrorStatusAsync()
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-absent-{Guid.NewGuid():N}.trrm");
        var (exit, stdout, stderr) = await RunAsync([], "run-trrm", path, "--runtime", "fail-fast", "--json");
        Assert.That(exit == 2, Is.True, $"{stdout}{Environment.NewLine}{stderr}");
        Assert.That(stderr, Is.Empty);
        Assert.That(stdout, Does.Contain("TRM2001"));
    }

    [Test]
    public async Task TrRmInvalidUtf8PreservesOutputAndReportsInputFailureAsync()
    {
        var output = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-invalid-utf8-{Guid.NewGuid():N}.trrm");
        try
        {
            await File.WriteAllBytesAsync(output, [0xAA, 0x55]);
            var (exit, stdout, stderr) = await RunAsync([0xC3, 0x28],
                "compile-trrm", "-", "--identity", "invalid-utf8", "-o", output,
                "--runtime", "fail-fast", "--json");
            Assert.That(exit == 2, Is.True, $"{stdout}{Environment.NewLine}{stderr}");
            Assert.That(stderr, Is.Empty);
            Assert.That(stdout, Does.Contain("TRM2001"));
            Assert.That(await File.ReadAllBytesAsync(output), Is.EqualTo(new byte[] { 0xAA, 0x55 }));
        }
        finally
        {
            File.Delete(output);
        }
    }
}
