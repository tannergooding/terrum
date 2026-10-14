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

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [TestCase("require true; return 42;",42)]
    [TestCase("require false; return 42;",unchecked((int)0xE0540003))]
    [TestCase("require int32(-1) < int32(42); return 42;",42)]
    [TestCase("require uint32(4294967295) > uint32(42); return 42;",42)]
    [TestCase("require int64(-9223372036854775808) < int64(42); return 42;",42)]
    [TestCase("require uint64(18446744073709551615) > uint64(42); return 42;",42)]
    [TestCase("require char16(65535) != char16(42); return 42;",42)]
    [TestCase("bool b = true; require b == true; return 42;",42)]
    [TestCase("mut int32 x = 40; require ++x == 41; return x + 1;",42)]
    [TestCase("mut int32 x = 40; require x++ == 40; return x + 1;",42)]
    [TestCase("mut int32 x = 40; require x++ + ++x == 82; return x;",42)]
    [TestCase("mut int32 x = 41; require true || ++x == 0; return x + 1;",42)]
    [TestCase("mut int32 x = 41; require !(false && ++x == 0); return x + 1;",42)]
    [TestCase("mut int32 x = 40; require true ? ++x == 41 : ++x == 0; return x + 1;",42)]
    [TestCase("mut int32 x = 40; require false ? ++x == 0 : ++x == 41; return x + 1;",42)]
    [TestCase("if (true) { require true; return 42; } else { require false; return 0; }",42)]
    [TestCase("mut int32 x = 0; while (x < 2) { require ++x <= 2; } return x + 40;",42)]
    [TestCase("mut int32 x = 0; for (; x < 2; x++) { do { require true; } while (false); } return x + 40;",42)]
    [TestCase("{ _ = int32(1); require true; } return 42;",42)]
    [TestCase("mut int32 x = 0; @const: do { { _ = x++; require x == 1; while (true) { break const; } } } while (false); return x + 41;",42)]
    [TestCase("/*\U0001F600*/ mut int32 名 = 41; require ++名 == 42; return 名;",42)]
    [TestCase("mut int32 @require = 41; require ++@require == 42; return @require;",42)]
    [TestCase("require false && int32(1) / int32(0) == int32(0); return 42;",unchecked((int)0xE0540003))]
    [TestCase("require int32(1) / int32(0) == int32(0); return 42;",unchecked((int)0xE0540002))]
    [TestCase("require int32(2147483647) + int32(1) == int32(0); return 42;",unchecked((int)0xE0540001))]
    [TestCase("require int32(uint64(4294967296)) == int32(0); return 42;",unchecked((int)0xE0540001))]
    public async Task NativeScalarRequireMatchesHostedScannerPeAndJitAsync(string body, int expected)
    {
        var source = $"int32 Main() {{ {body} }}";
        Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length <= 64, Is.True);
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"require-{Guid.NewGuid():N}.native.exe");
        var hosted = Path.Combine(root, $"require-{Guid.NewGuid():N}.hosted.exe");
        var scanner = Path.Combine(root, $"require-{Guid.NewGuid():N}.scanner.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (exit, output, error) = await RunAsync(input, "frontend-build",
                    Path.Combine(root, "self-host"), "-", "-o", native, "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{policy}: {output}{error}");
                using var document = JsonDocument.Parse(output);
                Assert.That(document.RootElement.GetProperty("result").GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                var (hostExit, hostOutput, hostError) = await RunAsync(input, "build",
                    "-", "-o", hosted, "--runtime", policy, "--json");
                Assert.That(hostExit == 0, Is.True, $"{hostOutput}{hostError}");
                var (scanExit, scanOutput, scanError) = await RunAsync(input, "build-component",
                    Path.Combine(root, "self-host"), "-", "-o", scanner,
                    "--runtime", policy, "--json");
                Assert.That(scanExit == 0, Is.True, $"{scanOutput}{scanError}");
                Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                Assert.That(await File.ReadAllBytesAsync(scanner), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                Assert.That(await ExecuteNativeImageAsync(native), Is.EqualTo(expected));
                var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                    Path.Combine(root, "self-host"), "-", "--runtime", policy, "--json");
                Assert.That(runExit == expected, Is.True, $"{policy}: {runOutput}{runError}");
                var (jitExit, jitOutput, jitError) = await RunAsync(input, "jit",
                    "-", "--runtime", policy, "--json");
                Assert.That(jitExit == expected, Is.True, $"{policy}: {jitOutput}{jitError}");
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(native).ConfigureAwait(true);
            File.Delete(hosted);
            File.Delete(scanner);
        }
    }
    [TestCase("static require true; return 42;")]
    [TestCase("const bool proven = true; static require proven; return 42;")]
    [TestCase("const bool proven = true ? true : false; static require proven; return 42;")]
    [TestCase("static require true || false; return 42;")]
    [TestCase("static require true ? true : false; return 42;")]
    [TestCase("static require true ? true : int32(1) / int32(0) == 0; return 42;")]
    [TestCase("bool value = false; static require true || value; return 42;")]
    [TestCase("static require true || int32(1) / int32(0) == 0; return 42;")]
    [TestCase("static ensure true && (int32(42) == int32(42)); return 42;")]
    [TestCase("static ensure false ? false : true; return 42;")]
    public async Task NativeScalarStaticProofsMatchHostedScannerPeAndJitAsync(string body)
    {
        var source = $"int32 Main() {{ {body} }}";
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"static-proof-{Guid.NewGuid():N}.native.exe");
        var hosted = Path.Combine(root, $"static-proof-{Guid.NewGuid():N}.hosted.exe");
        var scanner = Path.Combine(root, $"static-proof-{Guid.NewGuid():N}.scanner.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (exit, output, error) = await RunAsync(input, "frontend-build",
                    Path.Combine(root, "self-host"), "-", "-o", native, "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{policy}: {output}{error}");
                using var document = JsonDocument.Parse(output);
                Assert.That(document.RootElement.GetProperty("result")
                    .GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));

                var (hostExit, hostOutput, hostError) = await RunAsync(input, "build",
                    "-", "-o", hosted, "--runtime", policy, "--json");
                Assert.That(hostExit == 0, Is.True, $"{hostOutput}{hostError}");
                var (scanExit, scanOutput, scanError) = await RunAsync(input, "build-component",
                    Path.Combine(root, "self-host"), "-", "-o", scanner,
                    "--runtime", policy, "--json");
                Assert.That(scanExit == 0, Is.True, $"{scanOutput}{scanError}");
                Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                Assert.That(await File.ReadAllBytesAsync(scanner), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                Assert.That(await ExecuteNativeImageAsync(native), Is.EqualTo(42));

                var (jitExit, jitOutput, jitError) = await RunAsync(input, "jit",
                    "-", "--runtime", policy, "--json");
                Assert.That(jitExit == 42, Is.True, $"{policy}: {jitOutput}{jitError}");
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(native).ConfigureAwait(true);
            File.Delete(hosted);
            File.Delete(scanner);
        }
    }
    [TestCase("require 1; return 42;")]
    [TestCase("require int32(1); return 42;")]
    [TestCase("require null; return 42;")]
    [TestCase("require (); return 42;")]
    [TestCase("require Missing(); return 42;")]
    [TestCase("require 1 == 1; return 42;")]
    [TestCase("require true, 7; return 42;")]
    [TestCase("require true else 7; return 42;")]
    [TestCase("static require false; return 42;")]
    [TestCase("static ensure false; return 42;")]
    [TestCase("bool value = true; static require value; return 42;")]
    [TestCase("bool value = true; static require false || value; return 42;")]
    [TestCase("bool value = false; static ensure true || value; return 42;")]
    [TestCase("const bool value = true; static ensure value; return 42;")]
    [TestCase("if (true) { static ensure true; } return 42;")]
    [TestCase("for (require true; false;) {} return 42;")]
    [TestCase("for (; false; require true) {} return 42;")]
    [TestCase("require true")]
    [TestCase("require true;")]
    [TestCase("bool b = false; require b;")]
    [TestCase("require int32(1) == int32(2);")]
    [TestCase("int32 x = 42; { require true; } return missing;")]
    [TestCase("{ int32 x = 42; } require x == 42; return 42;")]
    [TestCase("_ = true; require _; return 42;")]
    [TestCase("require false; BadUnsupported(); return 42;")]
    [TestCase("require true; return 42; } int32 Unused() { require 1;")]
    public async Task NativeScalarRequireRejectsUnsupportedTypingContextsAndRetainsPriorPeAsync(string body)
    {
        var path = Path.Combine(RepositoryRoot(), $"require-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (initial, initialOutput, initialError) = await RunAsync(
                    Encoding.UTF8.GetBytes("int32 Main() { require true; return 42; }"),
                    "frontend-build", Path.Combine(RepositoryRoot(), "self-host"), "-", "-o", path,
                    "--runtime", policy, "--json");
                Assert.That(initial == 0, Is.True, $"{initialOutput}{initialError}");
                var bytes = await File.ReadAllBytesAsync(path);
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes($"int32 Main() {{ {body} }}"),
                    "frontend-build", Path.Combine(RepositoryRoot(), "self-host"), "-", "-o", path,
                    "--runtime", policy, "--json");
                Assert.That(exit == 1, Is.True, $"{policy}: {exit}: {output}{error}");
                Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(bytes));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
    [TestCase("require false;")]
    [TestCase("require !true;")]
    [TestCase("require false && int32(1) / int32(0) == int32(0);")]
    [TestCase("require true ? false : true;")]
    [TestCase("for (;;) { require true; }")]
    [TestCase("a: for (;;) { require true; do { continue a; } while (false); }")]
    public async Task NativeScalarRequireNonFallthroughCompileOnlyAsync(string body)
    {
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"require-{Guid.NewGuid():N}.native.exe");
        var hosted = Path.Combine(root, $"require-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var source = Encoding.UTF8.GetBytes($"int32 Main() {{ {body} }}");
                var (exit, output, error) = await RunAsync(source, "frontend-build",
                    Path.Combine(root, "self-host"), "-", "-o", native, "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{output}{error}");
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build",
                    "-", "-o", hosted, "--runtime", policy, "--json");
                Assert.That(hostExit == 0, Is.True, $"{hostOutput}{hostError}");
                Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
            }
        }
        finally
        {
            File.Delete(native);
            File.Delete(hosted);
        }
    }
    [TestCase("int32","int32(42)",42)]
    [TestCase("uint32","uint32(42)",42)]
    [TestCase("int64","int64(42)",42)]
    [TestCase("uint64","uint64(42)",42)]
    [TestCase("char16","char16(42)",42)]
    [TestCase("bool","true",1)]
    public async Task NativeScalarRequirePreservesAllSixNormalizedLibraryExportsAsync(string type, string value, int expected)
    {
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"require-{Guid.NewGuid():N}.native.dll");
        var hosted = Path.Combine(root, $"require-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            var source = Encoding.UTF8.GetBytes($"public {type} @await() {{ require true; return {value}; }}");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build-library",
                    Path.Combine(root, "self-host"), "-", "-o", native, "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{output}{error}");
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build-library",
                    "-", "--export", "await", "-o", hosted, "--runtime", policy, "--json");
                Assert.That(hostExit == 0, Is.True, $"{hostOutput}{hostError}");
                Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                var handle = NativeLibrary.Load(native);
                try
                {
                    var export = NativeLibrary.GetExport(handle, "await");
                    var actual = type switch {
                        "int32" => Marshal.GetDelegateForFunctionPointer<ScopedInt32>(export)(),
                        "uint32" => (long)Marshal.GetDelegateForFunctionPointer<ScopedUInt32>(export)(),
                        "int64" => Marshal.GetDelegateForFunctionPointer<ScopedInt64>(export)(),
                        "uint64" => (long)Marshal.GetDelegateForFunctionPointer<ScopedUInt64>(export)(),
                        "char16" => Marshal.GetDelegateForFunctionPointer<ScopedChar16>(export)(),
                        "bool" => Marshal.GetDelegateForFunctionPointer<ScopedBool>(export)() ? 1 : 0,
                        _ => throw new InvalidOperationException(type),
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
            File.Delete(native);
            File.Delete(hosted);
        }
    }

    [Test]
    public async Task NativeScalarRequirePreservesThreeParameterAbiAsync()
    {
        var native = Path.Combine(RepositoryRoot(), $"require-{Guid.NewGuid():N}.dll");
        var hosted = Path.Combine(RepositoryRoot(), $"require-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            var source = Encoding.UTF8.GetBytes("public int32 @await(int32 a, int32 b, int32 c) { require a + b + c == 42; return a + b + c; }");
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (exit, output, error) = await RunAsync(source, "frontend-build-library",
                    Path.Combine(RepositoryRoot(), "self-host"), "-", "-o", native, "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{output}{error}");
                var (hostExit, hostOutput, hostError) = await RunAsync(source, "build-library",
                    "-", "--export", "await", "-o", hosted, "--runtime", policy, "--json");
                Assert.That(hostExit == 0, Is.True, $"{hostOutput}{hostError}");
                Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                var handle = NativeLibrary.Load(native);
                try
                {
                    var export = Marshal.GetDelegateForFunctionPointer<DoForThreeScalars>(
                        NativeLibrary.GetExport(handle, "await"));
                    Assert.That(export(10, 20, 12), Is.EqualTo(42));
                }
                finally
                {
                    NativeLibrary.Free(handle);
                }
            }
        }
        finally
        {
            File.Delete(native);
            File.Delete(hosted);
        }
    }
    [TestCase(8,true)]
    [TestCase(9,false)]
    public Task NativeScalarRequireDoesNotConsumeHiddenOrNamedLocalIdentityAsync(int count, bool valid)
        => CheckRequireBoundaryAsync("int32 Main() { "
            + string.Concat(Enumerable.Repeat("_ = true; ", count - 1))
            + "int32 x = 42; require true; return x; }", valid, locals: count);
    [TestCase(6,true)]
    [TestCase(7,false)]
    public Task NativeScalarRequireDoesNotIntroduceSourceStructuralDepthAsync(int depth, bool valid)
        => CheckRequireBoundaryAsync("int32 Main() { " + new string('{', depth)
            + "require true; return 42;" + new string('}', depth) + " }", valid);
    [TestCase(12,true)]
    [TestCase(13,false)]
    public Task NativeScalarRequireCountsAgainstStatementRecordsAsync(int count, bool valid)
        => CheckRequireBoundaryAsync("int32 Main() { "
            + string.Concat(Enumerable.Repeat("require true; ", count - 1)) + "return 42; }",
            valid, statements: count);
    [TestCase(16,true)]
    [TestCase(17,false)]
    public Task NativeScalarRequireRetainsExpressionRecursionLimitAsync(int depth, bool valid)
        => CheckRequireBoundaryAsync("int32 Main() { require " + new string('(', depth - 1)
            + "true" + new string(')', depth - 1) + "; return 42; }", valid);
    [TestCase(32,true)]
    [TestCase(33,true)]
    public Task NativeScalarRequireAcceptsBeyondPreviousNodeLimitAsync(int count, bool valid)
        => CheckRequireBoundaryAsync("int32 Main() { require " + (count == 33 ? "!" : "")
            + string.Join(" && ", Enumerable.Repeat("true", 16)) + "; return 42; }", valid, nodes: count);
    [TestCase(16384,true)]
    [TestCase(16385,false)]
    public async Task NativeScalarRequireRetainsExactUtf16LimitAsync(int length, bool valid)
    {
        const string prefix = "int32 Main() { require true; return 42; } /*";
        var source = prefix + new string('x', length - prefix.Length - 2) + "*/";
        Assert.That(source.Length, Is.EqualTo(length));
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), "frontend-component",
                Path.Combine(RepositoryRoot(), "self-host"), "-", "--runtime", policy, "--json");
            Assert.That(exit == (valid ? 0 : 2), Is.True, $"{output}{error}");
        }
    }
    [TestCase(64,true)]
    [TestCase(65,true)]
    public async Task NativeScalarRequireAcceptsTokensBeyondThePreviousWindowAsync(int count, bool valid)
    {
        var declarations = string.Concat(Enumerable.Range(0, 7).Select(i => $"int32 x{i} = {i}; "));
        const string expression = "+(((((((+42)))))))";
        var source = $"int32 Main() {{ {declarations}require {(count == 64 ? "true" : "!false")}; return {expression}; }}";
        Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length, Is.EqualTo(count));
        await CheckRequireBoundaryAsync(source, valid, checkTokens: false);
    }

    private static async Task CheckRequireBoundaryAsync(string source, bool valid, bool checkTokens = true,
        int? locals = null, int? statements = null, int? nodes = null)
    {
        if (checkTokens)
        { Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length <= 64, Is.True); }
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), "frontend-component",
                Path.Combine(RepositoryRoot(), "self-host"), "-", "--runtime", policy, "--json");
            Assert.That(exit == (valid ? 0 : 1), Is.True, $"{output}{error}");
            if (valid)
            {
                using var document = JsonDocument.Parse(output);
                var result = document.RootElement.GetProperty("result");
                Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                if (locals is int localCount)
                { Assert.That(result.GetProperty("locals").GetArrayLength(), Is.EqualTo(localCount)); }
                if (statements is int statementCount)
                { Assert.That(result.GetProperty("statements").GetArrayLength(), Is.EqualTo(statementCount)); }
                if (nodes is int nodeCount)
                { Assert.That(result.GetProperty("nodes").GetArrayLength(), Is.EqualTo(nodeCount)); }
            }
        }
    }
}
