// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class VacantLocalTests
{
    internal const string ScalarSource = """
        bool Fill(bool publish, init ref<int32> value) {
            if (!publish) { return false; }
            value = 42;
            return true;
        }
        public int32 Read() {
            init var value;
            if (Fill(false, init value)) { return 1; }
            require Fill(true, init value);
            return value;
        }
        int32 Main() { return Read(); }
        """;

    [Theory]
    [InlineData("init var value;")]
    [InlineData("init int32 value;")]
    [InlineData("init /* comment */ int32 value;")]
    [InlineData("int32 init = 42;")]
    [InlineData("init();")]
    public void VacantSyntaxAndContextualIdentifierRemainLossless(string statement)
    {
        var source = "void F() { " + statement + " }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal(source, tree.Root.ToFullString());
        if (statement.StartsWith("int32 init", StringComparison.Ordinal)
            || statement == "init();")
        {
            Assert.DoesNotContain(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.VacantLocalDeclaration);
        }
        else
        {
            var local = Assert.Single(tree.Root.DescendantNodes(),
                node => node.Kind == SyntaxKind.VacantLocalDeclaration);
            Assert.Equal("init", ((SyntaxTokenElement)local.Children[0]).Token.Text);
        }
    }

    [Theory]
    [InlineData("int32 value;", "TRM1001")]
    [InlineData("var value;", "TRM1001")]
    [InlineData("init var value = 1;", "TRM1001")]
    public void VacantSyntaxRejectsOtherDeclarations(string statement, string code)
    {
        var source = "void F() { " + statement + " }";
        var tree = SyntaxTree.Parse(source);
        Assert.Contains(tree.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Equal(source, tree.Root.ToFullString());
    }

    [Theory]
    [InlineData("init int32 value;", PrimitiveType.Int32)]
    [InlineData("init var value;", PrimitiveType.Int32)]
    public void BothDeclarationsBindAReadOnlyVacantSlot(string declaration, PrimitiveType type)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            ScalarSource.Replace("init var value;", declaration, StringComparison.Ordinal)));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var read = Assert.Single(program.Functions, function => function.Name == "Read");
        var variable = Assert.Single(read.Variables, item => item.Name == "value");
        Assert.Equal(type, variable.Type);
        Assert.False(variable.IsMutable);
        Assert.Contains(read.Body.Statements, statement => statement is BoundVacantLocal);
    }

    [Fact]
    public void VerifierRejectsMalformedVacantSlotAndWritableForgedDeclaration()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(ScalarSource));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var index = program.Functions.Select((function, slot) => (function, slot))
            .Single(item => item.function.Name == "Read").slot;
        var read = program.Functions[index];
        var vacant = Assert.IsType<BoundVacantLocal>(read.Body.Statements[0]);
        var malformed = read with {
            Body = read.Body with {
                Statements = read.Body.Statements.SetItem(0, vacant with { Variable = -1 }),
            },
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(index, malformed),
        }));
        var writable = read with {
            Variables = read.Variables.SetItem(vacant.Variable, read.Variables[vacant.Variable] with {
                IsMutable = true,
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(index, writable),
        }));
    }

    [Fact]
    public void VerifierRejectsVacantOwnerHiddenOutsideItsCleanupScope()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public int32 Number;
                public Owner() { Number = 0; }
                [NoThrow] [Cleanup] public void Close() { }
            }
            int32 Main() { init Owner owner; return 42; }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var index = program.Functions.Select((function, slot) => (function, slot))
            .Single(item => item.function.Name == "Main").slot;
        var main = program.Functions[index];
        var vacant = Assert.IsType<BoundVacantLocal>(main.Body.Statements[0]);
        var malformed = main with {
            Body = main.Body with {
                Statements = [
                    new BoundIf(new BoundConstant(1, PrimitiveType.Bool, default), vacant, null, default),
                    new BoundReturn(new BoundConstant(42, PrimitiveType.Int32, default), default),
                ],
            },
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(index, malformed),
        }));
    }

    [Theory]
    [InlineData("return value;", "TRM3016")]
    [InlineData("if (Fill(true, init value)) { return value; } return value;", "TRM3016")]
    [InlineData("require Fill(true, init value); require Fill(true, init value); return value;", "TRM3016")]
    [InlineData("require Fill(true, init value); value = 1; return value;", "TRM3016")]
    [InlineData("require Fill(true, out value); return value;", "TRM3011")]
    [InlineData("init var value;", "TRM3016")]
    public void VacantLocalCannotBeReadOrReinitializedOutsideItsCheckedCompletion(string body, string code)
    {
        var source = ScalarSource[..ScalarSource.IndexOf("public int32 Read()", StringComparison.Ordinal)]
            + "int32 Main() { init var value; " + body + " return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Theory]
    [InlineData("for (init int32 value; Fill(true, init value); ) { return value; } return 42;", true)]
    [InlineData("while (false) { require Fill(true, init value); } return value;", false)]
    public void LoopInitializationRespectsTheEntryAndBackEdge(string body, bool valid)
    {
        var declaration = body.StartsWith("for", StringComparison.Ordinal) ? "" : "init int32 value;";
        var source = ScalarSource[..ScalarSource.IndexOf("public int32 Read()", StringComparison.Ordinal)]
            + "int32 Main() { " + declaration + body + " }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        if (valid)
        {
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
        else
        {
            Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3016");
        }
    }

    [Theory]
    [InlineData("init int64 value;", "Fill(init value)")]
    [InlineData("init var value;", "Copy(init value)")]
    public void VacantInferenceRejectsIncompatibleOrUnresolvedOutput(string declaration, string call)
    {
        var source = $$"""
            bool Fill(init ref<int32> value) { value = 42; return true; }
            [NoThrow] bool Copy<T>(T source, init ref<T> value) where T : [Copy] {
                value = source;
                return true;
            }
            int32 Main() {
                {{declaration}}
                require {{call}};
                return 42;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3011");
    }

    [Fact]
    public void GenericInputAndVacantOutputCannotEstablishContradictoryTypes()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow] bool Copy<T>(T source, init ref<T> result) where T : [Copy] {
                result = source;
                return true;
            }
            int32 Main() {
                init int64 output;
                require Copy(int32(42), init output);
                return 42;
            }
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task StandaloneVacancyRoundTripsModuleAndNativeConsumersAsync(NativeRuntimePolicy policy)
    {
        var runtimePolicy = (BoundRuntimePolicy)policy;
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(ScalarSource.Replace(
            "int32 Main() { return Read(); }", "", StringComparison.Ordinal)), runtimePolicy: runtimePolicy);
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var program = Assert.IsType<BoundProgram>(library.Program);
        var producer = ModuleCompiler.CreateProducer(program, ["Read"]);
        Assert.Equal(57, BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        var oldHeader = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(oldHeader.AsSpan(4, 4), 39);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(oldHeader));
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Read(); }"),
            reference, runtimePolicy: runtimePolicy);
        Assert.False(consumerCompilation.HasErrors, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Equal(42, NativeJit.Execute(linked, policy));
        Assert.NotEmpty(NativeEmitter.Emit(linked, policy));

        var nativeProducer = NativeModuleCompiler.CreateProducer(program, ["Read"], "NodeB", policy);
        var nativeConsumerCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Read(); }"),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), runtimePolicy: runtimePolicy);
        Assert.False(nativeConsumerCompilation.HasErrors,
            string.Join(Environment.NewLine, nativeConsumerCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(nativeConsumerCompilation.Program), [nativeProducer], policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var replacementCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(ScalarSource.Replace(
            "value = 42;", "value = 43;", StringComparison.Ordinal).Replace(
            "int32 Main() { return Read(); }", "", StringComparison.Ordinal)), runtimePolicy: runtimePolicy);
        Assert.False(replacementCompilation.HasErrors,
            string.Join(Environment.NewLine, replacementCompilation.Diagnostics));
        var replacement = NativeModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(replacementCompilation.Program), ["Read"], "NodeB", policy);
        NativeModuleCompiler.ValidateGraph(nativeConsumer, [replacement]);
        Assert.Equal(43, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [replacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..",
            "artifacts", "bin", "sources", "terrumc", configuration, frameworkDirectory.Name, "terrumc.exe"));
        Assert.Equal(43, await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [replacement],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
    }
}
