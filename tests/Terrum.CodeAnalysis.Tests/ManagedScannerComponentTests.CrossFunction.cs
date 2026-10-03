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
    [InlineData("public int32 Main() { mut int32 result = 0; if (Set(out result)) { return result; } return 0; } bool Set(out int32 value) { value = 42; return true; }", 42)]
    [InlineData("public int32 Main() { if (Set(40, out var value, out var extra)) { return value + extra; } return 0; } bool Set(int32 seed, out int32 value, out int32 extra) { value = seed + 1; extra = 1; return true; }", 42)]
    [InlineData("public int32 Main() { mut int32 value = 0; int32 status = Set(out value); return value + status; } int32 Set(out int32 value) { value = 40; return 2; }", 42)]
    [InlineData("public int32 Main() { mut int32 value = 0; return Set(out value) == 2 && value == 40 ? value : 0; } int32 Set(out int32 value) { value = 40; return 2; }", 40)]
    [InlineData("public int32 Main() { mut int32 value = 0; Fill(out value); return value; } void Fill(out int32 value) { value = 42; }", 42)]
    [InlineData("public int32 Main() { require !Fill(false, init var value); return Fill(true, init value) ? value : 0; } bool Fill(bool publish, init ref<int32> value) { if (publish) { value = 42; return true; } return false; }", 42)]
    [InlineData("public int32 Main() { for (Sink(1); false; Sink(2)) { } return 42; } void Sink(int32 value) { }", 42)]
    [InlineData("[DoesNotReturn] void Halt() { require false; } public int32 Main() { if (false) { Halt(); } return 42; }", 42)]
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

    [Fact]
    public async Task ScalarFrontendProgramProjectsCallerUnsafeCallsInsideExplicitScopesAsync()
    {
        const string text = "public unsafe int32 Danger(int32 value) => value + 1; public int32 Main() { return Safe(41); } int32 Safe(int32 value) { unsafe { return Danger(value); } } int32 Fill(init ref<int32> value) { value = 40; return 0; }";
        var source = SourceText.From(text);
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var (bytes, programBytes, stage) = await ProduceScalarProgramAsync(text, policy);
            Assert.Equal(83, BinaryPrimitives.ReadInt32LittleEndian(programBytes));

            var result = ScalarFrontendHost.ReadProgram(source, bytes, programBytes,
                (BoundRuntimePolicy)policy);
            Assert.True(result.Projection.IsValid);
            Assert.True(result.Projection.Functions[0].RequiresUnsafeCaller);
            Assert.False(result.Projection.Functions[1].RequiresUnsafeCaller);
            Assert.Equal(4, result.Projection.Functions.Length);

            var actual = Assert.IsType<BoundProgram>(result.Program);
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["Main"], policy),
                NativeEmitter.EmitLibrary(actual, ["Main"], policy));
            var entry = actual.Functions.IndexOf(actual.Functions.Single(static function => function.Name == "Main"));
            Assert.Equal(42, NativeJit.Execute(actual with { EntryPoint = entry }, policy));
            Assert.Equal(42, NativeJit.Execute(hosted with { EntryPoint = entry }, policy));
            await ExecuteAotAsync(stage, policy);

            var unsafeEffectOffset = (4 + 4 + 2) * sizeof(int);
            var unsafeEffects = BinaryPrimitives.ReadInt32LittleEndian(programBytes.AsSpan(unsafeEffectOffset));
            BinaryPrimitives.WriteInt32LittleEndian(programBytes.AsSpan(unsafeEffectOffset), unsafeEffects & ~8);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(source,
                bytes, programBytes, (BoundRuntimePolicy)policy));
            BinaryPrimitives.WriteInt32LittleEndian(programBytes.AsSpan(unsafeEffectOffset), unsafeEffects);

            BinaryPrimitives.WriteInt32LittleEndian(programBytes, 82);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(source,
                bytes, programBytes, (BoundRuntimePolicy)policy));
        }
    }

    [Theory]
    [InlineData("public unsafe int32 Danger() => 42; public int32 Main() => Danger();")]
    [InlineData("public unsafe int32 Danger() => 42; public unsafe int32 Main() => Danger();")]
    public async Task ScalarFrontendProgramRejectsCallerUnsafeCallsWithoutExplicitScopeAsync(string text)
    {
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, NativeRuntimePolicy.FailFast);
        Assert.Equal(83, BinaryPrimitives.ReadInt32LittleEndian(programBytes));
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(SourceText.From(text),
            bytes, programBytes, BoundRuntimePolicy.FailFast));
    }

    [Fact]
    public async Task ScalarFrontendProgramKeepsUnsafeExpressionScopesOutsideItsProjectionAsync()
    {
        const string text = "public unsafe int32 Danger() => 42; public int32 Main() => unsafe(Danger());";
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, NativeRuntimePolicy.FailFast);
        Assert.False(ScalarFrontendHost.ReadResult(SourceText.From(text), bytes, programBytes,
            BoundRuntimePolicy.FailFast).IsValid);
    }

    [Theory]
    [InlineData(77)]
    [InlineData(78)]
    public async Task ScalarFrontendProgramRejectsNonBooleanOutputsBeforeV80Async(int version)
    {
        const string text = "public int32 Main() { mut int32 value = 0; int32 status = Set(out value); return value + status; } int32 Set(out int32 value) { value = 40; return 2; }";
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, NativeRuntimePolicy.FailFast);
        BinaryPrimitives.WriteInt32LittleEndian(programBytes.AsSpan(0, sizeof(int)), version);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(SourceText.From(text),
            bytes, programBytes, BoundRuntimePolicy.FailFast));
    }

    [Fact]
    public async Task ScalarFrontendProgramProjectsNonBooleanAndVoidInitReferenceCompletionAsync()
    {
        const string text = "public int32 Main() { Fill(init var value); SetVoid(init var other); return value + other; } int32 Fill(init ref<int32> value) { value = 40; return 0; } void SetVoid(init ref<int32> value) { value = 2; }";
        var source = SourceText.From(text);
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var (bytes, programBytes, stage) = await ProduceScalarProgramAsync(text, policy);
            Assert.True(BinaryPrimitives.ReadInt32LittleEndian(programBytes.AsSpan(sizeof(int))) == 1,
                $"Error {BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(2 * sizeof(int)))} at "
                    + $"{BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(3 * sizeof(int)))}:"
                    + $"{BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4 * sizeof(int)))}; "
                    + $"single status {BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(sizeof(int)))}");
            Assert.Equal(82, BinaryPrimitives.ReadInt32LittleEndian(programBytes.AsSpan(0, sizeof(int))));
            var result = ScalarFrontendHost.ReadResult(source, bytes, programBytes, (BoundRuntimePolicy)policy);
            Assert.True(result.IsValid, $"Error {result.ErrorCode} at {result.ErrorSpan}");
            Assert.Equal(1, result.Functions[1].InitRefParameterMask);
            Assert.Equal(1, result.Functions[2].InitRefParameterMask);
            Assert.Contains(result.Functions[0].Nodes, static node => node.Operation == 108);

            var actual = ScalarFrontendHost.BindLibrary(source, bytes, programBytes, (BoundRuntimePolicy)policy);
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["Main"], policy),
                NativeEmitter.EmitLibrary(actual, ["Main"], policy));
            Assert.Equal(42, NativeJit.Execute(actual with { EntryPoint = 0 }, policy));
            await ExecuteAotAsync(stage, policy);

            BinaryPrimitives.WriteInt32LittleEndian(programBytes.AsSpan(0, sizeof(int)), 81);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(source,
                bytes, programBytes, (BoundRuntimePolicy)policy));
        }

        string[] incompleteTexts = [
            "public int32 Main() { return Fill(init var value); } int32 Fill(init ref<int32> value) { return 0; }",
            "public int32 Main() { Fill(init var value); return 42; } void Fill(init ref<int32> value) { }",
        ];
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            foreach (var incompleteText in incompleteTexts)
            {
                var (incompleteBytes, incompleteProgramBytes, _) = await ProduceScalarProgramAsync(
                    incompleteText, policy);
                Assert.Equal(82, BinaryPrimitives.ReadInt32LittleEndian(incompleteProgramBytes));
                Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(
                    SourceText.From(incompleteText), incompleteBytes, incompleteProgramBytes,
                    (BoundRuntimePolicy)policy));
            }
        }
    }

    [Fact]
    public async Task ScalarFrontendProgramAcceptsNonBooleanOutVarDestinationsAsync()
    {
        const string text = "public int32 Main() { int32 status = Set(out var value); return value + status; } int32 Set(out int32 value) { value = 40; return 2; }";
        var source = SourceText.From(text);
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, policy);
            var actual = ScalarFrontendHost.BindLibrary(source, bytes, programBytes, (BoundRuntimePolicy)policy);
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["Main"], policy),
                NativeEmitter.EmitLibrary(actual, ["Main"], policy));
            Assert.Equal(42, NativeJit.Execute(actual with { EntryPoint = 0 }, policy));
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
    [InlineData("int32 Main() { Set(42); return 0; } bool Set(out int32 value) { value = 42; return true; }")]
    [InlineData("int32 Main() { Set(out var value); return 0; } bool Set(int32 value) { return true; }")]
    [InlineData("bool Main() { mut int32 result = 0; if (Set(out result)) { return true; } return false; } int32 Set(out int32 value) { value = 42; return value; }")]
    [InlineData("int32 Main() { if (Fill(true, out var value)) { return value; } return 0; } bool Fill(bool publish, init ref<int32> value) { value = 42; return true; }")]
    [InlineData("int32 Main() { if (Fill(true, init var value)) { return value; } return 0; } bool Fill(bool publish, out int32 value) { value = 42; return true; }")]
    [InlineData("int32 Main() => Next(1); int32 Next(out int32 value) => 42;")]
    [InlineData("int32 Main(uint32 value) => Next(value); int32 Next(int32 value) => value;")]
    [InlineData("[DoesNotReturn] int32 Halt() => 42; int32 Main() => 0;")]
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

    [Theory]
    [InlineData("public int32 Main() { Halt(true); } [DoesNotReturn] void Halt(bool value) { require false; }")]
    [InlineData("public int32 Main() { if (true) { Halt(true); } } [DoesNotReturn] void Halt(bool value) { require false; }")]
    [InlineData("public int32 Main() { Halt(); } [NoThrow][DoesNotReturn] void Halt() { while (true) { } }")]
    public async Task ScalarFrontendCrossFunctionDoesNotReturnCallSuppressesFallthroughAsync(string text)
    {
        var source = SourceText.From(text);
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, policy);
            Assert.Equal(76, BinaryPrimitives.ReadInt32LittleEndian(programBytes));
            var actual = ScalarFrontendHost.BindLibrary(source, bytes, programBytes, (BoundRuntimePolicy)policy);
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.True(actual.Functions[1].DoesNotReturn);
            Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["Main"], policy),
                NativeEmitter.EmitLibrary(actual, ["Main"], policy));

            foreach (var version in new[] { 74, 75 })
            {
                BinaryPrimitives.WriteInt32LittleEndian(programBytes, version);
                Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source,
                    bytes, programBytes, (BoundRuntimePolicy)policy));
            }
            BinaryPrimitives.WriteInt32LittleEndian(programBytes, 76);

            var effectOffset = (4 + ScalarFrontendHost.ProgramFrameWordCount + 4 + 2) * sizeof(int);
            var effects = BinaryPrimitives.ReadInt32LittleEndian(programBytes.AsSpan(effectOffset));
            BinaryPrimitives.WriteInt32LittleEndian(programBytes.AsSpan(effectOffset), effects & ~4);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source,
                bytes, programBytes, (BoundRuntimePolicy)policy));
            BinaryPrimitives.WriteInt32LittleEndian(programBytes.AsSpan(effectOffset), effects);
        }
    }

    [Fact]
    public async Task ScalarFrontendCrossFunctionOutParametersRequireProjection77AndExactModesAsync()
    {
        const string text = "public int32 Main() { mut int32 result = 0; if (Set(out result)) { return result; } return 0; } bool Set(out int32 value) { value = 42; return true; }";
        var source = SourceText.From(text);
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, NativeRuntimePolicy.FailFast);
        Assert.Equal(77, BinaryPrimitives.ReadInt32LittleEndian(programBytes));
        Assert.True(ScalarFrontendHost.ReadResult(source, bytes, programBytes,
            BoundRuntimePolicy.FailFast).IsValid);
        var modeOffset = (4 + ScalarFrontendHost.ProgramFrameWordCount + 4 + 16) * sizeof(int);
        var mode = BinaryPrimitives.ReadInt32LittleEndian(programBytes.AsSpan(modeOffset));
        Assert.Equal(1, mode);
        foreach (var version in new[] { 74, 75, 76 })
        {
            BinaryPrimitives.WriteInt32LittleEndian(programBytes, version);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source,
                bytes, programBytes, BoundRuntimePolicy.FailFast));
        }
        BinaryPrimitives.WriteInt32LittleEndian(programBytes, 77);
        BinaryPrimitives.WriteInt32LittleEndian(programBytes.AsSpan(modeOffset), 0);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source,
            bytes, programBytes, BoundRuntimePolicy.FailFast));
    }

    [Fact]
    public async Task ScalarFrontendCrossFunctionInitReferencesRequireProjection78AndExactModesAsync()
    {
        const string text = "public int32 Main() { require !Fill(false, init var value); return Fill(true, init value) ? value : 0; } bool Fill(bool publish, init ref<int32> value) { if (publish) { value = 42; return true; } return false; }";
        var source = SourceText.From(text);
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, NativeRuntimePolicy.FailFast);
        Assert.Equal(78, BinaryPrimitives.ReadInt32LittleEndian(programBytes));
        var result = ScalarFrontendHost.ReadResult(source, bytes, programBytes,
            BoundRuntimePolicy.FailFast);
        Assert.True(result.IsValid, $"Error {result.ErrorCode} at {result.ErrorSpan}");
        Assert.Contains(result.Functions[0].Nodes,
            static node => node.Operation == 107);
        Assert.Contains(result.Functions[0].Nodes,
            static node => node.Operation == 108);
        var modeOffset = (4 + ScalarFrontendHost.ProgramFrameWordCount + 4 + 17) * sizeof(int);
        var mode = BinaryPrimitives.ReadInt32LittleEndian(programBytes.AsSpan(modeOffset));
        Assert.Equal(2, mode);
        foreach (var version in new[] { 74, 75, 76, 77 })
        {
            BinaryPrimitives.WriteInt32LittleEndian(programBytes, version);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source,
                bytes, programBytes, BoundRuntimePolicy.FailFast));
        }
        BinaryPrimitives.WriteInt32LittleEndian(programBytes, 78);
        BinaryPrimitives.WriteInt32LittleEndian(programBytes.AsSpan(modeOffset), 0);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source,
            bytes, programBytes, BoundRuntimePolicy.FailFast));
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
