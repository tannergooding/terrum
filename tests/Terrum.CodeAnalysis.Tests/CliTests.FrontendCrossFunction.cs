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
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    private static void AssertCurrentScalarFrontendVersion(JsonElement result)
    {
        Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(result.TryGetProperty("functions", out _) ? 88 : 87));
    }

    [Test]
    public async Task ScalarFrontendSupportsOneFileScopedNamespaceInCompleteProgramsAsync()
    {
        const string source = "namespace Demo.Tools; alias Scalar = int32; "
            + "Scalar Main() { return Add(20, 22); } "
            + "public Scalar Add(Scalar left, Scalar right) { return left + right; }";
        var root = RepositoryRoot();
        var directory = SelfHostPaths.SourceDirectory(root);
        var namespaceStart = source.IndexOf("Demo.Tools", StringComparison.Ordinal);
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var input = Encoding.UTF8.GetBytes(source);
            var (scanExit, scanOutput, scanError) = await RunAsync(input,
                "scan-component", directory, "-", "--runtime", policy, "--json");
            Assert.That(scanExit, Is.EqualTo(0));
            Assert.That(scanError, Is.Empty);
            using (var scan = JsonDocument.Parse(scanOutput))
            {
                Assert.That(global::System.Linq.Enumerable.Any(scan.RootElement.GetProperty("tokens").EnumerateArray(),static token => token.GetProperty("kind").GetString() == "NamespaceKeyword"),Is.True);
            }

            var (componentExit, componentOutput, componentError) = await RunAsync(input,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.That(componentExit == 0, Is.True, $"{componentOutput}{componentError}");
            Assert.That(componentError, Is.Empty);
            using (var component = JsonDocument.Parse(componentOutput))
            {
                var result = component.RootElement.GetProperty("result");
                AssertCurrentScalarFrontendVersion(result);
                var functions = result.GetProperty("functions");
                Assert.That(functions.GetArrayLength(), Is.EqualTo(2));
                foreach (var function in functions.EnumerateArray())
                {
                    var span = function.GetProperty("namespaceNameSpan");
                    Assert.That(span.GetProperty("start").GetInt32(), Is.EqualTo(namespaceStart));
                    Assert.That(span.GetProperty("length").GetInt32(), Is.EqualTo("Demo.Tools".Length));
                    Assert.That(function.GetProperty("qualifiedName").GetString(), Does.StartWith("Demo.Tools."));
                }
            }

            var (frontendExit, frontendOutput, frontendError) = await RunAsync(input,
                "frontend-run", directory, "-", "--runtime", policy, "--json");
            var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                "jit", "-", "--runtime", policy, "--json");
            Assert.That(frontendExit, Is.EqualTo(42));
            Assert.That(frontendExit, Is.EqualTo(hostedExit));
            Assert.That(frontendOutput, Is.Empty);
            Assert.That(hostedOutput, Is.Empty);
            Assert.That(frontendError, Is.Empty);
            Assert.That(hostedError, Is.Empty);

            var nativeLibrary = Path.Combine(TestArtifactPaths.DirectoryPath,
                $"terrum-namespace-native-{Guid.NewGuid():N}.dll");
            var hostedLibrary = Path.Combine(TestArtifactPaths.DirectoryPath,
                $"terrum-namespace-hosted-{Guid.NewGuid():N}.dll");
            try
            {
                var (nativeLibraryExit, _, nativeLibraryError) = await RunAsync(input,
                    "frontend-build-library", directory, "-", "-o", nativeLibrary,
                    "--runtime", policy, "--json");
                var (hostedLibraryExit, _, hostedLibraryError) = await RunAsync(input,
                    "build-library", "-", "--export", "Demo.Tools.Add", "-o", hostedLibrary,
                    "--runtime", policy, "--json");
                Assert.That(nativeLibraryExit, Is.EqualTo(0));
                Assert.That(hostedLibraryExit, Is.EqualTo(0));
                Assert.That(nativeLibraryError, Is.Empty);
                Assert.That(hostedLibraryError, Is.Empty);
                Assert.That(await File.ReadAllBytesAsync(nativeLibrary), Is.EqualTo(await File.ReadAllBytesAsync(hostedLibrary)));
            }
            finally
            {
                File.Delete(nativeLibrary);
                File.Delete(hostedLibrary);
            }
        }

        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }

    [Test]
    public async Task ScalarFrontendSupportsOneNamespaceBlockInCompleteProgramsAsync()
    {
        const string source = "namespace Demo.Tools { alias Scalar = int32; "
            + "Scalar Main() { return Add(20, 22); } "
            + "public Scalar Add(Scalar left, Scalar right) { return left + right; } }";
        const string hostedSource = "namespace Demo.Tools; alias Scalar = int32; "
            + "Scalar Main() { return Add(20, 22); } "
            + "public Scalar Add(Scalar left, Scalar right) { return left + right; }";
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var namespaceStart = source.IndexOf("Demo.Tools", StringComparison.Ordinal);
        var nativePath = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"terrum-namespace-block-{Guid.NewGuid():N}.native.exe");
        var hostedPath = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"terrum-namespace-block-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var hostedInput = Encoding.UTF8.GetBytes(hostedSource);
                var (componentExit, componentOutput, componentError) = await RunAsync(input,
                    "frontend-component", directory, "-", "--runtime", policy, "--json");
                Assert.That(componentExit == 0, Is.True, $"{componentOutput}{componentError}");
                Assert.That(componentError, Is.Empty);
                using (var component = JsonDocument.Parse(componentOutput))
                {
                    var result = component.RootElement.GetProperty("result");
                    Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(88));
                    var functions = result.GetProperty("functions");
                    Assert.That(functions.GetArrayLength(), Is.EqualTo(2));
                    Assert.That(functions.EnumerateArray().Select(static function =>
                            function.GetProperty("qualifiedName").GetString()).ToArray(), Is.EqualTo(new[] { "Demo.Tools.Main", "Demo.Tools.Add" }));
                    foreach (var function in functions.EnumerateArray())
                    {
                        var span = function.GetProperty("namespaceNameSpan");
                        Assert.That(span.GetProperty("start").GetInt32(), Is.EqualTo(namespaceStart));
                        Assert.That(span.GetProperty("length").GetInt32(), Is.EqualTo("Demo.Tools".Length));
                    }
                }

                var (nativeBuildExit, nativeBuildOutput, nativeBuildError) = await RunAsync(input,
                    "frontend-build", directory, "-", "-o", nativePath,
                    "--runtime", policy, "--json");
                var (hostedBuildExit, hostedBuildOutput, hostedBuildError) = await RunAsync(hostedInput,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.That(nativeBuildExit == 0, Is.True, $"{nativeBuildOutput}{nativeBuildError}");
                Assert.That(hostedBuildExit == 0, Is.True, $"{hostedBuildOutput}{hostedBuildError}");
                Assert.That(nativeBuildError, Is.Empty);
                Assert.That(hostedBuildError, Is.Empty);
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
                Assert.That(await ExecuteNativeImageAsync(nativePath), Is.EqualTo(42));
                Assert.That(await ExecuteNativeImageAsync(hostedPath), Is.EqualTo(42));

                var (frontendExit, frontendOutput, frontendError) = await RunAsync(input,
                    "frontend-run", directory, "-", "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(hostedInput,
                    "jit", "-", "--runtime", policy, "--json");
                Assert.That(frontendExit, Is.EqualTo(42));
                Assert.That(frontendExit, Is.EqualTo(hostedExit));
                Assert.That(frontendOutput, Is.Empty);
                Assert.That(hostedOutput, Is.Empty);
                Assert.That(frontendError, Is.Empty);
                Assert.That(hostedError, Is.Empty);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(nativePath).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(hostedPath).ConfigureAwait(true);
        }

    }

    [Test]
    public async Task ScalarFrontendSupportsNestedNamespaceBlockInCompleteProgramsAsync()
    {
        const string source = "namespace Demo.Tools { namespace Inner { "
            + "alias Scalar = int32; Scalar Main() { Scalar result = Add(20, 22); return result; } "
            + "public Scalar Add(Scalar left, Scalar right) { return left + right; } } }";
        const string hostedSource = "namespace Demo.Tools.Inner; "
            + "alias Scalar = int32; Scalar Main() { Scalar result = Add(20, 22); return result; } "
            + "public Scalar Add(Scalar left, Scalar right) { return left + right; }";
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var namespaceStart = source.IndexOf("Inner", StringComparison.Ordinal);
        var nativePath = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"terrum-nested-namespace-{Guid.NewGuid():N}.native.exe");
        var hostedPath = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"terrum-nested-namespace-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var hostedInput = Encoding.UTF8.GetBytes(hostedSource);
                var (componentExit, componentOutput, componentError) = await RunAsync(input,
                    "frontend-component", directory, "-", "--runtime", policy, "--json");
                Assert.That(componentExit == 0, Is.True, $"{componentOutput}{componentError}");
                Assert.That(componentError, Is.Empty);
                using (var component = JsonDocument.Parse(componentOutput))
                {
                    var result = component.RootElement.GetProperty("result");
                    Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(88));
                    var functions = result.GetProperty("functions");
                    Assert.That(functions.EnumerateArray().Select(static function =>
                            function.GetProperty("qualifiedName").GetString()).ToArray(), Is.EqualTo(new[] { "Demo.Tools.Inner.Main", "Demo.Tools.Inner.Add" }));
                    foreach (var function in functions.EnumerateArray())
                    {
                        var span = function.GetProperty("namespaceNameSpan");
                        Assert.That(span.GetProperty("start").GetInt32(), Is.EqualTo(namespaceStart));
                        Assert.That(span.GetProperty("length").GetInt32(), Is.EqualTo("Inner".Length));
                    }
                }

                var (nativeBuildExit, nativeBuildOutput, nativeBuildError) = await RunAsync(input,
                    "frontend-build", directory, "-", "-o", nativePath,
                    "--runtime", policy, "--json");
                var (hostedBuildExit, hostedBuildOutput, hostedBuildError) = await RunAsync(hostedInput,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.That(nativeBuildExit == 0, Is.True, $"{nativeBuildOutput}{nativeBuildError}");
                Assert.That(hostedBuildExit == 0, Is.True, $"{hostedBuildOutput}{hostedBuildError}");
                Assert.That(nativeBuildError, Is.Empty);
                Assert.That(hostedBuildError, Is.Empty);
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
                Assert.That(await ExecuteNativeImageAsync(nativePath), Is.EqualTo(42));

                var (frontendExit, frontendOutput, frontendError) = await RunAsync(input,
                    "frontend-run", directory, "-", "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(hostedInput,
                    "jit", "-", "--runtime", policy, "--json");
                Assert.That(frontendExit, Is.EqualTo(42));
                Assert.That(frontendExit, Is.EqualTo(hostedExit));
                Assert.That(frontendOutput, Is.Empty);
                Assert.That(hostedOutput, Is.Empty);
                Assert.That(frontendError, Is.Empty);
                Assert.That(hostedError, Is.Empty);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(nativePath).ConfigureAwait(true);
            File.Delete(hostedPath);
        }
    }
    [TestCase("alias Scalar = int32; alias Scalar = int64; Scalar Main() { return 42; }")]
    [TestCase("alias Scalar = Missing; Scalar Main() { return 42; }")]
    [TestCase("alias Scalar = Count; alias Count = Scalar; Scalar Main() { return 42; }")]
    public async Task ScalarFrontendRejectsInvalidAliasesInNestedNamespaceLeafAsync(string declarations)
    {
        var source = Encoding.UTF8.GetBytes(
            $"namespace Demo.Tools {{ namespace Inner {{ {declarations} }} }}");
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(source,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
        }
    }
    [TestCase(12,true)]
    [TestCase(13,false)]
    public async Task ScalarFrontendPreservesNestedNamespaceAliasLimitAsync(
        int aliasCount, bool expectedSuccess)
    {
        var aliases = string.Join(' ', Enumerable.Range(0, aliasCount)
            .Select(index => $"alias Alias{index} = {(index == 0 ? "int32" : $"Alias{index - 1}")};"));
        var source = Encoding.UTF8.GetBytes(
            $"namespace Demo.Tools {{ namespace Inner {{ {aliases} Alias{aliasCount - 1} Main() {{ return 42; }} }} }}");
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(source,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.That(exit, Is.EqualTo(expectedSuccess ? 0 : 1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.EqualTo(expectedSuccess));
            if (expectedSuccess)
            {
                Assert.That(document.RootElement.GetProperty("result").GetProperty("functions")[0]
                        .GetProperty("qualifiedName").GetString(), Is.EqualTo("Demo.Tools.Inner.Main"));
            }
        }
    }
    [TestCase(3)]
    [TestCase(8)]
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
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(source,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            var functions = document.RootElement.GetProperty("result").GetProperty("functions");
            Assert.That(functions.EnumerateArray().Select(static function =>
                    function.GetProperty("qualifiedName").GetString()).ToArray(), Is.EqualTo(new[] { qualifiedNamespace + ".Main", qualifiedNamespace + ".Add" }));
            foreach (var function in functions.EnumerateArray())
            {
                var span = function.GetProperty("namespaceNameSpan");
                Assert.That(span.GetProperty("start").GetInt32(), Is.EqualTo(leafStart));
                Assert.That(span.GetProperty("length").GetInt32(), Is.EqualTo(namespaceNames[^1].Length));
            }

            var nativePath = Path.Combine(TestArtifactPaths.DirectoryPath,
                $"terrum-namespace-chain-{policy}-{Guid.NewGuid():N}.native.exe");
            var hostedPath = Path.Combine(TestArtifactPaths.DirectoryPath,
                $"terrum-namespace-chain-{policy}-{Guid.NewGuid():N}.hosted.exe");
            try
            {
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(source,
                    "frontend-build", directory, "-", "-o", nativePath,
                    "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(hostedSource,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.That(nativeExit == 0, Is.True, $"{nativeOutput}{nativeError}");
                Assert.That(hostedExit == 0, Is.True, $"{hostedOutput}{hostedError}");
                Assert.That(nativeError, Is.Empty);
                Assert.That(hostedError, Is.Empty);
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
                Assert.That(await ExecuteNativeImageAsync(nativePath), Is.EqualTo(42));
            }
            finally
            {
                await TestFileCleanup.DeleteWithRetryAsync(nativePath).ConfigureAwait(true);
                File.Delete(hostedPath);
            }
        }
    }

    [Test]
    public async Task ScalarFrontendRejectsNamespaceBlockChainsBeyondDepthLimitAsync()
    {
        const int depth = 9;
        var opening = string.Join(' ', Enumerable.Range(0, depth)
            .Select(static index => $"namespace Scope{index} {{"));
        var source = Encoding.UTF8.GetBytes(opening
            + " int32 Main() { return 42; } int32 Other() { return 0; }"
            + string.Concat(Enumerable.Repeat(" }", depth)));
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(source,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
        }
    }

    [Test]
    public async Task ScalarFrontendSupportsRepeatedSameNameNamespaceBlocksAsync()
    {
        const string source = "namespace /* first */ Demo.\U00010400 { "
            + "int32 Main() { return Add(20, 22); } } "
            + "namespace Demo.\U00010400 { public int32 Add(int32 left, int32 right) "
            + "{ return left + right; } } namespace Demo.\U00010400 { }";
        const string hostedSource = "namespace Demo.\U00010400; "
            + "int32 Main() { return Add(20, 22); } "
            + "public int32 Add(int32 left, int32 right) { return left + right; }";
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var firstNamespaceStart = source.IndexOf("Demo.\U00010400", StringComparison.Ordinal);
        var secondNamespaceStart = source.IndexOf("Demo.\U00010400", firstNamespaceStart + 1,
            StringComparison.Ordinal);

        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var input = Encoding.UTF8.GetBytes(source);
            var hostedInput = Encoding.UTF8.GetBytes(hostedSource);
            var (componentExit, componentOutput, componentError) = await RunAsync(input,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.That(componentExit == 0, Is.True, $"{componentOutput}{componentError}");
            Assert.That(componentError, Is.Empty);
            using (var component = JsonDocument.Parse(componentOutput))
            {
                var result = component.RootElement.GetProperty("result");
                Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(88));
                var functions = result.GetProperty("functions");
                Assert.That(functions.EnumerateArray().Select(static function =>
                        function.GetProperty("qualifiedName").GetString()).ToArray(), Is.EqualTo(new[] { "Demo.\U00010400.Main", "Demo.\U00010400.Add" }));
                var spans = functions.EnumerateArray()
                    .Select(static function => function.GetProperty("namespaceNameSpan")
                        .GetProperty("start").GetInt32())
                    .ToArray();
                Assert.That(spans, Is.EqualTo(new[] { firstNamespaceStart, secondNamespaceStart }));
            }

            var (frontendExit, frontendOutput, frontendError) = await RunAsync(input,
                "frontend-run", directory, "-", "--runtime", policy, "--json");
            var (hostedExit, hostedOutput, hostedError) = await RunAsync(hostedInput,
                "jit", "-", "--runtime", policy, "--json");
            Assert.That(frontendExit, Is.EqualTo(42));
            Assert.That(frontendExit, Is.EqualTo(hostedExit));
            Assert.That(frontendOutput, Is.Empty);
            Assert.That(hostedOutput, Is.Empty);
            Assert.That(frontendError, Is.Empty);
            Assert.That(hostedError, Is.Empty);

            var nativePath = Path.Combine(TestArtifactPaths.DirectoryPath,
                $"terrum-repeated-namespace-{policy}-{Guid.NewGuid():N}.native.exe");
            var hostedPath = Path.Combine(TestArtifactPaths.DirectoryPath,
                $"terrum-repeated-namespace-{policy}-{Guid.NewGuid():N}.hosted.exe");
            try
            {
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build", directory, "-", "-o", nativePath,
                    "--runtime", policy, "--json");
                var (hostedBuildExit, hostedBuildOutput, hostedBuildError) = await RunAsync(hostedInput,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.That(nativeExit == 0, Is.True, $"{nativeOutput}{nativeError}");
                Assert.That(hostedBuildExit == 0, Is.True, $"{hostedBuildOutput}{hostedBuildError}");
                Assert.That(nativeError, Is.Empty);
                Assert.That(hostedBuildError, Is.Empty);
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
                Assert.That(await ExecuteNativeImageAsync(nativePath), Is.EqualTo(42));
                Assert.That(await ExecuteNativeImageAsync(hostedPath), Is.EqualTo(42));
            }
            finally
            {
                await TestFileCleanup.DeleteWithRetryAsync(nativePath).ConfigureAwait(true);
                await TestFileCleanup.DeleteWithRetryAsync(hostedPath).ConfigureAwait(true);
            }
        }
    }
    [TestCase("namespace Demo.Tools { int32 Main() { return 42; } } namespace Other { int32 Next() { return 0; } }")]
    [TestCase("namespace Demo.Tools { alias Scalar = int32; int32 Main() { return 42; } } namespace Demo.Tools { int32 Next() { return 0; } }")]
    [TestCase("namespace Demo.Tools { alias Scalar = int32; namespace Inner { Scalar Main() { return 42; } } }")]
    [TestCase("namespace Demo.Tools { namespace Other { int32 Main() { return 42; } } int32 Root() { return 0; } }")]
    [TestCase("namespace Demo { namespace Tools { int32 Main() { return 42; } } namespace Other { int32 Next() { return 0; } } }")]
    [TestCase("namespace Demo { int32 Root() { return 0; } namespace Tools { int32 Main() { return 42; } } }")]
    [TestCase("namespace Demo { namespace Tools { int32 Main() { return 42; } } int32 Root() { return 0; } }")]
    [TestCase("namespace Demo.Tools { using Other; int32 Main() { return 42; } }")]
    [TestCase("namespace Demo.Tools { import Other; int32 Main() { return 42; } }")]
    [TestCase("int32 Before() { return 0; } namespace Demo.Tools { int32 Main() { return 42; } }")]
    [TestCase("namespace Demo.Tools { int32 Main() { return 42; } } int32 After() { return 0; }")]
    [TestCase("namespace Demo; namespace Other; int32 Main() { return 42; }")]
    [TestCase("using Demo; namespace Demo; int32 Main() { return 42; }")]
    [TestCase("namespace Demo; using Other; int32 Main() { return 42; }")]
    [TestCase("int32 Before() { return 0; } namespace Demo; int32 Main() { return 42; }")]
    public async Task ScalarFrontendRejectsUnsupportedNamespaceAndImportFormsAsync(string source)
    {
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
        }
    }
    [TestCase("void Main() {} void Other() {}",0)]
    [TestCase("void Main() => require true; void Other() {}",0)]
    [TestCase("int32 Main() { { return 42; } } int32 Unused() { return 0; }",42)]
    [TestCase("int32 Main() { _ = int32(1); return 42; } int32 Unused() { return 0; }",42)]
    [TestCase("void Main() { if (false) {} else if (true) {} } void Other() {}",0)]
    public async Task ScalarFrontendCurrentProgramIncludesSimpleAndStructuredFunctionsAsync(
        string source, int expected)
    {
        var root = RepositoryRoot();
        var native = CreateTestArtifactPath($"current-program-{Guid.NewGuid():N}.native.exe");
        var hosted = CreateTestArtifactPath($"current-program-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (componentExit, componentOutput, componentError) = await RunAsync(input,
                    "frontend-component", SelfHostPaths.SourceDirectory(root), "-",
                    "--runtime", policy, "--json");
                Assert.That(componentExit == 0, Is.True, $"{componentOutput}{componentError}");
                using var component = JsonDocument.Parse(componentOutput);
                var result = component.RootElement.GetProperty("result");
                AssertCurrentScalarFrontendVersion(result);
                Assert.That(result.GetProperty("functions").GetArrayLength(), Is.EqualTo(2));

                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build", SelfHostPaths.SourceDirectory(root), "-", "-o", native,
                    "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build", "-", "-o", hosted, "--runtime", policy, "--json");
                Assert.That(nativeExit == 0, Is.True, $"{nativeOutput}{nativeError}");
                Assert.That(hostedExit == 0, Is.True, $"{hostedOutput}{hostedError}");
                Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                Assert.That(await ExecuteNativeImageAsync(native), Is.EqualTo(expected));
                Assert.That(await ExecuteNativeImageAsync(hosted), Is.EqualTo(expected));
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(native).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(hosted).ConfigureAwait(true);
        }
    }

    [Test]
    public async Task ScalarFrontendProjectsCompleteProgramsBeyondSingleFunctionTokenWindowAsync()
    {
        var emptyReturns = string.Join(" ", Enumerable.Range(0, 9)
            .Select(static index => $"void Empty{index}() {{ return; }}"));
        var source = $"alias Number = int32; Number Main() {{ return 42; }} {emptyReturns}";
        Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length > 64, Is.True);

        var input = Encoding.UTF8.GetBytes(source);
        var (componentExit, componentOutput, componentError) = await RunAsync(input,
            "frontend-component", SelfHostPaths.SourceDirectory(RepositoryRoot()), "-",
            "--runtime", "fail-fast", "--json");
        Assert.That(componentExit == 0, Is.True, $"{componentOutput}{componentError}");
        using (var component = JsonDocument.Parse(componentOutput))
        {
            var result = component.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
            Assert.That(result.GetProperty("functions").GetArrayLength(), Is.EqualTo(10));
        }

        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }

    [Test]
    public async Task ScalarFrontendProjectsSingleFunctionsBeyond64TokensWithinBudgetsAsync()
    {
        var castDepth = string.Concat(Enumerable.Repeat("int32(", 11));
        var casts = castDepth + "42" + new string(')', 11);
        var source = $"int32 Main() => true ? {casts} : {casts};";
        Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length > 64, Is.True);

        var input = Encoding.UTF8.GetBytes(source);
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (componentExit, componentOutput, componentError) = await RunAsync(input,
                "frontend-component", SelfHostPaths.SourceDirectory(RepositoryRoot()), "-",
                "--runtime", policy, "--json");
            Assert.That(componentExit, Is.EqualTo(0));
            Assert.That(componentError, Is.Empty);
            using var component = JsonDocument.Parse(componentOutput);
            var result = component.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
            Assert.That(result.GetProperty("valid").GetBoolean(), Is.True);
        }

        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }

    [Test]
    public async Task ScalarFrontendProjectsLongFunctionAlongsideOtherProgramFunctionsAsync()
    {
        var declarations = string.Concat(Enumerable.Range(0, 8)
            .Select(static index => $"mut int32 x{index} = {index}; "));
        var expression = string.Concat(new string('(', 15), "42", new string(')', 15),
            " + 0 + 0 + 0 + 0 + 0 + 0");
        var longFunction = $"int32 Main() {{ {{ {declarations}return {expression} + Zero(); }} }}";
        Assert.That(Lexer.Lex(SourceText.From(longFunction)).Tokens.Length > 64, Is.True);
        var source = $"int32 Other() => 0; int32 Zero() => 0; {longFunction}";
        Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length > 64, Is.True);

        var input = Encoding.UTF8.GetBytes(source);
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (componentExit, componentOutput, componentError) = await RunAsync(input,
                "frontend-component", SelfHostPaths.SourceDirectory(RepositoryRoot()), "-",
                "--runtime", policy, "--json");
            Assert.That(componentExit, Is.EqualTo(0));
            Assert.That(componentError, Is.Empty);
            using var component = JsonDocument.Parse(componentOutput);
            var result = component.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
            var functions = result.GetProperty("functions");
            Assert.That(functions.GetArrayLength(), Is.EqualTo(3));
            var nodes = functions[2].GetProperty("nodes");
            Assert.That(nodes.GetArrayLength(), Is.InRange(33, 64));
            var callNode = Enumerable.Range(0, nodes.GetArrayLength())
                .Single(index => nodes[index].GetProperty("operation").GetInt32() == 105);
            Assert.That(callNode, Is.InRange(32, nodes.GetArrayLength() - 1));
        }

        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }
    [TestCase("int32 Main() { return Shift(21, 1); } int32 Shift(int32 value, int32 count) { return value << count; }")]
    [TestCase("int32 Main() { return Shift(-84, 1) == -42 ? 42 : 0; } int32 Shift(int32 value, int32 count) { return value >> count; }")]
    [TestCase("int32 Main() { return Shift(-1, 1) == 2147483647 ? 42 : 0; } int32 Shift(int32 value, int32 count) { return value >>> count; }")]
    [TestCase("int32 Main() { return Shift(1, 31) == 0x80000000 ? 42 : 0; } uint32 Shift(uint32 value, int32 count) { return value << count; }")]
    [TestCase("int32 Main() { return Shift(-84, 2) == -21 ? 42 : 0; } int64 Shift(int64 value, int32 count) { return value >> count; }")]
    [TestCase("int32 Main() { return Shift(0x8000000000000000, 63) == 1 ? 42 : 0; } uint64 Shift(uint64 value, int32 count) { return value >>> count; }")]
    public async Task ScalarFrontendProjectsCheckedIntegerShiftsAsync(string source)
    {
        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }
    [TestCase("int32 Main() { return Shift(21) == 42 ? 42 : 0; } int32 Shift(int32 value) { return value << 1; }")]
    [TestCase("int32 Main() { return Shift(1) == 0x80000000 ? 42 : 0; } uint32 Shift(uint32 value) { return value << 31; }")]
    [TestCase("int32 Main() { return Shift(-1) == 1 ? 42 : 0; } int64 Shift(int64 value) { return value >>> 63; }")]
    [TestCase("int32 Main() { return Shift(1) == 0x8000000000000000 ? 42 : 0; } uint64 Shift(uint64 value) { return value << 63; }")]
    public async Task ScalarFrontendProjectsCheckedIntegerShiftsWithLiteralCountsAsync(string source)
    {
        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }
    [TestCase("int32 Main() { int32 value = 21; int32 count = 1; return value << count; }")]
    [TestCase("int32 Main() { return Shift(84, 1); } int32 Shift(int32 value, int32 count) { int32 shifted = value; return shifted >> count; }")]
    [TestCase("int32 Main() { return Shift(21); } int32 Shift(int32 value) { int32 count = 1; return value << count; }")]
    [TestCase("int32 Main() { uint32 value = 0x80000000; int32 count = 31; return value >>> count == 1 ? 42 : 0; }")]
    [TestCase("int32 Main() { uint32 value = 1; return value << 31 == 0x80000000 ? 42 : 0; }")]
    [TestCase("int32 Main() { int32 count = 5; int32 value = 1 << count; return value == 32 ? 42 : 0; }")]
    [TestCase("int32 Main() { int32 count = 5; init int32 value; value = 1 << count; return value == 32 ? 42 : 0; }")]
    [TestCase("int32 Main() { int32 count = 2; var value = +(1 << count); return value == 4 ? 42 : 0; }")]
    [TestCase("int32 Main() { int32 count = 2; var value = (1 << count) << count; return value == 16 ? 42 : 0; }")]
    [TestCase("int32 Main() { int32 count = 2; var value = true ? 1 << count : 16; return value == 4 ? 42 : 0; }")]
    [TestCase("int32 Main() { int32 count = 2; var value = true ? 1 << count : count; return value == 4 ? 42 : 0; }")]
    [TestCase("int32 Main() { int32 count = 2; var value = (1 << count) + 1; return value == 5 ? 42 : 0; }")]
    [TestCase("int32 Main() { int32 count = 2; var value = true ? 1 << count : 2 << count; return value == 4 ? 42 : 0; }")]
    public async Task ScalarFrontendProjectsCheckedIntegerShiftsWithLocalOperandsAsync(string source)
    {
        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }
    [TestCase("int32 Main() { return Shift(5) == 32 ? 42 : 0; } int32 Shift(int32 count) { return 1 << count; }")]
    [TestCase("int32 Main() { return Shift(31) == 0x80000000 ? 42 : 0; } uint32 Shift(int32 count) { return 1 << count; }")]
    [TestCase("int32 Main() { return Shift(63) == 0x8000000000000000 ? 42 : 0; } uint64 Shift(int32 count) { return 1 << count; }")]
    [TestCase("int32 Main() { return Shift(1) == 2147483647 ? 42 : 0; } int32 Shift(int32 count) { return -1 >>> count; }")]
    [TestCase("int32 Main() { return Shift(0) == -2147483648 ? 42 : 0; } int32 Shift(int32 count) { return -2147483648 >>> count; }")]
    public async Task ScalarFrontendProjectsCheckedIntegerShiftsWithLiteralLeftOperandsAsync(string source)
    {
        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }

    [Test]
    public async Task ScalarFrontendPreservesLiteralLeftShiftTypeThroughComparisonAsync()
    {
        const string source = "int32 Main() { int32 count = 5; return Shift(count) ? 42 : 0; } bool Shift(int32 count) { return 1 << count == 32; }";
        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }
    [TestCase("int32 Shift(int32 count) { return 1 << count; }",388,1,1)]
    [TestCase("int64 Shift(int32 count) { return 1 >> count; }",389,3,1)]
    [TestCase("uint32 Shift(int32 count) { return 1 >>> count; }",390,2,1)]
    [TestCase("int32 Shift(int32 count) { return -2147483648 >>> count; }",1590,1,int.MinValue)]
    public async Task ScalarFrontendProjectsLiteralLeftShiftsInCompactProjectionAsync(
        string source, int operation, int type, int literalLow)
    {
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(0));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.That(result.GetProperty("valid").GetBoolean(), Is.True);
        Assert.That(result.GetProperty("whenTrue").GetProperty("operation").GetInt32(), Is.EqualTo(operation));
        Assert.That(result.GetProperty("whenTrue").GetProperty("type").GetInt32(), Is.EqualTo(type));
        Assert.That(result.GetProperty("whenTrue").GetProperty("leftParameter").GetInt32(), Is.EqualTo(literalLow));
        Assert.That(result.GetProperty("whenTrue").GetProperty("rightParameter").GetInt32(), Is.EqualTo(0));
    }
    [TestCase("int32 Main() { return 42; } int32 Shift(int32 value) { return value << 32; }")]
    [TestCase("int32 Main() { return 42; } int64 Shift(int64 value) { return value >> 64; }")]
    [TestCase("int32 Main() { return 42; } int32 Shift(int32 value) { return value << 2147483647; }")]
    [TestCase("int32 Main() { return 42; } int32 Shift(int32 value) { return value >>> -1; }")]
    [TestCase("int32 Main() { return 42; } int64 Shift(int64 value) { return value << -2147483648; }")]
    public async Task ScalarFrontendPreservesHostedLiteralShiftCountChecksAsync(string source)
    {
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-literal-shift-{Guid.NewGuid():N}");
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
                Assert.That(nativeExit, Is.EqualTo(0));
                Assert.That(hostedExit, Is.EqualTo(0));
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
    [TestCase("int32 Main() { return 42; } int32 Shift(int32 value) { return value << 2147483648; }")]
    [TestCase("int32 Main() { return 42; } int32 Shift(int32 value) { return value >> -2147483649; }")]
    public async Task ScalarFrontendRejectsShiftLiteralCountsOutsideInt32Async(string source)
    {
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(document.RootElement.GetProperty("result")
                .GetProperty("errorCode").GetInt32(), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task ScalarFrontendRejectsIntegerShiftWithNonInt32CountAsync()
    {
        const string source = "int32 Main() { return Shift(1, 1); } int32 Shift(int32 value, uint32 count) { return value << count; }";
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(document.RootElement.GetProperty("result")
            .GetProperty("errorCode").GetInt32(), Is.EqualTo(2));
    }
    [TestCase(64,true)]
    [TestCase(65,true)]
    public async Task ScalarFrontendAcceptsProgramsBeyondThePreviousWindowAndPreservesLaterFrameIndexesAsync(
        int scannerTokens, bool valid)
    {
        var firstModifier = scannerTokens == 65 ? "public " : "";
        var source = firstModifier + "int32 First() { "
            + string.Concat(Enumerable.Range(0, 6).Select(index => $"int32 x{index} = {index}; "))
            + "_ = true; return 1; } "
            + "int32 Second() { return 42; } "
            + "int32 Main() { return Second(); }";
        Assert.That(Lexer.Lex(SourceText.From(source)).Tokens.Length, Is.EqualTo(scannerTokens));

        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.That(exit, Is.EqualTo(valid ? 0 : 1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.EqualTo(valid));
            if (!valid)
            {
                continue;
            }

            var functions = document.RootElement.GetProperty("result").GetProperty("functions");
            Assert.That(functions.GetArrayLength(), Is.EqualTo(3));
            Assert.That(functions[1].GetProperty("index").GetInt32(), Is.EqualTo(1));
            var literalStart = source.IndexOf("42", StringComparison.Ordinal);
            Assert.That(global::System.Linq.Enumerable.Any(functions[1].GetProperty("nodes").EnumerateArray(),node =>
                node.GetProperty("operation").GetInt32() == 3
                    && node.GetProperty("low").GetInt32() == 42
                    && node.GetProperty("span").GetProperty("start").GetInt32() == literalStart
                    && node.GetProperty("span").GetProperty("length").GetInt32() == 2),Is.True);
            Assert.That(global::System.Linq.Enumerable.Any(functions[2].GetProperty("nodes").EnumerateArray(),static node =>
                node.GetProperty("operation").GetInt32() == 105
                    && node.GetProperty("function").GetInt32() == 1),Is.True);

            var (runExit, runOutput, runError) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-run", directory, "-", "--runtime", policy, "--json");
            Assert.That(runExit, Is.EqualTo(42));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);
        }
    }
    [TestCase(64,true)]
    [TestCase(65,false)]
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
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());

        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.That(exit, Is.EqualTo(valid ? 0 : 1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            Assert.That(root.GetProperty("success").GetBoolean(), Is.EqualTo(valid));
            if (!valid)
            {
                continue;
            }

            var result = root.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
            var functions = result.GetProperty("functions");
            Assert.That(functions.GetArrayLength(), Is.EqualTo(2));
            Assert.That(functions[0].GetProperty("nodes").GetArrayLength(), Is.EqualTo(nodeCount));
        }
    }

    [Test]
    public async Task ScalarFrontendCrossFunctionVoidStatementCliUsesCurrentProjectionAsync()
    {
        const string text = "public int32 Main() { Step(); return 42; } void Step() { }";
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var input = Encoding.UTF8.GetBytes(text);
        var (exit, output, error) = await RunAsync(input, "frontend-component",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit == 0, Is.True, $"{exit}: {output}{error}");
        using (var document = JsonDocument.Parse(output))
        {
            var result = document.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
            var main = result.GetProperty("functions")[0];
            Assert.That(global::System.Linq.Enumerable.Any(main.GetProperty("nodes").EnumerateArray(),static node => node.GetProperty("operation").GetInt32() == 105
                    && node.GetProperty("type").GetInt32() == 0),Is.True);
        }
        var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(runExit, Is.EqualTo(42));
        Assert.That(runOutput, Is.Empty);
        Assert.That(runError, Is.Empty);
    }

    [Test]
    public async Task ScalarFrontendCrossFunctionDoesNotReturnCliUsesCurrentProjectionAsync()
    {
        const string text = "[DoesNotReturn] void Halt() { require false; } public int32 Main() { if (false) { Halt(); } return 42; }";
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.That(exit == 0, Is.True, $"{exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                AssertCurrentScalarFrontendVersion(result);
                Assert.That(result.GetProperty("functions").GetArrayLength(), Is.EqualTo(2));
            }
            var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                directory, "-", "--runtime", runtime, "--json");
            Assert.That(runExit, Is.EqualTo(42));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);
        }
    }

    [Test]
    public async Task ScalarFrontendCrossFunctionOutputArgumentsCliUsesCurrentProjectionAsync()
    {
        const string text = "public int32 Main() { if (Set(40, out var value, out var extra)) { return value + extra; } return 0; } bool Set(int32 seed, out int32 value, out int32 extra) { value = seed + 1; extra = 1; return true; }";
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.That(exit == 0, Is.True, $"{exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                AssertCurrentScalarFrontendVersion(result);
                var main = result.GetProperty("functions")[0];
                Assert.That(main.GetProperty("outParameterMask").GetInt32(), Is.EqualTo(0));
                Assert.That(result.GetProperty("functions")[1]
                    .GetProperty("outParameterMask").GetInt32(), Is.EqualTo(6));
                Assert.That(global::System.Linq.Enumerable.Any(main.GetProperty("nodes").EnumerateArray(),static node => node.GetProperty("operation").GetInt32() == 104),Is.True);
            }
            var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                directory, "-", "--runtime", runtime, "--json");
            Assert.That(runExit, Is.EqualTo(42));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);
        }
    }

    [Test]
    public async Task ScalarFrontendCrossFunctionNonBooleanOutputCliUsesCurrentProjectionAsync()
    {
        const string text = "public int32 Main() { mut int32 value = 0; int32 status = Set(out value); return value + status; } int32 Set(out int32 value) { value = 40; return 2; }";
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.That(exit == 0, Is.True, $"{runtime}: {exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                AssertCurrentScalarFrontendVersion(result);
                Assert.That(result.GetProperty("functions")[1]
                    .GetProperty("outParameterMask").GetInt32(), Is.EqualTo(1));
            }
            var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                directory, "-", "--runtime", runtime, "--json");
            Assert.That(runExit, Is.EqualTo(42));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);
        }
    }

    [Test]
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
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.That(exit == 0, Is.True, $"{runtime}: {exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                AssertCurrentScalarFrontendVersion(result);
                Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("functions")[0].GetProperty("nodes")
                    .EnumerateArray(),static node => node.GetProperty("operation").GetInt32() == 104),Is.True);
            }
            var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                directory, "-", "--runtime", runtime, "--json");
            Assert.That(runExit, Is.EqualTo(42));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var policy = runtime == "fail-fast"
                ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
            var outputPath = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-frontend-outvar-{Guid.NewGuid():N}.dll");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(input, "frontend-build-library",
                    directory, "-", "-o", outputPath, "--runtime", runtime, "--json");
                Assert.That(buildExit == 0, Is.True, $"{runtime}: {buildExit}: {buildOutput}{buildError}");
                Assert.That(await File.ReadAllBytesAsync(outputPath), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["Main"], (NativeRuntimePolicy)policy)));
            }
            finally
            {
                File.Delete(outputPath);
            }
        }
    }
    [TestCase("public int32 Main() { bool success = Try(out var value); return success ? 42 : 0; } bool Try(out int32 value) { value = 42; return true; }",77,104)]
    [TestCase("public int32 Main() { bool success = Fill(true, init var value); return success ? 42 : 0; } bool Fill(bool publish, init ref<int32> value) { if (publish) { value = 42; return true; } return false; }",78,108)]
    public async Task ScalarFrontendOutputVariablesInInitializedLocalsRunAsync(
        string text, int projectionVersion, int operation)
    {
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.That(exit == 0, Is.True, $"{runtime}: {exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.Not.EqualTo(projectionVersion));
                AssertCurrentScalarFrontendVersion(result);
                Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("functions")[0].GetProperty("nodes")
                    .EnumerateArray(),node => node.GetProperty("operation").GetInt32() == operation),Is.True);
            }

            var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                directory, "-", "--runtime", runtime, "--json");
            Assert.That(runExit, Is.EqualTo(42));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);
        }
    }

    [Test]
    public async Task ScalarFrontendSingleFunctionNonBooleanOutputCliUsesCurrentProjectionAsync()
    {
        const string text = "public int32 Fill(int32 seed, out int32 value) { if (seed == 0) { value = 40; return 2; } return Fill(seed - 1, out value); }";
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var input = Encoding.UTF8.GetBytes(text);
        var outputDirectory = TestArtifactPaths.CreateDirectory("scanner-tests");
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.That(exit == 0, Is.True, $"{runtime}: {exit}: {output}{error}");
            using var document = JsonDocument.Parse(output);
            var result = document.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
            Assert.That(result.GetProperty("outParameterMask").GetInt32(), Is.EqualTo(2));
            Assert.That(result.GetProperty("initRefParameterMask").GetInt32(), Is.EqualTo(0));
            Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("nodes").EnumerateArray(),static node =>
                node.GetProperty("operation").GetInt32() == 102),Is.True);
            Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("nodes").EnumerateArray(),static node =>
                node.GetProperty("operation").GetInt32() == 103),Is.True);

            var path = Path.Combine(outputDirectory, $"single-init-ref-{Guid.NewGuid():N}.dll");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(input, "frontend-build-library",
                    directory, "-", "-o", path, "--runtime", runtime, "--json");
                Assert.That(buildExit == 0, Is.True, $"{runtime}: {buildExit}: {buildOutput}{buildError}");
                var policy = runtime == "fail-fast"
                    ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
                var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
                Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
                var hosted = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
                Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["Fill"], (NativeRuntimePolicy)policy)));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Test]
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
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.That(exit == 0, Is.True, $"{runtime}: {exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                AssertCurrentScalarFrontendVersion(result);
                Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("nodes").EnumerateArray(),static node =>
                    node.GetProperty("operation").GetInt32() == 104),Is.True);
            }

            var policy = runtime == "fail-fast"
                ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
            var outputPath = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-frontend-outvar-{Guid.NewGuid():N}.dll");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(input, "frontend-build-library",
                    directory, "-", "-o", outputPath, "--runtime", runtime, "--json");
                Assert.That(buildExit == 0, Is.True, $"{runtime}: {buildExit}: {buildOutput}{buildError}");
                Assert.That(await File.ReadAllBytesAsync(outputPath), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["Fill"], (NativeRuntimePolicy)policy)));
            }
            finally
            {
                File.Delete(outputPath);
            }
        }
    }

    [Test]
    public async Task ScalarFrontendCrossFunctionInitReferencesCliUsesCurrentProjectionAsync()
    {
        const string text = "public int32 Main() { require !Fill(false, init var value); return Fill(true, init value) ? value : 0; } bool Fill(bool publish, init ref<int32> value) { if (publish) { value = 42; return true; } return false; }";
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.That(exit == 0, Is.True, $"{exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                AssertCurrentScalarFrontendVersion(result);
                Assert.That(result.GetProperty("functions")[0]
                    .GetProperty("initRefParameterMask").GetInt32(), Is.EqualTo(0));
                Assert.That(result.GetProperty("functions")[1]
                    .GetProperty("initRefParameterMask").GetInt32(), Is.EqualTo(2));
                Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("functions")[0].GetProperty("nodes")
                    .EnumerateArray(),static node =>
                    node.GetProperty("operation").GetInt32() == 107),Is.True);
                Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("functions")[0].GetProperty("nodes")
                    .EnumerateArray(),static node =>
                    node.GetProperty("operation").GetInt32() == 108),Is.True);
            }
            var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                directory, "-", "--runtime", runtime, "--json");
            Assert.That(runExit, Is.EqualTo(42));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);
        }
    }

    [Test]
    public async Task ScalarFrontendSingleInitReferencesCliUsesCurrentProjectionAsync()
    {
        const string text = "public bool Fill(bool publish, init ref<int32> value) { if (publish) { value = 42; return true; } return Fill(true, init value); }";
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var input = Encoding.UTF8.GetBytes(text);
        var outputDirectory = TestArtifactPaths.CreateDirectory("scanner-tests");
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.That(exit == 0, Is.True, $"{exit}: {output}{error}");
            using var document = JsonDocument.Parse(output);
            var result = document.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
            Assert.That(result.GetProperty("outParameterMask").GetInt32(), Is.EqualTo(0));
            Assert.That(result.GetProperty("initRefParameterMask").GetInt32(), Is.EqualTo(2));
            Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("nodes").EnumerateArray(),static node =>
                node.GetProperty("operation").GetInt32() == 107),Is.True);

            var path = Path.Combine(outputDirectory, $"single-nonboolean-init-ref-{Guid.NewGuid():N}.dll");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(input, "frontend-build-library",
                    directory, "-", "-o", path, "--runtime", runtime, "--json");
                Assert.That(buildExit == 0, Is.True, $"{buildExit}: {buildOutput}{buildError}");
                var policy = runtime == "fail-fast"
                    ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
                var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
                Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
                var hosted = ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))();
                Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["Fill"], (NativeRuntimePolicy)policy)));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public async Task ScalarFrontendSingleNonBooleanInitReferenceCliUsesCurrentProjectionAsync()
    {
        const string text = "public int32 Fill(int32 seed, init ref<int32> value) { if (seed == 0) { value = 40; return 0; } return Fill(seed - 1, init value); }";
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.That(exit == 0, Is.True, $"{runtime}: {exit}: {output}{error}");
            using var document = JsonDocument.Parse(output);
            var result = document.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
            Assert.That(result.GetProperty("initRefParameterMask").GetInt32(), Is.EqualTo(2));
            Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("nodes").EnumerateArray(),static node =>
                node.GetProperty("operation").GetInt32() == 107),Is.True);

            var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"nonboolean-init-ref-{Guid.NewGuid():N}.dll");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(input, "frontend-build-library",
                    directory, "-", "-o", path, "--runtime", runtime, "--json");
                Assert.That(buildExit == 0, Is.True, $"{runtime}: {buildExit}: {buildOutput}{buildError}");
                var policy = runtime == "fail-fast"
                    ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
                var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
                Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
                var hosted = ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))();
                Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["Fill"], (NativeRuntimePolicy)policy)));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public async Task ScalarFrontendCrossFunctionNonBooleanAndVoidInitReferenceCliUsesCurrentProjectionAsync()
    {
        const string text = "public int32 Main() { Fill(init var value); SetVoid(init var other); return value + other; } int32 Fill(init ref<int32> value) { value = 40; return 0; } void SetVoid(init ref<int32> value) { value = 2; }";
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.That(exit == 0, Is.True, $"{runtime}: {exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                AssertCurrentScalarFrontendVersion(result);
                Assert.That(result.GetProperty("functions")[1]
                    .GetProperty("initRefParameterMask").GetInt32(), Is.EqualTo(1));
                Assert.That(result.GetProperty("functions")[2]
                    .GetProperty("initRefParameterMask").GetInt32(), Is.EqualTo(1));
            }

            var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                directory, "-", "--runtime", runtime, "--json");
            Assert.That(runExit, Is.EqualTo(42));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"cross-function-init-ref-{Guid.NewGuid():N}.dll");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(input, "frontend-build-library",
                    directory, "-", "-o", path, "--runtime", runtime, "--json");
                Assert.That(buildExit == 0, Is.True, $"{runtime}: {buildExit}: {buildOutput}{buildError}");
                var policy = runtime == "fail-fast"
                    ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
                var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
                Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
                var hosted = ((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = compilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))();
                Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["Main"], (NativeRuntimePolicy)policy)));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public async Task ScalarFrontendCrossFunctionCliSelectsEntryAndExportsInDeclarationOrderAsync()
    {
        const string text = "public int32 Next(int32 value) => value + 1; public int32 Main() => Next(41);";
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var outputDirectory = TestArtifactPaths.CreateDirectory("scanner-tests");
        foreach (var runtime in new[] { "fail-fast", "checked-exceptions" })
        {
            var input = Encoding.UTF8.GetBytes(text);
            var (exit, output, error) = await RunAsync(input, "frontend-component",
                directory, "-", "--runtime", runtime, "--json");
            Assert.That(exit == 0, Is.True, $"{exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                var result = document.RootElement.GetProperty("result");
                AssertCurrentScalarFrontendVersion(result);
                var functions = result.GetProperty("functions");
                Assert.That(functions.GetArrayLength(), Is.EqualTo(2));
                Assert.That(functions[0].GetProperty("index").GetInt32(), Is.EqualTo(0));
                Assert.That(functions[1].GetProperty("index").GetInt32(), Is.EqualTo(1));
                Assert.That(functions[1].GetProperty("nodes").EnumerateArray()
                    .Single(node => node.GetProperty("operation").GetInt32() == 105)
                    .GetProperty("function").GetInt32(), Is.EqualTo(0));
            }
            var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                directory, "-", "--runtime", runtime, "--json");
            Assert.That(runExit, Is.EqualTo(42));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var path = Path.Combine(outputDirectory, $"cross-function-library-{Guid.NewGuid():N}.dll");
            var executablePath = Path.ChangeExtension(path, ".exe");
            try
            {
                var (buildExit, buildOutput, buildError) = await RunAsync(input, "frontend-build-library",
                    directory, "-", "-o", path, "--runtime", runtime, "--json");
                Assert.That(buildExit == 0, Is.True, $"{buildExit}: {buildOutput}{buildError}");
                var policy = runtime == "fail-fast"
                    ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
                var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
                Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
                var hosted = ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = compilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))();
                Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["Next", "Main"], (NativeRuntimePolicy)policy)));
                var (executableExit, executableOutput, executableError) = await RunAsync(input, "frontend-build",
                    directory, "-", "-o", executablePath, "--runtime", runtime, "--json");
                Assert.That(executableExit == 0, Is.True, $"{executableExit}: {executableOutput}{executableError}");
                Assert.That(await File.ReadAllBytesAsync(executablePath), Is.EqualTo(NativeEmitter.Emit(hosted with { EntryPoint = 1 }, (NativeRuntimePolicy)policy)));
                using var process = Process.Start(new ProcessStartInfo(executablePath) {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }) ?? throw new InvalidOperationException("Could not execute frontend output.");
                await process.WaitForExitAsync();
                Assert.That(process.ExitCode, Is.EqualTo(42));
            }
            finally
            {
                File.Delete(path);
                await TestFileCleanup.DeleteWithRetryAsync(executablePath).ConfigureAwait(true);
            }
        }
    }
    [TestCase("int32 Main() { return Add(Next(Identity(20)), Next(Identity(21))) - 1; } int32 Next(int32 value) => value + 1; int32 Identity(int32 value) => value; int32 Add(int32 first, int32 second) => first + second;")]
    [TestCase("int32 Main() { return Sum(Identity(20), Identity(21)) == 41 ? 42 : 0; } int32 Identity(int32 value) => value; int32 Sum(int32 first, int32 second) => first + second;")]
    public async Task ScalarFrontendNestedValueCallsMatchHostedAsync(string source)
    {
        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }

    [Test]
    public async Task ScalarFrontendNestedCallsPreserveSourceOrderAsync()
    {
        const string source = "int32 Set(out int32 trace) { trace = 3; return trace; } int32 Pair(int32 first, int32 second) => first + second; int32 Main() { mut int32 trace = 0; return Pair(Set(out trace), trace) == 6 ? 42 : 0; }";
        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ScalarFrontendFourParameterCliProjectsFourthSignatureAndCallRootAsync(
        string runtime)
    {
        const string source = "public int32 Main() => Sum(fourth: 12, second: 10, first: 10, third: 10); int32 Sum(int32 first, int32 second, int32 third, int32 fourth) => first + second + third + fourth;";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", SelfHostPaths.SourceDirectory(RepositoryRoot()), "-",
            "--runtime", runtime, "--json");
        Assert.That(exit, Is.EqualTo(0));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(88));
        var functions = result.GetProperty("functions");
        Assert.That(functions.GetArrayLength(), Is.EqualTo(2));
        Assert.That(functions[1].GetProperty("parameterCount").GetInt32(), Is.EqualTo(4));
        Assert.That(functions[1].GetProperty("fourthType").GetInt32(), Is.EqualTo(1));
        Assert.That(functions[1].GetProperty("fourthName").GetProperty("start").GetInt32(), Is.EqualTo(source.IndexOf("int32 fourth", StringComparison.Ordinal) + "int32 ".Length));
        var call = global::System.Linq.Enumerable.Single(functions[0].GetProperty("nodes").EnumerateArray(), static node => node.GetProperty("operation").GetInt32() == 105);
        Assert.That(call.GetProperty("fourthArgumentRoot").GetInt32(), Is.InRange(0, 31));
        Assert.That(call.GetProperty("argumentParameterIndices").EnumerateArray()
                .Select(static parameter => parameter.GetInt32()), Is.EqualTo(new[] { 3, 1, 0, 2 }));
        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }
    [TestCase("public int32 Main() { init int32 result; Initialize(third: 3, second: 2, first: 1, output: init result); return result; } int32 Initialize(int32 first, int32 second, int32 third, init ref<int32> output) { output = 42; return 6; }")]
    [TestCase("public int32 Main() { int32 status = Initialize(third: 3, second: 2, first: 1, output: out var result); return result + status; } int32 Initialize(int32 first, int32 second, int32 third, out int32 output) { output = 36; return 6; }")]
    public async Task ScalarFrontendFourParameterCliProjectsFourthOutputAndRunsAsync(string source)
    {
        await AssertFrontendBuiltExecutableMatchesHostedAndRunsAsync(source);
    }
    [TestCase("int32 Main() { return Identity(IsReady()); } int32 Identity(int32 value) => value; bool IsReady() => true;","IsReady()",2)]
    [TestCase("int32 Main() { return Identity(Missing()); } int32 Identity(int32 value) => value;","Missing",3)]
    public async Task ScalarFrontendRejectsInvalidNestedCallsWithSourceSpansAsync(
        string source, string offending, int errorCode)
    {
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("errorCode").GetInt32(), Is.EqualTo(errorCode));
        var span = result.GetProperty("errorSpan");
        var expectedStart = source.IndexOf(offending, StringComparison.Ordinal);
        Assert.That(span.GetProperty("start").GetInt32(), Is.EqualTo(expectedStart));
        Assert.That(span.GetProperty("length").GetInt32(), Is.EqualTo(offending.Length));

        var hosted = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(hosted.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(hosted.Diagnostics,diagnostic => diagnostic.Span.Start == expectedStart
            && diagnostic.Span.Length == offending.Length),Is.True);
    }
    [TestCase(BoundRuntimePolicy.FailFast,"fail-fast")]
    [TestCase(BoundRuntimePolicy.CheckedExceptions,"checked-exceptions")]
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
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var outputDirectory = TestArtifactPaths.CreateDirectory("scanner-tests");
        var input = Encoding.UTF8.GetBytes(text);
        var (componentExit, componentOutput, componentError) = await RunAsync(input, "frontend-component",
            directory, "-", "--runtime", runtime, "--json");
        Assert.That(componentExit == 0, Is.True, $"{componentExit}: {componentOutput}{componentError}");
        using (var document = JsonDocument.Parse(componentOutput))
        {
            var result = document.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
            Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("functions")[2].GetProperty("nodes").EnumerateArray(),static node => node.GetProperty("operation").GetInt32() == 105),Is.True);
        }
        var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
            directory, "-", "--runtime", runtime, "--json");
        Assert.That(runExit, Is.EqualTo(6));
        Assert.That(runOutput, Is.Empty);
        Assert.That(runError, Is.Empty);

        var hosted = Compilation.Create(SyntaxTree.Parse(text), policy);
        Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
        var executablePath = Path.Combine(outputDirectory, $"named-calls-{Guid.NewGuid():N}.exe");
        try
        {
            var (buildExit, buildOutput, buildError) = await RunAsync(input, "frontend-build",
                directory, "-", "-o", executablePath, "--runtime", runtime, "--json");
            Assert.That(buildExit == 0, Is.True, $"{buildExit}: {buildOutput}{buildError}");
            Assert.That(await File.ReadAllBytesAsync(executablePath), Is.EqualTo(NativeEmitter.Emit(((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = hosted.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))(), (NativeRuntimePolicy)policy)));
        }
        finally
        {
            File.Delete(executablePath);
        }
    }
    [TestCase("int32 Set(out int32 trace, int32 value) { trace = value; return value; } int32 Main() { mut int32 trace = 0; return Set(value: 3, trace: out trace); }",3)]
    [TestCase("int32 Set(out int32 result, int32 value) { result = value; return value; } int32 Main() { return Set(value: 3, result: out var result) + result; }",6)]
    [TestCase("int32 Initialize(int32 value, init ref<int32> result) { result = value; return 0; } int32 Main() { init int32 result; int32 status = Initialize(result: init result, value: 7); return result + status; }",7)]
    [TestCase("int32 Initialize(int32 value, init ref<int32> result) { result = value; return 0; } int32 Main() { int32 status = Initialize(result: init var result, value: 7); return result + status; }",7)]
    [TestCase("void Set(out int32 result, int32 value) { result = value; } int32 Main() { Set(value: 6, result: out var result); return result; }",6)]
    [TestCase("void Initialize(int32 value, init ref<int32> result) { result = value; } int32 Main() { Initialize(result: init var result, value: 7); return result; }",7)]
    [TestCase("int32 Set(out int32 t, int32 v) { t = v; return v; } int32 Identity(int32 r) => r; int32 Main() { mut int32 t = 0; return Identity(r: Set(v: 3, t: out t)); }",3)]
    [TestCase("int32 Initialize(int32 v, init ref<int32> r) { r = v; return 0; } int32 Identity(int32 x) => x; int32 Main() { init int32 r; return Identity(x: Initialize(r: init r, v: 7)) + r; }",7)]
    public async Task ScalarFrontendNamedOutputCallsMatchHostedAsync(string text, int expected)
    {
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var input = Encoding.UTF8.GetBytes(text);
        foreach (var (policy, runtime) in new[]
        {
            (BoundRuntimePolicy.FailFast, "fail-fast"),
            (BoundRuntimePolicy.CheckedExceptions, "checked-exceptions"),
        })
        {
            var (componentExit, componentOutput, componentError) = await RunAsync(input,
                "frontend-component", directory, "-", "--runtime", runtime, "--json");
            Assert.That(componentExit == 0, Is.True, $"{componentExit}: {componentOutput}{componentError}");
            Assert.That(componentError, Is.Empty);
            using (var document = JsonDocument.Parse(componentOutput))
            {
                var result = document.RootElement.GetProperty("result");
                AssertCurrentScalarFrontendVersion(result);
                var functions = result.GetProperty("functions");
                Assert.That(global::System.Linq.Enumerable.Any(functions[functions.GetArrayLength() - 1].GetProperty("nodes").EnumerateArray(),static node => node.GetProperty("operation").GetInt32() == 105),Is.True);
            }

            var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
                directory, "-", "--runtime", runtime, "--json");
            Assert.That(runExit, Is.EqualTo(expected));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);

            var hosted = Compilation.Create(SyntaxTree.Parse(text), policy);
            Assert.That(hosted.HasErrors, Is.False, string.Join(Environment.NewLine, hosted.Diagnostics));
            Assert.That(NativeJit.Execute(
                ((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = hosted.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))(), (NativeRuntimePolicy)policy), Is.EqualTo(expected));
        }
    }
    [TestCase(BoundRuntimePolicy.FailFast,"fail-fast")]
    [TestCase(BoundRuntimePolicy.CheckedExceptions,"checked-exceptions")]
    public async Task ScalarFrontendNamedCallsBuildHostedParityLibraryAsync(
        BoundRuntimePolicy policy, string runtime)
    {
        const string text = """
            int32 Pair(int32 first, int32 second) => first + second;
            public int32 Value() => Pair(second: 3, first: 2);
            """;
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var input = Encoding.UTF8.GetBytes(text);
        var (componentExit, componentOutput, componentError) = await RunAsync(input, "frontend-component",
            directory, "-", "--runtime", runtime, "--json");
        Assert.That(componentExit == 0, Is.True, $"{componentExit}: {componentOutput}{componentError}");
        using (var document = JsonDocument.Parse(componentOutput))
        {
            AssertCurrentScalarFrontendVersion(document.RootElement.GetProperty("result"));
        }

        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var hosted = ((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = compilation.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))();
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"named-calls-{Guid.NewGuid():N}.dll");
        try
        {
            var (buildExit, buildOutput, buildError) = await RunAsync(input, "frontend-build-library",
                directory, "-", "-o", path, "--runtime", runtime, "--json");
            Assert.That(buildExit == 0, Is.True, $"{buildExit}: {buildOutput}{buildError}");
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["Value"], (NativeRuntimePolicy)policy)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ScalarFrontendNamedValueCallPreservesPositionalOutputParameterAsync()
    {
        const string text = "bool Fill(out int32 value, int32 other) { value = other; return true; } int32 Main() { if (Fill(out var result, other: 3)) { return result; } return 0; }";
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var input = Encoding.UTF8.GetBytes(text);
        var (componentExit, componentOutput, componentError) = await RunAsync(input, "frontend-component",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(componentExit == 0, Is.True, $"{componentExit}: {componentOutput}{componentError}");
        using (var document = JsonDocument.Parse(componentOutput))
        {
            var result = document.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
        }
        var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(runExit, Is.EqualTo(3));
        Assert.That(runOutput, Is.Empty);
        Assert.That(runError, Is.Empty);
    }

    [Test]
    public async Task ScalarFrontendNamedVoidCallPreservesPositionalOutputParameterAsync()
    {
        const string text = "void Fill(out int32 value, int32 other) { value = other; } int32 Main() { Fill(out var result, other: 3); return result; }";
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var input = Encoding.UTF8.GetBytes(text);
        var (componentExit, componentOutput, componentError) = await RunAsync(input, "frontend-component",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(componentExit == 0, Is.True, $"{componentExit}: {componentOutput}{componentError}");
        using (var document = JsonDocument.Parse(componentOutput))
        {
            var result = document.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
        }
        var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(runExit, Is.EqualTo(3));
        Assert.That(runOutput, Is.Empty);
        Assert.That(runError, Is.Empty);
    }
    [TestCase("int32 Main() { return Sum(unknown: 1, second: 2); } int32 Sum(int32 first, int32 second) { return first + second; }",3)]
    [TestCase("int32 Main() { return Sum(first: 1, first: 2); } int32 Sum(int32 first, int32 second) { return first + second; }",1)]
    [TestCase("int32 Main() { return Sum(second: 2, 1); } int32 Sum(int32 first, int32 second) { return first + second; }",1)]
    [TestCase("bool Fill(out int32 value, int32 other) { value = other; return true; } int32 Main() { Fill(other: 1, value: init var result); return 0; }",1)]
    public async Task ScalarFrontendRejectsInvalidNamedCallBindingsAsync(string text, int errorCode)
    {
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(text), "frontend-component",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(document.RootElement.GetProperty("result")
            .GetProperty("errorCode").GetInt32(), Is.EqualTo(errorCode));
    }

    [Test]
    public async Task ScalarFrontendNamedSelfCallsUseCurrentProjectionAsync()
    {
        const string text = "int32 Recurse(int32 first, int32 second, int32 third) => Recurse(first: second, second: first, third);";
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(text), "frontend-component",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit == 0, Is.True, $"{exit}: {output}{error}");
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        AssertCurrentScalarFrontendVersion(result);
        Assert.That(global::System.Linq.Enumerable.Any(result.GetProperty("nodes").EnumerateArray(),static node => node.GetProperty("operation").GetInt32() == 102),Is.True);
    }

    [Test]
    public async Task ScalarFrontendNamedCallsContinuePositionallyAfterInPositionLabelAsync()
    {
        const string text = "int32 Main() { return Select(first: 1, true, third: char16(65)); } int32 Select(int32 first, bool second, char16 third) { if (second) { return first * 100 + int32(third); } return 0; }";
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var input = Encoding.UTF8.GetBytes(text);
        var (componentExit, componentOutput, componentError) = await RunAsync(input, "frontend-component",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(componentExit == 0, Is.True, $"{componentExit}: {componentOutput}{componentError}");
        using (var document = JsonDocument.Parse(componentOutput))
        {
            var result = document.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
        }
        var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(runExit, Is.EqualTo(165));
        Assert.That(runOutput, Is.Empty);
        Assert.That(runError, Is.Empty);
    }
    [TestCase("int32 Take(int32 @await) => @await; int32 Main() { return Take(@await: 42); }")]
    [TestCase("int32 Take(int32 \U00010400) => \U00010400; int32 Main() { return Take(\U00010400: 42); }")]
    public async Task ScalarFrontendNamedCallsMatchEscapedAndSupplementaryNamesAsync(string text)
    {
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var input = Encoding.UTF8.GetBytes(text);
        var (componentExit, componentOutput, componentError) = await RunAsync(input, "frontend-component",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(componentExit == 0, Is.True, $"{componentExit}: {componentOutput}{componentError}");
        using (var document = JsonDocument.Parse(componentOutput))
        {
            var result = document.RootElement.GetProperty("result");
            AssertCurrentScalarFrontendVersion(result);
        }
        var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run",
            directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(runExit, Is.EqualTo(42));
        Assert.That(runOutput, Is.Empty);
        Assert.That(runError, Is.Empty);
    }
}
