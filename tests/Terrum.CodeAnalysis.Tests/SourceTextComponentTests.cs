// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class SourceLineComponentTests
{
    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SourceTextReadsEveryUtf16CodeUnitFromFreshGrantsAsync(BoundRuntimePolicy policy)
    {
        var trees = await ReadComponentsAsync(includeTests: false, includeLines: false).ConfigureAwait(true);
        var entry = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return 0; }
            public int32 VerifySourceCodeUnits(int32 expectedFirst) {
                int32 length = GetSourceTextLength();
                mut int32 position = 0;
                while (position < length) {
                    if (!TryReadSourceCodeUnit(position, init var codeUnit)
                        || int32(codeUnit) != expectedFirst + position) {
                        return position + 1;
                    }
                    position = position + 1;
                }
                if (TryReadSourceCodeUnit(length, init var outOfRange)) {
                    return -1;
                }
                return 0;
            }
            """, "source-text-exhaustive-entry.terrum");
        var producerCompilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, [65], false) { IsInvocationScoped = true }],
            policy);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(producerCompilation.Program),
            ["Terrum.CodeAnalysis.VerifySourceCodeUnits"]);

        BoundProgram CreateConsumer(int expectedFirst)
        {
            var consumerTree = SyntaxTree.Parse($$"""
                namespace Terrum.CodeAnalysis;
                int32 Main() { return VerifySourceCodeUnits({{expectedFirst}}); }
                """, "source-text-exhaustive-consumer.terrum");
            var consumerCompilation = Compilation.Create(
                consumerTree, ModuleCompiler.CreateReference(producer), policy);
            Assert.False(consumerCompilation.HasErrors,
                string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
            var consumer = ModuleCompiler.CreateConsumer(
                Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
            return ModuleCompiler.Link(consumer, producer);
        }

        var emptyConsumer = CreateConsumer(0);
        Assert.Equal(0, NativeJit.ExecuteWithSourceGrant(emptyConsumer,
            [new HostBuffer("Source", PrimitiveType.Char16, [], false) { IsInvocationScoped = true }],
            (NativeRuntimePolicy)policy));

        const int batchSize = 16_384;
        for (var start = 0; start <= char.MaxValue; start += batchSize)
        {
            var count = Math.Min(batchSize, char.MaxValue + 1 - start);
            var grant = new HostBuffer("Source", PrimitiveType.Char16,
                [.. Enumerable.Range(start, count)], false) { IsInvocationScoped = true };
            Assert.Equal(0, NativeJit.ExecuteWithSourceGrant(
                CreateConsumer(start), [grant], (NativeRuntimePolicy)policy));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("\r\n\u0085\u2028\u2029")]
    [InlineData("\uD800\0\uDC00\uD800\uDC00")]
    public async Task SourceTextReadsExactUnitsAndValidatesRangesAsync(string text)
    {
        var trees = await ReadComponentsAsync(includeTests: false, includeLines: false).ConfigureAwait(true);
        var codeUnitChecks = string.Join(Environment.NewLine, text.Select((unit, index) =>
            $"if (!TryReadSourceCodeUnit({index}, init var codeUnit{index})) {{ return {index + 2}; }}" +
            $"{Environment.NewLine}if (int32(codeUnit{index}) != {(int)unit}) {{ return {index + 2}; }}"));
        var lines = SourceText.From(text).Lines;
        var lineChecks = string.Join(Environment.NewLine, lines.Select((line, index) =>
            $"if (!TryReadSourceLine({line.Span.Start}, init var contentLine{index}, init var includingBreakLine{index})" +
            $"{Environment.NewLine}    || contentLine{index}.Start != {line.Span.Start}" +
            $"{Environment.NewLine}    || contentLine{index}.Length != {line.Span.Length}" +
            $"{Environment.NewLine}    || includingBreakLine{index}.Start != {line.SpanIncludingLineBreak.Start}" +
            $"{Environment.NewLine}    || includingBreakLine{index}.Length != {line.SpanIncludingLineBreak.Length})" +
            $"{Environment.NewLine}{{ return {20 + index}; }}"));
        var harness = SyntaxTree.Parse($$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (GetSourceTextLength() != {{text.Length}}) { return 1; }
                {{codeUnitChecks}}
                {{lineChecks}}
                if (TryReadSourceLine(-1, init var beforeLine, init var beforeBreak)
                    || TryReadSourceLine({{text.Length + 1}}, init var afterLine, init var afterBreak))
                {
                    return 19;
                }
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
