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
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int OperatorUnary(int value);

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task OperatorSpacingPreservesHostedComponentAndNativeExecutionAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var scanner = Path.Combine(frontend, "Scanner.terrum");
        var prefix = Path.Combine(root, "artifacts", $"operator-spacing-{Guid.NewGuid():N}");
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
                        : new[] { command, frontend, "-", "--runtime", policy, "--json" };
                    var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), arguments);
                    Assert.Equal(42, exit);
                    Assert.Empty(output);
                    Assert.Empty(error);
                }
                foreach (var arguments in new[] {
                    new[] { "build", "-", "-o", hosted, "--runtime", policy, "--json" },
                    new[] { "build-component", scanner, "-", "-o", component, "--runtime", policy, "--json" },
                    new[] { "frontend-build", frontend, "-", "-o", native, "--runtime", policy, "--json" },
                })
                {
                    var (exit, _, error) = await RunAsync(Encoding.UTF8.GetBytes(source), arguments);
                    Assert.Equal(0, exit);
                    Assert.Empty(error);
                }
                Assert.Equal(42, await ExecuteNativeImageAsync(hosted));
                Assert.Equal(42, await ExecuteNativeImageAsync(component));
                Assert.Equal(42, await ExecuteNativeImageAsync(native));
                Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(component));
                Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(native));
            }
            foreach (var (command, path) in new[] { ("build-component", component), ("frontend-build", native) })
            {
                var original = await File.ReadAllBytesAsync(path);
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
                    var (exit, _, error) = await RunAsync(Encoding.UTF8.GetBytes(invalid), command,
                        command == "build-component" ? scanner : frontend,
                        "-", "-o", path, "--runtime", policy, "--json");
                    Assert.Equal(1, exit);
                    Assert.Empty(error);
                    Assert.Equal(original, await File.ReadAllBytesAsync(path));
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

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task OperatorSpacingPreservesNativeLibraryBytesAndExportsAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var prefix = Path.Combine(root, "artifacts", $"operator-library-{Guid.NewGuid():N}");
        var hosted = prefix + ".hosted.dll";
        var native = prefix + ".native.dll";
        try
        {
            foreach (var source in new[] {
                "public int32 Value(int32 x)/* left */=>//** right **//-(-x)/* left */+/* right */20;",
                "public int32 Value(int32 @await) { mut int32 x = @await - 3; x += 1; return x++ + ++x; }",
            })
            {
                var (hostedExit, _, hostedError) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "build-library", "-", "--export", "Value", "-o", hosted, "--runtime", policy, "--json");
                var (nativeExit, _, nativeError) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build-library", frontend, "-", "-o", native, "--runtime", policy, "--json");
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
        }
        finally
        {
            File.Delete(hosted);
            File.Delete(native);
        }
    }
}
