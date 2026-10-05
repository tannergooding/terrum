// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class CallerUnsafeTests
{
    private static BoundProgram CompileLibrary(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    [Theory]
    [InlineData("unsafe { return Danger(); }")]
    [InlineData("return unsafe(Danger());")]
    [InlineData("unsafe(Danger()); return 42;")]
    public void ExplicitUnsafeScopesAuthorizeOnlyTheirCall(string body)
    {
        var source = $"unsafe int32 Danger() {{ return 42; }} int32 Caller() {{ {body} }}";
        var program = CompileLibrary(source);
        Assert.True(Assert.Single(program.Functions, static function => function.Name == "Danger")
            .RequiresUnsafeCaller);
        Assert.False(Assert.Single(program.Functions, static function => function.Name == "Caller")
            .RequiresUnsafeCaller);
    }

    [Fact]
    public void UnsafeDeclarationDoesNotAuthorizeItsBodyOrFollowingCalls()
    {
        var bodyCall = Compilation.CreateLibrary(SyntaxTree.Parse(
            "unsafe int32 Danger() { return 42; } unsafe int32 Wrapper() { return Danger(); }"));
        Assert.Null(bodyCall.Program);
        Assert.Contains(bodyCall.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var leakedScope = Compilation.CreateLibrary(SyntaxTree.Parse(
            "unsafe int32 Danger() { return 42; } int32 Caller() { unsafe(Danger()); return Danger(); }"));
        Assert.Null(leakedScope.Program);
        Assert.Contains(leakedScope.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");
    }

    [Fact]
    public void SafeWrapperMayUseUnsafeImplementationWithoutPropagatingTheObligation()
    {
        var program = CompileLibrary("""
            public unsafe int32 Danger() { return 42; }
            public int32 Wrapper() { unsafe { return Danger(); } }
            """);
        Assert.True(Assert.Single(program.Functions, static function => function.Name == "Danger")
            .RequiresUnsafeCaller);
        Assert.False(Assert.Single(program.Functions, static function => function.Name == "Wrapper")
            .RequiresUnsafeCaller);
    }

    [Fact]
    public void UnsafeClassConstructorsRequireAuthorizationThroughTheirFactories()
    {
        const string declaration = """
            class Leaf {
                public int32 Value;
                public unsafe Leaf(int32 value) { Value = value; }
            }
            """;

        var safeCall = Compilation.Create(SyntaxTree.Parse(declaration
            + " int32 Main() { require Leaf.TryCreate(42, out var leaf); return leaf.Value; }"));
        Assert.Null(safeCall.Program);
        Assert.Contains(safeCall.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var authorizedCall = Compilation.Create(SyntaxTree.Parse(declaration
            + " int32 Main() { unsafe { require Leaf.TryCreate(42, out var leaf); return leaf.Value; } }"));
        Assert.False(authorizedCall.HasErrors, string.Join(Environment.NewLine, authorizedCall.Diagnostics));
    }

    [Fact]
    public void UnsafeGetterRequiresAuthorizationAtThePropertyAccess()
    {
        const string source = """
            class Box {
                int32 _value;
                public Box(int32 value) { _value = value; }
                public int32 Value { unsafe get { return _value; } }
            }
            """;

        var safeAccess = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { require Box.TryCreate(42, out var box); return box.Value; }"));
        Assert.Null(safeAccess.Program);
        Assert.Contains(safeAccess.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var authorizedAccess = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { require Box.TryCreate(42, out var box); unsafe { return box.Value; } }"));
        Assert.False(authorizedAccess.HasErrors, string.Join(Environment.NewLine, authorizedAccess.Diagnostics));
    }

    [Fact]
    public void UnsafeInstanceAndStaticMethodsRequireAuthorizationWithoutTaintingSafeWrappers()
    {
        const string source = """
            class Box {
                public int32 Value;
                public Box(int32 value) { Value = value; }
                public unsafe int32 Read() { return Value; }
                public int32 SafeRead() { unsafe { return Read(); } }
                public static unsafe int32 Twice(int32 value) { return value * 2; }
            }
            """;

        var safeCalls = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { require Box.TryCreate(21, out var box); return box.Read() + Box.Twice(1); }"));
        Assert.Null(safeCalls.Program);
        Assert.Equal(2, safeCalls.Diagnostics.Count(static diagnostic => diagnostic.Code == "TRM3028"));

        var authorizedCalls = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { require Box.TryCreate(21, out var box);"
            + " unsafe { return box.Read() + Box.Twice(1); } }"));
        Assert.False(authorizedCalls.HasErrors, string.Join(Environment.NewLine, authorizedCalls.Diagnostics));

        var safeWrapper = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { require Box.TryCreate(21, out var box); return box.SafeRead(); }"));
        Assert.False(safeWrapper.HasErrors, string.Join(Environment.NewLine, safeWrapper.Diagnostics));
        Assert.False(Assert.Single(Assert.IsType<BoundProgram>(safeWrapper.Program).Functions,
            static function => function.Name == "Box.SafeRead").RequiresUnsafeCaller);
    }

    [Fact]
    public void UnsafeFieldsRequireAuthorizationForReadsAndWrites()
    {
        const string declaration = """
            struct Cell {
                public unsafe mut int32 Value;
                public Cell() { unsafe { Value = 1; } }
            }
            """;

        var safeRead = Compilation.CreateLibrary(SyntaxTree.Parse(declaration
            + " int32 Read() { Cell cell = new Cell(); return cell.Value; }"));
        Assert.Null(safeRead.Program);
        Assert.Contains(safeRead.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var authorizedRead = Compilation.CreateLibrary(SyntaxTree.Parse(declaration
            + " int32 Read() { Cell cell = new Cell(); return unsafe(cell.Value); }"));
        Assert.False(authorizedRead.HasErrors, string.Join(Environment.NewLine, authorizedRead.Diagnostics));

        var safeWrite = Compilation.Create(SyntaxTree.Parse(declaration
            + " int32 Main() { mut Cell cell = new Cell(); cell.Value = 42; return 0; }"));
        Assert.Null(safeWrite.Program);
        Assert.Contains(safeWrite.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var authorizedWrite = Compilation.Create(SyntaxTree.Parse(declaration
            + " int32 Main() { mut Cell cell = new Cell(); unsafe { cell.Value = 42; return cell.Value; } }"));
        Assert.False(authorizedWrite.HasErrors, string.Join(Environment.NewLine, authorizedWrite.Diagnostics));

        var unscopedInitialization = Compilation.CreateLibrary(SyntaxTree.Parse(
            "struct Cell { public unsafe int32 Value; public Cell() { Value = 1; } }"));
        Assert.Null(unscopedInitialization.Program);
        Assert.Contains(unscopedInitialization.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3028");

        var duplicateModifier = Compilation.CreateLibrary(SyntaxTree.Parse(
            "struct Cell { public unsafe unsafe int32 Value; public Cell() { unsafe { Value = 1; } } }"));
        Assert.Null(duplicateModifier.Program);
        Assert.Contains(duplicateModifier.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3001");
    }

    [Fact]
    public void UnsafeFieldObligationsApplyToEveryFieldInANestedPath()
    {
        const string source = """
            struct Inner {
                public int32 Value;
                public Inner() { Value = 42; }
            }
            struct Outer {
                public unsafe mut Inner Value;
                public Outer() { unsafe { Value = new Inner(); } }
            }
            """;

        var safeAccess = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { Outer outer = new Outer(); return outer.Value.Value; }"));
        Assert.Null(safeAccess.Program);
        Assert.Contains(safeAccess.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var authorizedAccess = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { mut Outer outer = new Outer(); unsafe { outer.Value = new Inner();"
            + " return outer.Value.Value; } }"));
        Assert.False(authorizedAccess.HasErrors, string.Join(Environment.NewLine, authorizedAccess.Diagnostics));

        var safeWrite = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { mut Outer outer = new Outer(); outer.Value = new Inner(); return 0; }"));
        Assert.Null(safeWrite.Program);
        Assert.Contains(safeWrite.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");
    }

    [Fact]
    public void UnsafeManagedClassFieldsRequireAuthorization()
    {
        const string source = """
            class Box {
                public unsafe int32 Value;
                public Box(int32 value) { unsafe { Value = value; } }
            }
            """;

        var safeAccess = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { require Box.TryCreate(42, out var box); return box.Value; }"));
        Assert.Null(safeAccess.Program);
        Assert.Contains(safeAccess.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var authorizedAccess = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { require Box.TryCreate(42, out var box); return unsafe(box.Value); }"));
        Assert.False(authorizedAccess.HasErrors, string.Join(Environment.NewLine, authorizedAccess.Diagnostics));
    }

    [Fact]
    public void GenericSpecializationPreservesCallerUnsafeRequirement()
    {
        const string declaration = """
            unsafe T Identity<T>(T value) where T : [Copy] { return value; }
            """;

        var safeCall = Compilation.Create(SyntaxTree.Parse(declaration
            + " int32 Main() { return Identity<int32>(42); }"));
        Assert.Null(safeCall.Program);
        Assert.Contains(safeCall.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var authorizedCall = Compilation.Create(SyntaxTree.Parse(declaration
            + " int32 Main() { return unsafe(Identity<int32>(42)); }"));
        Assert.False(authorizedCall.HasErrors, string.Join(Environment.NewLine, authorizedCall.Diagnostics));
    }

    [Fact]
    public void UnsafeStaticOperatorsRequireAuthorizationForBinaryUnaryAndUpdateSyntax()
    {
        const string declaration = """
            public struct Number {
                public int32 Value;
                public Number(int32 value) { Value = value; }
                public static unsafe Number operator +(Number left, Number right) {
                    return new Number(left.Value + right.Value);
                }
                public static unsafe Number operator +(Number value) {
                    return new Number(value.Value);
                }
                public static unsafe Number operator ++(Number value) {
                    return new Number(value.Value + 1);
                }
            }
            """;

        const string body = """
            int32 Main() {
                Number left = new Number(2);
                Number right = new Number(3);
                Number combined = left + right;
                Number unary = +left;
                mut Number updated = left;
                ++updated;
                return combined.Value + unary.Value + updated.Value;
            }
            """;

        var safeUse = Compilation.Create(SyntaxTree.Parse(declaration + body));
        Assert.Null(safeUse.Program);
        Assert.Equal(3, safeUse.Diagnostics.Count(static diagnostic => diagnostic.Code == "TRM3028"));

        var authorizedUse = Compilation.Create(SyntaxTree.Parse(declaration + """
            int32 Main() {
                Number left = new Number(2);
                Number right = new Number(3);
                unsafe {
                    Number combined = left + right;
                    Number unary = +left;
                    mut Number updated = left;
                    ++updated;
                    return combined.Value + unary.Value + updated.Value;
                }
            }
            """));
        Assert.False(authorizedUse.HasErrors, string.Join(Environment.NewLine, authorizedUse.Diagnostics));
    }

    [Fact]
    public void SemanticModulesRoundTripTopLevelCallerUnsafeContracts()
    {
        var providerProgram = CompileLibrary("public unsafe int32 Danger() { return 42; }");
        var producer = ModuleCompiler.CreateProducer(providerProgram, ["Danger"]);
        var reference = ModuleCompiler.CreateReference(producer);

        var safeCall = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Danger(); }"), reference);
        Assert.Null(safeCall.Program);
        Assert.Contains(safeCall.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var scopedCall = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { unsafe { return Danger(); } }"), reference);
        Assert.False(scopedCall.HasErrors, string.Join(Environment.NewLine, scopedCall.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(scopedCall.Program), producer);
        foreach (var unsupportedVersion in new[] { 54, 57 })
        {
            var unsupportedConsumer = (byte[])consumer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(unsupportedConsumer.AsSpan(4, sizeof(int)),
                unsupportedVersion);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(unsupportedConsumer, producer));
        }
        var linked = ModuleCompiler.Link(consumer, producer);
        SemanticVerifier.Verify(linked);
        Assert.Equal(42, NativeJit.Execute(linked, NativeRuntimePolicy.FailFast));
        var nativeProducer = NativeModuleCompiler.CreateProducer(providerProgram, ["Danger"],
            "UnsafeProvider", NativeRuntimePolicy.FailFast);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(scopedCall.Program), nativeProducer, NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.Validate(nativeConsumer, nativeProducer);

        var expressionScopedCall = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return unsafe(Danger()); }"), reference);
        Assert.False(expressionScopedCall.HasErrors,
            string.Join(Environment.NewLine, expressionScopedCall.Diagnostics));
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(expressionScopedCall.Program), producer));

        var safeProducer = ModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Danger() { return 42; }"), ["Danger"]);
        var safeReference = ModuleCompiler.CreateReference(safeProducer);
        var safeConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Danger(); }"), safeReference);
        Assert.False(safeConsumerCompilation.HasErrors,
            string.Join(Environment.NewLine, safeConsumerCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(safeConsumerCompilation.Program), safeProducer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(safeConsumer, safeProducer),
            NativeRuntimePolicy.FailFast));
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, safeProducer),
            NativeRuntimePolicy.FailFast));

        var changedUnsafeProducer = ModuleCompiler.CreateProducer(
            CompileLibrary("public unsafe int32 Danger() { return 7; }"), ["Danger"]);
        Assert.Equal(7, NativeJit.Execute(ModuleCompiler.Link(consumer, changedUnsafeProducer),
            NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void SemanticModuleReadersReconstructCallerUnsafeAuthorizationFromUnsafeBlocks()
    {
        const string source = "int32 Main() { unsafe { return Danger(); } }";
        var producer = ModuleCompiler.CreateProducer(
            CompileLibrary("public unsafe int32 Danger() { return 42; }"), ["Danger"]);
        var compilation = Compilation.Create(SyntaxTree.Parse(source), ModuleCompiler.CreateReference(producer));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var main = program.Functions[program.EntryPoint];
        var unsafeBlock = Assert.IsType<BoundUnsafe>(Assert.Single(main.Body.Statements));

        var forgedMain = main with {
            Body = main.Body with { Statements = [unsafeBlock.Body] },
        };
        var forgedProgram = program with {
            Functions = program.Functions.SetItem(program.EntryPoint, forgedMain),
        };
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateConsumer(forgedProgram, producer));

        var consumer = ModuleCompiler.CreateConsumer(program, producer);
        const byte BoundUnsafeStatementTag = 11;
        var unsafeScope = new byte[1 + (2 * sizeof(int))];
        unsafeScope[0] = BoundUnsafeStatementTag;
        BinaryPrimitives.WriteInt32LittleEndian(unsafeScope.AsSpan(1), unsafeBlock.Span.Start);
        BinaryPrimitives.WriteInt32LittleEndian(unsafeScope.AsSpan(1 + sizeof(int)), unsafeBlock.Span.Length);
        var unsafeScopeOffset = consumer.AsSpan().IndexOf(unsafeScope);
        Assert.True(unsafeScopeOffset >= 0);
        Assert.Equal(unsafeScopeOffset, consumer.AsSpan().LastIndexOf(unsafeScope));

        var forgedConsumer = new byte[consumer.Length - unsafeScope.Length];
        consumer.AsSpan(0, unsafeScopeOffset).CopyTo(forgedConsumer);
        consumer.AsSpan(unsafeScopeOffset + unsafeScope.Length)
            .CopyTo(forgedConsumer.AsSpan(unsafeScopeOffset));
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(forgedConsumer, producer));
    }

    [Fact]
    public async Task SemanticModuleGraphPersistsUnsafeImportsWithinUnsafeBlocksAsync()
    {
        var policy = NativeRuntimePolicy.FailFast;
        var leaf = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public unsafe int32 Danger() { return 42; }"), ["Danger"], "UnsafeLeaf", policy);
        var middleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 SafeWrapper() { unsafe { return Danger(); } }"),
            NativeModuleCompiler.CreateGraphReference([leaf]), (BoundRuntimePolicy)policy);
        Assert.False(middleCompilation.HasErrors,
            string.Join(Environment.NewLine, middleCompilation.Diagnostics));
        var middle = NativeModuleCompiler.CreateGraphProducer(
            Assert.IsType<BoundProgram>(middleCompilation.Program), ["SafeWrapper"], "SafeMiddle", [leaf], policy);

        var rootCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return SafeWrapper(); }"),
            NativeModuleCompiler.CreateGraphReference([middle]), (BoundRuntimePolicy)policy);
        Assert.False(rootCompilation.HasErrors, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(rootCompilation.Program), [middle], policy);
        NativeModuleCompiler.ValidateGraph(root, [middle, leaf]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [leaf, middle],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
    }

    [Fact]
    public async Task SemanticModuleSetsPersistCallerUnsafeImportsWithinUnsafeBlocksAsync()
    {
        var policy = NativeRuntimePolicy.FailFast;
        var provider = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public unsafe int32 Danger() { return 42; }"),
            ["Danger"], "UnsafeSetProvider", policy);
        var unusedProvider = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }"),
            ["Helper"], "UnusedSetProvider", policy);
        byte[][] providers = [provider, unusedProvider];
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { unsafe { return Danger(); } }"),
            NativeModuleCompiler.CreateReferenceSet(providers), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));

        var consumer = NativeModuleCompiler.CreateConsumerSet(
            Assert.IsType<BoundProgram>(compilation.Program), providers, policy);
        NativeModuleCompiler.ValidateConsumerSet(consumer, providers);
        Assert.Contains("UnsafeSetProvider", NativeModuleCompiler.InspectConsumerSet(consumer).Dependencies);
        Assert.Equal(42, await NativeModuleCompiler.RunConsumerSetAsync(consumer, providers,
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
    }

    [Fact]
    public void GraphReadersRejectCallerUnsafeMemberContracts()
    {
        var policy = NativeRuntimePolicy.FailFast;
        var dependency = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }"), ["Helper"], "SafeDependency", policy);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public struct Box {
                public int32 Value;
                public static int32 Danger() { return 42; }
            }
            """), NativeModuleCompiler.CreateGraphReference([dependency]), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var graph = NativeModuleCompiler.CreateGraphProducer(
            Assert.IsType<BoundProgram>(compilation.Program), ["Box.Danger"], "GraphProvider", [dependency], policy);
        var forged = ForgeCallerUnsafeMemberGraphPackage(graph, "Box.Danger");

        Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.CreateGraphReference([forged]));
        Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.InspectGraphArtifact(forged));
    }

    [Fact]
    public void TrRmPreservesCallerUnsafeObligationsAndRejectsCalls()
    {
        var declaration = CompileLibrary(
            "[NoThrow] public unsafe int32 Danger() { return 42; }"
            + " [DoesNotReturn] public unsafe void Never() { while (true) { } }");
        var loadedUnsafe = TrRmExecutableCompiler.Load(
            TrRmExecutableCompiler.Emit(declaration, "unsafe-contract"));
        var unsafeFunction = Assert.Single(loadedUnsafe.Functions,
            static function => function.Name == "Danger");
        Assert.True(unsafeFunction.IsNoThrow);
        Assert.False(unsafeFunction.DoesNotReturn);
        Assert.True(unsafeFunction.RequiresUnsafeCaller);
        var nonReturningFunction = Assert.Single(loadedUnsafe.Functions,
            static function => function.Name == "Never");
        Assert.False(nonReturningFunction.IsNoThrow);
        Assert.True(nonReturningFunction.DoesNotReturn);
        Assert.True(nonReturningFunction.RequiresUnsafeCaller);

        var safeDeclaration = CompileLibrary("[NoThrow] public int32 Danger() { return 42; }");
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(safeDeclaration, "safe-contract"));
        var function = Assert.Single(loaded.Functions);
        Assert.True(function.IsNoThrow);
        Assert.False(function.RequiresUnsafeCaller);

        var caller = Compilation.Create(SyntaxTree.Parse(
            "unsafe int32 Danger() { return 42; } int32 Main() { return unsafe(Danger()); }"));
        Assert.False(caller.HasErrors, string.Join(Environment.NewLine, caller.Diagnostics));
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(
            Assert.IsType<BoundProgram>(caller.Program), "unsafe-call"));
    }

    [Fact]
    public void PersistedWritersRejectUnsafeFieldContracts()
    {
        var program = CompileLibrary("""
            public struct Cell {
                public unsafe int32 Value;
                public Cell() { unsafe { Value = 42; } }
            }
            public int32 Read() { Cell cell = new Cell(); return unsafe(cell.Value); }
            """);

        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(program, ["Read"]));
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(program, "unsafe-field"));

        var classProgram = CompileLibrary("""
            public class Box {
                public unsafe int32 Value;
                public Box(int32 value) { unsafe { Value = value; } }
            }
            public int32 Read() {
                require Box.TryCreate(42, out var box);
                return unsafe(box.Value);
            }
            """);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(classProgram, ["Read"]));
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(classProgram, "unsafe-class-field"));
    }

    private static byte[] ForgeCallerUnsafeMemberGraphPackage(byte[] package, string functionName)
    {
        var contractLength = BinaryPrimitives.ReadInt32LittleEndian(package.AsSpan(11, 4));
        var imageLength = BinaryPrimitives.ReadInt32LittleEndian(package.AsSpan(15, 4));
        var contractStart = package.Length - 32 - imageLength - contractLength;
        var contract = package.AsSpan(contractStart, contractLength).ToArray();
        var name = Encoding.UTF8.GetBytes(functionName);
        byte[] contractMarker = [.. name, (byte)'|'];
        var contractTextStart = contract.AsSpan().IndexOf(contractMarker);
        Assert.True(contractTextStart >= sizeof(int));
        var contractLengthOffset = contractTextStart - sizeof(int);
        var contractTextLength = BinaryPrimitives.ReadInt32LittleEndian(
            contract.AsSpan(contractLengthOffset, sizeof(int)));
        var contractText = Encoding.UTF8.GetString(contract, contractTextStart, contractTextLength);
        var unsafeContractText = Encoding.UTF8.GetBytes(contractText + "|CallerUnsafe");
        var growth = unsafeContractText.Length - contractTextLength;
        var bodyNameStart = contract.AsSpan().LastIndexOf(name);
        Assert.True(bodyNameStart > contractTextStart);
        var forgedContract = new byte[contract.Length + growth];
        contract.AsSpan(0, contractLengthOffset).CopyTo(forgedContract);
        BinaryPrimitives.WriteInt32LittleEndian(forgedContract.AsSpan(contractLengthOffset, sizeof(int)),
            unsafeContractText.Length);
        unsafeContractText.CopyTo(forgedContract, contractLengthOffset + sizeof(int));
        contract.AsSpan(contractLengthOffset + sizeof(int) + contractTextLength)
            .CopyTo(forgedContract.AsSpan(contractLengthOffset + sizeof(int) + unsafeContractText.Length));
        var requiresUnsafeCallerOffset = bodyNameStart + growth + name.Length + sizeof(int) + 6;
        forgedContract[requiresUnsafeCallerOffset] = 1;

        var forgedPackage = new byte[package.Length + growth];
        package.AsSpan(0, contractStart).CopyTo(forgedPackage);
        BinaryPrimitives.WriteInt32LittleEndian(forgedPackage.AsSpan(11, sizeof(int)),
            forgedContract.Length);
        forgedContract.CopyTo(forgedPackage, contractStart);
        package.AsSpan(contractStart + contractLength, imageLength)
            .CopyTo(forgedPackage.AsSpan(contractStart + forgedContract.Length));
        SHA256.HashData(forgedPackage.AsSpan(0, forgedPackage.Length - 32))
            .CopyTo(forgedPackage, forgedPackage.Length - 32);
        return forgedPackage;
    }
}
