// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class ManagedScannerComponentTests
{
    [Theory]
    [InlineData("public int32 Main() => Next(41); int32 Next(int32 value) => value + 1;", 42)]
    [InlineData("public int32 Main() => Select(true, 42); int32 Select(bool choose, uint64 value) => choose ? int32(value) : 0;", 42)]
    [InlineData("public int32 Main() => Pack(Id(4), Id(2)); int32 Pack(int32 a, int32 b) => a * 10 + b; int32 Id(int32 value) => value;", 42)]
    [InlineData("int32 Next(int32 value) => value + 1; public int32 Main() => Next(41);", 42)]
    [InlineData("public int32 Main() { mut int32 n = 1; return Pack(n++, n++, n++); } int32 Pack(int32 a, int32 b, int32 c) => a * 100 + b * 10 + c;", 123)]
    [InlineData("public int32 Main() => Recur(3); int32 Recur(int32 n) => n == 0 ? 42 : Recur(n - 1);", 42)]
    [InlineData("public int32 Main() => First(3); int32 First(int32 n) => n == 0 ? 42 : Second(n - 1); int32 Second(int32 n) => First(n);", 42)]
    [InlineData("public int32 Main() { Next(); return 42; } int32 Next() => 7;", 42)]
    [InlineData("public void Main() { Next(); } int32 Next() => 42;", 0)]
    [InlineData("public int32 Main() { Next(); return 42; } void Next() { return; }", 42)]
    [InlineData("public void Main() { Next(); } void Next() { }", 0)]
    [InlineData("public int32 Main() { mut int32 n = 0; Sink(n++); return n; } void Sink(int32 value) { }", 1)]
    [InlineData("public int32 Main() { mut int32 n = 0; Sink(n++, n++, n++); return n; } void Sink(int32 a, int32 b, int32 c) { }", 3)]
    [InlineData("public int32 Main() { for (Sink(1); false; Sink(2)) { } return 42; } void Sink(int32 value) { }", 42)]
    [InlineData("[NoThrow] public void Main() { Step(); } [NoThrow] void Step() { }", 0)]
    [InlineData("public int32 Main() => Next(42); [NoThrow] int32 Next(int32 value) => value;", 42)]
    [InlineData("public int32 Main() { int32 result = Next(41); return result; } int32 Next(int32 value) { return value + 1; }", 42)]
    [InlineData("public int32 Main() { { int32 Next = 1; } return Next(); } int32 Next() => 42;", 42)]
    [InlineData("public int32 Main() => @await(41); int32 @await(int32 value) => value + 1;", 42)]
    [InlineData("public int32 Main() => int32(41); int32 int32(int32 value) => value + 1;", 42)]
    [InlineData("[NoThrow] public int32 Main() => Next(42); [NoThrow] int32 Next(int32 value) => value;", 42)]
    [InlineData("public int32 Main() => -Pack(1, 2); int32 Pack(int32 a, int32 b) => a + b;", -3)]
    [InlineData("public int32 Main() => -Pack(1, 2, 3); int32 Pack(int32 a, int32 b, int32 c) => a + b + c;", -6)]
    [InlineData("public int32 Main() => !Check(false, false) ? 42 : 0; bool Check(bool a, bool b) => a || b;", 42)]
    [InlineData("public int32 Main() => ~Pack(0, 0) == -1 ? 42 : 0; int32 Pack(int32 a, int32 b) => a + b;", 42)]
    public async Task ScalarFrontendCrossFunctionCallsMatchHostedAsync(string text, int expected)
    {
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var (bytes, programBytes, stage) = await ProduceScalarProgramAsync(text, policy);
            var source = SourceText.From(text);
            var result = ScalarFrontendHost.ReadResult(source, bytes, programBytes, (BoundRuntimePolicy)policy);
            Assert.True(result.IsValid);
            Assert.InRange(result.Functions.Length, 2, ScalarFrontendHost.MaximumFunctions);
            var actual = ScalarFrontendHost.BindLibrary(source, bytes, programBytes, (BoundRuntimePolicy)policy);
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.Equal(hosted.Functions.Select(static function => function.Name),
                actual.Functions.Select(static function => function.Name));
            Assert.Equal(hosted.Functions.Select(static function => function.Span),
                actual.Functions.Select(static function => function.Span));
            Assert.Equal(hosted.Functions.Select(static function => function.Body.Span),
                actual.Functions.Select(static function => function.Body.Span));
            Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["Main"], policy),
                NativeEmitter.EmitLibrary(actual, ["Main"], policy));
            var entry = actual.Functions.IndexOf(actual.Functions.Single(static function => function.Name == "Main"));
            Assert.Equal(expected, NativeJit.Execute(actual with { EntryPoint = entry }, policy));
            Assert.Equal(expected, NativeJit.Execute(hosted with { EntryPoint = entry }, policy));
            await ExecuteAotAsync(stage, policy);
        }
    }

    [Theory]
    [InlineData("bool", "Calc(true) ? 42 : 0")]
    [InlineData("char16", "int32(Calc(42))")]
    [InlineData("int32", "Calc(42)")]
    [InlineData("uint32", "int32(Calc(42))")]
    [InlineData("int64", "int32(Calc(42))")]
    [InlineData("uint64", "int32(Calc(42))")]
    public async Task ScalarFrontendCrossFunctionScalarTypesAsync(string type, string entry)
    {
        var text = $"int32 Main() => {entry}; public {type} Calc({type} value) => Id(value); {type} Id({type} value) => value;";
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, policy);
            var source = SourceText.From(text);
            var actual = ScalarFrontendHost.BindLibrary(source, bytes, programBytes, (BoundRuntimePolicy)policy);
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["Calc"], policy),
                NativeEmitter.EmitLibrary(actual, ["Calc"], policy));
            Assert.Equal(42, NativeJit.Execute(actual with { EntryPoint = 0 }, policy));
            Assert.Equal(42, NativeJit.Execute(hosted with { EntryPoint = 0 }, policy));
        }
    }

    [Theory]
    [InlineData("int32 Main() => Next(); int32 Next(int32 value) => value;")]
    [InlineData("int32 Main() => Next(1, 2); int32 Next(int32 value) => value;")]
    [InlineData("int32 Main() => Next(true); int32 Next(int32 value) => value;")]
    [InlineData("int32 Main() => Next(1); int32 Next(bool value) => 42;")]
    [InlineData("int32 Main() => @await(); int32 @await() => 42; int32 @await(int32 n) => n;")]
    [InlineData("int32 Main() => Next(); int32 Next() => 42; int32 Next(int32 n) => n;")]
    [InlineData("int32 Main() { int32 Next = 1; return Next(); } int32 Next() => 42;")]
    [InlineData("int32 Main(int32 Next) => Next(); int32 Next() => 42;")]
    [InlineData("int32 Main() => Missing(); int32 Next() => 42;")]
    [InlineData("int32 Main() => Next(); void Next() { return; }")]
    [InlineData("int32 Main() => Next(1); int32 Next(out int32 value) => 42;")]
    [InlineData("int32 Main(uint32 value) => Next(value); int32 Next(int32 value) => value;")]
    [InlineData("int32 Main() => 42; [DoesNotReturn] void Halt() { for (;;) { } }")]
    public async Task ScalarFrontendCrossFunctionRejectsInvalidSourcesAsync(string text)
    {
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, NativeRuntimePolicy.FailFast);
        var source = SourceText.From(text);
        var result = ScalarFrontendHost.ReadResult(source, bytes, programBytes, BoundRuntimePolicy.FailFast);
        Assert.False(result.IsValid);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.BindLibrary(source, bytes,
            programBytes, BoundRuntimePolicy.FailFast));
    }

    [Fact]
    public async Task ScalarFrontendCrossFunctionNoThrowRequiresNoThrowCalleeAsync()
    {
        const string text = "[NoThrow] int32 Main() => Next(); int32 Next() => 42;";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), BoundRuntimePolicy.FailFast);
        Assert.True(compilation.HasErrors);

        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, NativeRuntimePolicy.FailFast);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text),
            bytes, programBytes, BoundRuntimePolicy.FailFast));
    }

    [Fact]
    public async Task ScalarFrontendCrossFunctionVoidCallsRequireV75AndStatementRootsAsync()
    {
        const string text = "int32 Main() { Step(); return 42; } void Step() { }";
        var source = SourceText.From(text);
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, NativeRuntimePolicy.FailFast);
        var projection = ScalarFrontendHost.ReadResult(source, bytes, programBytes, BoundRuntimePolicy.FailFast);
        var call = Assert.Single(projection.Functions[0].Nodes,
            static node => node.Operation == 105 && node.Type == 0);
        var callIndex = projection.Functions[0].Nodes.IndexOf(call);
        const int frameWord = 4;
        var nodeTypeOffset = (frameWord + 4 + 42 + callIndex * 8 + 1) * sizeof(int);
        var statementRootOffset = (frameWord + 4 + 332 + 2) * sizeof(int);
        foreach (var (offset, value) in new[] {
            (0, 74), (nodeTypeOffset, 1), (statementRootOffset, -1),
        })
        {
            var previous = BinaryPrimitives.ReadInt32LittleEndian(programBytes.AsSpan(offset));
            BinaryPrimitives.WriteInt32LittleEndian(programBytes.AsSpan(offset), value);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes,
                programBytes, BoundRuntimePolicy.FailFast));
            BinaryPrimitives.WriteInt32LittleEndian(programBytes.AsSpan(offset), previous);
        }
    }

    [Fact]
    public async Task ScalarFrontendCrossFunctionIndependentlyAuditsFramesAndCallsAsync()
    {
        const string text = "int32 Main() => Next(41); int32 Next(int32 value) => value + 1; int32 Else(int32 value) => value;";
        var source = SourceText.From(text);
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, NativeRuntimePolicy.FailFast);
        var result = ScalarFrontendHost.ReadResult(source, bytes, programBytes, BoundRuntimePolicy.FailFast);
        Assert.Equal(3, result.Functions.Length);
        var first = result.Functions[0];
        var call = first.Nodes.IndexOf(first.Nodes.Single(static node => node.Operation == 105));
        Assert.Equal(1, first.CallTargets[call]);
        var frame = 4;
        foreach (var (word, value) in new[] {
            (0, 73), (1, 2), (2, 2), (3, 1),
            (frame, 1), (frame + 1, 1), (frame + 2, 1), (frame + 3, 1),
            (frame + 4, 73), (frame + 4 + 5, 5), (frame + 4 + 6, 1),
            (frame + 4 + 9, 1), (frame + 4 + 16, 1),
            (frame + 396 + call, -1), (frame + 396 + call, 0),
            (frame + 396 + call, 2), (frame + 396 + call, 3),
            (frame + 396, 1), (frame + 427, 0),
            (frame + 4 + 42 + call * 8, 102),
            (frame + 4 + 42 + call * 8 + 1, 5),
            (frame + 4 + 42 + call * 8 + 2, 0),
            (frame + 4 + 42 + call * 8 + 4, call),
            (frame + 4 + 42 + call * 8 + 5, 0),
            (frame + 4 + 42 + call * 8 + 7, 0),
            (4 + ScalarFrontendHost.ProgramFrameWordCount + 4 + 9, 0),
            (4 + 3 * ScalarFrontendHost.ProgramFrameWordCount, 1),
        })
        {
            var offset = word * sizeof(int);
            var previous = BinaryPrimitives.ReadInt32LittleEndian(programBytes.AsSpan(offset));
            BinaryPrimitives.WriteInt32LittleEndian(programBytes.AsSpan(offset), value);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes,
                programBytes, BoundRuntimePolicy.FailFast));
            BinaryPrimitives.WriteInt32LittleEndian(programBytes.AsSpan(offset), previous);
        }
        // Same signature does not authorize substituting another source target.
        var forged = SourceText.From(text.Replace("Next(41)", "Else(41)", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(forged, bytes,
            programBytes, BoundRuntimePolicy.FailFast));
        var duplicate = SourceText.From(text.Replace("Else", "Next", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(duplicate, bytes,
            programBytes, BoundRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData("int32 Main() { int32 slot = 1; return Next(); } int32 Next() => 42;")]
    [InlineData("int32 Main(int32 slot) => Next(); int32 Next() => 42;")]
    public async Task ScalarFrontendCrossFunctionReaderRejectsShadowedTargetAsync(string text)
    {
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, NativeRuntimePolicy.FailFast);
        Assert.True(ScalarFrontendHost.ReadResult(SourceText.From(text), bytes, programBytes,
            BoundRuntimePolicy.FailFast).IsValid);
        var shadowed = SourceText.From(text.Replace("slot", "Next", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(shadowed, bytes, programBytes,
            BoundRuntimePolicy.FailFast));
    }

    [Fact]
    public async Task ScalarFrontendCrossFunctionReaderRejectsConversionInsteadOfDeclaredCallAsync()
    {
        const string text = "int32 Main() => int32(41); int32 int32(int32 value) => value + 1;";
        var source = SourceText.From(text);
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, NativeRuntimePolicy.FailFast);
        var result = ScalarFrontendHost.ReadResult(source, bytes, programBytes, BoundRuntimePolicy.FailFast);
        var call = result.Nodes.IndexOf(result.Nodes.Single(static node => node.Operation == 105));
        var offset = 4 + 4 + 42 + call * 8;
        Set(offset, 83);
        Set(offset + 6, 0);
        Set(offset + 7, 0);
        Set(4 + 396 + call, -1);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes, programBytes,
            BoundRuntimePolicy.FailFast));

        void Set(int word, int value)
            => BinaryPrimitives.WriteInt32LittleEndian(programBytes.AsSpan(word * sizeof(int)), value);
    }

    [Fact]
    public async Task ScalarFrontendCrossFunctionReaderRejectsReorderedArgumentRootsAsync()
    {
        const string text = "int32 Main() => Pack(4, 2); int32 Pack(int32 a, int32 b) => a * 10 + b;";
        var source = SourceText.From(text);
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, NativeRuntimePolicy.FailFast);
        var result = ScalarFrontendHost.ReadResult(source, bytes, programBytes, BoundRuntimePolicy.FailFast);
        var call = result.Nodes.IndexOf(result.Nodes.Single(static node => node.Operation == 105));
        var offset = (4 + 4 + 42 + call * 8 + 4) * sizeof(int);
        var first = BinaryPrimitives.ReadInt32LittleEndian(programBytes.AsSpan(offset));
        var second = BinaryPrimitives.ReadInt32LittleEndian(programBytes.AsSpan(offset + sizeof(int)));
        BinaryPrimitives.WriteInt32LittleEndian(programBytes.AsSpan(offset), second);
        BinaryPrimitives.WriteInt32LittleEndian(programBytes.AsSpan(offset + sizeof(int)), first);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes, programBytes,
            BoundRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(false, 9)]
    [InlineData(true, 10)]
    public async Task ScalarFrontendCrossFunctionCapacityFollowsExistingTokenBoundAsync(bool isVoid, int count)
    {
        var text = string.Join(' ', Enumerable.Range(0, count)
            .Select(index => isVoid ? $"void F{index}() {{ }}" : $"int32 F{index}() => {index};"));
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, NativeRuntimePolicy.FailFast);
        Assert.Equal(count, ScalarFrontendHost.ReadResult(SourceText.From(text), bytes, programBytes,
            BoundRuntimePolicy.FailFast).Functions.Length);
    }

    [Fact]
    public async Task ScalarFrontendCrossFunctionCheckedFailureReachesCallerCatchAsync()
    {
        const string text = "public int32 Main() { unsafe { try { return Divide(0); } catch { return 42; } } } int32 Divide(int32 n) => 1 / n;";
        var source = SourceText.From(text);
        const NativeRuntimePolicy policy = NativeRuntimePolicy.CheckedExceptions;
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, policy);
        var actual = ScalarFrontendHost.BindLibrary(source, bytes, programBytes, (BoundRuntimePolicy)policy);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var hosted = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["Main"], policy),
            NativeEmitter.EmitLibrary(actual, ["Main"], policy));
        Assert.Equal(42, NativeJit.Execute(actual with { EntryPoint = 0 }, policy));
        Assert.Equal(42, NativeJit.Execute(hosted with { EntryPoint = 0 }, policy));
    }

    private static async Task<(byte[] Bytes, byte[] ProgramBytes, BoundProgram Stage)> ProduceScalarProgramAsync(
        string text, NativeRuntimePolicy policy)
    {
        var stage = await CompileAsync(text,
            "using Terrum.CodeAnalysis; int32 Main() { require TryPublishScalarFrontend(); return 42; }",
            (BoundRuntimePolicy)policy, "ScalarFrontendProjection");
        var native = NativeJit.ExecuteWithData(stage, policy);
        Assert.Equal(42, native.ExitCode);
        return (native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray(),
            native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.ProgramBufferIndex],
                ScalarFrontendHost.ProgramWordCount * sizeof(int)).ToArray(), stage);
    }
}
