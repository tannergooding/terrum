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
    [InlineData("int32 Main() => Later(); int32 Later() => 42;", 2)]
    [InlineData("int32 Recur(int32 value) => value == 0 ? 42 : Recur(value - 1); int32 Main() => Recur(1);", 2)]
    [InlineData("int32 First(int32 value) => value == 0 ? 42 : Second(value - 1); int32 Second(int32 value) => First(value);", 2)]
    [InlineData("int32 Main() { int32 value = 40; return Next(value); } int32 Next(int32 value) => value + 2;", 2)]
    [InlineData("bool Main(int32 value) { if (value > 0) { return Next(value); } else { return false; } } bool Next(int32 value) => value > 0;", 2)]
    [InlineData("void Main() { Step(); } void Step() { }", 2)]
    [InlineData("public int32 Main() { mut int32 value = 0; Fill(out value); return value; } void Fill(out int32 value) { value = 42; }", 2)]
    [InlineData("int32 Main() { outer: while (true) { break outer; } return 42; } int32 Next() => 0;", 2)]
    [InlineData("namespace Demo.Tools; int32 Main() => Later(); int32 Later() => 42;", 2)]
    [InlineData(
        "void F0() { } void F1() { } void F2() { } void F3() { } void F4() { } "
        + "void F5() { } void F6() { } void F7() { } void F8() { } void F9() { }",
        10)]
    public async Task ScalarFrontendProgramDecoderSummarizesForwardSelfAndMutualCallsAsync(
        string text, int functionCount)
    {
        var source = SourceText.From(text);
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var stage = await CompileAsync(text, """
                using Terrum.CodeAnalysis;
                int32 Main() {
                    if (!TryPublishScalarFrontend()) { return 1; }
                    if (!TryDecodeScalarFrontendProgram(
                        Host.FrontendProgram.Length, 63)) { return 2; }
                    return 42;
                }
                """, (BoundRuntimePolicy)policy, "ScalarFrontendProjection").ConfigureAwait(true);
            var native = NativeJit.ExecuteWithData(stage, policy);
            Assert.Equal(42, native.ExitCode);
            await ExecuteAotAsync(stage, policy).ConfigureAwait(true);

            var programBytes = native.Data.AsSpan(
                native.BufferOffsets[ScalarFrontendHost.ProgramBufferIndex],
                ScalarFrontendHost.ProgramWordCount * sizeof(int)).ToArray();
            var summaryBytes = native.Data.AsSpan(
                native.BufferOffsets[ScalarFrontendHost.BufferIndex],
                ScalarFrontendHost.WordCount * sizeof(int));
            var summary = new int[ScalarFrontendHost.WordCount];
            for (var word = 0; word < summary.Length; word++)
            {
                summary[word] = BinaryPrimitives.ReadInt32LittleEndian(
                    summaryBytes.Slice(word * sizeof(int), sizeof(int)));
            }

            var originalPublicationBytes = new byte[ScalarFrontendHost.WordCount * sizeof(int)];
            var hosted = ScalarFrontendHost.ReadProgram(source,
                originalPublicationBytes, programBytes,
                (BoundRuntimePolicy)policy);
            var functions = hosted.Projection.Functions;
            Assert.True(hosted.Projection.IsValid);
            Assert.Equal(functionCount, functions.Length);
            if (text.StartsWith("namespace Demo.Tools;", StringComparison.Ordinal))
            {
                var namespaceName = new TextSpan(text.IndexOf("Demo.Tools", StringComparison.Ordinal),
                    "Demo.Tools".Length);
                foreach (var function in functions)
                {
                    Assert.Equal(namespaceName, function.NamespaceNameSpan);
                    Assert.StartsWith("Demo.Tools.", function.QualifiedName,
                        StringComparison.Ordinal);
                }
            }
            Assert.Equal(88, summary[0]);
            Assert.Equal(1, summary[1]);

            var previousTokenEnd = 0;
            var previousSourceEnd = 0;
            for (var functionIndex = 0; functionIndex < functions.Length; functionIndex++)
            {
                var function = functions[functionIndex];
                var entry = 2 + functionIndex * 7;
                var tokenStart = summary[entry];
                var tokenEnd = summary[entry + 1];
                var sourceStart = summary[entry + 2];
                var sourceLength = summary[entry + 3];
                Assert.InRange(tokenStart, previousTokenEnd, 62);
                Assert.InRange(tokenEnd, tokenStart + 1, 63);
                Assert.InRange(sourceStart, previousSourceEnd, source.Length);
                Assert.InRange(sourceLength, 1, source.Length - sourceStart);
                Assert.InRange(function.Name.Start, sourceStart,
                    sourceStart + sourceLength - function.Name.Length);

                var flags = (function.IsPublic ? 1 : 0)
                    | (function.IsNoThrow ? 2 : 0)
                    | (function.DoesNotReturn ? 4 : 0)
                    | (function.RequiresUnsafeCaller ? 8 : 0);
                var signature = function.ReturnType
                    | (function.FirstType << 4)
                    | (function.SecondType << 8)
                    | (function.ThirdType << 12)
                    | (function.FourthType << 16)
                    | (flags << 20)
                    | (function.ParameterCount << 24)
                    | (function.OutParameterMask << 27);
                Assert.Equal(signature, summary[entry + 4]);
                Assert.Equal(function.Nodes.Length, summary[entry + 5]);
                Assert.Equal(function.Statements.Length | (function.Locals.Length << 4)
                    | (function.InitRefParameterMask << 8),
                    summary[entry + 6]);
                for (var node = 0; node < 64; node++)
                {
                    var target = node < function.Nodes.Length
                        && function.Nodes[node].Operation == 105
                            ? function.CallTargets[node] + 1
                            : 0;
                    Assert.Equal(target,
                        summary[ScalarFrontendHost.SummaryTargetOffset + functionIndex * 64 + node]);
                }
                previousTokenEnd = tokenEnd;
                previousSourceEnd = sourceStart + sourceLength;
            }
            for (var functionIndex = functions.Length;
                functionIndex < ScalarFrontendHost.MaximumFunctions; functionIndex++)
            {
                Assert.All(summary.AsSpan(2 + functionIndex * 7, 7).ToArray(),
                    static word => Assert.Equal(0, word));
                Assert.All(summary.AsSpan(
                    ScalarFrontendHost.SummaryTargetOffset + functionIndex * 64, 64).ToArray(),
                    static word => Assert.Equal(0, word));
            }
        }
    }

    [Theory]
    [InlineData("Host.FrontendProgram[1] = 0;")]
    [InlineData("Host.FrontendProgram[5] = 0;")]
    [InlineData("Host.FrontendProgram[9] = 0;")]
    [InlineData("Host.FrontendProgram[10] = 16;")]
    [InlineData("Host.FrontendProgram[14] = 4;")]
    [InlineData("Host.FrontendProgram[45] = 65;")]
    [InlineData("Host.FrontendProgram[721] = 2;")]
    [InlineData("Host.Frontend[1] = 1;")]
    public async Task ScalarFrontendProgramDecoderRejectsMalformedPublicationAndUnsafeEncodingsAsync(
        string mutation)
    {
        const string text = "int32 Main() => Next(41); int32 Next(int32 value) => value;";
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                if (!TryPublishScalarFrontend()) { return 1; }
                {{mutation}}
                if (TryDecodeScalarFrontendProgram(
                    Host.FrontendProgram.Length, 63)) { return 2; }
                return 42;
            }
            """;
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(text, entry, policy, "ScalarFrontendProjection")
                .ConfigureAwait(true);
            var native = NativeJit.ExecuteWithData(program, (NativeRuntimePolicy)policy);
            Assert.Equal(42, native.ExitCode);
            var publication = native.Data.AsSpan(
                native.BufferOffsets[ScalarFrontendHost.BufferIndex],
                ScalarFrontendHost.WordCount * sizeof(int));
            var expectedPublication = new byte[publication.Length];
            if (mutation == "Host.Frontend[1] = 1;")
            {
                BinaryPrimitives.WriteInt32LittleEndian(expectedPublication.AsSpan(sizeof(int)), 1);
            }
            Assert.Equal(expectedPublication, publication.ToArray());
        }
    }

    [Theory]
    [InlineData("int32 Main(int32 value) => value + 1; int32 Next() => 42;", "Host.FrontendProgram[67] = 5;", false)]
    [InlineData("int32 Main() => Next(); int32 Next() => 42;", "Host.FrontendProgram[23] = 1;", false)]
    [InlineData("int32 Main() { int32 value = 41; return Next(value); } int32 Next(int32 value) => value;", "Host.FrontendProgram[341] = 2147483647;", false)]
    [InlineData("int32 Main(int32 value) => value; int32 Next() => 42;", "Host.FrontendProgram[50] = 103;", true)]
    [InlineData("int32 Main() { mut int32 value = 0; Fill(out value); return value; } void Fill(out int32 value) { value = 42; }", "Host.FrontendProgram[309] = 0;", true)]
    [InlineData("int32 Main() { outer: while (true) { break outer; } return 42; } int32 Next() => 0;", "Host.FrontendProgram[341] = 1;", true)]
    [InlineData("int32 Main() { outer: while (true) { break outer; } return 42; } int32 Next() => 0;", "Host.FrontendProgram[346] = 1;", false)]
    [InlineData("int32 Main() => Pair(1, 2); int32 Pair(int32 left, int32 right) => left + right;", "Host.FrontendProgram[70] = 1; Host.FrontendProgram[71] = 0;", true)]
    [InlineData("int32 Main() => Next(); int32 Next() => 42; int32 Other() => 43;",
        "Host.FrontendProgram[720] = 2;", true)]
    [InlineData("int32 Main() => Next(); int32 Next() => 42;", "Host.FrontendProgram[10] = 2;", true)]
    public async Task ScalarFrontendProgramDecoderDoesNotTreatSemanticCorruptionAsVerifiedAsync(
        string text, string mutation, bool summaryReaderAccepts)
    {
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                if (!TryPublishScalarFrontend()) { return 1; }
                {{mutation}}
                if (!TryDecodeScalarFrontendProgram(
                    Host.FrontendProgram.Length, 63)) { return 2; }
                return 42;
            }
            """;
        var source = SourceText.From(text);
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(text, entry, policy, "ScalarFrontendProjection")
                .ConfigureAwait(true);
            var native = NativeJit.ExecuteWithData(program, (NativeRuntimePolicy)policy);
            Assert.Equal(summaryReaderAccepts ? 42 : 2, native.ExitCode);
            var programBytes = native.Data.AsSpan(
                native.BufferOffsets[ScalarFrontendHost.ProgramBufferIndex],
                ScalarFrontendHost.ProgramWordCount * sizeof(int)).ToArray();
            var summaryBytes = native.Data.AsSpan(
                native.BufferOffsets[ScalarFrontendHost.BufferIndex],
                ScalarFrontendHost.WordCount * sizeof(int));
            if (summaryReaderAccepts)
            {
                Assert.Equal(88, BinaryPrimitives.ReadInt32LittleEndian(summaryBytes));
                Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(
                    summaryBytes[sizeof(int)..]));
            }
            else
            {
                Assert.All(summaryBytes.ToArray(), static value => Assert.Equal(0, value));
            }

            var originalPublicationBytes = new byte[ScalarFrontendHost.WordCount * sizeof(int)];
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(source,
                originalPublicationBytes, programBytes, policy));
        }
    }

    [Theory]
    [InlineData("int32 Main() => Pick(third: 3, first: 1, fourth: 4, second: 2); " +
        "int32 Pick(int32 first, int32 second, int32 third, int32 fourth) => first;",
        "Host.FrontendProgram[89] = 2580;")]
    [InlineData("int32 Main() => Pick(third: 3, first: 1, second: 2); " +
        "int32 Pick(int32 first, int32 second, int32 third) => first;",
        "Host.FrontendProgram[81] = 2331;")]
    public async Task ScalarFrontendProgramDecoderRejectsMalformedNamedArgumentMappingsAsync(
        string text, string mutation)
    {
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                if (!TryPublishScalarFrontend()) { return 1; }
                {{mutation}}
                if (!TryDecodeScalarFrontendProgram(
                    Host.FrontendProgram.Length, 63)) { return 2; }
                return 42;
            }
            """;
        var source = SourceText.From(text);
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(text, entry, policy, "ScalarFrontendProjection")
                .ConfigureAwait(true);
            var native = NativeJit.ExecuteWithData(program, (NativeRuntimePolicy)policy);
            Assert.Equal(2, native.ExitCode);
            var programBytes = native.Data.AsSpan(
                native.BufferOffsets[ScalarFrontendHost.ProgramBufferIndex],
                ScalarFrontendHost.ProgramWordCount * sizeof(int)).ToArray();
            var originalPublicationBytes = new byte[ScalarFrontendHost.WordCount * sizeof(int)];
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(source,
                originalPublicationBytes, programBytes, policy));
        }
    }

    [Fact]
    public async Task ScalarFrontendProgramDecoderRejectsTruncatedPublicationAsync()
    {
        const string text = "int32 Main() => Next(); int32 Next() => 42;";
        const string entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                if (!TryPublishScalarFrontend()) { return 1; }
                if (TryDecodeScalarFrontendProgram(4283, 63)) { return 2; }
                return 42;
            }
            """;
        var program = await CompileAsync(text, entry, BoundRuntimePolicy.FailFast,
            "ScalarFrontendProjection").ConfigureAwait(true);
        Assert.Equal(42, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ScalarFrontendProgramDecoderRejectsFramePastTokenBufferAsync(BoundRuntimePolicy policy)
    {
        const string text = "int32 Main() => Next(); int32 Next() => 42;";
        const string entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                if (!TryPublishScalarFrontend()) { return 1; }
                Host.FrontendProgram[45] = 2147483647;
                if (TryDecodeScalarFrontendProgram(Host.FrontendProgram.Length, 2147483647)) { return 2; }
                return 42;
            }
            """;
        var program = await CompileAsync(text, entry, policy, "ScalarFrontendProjection").ConfigureAwait(true);
        Assert.Equal(42, NativeJit.Execute(program, (NativeRuntimePolicy)policy));
    }

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
    [InlineData("public int32 Main() { mut int32 result = 0; Set(second: 2, first: 4, result: out result); return result; } void Set(int32 first, int32 second, out int32 result) { result = first * 10 + second; }", 42)]
    [InlineData("public int32 Main() => Sum(10, 10, 10, 12); int32 Sum(int32 first, int32 second, int32 third, int32 fourth) => first + second + third + fourth;", 42)]
    [InlineData("public int32 Main() => Sum(0, 0, 0, Identity(41)) + 1; int32 Identity(int32 value) => value; int32 Sum(int32 first, int32 second, int32 third, int32 fourth) => first + second + third + fourth;", 42)]
    [InlineData("public int32 Main() => Sum(fourth: 12, second: 10, first: 10, third: 10); int32 Sum(int32 first, int32 second, int32 third, int32 fourth) => first + second + third + fourth;", 42)]
    [InlineData("public int32 Main() => ReadFourth(0, 0, 0, 42); int32 ReadFourth(int32 first, int32 second, int32 third, int32 fourth) { int32 local = fourth; return local; }", 42)]
    [InlineData("public int32 Main() { mut int32 result = 0; Fill(10, 10, 10, out result); return result; } void Fill(int32 first, int32 second, int32 third, out int32 fourth) { fourth = first + second + third + 12; }", 42)]
    [InlineData("public int32 Main() { Fill(10, 10, 10, init var result); return result; } void Fill(int32 first, int32 second, int32 third, init ref<int32> fourth) { fourth = first + second + third + 12; }", 42)]
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
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ScalarFrontendProgramRoutesOneFourParameterFunctionThroughCompleteContextAsync(
        BoundRuntimePolicy policy)
    {
        const string text = "public int32 Sum(int32 first, int32 second, int32 third, int32 fourth) => first + second + third + fourth;";
        var source = SourceText.From(text);
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, (NativeRuntimePolicy)policy);
        var read = ScalarFrontendHost.ReadProgram(source, bytes, programBytes, policy);

        Assert.True(read.Projection.IsValid);
        var function = Assert.Single(read.Projection.Functions);
        Assert.Equal(4, function.ParameterCount);
        Assert.Equal(1, function.FourthType);
        Assert.Equal(new TextSpan(text.IndexOf("fourth", StringComparison.Ordinal), "fourth".Length),
            function.FourthName);
        var actual = Assert.IsType<BoundProgram>(read.Program);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var hosted = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["Sum"], (NativeRuntimePolicy)policy),
            NativeEmitter.EmitLibrary(actual, ["Sum"], (NativeRuntimePolicy)policy));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ScalarFrontendFourParameterReaderRejectsMalformedProjectionAndSourceAsync(
        BoundRuntimePolicy policy)
    {
        const string text = "int32 Main() => Sum(fourth: 4, second: 2, first: 1, third: 3); int32 Sum(int32 first, int32 second, int32 third, int32 fourth) => first + second + third + fourth;";
        var source = SourceText.From(text);
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, (NativeRuntimePolicy)policy);
        var publication = new byte[ScalarFrontendHost.WordCount * sizeof(int)];
        var read = ScalarFrontendHost.ReadProgram(source, publication, programBytes, policy);
        Assert.True(read.Projection.IsValid);
        var callIndex = read.Projection.Functions[0].Nodes.IndexOf(
            Assert.Single(read.Projection.Functions[0].Nodes,
                static node => node.Operation == 105));
        var callHighWord = 4 + 4 + 42 + callIndex * 8 + 7;
        var rawHigh = BinaryPrimitives.ReadInt32LittleEndian(
            programBytes.AsSpan(callHighWord * sizeof(int), sizeof(int)));
        foreach (var invalidHigh in new[] {
            rawHigh | (1 << 18),
            rawHigh ^ (1 << 11),
            rawHigh ^ (3 << 5),
            rawHigh ^ (1 << 12),
        })
        {
            var forged = programBytes.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(
                forged.AsSpan(callHighWord * sizeof(int), sizeof(int)), invalidHigh);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(
                source, publication, forged, policy));
        }

        const string positionalText = "int32 Main() => Sum(1, 2, 3, 4); int32 Sum(int32 first, int32 second, int32 third, int32 fourth) => first + second + third + fourth;";
        var positionalSource = SourceText.From(positionalText);
        var (_, positionalProgramBytes, _) = await ProduceScalarProgramAsync(
            positionalText, (NativeRuntimePolicy)policy);
        var positionalRead = ScalarFrontendHost.ReadProgram(positionalSource, publication,
            positionalProgramBytes, policy);
        Assert.True(positionalRead.Projection.IsValid);
        var positionalCall = positionalRead.Projection.Functions[0].Nodes.IndexOf(
            Assert.Single(positionalRead.Projection.Functions[0].Nodes,
                static node => node.Operation == 105));
        var positionalHighWord = 4 + 4 + 42 + positionalCall * 8 + 7;
        var positionalHigh = BinaryPrimitives.ReadInt32LittleEndian(
            positionalProgramBytes.AsSpan(positionalHighWord * sizeof(int), sizeof(int)));
        Assert.Equal(0, positionalHigh & (0xFF << 3));
        var nonzeroPositionalMapping = positionalProgramBytes.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(
            nonzeroPositionalMapping.AsSpan(positionalHighWord * sizeof(int), sizeof(int)),
            positionalHigh | (1 << 3));
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(
            positionalSource, publication, nonzeroPositionalMapping, policy));

        var sumFramePayload = 4 + ScalarFrontendHost.ProgramFrameWordCount + 4;
        foreach (var (word, value) in new[] {
            (sumFramePayload + 6, 3),
            (sumFramePayload + 16, 1 << 3),
            (sumFramePayload + 17, 1 << 3),
            (sumFramePayload + 18, 2),
            (sumFramePayload + 19, BinaryPrimitives.ReadInt32LittleEndian(
                programBytes.AsSpan((sumFramePayload + 19) * sizeof(int), sizeof(int))) + 1),
        })
        {
            var forged = programBytes.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(word * sizeof(int), sizeof(int)), value);
            var exception = Record.Exception(() => {
                _ = ScalarFrontendHost.ReadProgram(source, publication, forged, policy);
            });
            Assert.True(exception is ArgumentException,
                $"Corruption at Sum frame word {word} was accepted or threw {exception?.GetType().Name ?? "no exception"}.");
        }

        var changedSource = SourceText.From(text.Replace(
            "int32 fourth) =>", "int32 lastxx) =>", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(
            changedSource, publication, programBytes, policy));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ScalarFrontendComputesDecimalFloat64BitsInTerrumAsync(
        BoundRuntimePolicy policy)
    {
        const string Source = "float64 Value() { return 0.1; }";
        const string Entry = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TokenSnapshot.TryScan(init var tokens);
                require TryParseScalarFrontend(move tokens, init var result);
                if (!result.IsValid || result.WhenTrue.Operation != 3
                    || result.WhenTrue.Type != 8
                    || result.WhenTrue.LeftParameter != -1717986918
                    || result.WhenTrue.RightParameter != 1069128089) {
                    return 1;
                }
                return 42;
            }
            """;
        var program = await CompileAsync(Source, Entry, policy, "ScalarFrontend");
        var nativePolicy = (NativeRuntimePolicy)policy;
        Assert.Equal(42, NativeJit.Execute(program, nativePolicy));
        await ExecuteAotAsync(program, nativePolicy);
    }

    [Theory]
    [InlineData("float64 Value() { return 0.1; }", 8, -1717986918, 1069128089)]
    [InlineData("float32 Value() { return 0X1.8P+1; }", 7, 0x40400000, 0)]
    [InlineData("float32 Value() { return 0B101; }", 7, 0x40A00000, 0)]
    [InlineData("float64 Value() { return 0X123; }", 8, 0, 0x40723000)]
    [InlineData("float32 Value() { return 1.25E-2; }", 7, 0x3C4CCCCD, 0)]
    public async Task ScalarFrontendDispatchesFloatLiteralBitsInTerrumAsync(
        string source, int type, int low, int high)
    {
        var entry = $$"""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require TokenSnapshot.TryScan(init var tokens);
                require TryParseScalarFrontend(move tokens, init var result);
                if (!result.IsValid || result.WhenTrue.Operation != 3
                    || result.WhenTrue.Type != {{type}}
                    || result.WhenTrue.LeftParameter != {{low}}
                    || result.WhenTrue.RightParameter != {{high}}) {
                    return 1;
                }
                return 42;
            }
            """;
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            var program = await CompileAsync(source, entry, policy, "ScalarFrontend");
            var nativePolicy = (NativeRuntimePolicy)policy;
            Assert.Equal(42, NativeJit.Execute(program, nativePolicy));
            await ExecuteAotAsync(program, nativePolicy);
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
    [InlineData("alias A0 = int32; alias A1 = int32; alias A2 = int32; alias A3 = int32; "
        + "alias A4 = int32; alias A5 = int32; alias A6 = int32; alias A7 = int32; "
        + "alias A8 = int32; alias A9 = int32; alias A10 = int32; alias A11 = int32; "
        + "alias A12 = int32; int32 Main() => 42;", "alias", true)]
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
    public async Task ScalarFrontendPreservesThirtyTwoFunctionLimitWithoutAliasesAsync()
    {
        var text = string.Join(' ', Enumerable.Range(0, ScalarFrontendHost.MaximumFunctions - 1)
            .Select(index => $"void F{index}() {{ }}"))
            + " public int32 Main() => 42;";
        var source = SourceText.From(text);
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text,
            NativeRuntimePolicy.FailFast);

        var read = ScalarFrontendHost.ReadProgram(source, bytes, programBytes,
            BoundRuntimePolicy.FailFast);

        Assert.True(read.Projection.IsValid);
        Assert.Equal(ScalarFrontendHost.MaximumFunctions, read.Projection.Functions.Length);
        var program = Assert.IsType<BoundProgram>(read.Program);
        var entry = program.Functions.IndexOf(program.Functions.Single(
            static function => function.Name == "Main"));
        Assert.Equal(42, NativeJit.Execute(program with { EntryPoint = entry },
            NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public async Task ScalarFrontendPlacesMaximumAliasScratchAfterThirtyTwoFramesAsync()
    {
        var aliases = string.Join(' ', Enumerable.Range(0, ScalarFrontendHost.MaximumAliases)
            .Select(index => $"alias Alias{index} = {(index == 0 ? "int32" : $"Alias{index - 1}")};"));
        var functions = string.Join(' ', Enumerable.Range(0, ScalarFrontendHost.MaximumFunctions - 1)
            .Select(index => $"void F{index}() {{ }}"));
        var text = $"{aliases} {functions} public Alias{ScalarFrontendHost.MaximumAliases - 1} Main() => 42;";
        var source = SourceText.From(text);
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text,
            NativeRuntimePolicy.FailFast);

        var read = ScalarFrontendHost.ReadProgram(source, bytes, programBytes,
            BoundRuntimePolicy.FailFast);

        Assert.True(read.Projection.IsValid);
        Assert.Equal(ScalarFrontendHost.MaximumFunctions, read.Projection.Functions.Length);
        var program = Assert.IsType<BoundProgram>(read.Program);
        var entry = program.Functions.IndexOf(program.Functions.Single(
            static function => function.Name == "Main"));
        Assert.Equal(42, NativeJit.Execute(program with { EntryPoint = entry },
            NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ScalarFrontendReportsDuplicateFunctionAtItsTokenAtMaximumFunctionCountAsync(
        NativeRuntimePolicy policy)
    {
        var names = Enumerable.Range(0, ScalarFrontendHost.MaximumFunctions - 2)
            .Select(static index => $"F{index}").Append("@await").Append("@await").ToArray();
        var text = string.Join(' ', names.Select(name => $"void {name}() {{ }}"));
        var lexical = Lexer.Lex(SourceText.From(text));
        Assert.Empty(lexical.Diagnostics);
        Assert.Equal(ScalarFrontendHost.MaximumFunctions * 6 + 1, lexical.Tokens.Length);

        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, policy);
        var duplicateName = new TextSpan(text.LastIndexOf("@await", StringComparison.Ordinal),
            "@await".Length);

        Assert.Equal(87, BinaryPrimitives.ReadInt32LittleEndian(bytes));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(sizeof(int))));
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(2 * sizeof(int))));
        Assert.Equal(duplicateName.Start,
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(3 * sizeof(int))));
        Assert.Equal(duplicateName.Length,
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4 * sizeof(int))));
        Assert.All(programBytes, static value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task ScalarFrontendProgramProjectsCallerUnsafeCallsInsideExplicitScopesAsync()
    {
        const string text = "public unsafe int32 Danger(int32 value) => value + 1; public int32 Main() { return Safe(41); } int32 Safe(int32 value) { unsafe { return Danger(value); } } int32 Fill(init ref<int32> value) { value = 40; return 0; }";
        var source = SourceText.From(text);
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var (bytes, programBytes, stage) = await ProduceScalarProgramAsync(text, policy);
            Assert.Equal(88, BinaryPrimitives.ReadInt32LittleEndian(programBytes));

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
        Assert.Equal(88, BinaryPrimitives.ReadInt32LittleEndian(programBytes));
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
            Assert.Equal(88, BinaryPrimitives.ReadInt32LittleEndian(programBytes));
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
            Assert.Equal(87, BinaryPrimitives.ReadInt32LittleEndian(bytes));
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
        Assert.Equal(87, BinaryPrimitives.ReadInt32LittleEndian(bytes));
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
            Assert.Equal(88, BinaryPrimitives.ReadInt32LittleEndian(programBytes.AsSpan(0, sizeof(int))));
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
                Assert.Equal(88, BinaryPrimitives.ReadInt32LittleEndian(incompleteProgramBytes));
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
    [InlineData("bool", "true", "fourth ? 42 : 0")]
    [InlineData("char16", "char16(42)", "int32(fourth)")]
    [InlineData("int32", "42", "int32(fourth)")]
    [InlineData("uint32", "uint32(42)", "int32(fourth)")]
    [InlineData("int64", "int64(42)", "int32(fourth)")]
    [InlineData("uint64", "uint64(42)", "int32(fourth)")]
    [InlineData("float32", "float32(42)", "int32(fourth)")]
    [InlineData("float64", "float64(42)", "int32(fourth)")]
    public async Task ScalarFrontendCrossFunctionFourthFormalScalarTypesAsync(
        string type, string argument, string result)
    {
        var text = $"public int32 Main() => ReadFourth(0, 0, 0, {argument}); "
            + $"public int32 ReadFourth(int32 first, int32 second, int32 third, {type} fourth) => {result};";
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, policy);
            var source = SourceText.From(text);
            var actual = ScalarFrontendHost.BindLibrary(source, bytes, programBytes, (BoundRuntimePolicy)policy);
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.Equal(NativeEmitter.EmitLibrary(hosted, ["ReadFourth"], policy),
                NativeEmitter.EmitLibrary(actual, ["ReadFourth"], policy));
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
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));

            var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, policy);
            var result = ScalarFrontendHost.ReadResult(source, bytes, programBytes, (BoundRuntimePolicy)policy);
            Assert.False(result.IsValid);
            Assert.Equal(1, result.ErrorCode);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.BindLibrary(source, bytes,
                programBytes, (BoundRuntimePolicy)policy));
        }
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
            Assert.Equal(88, BinaryPrimitives.ReadInt32LittleEndian(programBytes));
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
            BinaryPrimitives.WriteInt32LittleEndian(programBytes, 88);

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
        Assert.Equal(88, BinaryPrimitives.ReadInt32LittleEndian(programBytes));
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
        BinaryPrimitives.WriteInt32LittleEndian(programBytes, 88);
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
        Assert.Equal(88, BinaryPrimitives.ReadInt32LittleEndian(programBytes));
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
        BinaryPrimitives.WriteInt32LittleEndian(programBytes, 88);
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
            (frame + 4 + ScalarFrontendHost.WordCount + call, -2),
            (frame + 4 + ScalarFrontendHost.WordCount + call, 0),
            (frame + 4 + ScalarFrontendHost.WordCount + call, 2),
            (frame + 4 + ScalarFrontendHost.WordCount + call, 3),
            (frame + 4 + ScalarFrontendHost.WordCount, 1),
            (frame + ScalarFrontendHost.ProgramFrameWordCount - 1, 0),
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
            var error = Record.Exception(() => ScalarFrontendHost.ReadResult(source, bytes,
                programBytes, BoundRuntimePolicy.FailFast));
            Assert.True(error is ArgumentException, $"Program word {word}: {previous} -> {value}: {error}");
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
        Set(4 + 4 + ScalarFrontendHost.WordCount + call, -1);
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
    public async Task ScalarFrontendHostReadProgramEnforcesMaximumSourceLengthAsync()
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
            "using Terrum.CodeAnalysis; int32 Main() { if (!TryPublishScalarFrontend()) { return 1; } return 42; }",
            (BoundRuntimePolicy)policy, "ScalarFrontendProjection");
        var native = NativeJit.ExecuteWithData(stage, policy);
        if (native.ExitCode != 42)
        {
            var frontend = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
                5 * sizeof(int));
            var program = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.ProgramBufferIndex],
                50 * sizeof(int));
            var frontendWords = new int[5];
            var programWords = new int[50];
            for (var index = 0; index < frontendWords.Length; index++)
            {
                frontendWords[index] = BinaryPrimitives.ReadInt32LittleEndian(
                    frontend.Slice(index * sizeof(int), sizeof(int)));
            }
            for (var index = 0; index < programWords.Length; index++)
            {
                programWords[index] = BinaryPrimitives.ReadInt32LittleEndian(
                    program.Slice(index * sizeof(int), sizeof(int)));
            }
            Assert.True(false,
                $"Native entry returned {native.ExitCode}; frontend=[{string.Join(',', frontendWords)}]; "
                    + $"program=[{string.Join(',', programWords)}].");
        }
        return (native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray(),
            native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.ProgramBufferIndex],
                ScalarFrontendHost.ProgramWordCount * sizeof(int)).ToArray(), stage);
    }
}
