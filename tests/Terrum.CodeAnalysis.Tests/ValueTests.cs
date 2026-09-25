// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ValueTests
{
    [Theory]
    [InlineData("int32 Main() { return (int32)42u32; }", 42)]
    [InlineData("int32 Main() { char16 unit = (char16)65535u32; return (int32)unit; }", 65535)]
    [InlineData("int32 Main() { if (uint32.MaxValue > 2147483647u32) { return 1; } return 0; }", 1)]
    [InlineData("int32 Main() { uint32 n = 4000000000u32; return (int32)(n / 1000000000u32); }", 4)]
    [InlineData("namespace Example; int32 Main() { return (int32)Value(); } uint32 Value() { return 7u32; }", 7)]
    [InlineData("namespace Example; public struct Value { public int32 Number; public Value(int32 number) { Number = number; } } int32 F(Example /*type*/ . Value value) { return value.Number; } int32 Main() { Example /*local*/ . Value value = new Example /*new*/ . Value(7); require int32 /*constant*/ . MaxValue > value.Number; return Example /*call*/ . F(value); }", 7)]
    public async Task ScalarValuesExecuteInNativeCodeAsync(string source, int expected)
    {
        var program = Compile(source);
        var code = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast);
        Assert.InRange(code.EntryPoint, 0, code.Code.Length - 1);
        Assert.Equal(code.EntryPoint, code.Functions[0].Start);
        Assert.Equal(code.AotEntryPoint, code.Functions[1].Start);
        for (var index = 1; index < code.Functions.Length; index++)
        {
            Assert.True(code.Functions[index - 1].End <= code.Functions[index].Start);
        }
        SemanticVerifier.Verify(program);
        Assert.Equal(expected, await RunNativeAsync(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Theory]
    [InlineData("int32 Main() { return (int32)uint32.MaxValue; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { return (int32)(uint32.MaxValue + 1u32); }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { return (int32)(0u32 - 1u32); }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { return (int32)(2147483648u32 * 2u32); }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { return (int32)(char16)65536u32; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { return (int32)(uint32)(-1i32); }", unchecked((int)0xE0540001))]
    public async Task CheckedConversionsAndUnsignedArithmeticFailFastAsync(string source, int expected)
    {
        Assert.Equal(expected, await RunNativeAsync(NativeEmitter.Emit(Compile(source), NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Fact]
    public async Task PackedStructConstructorFieldsAndInstanceMethodExecuteAsync()
    {
        const string Source = """
            struct Range {
                public int32 Start;
                public int32 Length;
                private Range(int32 start, int32 length) {
                    this.Start = start;
                    this.Length = length;
                }
                public static Range Create(int32 start, int32 length) {
                    return new Range(start, length);
                }
                public int32 End() { return Start + Length; }
            }
            int32 Main() {
                Range first = Range.Create(2, 3);
                mut Range copy = first;
                copy.Start = 7;
                return first.End() + copy.End();
            }
            """;
        Assert.Equal(15, await RunNativeAsync(NativeEmitter.Emit(Compile(Source), NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Fact]
    public async Task InstanceMethodsCanCallOtherMethodsOnTheirReceiverAsync()
    {
        const string Source = """
            struct Range {
                public int32 Start;
                public Range(int32 start) { Start = start; }
                public int32 Value() { return Start; }
                public int32 Twice() { return Value() + this.Value(); }
            }
            int32 Main() { Range range = new Range(4); return range.Twice(); }
            """;
        Assert.Equal(8, await RunNativeAsync(NativeEmitter.Emit(Compile(Source), NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Fact]
    public async Task NaturallyAlignedFieldsUseScalarWindowsAbiSizeAsync()
    {
        const string Source = """
            public struct Pair {
                public bool Flag;
                public int32 Value;
                public Pair(bool flag, int32 value) { Flag = flag; Value = value; }
                public int32 Read() { if (Flag) { return Value; } return 0; }
            }
            int32 Main() { Pair value = new Pair(true, 7); return value.Read(); }
            """;
        var program = Compile(Source);
        Assert.Equal(0, program.Structures[0].Fields[0].Offset);
        Assert.Equal(4, program.Structures[0].Fields[1].Offset);
        Assert.Equal(8, program.Structures[0].Size);
        Assert.Equal(7, await RunNativeAsync(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Fact]
    public async Task InternalMembersRemainAccessibleWithinCompilationAsync()
    {
        const string Source = """
            namespace Example;
            internal struct Value {
                internal int32 Number;
                internal Value(int32 number) { Number = number; }
            }
            internal static int32 Main() {
                Value value = new Value(9);
                return value.Number;
            }
            """;
        var program = Compile(Source);
        Assert.True(program.Structures[0].IsInternal);
        Assert.True(program.Structures[0].Fields[0].IsInternal);
        Assert.True(program.Functions[0].IsInternal);
        Assert.Equal(9, await RunNativeAsync(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Fact]
    public void DefaultVisibilityDoesNotExposeModuleDeclarations()
    {
        var program = Compile("struct Item { int32 Value; Item() { Value = 1; } } "
            + "int32 Main() { return 0; } int32 Hidden() { return 1; }");
        Assert.True(program.Structures[0].IsInternal);
        Assert.True(program.Structures[0].Fields[0].IsPrivate);
        Assert.True(program.Functions[0].IsPrivate);
        Assert.True(program.Functions[1].IsInternal);
        Assert.True(program.Functions[2].IsInternal);
        SemanticVerifier.Verify(program);
    }

    [Fact]
    public async Task OutValuesCommitOnlyOnTrueAndFlowThroughBranchesAsync()
    {
        const string Source = """
            struct Range {
                public int32 Start;
                public int32 Length;
                private Range(int32 start, int32 length) {
                    this.Start = start;
                    this.Length = length;
                }
                public static bool TryCreate(int32 start, int32 length, out Range range) {
                    if (start < 0 || length < 0 || start > int32.MaxValue - length) {
                        return false;
                    }
                    range = new Range(start, length);
                    return true;
                }
                public int32 End() { return Start + Length; }
            }
            int32 Main() {
                if (!Range.TryCreate(2, 3, out var range)) { return 1; }
                if (Range.TryCreate(-1, 0, out var unused)) { return 2; }
                return range.End();
            }
            """;
        Assert.Equal(5, await RunNativeAsync(NativeEmitter.Emit(Compile(Source), NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Fact]
    public async Task AssignedOutStructFieldsWritePrivateValueStorageAsync()
    {
        const string Source = """
            struct Pair {
                public int32 A;
                public int32 B;
                public Pair(int32 a, int32 b) { A = a; B = b; }
            }
            bool Make(out Pair result) {
                result = new Pair(2, 3);
                result.A = 7;
                return true;
            }
            int32 Main() {
                if (!Make(out var pair)) { return 0; }
                return pair.A * 10 + pair.B;
            }
            """;
        Assert.Equal(73, await RunNativeAsync(NativeEmitter.Emit(Compile(Source), NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Theory]
    [InlineData("int32 Main() { if (Read(out var value)) { return (int32)value; } return 0; } bool Read(out char16 value) { value = (char16)65535; return true; }", 65535)]
    [InlineData("int32 Main() { if (Read(out var value) && value) { return 1; } return 0; } bool Read(out bool value) { value = true; return true; }", 1)]
    public async Task NarrowOutValuesAreCanonicalAfterCommitAsync(string source, int expected)
    {
        Assert.Equal(expected, await RunNativeAsync(NativeEmitter.Emit(Compile(source), NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Fact]
    public async Task SourceRangeAndCharacterFactsCompileAsSeparateSourcesAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var paths = new[] { "SourceRange.terrum", "CharacterFacts.terrum", "RangeTests.terrum" };
        var trees = new SyntaxTree[paths.Length + 1];
        for (var i = 0; i < paths.Length; i++)
        {
            trees[i] = SyntaxTree.Parse(await File.ReadAllTextAsync(Path.Combine(root, "self-host", paths[i])).ConfigureAwait(true));
        }

        trees[^1] = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis.Tests;
            int32 Main() {
                if (!CheckSourceRange()) { return 1; }
                if (!IsAsciiDigit((char16)48) || IsAsciiDigit((char16)47)) { return 2; }
                if (GetDecimalDigit((char16)57) != 9) { return 3; }
                return 0;
            }
            """);
        var compilation = Compilation.Create(trees);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(0, await RunNativeAsync(NativeEmitter.Emit(
            Assert.IsType<BoundProgram>(compilation.Program), NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Fact]
    public async Task QualifiedNamesIgnoreDirectiveTriviaAcrossSourcesAsync()
    {
        var trees = new[] {
            SyntaxTree.Parse("namespace /* scope */ Example . Utility; public int32 Value() { return 9; }"),
            SyntaxTree.Parse("using Example /* scope */ . Utility; namespace Client; int32 Main() { return Value(); }"),
        };
        var compilation = Compilation.Create(trees);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(9, await RunNativeAsync(NativeEmitter.Emit(
            Assert.IsType<BoundProgram>(compilation.Program), NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Fact]
    public void MultipleMainsAndDiagnosticsRetainTheirSourceTree()
    {
        var first = SyntaxTree.Parse("namespace One; int32 Main() { return 0; }");
        var second = SyntaxTree.Parse("namespace Two; int32 Main() { return Missing(); }");
        var compilation = Compilation.Create([first, second]);
        Assert.Null(compilation.Program);
        Assert.Equal(2, compilation.SyntaxTrees.Length);
        Assert.Equal(compilation.Diagnostics.Length, compilation.SourceDiagnostics.Length);
        var ambiguous = false;
        var unknown = false;
        foreach (var located in compilation.SourceDiagnostics)
        {
            Assert.Same(second, located.SyntaxTree);
            ambiguous |= located.Diagnostic.Message.Contains("ambiguous", StringComparison.Ordinal);
            unknown |= located.Diagnostic.Message.Contains("Unknown function", StringComparison.Ordinal);
        }
        Assert.True(ambiguous && unknown);

        var malformed = SyntaxTree.Parse("int32 Main() { return ; }");
        compilation = Compilation.Create([first, malformed]);
        Assert.Null(compilation.Program);
        foreach (var located in compilation.SourceDiagnostics)
        {
            Assert.Same(malformed, located.SyntaxTree);
        }
    }

    [Fact]
    public async Task BoundedHostBuffersExecuteInSharedNativeCodeAsync()
    {
        const string Source = """
            int32 Main() {
                int32 first = (int32)Host.Source[1];
                Host.Output[0] = first + Host.Source.Length;
                return Host.Output[0];
            }
            """;
        ImmutableArray<HostBuffer> buffers = [
            new("Source", PrimitiveType.Char16, [65, 66], false),
            new("Output", PrimitiveType.Int32, [0], true),
        ];
        var compilation = Compilation.Create(SyntaxTree.Parse(Source), buffers);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        SemanticVerifier.Verify(program);
        var code = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast);
        Assert.Equal(8, code.ReadOnlyDataLength);
        Assert.Equal(12, code.Data.Length);
        Assert.NotEmpty(code.DataFixups);
        Assert.Equal(68, await RunNativeAsync(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Fact]
    public async Task BufferBoundsAndReadOnlyRestrictionsAreEnforcedAsync()
    {
        ImmutableArray<HostBuffer> buffers = [new("Source", PrimitiveType.Int32, [5], false)];
        var illegalWrite = Compilation.Create(SyntaxTree.Parse("int32 Main() { Host.Source[0] = 9; return 0; }"), buffers);
        Assert.Null(illegalWrite.Program);
        var outputArgument = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { if (Try(out Host.Output[0])) { return 1; } return 0; } bool Try(out int32 value) { value = 1; return true; }"),
            [new HostBuffer("Output", PrimitiveType.Int32, [0], true)]);
        Assert.Null(outputArgument.Program);
        var outOfRange = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Host.Source[-1i32]; }"), buffers);
        var program = Assert.IsType<BoundProgram>(outOfRange.Program);
        Assert.Equal(unchecked((int)0xE0540004),
            await RunNativeAsync(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Theory]
    [InlineData("int32 Main() { if (Try(out var x)) { return x; } return 0; } bool Try(out int32 x) { return true; }", false)]
    [InlineData("int32 Main() { if (Try(out var x)) { return x; } return x; } bool Try(out int32 x) { x = 3; return true; }", false)]
    [InlineData("int32 Main() { if (Try(out var x)) { return x; } return 0; } bool Try(out int32 x) { x = 3; return true; }", true)]
    [InlineData("int32 Main() { mut int32 x = 0; if (Try(out x, out x)) { return x; } return 0; } bool Try(out int32 a, out int32 b) { a = 1; b = 2; return true; }", false)]
    [InlineData("int32 Main() { if (Try(out var x, x)) { return x; } return 0; } bool Try(out int32 x, int32 value) { x = value; return true; }", false)]
    [InlineData("int32 Main() { mut int32 x = 9; if (Try(out x)) { return 1; } return x; } bool Try(out int32 x) { x = 5; return false; }", false)]
    [InlineData("int32 Main() { mut int32 x = 9; if (Try(out x, x)) { return x; } return 0; } bool Try(out int32 x, int32 previous) { x = previous; return true; }", false)]
    [InlineData("int32 Main() { mut int32 value = 9; Try(out value); return value; } bool Try(out int32 value) { return false; }", false)]
    [InlineData("int32 Main() { mut int32 value = 9; bool result = false || Try(out value); return value; } bool Try(out int32 value) { return false; }", false)]
    [InlineData("int32 Main() { mut int32 value = 9; while (value > 0) { Try(out value); } return 0; } bool Try(out int32 value) { return false; }", false)]
    public void OutDefiniteAssignmentIsVerified(string source, bool valid)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        if (valid)
        {
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            SemanticVerifier.Verify(Assert.IsType<BoundProgram>(compilation.Program));
        }
        else
        {
            Assert.True(compilation.HasErrors);
            Assert.Null(compilation.Program);
        }
    }

    [Fact]
    public async Task CliAnalyzeReportsOutDefiniteAssignmentAsSourceDiagnosticAsync()
    {
        const string Source = """
            int32 Main() {
                if (Try(out var value)) { return value; }
                return value;
            }
            bool Try(out int32 value) { value = 1; return true; }
            """;
        using var json = await AnalyzeSourceAsync(Source).ConfigureAwait(true);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        var diagnostic = json.RootElement.GetProperty("diagnostics")[0];
        Assert.Equal("TRM3016", diagnostic.GetProperty("code").GetString());
        Assert.Equal(2, diagnostic.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
    }

    [Fact]
    public async Task CliAnalyzeBoundsAlternatingShortCircuitFlowWorkAsync()
    {
        var expression = "value";
        for (var depth = 0; depth < 40; depth++)
        {
            expression = $"({expression} {(depth % 2 == 0 ? "&&" : "||")} value)";
        }
        var source = $"int32 Main() {{ bool value = true; if ({expression}) {{ return 1; }} return 0; }}";
        using var json = await AnalyzeSourceAsync(source).ConfigureAwait(true);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        var diagnostic = json.RootElement.GetProperty("diagnostics")[0];
        Assert.Equal("TRM3016", diagnostic.GetProperty("code").GetString());
        Assert.Equal("Source exceeds the semantic verification work limit.", diagnostic.GetProperty("message").GetString());
    }

    private static async Task<JsonDocument> AnalyzeSourceAsync(string source)
    {
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name ?? throw new InvalidOperationException("Missing configuration directory.");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var cli = Path.Combine(root, "artifacts", "bin", "sources", "terrumc", configuration, frameworkDirectory.Name, "terrumc.dll");
        Assert.True(File.Exists(cli));
        var start = new ProcessStartInfo("dotnet") {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(cli);
        start.ArgumentList.Add("analyze");
        start.ArgumentList.Add("-");
        start.ArgumentList.Add("--json");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start compiler.");
        await process.StandardInput.WriteAsync(source).ConfigureAwait(true);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(true);
            }
            throw;
        }
        Assert.Equal(1, process.ExitCode);
        Assert.Equal("", await error.ConfigureAwait(true));
        return JsonDocument.Parse(await output.ConfigureAwait(true));
    }

    [Theory]
    [InlineData("int32 Main() { if (Try(out var x) && x == 7) { return x; } return 0; } bool Try(out int32 x) { x = 7; return true; }", 7)]
    [InlineData("int32 Main() { if (Try(out var x) || false) { return x; } return 0; } bool Try(out int32 x) { x = 7; return true; }", 7)]
    [InlineData("int32 Main() { if (!Try(out var x)) { return 0; } require x == 7; return x; } bool Try(out int32 x) { x = 7; return true; }", 7)]
    [InlineData("int32 Main() { mut int32 x = 9; if (!Try(out x)) { x = 9; } return x; } bool Try(out int32 x) { x = 5; return false; }", 9)]
    [InlineData("int32 Main() { mut int32 x = 9; if (Try(false, out x)) { return 1; } if (Try(true, out x)) { return x; } return 0; } bool Try(bool success, out int32 x) { if (!success) { return false; } x = 7; return true; }", 7)]
    [InlineData("int32 Main() { mut int32 value = 9; bool result = true || Try(out value); return value; } bool Try(out int32 value) { return false; }", 9)]
    [InlineData("int32 Main() { mut int32 value = 9; if (Try(out var x) || Try2(out value)) { return value; } return 0; } bool Try(out int32 x) { x = 1; return true; } bool Try2(out int32 x) { x = 7; return true; }", 9)]
    [InlineData("int32 Main() { require Try(out var x); return x; } bool Try(out int32 x) { if (true) { x = 7; } return true; }", 7)]
    [InlineData("int32 Main() { mut int32 n = 0; while (n < 1 && Try(out var x)) { require x == 7; n = n + 1; } return n; } bool Try(out int32 x) { x = 7; return true; }", 1)]
    public async Task OutputFlowHonorsTrueFalseAndShortCircuitAsync(string source, int expected)
    {
        Assert.Equal(expected, await RunNativeAsync(NativeEmitter.Emit(Compile(source), NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Fact]
    public void ForgedBufferAndOutIrCannotReachCodeGeneration()
    {
        var program = Compile("int32 Main() { return 1; }");
        var function = program.Functions[0];
        var span = function.Span;
        var buffers = ImmutableArray.Create(new HostBuffer("Source", PrimitiveType.Int32, [1], false));
        var forgedWrite = new BoundBufferAssignment(0, new BoundConstant(0, PrimitiveType.Int32, span),
            new BoundConstant(3, PrimitiveType.Int32, span), span);
        var invalid = program with {
            Buffers = buffers,
            Functions = [function with {
                Body = new BoundBlock([forgedWrite, new BoundReturn(new BoundConstant(1, PrimitiveType.Int32, span), span)], span),
            }],
        };
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(invalid, NativeRuntimePolicy.FailFast));
        invalid = program with {
            Functions = [function with {
                Variables = [new BoundVariable("x", PrimitiveType.Int32, true, span)],
                Body = new BoundBlock([new BoundReturn(
                    new BoundOutArgument(0, true, PrimitiveType.Int32, span), span)], span),
            }],
        };
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(invalid, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void NamespaceShapedFunctionsCannotReadPrivateStructFields()
    {
        var program = Compile("struct S { private int32 A; public S() { A = 1; } } int32 Main() { return 1; }");
        var span = program.Functions[0].Span;
        var type = (PrimitiveType)100;
        var impostor = new BoundFunction("S.Spoof", PrimitiveType.Int32,
            [new BoundVariable("value", type, false, span)], 1,
            new BoundBlock([new BoundReturn(new BoundFieldRead(new BoundRead(0, type, span),
                0, PrimitiveType.Int32, span), span)], span), span);
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.Add(impostor),
        }));
    }

    [Fact]
    public void MalformedValueLayoutsAndNegativeCodeUnitsFailVerification()
    {
        var program = Compile("struct S { public bool A; public S() { A = true; } } int32 Main() { return 0; }");
        var structure = program.Structures[0];
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Structures = program.Structures.SetItem(0, structure with { Size = 3 }),
        }));
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Structures = program.Structures.SetItem(0, structure with { Fields = structure.Fields.SetItem(0, null!) }),
        }));
        var constructor = program.Functions[0];
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(0, constructor with { Variables = constructor.Variables.SetItem(0, null!) }),
        }));
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Buffers = [new HostBuffer("Source", PrimitiveType.Char16, [-1], false)],
        }));
        var main = program.Functions[1];
        var span = main.Span;
        var type = (PrimitiveType)100;
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(1, main with {
                Body = new BoundBlock([new BoundReturn(
                    new BoundFieldRead(null!, 0, PrimitiveType.Bool, span), span)], span),
            }),
        }));
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(1, main with {
                Body = new BoundBlock([new BoundReturn(new BoundFieldRead(
                    new BoundConstant(0, type, span), 0, PrimitiveType.Bool, span), span)], span),
            }),
        }));
        var equality = new BoundBinary(TokenKind.EqualsEquals,
            new BoundCall(0, [], type, span), new BoundCall(0, [], type, span), PrimitiveType.Bool, span);
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(1, main with {
                Body = new BoundBlock([new BoundIf(equality,
                    new BoundReturn(new BoundConstant(1, PrimitiveType.Int32, span), span),
                    new BoundReturn(new BoundConstant(0, PrimitiveType.Int32, span), span), span)], span),
            }),
        }));
    }

    [Theory]
    [InlineData("struct S { int32 A; int32 B; public S(int32 a) { this.A = a; } } int32 Main() { S s = new S(1); return s.B; }")]
    [InlineData("struct S { int32 A; public S() { int32 x = this.A; this.A = x; } } int32 Main() { S s = new S(); return s.A; }")]
    [InlineData("struct S { int32 A; private S(int32 a) { this.A = a; } } int32 Main() { S s = new S(1); return s.A; }")]
    [InlineData("struct S { int32 A; public S() { this.A = 2; } } int32 Main() { S s = new S(); s.A = 4; return s.A; }")]
    [InlineData("struct S { public int32 A; public S() { A = 1; } } int32 Change(S value) { value.A = 2; return value.A; } int32 Main() { S value = new S(); return Change(value); }")]
    public void UninitializedOrImmutableStructStorageCannotBeReadOrMutated(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("int32 Main() { return (int32)4294967296u32; }")]
    [InlineData("int32 Main() { char16 c = 65536; return 0; }")]
    [InlineData("int32 Main() { return (int32)true; }")]
    [InlineData("int32 Main() { var n = 42; return n; }")]
    [InlineData("int32 Main() { uint32 n = -1; return 0; }")]
    [InlineData("struct S { public int32 A; public S() { A = 1; } public mut void Set() { A = 2; } } int32 Main() { S s = new S(); s.Set(); return s.A; }")]
    [InlineData("struct S { mut int32 A; public S() { A = 1; } } int32 Main() { return 0; }")]
    [InlineData("struct S { static int32 A; } int32 Main() { return 0; }")]
    [InlineData("struct S { int32 A = 1; } int32 Main() { return 0; }")]
    [InlineData("struct S { bool A; bool B; bool C; } int32 Main() { return 0; }")]
    [InlineData("private struct S { int32 A; } int32 Main() { return 0; }")]
    [InlineData("struct S { private int32 A; public S() { A = 1; } } int32 Main() { S value = new S(); return value.A; }")]
    public void InvalidValuesDoNotProduceExecutableIr(string source)
    {
        Assert.Null(Compilation.Create(SyntaxTree.Parse(source)).Program);
    }

    private static BoundProgram Compile(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    private static async Task<int> RunNativeAsync(byte[] image)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var path = Path.Combine(Directory.GetCurrentDirectory(), $"terrum-value-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true })
                ?? throw new InvalidOperationException("Native process did not start.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }
                throw;
            }
            return process.ExitCode;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
