// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
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
            new[] { "frontend-run", frontend, "-", "--runtime", policy, "--json" },
        })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source), arguments);
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
            Assert.That(await File.ReadAllBytesAsync(component), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
            Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
            const string Library = "public int32 @await(\nint32 @const, int32 some\n) { return int32(\nconst + @some\n); }";
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

            var originalImage = await File.ReadAllBytesAsync(native);
            var originalLibrary = await File.ReadAllBytesAsync(library);
            foreach (var gap in new[] { " ", "\n", "/* boundary */", "/// boundary\n" })
            {
                foreach (var invalid in new[] {
                    "int32 Main" + gap + "() { return 42; }",
                    "int32 Main() { return int32" + gap + "(42); }",
                })
                {
                    foreach (var arguments in new[] {
                        new[] { "build-component", scanner, "-", "-o", component, "--runtime", policy, "--json" },
                        new[] { "frontend-build", frontend, "-", "-o", native, "--runtime", policy, "--json" },
                    })
                    {
                        var (exit, _, error) = await RunAsync(Encoding.UTF8.GetBytes(invalid), arguments);
                        Assert.That(exit, Is.EqualTo(1));
                        Assert.That(error, Is.Empty);
                    }
                    Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(originalImage));
                    Assert.That(await File.ReadAllBytesAsync(component), Is.EqualTo(originalImage));
                }
                var (libraryExit, _, libraryError) = await RunAsync(Encoding.UTF8.GetBytes(
                    "public int32 @await" + gap + "(int32 @const, int32 some) { return const + some; }"),
                    "frontend-build-library", frontend, "-", "-o", library, "--runtime", policy, "--json");
                Assert.That(libraryExit, Is.EqualTo(1));
                Assert.That(libraryError, Is.Empty);
                Assert.That(await File.ReadAllBytesAsync(library), Is.EqualTo(originalLibrary));
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
