// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class SourceLineComponentTests
{
    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("\r\n\u0085\u2028\u2029")]
    [InlineData("\uD800\0\uDC00\uD800\uDC00")]
    public async Task SourceTextReadsExactUnitsAndValidatesRangesAsync(string text)
    {
        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var codeUnitChecks = string.Join(Environment.NewLine, text.Select((unit, index) =>
            $"if (!TryReadSourceCodeUnit({index}, init var codeUnit{index})) {{ return {index + 2}; }}" +
            $"{Environment.NewLine}if (int32(codeUnit{index}) != {(int)unit}) {{ return {index + 2}; }}"));
        var harness = SyntaxTree.Parse($$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (GetSourceTextLength() != {{text.Length}}) { return 1; }
                {{codeUnitChecks}}
                if (TryReadSourceCodeUnit(-1, init var before)
                    || TryReadSourceCodeUnit({{text.Length}}, init var after)
                    || TryReadSourceCodeUnit(int32.MaxValue, init var maximum))
                {
                    return 10;
                }
                if (!TryGetSourceRange(0, {{text.Length}}, init var whole)
                    || whole.Start != 0 || whole.Length != {{text.Length}})
                {
                    return 11;
                }
                if (!TryGetSourceRange({{text.Length}}, 0, init var eof)
                    || eof.Start != {{text.Length}} || eof.Length != 0)
                {
                    return 12;
                }
                if (TryGetSourceRange(-1, 0, init var negativeStart)
                    || TryGetSourceRange(0, -1, init var negativeLength)
                    || TryGetSourceRange({{text.Length}}, 1, init var pastEnd)
                    || TryGetSourceRange(int32.MaxValue, 1, init var maximumStart)
                    || TryGetSourceRange(0, int32.MaxValue, init var maximumLength))
                {
                    return 13;
                }
                return 0;
            }
            """, "entry.terrum");

        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var compilation = Compilation.Create(trees.Add(harness),
            [
                new HostBuffer("Source", PrimitiveType.Char16, [.. text.Select(unit => (int)unit)], false),
            ], (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine,
                compilation.SourceDiagnostics.Select(diagnostic =>
                    $"{diagnostic.SyntaxTree.Source.Path}: {diagnostic.Diagnostic}")));
            var program = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.Equal(0, NativeJit.Execute(program, policy));
            await ExecuteAsync(program, policy).ConfigureAwait(true);
        }
    }
}
