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
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Semantics;

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
    [TestCase("init var value;")]
    [TestCase("init int32 value;")]
    [TestCase("init /* comment */ int32 value;")]
    [TestCase("int32 init = 42;")]
    [TestCase("init();")]
    public void VacantSyntaxAndContextualIdentifierRemainLossless(string statement)
    {
        var source = "void F() { " + statement + " }";
        var tree = SyntaxTree.Parse(source);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
        if (statement.StartsWith("int32 init", StringComparison.Ordinal)
            || statement == "init();")
        {
            Assert.That(global::System.Linq.Enumerable.Any(tree.Root.DescendantNodes(),node => node.Kind == SyntaxKind.VacantLocalDeclaration),Is.False);
        }
        else
        {
            var local = global::System.Linq.Enumerable.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.VacantLocalDeclaration);
            Assert.That(((SyntaxTokenElement)local.Children[0]).Token.Text, Is.EqualTo("init"));
        }
    }
    [TestCase("int32 value;","TRM1001")]
    [TestCase("var value;","TRM1001")]
    [TestCase("init var value = 1;","TRM1001")]
    public void VacantSyntaxRejectsOtherDeclarations(string statement, string code)
    {
        var source = "void F() { " + statement + " }";
        var tree = SyntaxTree.Parse(source);
        Assert.That(global::System.Linq.Enumerable.Any(tree.Diagnostics,diagnostic => diagnostic.Code == code),Is.True);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
    }
    [TestCase("init int32 value;",PrimitiveType.Int32)]
    [TestCase("init var value;",PrimitiveType.Int32)]
    public void BothDeclarationsBindAReadOnlyVacantSlot(string declaration, PrimitiveType type)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            ScalarSource.Replace("init var value;", declaration, StringComparison.Ordinal)));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        var read = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "Read");
        var variable = global::System.Linq.Enumerable.Single(read.Variables, item => item.Name == "value");
        Assert.That(variable.Type, Is.EqualTo(type));
        Assert.That(variable.IsMutable, Is.False);
        Assert.That(global::System.Linq.Enumerable.Any(read.Body.Statements,statement => statement is BoundVacantLocal),Is.True);
    }

    [Test]
    public void VerifierRejectsMalformedVacantSlotAndAcceptsWritableVacantDeclaration()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(ScalarSource));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        var index = program.Functions.Select((function, slot) => (function, slot))
            .Single(item => item.function.Name == "Read").slot;
        var read = program.Functions[index];
        var vacant = ((global::System.Func<BoundVacantLocal>)(() => { var __nunitActual2 = read.Body.Statements[0]; Assert.That(__nunitActual2, Is.TypeOf<BoundVacantLocal>()); return (BoundVacantLocal)__nunitActual2!; }))();
        var malformed = read with {
            Body = read.Body with {
                Statements = read.Body.Statements.SetItem(0, vacant with { Variable = -1 }),
            },
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(index, malformed),
        }))!;

        var mutableCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                init mut int32 value;
                value = 1;
                value = 2;
                return value;
            }
            """));
        Assert.That(mutableCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, mutableCompilation.Diagnostics));
        SemanticVerifier.Verify(mutableCompilation.Program!);
    }

    [Test]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))();
        var index = program.Functions.Select((function, slot) => (function, slot))
            .Single(item => item.function.Name == "Main").slot;
        var main = program.Functions[index];
        var vacant = ((global::System.Func<BoundVacantLocal>)(() => { var __nunitActual4 = main.Body.Statements[0]; Assert.That(__nunitActual4, Is.TypeOf<BoundVacantLocal>()); return (BoundVacantLocal)__nunitActual4!; }))();
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
        }))!;
    }
    [TestCase("return value;","TRM3016")]
    [TestCase("if (Fill(true, init value)) { return value; } return value;","TRM3016")]
    [TestCase("require Fill(true, init value); require Fill(true, init value); return value;","TRM3016")]
    [TestCase("require Fill(true, init value); value = 1; return value;","TRM3016")]
    [TestCase("require Fill(true, out value); return value;","TRM3011")]
    [TestCase("init var value;","TRM3016")]
    public void VacantLocalCannotBeReadOrReinitializedOutsideItsCheckedCompletion(string body, string code)
    {
        var source = ScalarSource[..ScalarSource.IndexOf("public int32 Read()", StringComparison.Ordinal)]
            + "int32 Main() { init var value; " + body + " return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == code),Is.True);
    }
    [TestCase("for (init int32 value; Fill(true, init value); ) { return value; } return 42;",true)]
    [TestCase("while (false) { require Fill(true, init value); } return value;",false)]
    public void LoopInitializationRespectsTheEntryAndBackEdge(string body, bool valid)
    {
        var declaration = body.StartsWith("for", StringComparison.Ordinal) ? "" : "init int32 value;";
        var source = ScalarSource[..ScalarSource.IndexOf("public int32 Read()", StringComparison.Ordinal)]
            + "int32 Main() { " + declaration + body + " }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        if (valid)
        {
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
        else
        {
            Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3016"),Is.True);
        }
    }
    [TestCase("init int64 value;","Fill(init value)")]
    [TestCase("init var value;","Copy(init value)")]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3011"),Is.True);
    }

    [Test]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task StandaloneVacancyRoundTripsModuleAndNativeConsumersAsync(NativeRuntimePolicy policy)
    {
        var runtimePolicy = (BoundRuntimePolicy)policy;
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(ScalarSource.Replace(
            "int32 Main() { return Read(); }", "", StringComparison.Ordinal)), runtimePolicy: runtimePolicy);
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = library.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))();
        var producer = ModuleCompiler.CreateProducer(program, ["Read"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)), Is.EqualTo(76));
        var oldHeader = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(oldHeader.AsSpan(4, 4), 39);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(oldHeader))!;
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Read(); }"),
            reference, runtimePolicy: runtimePolicy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = consumerCompilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(NativeJit.Execute(linked, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(linked, policy), Is.Not.Empty);

        var nativeProducer = NativeModuleCompiler.CreateProducer(program, ["Read"], "NodeB", policy);
        var nativeConsumerCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Read(); }"),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), runtimePolicy: runtimePolicy);
        Assert.That(nativeConsumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, nativeConsumerCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = nativeConsumerCompilation.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))(), [nativeProducer], policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        var replacementCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(ScalarSource.Replace(
            "value = 42;", "value = 43;", StringComparison.Ordinal).Replace(
            "int32 Main() { return Read(); }", "", StringComparison.Ordinal)), runtimePolicy: runtimePolicy);
        Assert.That(replacementCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, replacementCompilation.Diagnostics));
        var replacement = NativeModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = replacementCompilation.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))(), ["Read"], "NodeB", policy);
        NativeModuleCompiler.ValidateGraph(nativeConsumer, [replacement]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [replacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(43));
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..",
            "artifacts", "bin", "sources", "terrumc", configuration, frameworkDirectory.Name, "terrumc.exe"));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [replacement],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(43));
    }
}
