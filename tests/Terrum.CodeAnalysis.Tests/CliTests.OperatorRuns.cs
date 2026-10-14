// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task OperatorRunsPreserveHostedScannerAndNativeParityAndRetainedOutputsAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var scanner = frontend;
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
                Assert.That(exit, Is.EqualTo(42));
                Assert.That(output, Is.Empty);
                Assert.That(error, Is.Empty);
            }
            var builds = new[] {
                new[] { "build", "-", "-o", paths[0], "--runtime", policy, "--json" },
                new[] { "build-component", scanner, "-", "-o", paths[1], "--runtime", policy, "--json" },
                new[] { "frontend-build", frontend, "-", "-o", paths[2], "--runtime", policy, "--json" },
            };
            for (var index = 0; index < builds.Length; index++)
            {
                var (exit, _, error) = await RunAsync(Encoding.UTF8.GetBytes(source), builds[index]);
                Assert.That(exit, Is.EqualTo(0));
                Assert.That(error, Is.Empty);
                Assert.That(await ExecuteNativeImageAsync(paths[index]), Is.EqualTo(42));
            }
            var expected = await File.ReadAllBytesAsync(paths[0]);
            Assert.That(await File.ReadAllBytesAsync(paths[1]), Is.EqualTo(expected));
            Assert.That(await File.ReadAllBytesAsync(paths[2]), Is.EqualTo(expected));
            foreach (var run in new[] { "+++", "!!", "+-", "===", ">>+", ">?>", "..", "..^", "?.", "..." })
            {
                var invalid = "int32 Main() { return 1 " + run + " 2; }";
                foreach (var arguments in builds)
                {
                    var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(invalid), arguments);
                    Assert.That(exit, Is.EqualTo(1));
                    Assert.That(error, Is.Empty);
                    Assert.That(output, Is.Not.Empty);
                }
                foreach (var path in paths)
                {
                    Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(expected));
                }
            }
            foreach (var run in new[] { "!!", ">?>" })
            {
                var invalid = "int32 Main() { return " + run + "1; }";
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(invalid),
                    "build-component", scanner, "-", "-o", paths[1], "--runtime", policy, "--json");
                Assert.That(exit, Is.EqualTo(1));
                Assert.That(error, Is.Empty);
                using var document = JsonDocument.Parse(output);
                var found = false;
                foreach (var diagnostic in document.RootElement.GetProperty("diagnostics").EnumerateArray())
                {
                    if (diagnostic.GetProperty("code").GetString() == "TRM0011")
                    {
                        Assert.That(diagnostic.GetProperty("span").GetProperty("start").GetInt32(), Is.EqualTo(invalid.IndexOf(run, StringComparison.Ordinal)));
                        Assert.That(diagnostic.GetProperty("span").GetProperty("length").GetInt32(), Is.EqualTo(run.Length));
                        found = true;
                    }
                }
                Assert.That(found, Is.True);
                if (run != ">?>")
                {
                    var (nativeExit, nativeOutput, nativeError) = await RunAsync(Encoding.UTF8.GetBytes(invalid),
                        "frontend-build", frontend, "-", "-o", paths[2], "--runtime", policy, "--json");
                    Assert.That(nativeExit, Is.EqualTo(1));
                    Assert.That(nativeError, Is.Empty);
                    using var failure = JsonDocument.Parse(nativeOutput);
                    var diagnostic = global::System.Linq.Enumerable.Single(failure.RootElement.GetProperty("diagnostics").EnumerateArray());
                    Assert.That(diagnostic.GetProperty("code").GetString(), Is.EqualTo("TRM0011"));
                    Assert.That(diagnostic.GetProperty("span").GetProperty("start").GetInt32(), Is.EqualTo(invalid.IndexOf(run, StringComparison.Ordinal)));
                    Assert.That(diagnostic.GetProperty("span").GetProperty("length").GetInt32(), Is.EqualTo(run.Length));
                    Assert.That(await File.ReadAllBytesAsync(paths[2]), Is.EqualTo(expected));
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
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
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
            Assert.That(hostedExit, Is.EqualTo(0));
            Assert.That(nativeExit, Is.EqualTo(0));
            Assert.That(hostedError, Is.Empty);
            Assert.That(nativeError, Is.Empty);
            Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
            var handle = NativeLibrary.Load(native);
            try
            {
                var invoke = Marshal.GetDelegateForFunctionPointer<OperatorUnary>(NativeLibrary.GetExport(handle, "Value"));
                Assert.That(invoke(22), Is.EqualTo(42));
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
