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
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PunctuationPredicate(bool condition);
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ContextualPunctuationPreservesScalarJitExeDllAndAtomicOutputAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var scanner = frontend;
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
                    Assert.That(exit, Is.EqualTo(42));
                    Assert.That(output, Is.Empty);
                    Assert.That(error, Is.Empty);
                }
                foreach (var arguments in new[] {
                    new[] { "build", "-", "-o", hosted, "--runtime", policy, "--json" },
                    new[] { "build-component", scanner, "-", "-o", component, "--runtime", policy, "--json" },
                    new[] { "frontend-build", frontend, "-", "-o", native, "--runtime", policy, "--json" },
                })
                {
                    var (exit, _, error) = await RunAsync(Encoding.UTF8.GetBytes(source), arguments);
                    Assert.That(exit, Is.EqualTo(0));
                    Assert.That(error, Is.Empty);
                }
                Assert.That(await ExecuteNativeImageAsync(hosted), Is.EqualTo(42));
                Assert.That(await ExecuteNativeImageAsync(component), Is.EqualTo(42));
                Assert.That(await ExecuteNativeImageAsync(native), Is.EqualTo(42));
                Assert.That(await File.ReadAllBytesAsync(component), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
                Assert.That(await File.ReadAllBytesAsync(native), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
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
                    Assert.That(exit, Is.EqualTo(1));
                    Assert.That(error, Is.Empty);
                    Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original));
                    if (command == "frontend-build")
                    {
                        var (probeExit, output, probeError) = await RunAsync(Encoding.UTF8.GetBytes(source),
                            "frontend-component", frontend, "-", "--runtime", policy, "--json");
                        Assert.That(probeExit, Is.EqualTo(1));
                        Assert.That(probeError, Is.Empty);
                        using var document = JsonDocument.Parse(output);
                        var result = document.RootElement.GetProperty("result");
                        Assert.That(result.GetProperty("errorCode").GetInt32(), Is.EqualTo(1));
                        var offending = expression.StartsWith("true?", StringComparison.Ordinal)
                            || expression.StartsWith("true ?42", StringComparison.Ordinal) ? '?' : ':';
                        Assert.That(result.GetProperty("errorSpan").GetProperty("start").GetInt32(), Is.EqualTo(source.IndexOf(offending)));
                        Assert.That(result.GetProperty("errorSpan").GetProperty("length").GetInt32(), Is.EqualTo(1));
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
                Assert.That(exit, Is.EqualTo(0));
                Assert.That(error, Is.Empty);
            }
            Assert.That(await File.ReadAllBytesAsync(nativeLibrary), Is.EqualTo(await File.ReadAllBytesAsync(hostedLibrary)));
            var handle = NativeLibrary.Load(nativeLibrary);
            try
            {
                var invoke = Marshal.GetDelegateForFunctionPointer<PunctuationPredicate>(NativeLibrary.GetExport(handle, "Value"));
                Assert.That(invoke(true), Is.EqualTo(42));
                Assert.That(invoke(false), Is.EqualTo(7));
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
