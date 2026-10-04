// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task DotAttachmentMatchesHostedAndScannerComponentWithAtomicRetentionAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var scanner = Path.Combine(root, "self-host");
        var prefix = Path.Combine(root, "artifacts", $"dot-attachment-{Guid.NewGuid():N}");
        var hosted = prefix + ".hosted.exe";
        var component = prefix + ".component.exe";
        try
        {
            foreach (var dot in new[] { ".", " .", ". ", "/* left */.", "./// right\n" })
            {
                var source = "int32 Main() { return int32" + dot + "MaxValue - 2147483605; }";
                var (jitExit, jitOutput, jitError) = await RunAsync(Encoding.UTF8.GetBytes(source), "jit", "-", "--runtime", policy, "--json");
                Assert.Equal(42, jitExit);
                Assert.Empty(jitOutput);
                Assert.Empty(jitError);
                foreach (var arguments in new[] {
                    new[] { "build", "-", "-o", hosted, "--runtime", policy, "--json" },
                    new[] { "build-component", scanner, "-", "-o", component, "--runtime", policy, "--json" },
                })
                {
                    var (exit, _, error) = await RunAsync(Encoding.UTF8.GetBytes(source), arguments);
                    Assert.Equal(0, exit);
                    Assert.Empty(error);
                }
                Assert.Equal(42, await ExecuteNativeImageAsync(hosted));
                Assert.Equal(42, await ExecuteNativeImageAsync(component));
                Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(component));
            }
            var original = await File.ReadAllBytesAsync(component);
            foreach (var invalid in new[] {
                "int32 Main() { return int32 . MaxValue; }",
                "int32 Main() { return int32/* left */./// right\nMaxValue; }",
                "namespace N .Types; int32 Main() { return 42; }",
                "namespace N. Types; int32 Main() { return 42; }",
                "using N /* left */ .Types; int32 Main() { return 42; }",
                "using N./// right\nTypes; int32 Main() { return 42; }",
            })
            {
                var (exit, _, error) = await RunAsync(Encoding.UTF8.GetBytes(invalid), "build-component",
                    scanner, "-", "-o", component, "--runtime", policy, "--json");
                Assert.Equal(1, exit);
                Assert.Empty(error);
                Assert.Equal(original, await File.ReadAllBytesAsync(component));
            }
        }
        finally
        {
            File.Delete(hosted);
            File.Delete(component);
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task DotAdoptionPreservesBoundedNativeFrontendExecutionAndLibraryParityAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var prefix = Path.Combine(root, "artifacts", $"dot-scalar-{Guid.NewGuid():N}");
        var executable = prefix + ".exe";
        var library = prefix + ".frontend.dll";
        var hostedLibrary = prefix + ".hosted.dll";
        try
        {
            const string Source = "int32 Main(\n) { return int32(\n42\n); }";
            var (runExit, runOutput, runError) = await RunAsync(Encoding.UTF8.GetBytes(Source),
                "frontend-run", frontend, "-", "--runtime", policy, "--json");
            Assert.Equal(42, runExit);
            Assert.Empty(runOutput);
            Assert.Empty(runError);
            var (buildExit, _, buildError) = await RunAsync(Encoding.UTF8.GetBytes(Source),
                "frontend-build", frontend, "-", "-o", executable, "--runtime", policy, "--json");
            Assert.Equal(0, buildExit);
            Assert.Empty(buildError);
            Assert.Equal(42, await ExecuteNativeImageAsync(executable));
            const string Library = "public int32 @await(int32 @const, int32 some) { return @const + @some; }";
            foreach (var arguments in new[] {
                new[] { "frontend-build-library", frontend, "-", "-o", library, "--runtime", policy, "--json" },
                new[] { "build-library", "-", "--export", "await", "-o", hostedLibrary, "--runtime", policy, "--json" },
            })
            {
                var (exit, _, error) = await RunAsync(Encoding.UTF8.GetBytes(Library), arguments);
                Assert.Equal(0, exit);
                Assert.Empty(error);
            }
            Assert.Equal(await File.ReadAllBytesAsync(hostedLibrary), await File.ReadAllBytesAsync(library));
            var handle = NativeLibrary.Load(library);
            try
            {
                var invoke = Marshal.GetDelegateForFunctionPointer<ScalarBinary>(NativeLibrary.GetExport(handle, "await"));
                Assert.Equal(42, invoke(20, 22));
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
            var original = await File.ReadAllBytesAsync(executable);
            foreach (var unsupported in new[] {
                "int32 Main() { return int32.MaxValue; }",
                "int32 Main() { return int32 .MaxValue; }",
                "namespace N.Types; int32 Main() { return 42; }",
            })
            {
                var (exit, _, error) = await RunAsync(Encoding.UTF8.GetBytes(unsupported),
                    "frontend-build", frontend, "-", "-o", executable, "--runtime", policy, "--json");
                Assert.Equal(1, exit);
                Assert.Empty(error);
                Assert.Equal(original, await File.ReadAllBytesAsync(executable));
            }
        }
        finally
        {
            foreach (var path in new[] { executable, library, hostedLibrary })
            {
                File.Delete(path);
            }
        }
    }
}
