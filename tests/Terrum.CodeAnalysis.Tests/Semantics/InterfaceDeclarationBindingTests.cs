// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class InterfaceDeclarationBindingTests
{
    [Test]
    public void BindsGenericFormalsDefaultsAndBaseInstances()
    {
        const string source = """
            public interface IBase<T> { }
            public interface IValue<this T, const int32 Size = 2, U = int32>
                : IBase<T>  { }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }

    [Test]
    public void BindsExistentialInterfaceTypeArgumentSlots()
    {
        const string source = """
            public interface IBox<T, U> { }
            public interface IView {
                IBox<int32, _> HideSecond();
                IBox<_, int32> HideFirst();
                IBox<_, _> HideBoth();
                IBox<IBox<_, int32>, _> HideNestedSlots();
            }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var interfaces = compilation.Program!.Interfaces;
        var view = interfaces.Single(static item =>
            item.QualifiedName == "IView");
        BoundInterfaceType ResultType(string name)
        {
            return view.Members.Single(member => member.Name == name)
                .ResultType!;
        }

        Assert.That(ResultType("HideSecond").TypeArguments[0].Kind,
            Is.EqualTo(BoundInterfaceTypeArgumentKind.Type));
        Assert.That(ResultType("HideSecond").TypeArguments[1].Kind,
            Is.EqualTo(BoundInterfaceTypeArgumentKind.ExistentialType));
        Assert.That(ResultType("HideFirst").TypeArguments[0].Kind,
            Is.EqualTo(BoundInterfaceTypeArgumentKind.ExistentialType));
        Assert.That(ResultType("HideBoth").TypeArguments.Select(
            static argument => argument.Kind),
            Is.All.EqualTo(BoundInterfaceTypeArgumentKind.ExistentialType));
        Assert.That(BoundInterfaceContract.AreEquivalent(
            ResultType("HideSecond"), ResultType("HideFirst")), Is.False);
        Assert.That(ResultType("HideNestedSlots").TypeArguments[0].Type!
            .TypeArguments[0].Kind,
            Is.EqualTo(BoundInterfaceTypeArgumentKind.ExistentialType));
        Assert.That(ResultType("HideNestedSlots").TypeArguments[1].Kind,
            Is.EqualTo(BoundInterfaceTypeArgumentKind.ExistentialType));

        var rewritten = interfaces.Select(@interface =>
            BoundTreeTraversal.RewriteTypes(@interface,
                static type => type == PrimitiveType.Int32
                    ? PrimitiveType.Int64 : type)).ToArray();
        var rewrittenView = rewritten.Single(static item =>
            item.QualifiedName == "IView");
        Assert.That(rewrittenView.Members.Single(static member =>
                member.Name == "HideSecond").ResultType!
                .TypeArguments[1].Kind,
            Is.EqualTo(BoundInterfaceTypeArgumentKind.ExistentialType));
        Assert.That(BoundInterfaceContract.IsValidTable([.. rewritten],
            static _ => true), Is.True);
    }

    [Test]
    public void RejectsExistentialInterfaceArgumentForConstantFormal()
    {
        const string source = """
            public interface ICount<const int32 Size> { }
            public interface IView { ICount<_> Read(); }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True);
    }

    [Test]
    public void ParsesExplicitAssociatedTypeAndConstantBindings()
    {
        const string source = """
            internal interface I<T> {
                type Item;
                const int32 Count;
            }
            internal struct S : I<int32> {
                type I<int32>.Item = uint8;
                const int32 I<int32>.Count = 3;
                int32 Value;
            }
            int32 Main() => 0;
            """;

        var tree = SyntaxTree.Parse(source);

        Assert.That(tree.HasErrors, Is.False,
            string.Join(Environment.NewLine, tree.Diagnostics));
        var structure = tree.Root.DescendantNodes().Single(static node =>
            node.Kind == SyntaxKind.StructDeclaration);
        Assert.That(structure.Children.OfType<SyntaxNode>()
            .Select(static member => member.Kind)
            .Where(static kind => kind is SyntaxKind.AssociatedTypeBinding
                or SyntaxKind.AssociatedConstantBinding),
            Is.EqualTo([
                SyntaxKind.AssociatedTypeBinding,
                SyntaxKind.AssociatedConstantBinding,
            ]));
    }

    [Test]
    public void ParsesQualifiedInterfaceWitnessSelection()
    {
        var tree = SyntaxTree.Parse("""
            internal interface I<T> { int32 Read(T value); }
            internal struct S : I<S> {
                int32 Value;
                public int32 Read(S value) { return Value; }
            }
            int32 ReadVia(S value) {
                return value.via<I<S>>.Read(value);
            }
            int32 Main() { return 0; }
            """);

        Assert.That(tree.HasErrors, Is.False,
            string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.DescendantNodes().Any(static node =>
            node.Kind
                == SyntaxKind.InterfaceWitnessProjectionExpression), Is.True);
    }

    [Test]
    public void QualifiedInterfaceCallsSelectExactExplicitWitness()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<T> { int32 Read(T value); }
            internal interface J<T> { int32 Read(T value); }
            internal struct S : I<S>, J<S> {
                int32 Value;
                public S(int32 value) { Value = value; }
                int32 I<S>.Read(S value) { return Value; }
                int32 J<S>.Read(S value) { return value.Value + 1; }
            }
            int32 Main() {
                S value = new S(40);
                return value.via<I<S>>.Read(value)
                    + value.via<J<S>>.Read(value);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var calls = new System.Collections.Generic.List<BoundCall>();
        var main = program.Functions.Single(static function =>
            function.Name == "Main");
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression => {
                if (expression is BoundCall {
                    InterfaceCallIdentity: not null,
                } call)
                {
                    calls.Add(call);
                }
            }), Is.True);
        Assert.That(calls.Select(static call =>
                call.InterfaceCallIdentity!.RequestedInterface.NominalName)
                .ToArray(),
            Is.EquivalentTo(["I", "J"]));
        Assert.That(calls.All(static call =>
            !call.InterfaceCallIdentity!.IsConstraintCall), Is.True);
        var mainIndex = program.Functions.IndexOf(main);
        var otherRequirement = calls.Single(static call =>
            call.InterfaceCallIdentity!.RequestedInterface.NominalName == "J")
            .InterfaceCallIdentity!.Requirement;
        var invalidMain = BoundTreeTraversal.RewriteExpressions(main,
            expression => expression is BoundCall {
                InterfaceCallIdentity: BoundInterfaceCallIdentity identity,
            } call && identity.RequestedInterface.NominalName == "I"
                ? call with {
                    InterfaceCallIdentity = identity with {
                        Requirement = otherRequirement,
                    },
                }
                : expression);
        Assert.That(SemanticVerifier.TryVerifySource(program with {
            Functions = program.Functions.SetItem(mainIndex, invalidMain),
        }, out _, out _, out _, out _), Is.False);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast),
            Is.EqualTo(81));
    }

    [Test]
    public void QualifiedInterfaceCallRequiresDeclaredWitness()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I { int32 Read(); }
            internal struct S {
                public int32 Read() { return 42; }
            }
            int32 Main() {
                S value = new S();
                return value.via<I>.Read();
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void QualifiedInterfacePropertyReadsUseGetterWitness()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I { int32 Value { get; } }
            internal struct S : I {
                int32 Field;
                public S(int32 value) { Field = value; }
                public int32 Value { get { return Field; } }
            }
            int32 Main() {
                S value = new S(42);
                return value.via<I>.Value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var main = program.Functions.Single(static function =>
            function.Name == "Main");
        var calls = new System.Collections.Generic.List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression => {
                if (expression is BoundCall {
                    InterfaceCallIdentity: not null,
                } call)
                {
                    calls.Add(call);
                }
            }), Is.True);
        Assert.That(calls, Has.Count.EqualTo(1));
        Assert.That(calls[0].InterfaceCallIdentity!.Requirement
            .AccessorOrdinal, Is.EqualTo(0));
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast),
            Is.EqualTo(42));
    }

    [Test]
    public void QualifiedInterfaceCallSelectsInheritedRequirementIdentity()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface IBase<T> { int32 Read(T value); }
            internal interface IDerived<T> : IBase<T> { }
            internal struct S : IDerived<S> {
                int32 Value;
                public S(int32 value) { Value = value; }
                public int32 Read(S value) { return Value; }
            }
            int32 Main() {
                S value = new S(42);
                return value.via<IBase<S>>.Read(value);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var main = program.Functions.Single(static function =>
            function.Name == "Main");
        var calls = new System.Collections.Generic.List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression => {
                if (expression is BoundCall {
                    InterfaceCallIdentity: not null,
                } call)
                {
                    calls.Add(call);
                }
            }), Is.True);
        Assert.That(calls, Has.Count.EqualTo(1));
        Assert.That(calls[0].InterfaceCallIdentity!.WitnessInterface
            .NominalName, Is.EqualTo("IDerived"));
        Assert.That(calls[0].InterfaceCallIdentity!.RequestedInterface
            .NominalName, Is.EqualTo("IBase"));
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast),
            Is.EqualTo(42));
    }

    [Test]
    public void QualifiedInterfaceCallRejectsUnloweredDefaultBody()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<T> {
                int32 Read(T value) { return 42; }
            }
            internal struct S : I<S> { int32 Field; }
            int32 Main() {
                S value = new S();
                return value.via<I<S>>.Read(value);
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3011"
            && diagnostic.Message.Contains(
                "directly callable source implementation",
                StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void GenericInterfaceCallUsesConstraintProofAndClosedWitness()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<T> { int32 Read(T value); }
            internal struct S : I<S> {
                int32 Value;
                public S(int32 value) { Value = value; }
                public int32 Read(S value) { return Value; }
            }
            int32 ReadVia<T>(T value) where T : I<T> {
                return value.via<I<T>>.Read(value);
            }
            int32 Main() { return ReadVia<S>(new S(42)); }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var genericDefinition = program.GenericMethodDefinitions.Single();
        var proofProgram = genericDefinition.ProofProgram!;
        var proofFunction = proofProgram.Functions[
            genericDefinition.FunctionIndex];
        var proofCalls = new System.Collections.Generic.List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(proofFunction.Body,
            expression => {
                if (expression is BoundCall {
                    InterfaceCallIdentity: not null,
                } call)
                {
                    proofCalls.Add(call);
                }
            }), Is.True);
        Assert.That(proofCalls, Has.Count.EqualTo(1));
        Assert.That(proofCalls[0].InterfaceCallIdentity!.IsConstraintCall,
            Is.True);

        var specialization = program.Functions.Single(function =>
            function.Name.StartsWith("ReadVia<", StringComparison.Ordinal));
        var specializedCalls = new System.Collections.Generic.List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(specialization.Body,
            expression => {
                if (expression is BoundCall {
                    InterfaceCallIdentity: not null,
                } call)
                {
                    specializedCalls.Add(call);
                }
            }), Is.True);
        Assert.That(specializedCalls, Has.Count.EqualTo(1));
        Assert.That(specializedCalls[0]
            .InterfaceCallIdentity!.IsConstraintCall, Is.False);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast),
            Is.EqualTo(42));
    }

    [Test]
    public void BindsExactAssociatedTypeAndConstantWitnessValues()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<T> {
                type Item;
                const int32 Count;
            }
            internal struct S : I<int32> {
                type I<int32>.Item = uint8;
                const int32 I<int32>.Count = 3;
                int32 Value;
            }
            int32 Main() { return 42; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var witness = program.InterfaceWitnesses.Single();
        Assert.That(witness.AssociatedTypes, Has.Length.EqualTo(1));
        Assert.That(witness.AssociatedConstants, Has.Length.EqualTo(1));
        Assert.That(witness.AssociatedTypes[0].Type.PrimitiveType,
            Is.EqualTo(PrimitiveType.UInt8));
        Assert.That(witness.AssociatedConstants[0].Value.Type,
            Is.EqualTo(PrimitiveType.Int32));
        Assert.That(witness.AssociatedConstants[0].Value.Value,
            Is.EqualTo(3));
    }

    [Test]
    public void UnsupportedModuleProfilesRejectNominalWitnessFacts()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<T> { }
            internal struct S : I<int32> { int32 Value; }
            int32 Main() { return 42; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(program, ["Main"]));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, "witness.consumer",
                "trrm/1-draft-generic-method-calls-parameter-labels-1", 0));
    }

    [Test]
    public void PrivateModuleRejectsInterfaceDeclarationsWithoutCodec()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<T> {
                mut int32 Read(int32 value);
            }
            int32 Main() { return 42; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var exception = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(compilation.Program!, ["Main"]));
        Assert.That(exception!.Message,
            Does.Contain("does not preserve interface declarations"));
    }

    [Test]
    public void RejectsAssociatedConstantBindingWithWrongDomain()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I {
                const int32 Count;
            }
            internal struct S : I {
                const uint32 I.Count = 3;
                int32 Value;
            }
            int32 Main() { return 42; }
            """));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3007"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void RejectsMissingAssociatedConstantBinding()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I {
                const int32 Count;
            }
            internal struct S : I { int32 Value; }
            int32 Main() { return 42; }
            """));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3007"
            && diagnostic.Message.Contains(
                "requires exactly one explicit source binding",
                StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void BindsDefaultCallableWitnessOrigin()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<T> {
                int32 Read(T value) { return 7; }
            }
            internal struct S : I<S> { int32 Value; }
            int32 Main() { return 42; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var witness = compilation.Program!.InterfaceWitnesses.Single();
        Assert.That(witness.Members, Has.Length.EqualTo(1));
        Assert.That(witness.Members[0].TargetKind,
            Is.EqualTo(BoundInterfaceWitnessTargetKind.Default));
        Assert.That(witness.Members[0].FunctionIndex, Is.EqualTo(-1));
        Assert.That(witness.Members[0].DefaultTarget,
            Is.EqualTo(witness.Members[0].Requirement));
    }

    [Test]
    public void BindsGenericCallableWitnessByFormalOrdinal()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                U Read<U>(U expectedValue);
            }
            internal struct S : I<S> {
                int32 Storage;
                public V Read<V>(V actualValue) { return actualValue; }
            }
            int32 Invoke(S value) {
                return value.via<I<S>>.Read<int32>(
                    expectedValue: 42);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var witness = program.InterfaceWitnesses.Single();
        var binding = witness.Members.Single();
        Assert.That(binding.GenericMethodDefinitionOrdinal,
            Is.GreaterThanOrEqualTo(0));
        Assert.That(binding.FunctionIndex, Is.EqualTo(-1));
        var call = (BoundCall)((BoundReturn)program.Functions.Single(
            static function => function.Name == "Invoke")
            .Body.Statements.Single()).Value!;
        Assert.That(call.InterfaceCallIdentity!.Requirement.DeclarationKey,
            Is.EqualTo(binding.Requirement.DeclarationKey));
        Assert.That(call.GenericMethodCall!.DefinitionOrdinal,
            Is.EqualTo(binding.GenericMethodDefinitionOrdinal));
        SemanticVerifier.VerifyLibrary(program);
    }

    [Test]
    public void BindsInterfaceWitnessNamedArgumentsUsingRequirementLabels()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                int32 Read(int32 expectedValue);
            }
            internal struct S : I<S> {
                int32 Field;
                public int32 Read(int32 actualValue) { return actualValue; }
            }
            int32 Invoke(S value) {
                return value.via<I<S>>.Read(expectedValue: 42);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        SemanticVerifier.VerifyLibrary(compilation.Program!);
    }

    [Test]
    public void BindsMutableInterfaceRequirementToReadonlyGenericImplementation()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<T> {
                mut U Read<U>(U expectedValue);
            }
            internal struct S : I<S> {
                int32 Field;
                public S(int32 field) { Field = field; }
                public V Read<V>(V actualValue) { return actualValue; }
            }
            int32 Invoke(S value) {
                mut S writable = value;
                return writable.via<I<S>>.Read<int32>(
                    expectedValue: 42);
            }
            int32 Main() { return Invoke(new S(0)); }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var callerIndex = program.Functions.IndexOf(
            program.Functions.Single(static function =>
                function.Name == "Invoke"));
        var caller = program.Functions[callerIndex];
        var calls = new System.Collections.Generic.List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body,
            expression => {
                if (expression is BoundCall {
                    InterfaceCallIdentity: not null,
                } boundCall)
                {
                    calls.Add(boundCall);
                }
            }), Is.True);
        var call = calls.Single();
        Assert.That(call.Arguments[0], Is.TypeOf<BoundRead>(),
            "The readonly implementation keeps its own receiver ABI.");
        var target = program.Functions[call.Function];
        Assert.That(target.Variables[0].IsMutable, Is.False,
            "The selected readonly implementation retains its own receiver contract.");
        SemanticVerifier.Verify(program);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast),
            Is.EqualTo(42));

        var forgedCaller = BoundTreeTraversal.RewriteExpressions(caller,
            expression => expression is BoundCall {
                InterfaceCallIdentity: not null,
            } forgedCall
                ? forgedCall with {
                    Arguments = forgedCall.Arguments.SetItem(0,
                        new BoundMutableReceiverLoan(
                            ((BoundRead)call.Arguments[0]).Variable,
                            call.Arguments[0].Type,
                            call.Arguments[0].Span)),
                }
                : expression);
        _ = Assert.Throws<ArgumentException>(() =>
            SemanticVerifier.Verify(program with {
                Functions = program.Functions.SetItem(callerIndex,
                    forgedCaller),
            }));
    }

    [Test]
    public void BindsMutableClassInterfaceRequirementThroughWritableHandle()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<T> {
                mut int32 Read(int32 value);
            }
            internal mut class C : I<C> {
                public mut int32 Value;
                public C(int32 value) { Value = value; }
                public int32 Read(int32 value) { return Value + value; }
            }
            int32 Main() {
                require C.TryCreate(1, out var writable);
                return writable.via<I<C>>.Read(41);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        SemanticVerifier.Verify(compilation.Program!);
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void BindsMutableClassImplementationThroughInterfaceWitness()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<T> {
                mut int32 Add(int32 value);
            }
            internal mut class C : I<C> {
                public mut int32 Value;
                public C(int32 value) { Value = value; }
                public mut int32 Add(int32 value) {
                    Value = Value + value;
                    return Value;
                }
            }
            int32 Main() {
                require C.TryCreate(40, out var writable);
                return writable.via<I<C>>.Add(2);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var main = program.Functions.Single(static function =>
            function.Name == "Main");
        BoundCall? interfaceCall = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundCall {
                        InterfaceCallIdentity: not null,
                    } call)
                {
                    interfaceCall = call;
                }
            }), Is.True);
        Assert.That(interfaceCall, Is.Not.Null);
        Assert.That(interfaceCall!.Arguments[0],
            Is.TypeOf<BoundMutableReceiverLoan>());
        SemanticVerifier.Verify(program);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast),
            Is.EqualTo(42));
    }

    [Test]
    public void RejectsReadonlyClassHandleForMutableInterfaceRequirement()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                mut int32 Read(int32 value);
            }
            internal mut class C : I<C> {
                public mut int32 Value;
                public C(int32 value) { Value = value; }
                public int32 Read(int32 value) { return Value + value; }
            }
            int32 Invoke(C value) {
                mut C localBinding = value;
                return localBinding.via<I<C>>.Read(41);
            }
            """));

        Assert.That(compilation.HasErrors, Is.True,
            "A mutable local handle binding cannot upgrade readonly object access.");
    }

    [Test]
    public void BindsMutableInterfaceRequirementThroughMutableStructField()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<T> {
                mut int32 Read(int32 value);
            }
            internal struct S : I<S> {
                int32 Field;
                public S(int32 field) { Field = field; }
                public int32 Read(int32 value) { return value; }
            }
            internal struct Holder {
                public mut S Child;
                public Holder(S child) { Child = child; }
            }
            int32 Main() {
                mut Holder holder = new Holder(new S(0));
                return holder.Child.via<I<S>>.Read(42);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        SemanticVerifier.Verify(compilation.Program!);
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void RejectsReadonlyStructFieldPlaceForMutableInterfaceRequirement()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                mut int32 Read(int32 value);
            }
            internal struct S : I<S> {
                int32 Field;
                public int32 Read(int32 value) { return value; }
            }
            internal struct Holder {
                public mut S Child;
                public Holder(S child) { Child = child; }
            }
            int32 Invoke(Holder holder) {
                return holder.Child.via<I<S>>.Read(42);
            }
            """));

        Assert.That(compilation.HasErrors, Is.True,
            "A mutable field does not grant writable access through a readonly containing place.");
    }

    [Test]
    public void GenericProofCallsMutableInterfaceRequirementThroughWritableFormal()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<T> {
                mut int32 Read(int32 value);
            }
            internal mut class C : I<write C> {
                public mut int32 Value;
                public C(int32 value) { Value = value; }
                public int32 Read(int32 value) { return Value + value; }
            }
            int32 ReadVia<T>(T value) where T : write, I<T> {
                return value.via<I<T>>.Read(42);
            }
            int32 Main() {
                require C.TryCreate(0, out var value);
                return ReadVia<write C>(value);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var genericDefinition = program.GenericMethodDefinitions.Single(
            static definition => definition.Name == "ReadVia");
        var proofProgram = genericDefinition.ProofProgram!;
        var proofFunction = proofProgram.Functions[
            genericDefinition.FunctionIndex];
        var proofCalls = new System.Collections.Generic.List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(proofFunction.Body,
            expression => {
                if (expression is BoundCall {
                    InterfaceCallIdentity: not null,
                } boundCall)
                {
                    proofCalls.Add(boundCall);
                }
            }), Is.True);
        var call = proofCalls.Single();
        Assert.That(call.Arguments[0],
            Is.TypeOf<BoundMutableReceiverLoan>());
        SemanticVerifier.Verify(program);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast),
            Is.EqualTo(42));
    }

    [Test]
    public void GenericProofCannotInferWritableReceiverFromMutableLocal()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                mut int32 Read(int32 value);
            }
            internal mut struct S : I<S> {
                public int32 Read(int32 value) { return value; }
            }
            int32 ReadVia<T>(T value) where T : I<T> {
                mut T writable = value;
                return writable.via<I<T>>.Read(42);
            }
            """));

        Assert.That(compilation.HasErrors, Is.True,
            "A generic receiver needs a declared writable type-formal contract.");
    }

    [Test]
    public void GenericProofCallsMutableInterfaceGetterThroughWritableFormal()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<T> {
                int32 Value { mut get; }
            }
            internal mut class C : I<write C> {
                public mut int32 Storage;
                public C(int32 value) { Storage = value; }
                public int32 Value { get { return Storage; } }
            }
            int32 Read<T>(T value) where T : write, I<T> {
                return value.via<I<T>>.Value;
            }
            int32 Main() {
                require C.TryCreate(42, out var value);
                return Read<write C>(value);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var genericDefinition = program.GenericMethodDefinitions.Single(
            static definition => definition.Name == "Read");
        var proofProgram = genericDefinition.ProofProgram!;
        var proofFunction = proofProgram.Functions[
            genericDefinition.FunctionIndex];
        var proofCalls = new System.Collections.Generic.List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(proofFunction.Body,
            expression => {
                if (expression is BoundCall {
                    InterfaceCallIdentity: not null,
                } boundCall)
                {
                    proofCalls.Add(boundCall);
                }
            }), Is.True);
        var call = proofCalls.Single();
        Assert.That(call.InterfaceCallIdentity!.Requirement.AccessorOrdinal,
            Is.EqualTo(0));
        Assert.That(call.Arguments[0],
            Is.TypeOf<BoundMutableReceiverLoan>());
        SemanticVerifier.Verify(program);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast),
            Is.EqualTo(42));
    }

    [Test]
    public void GenericProofCallsInterfaceIndexerWithCommaSeparatedArguments()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                int32 this[T row, int32 column] { get; }
            }
            int32 Read<T>(T value, T row, int32 column) where T : I<T> {
                return value.via<I<T>>[row, column];
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var genericDefinition = program.GenericMethodDefinitions.Single(
            static definition => definition.Name == "Read");
        var proofFunction = genericDefinition.ProofProgram!.Functions[
            genericDefinition.FunctionIndex];
        var proofCalls = new System.Collections.Generic.List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(proofFunction.Body,
            expression => {
                if (expression is BoundCall {
                    InterfaceCallIdentity: not null,
                } boundCall)
                {
                    proofCalls.Add(boundCall);
                }
            }), Is.True);

        var call = proofCalls.Single();
        Assert.That(call.Arguments, Has.Length.EqualTo(3));
        Assert.That(call.InterfaceCallIdentity!.Requirement.AccessorOrdinal,
            Is.EqualTo(0));
        SemanticVerifier.VerifyLibrary(program);
    }

    [Test]
    public void BindsAndCallsMultiArgumentInterfaceIndexerGetter()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface IMatrix<T> {
                int32 this[int32 row, int32 column] { get; }
            }
            internal struct Matrix : IMatrix<Matrix> {
                int32 Storage;
                public int32 this[int32 row, int32 column] {
                    get { return row + column; }
                }
            }
            int32 Read(Matrix matrix, int32 row, int32 column) {
                return matrix.via<IMatrix<Matrix>>[row, column];
            }
            int32 ReadDirect(Matrix matrix, int32 row, int32 column) {
                return matrix[row, column];
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        SemanticVerifier.VerifyLibrary(program);
        var read = program.Functions.Single(
            static function => function.Name == "Read");
        var calls = new System.Collections.Generic.List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(read.Body,
            expression => {
                if (expression is BoundCall {
                    InterfaceCallIdentity: not null,
                } boundCall)
                {
                    calls.Add(boundCall);
                }
            }), Is.True);
        Assert.That(calls.Single().Arguments, Has.Length.EqualTo(3));

        var directRead = program.Functions.Single(
            static function => function.Name == "ReadDirect");
        var directCalls = new System.Collections.Generic.List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(directRead.Body,
            expression => {
                if (expression is BoundCall {
                    InterfaceCallIdentity: null,
                } boundCall)
                {
                    directCalls.Add(boundCall);
                }
            }), Is.True);
        Assert.That(directCalls.Single().Arguments,
            Has.Length.EqualTo(3));
    }

    [Test]
    public void BindsExplicitInterfaceIndexerGetterAndSetter()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface IMatrix<T> {
                int32 this[int32 row, int32 column] { get; mut set; }
            }
            internal struct Matrix : IMatrix<Matrix> {
                mut int32 stored;
                int32 IMatrix<Matrix>.this[int32 row, int32 column] {
                    get { return row + column; }
                    mut set { stored = value + row + column; }
                }
            }
            int32 Read(Matrix matrix, int32 row, int32 column) {
                return matrix.via<IMatrix<Matrix>>[row, column];
            }
            int32 Update(Matrix matrix, int32 row, int32 column,
                int32 value) {
                mut Matrix writable = matrix;
                writable.via<IMatrix<Matrix>>[row, column] = value;
                return writable.via<IMatrix<Matrix>>[row, column];
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.VerifyLibrary(compilation.Program!);
    }

    [Test]
    public void BindsMultiArgumentIndexerSetterCall()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal struct S {
                mut int32 stored;
                public int32 this[int32 row, int32 column] {
                    get { return stored; }
                    mut set { stored = row + column + value; }
                }
            }
            int32 Update(S input) {
                mut S writable = input;
                writable[1, 2] = 3;
                return writable[1, 2];
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.VerifyLibrary(compilation.Program!);
    }

    [Test]
    public void MaterializesDirectIndexerSetterSuffixDefaultBeforeValue()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal struct S {
                mut int32 stored;
                public int32 this[int32 row, int32 column = 2] {
                    get { return stored; }
                    mut set { stored = value + row + column; }
                }
            }
            int32 Update(S input) {
                mut S writable = input;
                writable[1] = 3;
                return writable[1, 2];
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.VerifyLibrary(compilation.Program!);
    }

    [Test]
    public void MutableIndexerSetterRequiresWritableReceiver()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal struct S {
                mut int32 stored;
                public int32 this[int32 index] {
                    get { return stored; }
                    mut set { stored = value + index; }
                }
            }
            int32 Update(S input) {
                input[1] = 3;
                return 0;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True,
            "A mutable indexer setter needs writable caller access.");
    }

    [Test]
    public void BindsInterfaceIndexerSetterCall()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                int32 this[int32 index] { get; mut set; }
            }
            internal struct S : I<S> {
                mut int32 stored;
                public int32 this[int32 index] {
                    get { return stored; }
                    mut set { stored = value + index; }
                }
            }
            int32 Update(S input) {
                mut S writable = input;
                writable.via<I<S>>[1] = 3;
                return writable.via<I<S>>[1];
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.VerifyLibrary(compilation.Program!);
    }

    [Test]
    public void BindsGenericProofInterfaceIndexerSetterCall()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                int32 this[int32 index] { get; mut set; }
            }
            int32 Update<T>(T input, int32 index)
                where T : write, I<T> {
                mut T writable = input;
                writable.via<I<T>>[index] = 3;
                return writable.via<I<T>>[index];
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.VerifyLibrary(compilation.Program!);
    }

    [Test]
    public void MutableIndexerRequirementAcceptsReadonlyImplementationWithWritableCaller()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                int32 this[int32 index] { mut get; }
            }
            internal struct S : I<S> {
                int32 Storage;
                public int32 this[int32 index] { get { return index; } }
            }
            int32 Read(S value) {
                mut S writable = value;
                return writable.via<I<S>>[1];
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.VerifyLibrary(compilation.Program!);
    }

    [Test]
    public void MutableIndexerRequirementRejectsReadonlyCallerPlace()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                int32 this[int32 index] { mut get; }
            }
            internal struct S : I<S> {
                int32 Storage;
                public int32 this[int32 index] { get { return index; } }
            }
            int32 Read(S value) {
                return value.via<I<S>>[1];
            }
            """));

        Assert.That(compilation.HasErrors, Is.True,
            "A mutable indexer requirement needs writable caller access.");
    }

    [Test]
    public void RejectsWrongIndexArgumentCountForGenericInterfaceIndexer()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                int32 this[int32 index] { get; }
            }
            int32 Read<T>(T value, int32 row, int32 column) where T : I<T> {
                return value.via<I<T>>[row, column];
            }
            """));

        Assert.That(compilation.HasErrors, Is.True,
            "The selected indexer requirement has one parameter.");
    }

    [Test]
    public void BindsMutableImplementationForMutableInterfaceRequirement()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                mut U Read<U>(U expectedValue);
            }
            internal struct S : I<S> {
                int32 Storage;
                public mut V Read<V>(V actualValue) { return actualValue; }
            }
            int32 Invoke(S value) {
                mut S writable = value;
                return writable.via<I<S>>.Read<int32>(
                    expectedValue: 42);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        SemanticVerifier.VerifyLibrary(compilation.Program!);
    }

    [Test]
    public void RejectsReadonlyPlaceForMutableInterfaceRequirement()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                mut U Read<U>(U expectedValue);
            }
            internal struct S : I<S> {
                V Read<V>(V actualValue) { return actualValue; }
            }
            int32 Invoke(S value) {
                return value.via<I<S>>.Read<int32>(
                    expectedValue: 42);
            }
            """));

        Assert.That(compilation.HasErrors, Is.True,
            "A mutable interface receiver requirement cannot be called through a readonly place.");
    }

    [Test]
    public void RejectsMutableImplementationForReadonlyInterfaceRequirement()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                U Read<U>(U expectedValue);
            }
            internal struct S : I<S> {
                mut V Read<V>(V actualValue) { return actualValue; }
            }
            """));

        Assert.That(compilation.HasErrors, Is.True,
            "A mutable implementation cannot add a receiver-write obligation.");
    }

    [Test]
    public void BindsMutableInterfacePropertyGetterToReadonlyGetter()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<T> {
                int32 Value { mut get; }
            }
            internal struct S : I<S> {
                int32 Field;
                public S(int32 value) { Field = value; }
                public int32 Value { get { return Field; } }
            }
            int32 Invoke(S value) {
                mut S writable = value;
                return writable.via<I<S>>.Value;
            }
            int32 Main() { return Invoke(new S(42)); }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        SemanticVerifier.Verify(compilation.Program!);
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void BindsMutableInterfacePropertyGetterToMutableGetter()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<T> {
                int32 Value { mut get; }
            }
            internal struct S : I<S> {
                public mut int32 Field;
                public S(int32 value) { Field = value; }
                public int32 Value { mut get { return Field; } }
            }
            int32 Main() {
                mut S writable = new S(42);
                return writable.via<I<S>>.Value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        SemanticVerifier.Verify(compilation.Program!);
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void RejectsReadonlyPlaceForMutableInterfacePropertyGetter()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                int32 Value { mut get; }
            }
            internal struct S : I<S> {
                int32 Field;
                int32 Value { get { return Field; } }
            }
            int32 Invoke(S value) {
                return value.via<I<S>>.Value;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True,
            "A mutable interface getter requirement needs a writable receiver.");
    }

    [Test]
    public void InterfacePropertyIdentityIncludesMutableAccessorRequirement()
    {
        var readonlyCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                internal interface I<T> { int32 Value { get; } }
                """));
        var mutableCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                internal interface I<T> { int32 Value { mut get; } }
                """));
        Assert.That(readonlyCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                readonlyCompilation.Diagnostics));
        Assert.That(mutableCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                mutableCompilation.Diagnostics));

        var readonlyKey = readonlyCompilation.Program!.Interfaces.Single()
            .Members.Single().DeclarationKey;
        var mutableMember = mutableCompilation.Program!.Interfaces.Single()
            .Members.Single();
        var mutableKey = mutableMember.DeclarationKey;
        Assert.That(mutableKey, Is.Not.EqualTo(readonlyKey));
    }

    [Test]
    public void InterfaceCallableIdentityIncludesMutableReceiverRequirement()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                int32 Read(int32 value);
            }
            internal interface J<T> {
                mut int32 Read(int32 value);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var members = compilation.Program!.Interfaces
            .SelectMany(static definition => definition.Members)
            .OrderBy(static member => member.RequiresMutableReceiver)
            .ToArray();
        Assert.That(members.Select(static member =>
                member.RequiresMutableReceiver),
            Is.EqualTo(new[] { false, true }));
        Assert.That(members[0].DeclarationKey,
            Is.Not.EqualTo(members[1].DeclarationKey));
    }

    [TestCase("static int32 Value { mut get; }")]
    [TestCase("int32 Value { mut init; }")]
    public void RejectsMutableNonordinaryInterfaceAccessors(
        string declaration)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"internal interface I<T> {{ {declaration} }}"));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void BindsGenericCallableWitnessWithAlphaRenamedInterfaceConstraint()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                U Read<U>(U expectedValue) where U : I<U>;
            }
            internal struct S : I<S> {
                int32 Storage;
                public V Read<V>(V actualValue) where V : I<V> {
                    return actualValue;
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        SemanticVerifier.VerifyLibrary(compilation.Program!);
    }

    [Test]
    public void BindsGenericCallableWitnessNoReturnContract()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                [DoesNotReturn] U Read<U>(U expectedValue);
            }
            internal struct S : I<S> {
                int32 Storage;
                [DoesNotReturn] public V Read<V>(V actualValue) {
                    require false;
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        SemanticVerifier.VerifyLibrary(compilation.Program!);
    }

    [Test]
    public void RejectsGenericWitnessWithMismatchedInterfaceConstraint()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                U Read<U>(U expectedValue) where U : I<U>;
            }
            internal interface J<T> { }
            internal struct S : I<S> {
                V Read<V>(V actualValue) where V : J<V> {
                    return actualValue;
                }
            }
            """));

        Assert.That(compilation.HasErrors, Is.True,
            "The implementation must preserve the interface formal's exact constraint identity.");
    }

    [Test]
    public void RejectsMismatchedExplicitGenericWitnessArgument()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                U Read<U>(U expectedValue);
            }
            internal struct S : I<S> {
                V Read<V>(V actualValue) { return actualValue; }
            }
            int32 Invoke(S value, int32 argument) {
                return value.via<I<S>>.Read<bool>(
                    expectedValue: argument);
            }
            """));

        Assert.That(compilation.HasErrors, Is.True,
            "The explicit bool type argument must not be replaced by inference from the int32 value.");
    }

    [Test]
    public void RejectsGenericWitnessWithMismatchedFormalArity()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                int32 Read<U>(U expectedValue);
            }
            internal struct S : I<S> {
                int32 Read<V, W>(V actualValue) { return 0; }
            }
            """));

        Assert.That(compilation.HasErrors, Is.True,
            "The implementation must preserve the interface method's generic arity.");
    }

    [Test]
    public void RejectsGenericWitnessWithAddedFormalConstraint()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                int32 Read<U>(U expectedValue);
            }
            internal struct S : I<S> {
                int32 Read<V>(V actualValue) where V : unique {
                    return 0;
                }
            }
            """));

        Assert.That(compilation.HasErrors, Is.True,
            "An implementation cannot narrow the interface method's caller domain.");
    }

    [Test]
    public void BindsPlainPropertyGetterWitness()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I {
                int32 Value { get; }
            }
            internal struct S : I {
                int32 Field;
                public int32 Value {
                    get { return Field; }
                }
            }
            int32 Main() { return 42; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var witness = compilation.Program!.InterfaceWitnesses.Single();
        Assert.That(witness.Members, Has.Length.EqualTo(1));
        Assert.That(witness.Members[0].Requirement.AccessorOrdinal,
            Is.EqualTo(0));
        Assert.That(witness.Members[0].TargetKind,
            Is.EqualTo(BoundInterfaceWitnessTargetKind.Implementation));
        Assert.That(compilation.Program.Functions[
                witness.Members[0].FunctionIndex].PropertyName,
            Is.EqualTo("Value"));
    }

    [Test]
    public void BindsDefaultPropertyGetterWitnessOrigin()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I {
                int32 Value {
                    get { return 7; }
                }
            }
            internal struct S : I { int32 Field; }
            int32 Main() { return 42; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var binding = compilation.Program!.InterfaceWitnesses.Single()
            .Members.Single();
        Assert.That(binding.TargetKind,
            Is.EqualTo(BoundInterfaceWitnessTargetKind.Default));
        Assert.That(binding.DefaultTarget,
            Is.EqualTo(binding.Requirement));
    }

    [Test]
    public void BindsExplicitCallableImplementationToExactInterfaceIdentity()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<T> {
                bool Equals(T other);
            }
            internal struct S : I<S> {
                int32 Value;
                public S(int32 value) { Value = value; }
                bool I<S>.Equals(S other) {
                    return Value == other.Value;
                }
            }
            int32 Main() { return 42; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var witness = program.InterfaceWitnesses.Single();
        var binding = witness.Members.Single();
        Assert.That(binding.TargetKind,
            Is.EqualTo(BoundInterfaceWitnessTargetKind.ExplicitImplementation));
        var target = program.Functions[binding.FunctionIndex];
        Assert.That(target.IsPrivate, Is.True);
        Assert.That(target.ExplicitInterfaceOwner
            is BoundInterfaceType explicitOwner
            && BoundInterfaceContract.AreEquivalent(explicitOwner,
                witness.InterfaceType), Is.True);
        Assert.That(SemanticVerifier.TryVerifySource(program, out _, out _,
            out _, out _), Is.True);
    }

    [Test]
    public void RejectsExplicitImplementationForDifferentInterfaceInstance()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<T> { bool Equals(T other); }
            internal struct S : I<S> {
                int32 Value;
                public S(int32 value) { Value = value; }
                bool I<int32>.Equals(int32 other) { return true; }
            }
            int32 Main() { return 42; }
            """));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3007"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void KeepsSameNamedExplicitRequirementsDistinct()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<T> { int32 Read(T value); }
            internal interface J<T> { int32 Read(T value); }
            internal struct S : I<S>, J<S> {
                int32 Value;
                int32 I<S>.Read(S value) { return Value; }
                int32 J<S>.Read(S value) { return value.Value; }
            }
            int32 Main() { return 42; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var witnesses = compilation.Program!.InterfaceWitnesses;
        Assert.That(witnesses, Has.Length.EqualTo(2));
        var targets = witnesses.Select(static witness =>
                witness.Members.Single())
            .ToArray();
        Assert.That(targets.Select(static target => target.FunctionIndex)
            .Distinct().ToArray(), Has.Length.EqualTo(2));
        Assert.That(targets.Select(static target => target.TargetKind),
            Is.All.EqualTo(
                BoundInterfaceWitnessTargetKind.ExplicitImplementation));
    }

    [Test]
    public void BindsLiteralAndForwardedConstantArgumentsOnInterfaceBases()
    {
        const string source = """
            public interface ICount<this T, const int32 Size = 2> { }
            public interface IBase<T = int32> { }
            public interface IWrapper<this T> : ICount<T, 4> { }
            public interface IDefault<this T> : ICount<T> { }
            public interface ITypeDefault : IBase { }
            public interface IForward<this T, const int32 Size> : ICount<T, Size> {
                ICount<T, Count> Convert<const int32 Count>(
                    ICount<T, Count> value);
            }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var wrapper = compilation.Program!.Interfaces.Single(static item =>
            item.QualifiedName == "IWrapper").Bases.Single();
        Assert.That(wrapper.TypeArguments[0].Kind,
            Is.EqualTo(BoundInterfaceTypeArgumentKind.Type));
        Assert.That(wrapper.TypeArguments[1].Kind,
            Is.EqualTo(BoundInterfaceTypeArgumentKind.Constant));
        Assert.That(wrapper.TypeArguments[1].ConstantValue!.Value, Is.EqualTo(4));

        var defaulted = compilation.Program.Interfaces.Single(static item =>
            item.QualifiedName == "IDefault").Bases.Single();
        Assert.That(defaulted.TypeArguments.Length, Is.EqualTo(2));
        Assert.That(defaulted.TypeArguments[1].ConstantValue!.Value, Is.EqualTo(2));

        var typeDefault = compilation.Program.Interfaces.Single(static item =>
            item.QualifiedName == "ITypeDefault").Bases.Single();
        Assert.That(typeDefault.TypeArguments.Single().Type!.PrimitiveType,
            Is.EqualTo(PrimitiveType.Int32));

        var forwarding = compilation.Program.Interfaces.Single(static item =>
            item.QualifiedName == "IForward").Bases.Single();
        Assert.That(forwarding.TypeArguments[1].ConstantValue, Is.Null);
        Assert.That(forwarding.TypeArguments[1].ConstantParameterScope,
            Is.EqualTo(BoundInterfaceTypeParameterScope.Interface));
        Assert.That(forwarding.TypeArguments[1].ConstantParameterOrdinal, Is.EqualTo(1));
        var method = compilation.Program.Interfaces.Single(static item =>
            item.QualifiedName == "IForward").Members.Single();
        Assert.That(method.ResultType!.TypeArguments[1].ConstantParameterScope,
            Is.EqualTo(BoundInterfaceTypeParameterScope.Member));
        Assert.That(method.ResultType.TypeArguments[1].ConstantParameterOrdinal,
            Is.EqualTo(0));
        Assert.That(method.Parameters.Single().Type.TypeArguments[1]
                .ConstantParameterScope,
            Is.EqualTo(BoundInterfaceTypeParameterScope.Member));
    }

    [Test]
    public void RejectsInterfaceConstantArgumentsFromAnIncompatibleDomain()
    {
        const string source = """
            public interface ICount<this T, const int32 Size> { }
            public interface IInvalid<this T, const bool Enabled>
                : ICount<T, Enabled> { }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True);
    }

    [Test]
    public void RewritesInterfaceConstantTypeAndValueTogether()
    {
        const string source = """
            public interface ICount<this T, const int32 Size = 2> { }
            public interface IWrapper<this T> : ICount<T, 4> { }
            int32 Main() => 0;
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var rewritten = compilation.Program!.Interfaces
            .Select(@interface => BoundTreeTraversal.RewriteTypes(
                @interface,
                static type => type == PrimitiveType.Int32
                    ? PrimitiveType.Int64 : type))
            .ToArray();
        var wrapper = rewritten.Single(static item =>
            item.QualifiedName == "IWrapper").Bases.Single();
        var constant = wrapper.TypeArguments[1];

        Assert.That(constant.ConstantType!.PrimitiveType, Is.EqualTo(PrimitiveType.Int64));
        Assert.That(constant.ConstantValue!.Type, Is.EqualTo(PrimitiveType.Int64));
        Assert.That(constant.ConstantValue.Value, Is.EqualTo(4));
        Assert.That(BoundInterfaceContract.IsValidTable([.. rewritten], static _ => true), Is.True);
    }

    [Test]
    public void BindsCallablePropertyIndexerAndAssociatedRequirements()
    {
        const string source = """
            public interface IValue<this T> {
                T Read<U>(U value) require true ensure true;
                T Value { get; set; }
                T this[int32 index] { get; }
                type Item = T;
                const int32 Limit;
            }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }

    [Test]
    public void RejectsAssociatedConstantDeclarationDefault()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I {
                const int32 Count = 3;
            }
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
            && diagnostic.Message.Contains(
                "cannot declare a default value",
                StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void BindsGenericDependencyAndOutputContracts()
    {
        const string source = """
            public interface IStorage<T> {
                [return: DependsOn(nameof(value))]
                [return: ForwardsBacking(nameof(value))]
                T Forward(T value);
                bool Initialize([DependsOn(nameof(source))] ref<init T> output, T source);
            }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }

    [Test]
    public void RejectsUnknownInterfaceDependencySource()
    {
        const string source = """
            public interface IStorage<T> {
                [return: DependsOn(nameof(missing))]
                T Forward(T value);
            }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3023"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void BindsAccessorLocalDependencyContracts()
    {
        const string source = """
            public interface IStorage<T> {
                T Current { [return: DependsOn(this)] get; }
                T Value { get; [this: Captures(nameof(value))] set; }
            }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }

    [Test]
    public void ComposesPartialInterfacesAndDistinctCallableOverloads()
    {
        const string source = """
            public partial interface IValues<T> {
                T Read(int32 index);
            }
            public partial interface IValues<T> {
                T Read(bool selector);
            }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }

    [Test]
    public void RejectsDuplicateInterfaceMemberIdentity()
    {
        const string source = """
            public interface IValues<T> {
                T Read(int32 index);
                bool Read(int32 position);
            }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3003"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [TestCase("public interface I<T> : Missing<T> { }")]
    [TestCase("public interface IBase<T> { } public interface I<T> : IBase { }")]
    public void RejectsUnresolvedOrWrongArityBaseInterface(string declarations)
    {
        var source = declarations + " int32 Main() => 0;";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code is "TRM3005" or "TRM3003"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void RejectsCyclicInterfaceInheritance()
    {
        const string source = """
            public interface ILeft<T> : IRight<T> { }
            public interface IRight<T> : ILeft<T> { }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void RetainsNativeSignatureTypesInInterfaceMembers()
    {
        const string source = """
            public interface ICallback {
                void Invoke(fnptr<int32> callback);
            }
            int32 Main() => 0;
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }
}
