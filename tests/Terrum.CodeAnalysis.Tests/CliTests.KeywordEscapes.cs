// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task KeywordEscapesExecuteAcrossHostedComponentAndFrontendAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var frontend = SelfHostPaths.SourceDirectory(root);
        var scanner = frontend;
        const string Source = "/// heading\nint32 Main() { mut int32 @const = 0; while (const < 42) { @const += 1; } return const; }";
        foreach (var arguments in new[] {
            new[] { "jit", "-", "--runtime", policy, "--json" },
            new[] { "frontend-run", frontend, "-", "--runtime", policy, "--json" },
        })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source), arguments);
            Assert.That(exit, Is.EqualTo(42));
            Assert.That(output, Is.Empty);
            Assert.That(error, Is.Empty);
        }
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"keyword-escapes-{Guid.NewGuid():N}");
        var hosted = prefix + ".hosted.exe";
        var component = prefix + ".component.exe";
        var native = prefix + ".frontend.exe";
        var library = prefix + ".frontend.dll";
        var hostedLibrary = prefix + ".hosted.dll";
        try
        {
            foreach (var arguments in new[] {
                new[] { "build", "-", "-o", hosted, "--runtime", policy, "--json" },
                new[] { "build-component", scanner, "-", "-o", component, "--runtime", policy, "--json" },
                new[] { "frontend-build", frontend, "-", "-o", native, "--runtime", policy, "--json" },
            })
            {
                var (exit, _, error) = await RunAsync(Encoding.UTF8.GetBytes(Source), arguments);
                Assert.That(exit, Is.EqualTo(0));
                Assert.That(error, Is.Empty);
            }
            Assert.That(await ExecuteNativeImageAsync(hosted), Is.EqualTo(42));
            Assert.That(await ExecuteNativeImageAsync(component), Is.EqualTo(42));
            Assert.That(await ExecuteNativeImageAsync(native), Is.EqualTo(42));
            Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
            const string Library = "public int32 @await(int32 @const, int32 some) { mut var @return = const + @some; @return += 2; return @return; }";
            var (libraryExit, _, libraryError) = await RunAsync(Encoding.UTF8.GetBytes(Library),
                "frontend-build-library", frontend, "-", "-o", library, "--runtime", policy, "--json");
            Assert.That(libraryExit, Is.EqualTo(0));
            Assert.That(libraryError, Is.Empty);
            var (hostedExit, _, hostedError) = await RunAsync(Encoding.UTF8.GetBytes(Library),
                "build-library", "-", "--export", "await", "-o", hostedLibrary, "--runtime", policy, "--json");
            Assert.That(hostedExit, Is.EqualTo(0));
            Assert.That(hostedError, Is.Empty);
            Assert.That(await File.ReadAllBytesAsync(library), Is.EqualTo(await File.ReadAllBytesAsync(hostedLibrary)));
            var handle = NativeLibrary.Load(library);
            try
            {
                var invoke = Marshal.GetDelegateForFunctionPointer<ScalarBinary>(NativeLibrary.GetExport(handle, "await"));
                Assert.That(invoke(20, 20), Is.EqualTo(42));
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
            var original = await File.ReadAllBytesAsync(native);
            var (invalidExit, _, invalidError) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Main() { int32 @out = 42; return @out; }"),
                "frontend-build", frontend, "-", "-o", native, "--runtime", policy, "--json");
            Assert.That(invalidExit, Is.EqualTo(1));
            Assert.That(invalidError, Is.Empty);
            Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(original));
        }
        finally
        {
            foreach (var path in new[] { hosted, component, native, library, hostedLibrary })
            {
                File.Delete(path);
            }
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task KeywordEscapeScannerComponentPreservesRawNamesAndSpansAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        const string Source = "/* heading */ @await @const @union @via @out @int32 @ await";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source), "scan-component",
            SelfHostPaths.SourceDirectory(root), "-", "--runtime", policy, "--json");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(ScannerHost.SchemaVersion));
        var tokens = document.RootElement.GetProperty("tokens").EnumerateArray().ToArray();
        Assert.That(tokens.Select(token => Source.Substring(token.GetProperty("start").GetInt32(),
                token.GetProperty("length").GetInt32())), Is.EqualTo(new[] { "@await", "@const", "@union", "@via", "@out", "@int32", "@", "await", "" }));
        Assert.That(tokens.Select(token => token.GetProperty("kind").GetString()), Is.EqualTo(new[] { "Identifier", "Identifier", "Identifier", "Identifier", "Bad", "Bad", "Bad", "AwaitKeyword", "EndOfFile" }));
        Assert.That(tokens[0].GetProperty("start").GetInt32(), Is.EqualTo(14));
        Assert.That(tokens[0].GetProperty("length").GetInt32(), Is.EqualTo(6));
    }
}
