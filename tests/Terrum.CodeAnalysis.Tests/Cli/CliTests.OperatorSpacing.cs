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
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int OperatorUnary(int value);
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task OperatorSpacingPreservesHostedComponentAndNativeExecutionAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var frontend = TerrumProjectPaths.SourceDirectory(root);
        var scanner = frontend;
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"operator-spacing-{Guid.NewGuid():N}");
        var hosted = prefix + ".hosted.exe";
        var component = prefix + ".component.exe";
        var native = prefix + ".native.exe";
        try
        {
            foreach (var source in new[] {
                "int32 Main() { return 20/* left */+//** right **//22; }",
                "int32 Main()/* left */=>/// right\n-( /* interior */ -42 );",
                "int32 Main() { mut int32 @await = 19; @await += 1; return (@await)++ + ++(@await); }",
            })
            {
                foreach (var command in new[] { "jit", "frontend-run" })
                {
                    var arguments = command == "jit"
                        ? new[] { command, "-", "--runtime", policy, "--json" }
                        : [command, frontend, "-", "--runtime", policy, "--json"];
                    var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), arguments).ConfigureAwait(false);
                    Assert.That(exit, Is.EqualTo(42));
                    Assert.That(output, Is.Empty);
                    Assert.That(error, Is.Empty);
                }
                foreach (var arguments in new[] {
                    new[] { "build", "-", "-o", hosted, "--runtime", policy, "--json" },
                    ["build-component", scanner, "-", "-o", component, "--runtime", policy, "--json"],
                    ["frontend-build", frontend, "-", "-o", native, "--runtime", policy, "--json"],
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
                Assert.That(await ExecuteNativeImageAsync(native).ConfigureAwait(false), Is.EqualTo(42));
                Assert.That(await File.ReadAllBytesAsync(component).ConfigureAwait(false), Is.EqualTo(await File.ReadAllBytesAsync(hosted).ConfigureAwait(false)));
                Assert.That(await File.ReadAllBytesAsync(native).ConfigureAwait(false), Is.EqualTo(await File.ReadAllBytesAsync(hosted).ConfigureAwait(false)));
            }
            foreach (var (command, path) in new[] { ("build-component", component), ("frontend-build", native) })
            {
                var original = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                foreach (var invalid in new[] {
                    "int32 Main() { return 20+22; }",
                    "int32 Main() { return 84 >>1; }",
                    "int32 Main() { return - /* gap */ 42; }",
                    "int32 Main() { mut int32 x= 42; return x; }",
                    "int32 Main() { mut int32 x = 42; x +=1; return x; }",
                    "int32 Main() { mut int32 x = 42; return x /* gap */++; }",
                    "int32 Main()=> 42;",
                    "int32 Main() =>42;",
                })
                {
                    var input = Encoding.UTF8.GetBytes(invalid);
                    var arguments = new[] {
                        command,
                        command == "build-component" ? scanner : frontend,
                        "-", "-o", path, "--runtime", policy, "--json",
                    };
                    var (exit, _, error) = command == "build-component"
                        ? await RunScannerCommandWithLayoutAsync(input, arguments).ConfigureAwait(false)
                        : await RunAsync(input, arguments).ConfigureAwait(false);
                    Assert.That(exit, Is.EqualTo(1));
                    Assert.That(error, Is.Empty);
                    Assert.That(await File.ReadAllBytesAsync(path).ConfigureAwait(false), Is.EqualTo(original));
                }
            }
        }
        finally
        {
            foreach (var path in new[] { hosted, component, native })
            {
                File.Delete(path);
            }
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task OperatorSpacingPreservesNativeLibraryBytesAndExportsAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var frontend = TerrumProjectPaths.SourceDirectory(root);
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"operator-library-{Guid.NewGuid():N}");
        var hosted = prefix + ".hosted.dll";
        var native = prefix + ".native.dll";
        try
        {
            foreach (var source in new[] {
                "public int32 Value(int32 x)/* left */=>//** right **//-(-x)/* left */+/* right */20;",
                "public int32 Value(int32 @await) { mut int32 x = @await - 3; x += 1; return x++ + ++x; }",
            })
            {
                var compiled = await AssertFrontendBuildLibraryParityAsync(
                    Encoding.UTF8.GetBytes(source), frontend, "Value", policy,
                    native, hosted).ConfigureAwait(false);
                if (!compiled)
                {
                    continue;
                }

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
        }
        finally
        {
            File.Delete(hosted);
            File.Delete(native);
        }
    }
}
