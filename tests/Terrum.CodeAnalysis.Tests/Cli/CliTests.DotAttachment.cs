// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

internal sealed partial class CliTests
{
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task DotAttachmentMatchesHostedAndScannerComponentWithAtomicRetentionAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var scanner = TerrumProjectPaths.SourceDirectory(root);
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"dot-attachment-{Guid.NewGuid():N}");
        var hosted = prefix + ".hosted.exe";
        var component = prefix + ".component.exe";
        try
        {
            foreach (var dot in new[] { ".", " .", ". ", "/* left */.", "./// right\n" })
            {
                var source = "int32 Main() { return int32" + dot + "MaxValue - 2147483605; }";
                var (jitExit, jitOutput, jitError) = await RunAsync(Encoding.UTF8.GetBytes(source), "jit", "-", "--runtime", policy, "--json").ConfigureAwait(false);
                Assert.That(jitExit, Is.EqualTo(42));
                Assert.That(jitOutput, Is.Empty);
                Assert.That(jitError, Is.Empty);
                foreach (var arguments in new[] {
                    new[] { "build", "-", "-o", hosted, "--runtime", policy, "--json" },
                    ["build-component", scanner, "-", "-o", component, "--runtime", policy, "--json"],
                })
                {
                    var input = Encoding.UTF8.GetBytes(source);
                    var (exit, _, error) = arguments[0] == "build-component"
                        ? await RunScannerCommandWithLayoutAsync(input, arguments).ConfigureAwait(false)
                        : await RunAsync(input, arguments).ConfigureAwait(false);
                    Assert.That(exit, Is.EqualTo(0));
                    Assert.That(error, Is.Empty);
                }
                Assert.That(await ExecuteNativeImageAsync(hosted).ConfigureAwait(false), Is.EqualTo(42));
                Assert.That(await ExecuteNativeImageAsync(component).ConfigureAwait(false), Is.EqualTo(42));
                Assert.That(await File.ReadAllBytesAsync(component).ConfigureAwait(false), Is.EqualTo(await File.ReadAllBytesAsync(hosted).ConfigureAwait(false)));
            }
            var original = await File.ReadAllBytesAsync(component).ConfigureAwait(false);
            foreach (var invalid in new[] {
                "int32 Main() { return int32 . MaxValue; }",
                "int32 Main() { return int32/* left */./// right\nMaxValue; }",
                "namespace N .Types; int32 Main() { return 42; }",
                "namespace N. Types; int32 Main() { return 42; }",
                "using N /* left */ .Types; int32 Main() { return 42; }",
                "using N./// right\nTypes; int32 Main() { return 42; }",
            })
            {
                var (exit, _, error) = await RunScannerCommandWithLayoutAsync(Encoding.UTF8.GetBytes(invalid), "build-component",
                    scanner, "-", "-o", component, "--runtime", policy, "--json").ConfigureAwait(false);
                Assert.That(exit, Is.EqualTo(1));
                Assert.That(error, Is.Empty);
                Assert.That(await File.ReadAllBytesAsync(component).ConfigureAwait(false), Is.EqualTo(original));
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
        var frontend = TerrumProjectPaths.SourceDirectory(root);
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"dot-scalar-{Guid.NewGuid():N}");
        var executable = prefix + ".exe";
        var hostedExecutable = prefix + ".hosted.exe";
        var library = prefix + ".frontend.dll";
        var hostedLibrary = prefix + ".hosted.dll";
        try
        {
            const string Source = "int32 Main(\n) { return int32(\n42\n); }";
            var (runExit, runOutput, runError) = await RunAsync(Encoding.UTF8.GetBytes(Source),
                "frontend-run", frontend, "-", "--runtime", policy, "--json").ConfigureAwait(false);
            Assert.That(runExit, Is.EqualTo(42));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);
            var (buildExit, _, buildError) = await RunAsync(Encoding.UTF8.GetBytes(Source),
                "frontend-build", frontend, "-", "-o", executable, "--runtime", policy, "--json").ConfigureAwait(false);
            Assert.That(buildExit, Is.EqualTo(0));
            Assert.That(buildError, Is.Empty);
            Assert.That(await ExecuteNativeImageAsync(executable).ConfigureAwait(false), Is.EqualTo(42));
            const string Library = "public int32 @await(int32 @const, int32 some) { return @const + @some; }";
            foreach (var arguments in new[] {
                new[] { "frontend-build-library", frontend, "-", "-o", library, "--runtime", policy, "--json" },
                ["build-library", "-", "--export", "await", "-o", hostedLibrary, "--runtime", policy, "--json"],
            })
            {
                var (exit, _, error) = await RunAsync(Encoding.UTF8.GetBytes(Library), arguments).ConfigureAwait(false);
                Assert.That(exit, Is.EqualTo(0));
                Assert.That(error, Is.Empty);
            }
            Assert.That(await File.ReadAllBytesAsync(library).ConfigureAwait(false), Is.EqualTo(await File.ReadAllBytesAsync(hostedLibrary).ConfigureAwait(false)));
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
            foreach (var dotSource in new[] {
                "int32 Main() { return int32.MaxValue; }",
                "int32 Main() { return int32 .MaxValue; }",
            })
            {
                var input = Encoding.UTF8.GetBytes(dotSource);
                var (frontendExit, frontendOutput, frontendError) = await RunAsync(input,
                    "frontend-build", frontend, "-", "-o", executable, "--runtime", policy, "--json").ConfigureAwait(false);
                Assert.That(frontendExit, Is.Zero, frontendOutput + frontendError);
                var (hostExit, hostOutput, hostError) = await RunAsync(input,
                    "build", "-", "-o", hostedExecutable, "--runtime", policy, "--json").ConfigureAwait(false);
                Assert.That(hostExit, Is.Zero, hostOutput + hostError);
                Assert.That(await File.ReadAllBytesAsync(executable).ConfigureAwait(false),
                    Is.EqualTo(await File.ReadAllBytesAsync(hostedExecutable).ConfigureAwait(false)));
                Assert.That(await ExecuteNativeImageAsync(executable).ConfigureAwait(false), Is.EqualTo(int.MaxValue));
            }
            var original = await File.ReadAllBytesAsync(executable).ConfigureAwait(false);
            foreach (var unsupported in new[] {
                "int32 Main() { return int32 . MaxValue; }",
                "namespace N.Types; namespace M.Types; int32 Main() { return 42; }",
            })
            {
                var (exit, _, error) = await RunAsync(Encoding.UTF8.GetBytes(unsupported),
                    "frontend-build", frontend, "-", "-o", executable, "--runtime", policy, "--json").ConfigureAwait(false);
                Assert.That(exit, Is.EqualTo(1));
                Assert.That(error, Is.Empty);
                Assert.That(await File.ReadAllBytesAsync(executable).ConfigureAwait(false), Is.EqualTo(original));
            }
        }
        finally
        {
            foreach (var path in new[] { executable, hostedExecutable, library, hostedLibrary })
            {
                File.Delete(path);
            }
        }
    }
}
