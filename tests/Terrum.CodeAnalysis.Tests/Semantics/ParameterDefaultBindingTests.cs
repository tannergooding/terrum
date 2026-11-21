// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class ParameterDefaultBindingTests
{
    [Test]
    public void MaterializesInterleavedDefaultsAgainstNamedFormalPositions()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            int32 Sum(int32 first = 1 + 2, int32 required, int32 last = 4) {
                int32 firstCopy = first;
                int32 lastCopy = last;
                return firstCopy + required + lastCopy;
            }
            int32 Use() { return Sum(required: 2); }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        SemanticVerifier.VerifyLibrary(program);

        var sum = program.Functions.Single(
            static function => function.Name == "Sum");
        var parameterDefaults = sum.OrdinaryParameterDefaults;
        Assert.That(parameterDefaults.Select(
                static parameterDefault => parameterDefault.ParameterOrdinal),
            Is.EqualTo([0, 2]));
        var firstDefault = parameterDefaults[0];
        var lastDefault = parameterDefaults[1];
        Assert.That(firstDefault.Value!.Value, Is.EqualTo(3));
        Assert.That(lastDefault.Value!.Value, Is.EqualTo(4));
        var visitedDefaultConstants = 0;
        var expressionRewrittenFunction
            = BoundTreeTraversal.RewriteExpressions(sum, expression =>
            {
                if (ReferenceEquals(expression, firstDefault.Value)
                    || ReferenceEquals(expression, lastDefault.Value))
                {
                    visitedDefaultConstants++;
                }

                return expression;
            });
        Assert.That(visitedDefaultConstants, Is.EqualTo(2));
        var firstLocal = sum.ParameterCount;
        var secondLocal = firstLocal + 1;
        Assert.That(sum.Variables.Length, Is.GreaterThan(secondLocal));
        var remappedVariables = sum.Variables.ToBuilder();
        var firstLocalVariable = remappedVariables[firstLocal];
        remappedVariables[firstLocal] = remappedVariables[secondLocal];
        remappedVariables[secondLocal] = firstLocalVariable;
        var remappedFunction = BoundTreeTraversal.RewriteVariables(sum,
            remappedVariables.ToImmutable(),
            variable => variable == firstLocal ? secondLocal
                : variable == secondLocal ? firstLocal : variable);
        var remappedDefaults = remappedFunction.OrdinaryParameterDefaults;
        Assert.That(remappedDefaults.Select(
                static parameterDefault => parameterDefault.ParameterOrdinal),
            Is.EqualTo([0, 2]));
        Assert.That(remappedDefaults.Select(
                static parameterDefault => parameterDefault.Value!.Value),
            Is.EqualTo([3, 4]));
        Assert.That(remappedDefaults[0].SourceInputOrdinal,
            Is.EqualTo(firstDefault.SourceInputOrdinal));
        Assert.That(remappedDefaults[0].Span, Is.EqualTo(firstDefault.Span));
        Assert.That(remappedDefaults[1].SourceInputOrdinal,
            Is.EqualTo(lastDefault.SourceInputOrdinal));
        Assert.That(remappedDefaults[1].Span, Is.EqualTo(lastDefault.Span));

        var use = program.Functions.Single(
            static function => function.Name == "Use");
        var calls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(use.Body,
            expression => {
                if (expression is BoundCall call)
                {
                    calls.Add(call);
                }
            }), Is.True);

        var call = calls.Single();
        Assert.That(call.Arguments, Has.Length.EqualTo(3));
        Assert.That(call.ParameterIndex(0), Is.EqualTo(1));
        Assert.That(call.ParameterIndex(1), Is.EqualTo(0));
        Assert.That(call.ParameterIndex(2), Is.EqualTo(2));
        Assert.That(call.Arguments[0], Is.TypeOf<BoundConstant>());
        Assert.That(((BoundConstant)call.Arguments[0]).Value,
            Is.EqualTo(2));
        Assert.That(call.Arguments[1], Is.TypeOf<BoundConstant>());
        Assert.That(((BoundConstant)call.Arguments[1]).Value,
            Is.EqualTo(3));
        Assert.That(call.Arguments[2], Is.TypeOf<BoundConstant>());
        Assert.That(((BoundConstant)call.Arguments[2]).Value,
            Is.EqualTo(4));
    }

    [Test]
    public void ExplicitValuesDoNotMaterializeDefaults()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            int32 Sum(int32 first = 1, int32 second = 2) {
                return first + second;
            }
            int32 Use() { return Sum(4, 5); }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        SemanticVerifier.VerifyLibrary(program);

        var use = program.Functions.Single(
            static function => function.Name == "Use");
        var calls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(use.Body,
            expression => {
                if (expression is BoundCall call)
                {
                    calls.Add(call);
                }
            }), Is.True);

        var call = calls.Single();
        Assert.That(call.Arguments, Has.Length.EqualTo(2));
        Assert.That(call.ArgumentParameterIndices.IsDefault, Is.True);
    }

    [Test]
    public void FormsConstEvalParameterDefaultAtDeclaration()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [ConstEval] int32 Seven() { return 7; }
            [ConstEval] int32 Select(int32 value = const Seven()) {
                return value;
            }
            [ConstEval] int32 Outer() { return const Select(); }
            int32 Main() { return const Outer(); }
            """), CoreEvaluationFormTestHelpers
                .OptionsWithConstEvalAndContractFailures());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        SemanticVerifier.Verify(program);

        var outer = program.Functions.Single(
            static function => function.Name == "Outer");
        Assert.That(((BoundConstant)((BoundReturn)
            outer.Body.Statements.Single()).Value!).Value, Is.EqualTo(7));
        var main = program.Functions.Single(
            static function => function.Name == "Main");
        Assert.That(((BoundConstant)((BoundReturn)
            main.Body.Statements.Single()).Value!).Value, Is.EqualTo(7));
    }

    [Test]
    public void RejectsFailedConstEvalDefaultEvenWhenCallSuppliesValue()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [ConstEval] int32 Divide(int32 left, int32 right) {
                return left / right;
            }
            int32 Read(int32 value = Divide(1, 0)) { return value; }
            int32 Use() { return Read(9); }
            """), CoreEvaluationFormTestHelpers
                .OptionsWithConstEvalAndContractFailures());

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3005"), Is.True);
    }

    [TestCase("int32 Read(ref<int32> value = 1) { return 0; }")]
    [TestCase("bool Try(out int32 value = 0) { value = 1; return true; }")]
    public void RejectsDefaultsOnReferenceAndOutputParameters(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.True,
            "Reference and output parameters cannot declare defaults.");
    }

    [Test]
    public void MaterializesPositionalDefaultAtItsFormalOrdinal()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            int32 Sum(int32 first, int32 second = 7) {
                return first + second;
            }
            int32 Use() { return Sum(3); }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        SemanticVerifier.VerifyLibrary(program);

        var use = program.Functions.Single(
            static function => function.Name == "Use");
        var calls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(use.Body,
            expression => {
                if (expression is BoundCall call)
                {
                    calls.Add(call);
                }
            }), Is.True);

        var call = calls.Single();
        Assert.That(call.Arguments, Has.Length.EqualTo(2));
        Assert.That(call.ParameterIndex(0), Is.EqualTo(0));
        Assert.That(call.ParameterIndex(1), Is.EqualTo(1));
        Assert.That(call.Arguments[1], Is.TypeOf<BoundConstant>());
        Assert.That(((BoundConstant)call.Arguments[1]).Value,
            Is.EqualTo(7));
    }

    [Test]
    public void MaterializesClosedGenericMethodParameterDefault()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            int32 Get<T>(int32 value = 7, T marker) {
                return value;
            }
            int32 Use() { return Get<int32>(marker: 3); }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        SemanticVerifier.VerifyLibrary(program);

        var use = program.Functions.Single(
            static function => function.Name == "Use");
        var calls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(use.Body,
            expression =>
            {
                if (expression is BoundCall call)
                {
                    calls.Add(call);
                }
            }), Is.True);

        var call = calls.Single();
        Assert.That(call.Arguments, Has.Length.EqualTo(2));
        Assert.That(call.ParameterIndex(0), Is.EqualTo(1));
        Assert.That(call.ParameterIndex(1), Is.EqualTo(0));
        Assert.That(((BoundConstant)call.Arguments[1]).Value,
            Is.EqualTo(7));
    }

    [Test]
    public void GenericMethodDefaultSubstitutesConstantFormalsAndPreservesOpenCalls()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            int32 Read<T, const int32 N>(T marker, int32 value = N + 1) {
                return value;
            }
            int32 Forward<T, const int32 N>(T marker) {
                return Read<T, N>(marker);
            }
            int32 Use() { return Read<int32, 41>(0); }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        SemanticVerifier.VerifyLibrary(program);

        var use = program.Functions.Single(
            static function => function.Name == "Use");
        var calls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(use.Body,
            expression =>
            {
                if (expression is BoundCall call)
                {
                    calls.Add(call);
                }
            }), Is.True);
        Assert.That(((BoundConstant)calls.Single().Arguments[1]).Value,
            Is.EqualTo(42));

        var forward = program.GenericMethodDefinitions.Single(
            static definition => definition.Name == "Forward");
        var proofFunction = forward.ProofProgram.Functions[
            forward.FunctionIndex];
        var proofCalls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(proofFunction.Body,
            expression =>
            {
                if (expression is BoundCall call)
                {
                    proofCalls.Add(call);
                }
            }), Is.True);
        var proofCall = proofCalls.Single();
        Assert.That(proofCall.Arguments[1],
            Is.TypeOf<BoundBinary>());
        var defaultTerm = (BoundBinary)proofCall.Arguments[1];
        Assert.That(defaultTerm.Left, Is.TypeOf<BoundRead>());
        Assert.That(defaultTerm.Right, Is.TypeOf<BoundConstant>());
    }

    [Test]
    public void ForwardsOpenGenericNominalFormalThroughOrdinaryDefault()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Transparent)]
            struct Tagged<T> { public int32 Value; }
            int32 Read<T, const Tagged<T> Marker>(
                Tagged<T> value = Marker) {
                return 1;
            }
            int32 Forward<T, const Tagged<T> Marker>() {
                return Read<T, Marker>();
            }
            """), new CompilationOptions {
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        SemanticVerifier.VerifyLibrary(program);

        var read = program.GenericMethodDefinitions.Single(
            static definition => definition.Name == "Read");
        var readConstantFormal = read.ConstantFormals.Single();
        var readPlaceholderStructureIndex
            = (int)readConstantFormal.PlaceholderType - 100;
        Assert.That(readPlaceholderStructureIndex,
            Is.GreaterThanOrEqualTo(0).And.LessThan(
                read.ProofProgram.Structures.Length));
        Assert.That(read.ProofProgram.Structures[
                readPlaceholderStructureIndex]
            .SourceGenericTypeDefinitionIndex, Is.GreaterThanOrEqualTo(0));
        var defaultValue = read.OrdinaryParameterDefaults.Single();
        Assert.That(defaultValue, Is.Not.Null);
        var defaultDependency = defaultValue!.FormalDependencies.Single();
        Assert.That(defaultDependency.OwnerKind,
            Is.EqualTo(BoundGenericTypeFormalOwnerKind.GenericMethod));
        Assert.That(defaultDependency.DefinitionOrdinal,
            Is.EqualTo(read.DefinitionOrdinal));
        Assert.That(defaultDependency.ConstantFormalOrdinal, Is.Zero);

        var forward = program.GenericMethodDefinitions.Single(
            static definition => definition.Name == "Forward");
        var proofFunction = forward.ProofProgram!.Functions[
            forward.FunctionIndex];
        var calls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(proofFunction.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation,
                    } call)
                {
                    calls.Add(call);
                }
            }), Is.True);
        var call = calls.Single();
        var forwardedDefault = call.Arguments.Single();
        Assert.That(forwardedDefault, Is.TypeOf<BoundRead>());
        Assert.That(forwardedDefault.Type,
            Is.EqualTo(forward.ConstantFormals.Single().PlaceholderType));
        Assert.That(proofFunction.Variables[
                ((BoundRead)forwardedDefault).Variable].Name,
            Is.EqualTo("Marker"));

        var malformedDefaults = read.OrdinaryParameterDefaults.ToBuilder();
        malformedDefaults[0] = defaultValue! with {
            FormalDependencies = [
                defaultDependency with {
                    ConstantFormalOrdinal = int.MaxValue,
                },
            ],
        };
        var malformedRead = read with {
            OrdinaryParameterDefaults = malformedDefaults.ToImmutable(),
        };
        var malformedProgram = program with {
            GenericMethodDefinitions = [
                .. program.GenericMethodDefinitions.Select(candidate =>
                    candidate.DefinitionOrdinal == read.DefinitionOrdinal
                        ? malformedRead : candidate),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(malformedProgram,
            out _, out _, out _, out _), Is.False);

        var foreignOwnerDefaults
            = read.OrdinaryParameterDefaults.ToBuilder();
        foreignOwnerDefaults[0] = defaultValue! with {
            FormalDependencies = [
                defaultDependency with {
                    DefinitionOrdinal = forward.DefinitionOrdinal,
                },
            ],
        };
        var foreignOwnerRead = read with {
            OrdinaryParameterDefaults = foreignOwnerDefaults.ToImmutable(),
        };
        var foreignOwnerProgram = program with {
            GenericMethodDefinitions = [
                .. program.GenericMethodDefinitions.Select(candidate =>
                    candidate.DefinitionOrdinal == read.DefinitionOrdinal
                        ? foreignOwnerRead : candidate),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(foreignOwnerProgram,
            out _, out _, out _, out _), Is.False);
    }

    [Test]
    public void ForwardsSourceOwnerFormalThroughOpenGenericParameterDefault()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Cell<const int32 Seed> {
                public int32 Value;
                public Cell(int32 value) { this.Value = value; }
                public int32 Read<const int32 Amount>(
                    int32 value = Seed + Amount) {
                    return value;
                }
            }
            struct Wrapper<const int32 Seed> {
                public Wrapper(int32 value) { }
                public int32 Forward<const int32 N>(Cell<Seed> cell) {
                    return cell.Read<N>() + Identity<Seed>();
                }
            }
            int32 Identity<const int32 Value>() { return Value; }
            struct Other<const int32 Seed> { public int32 Value; }
            int32 Main() {
                Cell<3> cell = new Cell<3>(1);
                Wrapper<3> wrapper = new Wrapper<3>(0);
                return wrapper.Forward<2>(cell);
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The generic owner-default forwarding program did not bind.");
        SemanticVerifier.Verify(program);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast),
            Is.EqualTo(8));

        var forward = program.GenericMethodDefinitions.Single(
            static definition => definition.Name.Contains("Forward",
                StringComparison.Ordinal));
        var owner = forward.Owner
            ?? throw new InvalidOperationException(
                "The generic caller has no source owner.");
        var other = program.SourceGenericTypeDefinitions.Single(
            static definition => definition.Name == "Other");
        var foreignOwner = owner with {
            ConstantArguments = [
                .. owner.ConstantArguments.Select((argument,
                    ordinal) => ordinal == 0
                    ? argument with {
                        CallerDefinitionOrdinal = other.DefinitionIndex,
                    }
                    : argument),
            ],
        };
        var foreignProofSignatures
            = forward.ProofProgram.GenericMethodSignatures.ToBuilder();
        foreignProofSignatures[forward.DefinitionOrdinal]
            = foreignProofSignatures[forward.DefinitionOrdinal] with {
                Owner = foreignOwner,
            };
        var foreignForward = forward with {
            Owner = foreignOwner,
            ProofProgram = forward.ProofProgram with {
                GenericMethodSignatures =
                    foreignProofSignatures.ToImmutable(),
            },
        };
        var foreignSignatures = program.GenericMethodSignatures.ToBuilder();
        foreignSignatures[forward.DefinitionOrdinal]
            = foreignSignatures[forward.DefinitionOrdinal] with {
                Owner = foreignOwner,
            };
        var foreignOwnerProgram = program with {
            GenericMethodDefinitions = [
                .. program.GenericMethodDefinitions.Select(definition =>
                    definition.DefinitionOrdinal
                        == forward.DefinitionOrdinal
                        ? foreignForward : definition),
            ],
            GenericMethodSignatures = foreignSignatures.ToImmutable(),
        };
        Assert.That(SemanticVerifier.TryVerifySource(foreignOwnerProgram,
            out _, out _, out _, out _), Is.False);
    }

    [Test]
    public void SubstitutesOpenOwnerTypeFormalsInGenericDefaultDomains()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Transparent)]
            struct Tagged<T> {
                public int32 Value;
                [ConstEval]
                public Tagged(int32 value) { Value = value; }
            }

            struct Cell<const Tagged<T> Seed, T> {
                public int32 Value;
                public Cell(int32 value) { Value = value; }
                public Tagged<T> Read<const int32 Amount>(
                    Tagged<T> value = Seed) {
                    return value;
                }
            }

            struct Wrapper<const Tagged<T> Seed, T> {
                public int32 Value;
                public Wrapper(int32 value) { Value = value; }
                public int32 Forward<const int32 N>(
                    Cell<Seed, T> cell) {
                    return cell.Read<N>().Value
                        + Identity<T, Seed>();
                }
            }
            int32 Identity<T, const Tagged<T> Value>() {
                return Value.Value;
            }
            int32 Main() {
                Cell<(new Tagged<int32>(9)), int32> cell
                    = new Cell<(new Tagged<int32>(9)), int32>(0);
                Wrapper<(new Tagged<int32>(9)), int32> wrapper
                    = new Wrapper<(new Tagged<int32>(9)), int32>(0);
                return wrapper.Forward<2>(cell);
            }
            """), CoreEvaluationFormTestHelpers.WithConstEval(
                new CompilationOptions {
                    RuntimePolicy =
                        BoundRuntimePolicy.CheckedExceptions,
                    LayoutCoreRegistration =
                        LayoutCoreRegistrationTestData.Create(),
                }));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The generic owner wrapper-domain default did not bind.");
        SemanticVerifier.Verify(program);
        Assert.That(NativeJit.Execute(program,
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(18));
    }

    [Test]
    public void RetainsOpenOwnerWrapperDomainInGenericParameterDefault()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Transparent)]
            struct Tagged<T> { public int32 Value; }

            struct Cell<const Tagged<T> Seed, T> {
                public Tagged<T> Read<const int32 Amount>(
                    Tagged<T> value = Seed) {
                    return value;
                }
            }

            struct Wrapper<const Tagged<T> Seed, T> {
                public Tagged<T> Forward<const int32 N>(
                    Cell<Seed, T> cell) {
                    return cell.Read<N>();
                }
            }
            """), new CompilationOptions {
            LayoutCoreRegistration =
                LayoutCoreRegistrationTestData.Create(),
        });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The generic owner wrapper-domain default did not bind.");
        SemanticVerifier.VerifyLibrary(program);

        var read = program.GenericMethodDefinitions.Single(
            static definition => definition.Name.Contains("Read",
                StringComparison.Ordinal));
        var dependency = read.OrdinaryParameterDefaults.Single()!
            .FormalDependencies.Single();
        Assert.That(dependency.OwnerKind,
            Is.EqualTo(BoundGenericTypeFormalOwnerKind.SourceGenericType));
        Assert.That(dependency.DefinitionOrdinal,
            Is.EqualTo(read.Owner!.SourceGenericTypeDefinitionIndex));
        Assert.That(dependency.Domain.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.SourceGeneric));
    }

    [Test]
    public void CapturesOwnerAndMethodFormalsInGenericParameterDefault()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Cell<const int32 Seed> {
                public int32 Value;
                public Cell(int32 value) { this.Value = value; }
                public int32 Read<const int32 Amount>(
                    int32 value = Seed + Amount) {
                    return value;
                }
            }
            struct Other<const int32 Seed> { public int32 Value; }
            int32 Forward<const int32 N>(Cell<N> cell) {
                return cell.Read<2>();
            }
            int32 Main() {
                Cell<3> cell = new Cell<3>(1);
                return Forward<3>(cell);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        SemanticVerifier.Verify(program);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast),
            Is.EqualTo(5));

        var forward = program.GenericMethodDefinitions.Single(
            static definition => definition.Name == "Forward");
        var read = program.GenericMethodDefinitions.Single(definition =>
            definition.Name.Contains("Read", StringComparison.Ordinal)
            && definition.Owner is BoundGenericMethodOwner owner
            && owner.ConstantArguments.Length == 1
            && owner.ConstantArguments[0].Kind
                == BoundGenericMethodConstantArgumentKind.Formal
            && owner.ConstantArguments[0].CallerDefinitionOrdinal
                == forward.DefinitionOrdinal);
        var dependencies = read.OrdinaryParameterDefaults.Single()!
            .FormalDependencies;
        Assert.That(dependencies.Select(static dependency =>
                dependency.OwnerKind),
            Is.EqualTo([
                BoundGenericTypeFormalOwnerKind.SourceGenericType,
                BoundGenericTypeFormalOwnerKind.GenericMethod,
            ]));
        Assert.That(dependencies.Select(static dependency =>
                dependency.ConstantFormalOrdinal),
            Is.EqualTo(new[] { 0, 0 }));
        var other = program.SourceGenericTypeDefinitions.Single(
            static definition => definition.Name == "Other");
        var foreignOwnerDependencies = dependencies.ToBuilder();
        foreignOwnerDependencies[0] = dependencies[0] with {
            DefinitionOrdinal = other.DefinitionIndex,
        };
        var malformedRead = read with {
            OrdinaryParameterDefaults = [
                read.OrdinaryParameterDefaults.Single()! with {
                    FormalDependencies =
                        foreignOwnerDependencies.ToImmutable(),
                },
            ],
        };
        var malformedProgram = program with {
            GenericMethodDefinitions = [
                .. program.GenericMethodDefinitions.Select(definition =>
                    definition.DefinitionOrdinal == read.DefinitionOrdinal
                        ? malformedRead : definition),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(malformedProgram,
            out _, out _, out _, out _), Is.False);

        var reversedRead = read with {
            OrdinaryParameterDefaults = [
                read.OrdinaryParameterDefaults.Single()! with {
                    FormalDependencies = [
                        dependencies[1],
                        dependencies[0],
                    ],
                },
            ],
        };
        var reversedProgram = program with {
            GenericMethodDefinitions = [
                .. program.GenericMethodDefinitions.Select(definition =>
                    definition.DefinitionOrdinal == read.DefinitionOrdinal
                        ? reversedRead : definition),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(reversedProgram,
            out _, out _, out _, out _), Is.False);

        var proofFunction = forward.ProofProgram.Functions[
            forward.FunctionIndex];
        var calls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(proofFunction.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation,
                    } call)
                {
                    calls.Add(call);
                }
            }), Is.True);

        var forwardedDefault = calls.Single().Arguments.Last();
        Assert.That(forwardedDefault, Is.TypeOf<BoundBinary>());
        var forwardedRead = ((BoundBinary)forwardedDefault).Left;
        Assert.That(forwardedRead, Is.TypeOf<BoundRead>());
        Assert.That(proofFunction.Variables[
                ((BoundRead)forwardedRead).Variable].Name,
            Is.EqualTo("N"));
    }

    [Test]
    public void GenericMethodDefaultFormsOnlyWhenAnOmissionSelectsIt()
    {
        const string source = """
            int32 Read<T, const int32 N>(T marker, int32 value = 1 / N) {
                return value;
            }
            int32 Use() { return Read<int32, 0>(0, 7); }
            """;
        var explicitCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source));
        Assert.That(explicitCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                explicitCompilation.Diagnostics));
        SemanticVerifier.VerifyLibrary(explicitCompilation.Program!);

        var omittedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source.Replace("Read<int32, 0>(0, 7)",
                "Read<int32, 0>(0)",
                StringComparison.Ordinal)));
        Assert.That(omittedCompilation.HasErrors, Is.True,
            "The selected default must fail checked constant formation.");
        Assert.That(omittedCompilation.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3005"), Is.True);
    }

    [Test]
    public void InterfaceCallsUseRequirementDefaultsInsteadOfImplementationDefaults()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                int32 Sum(int32 first = 1, int32 required, int32 last = 3);
            }
            internal struct S : I<S> {
                int32 Field;
                public int32 Sum(int32 x = 9, int32 y, int32 z = 11) {
                    return x + y + z;
                }
            }
            int32 ThroughInterface(S value) {
                return value.via<I<S>>.Sum(required: 2);
            }
            int32 Direct(S value) { return value.Sum(y: 2); }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        SemanticVerifier.VerifyLibrary(program);

        var interfaceFunction = program.Functions.Single(
            static function => function.Name == "ThroughInterface");
        var interfaceCalls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(
            interfaceFunction.Body, expression =>
            {
                if (expression is BoundCall call)
                {
                    interfaceCalls.Add(call);
                }
            }), Is.True);
        var interfaceCall = interfaceCalls.Single(
            static call => call.InterfaceCallIdentity is not null);
        Assert.That(interfaceCall.Arguments, Has.Length.EqualTo(4));
        Assert.That(interfaceCall.ParameterIndex(1), Is.EqualTo(2));
        Assert.That(interfaceCall.ParameterIndex(2), Is.EqualTo(1));
        Assert.That(interfaceCall.ParameterIndex(3), Is.EqualTo(3));
        Assert.That(((BoundConstant)interfaceCall.Arguments[2]).Value,
            Is.EqualTo(1));
        Assert.That(((BoundConstant)interfaceCall.Arguments[3]).Value,
            Is.EqualTo(3));

        var directFunction = program.Functions.Single(
            static function => function.Name == "Direct");
        var directCalls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(directFunction.Body,
            expression =>
            {
                if (expression is BoundCall call)
                {
                    directCalls.Add(call);
                }
            }), Is.True);
        var directCall = directCalls.Single();
        Assert.That(directCall.ParameterIndex(1), Is.EqualTo(2));
        Assert.That(directCall.ParameterIndex(2), Is.EqualTo(1));
        Assert.That(directCall.ParameterIndex(3), Is.EqualTo(3));
        Assert.That(((BoundConstant)directCall.Arguments[2]).Value,
            Is.EqualTo(9));
        Assert.That(((BoundConstant)directCall.Arguments[3]).Value,
            Is.EqualTo(11));
    }

    [Test]
    public void GenericInterfaceProofCallsUseRequirementDefaults()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                int32 Read(int32 value = 5);
            }
            int32 Read<T>(T value) where T : I<T> {
                return value.via<I<T>>.Read();
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.VerifyLibrary(compilation.Program!);
    }

    [Test]
    public void InterfaceCallsMaterializeConstEvalRequirementDefaults()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [ConstEval] int32 DefaultValue() { return 5; }
            internal interface I<T> {
                int32 Read(int32 value = const DefaultValue());
            }
            internal struct S : I<S> {
                int32 Field;
                public int32 Read(int32 implementationValue = 9) {
                    return implementationValue;
                }
            }
            int32 ThroughInterface(S value) {
                return value.via<I<S>>.Read();
            }
            int32 ThroughProof<T>(T value) where T : I<T> {
                return value.via<I<T>>.Read();
            }
            """), CoreEvaluationFormTestHelpers
                .OptionsWithConstEvalAndContractFailures());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        SemanticVerifier.VerifyLibrary(program);

        var directFunction = program.Functions.Single(
            static function => function.Name == "ThroughInterface");
        var directCalls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(
            directFunction.Body, expression =>
            {
                if (expression is BoundCall {
                    InterfaceCallIdentity: not null,
                } call)
                {
                    directCalls.Add(call);
                }
            }), Is.True);
        Assert.That(((BoundConstant)directCalls.Single().Arguments[1]).Value,
            Is.EqualTo(5));

        var genericDefinition = program.GenericMethodDefinitions.Single(
            static definition => definition.Name == "ThroughProof");
        var proofFunction = genericDefinition.ProofProgram!.Functions[
            genericDefinition.FunctionIndex];
        var proofCalls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(proofFunction.Body,
            expression =>
            {
                if (expression is BoundCall {
                    InterfaceCallIdentity: not null,
                } call)
                {
                    proofCalls.Add(call);
                }
            }), Is.True);
        var proofCall = proofCalls.Single();
        Assert.That(proofCall.DefaultedArguments.IsDefaultOrEmpty, Is.True);
        Assert.That(((BoundConstant)proofCall.Arguments[1]).Value,
            Is.EqualTo(5));
    }

    [Test]
    public void InterfaceIndexerGetterUsesRequirementIndexDefault()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal interface I<T> {
                int32 this[int32 row, int32 column = 3] { get; }
            }
            internal struct S : I<S> {
                int32 Field;
                public int32 this[int32 x, int32 y = 11] {
                    get { return x * 100 + y; }
                }
            }
            int32 ThroughInterface(S value) {
                return value.via<I<S>>[1];
            }
            int32 Direct(S value) { return value[1]; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        SemanticVerifier.VerifyLibrary(program);

        var interfaceFunction = program.Functions.Single(
            static function => function.Name == "ThroughInterface");
        var interfaceCalls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(
            interfaceFunction.Body, expression =>
            {
                if (expression is BoundCall call)
                {
                    interfaceCalls.Add(call);
                }
            }), Is.True);
        var interfaceCall = interfaceCalls.Single(
            static call => call.InterfaceCallIdentity is not null);
        Assert.That(((BoundConstant)interfaceCall.Arguments[2]).Value,
            Is.EqualTo(3));

        var directFunction = program.Functions.Single(
            static function => function.Name == "Direct");
        var directCalls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(directFunction.Body,
            expression =>
            {
                if (expression is BoundCall call)
                {
                    directCalls.Add(call);
                }
            }), Is.True);
        Assert.That(((BoundConstant)directCalls.Single().Arguments[2]).Value,
            Is.EqualTo(11));
    }

    [Test]
    public void InterfaceIndexerSetterBindsValueAfterRequirementIndexDefault()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [ConstEval] int32 DefaultColumn() { return 2; }
            internal interface I<T> {
                int32 this[int32 row, int32 column = const DefaultColumn()] {
                    get;
                    mut set;
                }
            }
            internal struct S : I<S> {
                int32 Field;
                public int32 this[int32 x, int32 y = 11] {
                    get { return x * 100 + y; }
                    mut set { }
                }
            }
            int32 Update(S input) {
                mut S writable = input;
                writable.via<I<S>>[1] = 3;
                return writable.via<I<S>>[1, 2];
            }
            int32 UpdateGeneric<T>(T input) where T : write, I<T> {
                mut T writable = input;
                writable.via<I<T>>[1] = 3;
                return writable.via<I<T>>[1, 2];
            }
            """), CoreEvaluationFormTestHelpers
                .OptionsWithConstEvalAndContractFailures());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        SemanticVerifier.VerifyLibrary(program);

        var update = program.Functions.Single(
            static function => function.Name == "Update");
        var calls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(update.Body,
            expression =>
            {
                if (expression is BoundCall {
                    InterfaceCallIdentity: not null,
                } call)
                {
                    calls.Add(call);
                }
            }), Is.True);
        var setterCall = calls.Single(
            static call => call.InterfaceCallIdentity!.Requirement
                .AccessorOrdinal == 1);
        Assert.That(setterCall.Arguments, Has.Length.EqualTo(4));
        Assert.That(((BoundConstant)setterCall.Arguments[1]).Value,
            Is.EqualTo(1));
        Assert.That(((BoundConstant)setterCall.Arguments[2]).Value,
            Is.EqualTo(2));
        Assert.That(((BoundConstant)setterCall.Arguments[3]).Value,
            Is.EqualTo(3));

        var genericDefinition = program.GenericMethodDefinitions.Single(
            static definition => definition.Name == "UpdateGeneric");
        var proofFunction = genericDefinition.ProofProgram!.Functions[
            genericDefinition.FunctionIndex];
        var proofCalls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(proofFunction.Body,
            expression =>
            {
                if (expression is BoundCall {
                    InterfaceCallIdentity: not null,
                } call)
                {
                    proofCalls.Add(call);
                }
            }), Is.True);
        var proofSetterCall = proofCalls.Single(
            static call => call.InterfaceCallIdentity!.Requirement
                .AccessorOrdinal == 1);
        Assert.That(proofSetterCall.Arguments, Has.Length.EqualTo(4));
        Assert.That(((BoundConstant)proofSetterCall.Arguments[2]).Value,
            Is.EqualTo(2));
        Assert.That(((BoundConstant)proofSetterCall.Arguments[3]).Value,
            Is.EqualTo(3));
    }
}
