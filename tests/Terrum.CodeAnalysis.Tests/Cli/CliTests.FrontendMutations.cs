// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
{
    [TestCase("int32")]
    [TestCase("uint32")]
    [TestCase("int64")]
    [TestCase("uint64")]
    public async Task FrontendAllIntegerCompoundsAndShiftsExecuteAcrossPoliciesAsync(string type)
    {
        var frontend = TerrumProjectPaths.SourceDirectory(RepositoryRoot());
        var cases = new (string Operator, string Operand, int Expected)[] {
            ("+=", "4", 44), ("-=", "2", 38), ("*=", "2", 80),
            ("/=", "2", 20), ("%=", "6", 4), ("&=", "7", 0),
            ("|=", "3", 43), ("^=", "3", 43), ("<<=", "1", 80),
            (">>=", "1", 20), (">>>=", "1", 20),
            ("<<", "1", 80), (">>", "1", 20), (">>>", "1", 20),
            ("<<=", "count", 80), (">>=", "count", 20), (">>>=", "count", 20),
        };
        const int groupSize = 6;
        var groupCount = (cases.Length + groupSize - 1) / groupSize;
        var groupDescriptions = new string[groupCount];
        var sourceBuilder = new StringBuilder();
        for (var index = 0; index < cases.Length; index++)
        {
            var (operation, operand, expected) = cases[index];
            var compound = operation.EndsWith('=');
            var declaration = operand == "count" ? "int32 count = 1; " : string.Empty;
            sourceBuilder.Append($"int32 Check{index}() {{ mut {type} x = 40; {declaration}");
            if (compound)
            {
                sourceBuilder.Append($"x {operation} {operand}; ");
            }

            var expression = compound
                ? $"x == {expected}"
                : $"(x {operation} {operand}) == {expected}";
            sourceBuilder.Append($"return {expression} ? 1 : 0; }} ");
        }

        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            var start = groupIndex * groupSize;
            var length = Math.Min(groupSize, cases.Length - start);
            var checks = new string[length];
            var descriptions = new string[length];
            for (var offset = 0; offset < length; offset++)
            {
                var index = start + offset;
                checks[offset] = $"Check{index}()";
                descriptions[offset] = $"{cases[index].Operator} {cases[index].Operand}";
            }

            sourceBuilder.Append($"int32 Verify{groupIndex}() {{ return {string.Join(" + ", checks)}; }} ");
            groupDescriptions[groupIndex] = string.Join(", ", descriptions);
        }

        sourceBuilder.Append("int32 Main() { ");
        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            var length = Math.Min(groupSize, cases.Length - (groupIndex * groupSize));
            sourceBuilder.Append($"if (Verify{groupIndex}() != {length}) {{ return {groupIndex + 1}; }} ");
        }
        sourceBuilder.Append("return 42; }");
        var input = Encoding.UTF8.GetBytes(sourceBuilder.ToString());
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (projectionExit, projectionOutput, projectionError) = await RunAsync(input,
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.That(projectionExit == 0, Is.True, $"{type} {policy}: {projectionExit}: {projectionOutput}{projectionError}");
            Assert.That(projectionError, Is.Empty);
            using var document = JsonDocument.Parse(projectionOutput);
            Assert.That(document.RootElement.GetProperty("result")
                .GetProperty("projectionVersion").GetInt32(), Is.EqualTo(88));
            var (runExit, runOutput, runError) = await RunAsync(input,
                "frontend-run", frontend, "-", "--runtime", policy, "--json");
            var failedGroup = runExit > 0 && runExit <= groupDescriptions.Length
                ? $"failed group {runExit}: {groupDescriptions[runExit - 1]}; "
                : string.Empty;
            Assert.That(runExit == 42, Is.True, $"{type} {policy}: {runExit}: {failedGroup}{runOutput}{runError}");
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);
        }
    }
    [TestCase("int32")]
    [TestCase("uint32")]
    [TestCase("int64")]
    [TestCase("uint64")]
    public async Task FrontendEveryIntegerUpdateFormMatchesHostedLibraryAsync(string type)
    {
        var root = RepositoryRoot();
        var nativePath = CreateTestArtifactPath($"mutations-{Guid.NewGuid():N}.native.dll");
        var hostedPath = CreateTestArtifactPath($"mutations-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                foreach (var (expression, old, current) in new[] {
                    ("x++", 4, 5), ("++x", 5, 5),
                    ("x--", 4, 3), ("--x", 3, 3),
                })
                {
                    var input = Encoding.UTF8.GetBytes(
                        $"public {type} Value() {{ mut {type} x = 4; var old = {expression};"
                        + $" return old == {old} && x == {current} ? x : 0; }}");
                    var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                        "frontend-build-library", TerrumProjectPaths.SourceDirectory(root), "-", "-o",
                        nativePath, "--runtime", policy, "--json");
                    var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                        "build-library", "-", "--export", "Value", "-o",
                        hostedPath, "--runtime", policy, "--json");
                    Assert.That(nativeExit == 0, Is.True, $"{type} {expression} {policy}: {nativeExit}: {nativeOutput}{nativeError}");
                    Assert.That(hostedExit == 0, Is.True, $"{type} {expression} {policy}: {hostedExit}: {hostedOutput}{hostedError}");
                    Assert.That(nativeError, Is.Empty);
                    Assert.That(hostedError, Is.Empty);
                    Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
                    var handle = NativeLibrary.Load(nativePath);
                    try
                    {
                        var address = NativeLibrary.GetExport(handle, "Value");
                        var actual = type switch {
                            "int32" => Marshal.GetDelegateForFunctionPointer<MutationInt32>(address)(),
                            "uint32" => Marshal.GetDelegateForFunctionPointer<MutationUInt32>(address)(),
                            "int64" => Marshal.GetDelegateForFunctionPointer<MutationInt64>(address)(),
                            "uint64" => (long)Marshal.GetDelegateForFunctionPointer<MutationUInt64>(address)(),
                            _ => throw new InvalidOperationException("Unexpected scalar type."),
                        };
                        Assert.That(actual, Is.EqualTo(current));
                    }
                    finally
                    {
                        NativeLibrary.Free(handle);
                    }
                }
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }
    [TestCase("int32")]
    [TestCase("uint32")]
    [TestCase("int64")]
    [TestCase("uint64")]
    public async Task FrontendShiftExpressionBodyMatchesHostedLibraryAsync(string type)
    {
        var root = RepositoryRoot();
        var nativePath = CreateTestArtifactPath($"mutations-{Guid.NewGuid():N}.native.dll");
        var hostedPath = CreateTestArtifactPath($"mutations-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(
                    $"public {type} Value({type} x, int32 count) => x >>> count;");
                var (projectionExit, projectionOutput, projectionError) = await RunAsync(input,
                    "frontend-component", TerrumProjectPaths.SourceDirectory(root), "-",
                    "--runtime", policy, "--json");
                Assert.That(projectionExit == 0, Is.True, $"{type} {policy}: {projectionExit}: {projectionOutput}{projectionError}");
                Assert.That(projectionError, Is.Empty);
                using var document = JsonDocument.Parse(projectionOutput);
                var result = document.RootElement.GetProperty("result");
                Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                Assert.That(result.GetProperty("expressionBody").GetBoolean(), Is.True);
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build-library", TerrumProjectPaths.SourceDirectory(root), "-", "-o",
                    nativePath, "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build-library", "-", "--export", "Value", "-o",
                    hostedPath, "--runtime", policy, "--json");
                Assert.That(nativeExit == 0, Is.True, $"{type} {policy}: {nativeOutput}{nativeError}");
                Assert.That(hostedExit == 0, Is.True, $"{type} {policy}: {hostedOutput}{hostedError}");
                Assert.That(nativeError, Is.Empty);
                Assert.That(hostedError, Is.Empty);
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }
    [TestCase("int32 Main() { mut int32 x = 4; return ++x + x--; }",10)]
    [TestCase("int32 Main() { mut int32 x = 4; return x++ + --x; }",8)]
    [TestCase("int32 Main() { mut int32 x = 4; x += x++; return x; }",8)]
    [TestCase("int32 Main() { mut int32 x = 4; (x) += x++; return x; }",8)]
    [TestCase("int32 Main() { mut int32 x = 2; ((x)) <<= x++; return x; }",8)]
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 2) { (x) += 1; } return x; }",2)]
    [TestCase("int32 Main() { mut int32 x = 4; x += (x++ + x); return x; }",13)]
    [TestCase("int32 Main() { mut int32 x = 4; x += ++x * 2; return x; }",14)]
    [TestCase("int32 Main() { mut int32 x = 4; x += (true ? x++ : x / 0); return x; }",8)]
    [TestCase("int32 Main() { mut int32 x = 4; x += (false ? x / 0 : x++); return x; }",8)]
    [TestCase("int32 Main() { mut int32 x = 2; x <<= x++; return x; }",8)]
    [TestCase("int32 Main() { mut int32 x = 4; x -= x--; return x; }",0)]
    [TestCase("int32 Main() { mut var x = int32(4); x++; return x; }",5)]
    [TestCase("int32 Main() { mut int32 x = 4; ++x; x--; return x; }",4)]
    [TestCase("int32 Main() { mut int32 x = 4; (x)++; return ++(x); }",6)]
    [TestCase("int32 Main() { mut int32 x = 4; return true ? x++ + x : 1 / 0; }",9)]
    [TestCase("int32 Main() { mut int32 x = 4; return false && x++ == 4 ? 0 : x; }",4)]
    [TestCase("int32 Main() { mut int32 x = 4; return false ? x / 0 : x++ + x; }",9)]
    [TestCase("int32 Main() { mut int32 x = 4; return true || x++ == 0 ? x : 0; }",4)]
    [TestCase("int32 Main() { mut int32 x = 1; x <<= 31; return x >> 31 == -1 && x >>> 31 == 1 ? 42 : 0; }",42)]
    [TestCase("int32 Main() { mut int32 x = 1; return x << 31 == -2147483648 ? 42 : 0; }",42)]
    [TestCase("int32 Main() { mut int64 x = -1; return x >>> 63 == 1 ? 42 : 0; }",42)]
    [TestCase("int32 Main() { mut int64 x = 1; x <<= 63; return x < 0 ? 42 : 0; }",42)]
    [TestCase("int32 Main() { mut uint64 x = 1; return x << 63 == 9223372036854775808 ? 42 : 0; }",42)]
    [TestCase("int32 Main() { mut uint64 x = 1; return x << 32 == 4294967296 ? 42 : 0; }",42)]
    [TestCase("int32 Main() { mut uint32 x = 4294967295; return x >> 31 == 1 && x >>> 31 == 1 ? 42 : 0; }",42)]
    [TestCase("int32 Main() { mut uint64 x = 18446744073709551615; return x >> 63 == 1 && x >>> 63 == 1 ? 42 : 0; }",42)]
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 2) { x += 1; if (x == 1) { continue; } return x + 40; } return 0; }",42)]
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 3) { while (true) { x++; break; } if (x == 2) { continue; } if (x == 3) { return 42; } } return 0; }",42)]
    public async Task FrontendUpdateSnapshotsAndControlFlowMatchHostedAsync(string source, int expected)
    {
        var root = RepositoryRoot();
        var frontend = TerrumProjectPaths.SourceDirectory(root);
        var nativePath = CreateTestArtifactPath($"mutations-{Guid.NewGuid():N}.native.exe");
        var hostedPath = CreateTestArtifactPath($"mutations-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build", frontend, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.That(nativeExit == 0, Is.True, $"frontend-build {policy}: {nativeExit}: {nativeOutput}{nativeError}");
                Assert.That(hostedExit == 0, Is.True, $"build {policy}: {hostedExit}: {hostedOutput}{hostedError}");
                Assert.That(nativeError, Is.Empty);
                Assert.That(hostedError, Is.Empty);
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
                Assert.That(await ExecuteNativeImageAsync(nativePath), Is.EqualTo(expected));
                var (runExit, runOutput, runError) = await RunAsync(input,
                    "frontend-run", frontend, "-", "--runtime", policy, "--json");
                Assert.That(runExit == expected, Is.True, $"frontend-run {policy}: {runExit}: {runOutput}{runError}");
                Assert.That(runOutput, Is.Empty);
                Assert.That(runError, Is.Empty);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(nativePath).ConfigureAwait(true);
            File.Delete(hostedPath);
        }
    }
    [TestCase("int32","2147483647","x++",unchecked((int)0xE0540001))]
    [TestCase("int32","-2147483648","--x",unchecked((int)0xE0540001))]
    [TestCase("uint32","0","x--",unchecked((int)0xE0540001))]
    [TestCase("uint64","0","--x",unchecked((int)0xE0540001))]
    [TestCase("uint32","4294967295","x += 1;",unchecked((int)0xE0540001))]
    [TestCase("int64","9223372036854775807","++x",unchecked((int)0xE0540001))]
    [TestCase("uint64","18446744073709551615","x *= 2;",unchecked((int)0xE0540001))]
    [TestCase("int32","1","x /= 0;",unchecked((int)0xE0540002))]
    [TestCase("int64","-9223372036854775808","x /= -1;",unchecked((int)0xE0540001))]
    [TestCase("int64","-9223372036854775808","x %= -1;",42)]
    [TestCase("int32","-2147483648","x %= -1;",42)]
    [TestCase("int32","1","x <<= -1;",unchecked((int)0xE0540001))]
    [TestCase("int32","1","x >>>= 32;",unchecked((int)0xE0540001))]
    [TestCase("uint32","1","x >>= -1;",unchecked((int)0xE0540001))]
    [TestCase("int64","1","x >>= 64;",unchecked((int)0xE0540001))]
    [TestCase("uint64","1","x >>>= 64;",unchecked((int)0xE0540001))]
    [TestCase("int32","1","return x << -1;",unchecked((int)0xE0540001))]
    [TestCase("int64","1","return int32(x >>> 64);",unchecked((int)0xE0540001))]
    public async Task FrontendMutationsPreserveExactCheckedStatusesAsync(
        string type, string initial, string mutation, int expected)
    {
        var root = RepositoryRoot();
        var path = CreateTestArtifactPath($"mutations-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var statement = mutation.EndsWith(';')
                    ? mutation : $"return {mutation} == {initial} ? 1 : 42;";
                if (mutation.EndsWith(';'))
                {
                    statement += " return 42;";
                }
                var input = Encoding.UTF8.GetBytes(
                    $"int32 Main() {{ mut {type} x = {initial}; {statement} }}");
                var (exit, output, error) = await RunAsync(input,
                    "frontend-build", TerrumProjectPaths.SourceDirectory(root), "-", "-o", path,
                    "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{type} {mutation} {policy}: {exit}: {output}{error}");
                Assert.That(error, Is.Empty);
                Assert.That(await ExecuteNativeImageAsync(path), Is.EqualTo(expected));
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }
    [TestCase("int32 Main() { int32 x = 1; x++; return x; }")]
    [TestCase("int32 Main(int32 x) { ++x; return x; }")]
    [TestCase("int32 Main() { mut bool x = true; x++; return 42; }")]
    [TestCase("int32 Main() { mut char16 x = 1; x += 1; return 42; }")]
    [TestCase("int32 Main() { mut int32 x = 1; x <<= int64(1); return x; }")]
    [TestCase("int32 Main() { mut int64 x = 1; return int32(x << int64(1)); }")]
    [TestCase("int32 Main() { mut int32 x = 1; x += uint32(1); return x; }")]
    [TestCase("int32 Main() { mut int32 x = 1; return x++ + Missing(); }")]
    [TestCase("int32 Main() { mut int32 x = 1; return x.Value++; }")]
    [TestCase("int32 Main() { mut int32 x = 1; return x / 0 + unknown++; }")]
    [TestCase("int32 Main() { mut int32 x = 1; x += 1.5; return x; }")]
    [TestCase("int32 Main() { mut int32 x = 1; return (x + 1)++; }")]
    [TestCase("int32 Main() { mut int32 x = 1; x += int64(2); return x; }")]
    [TestCase("int32 Main() { mut int32 x = 1; return x >>> uint32(1); }")]
    [TestCase("int32 Main() { var x = 1 << 2; return 42; }")]
    [TestCase("int32 Main() { mut int32 count = 2; var x = 1 << count; return x; }")]
    [TestCase("int32 Main() { mut int32 count = 2; _ = 1 << count; return 42; }")]
    [TestCase("int32 Main() { mut int32 count = 2; init var x; x = 1 << count; return x; }")]
    [TestCase("int32 Main() { mut int32 x = 1; (x + 1) += 2; return x; }")]
    [TestCase("int32 Main() { int32 x = 1; (x) += 2; return x; }")]
    [TestCase("int32 Main() { mut int32 x = 4; ((x++)); (++x); return x; }")]
    public async Task FrontendInvalidMutationRetainsOutputAsync(string source)
    {
        var root = RepositoryRoot();
        var path = CreateTestArtifactPath($"mutations-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, initialOutput, initialError) = await RunAsync(
                    Encoding.UTF8.GetBytes("int32 Main() { mut int32 x = 1; return x++; }"),
                    "frontend-build", TerrumProjectPaths.SourceDirectory(root), "-", "-o", path,
                    "--runtime", policy, "--json");
                Assert.That(initial == 0, Is.True, $"{initial}: {initialOutput}{initialError}");
                Assert.That(initialError, Is.Empty);
                var original = await File.ReadAllBytesAsync(path);
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", TerrumProjectPaths.SourceDirectory(root), "-", "-o", path,
                    "--runtime", policy, "--json");
                Assert.That(exit == 1, Is.True, $"{source}: {exit}: {output}{error}");
                Assert.That(error, Is.Empty);
                using var document = JsonDocument.Parse(output);
                Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
                Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
    [TestCase("int32 Main() { int32 x = 1; return x++; }",2,"x++")]
    [TestCase("int32 Main() { mut int32 x = 1; x += uint32(1); return x; }",2,"uint32(1)")]
    [TestCase("int32 Main() { mut int32 x = 1; x <<= int64(1); return x; }",2,"int64(1)")]
    [TestCase("int32 Main() { mut int32 x = 1; x += Missing; return x; }",3,"Missing")]
    public async Task FrontendMutationErrorsRetainDiagnosticKindAndSpanAsync(
        string source, int expectedCode, string offending)
    {
        var frontend = TerrumProjectPaths.SourceDirectory(RepositoryRoot());
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.That(exit == 1, Is.True, $"{source} {policy}: {exit}: {output}{error}");
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            var result = document.RootElement.GetProperty("result");
            Assert.That(result.GetProperty("valid").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("errorCode").GetInt32(), Is.EqualTo(expectedCode));
            var span = result.GetProperty("errorSpan");
            var start = span.GetProperty("start").GetInt32();
            Assert.That(start, Is.EqualTo(source.LastIndexOf(offending, StringComparison.Ordinal)));
            Assert.That(span.GetProperty("length").GetInt32() > 0, Is.True);
        }
    }

    [Test]
    public async Task FrontendMutationsKeepStatementAndNodeEnvelopesAsync()
    {
        var frontend = TerrumProjectPaths.SourceDirectory(RepositoryRoot());
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var withinStatements = "int32 Main() { mut int32 x = 0; "
                + string.Concat(Enumerable.Repeat("x++; ", 11)) + "return x; }";
            var beyondStatements = "int32 Main() { mut int32 x = 0; "
                + string.Concat(Enumerable.Repeat("x++; ", 12)) + "return x; }";
            var withinNodes = "int32 Main() { mut int32 x = 0; return +("
                + string.Join(" + ", Enumerable.Repeat("x++", 20)) + "); }";
            var beyondNodes = "int32 Main() { mut int32 x = 0; return "
                + string.Join(" + ", Enumerable.Repeat("x++", 22)) + "; }";
            foreach (var source in new[] { withinStatements, withinNodes })
            {
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-component", frontend, "-", "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{source}: {exit}: {output}{error}");
                Assert.That(error, Is.Empty);
                using var document = JsonDocument.Parse(output);
                var result = document.RootElement.GetProperty("result");
                Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                Assert.That(result.GetProperty(source == withinNodes ? "nodes" : "statements").GetArrayLength(), Is.EqualTo(source == withinNodes ? 62 : 12));
            }
            foreach (var source in new[] { beyondStatements, beyondNodes })
            {
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-component", frontend, "-", "--runtime", policy, "--json");
                Assert.That(exit == 1, Is.True, $"{source}: {exit}: {output}{error}");
                Assert.That(error, Is.Empty);
                using var document = JsonDocument.Parse(output);
                Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int MutationInt32();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint MutationUInt32();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate long MutationInt64();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate ulong MutationUInt64();

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
