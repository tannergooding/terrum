// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class InitReferenceTests
{
    internal const string ScalarSource = """
        bool Fill(bool publish, init ref<int32> value) {
            if (!publish) { return false; }
            value = 42;
            return true;
        }
        int32 Main() {
            if (Fill(false, init var value)) { return 1; }
            require Fill(true, init value);
            return value;
        }
        """;

    [Fact]
    public void InitReferenceHasDistinctBoundParameterAndArgumentModes()
    {
        var tree = SyntaxTree.Parse(ScalarSource);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal(ScalarSource, tree.Root.ToFullString());
        var compilation = Compilation.Create(tree);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var fill = Assert.Single(program.Functions, function => function.Name == "Fill");
        Assert.True(fill.Variables[1].IsOut);
        Assert.True(fill.Variables[1].IsInit);
        Assert.Equal("syntax-23", SyntaxTree.LanguageVersion);
    }

    [Theory]
    [InlineData("if (Fill(false, init var value)) { return 1; }", "require Fill(true, init var value);", "TRM3016")]
    [InlineData("require Fill(true, init value);", "require Fill(true, out value);", "TRM3011")]
    [InlineData("require Fill(true, init value);", "return value;", "TRM3016")]
    [InlineData("require Fill(true, init value);", "require Fill(true, init value); require Fill(true, init value);", "TRM3016")]
    [InlineData("if (Fill(false, init var value)) { return 1; }", "if (Fill(false, init var value)) { return 1; } return value;", "TRM3016")]
    public void InitReferencesRejectInvalidSource(string before, string after, string diagnostic)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(ScalarSource.Replace(before, after,
            StringComparison.Ordinal)));
        Assert.Contains(compilation.Diagnostics, item => item.Code == diagnostic);
    }

    [Fact]
    public void PublicInitContractIsRejectedUntilItsModuleModeIsRepresented()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(ScalarSource.Replace("bool Fill(",
            "public bool Fill(", StringComparison.Ordinal)));
        Assert.Contains(compilation.Diagnostics, item => item.Code == "TRM3011");
    }

    [Fact]
    public void PrivateInitPreservesExplicitNoThrowProof()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(ScalarSource.Replace("bool Fill(",
            "[NoThrow] bool Fill(", StringComparison.Ordinal)));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Theory]
    [InlineData("int32 value = 1; require Fill(true, init value);")]
    [InlineData("if (Fill(true, init var value)) { require Fill(true, init value); }")]
    [InlineData("if (Fill(true, init var value)) { return value; } return value;")]
    public void AnInitializedOrPossiblyVacantDestinationCannotBeReinitializedOrRead(string body)
    {
        var source = ScalarSource[..ScalarSource.IndexOf("int32 Main()", StringComparison.Ordinal)]
            + "int32 Main() { " + body + " return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3016");
    }

    [Fact]
    public void RepeatedInitAliasInOneCallIsRejected()
    {
        const string Source = """
            bool Split(init ref<int32> first, init ref<int32> second) {
                first = 40;
                second = 2;
                return true;
            }
            int32 Main() {
                require Split(init var result, init result);
                return result;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code is "TRM3023" or "TRM3016");
    }

    [Theory]
    [InlineData("void Fill(init ref<int32> value) { value = 42; }", "TRM3011")]
    [InlineData("bool Fill(init ref<int32> value) { return true; }", "TRM3016")]
    [InlineData("bool Fill(init ref<ManagedNode> value) { return false; }", "TRM3011")]
    [InlineData("bool Fill(out int32 value) { value = 42; return true; } int32 Main() { require Fill(init var item); return item; }", "TRM3011")]
    public void InitModeRequiresBooleanCompletionAndMatchingCall(string source, string code)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source.Contains("int32 Main()", StringComparison.Ordinal)
            ? source : source + "int32 Main() { return 42; }"));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task PrivateInitHelperServicesUnchangedSemanticAndNativeConsumersAsync(NativeRuntimePolicy policy)
    {
        var sourcePolicy = policy == NativeRuntimePolicy.FailFast
            ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
        const string Library = """
            bool Fill(bool publish, init ref<int32> value) {
                if (!publish) { return false; }
                value = 42;
                return true;
            }
            public int32 Read() {
                if (Fill(false, init var result)) { return 1; }
                require Fill(true, init result);
                return result;
            }
            """;
        const string Consumer = "int32 Main() { return Read(); }";
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(Library), runtimePolicy: sourcePolicy);
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var producerProgram = Assert.IsType<BoundProgram>(library.Program);
        var producer = ModuleCompiler.CreateProducer(producerProgram, ["Read"]);
        Assert.Equal(33, BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        var compilation = Compilation.Create(SyntaxTree.Parse(Consumer),
            ModuleCompiler.CreateReference(producer), runtimePolicy: sourcePolicy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.True(Assert.Single(linked.Functions, function => function.Name == "$producer.Fill")
            .Variables[1].IsInit);
        Assert.Equal(42, NativeJit.Execute(linked, policy));
        Assert.NotEmpty(NativeEmitter.Emit(linked, policy));

        var nativeProducer = NativeModuleCompiler.CreateProducer(producerProgram, ["Read"], "NodeB", policy);
        var nativeCompilation = Compilation.Create(SyntaxTree.Parse(Consumer),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), runtimePolicy: sourcePolicy);
        Assert.False(nativeCompilation.HasErrors, string.Join(Environment.NewLine, nativeCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(nativeCompilation.Program), [nativeProducer], policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
    }

    [Fact]
    public void LegacyOutProducerRetainsItsSelectedSemanticVersion()
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("""
            bool Fill(out int32 value) { value = 42; return true; }
            public int32 Read() { require Fill(out var value); return value; }
            """));
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(library.Program), ["Read"]);
        Assert.NotEqual(33, BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
    }

    [Fact]
    public void PrivateInitModeRequiresItsSemanticReaderVersion()
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("""
            bool Fill(init ref<int32> value) { value = 42; return true; }
            public int32 Read() { require Fill(init var result); return result; }
            """));
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(library.Program), ["Read"]);
        Assert.Equal(33, BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        BinaryPrimitives.WriteInt32LittleEndian(producer.AsSpan(4, 4), 32);
        _ = Assert.Throws<ModuleFormatException>(() => _ = ModuleCompiler.CreateReference(producer));
    }

    [Fact]
    public void GenericInitParametersDiagnoseUntilGenericLoanProofIsSupported()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            bool Fill<T>(T value, init ref<T> result) where T : [Copy] {
                result = value;
                return true;
            }
            int32 Main() { return 42; }
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
    }
}
