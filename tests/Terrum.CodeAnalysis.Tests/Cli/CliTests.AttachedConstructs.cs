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
    public async Task AttachedConstructsHaveHostedComponentAndNativeFrontendParityAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var frontend = TerrumProjectPaths.SourceDirectory(root);
        var scanner = frontend;
        const string Source = "int32 Main(\n) { return int32(\n ( /* grouping */ 42 )\n); }";
        foreach (var arguments in new[] {
            new[] { "jit", "-", "--runtime", policy, "--json" },
            ["frontend-run", frontend, "-", "--runtime", policy, "--json"],
        })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source), arguments).ConfigureAwait(false);
            Assert.That(exit, Is.EqualTo(42));
            Assert.That(output, Is.Empty);
            Assert.That(error, Is.Empty);
        }
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"attached-{Guid.NewGuid():N}");
        var hosted = prefix + ".hosted.exe";
        var component = prefix + ".component.exe";
        var native = prefix + ".frontend.exe";
        var library = prefix + ".frontend.dll";
        var hostedLibrary = prefix + ".hosted.dll";
        try
        {
            foreach (var arguments in new[] {
                new[] { "build", "-", "-o", hosted, "--runtime", policy, "--json" },
                ["build-component", scanner, "-", "-o", component, "--runtime", policy, "--json"],
                ["frontend-build", frontend, "-", "-o", native, "--runtime", policy, "--json"],
            })
            {
                var input = Encoding.UTF8.GetBytes(Source);
                var (exit, _, error) = arguments[0] == "build-component"
                    ? await RunScannerCommandWithLayoutAsync(input, arguments).ConfigureAwait(false)
                    : await RunAsync(input, arguments).ConfigureAwait(false);
                Assert.That(exit, Is.EqualTo(0));
                Assert.That(error, Is.Empty);
            }
            Assert.That(await ExecuteNativeImageAsync(hosted).ConfigureAwait(false), Is.EqualTo(42));
            Assert.That(await ExecuteNativeImageAsync(component).ConfigureAwait(false), Is.EqualTo(42));
            Assert.That(await ExecuteNativeImageAsync(native).ConfigureAwait(false), Is.EqualTo(42));
            Assert.That(await File.ReadAllBytesAsync(component).ConfigureAwait(false), Is.EqualTo(await File.ReadAllBytesAsync(hosted).ConfigureAwait(false)));
            Assert.That(await File.ReadAllBytesAsync(native).ConfigureAwait(false), Is.EqualTo(await File.ReadAllBytesAsync(hosted).ConfigureAwait(false)));
            const string FailingLibrary = "public int32 @await(\nint32 @const, int32 some\n) { return int32(\nconst + @some\n); }";
            if (policy == "checked-exceptions")
            {
                foreach (var arguments in new[] {
                    new[] { "frontend-build-library", frontend, "-", "-o", library, "--runtime", policy, "--json" },
                    ["build-library", "-", "--export", "await", "-o", hostedLibrary, "--runtime", policy, "--json"],
                })
                {
                    var (exit, output, error) = await RunAsync(
                        Encoding.UTF8.GetBytes(FailingLibrary), arguments).ConfigureAwait(false);
                    Assert.That(exit, Is.EqualTo(1), $"{arguments[0]}: {output}{error}");
                    Assert.That(output, Does.Contain("TRM4002"));
                    Assert.That(error, Is.Empty);
                }
                Assert.That(File.Exists(library), Is.False);
                Assert.That(File.Exists(hostedLibrary), Is.False);
            }
            var librarySource = policy == "checked-exceptions"
                ? "public int32 @await(\nint32 @const, int32 some\n) { return int32(\nconst\n); }"
                : FailingLibrary;
            var firstArgument = policy == "checked-exceptions" ? 42 : 20;
            var secondArgument = policy == "checked-exceptions" ? 0 : 22;
            foreach (var arguments in new[] {
                new[] { "frontend-build-library", frontend, "-", "-o", library, "--runtime", policy, "--json" },
                ["build-library", "-", "--export", "await", "-o", hostedLibrary, "--runtime", policy, "--json"],
            })
            {
                var (exit, output, error) = await RunAsync(
                    Encoding.UTF8.GetBytes(librarySource), arguments).ConfigureAwait(false);
                Assert.That(exit, Is.EqualTo(0),
                    $"{arguments[0]}: {output}{error}");
                Assert.That(error, Is.Empty);
            }
            Assert.That(await File.ReadAllBytesAsync(library).ConfigureAwait(false), Is.EqualTo(await File.ReadAllBytesAsync(hostedLibrary).ConfigureAwait(false)));
            var handle = NativeLibrary.Load(library);
            try
            {
                var invoke = Marshal.GetDelegateForFunctionPointer<ScalarBinary>(NativeLibrary.GetExport(handle, "await"));
                Assert.That(invoke(firstArgument, secondArgument), Is.EqualTo(42));
            }
            finally
            {
                NativeLibrary.Free(handle);
            }

            var originalImage = await File.ReadAllBytesAsync(native).ConfigureAwait(false);
            var originalLibrary = await File.ReadAllBytesAsync(library).ConfigureAwait(false);
            foreach (var gap in new[] { " ", "\n", "/* boundary */", "/// boundary\n" })
            {
                foreach (var invalid in new[] {
                    "int32 Main" + gap + "() { return 42; }",
                    "int32 Main() { return int32" + gap + "(42); }",
                })
                {
                    foreach (var arguments in new[] {
                        new[] { "build-component", scanner, "-", "-o", component, "--runtime", policy, "--json" },
                        ["frontend-build", frontend, "-", "-o", native, "--runtime", policy, "--json"],
                    })
                    {
                        var input = Encoding.UTF8.GetBytes(invalid);
                        var (exit, _, error) = arguments[0] == "build-component"
                            ? await RunScannerCommandWithLayoutAsync(input, arguments).ConfigureAwait(false)
                            : await RunAsync(input, arguments).ConfigureAwait(false);
                        Assert.That(exit, Is.EqualTo(1));
                        Assert.That(error, Is.Empty);
                    }
                    Assert.That(await File.ReadAllBytesAsync(native).ConfigureAwait(false), Is.EqualTo(originalImage));
                    Assert.That(await File.ReadAllBytesAsync(component).ConfigureAwait(false), Is.EqualTo(originalImage));
                }
                var (libraryExit, _, libraryError) = await RunAsync(Encoding.UTF8.GetBytes(
                    "public int32 @await" + gap + "(int32 @const, int32 some) { return const + some; }"),
                    "frontend-build-library", frontend, "-", "-o", library, "--runtime", policy, "--json").ConfigureAwait(false);
                Assert.That(libraryExit, Is.EqualTo(1));
                Assert.That(libraryError, Is.Empty);
                Assert.That(await File.ReadAllBytesAsync(library).ConfigureAwait(false), Is.EqualTo(originalLibrary));
            }
        }
        finally
        {
            foreach (var path in new[] { hosted, component, native, library, hostedLibrary })
            {
                File.Delete(path);
            }
        }
    }
}
