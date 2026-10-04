// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class DotAttachmentTests
{
    private static readonly string[] Gaps = ["", " ", "\n", "\r\n", "/* gap */", "//** gap **//", "// gap\n", "/// gap\n"];
    private static readonly string[] QualifiedForms = [
        "using N#Types;",
        "namespace N#Types;",
        "using @namespace#\U00010400;",
        "namespace \U00010400#@namespace;",
        "N#T F() { return value; }",
        "void F(N#T value) { }",
        "struct Box { public N#T Value; }",
        "struct Box { public N#T Value => value; }",
        "void F() { N#T value = source; }",
        "void F(init ref<N#T> value) { }",
        "struct Box : N#I<Box> { }",
        "T F<T>(T value) where T : N#I<T> { return value; }",
        "N#Box<\nN.Value\n> F() { return value; }",
        "int32 Main() { return F<N#T>(42); }",
        "int32 Main() { return F<Box<Box<N#T>>>(42); }",
        "int32 Main() { var value = new N#T(42); return 42; }",
    ];
    private static readonly string[] MemberForms = [
        "int32 Main() { return value#Field; }",
        "int32 Main() { return int32#MaxValue; }",
        "int32 Main() { return @await#@return; }",
        "int32 Main() { return \U00010400#\U00010401; }",
        "int32 Main() { return value.Parent#Field; }",
        "int32 Main() { return (value.Parent)#Field; }",
        "int32 Main() { return Get()#Field; }",
        "int32 Main() { return values[0]#Field; }",
        "int32 Main() { return Get()[0]#Field; }",
        "int32 Main() { return F<Box<Box<int32>>>()#Field; }",
        "int32 Main() { return (F<Box<Box<int32>>>())#Field; }",
        "int32 Main() { return new N.T(42)#Field; }",
        "int32 Main() { return (42)#Field; }",
        "int32 Main() { return value#Method(42).Field; }",
    ];

    public static IEnumerable<object[]> Boundaries =>
        from qualified in new[] { true, false }
        from form in qualified ? QualifiedForms : MemberForms
        from pair in BoundaryPairs()
        select new object[] { form, pair.Left, pair.Right, qualified };

    private static IEnumerable<(string Left, string Right)> BoundaryPairs()
    {
        yield return ("", "");
        foreach (var gap in Gaps.Skip(1))
        {
            yield return (gap, "");
            yield return ("", gap);
            yield return (gap, gap);
        }
        yield return ("/* left */", "/// right\n");
        yield return ("\r\n", "//** right **//");
    }

    [Theory]
    [MemberData(nameof(Boundaries))]
    public void DotRoleUsesRawBoundariesBeforeBinding(string form, string left, string right, bool qualified)
    {
        var source = form.Replace("#", left + "." + right, StringComparison.Ordinal);
        var dot = new TextSpan(form.IndexOf('#') + left.Length, 1);
        var invalid = qualified ? left.Length != 0 || right.Length != 0 : left.Length != 0 && right.Length != 0;
        var lexed = Lexer.Lex(SourceText.From(source));
        Assert.Empty(lexed.Diagnostics);
        foreach (var tree in new[] { SyntaxTree.Parse(source), SyntaxTree.Parse(lexed) })
        {
            Assert.Equal(source, tree.Root.ToFullString());
            if (invalid)
            {
                var diagnostic = Assert.Single(tree.Diagnostics);
                Assert.Equal("TRM1007", diagnostic.Code);
                Assert.Equal(dot, diagnostic.Span);
            }
            else
            {
                Assert.Empty(tree.Diagnostics);
            }
        }
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CompiledScannerAdapterPreservesEveryRoleAndBoundaryAsync(NativeRuntimePolicy policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var scanner = await ScannerTests.ReadScannerSourceAsync(root);
        var chunk = new StringBuilder();
        var chunks = new List<string>();
        var expectedFailures = 0;
        foreach (var row in Boundaries)
        {
            var form = (string)row[0];
            var left = (string)row[1];
            var right = (string)row[2];
            var qualified = (bool)row[3];
            if (qualified ? left.Length != 0 || right.Length != 0 : left.Length != 0 && right.Length != 0)
            {
                expectedFailures++;
            }
            var text = form.Replace("#", left + "." + right, StringComparison.Ordinal) + "\n";
            if (chunk.Length + text.Length > ScannerHost.MaximumSourceLength)
            {
                chunks.Add(chunk.ToString());
                chunk.Clear();
            }
            chunk.Append(text);
        }
        chunks.Add(chunk.ToString());
        var actualFailures = 0;
        foreach (var text in chunks)
        {
            var source = SourceText.From(text);
            var buffers = ScannerHost.CreateBuffers(source);
            var compilation = Compilation.Create(
                SyntaxTree.Parse(scanner + "int32 Main() { return Scan(); }"), buffers, (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var native = NativeJit.ExecuteWithData(Assert.IsType<BoundProgram>(compilation.Program), policy);
            var raw = ScannerHost.ReadTokens(source, native.ExitCode, Buffer(3));
            Assert.Equal(Lexer.Lex(source).Tokens.Select(token => new ScannerToken(token.Kind, token.Span)), raw);
            var adapted = ScannerHost.ReadLexResult(source, native.ExitCode, Buffer(3), Buffer(5), Buffer(4));
            var hosted = SyntaxTree.Parse(source);
            var current = SyntaxTree.Parse(adapted);
            Assert.Equal(text, current.Root.ToFullString());
            Assert.Equal(hosted.Diagnostics.ToArray(), current.Diagnostics.ToArray());
            actualFailures += current.Diagnostics.Count(diagnostic => diagnostic.Code == "TRM1007");

            ReadOnlySpan<byte> Buffer(int index) =>
                native.Data.AsSpan(native.BufferOffsets[index], buffers[index].Values.Length * sizeof(int));
        }
        Assert.Equal(expectedFailures, actualFailures);
    }

    [Theory]
    [InlineData("int32 Main() { return F<N /* left */ .T>(42); }")]
    [InlineData("int32 Main() { return F<N. /* right */ T>(42); }")]
    public void GenericCallSelectionDoesNotFallBackAfterQualifiedDotFailure(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.Equal("TRM1007", Assert.Single(tree.Diagnostics).Code);
        Assert.Contains(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.GenericNameExpression);
        Assert.Equal(source, tree.Root.ToFullString());
    }

    [Theory]
    [InlineData("using N .")]
    [InlineData("namespace N. ")]
    [InlineData("N .T")]
    [InlineData("int32 Main() { return value /* left */ . ")]
    [InlineData("int32 Main() { return value.")]
    public void IncompleteDotFormsStillRoundTrip(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.NotEmpty(tree.Diagnostics);
        Assert.Equal(source, tree.Root.ToFullString());
    }

    [Theory]
    [InlineData("[N.Policy] int32 Main() { return 42; }")]
    [InlineData("int32 Main() { return node?.Value; }")]
    public void UnsupportedQualifiedAttributesAndOptionalAccessRemainUnsupported(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.True(tree.HasErrors);
        Assert.Equal(source, tree.Root.ToFullString());
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void LookupDoesNotChangeExpressionDotRules(NativeRuntimePolicy policy)
    {
        foreach (var (left, right) in BoundaryPairs().Where(pair => pair.Left.Length == 0 || pair.Right.Length == 0))
        {
            var dot = left + "." + right;
            foreach (var body in new[] {
                "return int32" + dot + "MaxValue - 2147483605;",
                "var value = new Box(42); return value" + dot + "Read();",
                "return (new Box(42))" + dot + "Value;",
                "return Identity<Box>(new Box(42))" + dot + "Value;",
            })
            {
                var source = """
                    struct Box {
                        public int32 Value;
                        public Box(int32 value) { Value = value; }
                        public int32 Read() { return Value; }
                    }
                    T Identity<T>(T value) where T : [Copy] { return value; }
                    """ + "int32 Main() { " + body + " }";
                var compilation = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
                Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
                Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(compilation.Program), policy));
            }
        }
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task QualifiedIdentityServicesUnchangedPersistedConsumersAsync(NativeRuntimePolicy policy)
    {
        BoundProgram Producer(string body)
        {
            var compilation = Compilation.CreateLibrary(
                SyntaxTree.Parse("namespace @namespace.N; public int32 @await() { " + body + " }"), (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            return Assert.IsType<BoundProgram>(compilation.Program);
        }
        var original = Producer("return 42;");
        var changed = Producer("return int32 .MaxValue - 2147483605;");
        var producer = ModuleCompiler.CreateProducer(original, ["namespace.N.await"]);
        var replacement = ModuleCompiler.CreateProducer(changed, ["namespace.N.await"]);
        const string Client = "using @namespace.N; int32 Main() { return @namespace .N.@await(); }";
        var client = Compilation.Create(SyntaxTree.Parse(Client), ModuleCompiler.CreateReference(producer), (BoundRuntimePolicy)policy);
        Assert.False(client.HasErrors, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(client.Program), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, replacement), policy));
        var nativeProducer = NativeModuleCompiler.CreateProducer(original, ["namespace.N.await"], "DotAttachment", policy);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(changed, ["namespace.N.await"], "DotAttachment", policy);
        var nativeClient = Compilation.Create(SyntaxTree.Parse(Client),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), (BoundRuntimePolicy)policy);
        Assert.False(nativeClient.HasErrors, string.Join(Environment.NewLine, nativeClient.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Assert.IsType<BoundProgram>(nativeClient.Program), [nativeProducer], policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement], TimeSpan.FromSeconds(10), policy));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" /* left */ ")]
    public void ScalarProjectionDoesNotAcquireMemberOrDirectiveSourceSupport(string gap)
    {
        foreach (var text in new[] {
            "int32 Main() { return int32" + gap + ".MaxValue; }",
            "using N" + gap + ".Types; int32 Main() { return 42; }",
        })
        {
            var words = new int[ScalarFrontendHost.WordCount];
            words[0] = 85; words[1] = 1; words[5] = 1;
            words[9] = text.IndexOf("Main", StringComparison.Ordinal); words[10] = 4;
            words[22] = 1; words[23] = 3;
            var start = text.IndexOf("return", StringComparison.Ordinal) + 7;
            words[24] = text.Contains("MaxValue", StringComparison.Ordinal) ? int.MaxValue : 42;
            words[26] = start; words[27] = text.IndexOf(';', start) - start;
            var bytes = new byte[words.Length * 4];
            for (var index = 0; index < words.Length; index++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4), words[index]);
            }
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), bytes));
        }
    }
}
