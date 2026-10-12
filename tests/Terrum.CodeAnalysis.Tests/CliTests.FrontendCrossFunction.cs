// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    private static void AssertCurrentScalarFrontendVersion(JsonElement result)
    {
        Assert.Equal(result.TryGetProperty("functions", out _) ? 88 : 87,
            result.GetProperty("projectionVersion").GetInt32());
    }

    [Fact]
    public async Task ScalarFrontendSupportsOneFileScopedNamespaceInCompleteProgramsAsync()
    {
        const string source = "namespace Demo.Tools; alias Scalar = int32; "
            + "Scalar Main() { return Add(20, 22); } "
            + "public Scalar Add(Scalar left, Scalar right) { return left + right; }";
        var root = RepositoryRoot();
        var directory = Path.Combine(root, "self-host");
        var namespaceStart = source.IndexOf("Demo.Tools", StringComparison.Ordinal);
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var input = Encoding.UTF8.GetBytes(source);
            var (scanExit, scanOutput, scanError) = await RunAsync(input,
                "scan-component", directory, "-", "--runtime", policy, "--json");
            Assert.Equal(0, scanExit);
            Assert.Empty(scanError);
            using (var scan = JsonDocument.Parse(scanOutput))
            {
                Assert.Contains(scan.RootElement.GetProperty("tokens").EnumerateArray(),
                    static token => token.GetProperty("kind").GetString() == "NamespaceKeyword");
            }

            var (componentExit, componentOutput, componentError) = await RunAsync(input,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.True(componentExit == 0, $"{componentOutput}{componentError}");
            Assert.Empty(componentError);
            using (var component = JsonDocument.Parse(componentOutput))
            {
                var result = component.RootElement.GetProperty("result");
                AssertCurrentScalarFrontendVersion(result);
                var functions = result.GetProperty("functions");
                Assert.Equal(2, functions.GetArrayLength());
                foreach (var function in functions.EnumerateArray())
                {
                    var span = function.GetProperty("namespaceNameSpan");
                    Assert.Equal(namespaceStart, span.GetProperty("start").GetInt32());
                    Assert.Equal("Demo.Tools".Length, span.GetProperty("length").GetInt32());
                    Assert.StartsWith("Demo.Tools.", function.GetProperty("qualifiedName").GetString(),
                        StringComparison.Ordinal);
                }
            }

            var (frontendExit, frontendOutput, frontendError) = await RunAsync(input,
                "frontend-run", directory, "-", "--runtime", policy, "--json");
            var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                "jit", "-", "--runtime", policy, "--json");
            Assert.Equal(42, frontendExit);
            Assert.Equal(hostedExit, frontendExit);
            Assert.Empty(frontendOutput);
            Assert.Empty(hostedOutput);
            Assert.Empty(frontendError);
            Assert.Empty(hostedError);

            var nativeLibrary = Path.Combine(Path.GetTempPath(),
                $"terrum-namespace-native-{Guid.NewGuid():N}.dll");
            var hostedLibrary = Path.Combine(Path.GetTempPath(),
                $"terrum-namespace-hosted-{Guid.NewGuid():N}.dll");
            try
            {
                var (nativeLibraryExit, _, nativeLibraryError) = await RunAsync(input,
                    "frontend-build-library", directory, "-", "-o", nativeLibrary,
                    "--runtime", policy, "--json");
                var (hostedLibraryExit, _, hostedLibraryError) = await RunAsync(input,
                    "build-library", "-", "--export", "Demo.Tools.Add", "-o", hostedLibrary,
                    "--runtime", policy, "--json");
                Assert.Equal(0, nativeLibraryExit);
                Assert.Equal(0, hostedLibraryExit);
                Assert.Empty(nativeLibraryError);
                Assert.Empty(hostedLibraryError);
                Assert.Equal(await File.ReadAllBytesAsync(hostedLibrary),
                    await File.ReadAllBytesAsync(nativeLibrary));
            }
            finally
            {
                File.Delete(nativeLibrary);
                File.Delete(hostedLibrary);
            }
        }

        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }

    [Fact]
    public async Task ScalarFrontendSupportsOneNamespaceBlockInCompleteProgramsAsync()
    {
        const string source = "namespace Demo.Tools { alias Scalar = int32; "
            + "Scalar Main() { return Add(20, 22); } "
            + "public Scalar Add(Scalar left, Scalar right) { return left + right; } }";
        const string hostedSource = "namespace Demo.Tools; alias Scalar = int32; "
            + "Scalar Main() { return Add(20, 22); } "
            + "public Scalar Add(Scalar left, Scalar right) { return left + right; }";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var namespaceStart = source.IndexOf("Demo.Tools", StringComparison.Ordinal);
        var nativePath = Path.Combine(Path.GetTempPath(),
            $"terrum-namespace-block-{Guid.NewGuid():N}.native.exe");
        var hostedPath = Path.Combine(Path.GetTempPath(),
            $"terrum-namespace-block-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var hostedInput = Encoding.UTF8.GetBytes(hostedSource);
                var (componentExit, componentOutput, componentError) = await RunAsync(input,
                    "frontend-component", directory, "-", "--runtime", policy, "--json");
                Assert.True(componentExit == 0, $"{componentOutput}{componentError}");
                Assert.Empty(componentError);
                using (var component = JsonDocument.Parse(componentOutput))
                {
                    var result = component.RootElement.GetProperty("result");
                    Assert.Equal(88, result.GetProperty("projectionVersion").GetInt32());
                    var functions = result.GetProperty("functions");
                    Assert.Equal(2, functions.GetArrayLength());
                    Assert.Equal(new[] { "Demo.Tools.Main", "Demo.Tools.Add" },
                        functions.EnumerateArray().Select(static function =>
                            function.GetProperty("qualifiedName").GetString()).ToArray());
                    foreach (var function in functions.EnumerateArray())
                    {
                        var span = function.GetProperty("namespaceNameSpan");
                        Assert.Equal(namespaceStart, span.GetProperty("start").GetInt32());
                        Assert.Equal("Demo.Tools".Length, span.GetProperty("length").GetInt32());
                    }
                }

                var (nativeBuildExit, nativeBuildOutput, nativeBuildError) = await RunAsync(input,
                    "frontend-build", directory, "-", "-o", nativePath,
                    "--runtime", policy, "--json");
                var (hostedBuildExit, hostedBuildOutput, hostedBuildError) = await RunAsync(hostedInput,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.True(nativeBuildExit == 0, $"{nativeBuildOutput}{nativeBuildError}");
                Assert.True(hostedBuildExit == 0, $"{hostedBuildOutput}{hostedBuildError}");
                Assert.Empty(nativeBuildError);
                Assert.Empty(hostedBuildError);
                Assert.Equal(await File.ReadAllBytesAsync(hostedPath),
                    await File.ReadAllBytesAsync(nativePath));
                Assert.Equal(42, await ExecuteNativeImageAsync(nativePath));
                Assert.Equal(42, await ExecuteNativeImageAsync(hostedPath));

                var (frontendExit, frontendOutput, frontendError) = await RunAsync(input,
                    "frontend-run", directory, "-", "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(hostedInput,
                    "jit", "-", "--runtime", policy, "--json");
                Assert.Equal(42, frontendExit);
                Assert.Equal(hostedExit, frontendExit);
                Assert.Empty(frontendOutput);
                Assert.Empty(hostedOutput);
                Assert.Empty(frontendError);
                Assert.Empty(hostedError);
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }

    }

    [Fact]
    public async Task ScalarFrontendSupportsNestedNamespaceBlockInCompleteProgramsAsync()
    {
        const string source = "namespace Demo.Tools { namespace Inner { "
            + "alias Scalar = int32; Scalar Main() { Scalar result = Add(20, 22); return result; } "
            + "public Scalar Add(Scalar left, Scalar right) { return left + right; } } }";
        const string hostedSource = "namespace Demo.Tools.Inner; "
            + "alias Scalar = int32; Scalar Main() { Scalar result = Add(20, 22); return result; } "
            + "public Scalar Add(Scalar left, Scalar right) { return left + right; }";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var namespaceStart = source.IndexOf("Inner", StringComparison.Ordinal);
        var nativePath = Path.Combine(Path.GetTempPath(),
            $"terrum-nested-namespace-{Guid.NewGuid():N}.native.exe");
        var hostedPath = Path.Combine(Path.GetTempPath(),
            $"terrum-nested-namespace-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var hostedInput = Encoding.UTF8.GetBytes(hostedSource);
                var (componentExit, componentOutput, componentError) = await RunAsync(input,
                    "frontend-component", directory, "-", "--runtime", policy, "--json");
                Assert.True(componentExit == 0, $"{componentOutput}{componentError}");
                Assert.Empty(componentError);
                using (var component = JsonDocument.Parse(componentOutput))
                {
                    var result = component.RootElement.GetProperty("result");
                    Assert.Equal(88, result.GetProperty("projectionVersion").GetInt32());
                    var functions = result.GetProperty("functions");
                    Assert.Equal(new[] { "Demo.Tools.Inner.Main", "Demo.Tools.Inner.Add" },
                        functions.EnumerateArray().Select(static function =>
                            function.GetProperty("qualifiedName").GetString()).ToArray());
                    foreach (var function in functions.EnumerateArray())
                    {
                        var span = function.GetProperty("namespaceNameSpan");
                        Assert.Equal(namespaceStart, span.GetProperty("start").GetInt32());
                        Assert.Equal("Inner".Length, span.GetProperty("length").GetInt32());
                    }
                }

                var (nativeBuildExit, nativeBuildOutput, nativeBuildError) = await RunAsync(input,
                    "frontend-build", directory, "-", "-o", nativePath,
                    "--runtime", policy, "--json");
                var (hostedBuildExit, hostedBuildOutput, hostedBuildError) = await RunAsync(hostedInput,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.True(nativeBuildExit == 0, $"{nativeBuildOutput}{nativeBuildError}");
                Assert.True(hostedBuildExit == 0, $"{hostedBuildOutput}{hostedBuildError}");
                Assert.Empty(nativeBuildError);
                Assert.Empty(hostedBuildError);
                Assert.Equal(await File.ReadAllBytesAsync(hostedPath),
                    await File.ReadAllBytesAsync(nativePath));
                Assert.Equal(42, await ExecuteNativeImageAsync(nativePath));

                var (frontendExit, frontendOutput, frontendError) = await RunAsync(input,
                    "frontend-run", directory, "-", "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(hostedInput,
                    "jit", "-", "--runtime", policy, "--json");
                Assert.Equal(42, frontendExit);
                Assert.Equal(hostedExit, frontendExit);
                Assert.Empty(frontendOutput);
                Assert.Empty(hostedOutput);
                Assert.Empty(frontendError);
                Assert.Empty(hostedError);
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }

    [Theory]
    [InlineData("alias Scalar = int32; alias Scalar = int64; Scalar Main() { return 42; }")]
    [InlineData("alias Scalar = Missing; Scalar Main() { return 42; }")]
    [InlineData("alias Scalar = Count; alias Count = Scalar; Scalar Main() { return 42; }")]
    public async Task ScalarFrontendRejectsInvalidAliasesInNestedNamespaceLeafAsync(string declarations)
    {
        var source = Encoding.UTF8.GetBytes(
            $"namespace Demo.Tools {{ namespace Inner {{ {declarations} }} }}");
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(source,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.Equal(1, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        }
    }

    [Theory]
    [InlineData(12, true)]
    [InlineData(13, false)]
    public async Task ScalarFrontendPreservesNestedNamespaceAliasLimitAsync(
        int aliasCount, bool expectedSuccess)
    {
        var aliases = string.Join(' ', Enumerable.Range(0, aliasCount)
            .Select(index => $"alias Alias{index} = {(index == 0 ? "int32" : $"Alias{index - 1}")};"));
        var source = Encoding.UTF8.GetBytes(
            $"namespace Demo.Tools {{ namespace Inner {{ {aliases} Alias{aliasCount - 1} Main() {{ return 42; }} }} }}");
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(source,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.Equal(expectedSuccess ? 0 : 1, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal(expectedSuccess, document.RootElement.GetProperty("success").GetBoolean());
            if (expectedSuccess)
            {
                Assert.Equal("Demo.Tools.Inner.Main",
                    document.RootElement.GetProperty("result").GetProperty("functions")[0]
                        .GetProperty("qualifiedName").GetString());
            }
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    public async Task ScalarFrontendSupportsNestedNamespaceBlockChainsAsync(int depth)
    {
        var namespaceNames = Enumerable.Range(0, depth)
            .Select(static index => $"Scope{index}").ToArray();
        var opening = string.Join(' ', namespaceNames.Select(static name => $"namespace {name} {{"));
        var closing = string.Concat(Enumerable.Repeat(" }", depth));
        var text = opening
            + " alias Scalar = int32; Scalar Main() { return Add(20, 22); } "
            + "public Scalar Add(Scalar left, Scalar right) { return left + right; }"
            + closing;
        var source = Encoding.UTF8.GetBytes(text);
        var qualifiedNamespace = string.Join('.', namespaceNames);
        var hostedSource = Encoding.UTF8.GetBytes(
            $"namespace {qualifiedNamespace}; alias Scalar = int32; "
            + "Scalar Main() { return Add(20, 22); } "
            + "public Scalar Add(Scalar left, Scalar right) { return left + right; }");
        var leafStart = text.IndexOf(namespaceNames[^1], StringComparison.Ordinal);
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(source,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            var functions = document.RootElement.GetProperty("result").GetProperty("functions");
            Assert.Equal(new[] { qualifiedNamespace + ".Main", qualifiedNamespace + ".Add" },
                functions.EnumerateArray().Select(static function =>
                    function.GetProperty("qualifiedName").GetString()).ToArray());
            foreach (var function in functions.EnumerateArray())
            {
                var span = function.GetProperty("namespaceNameSpan");
                Assert.Equal(leafStart, span.GetProperty("start").GetInt32());
                Assert.Equal(namespaceNames[^1].Length, span.GetProperty("length").GetInt32());
            }

            var nativePath = Path.Combine(Path.GetTempPath(),
                $"terrum-namespace-chain-{policy}-{Guid.NewGuid():N}.native.exe");
            var hostedPath = Path.Combine(Path.GetTempPath(),
                $"terrum-namespace-chain-{policy}-{Guid.NewGuid():N}.hosted.exe");
            try
            {
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(source,
                    "frontend-build", directory, "-", "-o", nativePath,
                    "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(hostedSource,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.True(nativeExit == 0, $"{nativeOutput}{nativeError}");
                Assert.True(hostedExit == 0, $"{hostedOutput}{hostedError}");
                Assert.Empty(nativeError);
                Assert.Empty(hostedError);
                Assert.Equal(await File.ReadAllBytesAsync(hostedPath),
                    await File.ReadAllBytesAsync(nativePath));
                Assert.Equal(42, await ExecuteNativeImageAsync(nativePath));
            }
            finally
            {
                File.Delete(nativePath);
                File.Delete(hostedPath);
            }
        }
    }

    [Fact]
    public async Task ScalarFrontendRejectsNamespaceBlockChainsBeyondDepthLimitAsync()
    {
        const int depth = 9;
        var opening = string.Join(' ', Enumerable.Range(0, depth)
            .Select(static index => $"namespace Scope{index} {{"));
        var source = Encoding.UTF8.GetBytes(opening
            + " int32 Main() { return 42; } int32 Other() { return 0; }"
            + string.Concat(Enumerable.Repeat(" }", depth)));
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(source,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.Equal(1, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        }
    }

    [Fact]
    public async Task ScalarFrontendSupportsRepeatedSameNameNamespaceBlocksAsync()
    {
        const string source = "namespace /* first */ Demo.\U00010400 { "
            + "int32 Main() { return Add(20, 22); } } "
            + "namespace Demo.\U00010400 { public int32 Add(int32 left, int32 right) "
            + "{ return left + right; } } namespace Demo.\U00010400 { }";
        const string hostedSource = "namespace Demo.\U00010400; "
            + "int32 Main() { return Add(20, 22); } "
            + "public int32 Add(int32 left, int32 right) { return left + right; }";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var firstNamespaceStart = source.IndexOf("Demo.\U00010400", StringComparison.Ordinal);
        var secondNamespaceStart = source.IndexOf("Demo.\U00010400", firstNamespaceStart + 1,
            StringComparison.Ordinal);

        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var input = Encoding.UTF8.GetBytes(source);
            var hostedInput = Encoding.UTF8.GetBytes(hostedSource);
            var (componentExit, componentOutput, componentError) = await RunAsync(input,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.True(componentExit == 0, $"{componentOutput}{componentError}");
            Assert.Empty(componentError);
            using (var component = JsonDocument.Parse(componentOutput))
            {
                var result = component.RootElement.GetProperty("result");
                Assert.Equal(88, result.GetProperty("projectionVersion").GetInt32());
                var functions = result.GetProperty("functions");
                Assert.Equal(new[] { "Demo.\U00010400.Main", "Demo.\U00010400.Add" },
                    functions.EnumerateArray().Select(static function =>
                        function.GetProperty("qualifiedName").GetString()).ToArray());
                var spans = functions.EnumerateArray()
                    .Select(static function => function.GetProperty("namespaceNameSpan")
                        .GetProperty("start").GetInt32())
                    .ToArray();
                Assert.Equal(new[] { firstNamespaceStart, secondNamespaceStart }, spans);
            }

            var (frontendExit, frontendOutput, frontendError) = await RunAsync(input,
                "frontend-run", directory, "-", "--runtime", policy, "--json");
            var (hostedExit, hostedOutput, hostedError) = await RunAsync(hostedInput,
                "jit", "-", "--runtime", policy, "--json");
            Assert.Equal(42, frontendExit);
            Assert.Equal(hostedExit, frontendExit);
            Assert.Empty(frontendOutput);
            Assert.Empty(hostedOutput);
            Assert.Empty(frontendError);
            Assert.Empty(hostedError);

            var nativePath = Path.Combine(Path.GetTempPath(),
                $"terrum-repeated-namespace-{policy}-{Guid.NewGuid():N}.native.exe");
            var hostedPath = Path.Combine(Path.GetTempPath(),
                $"terrum-repeated-namespace-{policy}-{Guid.NewGuid():N}.hosted.exe");
            try
            {
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build", directory, "-", "-o", nativePath,
                    "--runtime", policy, "--json");
                var (hostedBuildExit, hostedBuildOutput, hostedBuildError) = await RunAsync(hostedInput,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.True(nativeExit == 0, $"{nativeOutput}{nativeError}");
                Assert.True(hostedBuildExit == 0, $"{hostedBuildOutput}{hostedBuildError}");
                Assert.Empty(nativeError);
                Assert.Empty(hostedBuildError);
                Assert.Equal(await File.ReadAllBytesAsync(hostedPath),
                    await File.ReadAllBytesAsync(nativePath));
                Assert.Equal(42, await ExecuteNativeImageAsync(nativePath));
                Assert.Equal(42, await ExecuteNativeImageAsync(hostedPath));
            }
            finally
            {
                File.Delete(nativePath);
                File.Delete(hostedPath);
            }
        }
    }

    [Theory]
    [InlineData("namespace Demo.Tools { int32 Main() { return 42; } } namespace Other { int32 Next() { return 0; } }")]
    [InlineData("namespace Demo.Tools { alias Scalar = int32; int32 Main() { return 42; } } namespace Demo.Tools { int32 Next() { return 0; } }")]
    [InlineData("namespace Demo.Tools { alias Scalar = int32; namespace Inner { Scalar Main() { return 42; } } }")]
    [InlineData("namespace Demo.Tools { namespace Other { int32 Main() { return 42; } } int32 Root() { return 0; } }")]
    [InlineData("namespace Demo { namespace Tools { int32 Main() { return 42; } } namespace Other { int32 Next() { return 0; } } }")]
    [InlineData("namespace Demo { int32 Root() { return 0; } namespace Tools { int32 Main() { return 42; } } }")]
    [InlineData("namespace Demo { namespace Tools { int32 Main() { return 42; } } int32 Root() { return 0; } }")]
    [InlineData("namespace Demo.Tools { using Other; int32 Main() { return 42; } }")]
    [InlineData("namespace Demo.Tools { import Other; int32 Main() { return 42; } }")]
    [InlineData("int32 Before() { return 0; } namespace Demo.Tools { int32 Main() { return 42; } }")]
    [InlineData("namespace Demo.Tools { int32 Main() { return 42; } } int32 After() { return 0; }")]
    [InlineData("namespace Demo; namespace Other; int32 Main() { return 42; }")]
    [InlineData("using Demo; namespace Demo; int32 Main() { return 42; }")]
    [InlineData("namespace Demo; using Other; int32 Main() { return 42; }")]
    [InlineData("int32 Before() { return 0; } namespace Demo; int32 Main() { return 42; }")]
    public async Task ScalarFrontendRejectsUnsupportedNamespaceAndImportFormsAsync(string source)
    {
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.Equal(1, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        }
    }

    [Theory]
    [InlineData("void Main() {} void Other() {}", 0)]
    [InlineData("void Main() => require true; void Other() {}", 0)]
    [InlineData("int32 Main() { { return 42; } } int32 Unused() { return 0; }", 42)]
    [InlineData("int32 Main() { _ = int32(1); return 42; } int32 Unused() { return 0; }", 42)]
    [InlineData("void Main() { if (false) {} else if (true) {} } void Other() {}", 0)]
    public async Task ScalarFrontendCurrentProgramIncludesSimpleAndStructuredFunctionsAsync(
        string source, int expected)
    {
        var root = RepositoryRoot();
        var native = Path.Combine(root, $"current-program-{Guid.NewGuid():N}.native.exe");
        var hosted = Path.Combine(root, $"current-program-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (componentExit, componentOutput, componentError) = await RunAsync(input,
                    "frontend-component", Path.Combine(root, "self-host"), "-",
                    "--runtime", policy, "--json");
                Assert.True(componentExit == 0, $"{componentOutput}{componentError}");
                using var component = JsonDocument.Parse(componentOutput);
                var result = component.RootElement.GetProperty("result");
                AssertCurrentScalarFrontendVersion(result);
                Assert.Equal(2, result.GetProperty("functions").GetArrayLength());

                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build", Path.Combine(root, "self-host"), "-", "-o", native,
                    "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build", "-", "-o", hosted, "--runtime", policy, "--json");
                Assert.True(nativeExit == 0, $"{nativeOutput}{nativeError}");
                Assert.True(hostedExit == 0, $"{hostedOutput}{hostedError}");
                Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(native));
                Assert.Equal(expected, await ExecuteNativeImageAsync(native));
                Assert.Equal(expected, await ExecuteNativeImageAsync(hosted));
            }
        }
        finally
        {
            File.Delete(native);
            File.Delete(hosted);
        }
    }

    [Fact]
    public async Task ScalarFrontendProjectsCompleteProgramsBeyondSingleFunctionTokenWindowAsync()
    {
        var emptyReturns = string.Join(" ", Enumerable.Range(0, 9)
            .Select(static index => $"void Empty{index}() {{ return; }}"));
        var source = $"alias Number = int32; Number Main() {{ return 42; }} {emptyReturns}";
        Assert.True(Lexer.Lex(SourceText.From(source)).Tokens.Length > 64);

        var input = Encoding.UTF8.GetBytes(source);
        var (componentExit, componentOutput, componentError) = await RunAsync(input,
            "frontend-component", Path.Combine(RepositoryRoot(), "self-host"), "-",
            "--runtime", "fail-fast", "--json");
        Assert.True(componentExit == 0, $"{componentOutput}{componentError}");
        using (var component = JsonDocument.Parse(componentOutput))
        {
            var result = component.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
            Assert.Equal(10, result.GetProperty("functions").GetArrayLength());
        }

        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }

    [Fact]
    public async Task ScalarFrontendProjectsSingleFunctionsBeyond64TokensWithinBudgetsAsync()
    {
        var castDepth = string.Concat(Enumerable.Repeat("int32(", 11));
        var casts = castDepth + "42" + new string(')', 11);
        var source = $"int32 Main() => true ? {casts} : {casts};";
        Assert.True(Lexer.Lex(SourceText.From(source)).Tokens.Length > 64);

        var input = Encoding.UTF8.GetBytes(source);
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (componentExit, componentOutput, componentError) = await RunAsync(input,
                "frontend-component", Path.Combine(RepositoryRoot(), "self-host"), "-",
                "--runtime", policy, "--json");
            Assert.Equal(0, componentExit);
            Assert.Empty(componentError);
            using var component = JsonDocument.Parse(componentOutput);
            var result = component.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
            Assert.True(result.GetProperty("valid").GetBoolean());
        }

        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }

    [Fact]
    public async Task ScalarFrontendProjectsLongFunctionAlongsideOtherProgramFunctionsAsync()
    {
        var declarations = string.Concat(Enumerable.Range(0, 8)
            .Select(static index => $"mut int32 x{index} = {index}; "));
        var expression = string.Concat(new string('(', 15), "42", new string(')', 15),
            " + 0 + 0 + 0 + 0 + 0 + 0");
        var longFunction = $"int32 Main() {{ {{ {declarations}return {expression} + Zero(); }} }}";
        Assert.True(Lexer.Lex(SourceText.From(longFunction)).Tokens.Length > 64);
        var source = $"int32 Other() => 0; int32 Zero() => 0; {longFunction}";
        Assert.True(Lexer.Lex(SourceText.From(source)).Tokens.Length > 64);

        var input = Encoding.UTF8.GetBytes(source);
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (componentExit, componentOutput, componentError) = await RunAsync(input,
                "frontend-component", Path.Combine(RepositoryRoot(), "self-host"), "-",
                "--runtime", policy, "--json");
            Assert.Equal(0, componentExit);
            Assert.Empty(componentError);
            using var component = JsonDocument.Parse(componentOutput);
            var result = component.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
            var functions = result.GetProperty("functions");
            Assert.Equal(3, functions.GetArrayLength());
            var nodes = functions[2].GetProperty("nodes");
            Assert.InRange(nodes.GetArrayLength(), 33, 64);
            var callNode = Enumerable.Range(0, nodes.GetArrayLength())
                .Single(index => nodes[index].GetProperty("operation").GetInt32() == 105);
            Assert.InRange(callNode, 32, nodes.GetArrayLength() - 1);
        }

        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }

    [Theory]
    [InlineData("int32 Main() { return Shift(21, 1); } int32 Shift(int32 value, int32 count) { return value << count; }")]
    [InlineData("int32 Main() { return Shift(-84, 1) == -42 ? 42 : 0; } int32 Shift(int32 value, int32 count) { return value >> count; }")]
    [InlineData("int32 Main() { return Shift(-1, 1) == 2147483647 ? 42 : 0; } int32 Shift(int32 value, int32 count) { return value >>> count; }")]
    [InlineData("int32 Main() { return Shift(1, 31) == 0x80000000 ? 42 : 0; } uint32 Shift(uint32 value, int32 count) { return value << count; }")]
    [InlineData("int32 Main() { return Shift(-84, 2) == -21 ? 42 : 0; } int64 Shift(int64 value, int32 count) { return value >> count; }")]
    [InlineData("int32 Main() { return Shift(0x8000000000000000, 63) == 1 ? 42 : 0; } uint64 Shift(uint64 value, int32 count) { return value >>> count; }")]
    public async Task ScalarFrontendProjectsCheckedIntegerShiftsAsync(string source)
    {
        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }

    [Theory]
    [InlineData("int32 Main() { return Shift(21) == 42 ? 42 : 0; } int32 Shift(int32 value) { return value << 1; }")]
    [InlineData("int32 Main() { return Shift(1) == 0x80000000 ? 42 : 0; } uint32 Shift(uint32 value) { return value << 31; }")]
    [InlineData("int32 Main() { return Shift(-1) == 1 ? 42 : 0; } int64 Shift(int64 value) { return value >>> 63; }")]
    [InlineData("int32 Main() { return Shift(1) == 0x8000000000000000 ? 42 : 0; } uint64 Shift(uint64 value) { return value << 63; }")]
    public async Task ScalarFrontendProjectsCheckedIntegerShiftsWithLiteralCountsAsync(string source)
    {
        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }

    [Theory]
    [InlineData("int32 Main() { int32 value = 21; int32 count = 1; return value << count; }")]
    [InlineData("int32 Main() { return Shift(84, 1); } int32 Shift(int32 value, int32 count) { int32 shifted = value; return shifted >> count; }")]
    [InlineData("int32 Main() { return Shift(21); } int32 Shift(int32 value) { int32 count = 1; return value << count; }")]
    [InlineData("int32 Main() { uint32 value = 0x80000000; int32 count = 31; return value >>> count == 1 ? 42 : 0; }")]
    [InlineData("int32 Main() { uint32 value = 1; return value << 31 == 0x80000000 ? 42 : 0; }")]
    [InlineData("int32 Main() { int32 count = 5; int32 value = 1 << count; return value == 32 ? 42 : 0; }")]
    [InlineData("int32 Main() { int32 count = 5; init int32 value; value = 1 << count; return value == 32 ? 42 : 0; }")]
    [InlineData("int32 Main() { int32 count = 2; var value = +(1 << count); return value == 4 ? 42 : 0; }")]
    [InlineData("int32 Main() { int32 count = 2; var value = (1 << count) << count; return value == 16 ? 42 : 0; }")]
    [InlineData("int32 Main() { int32 count = 2; var value = true ? 1 << count : 16; return value == 4 ? 42 : 0; }")]
    [InlineData("int32 Main() { int32 count = 2; var value = true ? 1 << count : count; return value == 4 ? 42 : 0; }")]
    [InlineData("int32 Main() { int32 count = 2; var value = (1 << count) + 1; return value == 5 ? 42 : 0; }")]
    [InlineData("int32 Main() { int32 count = 2; var value = true ? 1 << count : 2 << count; return value == 4 ? 42 : 0; }")]
    public async Task ScalarFrontendProjectsCheckedIntegerShiftsWithLocalOperandsAsync(string source)
    {
        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }

    [Theory]
    [InlineData("int32 Main() { return Shift(5) == 32 ? 42 : 0; } int32 Shift(int32 count) { return 1 << count; }")]
    [InlineData("int32 Main() { return Shift(31) == 0x80000000 ? 42 : 0; } uint32 Shift(int32 count) { return 1 << count; }")]
    [InlineData("int32 Main() { return Shift(63) == 0x8000000000000000 ? 42 : 0; } uint64 Shift(int32 count) { return 1 << count; }")]
    [InlineData("int32 Main() { return Shift(1) == 2147483647 ? 42 : 0; } int32 Shift(int32 count) { return -1 >>> count; }")]
    [InlineData("int32 Main() { return Shift(0) == -2147483648 ? 42 : 0; } int32 Shift(int32 count) { return -2147483648 >>> count; }")]
    public async Task ScalarFrontendProjectsCheckedIntegerShiftsWithLiteralLeftOperandsAsync(string source)
    {
        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }

    [Fact]
    public async Task ScalarFrontendPreservesLiteralLeftShiftTypeThroughComparisonAsync()
    {
        const string source = "int32 Main() { int32 count = 5; return Shift(count) ? 42 : 0; } bool Shift(int32 count) { return 1 << count == 32; }";
        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }

    [Theory]
    [InlineData("int32 Shift(int32 count) { return 1 << count; }", 388, 1, 1)]
    [InlineData("int64 Shift(int32 count) { return 1 >> count; }", 389, 3, 1)]
    [InlineData("uint32 Shift(int32 count) { return 1 >>> count; }", 390, 2, 1)]
    [InlineData("int32 Shift(int32 count) { return -2147483648 >>> count; }", 1590, 1, int.MinValue)]
    public async Task ScalarFrontendProjectsLiteralLeftShiftsInCompactProjectionAsync(
        string source, int operation, int type, int literalLow)
    {
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("valid").GetBoolean());
        Assert.Equal(operation, result.GetProperty("whenTrue").GetProperty("operation").GetInt32());
        Assert.Equal(type, result.GetProperty("whenTrue").GetProperty("type").GetInt32());
        Assert.Equal(literalLow, result.GetProperty("whenTrue").GetProperty("leftParameter").GetInt32());
        Assert.Equal(0, result.GetProperty("whenTrue").GetProperty("rightParameter").GetInt32());
    }

    [Theory]
    [InlineData("int32 Main() { return 42; } int32 Shift(int32 value) { return value << 32; }")]
    [InlineData("int32 Main() { return 42; } int64 Shift(int64 value) { return value >> 64; }")]
    [InlineData("int32 Main() { return 42; } int32 Shift(int32 value) { return value << 2147483647; }")]
    [InlineData("int32 Main() { return 42; } int32 Shift(int32 value) { return value >>> -1; }")]
    [InlineData("int32 Main() { return 42; } int64 Shift(int64 value) { return value << -2147483648; }")]
    public async Task ScalarFrontendPreservesHostedLiteralShiftCountChecksAsync(string source)
    {
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-literal-shift-{Guid.NewGuid():N}");
        var nativePath = prefix + ".native.exe";
        var hostedPath = prefix + ".hosted.exe";
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (nativeExit, _, nativeError) = await RunAsync(input, "frontend-build",
                    directory, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, _, hostedError) = await RunAsync(input, "build",
                    "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.Equal(0, nativeExit);
                Assert.Equal(0, hostedExit);
                Assert.Empty(nativeError);
                Assert.Empty(hostedError);
                Assert.Equal(await File.ReadAllBytesAsync(hostedPath),
                    await File.ReadAllBytesAsync(nativePath));
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }

    [Theory]
    [InlineData("int32 Main() { return 42; } int32 Shift(int32 value) { return value << 2147483648; }")]
    [InlineData("int32 Main() { return 42; } int32 Shift(int32 value) { return value >> -2147483649; }")]
    public async Task ScalarFrontendRejectsShiftLiteralCountsOutsideInt32Async(string source)
    {
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.Equal(1, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(2, document.RootElement.GetProperty("result")
                .GetProperty("errorCode").GetInt32());
        }
    }

    [Fact]
    public async Task ScalarFrontendRejectsIntegerShiftWithNonInt32CountAsync()
    {
        const string source = "int32 Main() { return Shift(1, 1); } int32 Shift(int32 value, uint32 count) { return value << count; }";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(1, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(2, document.RootElement.GetProperty("result")
            .GetProperty("errorCode").GetInt32());
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, true)]
    public async Task ScalarFrontendAcceptsProgramsBeyondThePreviousWindowAndPreservesLaterFrameIndexesAsync(
        int scannerTokens, bool valid)
    {
        var firstModifier = scannerTokens == 65 ? "public " : "";
        var source = firstModifier + "int32 First() { "
            + string.Concat(Enumerable.Range(0, 6).Select(index => $"int32 x{index} = {index}; "))
            + "_ = true; return 1; } "
            + "int32 Second() { return 42; } "
            + "int32 Main() { return Second(); }";
        Assert.Equal(scannerTokens, Lexer.Lex(SourceText.From(source)).Tokens.Length);

        var directory = Path.Combine(RepositoryRoot(), "self-host");
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.Equal(valid ? 0 : 1, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal(valid, document.RootElement.GetProperty("success").GetBoolean());
            if (!valid)
            {
                continue;
            }

            var functions = document.RootElement.GetProperty("result").GetProperty("functions");
            Assert.Equal(3, functions.GetArrayLength());
            Assert.Equal(1, functions[1].GetProperty("index").GetInt32());
            var literalStart = source.IndexOf("42", StringComparison.Ordinal);
            Assert.Contains(functions[1].GetProperty("nodes").EnumerateArray(), node =>
                node.GetProperty("operation").GetInt32() == 3
                    && node.GetProperty("low").GetInt32() == 42
                    && node.GetProperty("span").GetProperty("start").GetInt32() == literalStart
                    && node.GetProperty("span").GetProperty("length").GetInt32() == 2);
            Assert.Contains(functions[2].GetProperty("nodes").EnumerateArray(), static node =>
                node.GetProperty("operation").GetInt32() == 105
                    && node.GetProperty("function").GetInt32() == 1);

            var (runExit, runOutput, runError) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-run", directory, "-", "--runtime", policy, "--json");
            Assert.Equal(42, runExit);
            Assert.Empty(runOutput);
            Assert.Empty(runError);
        }
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public async Task ScalarFrontendProgramRetainsExactExpressionNodeCapacityAsync(int nodeCount, bool valid)
    {
        var terms = new string[nodeCount == 64 ? 32 : 33];
        Array.Fill(terms, "x");
        if (nodeCount == 64)
        {
            terms[0] = "(x)";
        }
        var expression = string.Join(" + ", terms);
        var source = $"int32 First(int32 x) => {expression}; int32 Main() => 42;";
        var directory = Path.Combine(RepositoryRoot(), "self-host");

        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.Equal(valid ? 0 : 1, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            Assert.Equal(valid, root.GetProperty("success").GetBoolean());
            if (!valid)
            {
                continue;
            }

            var result = root.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
            var functions = result.GetProperty("functions");
            Assert.Equal(2, functions.GetArrayLength());
            Assert.Equal(nodeCount, functions[0].GetProperty("nodes").GetArrayLength());
        }
    }

    [Fact]
    public async Task ScalarFrontendCrossFunctionVoidStatementCliUsesCurrentProjectionAsync()
    {
        const string text = "public int32 Main() { Step(); return 42; } void Step() { }";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var input = Encoding.UTF8.GetBytes(text);
        var (exit, output, error) = await RunAsync(input, "frontend-component",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.True(exit == 0, $"{exit}: {output}{error}");
        using (var document = JsonDocument.Parse(output))
        {
            var result = document.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
            var main = result.GetProperty("functions")[0];
            Assert.Contains(main.GetProperty("nodes").EnumerateArray(),
                static node => node.GetProperty("operation").GetInt32() == 105
                    && node.GetProperty("type").GetInt32() == 0);
        }
        var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(42, runExit);
        Assert.Empty(runOutput);
        Assert.Empty(runError);
    }

    [Fact]
    public async Task ScalarFrontendCrossFunctionDoesNotReturnCliUsesCurrentProjectionAsync()
    {
        const string text = "[DoesNotReturn] void Halt() { require false; } public int32 Main() { if (false) { Halt(); } return 42; }";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.True(exit == 0, $"{exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                AssertCurrentScalarFrontendVersion(result);
                Assert.Equal(2, result.GetProperty("functions").GetArrayLength());
            }
            var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                directory, "-", "--runtime", runtime, "--json");
            Assert.Equal(42, runExit);
            Assert.Empty(runOutput);
            Assert.Empty(runError);
        }
    }

    [Fact]
    public async Task ScalarFrontendCrossFunctionOutputArgumentsCliUsesCurrentProjectionAsync()
    {
        const string text = "public int32 Main() { if (Set(40, out var value, out var extra)) { return value + extra; } return 0; } bool Set(int32 seed, out int32 value, out int32 extra) { value = seed + 1; extra = 1; return true; }";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.True(exit == 0, $"{exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                AssertCurrentScalarFrontendVersion(result);
                var main = result.GetProperty("functions")[0];
                Assert.Equal(0, main.GetProperty("outParameterMask").GetInt32());
                Assert.Equal(6, result.GetProperty("functions")[1]
                    .GetProperty("outParameterMask").GetInt32());
                Assert.Contains(main.GetProperty("nodes").EnumerateArray(),
                    static node => node.GetProperty("operation").GetInt32() == 104);
            }
            var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                directory, "-", "--runtime", runtime, "--json");
            Assert.Equal(42, runExit);
            Assert.Empty(runOutput);
            Assert.Empty(runError);
        }
    }

    [Fact]
    public async Task ScalarFrontendCrossFunctionNonBooleanOutputCliUsesCurrentProjectionAsync()
    {
        const string text = "public int32 Main() { mut int32 value = 0; int32 status = Set(out value); return value + status; } int32 Set(out int32 value) { value = 40; return 2; }";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.True(exit == 0, $"{runtime}: {exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                AssertCurrentScalarFrontendVersion(result);
                Assert.Equal(1, result.GetProperty("functions")[1]
                    .GetProperty("outParameterMask").GetInt32());
            }
            var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                directory, "-", "--runtime", runtime, "--json");
            Assert.Equal(42, runExit);
            Assert.Empty(runOutput);
            Assert.Empty(runError);
        }
    }

    [Fact]
    public async Task ScalarFrontendCrossFunctionNonBooleanOutVariablesUseCurrentProjectionAsync()
    {
        const string text = """
            public int32 Main() {
                int32 status = Set(out var value);
                Fill(out var extra);
                return status + value + extra;
            }
            int32 Set(out int32 value) { value = 20; return 1; }
            void Fill(out int32 value) { value = 21; }
            """;
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.True(exit == 0, $"{runtime}: {exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                AssertCurrentScalarFrontendVersion(result);
                Assert.Contains(result.GetProperty("functions")[0].GetProperty("nodes")
                    .EnumerateArray(), static node => node.GetProperty("operation").GetInt32() == 104);
            }
            var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                directory, "-", "--runtime", runtime, "--json");
            Assert.Equal(42, runExit);
            Assert.Empty(runOutput);
            Assert.Empty(runError);

            var policy = runtime == "fail-fast"
                ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = Assert.IsType<BoundProgram>(compilation.Program);
            var outputPath = Path.Combine(Path.GetTempPath(), $"terrum-frontend-outvar-{Guid.NewGuid():N}.dll");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(input, "frontend-build-library",
                    directory, "-", "-o", outputPath, "--runtime", runtime, "--json");
                Assert.True(buildExit == 0, $"{runtime}: {buildExit}: {buildOutput}{buildError}");
                Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["Main"], (NativeRuntimePolicy)policy),
                    await File.ReadAllBytesAsync(outputPath));
            }
            finally
            {
                File.Delete(outputPath);
            }
        }
    }

    [Theory]
    [InlineData("public int32 Main() { bool success = Try(out var value); return success ? 42 : 0; } bool Try(out int32 value) { value = 42; return true; }", 77, 104)]
    [InlineData("public int32 Main() { bool success = Fill(true, init var value); return success ? 42 : 0; } bool Fill(bool publish, init ref<int32> value) { if (publish) { value = 42; return true; } return false; }", 78, 108)]
    public async Task ScalarFrontendOutputVariablesInInitializedLocalsRunAsync(
        string text, int projectionVersion, int operation)
    {
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.True(exit == 0, $"{runtime}: {exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.NotEqual(projectionVersion, result.GetProperty("projectionVersion").GetInt32());
                AssertCurrentScalarFrontendVersion(result);
                Assert.Contains(result.GetProperty("functions")[0].GetProperty("nodes")
                    .EnumerateArray(), node => node.GetProperty("operation").GetInt32() == operation);
            }

            var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                directory, "-", "--runtime", runtime, "--json");
            Assert.Equal(42, runExit);
            Assert.Empty(runOutput);
            Assert.Empty(runError);
        }
    }

    [Fact]
    public async Task ScalarFrontendSingleFunctionNonBooleanOutputCliUsesCurrentProjectionAsync()
    {
        const string text = "public int32 Fill(int32 seed, out int32 value) { if (seed == 0) { value = 40; return 2; } return Fill(seed - 1, out value); }";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var input = Encoding.UTF8.GetBytes(text);
        var outputDirectory = Path.Combine(RepositoryRoot(), "artifacts", "scanner-tests");
        Directory.CreateDirectory(outputDirectory);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.True(exit == 0, $"{runtime}: {exit}: {output}{error}");
            using var document = JsonDocument.Parse(output);
            var result = document.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
            Assert.Equal(2, result.GetProperty("outParameterMask").GetInt32());
            Assert.Equal(0, result.GetProperty("initRefParameterMask").GetInt32());
            Assert.Contains(result.GetProperty("nodes").EnumerateArray(), static node =>
                node.GetProperty("operation").GetInt32() == 102);
            Assert.Contains(result.GetProperty("nodes").EnumerateArray(), static node =>
                node.GetProperty("operation").GetInt32() == 103);

            var path = Path.Combine(outputDirectory, $"single-init-ref-{Guid.NewGuid():N}.dll");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(input, "frontend-build-library",
                    directory, "-", "-o", path, "--runtime", runtime, "--json");
                Assert.True(buildExit == 0, $"{runtime}: {buildExit}: {buildOutput}{buildError}");
                var policy = runtime == "fail-fast"
                    ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
                var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
                Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
                var hosted = Assert.IsType<BoundProgram>(compilation.Program);
                Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["Fill"], (NativeRuntimePolicy)policy),
                    await File.ReadAllBytesAsync(path));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task ScalarFrontendSingleFunctionNonBooleanOutVariablesUseCurrentProjectionAsync()
    {
        const string text = """
            public int32 Fill(int32 seed, out int32 value) {
                if (seed == 0) { value = 40; return 2; }
                int32 result = Fill(seed - 1, out var recursiveValue);
                value = recursiveValue;
                return result;
            }
            """;
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.True(exit == 0, $"{runtime}: {exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                AssertCurrentScalarFrontendVersion(result);
                Assert.Contains(result.GetProperty("nodes").EnumerateArray(), static node =>
                    node.GetProperty("operation").GetInt32() == 104);
            }

            var policy = runtime == "fail-fast"
                ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = Assert.IsType<BoundProgram>(compilation.Program);
            var outputPath = Path.Combine(Path.GetTempPath(), $"terrum-frontend-outvar-{Guid.NewGuid():N}.dll");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(input, "frontend-build-library",
                    directory, "-", "-o", outputPath, "--runtime", runtime, "--json");
                Assert.True(buildExit == 0, $"{runtime}: {buildExit}: {buildOutput}{buildError}");
                Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["Fill"], (NativeRuntimePolicy)policy),
                    await File.ReadAllBytesAsync(outputPath));
            }
            finally
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public async Task ScalarFrontendCrossFunctionInitReferencesCliUsesCurrentProjectionAsync()
    {
        const string text = "public int32 Main() { require !Fill(false, init var value); return Fill(true, init value) ? value : 0; } bool Fill(bool publish, init ref<int32> value) { if (publish) { value = 42; return true; } return false; }";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.True(exit == 0, $"{exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                AssertCurrentScalarFrontendVersion(result);
                Assert.Equal(0, result.GetProperty("functions")[0]
                    .GetProperty("initRefParameterMask").GetInt32());
                Assert.Equal(2, result.GetProperty("functions")[1]
                    .GetProperty("initRefParameterMask").GetInt32());
                Assert.Contains(result.GetProperty("functions")[0].GetProperty("nodes")
                    .EnumerateArray(), static node =>
                    node.GetProperty("operation").GetInt32() == 107);
                Assert.Contains(result.GetProperty("functions")[0].GetProperty("nodes")
                    .EnumerateArray(), static node =>
                    node.GetProperty("operation").GetInt32() == 108);
            }
            var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                directory, "-", "--runtime", runtime, "--json");
            Assert.Equal(42, runExit);
            Assert.Empty(runOutput);
            Assert.Empty(runError);
        }
    }

    [Fact]
    public async Task ScalarFrontendSingleInitReferencesCliUsesCurrentProjectionAsync()
    {
        const string text = "public bool Fill(bool publish, init ref<int32> value) { if (publish) { value = 42; return true; } return Fill(true, init value); }";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var input = Encoding.UTF8.GetBytes(text);
        var outputDirectory = Path.Combine(RepositoryRoot(), "artifacts", "scanner-tests");
        Directory.CreateDirectory(outputDirectory);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.True(exit == 0, $"{exit}: {output}{error}");
            using var document = JsonDocument.Parse(output);
            var result = document.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
            Assert.Equal(0, result.GetProperty("outParameterMask").GetInt32());
            Assert.Equal(2, result.GetProperty("initRefParameterMask").GetInt32());
            Assert.Contains(result.GetProperty("nodes").EnumerateArray(), static node =>
                node.GetProperty("operation").GetInt32() == 107);

            var path = Path.Combine(outputDirectory, $"single-nonboolean-init-ref-{Guid.NewGuid():N}.dll");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(input, "frontend-build-library",
                    directory, "-", "-o", path, "--runtime", runtime, "--json");
                Assert.True(buildExit == 0, $"{buildExit}: {buildOutput}{buildError}");
                var policy = runtime == "fail-fast"
                    ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
                var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
                Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
                var hosted = Assert.IsType<BoundProgram>(compilation.Program);
                Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["Fill"], (NativeRuntimePolicy)policy),
                    await File.ReadAllBytesAsync(path));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task ScalarFrontendSingleNonBooleanInitReferenceCliUsesCurrentProjectionAsync()
    {
        const string text = "public int32 Fill(int32 seed, init ref<int32> value) { if (seed == 0) { value = 40; return 0; } return Fill(seed - 1, init value); }";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.True(exit == 0, $"{runtime}: {exit}: {output}{error}");
            using var document = JsonDocument.Parse(output);
            var result = document.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
            Assert.Equal(2, result.GetProperty("initRefParameterMask").GetInt32());
            Assert.Contains(result.GetProperty("nodes").EnumerateArray(), static node =>
                node.GetProperty("operation").GetInt32() == 107);

            var path = Path.Combine(Path.GetTempPath(), $"nonboolean-init-ref-{Guid.NewGuid():N}.dll");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(input, "frontend-build-library",
                    directory, "-", "-o", path, "--runtime", runtime, "--json");
                Assert.True(buildExit == 0, $"{runtime}: {buildExit}: {buildOutput}{buildError}");
                var policy = runtime == "fail-fast"
                    ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
                var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
                Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
                var hosted = Assert.IsType<BoundProgram>(compilation.Program);
                Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["Fill"], (NativeRuntimePolicy)policy),
                    await File.ReadAllBytesAsync(path));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task ScalarFrontendCrossFunctionNonBooleanAndVoidInitReferenceCliUsesCurrentProjectionAsync()
    {
        const string text = "public int32 Main() { Fill(init var value); SetVoid(init var other); return value + other; } int32 Fill(init ref<int32> value) { value = 40; return 0; } void SetVoid(init ref<int32> value) { value = 2; }";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.True(exit == 0, $"{runtime}: {exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                AssertCurrentScalarFrontendVersion(result);
                Assert.Equal(1, result.GetProperty("functions")[1]
                    .GetProperty("initRefParameterMask").GetInt32());
                Assert.Equal(1, result.GetProperty("functions")[2]
                    .GetProperty("initRefParameterMask").GetInt32());
            }

            var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                directory, "-", "--runtime", runtime, "--json");
            Assert.Equal(42, runExit);
            Assert.Empty(runOutput);
            Assert.Empty(runError);

            var path = Path.Combine(Path.GetTempPath(), $"cross-function-init-ref-{Guid.NewGuid():N}.dll");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(input, "frontend-build-library",
                    directory, "-", "-o", path, "--runtime", runtime, "--json");
                Assert.True(buildExit == 0, $"{runtime}: {buildExit}: {buildOutput}{buildError}");
                var policy = runtime == "fail-fast"
                    ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
                var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
                Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
                var hosted = Assert.IsType<BoundProgram>(compilation.Program);
                Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["Main"], (NativeRuntimePolicy)policy),
                    await File.ReadAllBytesAsync(path));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task ScalarFrontendCrossFunctionCliSelectsEntryAndExportsInDeclarationOrderAsync()
    {
        const string text = "public int32 Next(int32 value) => value + 1; public int32 Main() => Next(41);";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var outputDirectory = Path.Combine(RepositoryRoot(), "artifacts", "scanner-tests");
        Directory.CreateDirectory(outputDirectory);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var input = Encoding.UTF8.GetBytes(text);
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.True(exit == 0, $"{exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                AssertCurrentScalarFrontendVersion(result);
                var functions = result.GetProperty("functions");
                Assert.Equal(2, functions.GetArrayLength());
                Assert.Equal(0, functions[0].GetProperty("index").GetInt32());
                Assert.Equal(1, functions[1].GetProperty("index").GetInt32());
                Assert.Equal(0, functions[1].GetProperty("nodes").EnumerateArray()
                    .Single(node => node.GetProperty("operation").GetInt32() == 105)
                    .GetProperty("function").GetInt32());
            }
            var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                directory, "-", "--runtime", runtime, "--json");
            Assert.Equal(42, runExit);
            Assert.Empty(runOutput);
            Assert.Empty(runError);

            var path = Path.Combine(outputDirectory, $"cross-function-library-{Guid.NewGuid():N}.dll");
            var executablePath = Path.ChangeExtension(path, ".exe");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(input, "frontend-build-library",
                    directory, "-", "-o", path, "--runtime", runtime, "--json");
                Assert.True(buildExit == 0, $"{buildExit}: {buildOutput}{buildError}");
                var policy = runtime == "fail-fast"
                    ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
                var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
                Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
                var hosted = Assert.IsType<BoundProgram>(compilation.Program);
                Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["Next", "Main"], (NativeRuntimePolicy)policy),
                    await File.ReadAllBytesAsync(path));
                var (executableExit, executableOutput, executableError) = await RunAsync(input, "frontend-build",
                    directory, "-", "-o", executablePath, "--runtime", runtime, "--json");
                Assert.True(executableExit == 0, $"{executableExit}: {executableOutput}{executableError}");
                Assert.Equal(NativeEmitter.Emit(hosted with { EntryPoint = 1 }, (NativeRuntimePolicy)policy),
                    await File.ReadAllBytesAsync(executablePath));
                using var process = Process.Start(new ProcessStartInfo(executablePath) {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }) ?? throw new InvalidOperationException("Could not execute frontend output.");
                await process.WaitForExitAsync();
                Assert.Equal(42, process.ExitCode);
            }
            finally
            {
                File.Delete(path);
                File.Delete(executablePath);
            }
        }
    }

    [Theory]
    [InlineData("int32 Main() { return Add(Next(Identity(20)), Next(Identity(21))) - 1; } int32 Next(int32 value) => value + 1; int32 Identity(int32 value) => value; int32 Add(int32 first, int32 second) => first + second;")]
    [InlineData("int32 Main() { return Sum(Identity(20), Identity(21)) == 41 ? 42 : 0; } int32 Identity(int32 value) => value; int32 Sum(int32 first, int32 second) => first + second;")]
    public async Task ScalarFrontendNestedValueCallsMatchHostedAsync(string source)
    {
        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }

    [Fact]
    public async Task ScalarFrontendNestedCallsPreserveSourceOrderAsync()
    {
        const string source = "int32 Set(out int32 trace) { trace = 3; return trace; } int32 Pair(int32 first, int32 second) => first + second; int32 Main() { mut int32 trace = 0; return Pair(Set(out trace), trace) == 6 ? 42 : 0; }";
        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task ScalarFrontendFourParameterCliProjectsFourthSignatureAndCallRootAsync(
        string runtime)
    {
        const string source = "public int32 Main() => Sum(fourth: 12, second: 10, first: 10, third: 10); int32 Sum(int32 first, int32 second, int32 third, int32 fourth) => first + second + third + fourth;";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", Path.Combine(RepositoryRoot(), "self-host"), "-",
            "--runtime", runtime, "--json");
        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.Equal(88, result.GetProperty("projectionVersion").GetInt32());
        var functions = result.GetProperty("functions");
        Assert.Equal(2, functions.GetArrayLength());
        Assert.Equal(4, functions[1].GetProperty("parameterCount").GetInt32());
        Assert.Equal(1, functions[1].GetProperty("fourthType").GetInt32());
        Assert.Equal(source.IndexOf("int32 fourth", StringComparison.Ordinal) + "int32 ".Length,
            functions[1].GetProperty("fourthName").GetProperty("start").GetInt32());
        var call = Assert.Single(functions[0].GetProperty("nodes").EnumerateArray(),
            static node => node.GetProperty("operation").GetInt32() == 105);
        Assert.InRange(call.GetProperty("fourthArgumentRoot").GetInt32(), 0, 31);
        Assert.Equal(new[] { 3, 1, 0, 2 },
            call.GetProperty("argumentParameterIndices").EnumerateArray()
                .Select(static parameter => parameter.GetInt32()));
        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }

    [Theory]
    [InlineData("public int32 Main() { init int32 result; Initialize(third: 3, second: 2, first: 1, output: init result); return result; } int32 Initialize(int32 first, int32 second, int32 third, init ref<int32> output) { output = 42; return 6; }")]
    [InlineData("public int32 Main() { int32 status = Initialize(third: 3, second: 2, first: 1, output: out var result); return result + status; } int32 Initialize(int32 first, int32 second, int32 third, out int32 output) { output = 36; return 6; }")]
    public async Task ScalarFrontendFourParameterCliProjectsFourthOutputAndRunsAsync(string source)
    {
        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }

    [Theory]
    [InlineData("int32 Main() { return Identity(IsReady()); } int32 Identity(int32 value) => value; bool IsReady() => true;", "IsReady()", 2)]
    [InlineData("int32 Main() { return Identity(Missing()); } int32 Identity(int32 value) => value;", "Missing", 3)]
    public async Task ScalarFrontendRejectsInvalidNestedCallsWithSourceSpansAsync(
        string source, string offending, int errorCode)
    {
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(1, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(errorCode, result.GetProperty("errorCode").GetInt32());
        var span = result.GetProperty("errorSpan");
        var expectedStart = source.IndexOf(offending, StringComparison.Ordinal);
        Assert.Equal(expectedStart, span.GetProperty("start").GetInt32());
        Assert.Equal(offending.Length, span.GetProperty("length").GetInt32());

        var hosted = Compilation.Create(SyntaxTree.Parse(source));
        Assert.True(hosted.HasErrors);
        Assert.Contains(hosted.Diagnostics, diagnostic => diagnostic.Span.Start == expectedStart
            && diagnostic.Span.Length == offending.Length);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "fail-fast")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "checked-exceptions")]
    public async Task ScalarFrontendNamedCallsPreserveSourceOrderAndFormalMappingAsync(
        BoundRuntimePolicy policy, string runtime)
    {
        const string text = """
            int32 Set(out int32 trace, int32 value) { trace = value; return value; }
            int32 Pair(int32 first, int32 second) => first + second;
            int32 Main() {
                mut int32 trace = 0;
                return Pair(second: Set(out trace, 3), first: trace);
            }
            """;
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var outputDirectory = Path.Combine(RepositoryRoot(), "artifacts", "scanner-tests");
        Directory.CreateDirectory(outputDirectory);
        var input = Encoding.UTF8.GetBytes(text);
        var (componentExit, componentOutput, componentError) = await RunAsync(input, "frontend-component",
            directory, "-", "--runtime", runtime, "--json");
        Assert.True(componentExit == 0, $"{componentExit}: {componentOutput}{componentError}");
        using (var document = JsonDocument.Parse(componentOutput))
        {
            var result = document.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
            Assert.Contains(result.GetProperty("functions")[2].GetProperty("nodes").EnumerateArray(),
                static node => node.GetProperty("operation").GetInt32() == 105);
        }
        var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
            directory, "-", "--runtime", runtime, "--json");
        Assert.Equal(6, runExit);
        Assert.Empty(runOutput);
        Assert.Empty(runError);

        var hosted = Compilation.Create(SyntaxTree.Parse(text), policy);
        Assert.False(hosted.HasErrors, string.Join(Environment.NewLine, hosted.Diagnostics));
        var executablePath = Path.Combine(outputDirectory, $"named-calls-{Guid.NewGuid():N}.exe");
        try
        {
            var (buildExit, buildOutput, buildError) = await RunAsync(input, "frontend-build",
                directory, "-", "-o", executablePath, "--runtime", runtime, "--json");
            Assert.True(buildExit == 0, $"{buildExit}: {buildOutput}{buildError}");
            Assert.Equal(NativeEmitter.Emit(Assert.IsType<BoundProgram>(hosted.Program), (NativeRuntimePolicy)policy),
                await File.ReadAllBytesAsync(executablePath));
        }
        finally
        {
            File.Delete(executablePath);
        }
    }

    [Theory]
    [InlineData("int32 Set(out int32 trace, int32 value) { trace = value; return value; } int32 Main() { mut int32 trace = 0; return Set(value: 3, trace: out trace); }", 3)]
    [InlineData("int32 Set(out int32 result, int32 value) { result = value; return value; } int32 Main() { return Set(value: 3, result: out var result) + result; }", 6)]
    [InlineData("int32 Initialize(int32 value, init ref<int32> result) { result = value; return 0; } int32 Main() { init int32 result; int32 status = Initialize(result: init result, value: 7); return result + status; }", 7)]
    [InlineData("int32 Initialize(int32 value, init ref<int32> result) { result = value; return 0; } int32 Main() { int32 status = Initialize(result: init var result, value: 7); return result + status; }", 7)]
    [InlineData("void Set(out int32 result, int32 value) { result = value; } int32 Main() { Set(value: 6, result: out var result); return result; }", 6)]
    [InlineData("void Initialize(int32 value, init ref<int32> result) { result = value; } int32 Main() { Initialize(result: init var result, value: 7); return result; }", 7)]
    [InlineData("int32 Set(out int32 t, int32 v) { t = v; return v; } int32 Identity(int32 r) => r; int32 Main() { mut int32 t = 0; return Identity(r: Set(v: 3, t: out t)); }", 3)]
    [InlineData("int32 Initialize(int32 v, init ref<int32> r) { r = v; return 0; } int32 Identity(int32 x) => x; int32 Main() { init int32 r; return Identity(x: Initialize(r: init r, v: 7)) + r; }", 7)]
    public async Task ScalarFrontendNamedOutputCallsMatchHostedAsync(string text, int expected)
    {
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var (policy, runtime) in new[]
        {
            (BoundRuntimePolicy.FailFast, "fail-fast"),
            (BoundRuntimePolicy.CheckedExceptions, "checked-exceptions"),
        })
        {
            var (componentExit, componentOutput, componentError) = await RunAsync(input,
                "frontend-component", directory, "-", "--runtime", runtime, "--json");
            Assert.True(componentExit == 0, $"{componentExit}: {componentOutput}{componentError}");
            Assert.Empty(componentError);
            using (var document = JsonDocument.Parse(componentOutput))
            {
                var result = document.RootElement.GetProperty("result");
                AssertCurrentScalarFrontendVersion(result);
                var functions = result.GetProperty("functions");
                Assert.Contains(functions[functions.GetArrayLength() - 1].GetProperty("nodes").EnumerateArray(),
                    static node => node.GetProperty("operation").GetInt32() == 105);
            }

            var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                directory, "-", "--runtime", runtime, "--json");
            Assert.Equal(expected, runExit);
            Assert.Empty(runOutput);
            Assert.Empty(runError);

            var hosted = Compilation.Create(SyntaxTree.Parse(text), policy);
            Assert.False(hosted.HasErrors, string.Join(Environment.NewLine, hosted.Diagnostics));
            Assert.Equal(expected, NativeJit.Execute(
                Assert.IsType<BoundProgram>(hosted.Program), (NativeRuntimePolicy)policy));
        }
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "fail-fast")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "checked-exceptions")]
    public async Task ScalarFrontendNamedCallsBuildHostedParityLibraryAsync(
        BoundRuntimePolicy policy, string runtime)
    {
        const string text = """
            int32 Pair(int32 first, int32 second) => first + second;
            public int32 Value() => Pair(second: 3, first: 2);
            """;
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var input = Encoding.UTF8.GetBytes(text);
        var (componentExit, componentOutput, componentError) = await RunAsync(input, "frontend-component",
            directory, "-", "--runtime", runtime, "--json");
        Assert.True(componentExit == 0, $"{componentExit}: {componentOutput}{componentError}");
        using (var document = JsonDocument.Parse(componentOutput))
        {
            AssertCurrentScalarFrontendVersion(document.RootElement.GetProperty("result"));
        }

        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var hosted = Assert.IsType<BoundProgram>(compilation.Program);
        var path = Path.Combine(Path.GetTempPath(), $"named-calls-{Guid.NewGuid():N}.dll");
        try
        {
            var (buildExit, buildOutput, buildError) = await RunAsync(input, "frontend-build-library",
                directory, "-", "-o", path, "--runtime", runtime, "--json");
            Assert.True(buildExit == 0, $"{buildExit}: {buildOutput}{buildError}");
            Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["Value"], (NativeRuntimePolicy)policy),
                await File.ReadAllBytesAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ScalarFrontendNamedValueCallPreservesPositionalOutputParameterAsync()
    {
        const string text = "bool Fill(out int32 value, int32 other) { value = other; return true; } int32 Main() { if (Fill(out var result, other: 3)) { return result; } return 0; }";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var input = Encoding.UTF8.GetBytes(text);
        var (componentExit, componentOutput, componentError) = await RunAsync(input, "frontend-component",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.True(componentExit == 0, $"{componentExit}: {componentOutput}{componentError}");
        using (var document = JsonDocument.Parse(componentOutput))
        {
            var result = document.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
        }
        var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(3, runExit);
        Assert.Empty(runOutput);
        Assert.Empty(runError);
    }

    [Fact]
    public async Task ScalarFrontendNamedVoidCallPreservesPositionalOutputParameterAsync()
    {
        const string text = "void Fill(out int32 value, int32 other) { value = other; } int32 Main() { Fill(out var result, other: 3); return result; }";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var input = Encoding.UTF8.GetBytes(text);
        var (componentExit, componentOutput, componentError) = await RunAsync(input, "frontend-component",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.True(componentExit == 0, $"{componentExit}: {componentOutput}{componentError}");
        using (var document = JsonDocument.Parse(componentOutput))
        {
            var result = document.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
        }
        var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(3, runExit);
        Assert.Empty(runOutput);
        Assert.Empty(runError);
    }

    [Theory]
    [InlineData("int32 Main() { return Sum(unknown: 1, second: 2); } int32 Sum(int32 first, int32 second) { return first + second; }", 3)]
    [InlineData("int32 Main() { return Sum(first: 1, first: 2); } int32 Sum(int32 first, int32 second) { return first + second; }", 1)]
    [InlineData("int32 Main() { return Sum(second: 2, 1); } int32 Sum(int32 first, int32 second) { return first + second; }", 1)]
    [InlineData("bool Fill(out int32 value, int32 other) { value = other; return true; } int32 Main() { Fill(other: 1, value: init var result); return 0; }", 1)]
    public async Task ScalarFrontendRejectsInvalidNamedCallBindingsAsync(string text, int errorCode)
    {
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(text), "frontend-component",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(1, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(errorCode, document.RootElement.GetProperty("result")
            .GetProperty("errorCode").GetInt32());
    }

    [Fact]
    public async Task ScalarFrontendNamedSelfCallsUseCurrentProjectionAsync()
    {
        const string text = "int32 Recurse(int32 first, int32 second, int32 third) => Recurse(first: second, second: first, third);";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(text), "frontend-component",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.True(exit == 0, $"{exit}: {output}{error}");
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        AssertCurrentScalarFrontendVersion(result);
        Assert.Contains(result.GetProperty("nodes").EnumerateArray(),
            static node => node.GetProperty("operation").GetInt32() == 102);
    }

    [Fact]
    public async Task ScalarFrontendNamedCallsContinuePositionallyAfterInPositionLabelAsync()
    {
        const string text = "int32 Main() { return Select(first: 1, true, third: char16(65)); } int32 Select(int32 first, bool second, char16 third) { if (second) { return first * 100 + int32(third); } return 0; }";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var input = Encoding.UTF8.GetBytes(text);
        var (componentExit, componentOutput, componentError) = await RunAsync(input, "frontend-component",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.True(componentExit == 0, $"{componentExit}: {componentOutput}{componentError}");
        using (var document = JsonDocument.Parse(componentOutput))
        {
            var result = document.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
        }
        var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(165, runExit);
        Assert.Empty(runOutput);
        Assert.Empty(runError);
    }

    [Theory]
    [InlineData("int32 Take(int32 @await) => @await; int32 Main() { return Take(@await: 42); }")]
    [InlineData("int32 Take(int32 \U00010400) => \U00010400; int32 Main() { return Take(\U00010400: 42); }")]
    public async Task ScalarFrontendNamedCallsMatchEscapedAndSupplementaryNamesAsync(string text)
    {
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var input = Encoding.UTF8.GetBytes(text);
        var (componentExit, componentOutput, componentError) = await RunAsync(input, "frontend-component",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.True(componentExit == 0, $"{componentExit}: {componentOutput}{componentError}");
        using (var document = JsonDocument.Parse(componentOutput))
        {
            var result = document.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
        }
        var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(42, runExit);
        Assert.Empty(runOutput);
        Assert.Empty(runError);
    }
}
