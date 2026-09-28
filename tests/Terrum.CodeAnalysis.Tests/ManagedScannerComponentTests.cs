// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ManagedScannerComponentTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData("// trivia\r\nalpha 42", false)]
    [InlineData("\U00010400 42", false)]
    [InlineData("\uD800", true)]
    [InlineData("a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a a", true)]
    public async Task PublishedTokensMatchHostAndFailedScansDoNotPublishAsync(string text, bool fails)
    {
        var lexical = Lexer.Lex(SourceText.From(text));
        var expected = lexical.Tokens.ToArray();
        Assert.Equal(lexical.Diagnostics.Length != 0 || expected.Length > 64, fails);
        var source = new StringBuilder("""
            using Terrum.CodeAnalysis;
            int32 Main() {
                if (!TryScanManaged(out var chain)) {
                    return EXPECTED_FAILURE;
                }
            """.Replace("EXPECTED_FAILURE", fails ? "42" : "1", StringComparison.Ordinal));
        if (fails)
        {
            _ = source.Append("return 2; }");
        }
        else
        {
            _ = source.Append(CultureInfo.InvariantCulture, $"if (chain.Count != {expected.Length}) {{ return 3; }}");
            for (var index = 0; index < expected.Length; index++)
            {
                _ = source.Append(CultureInfo.InvariantCulture,
                    $"if (!chain.TryGet({index}, out var token{index}) || token{index}.Kind != {(int)expected[index].Kind} || token{index}.Range.Start != {expected[index].Span.Start} || token{index}.Range.Length != {expected[index].Span.Length}) {{ return 4; }}");
            }
            _ = source.Append(CultureInfo.InvariantCulture,
                $"if (!chain.TryClear() || ManagedHeap.Collect() != {expected.Length}) {{ return 5; }}");
            for (var index = 0; index < expected.Length; index++)
            {
                _ = source.Append(CultureInfo.InvariantCulture,
                    $"if (token{index}.Kind != {(int)expected[index].Kind} || token{index}.Range.Start != {expected[index].Span.Start}) {{ return 6; }}");
            }
            _ = source.Append(CultureInfo.InvariantCulture,
                $"if (!TryScanManaged(out var again) || again.Count != {expected.Length}) {{ return 7; }}");
            _ = source.Append(CultureInfo.InvariantCulture,
                $"if (!again.TryClear() || ManagedHeap.Collect() != {expected.Length}) {{ return 8; }}");
            _ = source.Append("return 42; }");
        }

        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(text, source.ToString(), (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData(63, false)]
    [InlineData(64, true)]
    public Task CapacityCountsTheEndOfFileTokenAsync(int identifiers, bool fails) =>
        PublishedTokensMatchHostAndFailedScansDoNotPublishAsync(
            string.Join(" ", Enumerable.Repeat("a", identifiers)), fails);

    [Theory]
    [InlineData(3, -1, 2)]
    [InlineData(5, 100, 2)]
    [InlineData(6, 1, 3)]
    public async Task InvalidHostRecordDoesNotPublishPartiallyBuiltChainAsync(int word, int value, int collected)
    {
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                int32 count = Scan();
                Host.Tokens[{{word}}] = {{value}};
                if (TryBuildManagedTokenChain(count, out var chain)) { return 1; }
                if (ManagedHeap.Collect() != {{collected}}) { return 2; }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync("alpha beta", entry, (BoundRuntimePolicy)policy).ConfigureAwait(true);
            Assert.Equal(42, NativeJit.Execute(program, policy));
            await ExecuteAotAsync(program, policy).ConfigureAwait(true);
        }
    }

    private static async Task<BoundProgram> CompileAsync(string text, string entry, BoundRuntimePolicy policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        string[] names = ["SourceRange", "Token", "ManagedTokenChain", "Scanner", "ManagedScanner"];
        var trees = new List<SyntaxTree>(names.Length + 1);
        foreach (var name in names)
        {
            var path = Path.Combine(root, "self-host", name + ".terrum");
            trees.Add(SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path));
        }
        trees.Add(SyntaxTree.Parse(entry, "managed-scanner-entry.terrum"));
        var compilation = Compilation.Create(trees, ScannerHost.CreateBuffers(SourceText.From(text)), policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    private static async Task ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var directory = Path.Combine(root, "artifacts", "scanner-tests");
        _ = Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"terrum-managed-scanner-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled managed scanner.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
            Assert.Equal(42, process.ExitCode);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
