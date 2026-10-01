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
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PunctuationPredicate(bool condition);

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task ContextualPunctuationPreservesScalarJitExeDllAndAtomicOutputAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var scanner = Path.Combine(frontend, "Scanner.terrum");
        var prefix = Path.Combine(root, "artifacts", $"contextual-punctuation-{Guid.NewGuid():N}");
        var hosted = prefix + ".hosted.exe";
        var component = prefix + ".component.exe";
        var native = prefix + ".native.exe";
        var hostedLibrary = prefix + ".hosted.dll";
        var nativeLibrary = prefix + ".native.dll";
        try
        {
            foreach (var source in new[] {
                "int32 Main() { return true/* before */?/// after\n42//** before **//:/* after */1 / 0; }",
                "int32 Main() { mut int32 @await = 20; return (@await++ == 20) ? @await++ + @await - 1 : 1 / 0; }",
                "int32 Main() => false ? 1 / 0 : true ? 42 : 0;",
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
            foreach (var (command, path) in new[] { ("build", hosted), ("build-component", component), ("frontend-build", native) })
            {
                var original = await File.ReadAllBytesAsync(path);
                foreach (var expression in new[] { "true? 42 : 0", "true ?42 : 0", "true ? 42: 0", "true ? 42 :0", "true?42:0" })
                {
                    var source = "int32 Main() { return " + expression + "; }";
                    var arguments = command == "build"
                        ? new[] { command, "-", "-o", path, "--runtime", policy, "--json" }
                        : new[] { command, command == "build-component" ? scanner : frontend, "-", "-o", path, "--runtime", policy, "--json" };
                    var (exit, _, error) = await RunAsync(Encoding.UTF8.GetBytes(source), arguments);
                    Assert.Equal(1, exit);
                    Assert.Empty(error);
                    Assert.Equal(original, await File.ReadAllBytesAsync(path));
                    if (command == "frontend-build")
                    {
                        var (probeExit, output, probeError) = await RunAsync(Encoding.UTF8.GetBytes(source),
                            "frontend-component", frontend, "-", "--runtime", policy, "--json");
                        Assert.Equal(1, probeExit);
                        Assert.Empty(probeError);
                        using var document = JsonDocument.Parse(output);
                        var result = document.RootElement.GetProperty("result");
                        Assert.Equal(1, result.GetProperty("errorCode").GetInt32());
                        var offending = expression.StartsWith("true?", StringComparison.Ordinal)
                            || expression.StartsWith("true ?42", StringComparison.Ordinal) ? '?' : ':';
                        Assert.Equal(source.IndexOf(offending), result.GetProperty("errorSpan").GetProperty("start").GetInt32());
                        Assert.Equal(1, result.GetProperty("errorSpan").GetProperty("length").GetInt32());
                    }
                }
            }
            const string library = "public int32 Value(bool @await) => @await/* left */?/* right */42\r\n:\r\n7;";
            foreach (var arguments in new[] {
                new[] { "build-library", "-", "--export", "Value", "-o", hostedLibrary, "--runtime", policy, "--json" },
                new[] { "frontend-build-library", frontend, "-", "-o", nativeLibrary, "--runtime", policy, "--json" },
            })
            {
                var (exit, _, error) = await RunAsync(Encoding.UTF8.GetBytes(library), arguments);
                Assert.Equal(0, exit);
                Assert.Empty(error);
            }
            Assert.Equal(await File.ReadAllBytesAsync(hostedLibrary), await File.ReadAllBytesAsync(nativeLibrary));
            var handle = NativeLibrary.Load(nativeLibrary);
            try
            {
                var invoke = Marshal.GetDelegateForFunctionPointer<PunctuationPredicate>(NativeLibrary.GetExport(handle, "Value"));
                Assert.Equal(42, invoke(true));
                Assert.Equal(7, invoke(false));
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        finally
        {
            foreach (var path in new[] { hosted, component, native, hostedLibrary, nativeLibrary })
            {
                File.Delete(path);
            }
        }
    }
}
