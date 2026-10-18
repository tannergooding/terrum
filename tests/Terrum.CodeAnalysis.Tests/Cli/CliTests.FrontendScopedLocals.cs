// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
{
    [TestCase("int32 Main() { if (true) { int32 x = 42; return x; } return 0; }",42)]
    [TestCase("int32 Main() { if (true) { int32 x = 42; return x; } }",42)]
    [TestCase("int32 Main() { if (false) { int32 x = 1; return x; } else { int32 x = 42; return x; } }",42)]
    [TestCase("int32 Main() { mut int32 n = 0; while (n < 3) { mut var x = n; n += 1; if (n == 2) { continue; } if (n == 3) { break; } } return n + 39; }",42)]
    [TestCase("int32 Main() { mut int32 n = 0; while (n < 3) { mut var x = n; x += x++; n += 1; } return n + 39; }",42)]
    [TestCase("int32 Main() { mut int32 n = 0; mut int32 s = 0; while (n < 3) { mut int32 x = 1; s += x; n++; } return s + 39; }",42)]
    [TestCase("int32 Main() { mut int32 n = 0; mut int32 s = 0; while (n < 3) { mut int32 x = 1; s += x++; n++; } return s + 39; }",42)]
    [TestCase("int32 Main() { if (false) { int32 x = 1; } mut int32 x = 41; x++; return x; }",42)]
    [TestCase("int32 Main() { while (false) { int32 x = 1 / 0; } return 42; }",42)]
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 2) { int32 y = x; x = y + 1; } return x + 40; }",42)]
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 2) { if (true) { mut int32 y = x; x = y + 1; } } return x + 40; }",42)]
    [TestCase("int32 Main() { mut int32 n = 0; mut int32 s = 0; while (n < 3) { int32 x = 1; n++; if (n == 2) { continue; } s += x; } return s + 40; }",42)]
    [TestCase("int32 Main() { if (true) { int32 x = 42; if (x == 42) { var y = x; return y; } } return 0; }",42)]
    [TestCase("int32 Main() { mut int32 x = 0; if (true) { mut var y = x; y = 42; x = y; } return x; }",42)]
    [TestCase("int32 Main() { if (false) { int32 x = 0; return x; } else { int32 x = 42; return x; } }",42)]
    [TestCase("int32 Main() { if (false) { int32 int32 = 1; } return int32(uint64(42)); }",42)]
    [TestCase("int32 Main() { if (true) { if (true) { if (true) { if (true) { if (true) { if (true) { int32 x = 42; return x; } } } } } } return 0; }",42)]
    [TestCase("int32 Main() { if (false) { int32 a = 1; } else { int32 a = 2; } if (true) { int32 a = 3; } else { int32 a = 4; } return 42; }",42)]
    public async Task ScopedLocalsMatchHostedExecutableAndJitAsync(string source, int expected)
    {
        var root = RepositoryRoot();
        var frontend = TerrumProjectPaths.SourceDirectory(root);
        var nativePath = CreateTestArtifactPath($"scoped-{Guid.NewGuid():N}.native.exe");
        var hostedPath = CreateTestArtifactPath($"scoped-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (componentExit, componentOutput, componentError) = await RunAsync(input,
                    "frontend-component", frontend, "-", "--runtime", policy, "--json");
                Assert.That(componentExit == 0, Is.True, $"{policy}: {componentExit}: {componentOutput}{componentError}");
                using var document = JsonDocument.Parse(componentOutput);
                Assert.That(document.RootElement.GetProperty("result")
                    .GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build", frontend, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.That(nativeExit == 0, Is.True, $"{policy}: {nativeOutput}{nativeError}");
                Assert.That(hostedExit == 0, Is.True, $"{policy}: {hostedOutput}{hostedError}");
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
                Assert.That(await ExecuteNativeImageAsync(nativePath), Is.EqualTo(expected));
                var (runExit, runOutput, runError) = await RunAsync(input,
                    "frontend-run", frontend, "-", "--runtime", policy, "--json");
                var (jitExit, jitOutput, jitError) = await RunAsync(input,
                    "jit", "-", "--runtime", policy, "--json");
                Assert.That(runExit == expected, Is.True, $"{policy}: {runExit}: {runOutput}{runError}");
                Assert.That(runExit, Is.EqualTo(jitExit));
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(nativePath).ConfigureAwait(true);
            File.Delete(hostedPath);
        }
    }
    [TestCase("int32 Main() { int32 value = 1; value = _; return 42; }",42,60)]
    [TestCase("int32 Main() { mut int32 value = 1; value = _; value = 42; return value; }",42,60)]
    [TestCase("int32 Main() { if (true) { int32 value = 1; value = _; } return 42; }",42,60)]
    [TestCase("[NoThrow] int32 Main() { int32 value = 1; value = _; return 42; }",42,60)]
    [TestCase("int32 Main() { for (int32 value = 1; false; value = _) { } return 42; }",42,60)]
    [TestCase("void Main() { int32 value = 1; value = _; }",0,60)]
    [TestCase("int32 Main() { init int32 value; value = 42; return value; }",42,61)]
    [TestCase("int32 Main() { init int32 value; value = 42; value = _; value = 24; return value + 18; }",42,61)]
    [TestCase("[NoThrow] int32 Main() { init int32 value; value = 42; return value; }",42,61)]
    [TestCase("int32 Main() { for (init int32 value; true;) { value = 42; return value; } }",42,61)]
    [TestCase("int32 Main() { init uint32 value; value = 42; return int32(value); }",42,61)]
    [TestCase("int32 Main() { init int64 value; value = 42; return int32(value); }",42,61)]
    [TestCase("int32 Main() { init uint64 value; value = 42; return int32(value); }",42,61)]
    [TestCase("int32 Main() { init bool value; value = true; if (value) { return 42; } return 0; }",42,61)]
    [TestCase("int32 Main() { init char16 value; value = char16(42); return int32(value); }",42,61)]
    [TestCase("int32 Main() { init var value; value = int32(42); return value; }",42,62)]
    [TestCase("int32 Main() { init var value; value = uint32(42); return int32(value); }",42,62)]
    [TestCase("int32 Main() { init var value; value = int64(42); return int32(value); }",42,62)]
    [TestCase("int32 Main() { init var value; value = uint64(42); return int32(value); }",42,62)]
    [TestCase("int32 Main() { init var value; value = char16(42); return int32(value); }",42,62)]
    [TestCase("int32 Main() { init var value; value = true; if (value) { return 42; } return 0; }",42,62)]
    [TestCase("int32 Main() { int32 seed = 24; init var value; value = seed; return value + 18; }",42,62)]
    [TestCase("int32 Main() { init var value; value = int32(42); value = _; value = int32(24); return value + 18; }",42,62)]
    [TestCase("int32 Main() { for (init var value; true;) { value = int32(42); return value; } }",42,62)]
    [TestCase("int32 Main() { const int32 value = 42; return value; }",42,63)]
    [TestCase("int32 Main() { const uint32 value = 42; return int32(value); }",42,63)]
    [TestCase("int32 Main() { const int64 value = 42; return int32(value); }",42,63)]
    [TestCase("int32 Main() { const uint64 value = 42; return int32(value); }",42,63)]
    [TestCase("int32 Main() { const uint64 value = 18446744073709551615; return value == 18446744073709551615 ? 42 : 0; }",42,63)]
    [TestCase("int32 Main() { const bool value = true; if (value) { return 42; } return 0; }",42,63)]
    [TestCase("int32 Main() { const int32 value = true ? 42 : int32(1) / int32(0); return value; }",42,64)]
    [TestCase("int32 Main() { int32 seed = 0; const int32 value = true ? 42 : seed; return value; }",42,64)]
    [TestCase("int32 Main() { const char16 value = 42; return int32(value); }",42,63)]
    [TestCase("int32 Main() { const bool value = !false; if (value) { return 42; } return 0; }",42,64)]
    [TestCase("int32 Main() { const bool value = true && !false; if (value) { return 42; } return 0; }",42,64)]
    [TestCase("int32 Main() { const bool value = true || false; if (value) { return 42; } return 0; }",42,64)]
    [TestCase("int32 Main() { const bool value = true == false; if (!value) { return 42; } return 0; }",42,64)]
    [TestCase("int32 Main() { const bool value = false != true; if (value) { return 42; } return 0; }",42,64)]
    [TestCase("int32 Main() { const bool value = true ^ false; if (value) { return 42; } return 0; }",42,64)]
    [TestCase("int32 Main() { const bool value = int32(40) < int32(42); if (value) { return 42; } return 0; }",42,64)]
    [TestCase("int32 Main() { const bool value = int32(42) <= int32(42); if (value) { return 42; } return 0; }",42,64)]
    [TestCase("int32 Main() { const bool value = int32(43) > int32(42); if (value) { return 42; } return 0; }",42,64)]
    [TestCase("int32 Main() { const bool value = int32(42) >= int32(42); if (value) { return 42; } return 0; }",42,64)]
    [TestCase("int32 Main() { const int32 value = 6 * 7; return value; }",42,64)]
    [TestCase("int32 Main() { const int32 value = 84 / 2; return value; }",42,64)]
    [TestCase("int32 Main() { const int32 value = 42 % 43; return value; }",42,64)]
    [TestCase("int32 Main() { const int32 value = 43 & 42; return value; }",42,64)]
    [TestCase("int32 Main() { const int32 value = 42 | 0; return value; }",42,64)]
    [TestCase("int32 Main() { const int32 value = 43 ^ 1; return value; }",42,64)]
    [TestCase("int32 Main() { const int32 value = 21 << 1; return value; }",42,64)]
    [TestCase("int32 Main() { const int32 value = 84 >> 1; return value; }",42,64)]
    [TestCase("int32 Main() { const int32 value = 84 >>> 1; return value; }",42,64)]
    [TestCase("int32 Main() { const int32 value = ~(-43); return value; }",42,64)]
    [TestCase("int32 Main() { const int32 value = 40 + 2; return value; }",42,64)]
    [TestCase("int32 Main() { const int32 value = int32(uint64(42)); return value; }",42,64)]
    [TestCase("int32 Main() { const int32 value = -42 + 84; return value; }",42,64)]
    [TestCase("int32 Main() { const int32 first = 40 + 2; const int32 value = first + 1; return value; }",43,64)]
    [TestCase("int32 Main() { { const int32 value = 40 + 2; return value; } return 0; }",42,64)]
    [TestCase("int32 Main() { mut int32 value = 1; try { value = 42; } finally { value = 0; } return value; }",0,65)]
    [TestCase("int32 Main() { mut int32 value = 42; try { return value; } finally { value = 0; } }",42,65)]
    [TestCase("int32 Main() { mut int32 value = 0; while (true) { try { value = 42; break; } finally { value = 0; } } return value; }",0,65)]
    [TestCase("int32 Main() { mut int32 value = 42; try { try { return value; } finally { value = 1; } } finally { value = 0; } }",42,65)]
    public async Task ScopedLocalProjectionMatchesHostedExecutionAsync(string source, int expected,
        int projectionVersion)
    {
        var root = RepositoryRoot();
        var frontend = TerrumProjectPaths.SourceDirectory(root);
        var nativePath = CreateTestArtifactPath($"lifetime-end-{Guid.NewGuid():N}.native.exe");
        var hostedPath = CreateTestArtifactPath($"lifetime-end-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (componentExit, componentOutput, componentError) = await RunAsync(input,
                    "frontend-component", frontend, "-", "--runtime", policy, "--json");
                Assert.That(componentExit == 0, Is.True, $"{policy}: {componentExit}: {componentOutput}{componentError}");
                using var document = JsonDocument.Parse(componentOutput);
                Assert.That(document.RootElement.GetProperty("result")
                    .GetProperty("projectionVersion").GetInt32(), Is.Not.EqualTo(projectionVersion));
                Assert.That(document.RootElement.GetProperty("result")
                    .GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));

                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build", frontend, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.That(nativeExit == 0, Is.True, $"{policy}: {nativeOutput}{nativeError}");
                Assert.That(hostedExit == 0, Is.True, $"{policy}: {hostedOutput}{hostedError}");
                if (!source.Contains("const ", StringComparison.Ordinal)
                    && !source.Contains("try ", StringComparison.Ordinal))
                {
                    Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
                }
                Assert.That(await ExecuteNativeImageAsync(nativePath), Is.EqualTo(expected));
                Assert.That(await ExecuteNativeImageAsync(hostedPath), Is.EqualTo(expected));

                var (runExit, runOutput, runError) = await RunAsync(input,
                    "frontend-run", frontend, "-", "--runtime", policy, "--json");
                var (jitExit, jitOutput, jitError) = await RunAsync(input,
                    "jit", "-", "--runtime", policy, "--json");
                Assert.That(runExit == expected, Is.True, $"{policy}: {runExit}: {runOutput}{runError}");
                Assert.That(runExit, Is.EqualTo(jitExit));
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(nativePath).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(hostedPath).ConfigureAwait(true);
        }
    }
    [TestCase("int32 Main() { unsafe { try { require false; } catch { return 42; } } return 0; }",42,66)]
    [TestCase("int32 Main() { mut int32 value = 0; unsafe { try { try { require false; } finally { value = 1; } } catch { return value + 41; } } return 0; }",42,66)]
    [TestCase("int32 Main() { mut int32 value = 0; unsafe { try { value = 1; } catch { value = 0; } finally { value = 42; } } return value; }",42,67)]
    [TestCase("int32 Main() { mut int32 value = 0; unsafe { try { require false; } catch { value = 1; } finally { value = 42; } } return value; }",42,67)]
    public async Task CheckedCatchProjectionMatchesHostedExecutionAsync(string source, int expected,
        int projectionVersion)
    {
        var root = RepositoryRoot();
        var frontend = TerrumProjectPaths.SourceDirectory(root);
        var nativePath = CreateTestArtifactPath($"checked-catch-{Guid.NewGuid():N}.native.exe");
        var hostedPath = CreateTestArtifactPath($"checked-catch-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            var input = Encoding.UTF8.GetBytes(source);
            var (componentExit, componentOutput, componentError) = await RunAsync(input,
                "frontend-component", frontend, "-", "--runtime", "checked-exceptions", "--json");
            Assert.That(componentExit == 0, Is.True, $"checked-exceptions: {componentExit}: {componentOutput}{componentError}");
            using var document = JsonDocument.Parse(componentOutput);
            Assert.That(document.RootElement.GetProperty("result")
                .GetProperty("projectionVersion").GetInt32(), Is.Not.EqualTo(projectionVersion));
            Assert.That(document.RootElement.GetProperty("result")
                .GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));

            var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                "frontend-build", frontend, "-", "-o", nativePath, "--runtime", "checked-exceptions", "--json");
            var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                "build", "-", "-o", hostedPath, "--runtime", "checked-exceptions", "--json");
            Assert.That(nativeExit == 0, Is.True, $"checked-exceptions: {nativeOutput}{nativeError}");
            Assert.That(hostedExit == 0, Is.True, $"checked-exceptions: {hostedOutput}{hostedError}");
            Assert.That(await ExecuteNativeImageAsync(nativePath), Is.EqualTo(expected));
            Assert.That(await ExecuteNativeImageAsync(hostedPath), Is.EqualTo(expected));

            var (runExit, runOutput, runError) = await RunAsync(input,
                "frontend-run", frontend, "-", "--runtime", "checked-exceptions", "--json");
            var (jitExit, jitOutput, jitError) = await RunAsync(input,
                "jit", "-", "--runtime", "checked-exceptions", "--json");
            Assert.That(runExit == expected, Is.True, $"checked-exceptions: {runExit}: {runOutput}{runError}");
            Assert.That(runExit, Is.EqualTo(jitExit));

            var (failFastExit, failFastOutput, failFastError) = await RunAsync(input,
                "frontend-build", frontend, "-", "-o", nativePath, "--runtime", "fail-fast", "--json");
            Assert.That(failFastExit != 0, Is.True, $"{failFastOutput}{failFastError}");
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(nativePath).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(hostedPath).ConfigureAwait(true);
        }
    }
    [TestCase("public int32 Value() { mut int32 state = 0; unsafe { try { state = 1; } catch { state = 0; } finally { if (state == 1) { state = 42; } else { state = 0; } } } return state; }",42)]
    [TestCase("public int32 Value() { mut int32 state = 0; unsafe { try { require false; } catch { state = 1; } finally { if (state == 1) { state = 42; } else { state = 0; } } } return state; }",42)]
    [TestCase("public int32 Value() { mut int32 state = 0; unsafe { try { try { require false; } finally { state = 1; } } catch { return state + 41; } } return 0; }",42)]
    public async Task CheckedCatchFinallyLibraryMatchesHostedBytesAndExecutionAsync(string source,
        int expected)
    {
        var root = RepositoryRoot();
        var frontend = TerrumProjectPaths.SourceDirectory(root);
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"checked-catch-library-{Guid.NewGuid():N}");
        var frontendPath = prefix + ".frontend.dll";
        var hostedPath = prefix + ".hosted.dll";
        try
        {
            Assert.That(File.Exists(frontendPath), Is.False);
            Assert.That(File.Exists(hostedPath), Is.False);

            var input = Encoding.UTF8.GetBytes(source);
            var (frontendExit, frontendOutput, frontendError) = await RunAsync(input,
                "frontend-build-library", frontend, "-", "-o", frontendPath,
                "--runtime", "checked-exceptions", "--json");
            var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                "build-library", "-", "--export", "Value", "-o", hostedPath,
                "--runtime", "checked-exceptions", "--json");

            Assert.That(frontendExit == 0, Is.True, $"frontend-build-library: {frontendOutput}{frontendError}");
            Assert.That(hostedExit == 0, Is.True, $"build-library: {hostedOutput}{hostedError}");
            Assert.That(frontendError, Is.Empty);
            Assert.That(hostedError, Is.Empty);
            using (var document = JsonDocument.Parse(frontendOutput))
            {
                var result = document.RootElement;
                Assert.That(result.GetProperty("success").GetBoolean(), Is.True);
                Assert.That(result.GetProperty("output").GetString(), Is.EqualTo(frontendPath));
                Assert.That(result.GetProperty("result").GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
            }
            using (var document = JsonDocument.Parse(hostedOutput))
            {
                var result = document.RootElement;
                Assert.That(result.GetProperty("success").GetBoolean(), Is.True);
                var artifact = result.GetProperty("artifact");
                Assert.That(artifact.GetProperty("path").GetString(), Is.EqualTo(hostedPath));
                Assert.That(artifact.GetProperty("target").GetString(), Is.EqualTo("windows-x64"));
            }

            Assert.That(await File.ReadAllBytesAsync(frontendPath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
            foreach (var libraryPath in new[] { frontendPath, hostedPath })
            {
                var handle = NativeLibrary.Load(libraryPath);
                try
                {
                    var export = NativeLibrary.GetExport(handle, "Value");
                    var value = Marshal.GetDelegateForFunctionPointer<ScopedInt32>(export)();
                    Assert.That(value, Is.EqualTo(expected));
                }
                finally
                {
                    NativeLibrary.Free(handle);
                }
            }
        }
        finally
        {
            File.Delete(frontendPath);
            File.Delete(hostedPath);
        }
    }
    [TestCase("int32 Main() { if (true) { int32 x = 1 / 0; return x; } return 42; }",unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { while (true) { mut int32 x = 2147483647; x++; break; } return 42; }",unchecked((int)0xE0540001))]
    public async Task ScopedLocalsPreserveCheckedFailureStatusAsync(string source, int expected)
    {
        var root = RepositoryRoot();
        var nativePath = CreateTestArtifactPath($"scoped-{Guid.NewGuid():N}.native.exe");
        var hostedPath = CreateTestArtifactPath($"scoped-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build", TerrumProjectPaths.SourceDirectory(root), "-", "-o", nativePath,
                    "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.That(nativeExit == 0, Is.True, $"{policy}: {nativeOutput}{nativeError}");
                Assert.That(hostedExit == 0, Is.True, $"{policy}: {hostedOutput}{hostedError}");
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
                Assert.That(await ExecuteNativeImageAsync(nativePath), Is.EqualTo(expected));
                Assert.That(await ExecuteNativeImageAsync(hostedPath), Is.EqualTo(expected));
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(nativePath).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(hostedPath).ConfigureAwait(true);
        }
    }
    [TestCase("int32 Main() { if (true) { var x = 1; } return x; }")]
    [TestCase("int32 Main() { if (true) { var x = 1; } else { return x; } return 42; }")]
    [TestCase("int32 Main() { if (true) { var x = x; } return 42; }")]
    [TestCase("int32 Main() { if (true) { int32 x = x; } return 42; }")]
    [TestCase("int32 Main() { if (true) { var x = y; var y = 2; } return 42; }")]
    [TestCase("int32 Main() { if (true) { int32 x = 1; x = 2; } return 42; }")]
    [TestCase("int32 Main() { if (true) { int32 x = 1; x++; } return 42; }")]
    [TestCase("int32 Main() { int32 x = 1; if (true) { int32 x = 2; } return x; }")]
    [TestCase("int32 Main(int32 x) { if (true) { int32 x = 2; } return x; }")]
    [TestCase("int32 Main(int32 value) { value = _; return 42; }")]
    [TestCase("int32 Main() { int32 value = 1; value = _; return value; }")]
    [TestCase("int32 Main() { int32 value = 1; value = _; value = 42; return value; }")]
    [TestCase("int32 Main() { if (true) { int32 value = 1; } value = _; return 42; }")]
    [TestCase("int32 Main() { init int32 value; return value; }")]
    [TestCase("int32 Main() { init int32 value; value = 1; value = 2; return value; }")]
    [TestCase("int32 Main() { init var value; return value; }")]
    [TestCase("int32 Main() { init var value; value = 42; return value; }")]
    [TestCase("int32 Main() { init var value; int32 copy = value; value = int32(42); return copy; }")]
    [TestCase("int32 Main() { init var value; value = true; value = int32(42); return value; }")]
    [TestCase("int32 Main(int32 seed) { const int32 value = 40 + seed; return value; }")]
    [TestCase("int32 Main() { int32 seed = 0; const int32 value = true ? seed : 42; return value; }")]
    [TestCase("int32 Main() { const int32 value = 2147483647 + 1; return value; }")]
    [TestCase("int32 Main() { const int32 value = 42; value = 43; return value; }")]
    [TestCase("int32 Main() { for (const int32 value = 42; false;) { } return 42; }")]
    [TestCase("int32 Main() { try { return 42; } catch { return 0; } }")]
    [TestCase("int32 Main() { unsafe { try { return 42; } catch (int32 failure) { return 0; } } }")]
    [TestCase("int32 Main() { try { return 42; } finally { return 0; } }")]
    [TestCase("int32 Main() { try { return 42; } finally { require false; } }")]
    [TestCase("int32 Main() { try { int32 hidden = 42; } finally { int32 copy = hidden; } return 0; }")]
    [TestCase("int32 Main() { if (true) { int32 x = 1; } while (false) { return x; } return 42; }")]
    [TestCase("int32 Main() { if (true) { int32 x = 1; } else { int32 y = x; } return 42; }")]
    [TestCase("int32 Main() { if (true) { int32 int32 = 1; return int32(uint64(42)); } return 0; }")]
    public async Task ScopedLocalsRejectInactiveOrDuplicateNamesAtomicallyAsync(string source)
    {
        var root = RepositoryRoot();
        var frontend = TerrumProjectPaths.SourceDirectory(root);
        var path = CreateTestArtifactPath($"scoped-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, _, _) = await RunAsync(
                    Encoding.UTF8.GetBytes("int32 Main() { return 42; }"),
                    "frontend-build", frontend, "-", "-o", path, "--runtime", policy, "--json");
                Assert.That(initial, Is.EqualTo(0));
                var original = await File.ReadAllBytesAsync(path);
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", frontend, "-", "-o", path, "--runtime", policy, "--json");
                Assert.That(exit == 1, Is.True, $"{policy}: {exit}: {output}{error}");
                Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ScopedLocalIdentityBudgetRemainsEightAcrossScopesAsync()
    {
        var frontend = TerrumProjectPaths.SourceDirectory(RepositoryRoot());
        var first = string.Concat(Enumerable.Range(0, 4).Select(i => $"int32 x{i} = {i}; "));
        var eight = first;
        var nine = first + "int32 x4 = 4; ";
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            foreach (var (body, expected) in new[] { (eight, 8), (nine, -1) })
            {
                var source = $"int32 Main() {{ if (false) {{ {first} }} else {{ {body} }} return 42; }}";
                Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length <= 64, Is.True);
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-component", frontend, "-", "--runtime", policy, "--json");
                Assert.That(exit, Is.EqualTo(expected == -1 ? 1 : 0));
                Assert.That(error, Is.Empty);
                using var document = JsonDocument.Parse(output);
                if (expected != -1)
                {
                    var result = document.RootElement.GetProperty("result");
                    Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                    Assert.That(result.GetProperty("locals").GetArrayLength(), Is.EqualTo(expected));
                }
            }
        }
    }
    [TestCase("int32","42",42)]
    [TestCase("uint32","42",42)]
    [TestCase("int64","42",42)]
    [TestCase("uint64","42",42)]
    [TestCase("char16","42",42)]
    [TestCase("bool","true",1)]
    public async Task ScopedSixScalarsExportAndExecuteAsync(string type, string value, int expected)
    {
        var root = RepositoryRoot();
        var frontend = TerrumProjectPaths.SourceDirectory(root);
        var nativePath = CreateTestArtifactPath($"scoped-{Guid.NewGuid():N}.native.dll");
        var hostedPath = CreateTestArtifactPath($"scoped-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(
                    $"public {type} Value() {{ if (true) {{ {type} x = {value}; var y = x; return y; }} return {value}; }}");
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build-library", frontend, "-", "-o", nativePath,
                    "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build-library", "-", "--export", "Value", "-o", hostedPath,
                    "--runtime", policy, "--json");
                Assert.That(nativeExit == 0, Is.True, $"{type}: {nativeOutput}{nativeError}");
                Assert.That(hostedExit == 0, Is.True, $"{type}: {hostedOutput}{hostedError}");
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
                var handle = NativeLibrary.Load(nativePath);
                try
                {
                    var export = NativeLibrary.GetExport(handle, "Value");
                    var actual = type switch {
                        "int32" => Marshal.GetDelegateForFunctionPointer<ScopedInt32>(export)(),
                        "uint32" => (long)Marshal.GetDelegateForFunctionPointer<ScopedUInt32>(export)(),
                        "int64" => Marshal.GetDelegateForFunctionPointer<ScopedInt64>(export)(),
                        "uint64" => (long)Marshal.GetDelegateForFunctionPointer<ScopedUInt64>(export)(),
                        "char16" => Marshal.GetDelegateForFunctionPointer<ScopedChar16>(export)(),
                        "bool" => Marshal.GetDelegateForFunctionPointer<ScopedBool>(export)() ? 1 : 0,
                        _ => throw new InvalidOperationException("Unknown scalar type."),
                    };
                    Assert.That(actual, Is.EqualTo(expected));
                }
                finally
                {
                    NativeLibrary.Free(handle);
                }
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ScopedInt32();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint ScopedUInt32();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate long ScopedInt64();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate ulong ScopedUInt64();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate ushort ScopedChar16();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool ScopedBool();
}
