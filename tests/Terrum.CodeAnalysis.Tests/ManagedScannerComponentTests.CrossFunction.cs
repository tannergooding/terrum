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

    [Theory]
    [InlineData("alias Scalar = Wide; alias Wide = int32; public Scalar Main() { const Wide value = 40; return Bump(int32(value)); } Scalar Bump(Scalar value) => value + 2;", 2, 42)]
    [InlineData("public Number Main() { init Number value; Set(init value); return int32(value) + 2; } void Set(init ref<Scalar> value) { value = 40; } alias Number = Scalar; alias Scalar = int32;", 2, 42)]
    [InlineData("alias Number = int32; public Number Main() => 42;", 1, 42)]
    [InlineData("alias A = int32; alias B = int32; alias C = int32; alias D = int32; alias E = int32; alias F = int32; alias G = int32; alias H = int32; alias I = int32; alias J = int32; alias K = int32; public K Main() => 42;", 1, 42)]
    [InlineData("alias Number = int32; void A() {} void B() {} void C() {} void D() {} void E() {} void F() {} void G() {} void H() {} public Number Main() => 42;", 9, 42)]
    [InlineData("alias Scalar = int32; Scalar Scalar() => 40; public Scalar Main() => Scalar() + 2;", 2, 42)]
    public async Task ScalarFrontendProjectsInternalScalarAliasChainsAsync(string text,
        int expectedFunctions, int expected)
    {
        var source = SourceText.From(text);
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var (bytes, programBytes, stage) = await ProduceScalarProgramAsync(text, policy);
            var read = ScalarFrontendHost.ReadProgram(source, bytes, programBytes,
                (BoundRuntimePolicy)policy);
            Assert.True(read.Projection.IsValid);
            Assert.Equal(expectedFunctions, read.Projection.Functions.Length);

            var actual = Assert.IsType<BoundProgram>(read.Program);
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
                (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.Equal(hosted.Functions.Select(static function => function.Name),
                actual.Functions.Select(static function => function.Name));
            Assert.Equal(hosted.Functions.Select(static function => function.ReturnType),
                actual.Functions.Select(static function => function.ReturnType));
            Assert.Equal(hosted.Functions.Select(static function => function.Span),
                actual.Functions.Select(static function => function.Span));
            Assert.Equal(hosted.Functions.Select(static function => function.Body.Span),
                actual.Functions.Select(static function => function.Body.Span));
            Assert.Equal(hosted.Functions.Select(static function =>
                    (function.ParameterCount, function.IsInternal, function.IsNoThrow,
                        function.DoesNotReturn, function.RequiresUnsafeCaller)),
                actual.Functions.Select(static function =>
                    (function.ParameterCount, function.IsInternal, function.IsNoThrow,
                        function.DoesNotReturn, function.RequiresUnsafeCaller)));
            Assert.Equal(hosted.Functions.Select(static function => function.Variables
                    .Select(static variable => (variable.Name, variable.Type, variable.IsMutable,
                        variable.IsOut, variable.IsInit, variable.IsInitializationLocal)).ToArray()),
                actual.Functions.Select(static function => function.Variables
                    .Select(static variable => (variable.Name, variable.Type, variable.IsMutable,
                        variable.IsOut, variable.IsInit, variable.IsInitializationLocal)).ToArray()));
            Assert.Equal(hosted.Functions.SelectMany(static function => function.Body.Statements)
                    .OfType<BoundLocal>().Select(static local => (local.Variable, local.Span,
                        local.Initializer.Type, local.Initializer.Span)),
                actual.Functions.SelectMany(static function => function.Body.Statements)
                    .OfType<BoundLocal>().Select(static local => (local.Variable, local.Span,
                        local.Initializer.Type, local.Initializer.Span)));
            var hostedMain = hosted.Functions.Single(static function => function.Name == "Main");
            var actualMain = actual.Functions.Single(static function => function.Name == "Main");
            if (hostedMain.Body.Statements.OfType<BoundReturn>().Single().Value is BoundCall hostedCall)
            {
                var actualCall = Assert.IsType<BoundCall>(actualMain.Body.Statements
                    .OfType<BoundReturn>().Single().Value);
                Assert.Equal(hostedCall.Function, actualCall.Function);
                Assert.Equal(hostedCall.Arguments.Select(static argument =>
                        (argument.GetType(), argument.Type, argument.Span,
                            argument is BoundRead read ? read.Variable : -1,
                            argument is BoundConstant constant ? constant.Value : 0)),
                    actualCall.Arguments.Select(static argument =>
                        (argument.GetType(), argument.Type, argument.Span,
                            argument is BoundRead read ? read.Variable : -1,
                            argument is BoundConstant constant ? constant.Value : 0)));
            }
            Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["Main"], policy),
                NativeEmitter.EmitLibrary(actual, ["Main"], policy));
            var entry = actual.Functions.IndexOf(actual.Functions.Single(
                static function => function.Name == "Main"));
            Assert.Equal(expected, NativeJit.Execute(actual with { EntryPoint = entry }, policy));
            Assert.Equal(expected, NativeJit.Execute(hosted with { EntryPoint = entry }, policy));
            await ExecuteAotAsync(stage, policy);
        }
    }

    [Theory]
    [InlineData("alias Scalar = Missing; int32 Main() => 42;", "Missing", false)]
    [InlineData("alias Scalar = int32; alias Scalar = bool; int32 Main() => 42;", "Scalar", true)]
    [InlineData("alias Scalar = Scalar; int32 Main() => 42;", "Scalar", false)]
    [InlineData("alias First = Second; alias Second = First; int32 Main() => 42;", "First", false)]
    [InlineData("public alias Scalar = int32; int32 Main() => 42;", "alias", false)]
    [InlineData("alias Identity<T> = T; int32 Main() => 42;", "<", false)]
    [InlineData("alias Scalar = int32; int32 Main() => Scalar(42);", "=>", false)]
    public async Task ScalarFrontendRejectsUnsupportedOrInvalidAliasesAsync(string text,
        string expectedSpanText, bool expectLastOccurrence)
    {
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text,
            NativeRuntimePolicy.FailFast);
        var result = ScalarFrontendHost.ReadResult(SourceText.From(text), bytes,
            programBytes, BoundRuntimePolicy.FailFast);
        Assert.False(result.IsValid);
        Assert.Equal(1, result.ErrorCode);
        var expectedStart = expectLastOccurrence
            ? text.LastIndexOf(expectedSpanText, StringComparison.Ordinal)
            : text.IndexOf(expectedSpanText, StringComparison.Ordinal);
        Assert.Equal(new TextSpan(expectedStart, expectedSpanText.Length), result.ErrorSpan);
    }

    [Fact]
    public async Task ScalarFrontendReaderResolvesAliasesFromOriginalSourceAsync()
    {
        const string text = "alias Number = int32; public Number Main() => 42; Number Next() => 0;";
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text,
            NativeRuntimePolicy.FailFast);
        var changedSource = SourceText.From(text.Replace("int32", "int64", StringComparison.Ordinal));

        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(changedSource,
            bytes, programBytes, BoundRuntimePolicy.FailFast));
    }

    [Fact]
    public async Task ScalarFrontendPreservesTenFunctionLimitWithoutAliasesAsync()
    {
        const string text = "void A() {} void B() {} void C() {} void D() {} void E() {} void F() {} void G() {} void H() {} void I() {} public int32 Main() => 42;";
        var source = SourceText.From(text);
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text,
            NativeRuntimePolicy.FailFast);

        var read = ScalarFrontendHost.ReadProgram(source, bytes, programBytes,
            BoundRuntimePolicy.FailFast);

        Assert.True(read.Projection.IsValid);
        Assert.Equal(10, read.Projection.Functions.Length);
        var program = Assert.IsType<BoundProgram>(read.Program);
        var entry = program.Functions.IndexOf(program.Functions.Single(
            static function => function.Name == "Main"));
        Assert.Equal(42, NativeJit.Execute(program with { EntryPoint = entry },
            NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public async Task ScalarFrontendProgramProjectsCallerUnsafeCallsInsideExplicitScopesAsync()
    {
        const string text = "public unsafe int32 Danger(int32 value) => value + 1; public int32 Main() { return Safe(41); } int32 Safe(int32 value) { unsafe { return Danger(value); } } int32 Fill(init ref<int32> value) { value = 40; return 0; }";
        var source = SourceText.From(text);
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var (bytes, programBytes, stage) = await ProduceScalarProgramAsync(text, policy);
            Assert.Equal(85, BinaryPrimitives.ReadInt32LittleEndian(programBytes));

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
        Assert.Equal(85, BinaryPrimitives.ReadInt32LittleEndian(programBytes));
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(SourceText.From(text),
            bytes, programBytes, BoundRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData("public unsafe int32 Danger(int32 value) => value + 1; public int32 Main() => unsafe(Danger(41));", 42)]
    [InlineData("public unsafe int32 Danger(int32 value) => value + 1; public int32 Main() => unsafe(Danger(40) + 2);", 43)]
    [InlineData("public unsafe int32 Danger(int32 value) => value + 1; public int32 Main() => 1 + unsafe(Danger(40));", 42)]
    [InlineData("public int32 Main() { if (unsafe(true)) { return 42; } return 0; } int32 Other() => 0;", 42)]
    public async Task ScalarFrontendProgramProjectsUnsafeExpressionScopesAsync(string text, int expected)
    {
        var source = SourceText.From(text);
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var (bytes, programBytes, stage) = await ProduceScalarProgramAsync(text, policy);
            Assert.Equal(85, BinaryPrimitives.ReadInt32LittleEndian(programBytes));
            var result = ScalarFrontendHost.ReadProgram(source, bytes, programBytes,
                (BoundRuntimePolicy)policy);
            Assert.True(result.Projection.IsValid);
            var wrapperFunctionIndex = Enumerable.Range(0, result.Projection.Functions.Length)
                .Single(index => result.Projection.Functions[index].Nodes
                    .Any(static node => node.Operation == 110));
            var wrapperNodes = result.Projection.Functions[wrapperFunctionIndex].Nodes;
            var wrapper = Assert.Single(wrapperNodes,
                static node => node.Operation == 110);

            var actual = Assert.IsType<BoundProgram>(result.Program);
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["Main"], policy),
                NativeEmitter.EmitLibrary(actual, ["Main"], policy));
            var entry = actual.Functions.IndexOf(actual.Functions.Single(static function => function.Name == "Main"));
            Assert.Equal(expected, NativeJit.Execute(actual with { EntryPoint = entry }, policy));
            Assert.Equal(expected, NativeJit.Execute(hosted with { EntryPoint = entry }, policy));
            await ExecuteAotAsync(stage, policy);

            var wrapperIndex = wrapperNodes.IndexOf(wrapper);
            var wrapperOperationOffset = (4 + wrapperFunctionIndex * ScalarFrontendHost.ProgramFrameWordCount + 4
                + 42 + wrapperIndex * 8) * sizeof(int);
            BinaryPrimitives.WriteInt32LittleEndian(programBytes.AsSpan(wrapperOperationOffset), 46);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(source,
                bytes, programBytes, (BoundRuntimePolicy)policy));
            BinaryPrimitives.WriteInt32LittleEndian(programBytes.AsSpan(wrapperOperationOffset), 110);

            BinaryPrimitives.WriteInt32LittleEndian(programBytes, 83);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(source,
                bytes, programBytes, (BoundRuntimePolicy)policy));
        }
    }

    [Theory]
    [InlineData("public int32 Main() => unsafe(42);", 82, "Main", 42)]
    [InlineData("public int32 Main() { init int32 value; return unsafe(value is init) ? 42 : 0; }", 82, "Main", 0)]
    [InlineData("bool Init(init ref<int32> value) { if (unsafe(value is init)) { value = 42; return true; } return false; }", 82, "Init", 0)]
    [InlineData("public unsafe int32 Recur() { if (false) { return unsafe(Recur()); } return 42; }", 83, "Recur", 42)]
    [InlineData("public unsafe int32 Recur() { if (false) { unsafe { return Recur(); } } return 42; }", 83, "Recur", 42)]
    [InlineData("public unsafe void Recur() { if (false) { unsafe { Recur(); } } }", 83, "Recur", 0)]
    public async Task ScalarFrontendSingleFunctionProjectsUnsafeScopesAsync(
        string text, int projectionVersion, string functionName, int expected)
    {
        var source = SourceText.From(text);
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var (bytes, _, _) = await ProduceScalarProgramAsync(text, policy);
            Assert.Equal(85, BinaryPrimitives.ReadInt32LittleEndian(bytes));
            var projection = ScalarFrontendHost.ReadResult(source, bytes, (BoundRuntimePolicy)policy);
            Assert.True(projection.IsValid);
            if (text.Contains("unsafe(", StringComparison.Ordinal))
            {
                Assert.Contains(projection.Nodes, static node => node.Operation == 110);
            }
            Assert.Equal(text.Contains("public unsafe", StringComparison.Ordinal), projection.RequiresUnsafeCaller);

            var actual = ScalarFrontendHost.BindLibrary(source, bytes, (BoundRuntimePolicy)policy);
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.Equal(hosted.Functions.Single().RequiresUnsafeCaller,
                actual.Functions.Single().RequiresUnsafeCaller);
            if (functionName == "Main")
            {
                Assert.Equal(NativeEmitter.EmitLibrary(hosted, [functionName], policy),
                    NativeEmitter.EmitLibrary(actual, [functionName], policy));
                Assert.Equal(expected, NativeJit.Execute(actual with { EntryPoint = 0 }, policy));
            }

            var forgedScope = projection.Nodes.Any(static node => node.Operation == 110)
                ? text.Replace("unsafe(", "(", StringComparison.Ordinal)
                : text.Replace("unsafe {", "{", StringComparison.Ordinal);
            Assert.Throws<ArgumentException>(() =>
                ScalarFrontendHost.ReadResult(SourceText.From(forgedScope), bytes, (BoundRuntimePolicy)policy));

            var downgraded = bytes.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(downgraded, projectionVersion - 1);
            Assert.Throws<ArgumentException>(() =>
                ScalarFrontendHost.ReadResult(source, downgraded, (BoundRuntimePolicy)policy));

            if (text.Contains("public unsafe", StringComparison.Ordinal))
            {
                var forgedModifier = SourceText.From(text.Replace("public unsafe", "public",
                    StringComparison.Ordinal));
                Assert.Throws<ArgumentException>(() =>
                    ScalarFrontendHost.ReadResult(forgedModifier, bytes, (BoundRuntimePolicy)policy));
            }
        }
    }

    [Fact]
    public async Task ScalarFrontendSingleFunctionRejectsUnscopedCallerUnsafeSelfCallsAsync()
    {
        const string text = "public unsafe int32 Recur() { if (false) { return Recur(); } return 42; }";
        var source = SourceText.From(text);
        var (bytes, _, _) = await ProduceScalarProgramAsync(text, NativeRuntimePolicy.FailFast);
        Assert.Equal(85, BinaryPrimitives.ReadInt32LittleEndian(bytes));
        Assert.Throws<ArgumentException>(() =>
            ScalarFrontendHost.ReadResult(source, bytes, BoundRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(77)]
    [InlineData(78)]
    public async Task ScalarFrontendProgramRejectsObsoleteTagsWithNonBooleanOutputsAsync(int version)
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
            Assert.Equal(85, BinaryPrimitives.ReadInt32LittleEndian(programBytes.AsSpan(0, sizeof(int))));
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
                Assert.Equal(85, BinaryPrimitives.ReadInt32LittleEndian(incompleteProgramBytes));
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

    [Theory]
    [InlineData("[DoesNotReturn] void Halt() { require false; } bool Fill(out int32 value) { value = 42; return true; } public int32 Main() { Halt(); }")]
    [InlineData("[DoesNotReturn] void Halt() { require false; } bool Fill(init ref<int32> value) { value = 42; return true; } public int32 Main() { Halt(); }")]
    [InlineData("[DoesNotReturn] unsafe void Halt() { require false; } public int32 Main() { unsafe { Halt(); } }")]
    public async Task ScalarFrontendNonReturningCompletionRetainsExistingInventoryConstraintsAsync(string text)
    {
        var source = SourceText.From(text);
        Assert.InRange(Lexer.Lex(source).Tokens.Length, 1, 64);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), BoundRuntimePolicy.FailFast);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));

        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, NativeRuntimePolicy.FailFast);
        var result = ScalarFrontendHost.ReadResult(source, bytes, programBytes, BoundRuntimePolicy.FailFast);
        Assert.False(result.IsValid);
        Assert.Equal(1, result.ErrorCode);
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
    public async Task ScalarFrontendCrossFunctionVoidCallsRequireCurrentTagAndStatementRootsAsync()
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
            Assert.Equal(85, BinaryPrimitives.ReadInt32LittleEndian(programBytes));
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
            BinaryPrimitives.WriteInt32LittleEndian(programBytes, 85);

            var effectOffset = (4 + ScalarFrontendHost.ProgramFrameWordCount + 4 + 2) * sizeof(int);
            var effects = BinaryPrimitives.ReadInt32LittleEndian(programBytes.AsSpan(effectOffset));
            BinaryPrimitives.WriteInt32LittleEndian(programBytes.AsSpan(effectOffset), effects & ~4);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source,
                bytes, programBytes, (BoundRuntimePolicy)policy));
            BinaryPrimitives.WriteInt32LittleEndian(programBytes.AsSpan(effectOffset), effects);
        }
    }

    [Fact]
    public async Task ScalarFrontendCrossFunctionOutParametersRequireCurrentTagAndExactModesAsync()
    {
        const string text = "public int32 Main() { mut int32 result = 0; if (Set(out result)) { return result; } return 0; } bool Set(out int32 value) { value = 42; return true; }";
        var source = SourceText.From(text);
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, NativeRuntimePolicy.FailFast);
        Assert.Equal(85, BinaryPrimitives.ReadInt32LittleEndian(programBytes));
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
        BinaryPrimitives.WriteInt32LittleEndian(programBytes, 85);
        BinaryPrimitives.WriteInt32LittleEndian(programBytes.AsSpan(modeOffset), 0);
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source,
            bytes, programBytes, BoundRuntimePolicy.FailFast));
    }

    [Fact]
    public async Task ScalarFrontendCrossFunctionInitReferencesRequireCurrentTagAndExactModesAsync()
    {
        const string text = "public int32 Main() { require !Fill(false, init var value); return Fill(true, init value) ? value : 0; } bool Fill(bool publish, init ref<int32> value) { if (publish) { value = 42; return true; } return false; }";
        var source = SourceText.From(text);
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, NativeRuntimePolicy.FailFast);
        Assert.Equal(85, BinaryPrimitives.ReadInt32LittleEndian(programBytes));
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
        BinaryPrimitives.WriteInt32LittleEndian(programBytes, 85);
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

    [Fact]
    public async Task ScalarFrontendProgramReaderEnforcesMaximumSourceLengthAsync()
    {
        const string program = "public int32 Main() => 42;";
        var text = program + new string(' ', ScannerHost.MaximumSourceLength - program.Length);
        Assert.Equal(ScannerHost.MaximumSourceLength, text.Length);

        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var source = SourceText.From(text);
            var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, policy);
            var result = ScalarFrontendHost.ReadProgram(source, bytes, programBytes,
                (BoundRuntimePolicy)policy);
            Assert.True(result.Projection.IsValid);

            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(
                SourceText.From(text + " "), bytes, programBytes, (BoundRuntimePolicy)policy));
        }
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
