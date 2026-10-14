// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
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
                Assert.That(jitExit, Is.EqualTo(42));
                Assert.That(jitOutput, Is.Empty);
                Assert.That(jitError, Is.Empty);
                foreach (var arguments in new[] {
                    new[] { "build", "-", "-o", hosted, "--runtime", policy, "--json" },
                    new[] { "build-component", scanner, "-", "-o", component, "--runtime", policy, "--json" },
                })
                {
                    var (exit, _, error) = await RunAsync(Encoding.UTF8.GetBytes(source), arguments);
                    Assert.That(exit, Is.EqualTo(0));
                    Assert.That(error, Is.Empty);
                }
                Assert.That(await ExecuteNativeImageAsync(hosted), Is.EqualTo(42));
                Assert.That(await ExecuteNativeImageAsync(component), Is.EqualTo(42));
                Assert.That(await File.ReadAllBytesAsync(component), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
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
                Assert.That(exit, Is.EqualTo(1));
                Assert.That(error, Is.Empty);
                Assert.That(await File.ReadAllBytesAsync(component), Is.EqualTo(original));
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(hosted).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(component).ConfigureAwait(true);
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
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
            Assert.That(runExit, Is.EqualTo(42));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);
            var (buildExit, _, buildError) = await RunAsync(Encoding.UTF8.GetBytes(Source),
                "frontend-build", frontend, "-", "-o", executable, "--runtime", policy, "--json");
            Assert.That(buildExit, Is.EqualTo(0));
            Assert.That(buildError, Is.Empty);
            Assert.That(await ExecuteNativeImageAsync(executable), Is.EqualTo(42));
            const string Library = "public int32 @await(int32 @const, int32 some) { return @const + @some; }";
            foreach (var arguments in new[] {
                new[] { "frontend-build-library", frontend, "-", "-o", library, "--runtime", policy, "--json" },
                new[] { "build-library", "-", "--export", "await", "-o", hostedLibrary, "--runtime", policy, "--json" },
            })
            {
                var (exit, _, error) = await RunAsync(Encoding.UTF8.GetBytes(Library), arguments);
                Assert.That(exit, Is.EqualTo(0));
                Assert.That(error, Is.Empty);
            }
            Assert.That(await File.ReadAllBytesAsync(library), Is.EqualTo(await File.ReadAllBytesAsync(hostedLibrary)));
            var handle = NativeLibrary.Load(library);
            try
            {
                var invoke = Marshal.GetDelegateForFunctionPointer<ScalarBinary>(NativeLibrary.GetExport(handle, "await"));
                Assert.That(invoke(20, 22), Is.EqualTo(42));
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
            var original = await File.ReadAllBytesAsync(executable);
            foreach (var unsupported in new[] {
                "int32 Main() { return int32.MaxValue; }",
                "int32 Main() { return int32 .MaxValue; }",
                "namespace N.Types; namespace M.Types; int32 Main() { return 42; }",
            })
            {
                var (exit, _, error) = await RunAsync(Encoding.UTF8.GetBytes(unsupported),
                    "frontend-build", frontend, "-", "-o", executable, "--runtime", policy, "--json");
                Assert.That(exit, Is.EqualTo(1));
                Assert.That(error, Is.Empty);
                Assert.That(await File.ReadAllBytesAsync(executable), Is.EqualTo(original));
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
