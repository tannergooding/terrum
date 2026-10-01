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
    public async Task AttachedConstructsHaveHostedComponentAndNativeFrontendParityAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var scanner = Path.Combine(frontend, "Scanner.terrum");
        const string Source = "int32 Main(\n) { return int32(\n ( /* grouping */ 42 )\n); }";
        foreach (var arguments in new[] {
            new[] { "jit", "-", "--runtime", policy, "--json" },
            new[] { "frontend-run", frontend, "-", "--runtime", policy, "--json" },
        })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source), arguments);
            Assert.Equal(42, exit);
            Assert.Empty(output);
            Assert.Empty(error);
        }
        var prefix = Path.Combine(root, "artifacts", $"attached-{Guid.NewGuid():N}");
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
                Assert.Equal(0, exit);
                Assert.Empty(error);
            }
            Assert.Equal(42, await ExecuteNativeImageAsync(hosted));
            Assert.Equal(42, await ExecuteNativeImageAsync(component));
            Assert.Equal(42, await ExecuteNativeImageAsync(native));
            Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(component));
            Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(native));
            const string Library = "public int32 @await(\nint32 @const, int32 some\n) { return int32(\nconst + @some\n); }";
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
                        Assert.Equal(1, exit);
                        Assert.Empty(error);
                    }
                    Assert.Equal(originalImage, await File.ReadAllBytesAsync(native));
                    Assert.Equal(originalImage, await File.ReadAllBytesAsync(component));
                }
                var (libraryExit, _, libraryError) = await RunAsync(Encoding.UTF8.GetBytes(
                    "public int32 @await" + gap + "(int32 @const, int32 some) { return const + some; }"),
                    "frontend-build-library", frontend, "-", "-o", library, "--runtime", policy, "--json");
                Assert.Equal(1, libraryExit);
                Assert.Empty(libraryError);
                Assert.Equal(originalLibrary, await File.ReadAllBytesAsync(library));
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
