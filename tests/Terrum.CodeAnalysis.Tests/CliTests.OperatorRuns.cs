// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task OperatorRunsPreserveHostedScannerAndNativeParityAndRetainedOutputsAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var scanner = Path.Combine(frontend, "Scanner.terrum");
        var prefix = Path.Combine(root, "artifacts", $"operator-run-{Guid.NewGuid():N}");
        var paths = new[] { prefix + ".hosted.exe", prefix + ".component.exe", prefix + ".native.exe" };
        const string source = "int32 Main() { mut int32 x = 20; return x++ + ++x; }";
        try
        {
            foreach (var arguments in new[] {
                new[] { "jit", "-", "--runtime", policy, "--json" },
                new[] { "frontend-run", frontend, "-", "--runtime", policy, "--json" },
            })
            {
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), arguments);
                Assert.Equal(42, exit);
                Assert.Empty(output);
                Assert.Empty(error);
            }
            var builds = new[] {
                new[] { "build", "-", "-o", paths[0], "--runtime", policy, "--json" },
                new[] { "build-component", scanner, "-", "-o", paths[1], "--runtime", policy, "--json" },
                new[] { "frontend-build", frontend, "-", "-o", paths[2], "--runtime", policy, "--json" },
            };
            for (var index = 0; index < builds.Length; index++)
            {
                var (exit, _, error) = await RunAsync(Encoding.UTF8.GetBytes(source), builds[index]);
                Assert.Equal(0, exit);
                Assert.Empty(error);
                Assert.Equal(42, await ExecuteNativeImageAsync(paths[index]));
            }
            var expected = await File.ReadAllBytesAsync(paths[0]);
            Assert.Equal(expected, await File.ReadAllBytesAsync(paths[1]));
            Assert.Equal(expected, await File.ReadAllBytesAsync(paths[2]));
            foreach (var run in new[] { "+++", "!!", "+-", "===", ">>+", ">?>", "..", "..^", "?.", "..." })
            {
                var invalid = "int32 Main() { return 1 " + run + " 2; }";
                foreach (var arguments in builds)
                {
                    var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(invalid), arguments);
                    Assert.Equal(1, exit);
                    Assert.Empty(error);
                    Assert.NotEmpty(output);
                }
                foreach (var path in paths)
                {
                    Assert.Equal(expected, await File.ReadAllBytesAsync(path));
                }
            }
            foreach (var run in new[] { "!!", "..^", ">?>" })
            {
                var invalid = "int32 Main() { return " + run + "1; }";
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(invalid),
                    "build-component", scanner, "-", "-o", paths[1], "--runtime", policy, "--json");
                Assert.Equal(1, exit);
                Assert.Empty(error);
                using var document = JsonDocument.Parse(output);
                var found = false;
                foreach (var diagnostic in document.RootElement.GetProperty("diagnostics").EnumerateArray())
                {
                    if (diagnostic.GetProperty("code").GetString() == (run == "..^" ? "TRM0012" : "TRM0011"))
                    {
                        Assert.Equal(invalid.IndexOf(run, StringComparison.Ordinal), diagnostic.GetProperty("span").GetProperty("start").GetInt32());
                        Assert.Equal(run.Length, diagnostic.GetProperty("span").GetProperty("length").GetInt32());
                        found = true;
                    }
                }
                Assert.True(found);
                if (run != ">?>")
                {
                    var (nativeExit, nativeOutput, nativeError) = await RunAsync(Encoding.UTF8.GetBytes(invalid),
                        "frontend-build", frontend, "-", "-o", paths[2], "--runtime", policy, "--json");
                    Assert.Equal(1, nativeExit);
                    Assert.Empty(nativeError);
                    using var failure = JsonDocument.Parse(nativeOutput);
                    var diagnostic = Assert.Single(failure.RootElement.GetProperty("diagnostics").EnumerateArray());
                    Assert.Equal(run == "..^" ? "TRM0012" : "TRM0011", diagnostic.GetProperty("code").GetString());
                    Assert.Equal(invalid.IndexOf(run, StringComparison.Ordinal), diagnostic.GetProperty("span").GetProperty("start").GetInt32());
                    Assert.Equal(run.Length, diagnostic.GetProperty("span").GetProperty("length").GetInt32());
                    Assert.Equal(expected, await File.ReadAllBytesAsync(paths[2]));
                }
            }
        }
        finally
        {
            foreach (var path in paths)
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task GroupedOperatorRunsPreserveNativeLibraryBytesAndExportsAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var prefix = Path.Combine(root, "artifacts", $"operator-run-library-{Guid.NewGuid():N}");
        var hosted = prefix + ".hosted.dll";
        var native = prefix + ".native.dll";
        const string source = "public int32 Value(int32 x) => ~(~x) + 20;";
        try
        {
            var (hostedExit, _, hostedError) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "build-library", "-", "--export", "Value", "-o", hosted, "--runtime", policy, "--json");
            var (nativeExit, _, nativeError) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-build-library", Path.Combine(root, "self-host"), "-", "-o", native, "--runtime", policy, "--json");
            Assert.Equal(0, hostedExit);
            Assert.Equal(0, nativeExit);
            Assert.Empty(hostedError);
            Assert.Empty(nativeError);
            Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(native));
            var handle = NativeLibrary.Load(native);
            try
            {
                var invoke = Marshal.GetDelegateForFunctionPointer<OperatorUnary>(NativeLibrary.GetExport(handle, "Value"));
                Assert.Equal(42, invoke(22));
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        finally
        {
            File.Delete(hosted);
            File.Delete(native);
        }
    }
}
