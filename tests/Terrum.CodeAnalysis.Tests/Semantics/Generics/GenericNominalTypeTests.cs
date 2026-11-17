// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Semantics.Generics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class GenericNominalTypeTests
{
    [Test]
    public void SpecializesInternalGenericStructsAcrossFieldsMethodsAndConstruction()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Box<T>
            {
                T value;
                public Box(T value) { this.value = value; }
                public T Get() { return this.value; }
            }
            struct Nested<T>
            {
                Box<T> value;
                public Nested(Box<T> value) { this.value = value; }
                public T Get() { return this.value.Get(); }
            }
            uint32 ReadUnsigned(Box<uint32> value) { return value.Get(); }
            int32 Main()
            {
                Nested<int32> value = new Nested<int32>(new Box<int32>(42));
                return value.Get();
            }
            """));

        Assert.That(compilation.Diagnostics, Is.Empty);
        var program = compilation.Program!;
        var boxDefinition = program.SourceGenericTypeDefinitions.Single(
            static definition => definition.Name == "Box");
        var nestedDefinition = program.SourceGenericTypeDefinitions.Single(
            static definition => definition.Name == "Nested");
        Assert.That(boxDefinition.Formals.Select(static formal => formal.Ordinal),
            Is.EqualTo([0]));
        Assert.That(boxDefinition.Formals.Single().HasTypeDefault, Is.False);
        Assert.That(program.Structures.Single(static structure
                => structure.Name == "Box<int32>")
                .SourceGenericTypeDefinitionIndex,
            Is.EqualTo(boxDefinition.DefinitionIndex));
        var int32Box = program.Structures.Single(static structure
            => structure.Name == "Box<int32>");
        var int32BoxIndex = program.Structures.IndexOf(int32Box);
        Assert.That(program.SourceGenericTypeInstances.Single(instance
                => instance.StructureIndex == int32BoxIndex).TypeArguments,
            Is.EqualTo([PrimitiveType.Int32]));
        var uint32Box = program.Structures.Single(static structure
            => structure.Name == "Box<uint32>");
        var uint32BoxIndex = program.Structures.IndexOf(uint32Box);
        Assert.That(program.SourceGenericTypeInstances.Single(instance
                => instance.StructureIndex == uint32BoxIndex).TypeArguments,
            Is.EqualTo([PrimitiveType.UInt32]));
        Assert.That(program.SourceGenericTypeInstances.Single(instance
                => instance.DefinitionIndex == nestedDefinition.DefinitionIndex)
                .TypeArguments,
            Is.EqualTo([PrimitiveType.Int32]));
        var box = program.Structures.Single(static structure => structure.Name == "Box<int32>");
        Assert.That(box.Size, Is.EqualTo(4));
        Assert.That(box.Fields.Select(static field => field.Type),
            Is.EqualTo([PrimitiveType.Int32]));
        var unsignedBox = program.Structures.Single(static structure => structure.Name == "Box<uint32>");
        Assert.That(unsignedBox.Fields.Select(static field => field.Type),
            Is.EqualTo([PrimitiveType.UInt32]));
        var nested = program.Structures.Single(static structure => structure.Name == "Nested<int32>");
        Assert.That(nested.Fields.Single().Type, Is.EqualTo((PrimitiveType)(100
            + program.Structures.IndexOf(box))));
        Assert.That(program.Functions.Single(static function => function.Name == "Box<int32>.Get").ReturnType,
            Is.EqualTo(PrimitiveType.Int32));
        Assert.That(program.Functions.Single(static function => function.Name == "Box<int32>.Get").IsInternal,
            Is.True);
        Assert.That(program.Functions.Single(static function => function.Name == "Box<uint32>.Get").ReturnType,
            Is.EqualTo(PrimitiveType.UInt32));
        Assert.That(program.Functions.Single(static function => function.Name == "Nested<int32>.Get").ReturnType,
            Is.EqualTo(PrimitiveType.Int32));
        Assert.That(program.Functions.Single(static function => function.Name == "Main").ReturnType,
            Is.EqualTo(PrimitiveType.Int32));
    }

    [Test]
    public void AppliesGenericStructTypeDefaultsInDeclarationOrder()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Cell<T = int32, U = T> {
                public T first;
                public U second;
            }

            int32 ReadDefault(Cell value) { return value.second; }
            uint32 ReadExplicit(Cell<uint32> value) { return value.second; }
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Diagnostics, Is.Empty);
        var program = compilation.Program!;
        var cellDefinition = program.SourceGenericTypeDefinitions.Single(
            static definition => definition.Name == "Cell");
        Assert.That(cellDefinition.Formals.Select(static formal
                => formal.HasTypeDefault),
            Is.EqualTo([true, true]));
        var defaultCell = program.Structures.Single(static structure
            => structure.Name == "Cell<int32, int32>");
        Assert.That(defaultCell.Fields.Select(static field => field.Type),
            Is.EqualTo([
                PrimitiveType.Int32,
                PrimitiveType.Int32,
            ]));
        var unsignedCell = program.Structures.Single(static structure
            => structure.Name == "Cell<uint32, uint32>");
        Assert.That(unsignedCell.Fields.Select(static field => field.Type),
            Is.EqualTo([
                PrimitiveType.UInt32,
                PrimitiveType.UInt32,
            ]));
        Assert.That(program.Functions.Single(static function
            => function.Name == "ReadDefault").Variables[0].Type,
            Is.EqualTo((PrimitiveType)(100
                + program.Structures.IndexOf(defaultCell))));
        Assert.That(program.Functions.Single(static function
            => function.Name == "ReadExplicit").Variables[0].Type,
            Is.EqualTo((PrimitiveType)(100
                + program.Structures.IndexOf(unsignedCell))));
    }

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void DeclaresMixedGenericStructTypeAndConstantFormals(
        BoundRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Configuration<const int32 Count,
                T = int32, const int32 Next = Count + 1,
                const bool Enabled = true>
            {
                public T Value;
            }
            int32 Main() { return 0; }
            """), new CompilationOptions {
                RuntimePolicy = policy,
            });

        Assert.That(compilation.Diagnostics, Is.Empty,
            string.Join(System.Environment.NewLine, compilation.Diagnostics));
        var definition = compilation.Program!
            .SourceGenericTypeDefinitions.Single(static definition
                => definition.Name == "Configuration");
        Assert.That(definition.Formals.Select(static formal => formal.Kind),
            Is.EqualTo([
                BoundGenericMethodFormalKind.Constant,
                BoundGenericMethodFormalKind.Type,
                BoundGenericMethodFormalKind.Constant,
                BoundGenericMethodFormalKind.Constant,
            ]));
        Assert.That(definition.FormalOrder.Select(static formal => formal.Ordinal),
            Is.EqualTo([0, 1, 2, 3]));
        Assert.That(definition.Formals[0].ConstantFormalOrdinal,
            Is.Zero);
        Assert.That(definition.Formals[1].TypeFormalOrdinal,
            Is.Zero);
        Assert.That(definition.Formals[1].HasTypeDefault,
            Is.True);
        Assert.That(definition.Formals[1].DefaultFormalDependencies,
            Is.Empty);
        Assert.That(definition.ConstantFormals.Select(static formal
                => formal.Name),
            Is.EqualTo(["Count", "Next", "Enabled"]));
        Assert.That(definition.ConstantFormals.Select(static formal
                => formal.HasDefault),
            Is.EqualTo([false, true, true]));
        Assert.That(definition.ConstantFormals[1]
                .DefaultFormalDependencies,
            Is.EqualTo([0]));
        Assert.That(definition.ConstantFormals[0].Domain.ConcreteType,
            Is.EqualTo(PrimitiveType.Int32));
        Assert.That(definition.ConstantFormals[1].Domain.ConcreteType,
            Is.EqualTo(PrimitiveType.Int32));
        Assert.That(definition.ConstantFormals[2].Domain.ConcreteType,
            Is.EqualTo(PrimitiveType.Bool));
    }

    [TestCase("struct Invalid<const int32? Value> { }")]
    [TestCase("struct Invalid<const int32 First = Second, const int32 Second = 2> { }")]
    public void RejectsInvalidGenericStructConstantFormalDomainsAndDefaults(
        string declaration)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            declaration + " int32 Main() { return 0; }"));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True,
            string.Join(System.Environment.NewLine, compilation.Diagnostics));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void SpecializesGenericStructsByConstantValue(
        NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Marker<const int32 N>
            {
                int32 Value;
                public Marker(int32 value) { this.Value = value; }
                public int32 Read() { return N + this.Value; }
            }
            int32 Main() {
                Marker<3> first = new Marker<3>(1);
                Marker<4> second = new Marker<4>(2);
                return first.Read() * 10 + second.Read();
            }
            """), new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(System.Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new System.InvalidOperationException(
                "The source generic constant specialization did not bind.");
        var definition = program.SourceGenericTypeDefinitions.Single();
        var instances = program.SourceGenericTypeInstances
            .Where(instance => instance.DefinitionIndex
                == definition.DefinitionIndex)
            .OrderBy(static instance => instance.ConstantArguments[0].Bits.Low)
            .ToArray();
        Assert.That(instances, Has.Length.EqualTo(2));
        Assert.That(instances.Select(static instance
                => instance.ConstantArguments[0].Bits.Low),
            Is.EqualTo([3UL, 4UL]));
        Assert.That(instances.Select(static instance
                => instance.StructureIndex).Distinct().Count(),
            Is.EqualTo(2));
        Assert.That(program.Structures[instances[0].StructureIndex].Size,
            Is.EqualTo(program.Structures[instances[1].StructureIndex].Size));
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(46));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void PreservesCleanupAcrossGenericStructConstantSpecializations(
        NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Marker<const int32 N>
            {
                public int32 Value;
                public Marker(int32 value) { Value = value; }
                [Cleanup] private void Close() { }
            }
            int32 Main() {
                Marker<1> first = new Marker<1>(20);
                Marker<2> second = new Marker<2>(22);
                return first.Value + second.Value;
            }
            """), new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(System.Environment.NewLine,
                compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new System.InvalidOperationException(
                "The generic Cleanup specializations did not bind.");
        var definition = program.SourceGenericTypeDefinitions.Single(
            static item => item.Name == "Marker");
        var instances = program.SourceGenericTypeInstances
            .Where(instance => instance.DefinitionIndex
                == definition.DefinitionIndex)
            .OrderBy(static instance => instance.ConstantArguments[0]
                .Bits.Low)
            .ToArray();
        Assert.That(instances, Has.Length.EqualTo(2));
        var cleanupFunctions = new int[instances.Length];
        for (var index = 0; index < instances.Length; index++)
        {
            var structure = program.Structures[
                instances[index].StructureIndex];
            Assert.That(structure.CleanupMethod, Is.Not.Null);
            var cleanup = program.Functions.Single(function =>
                function.Name == structure.CleanupMethod);
            Assert.That(cleanup.IsCleanup, Is.True);
            cleanupFunctions[index] = program.Functions.IndexOf(cleanup);
        }

        var drops = new List<int>();
        var entry = program.Functions[program.EntryPoint];
        Assert.That(BoundTreeTraversal.VisitStatements(entry.Body,
            statement => {
                if (statement is BoundDrop drop)
                {
                    drops.Add(drop.CleanupFunction);
                }
            }), Is.True);
        Assert.That(drops.OrderBy(static function => function),
            Is.EqualTo(cleanupFunctions.OrderBy(
                static function => function)));
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void AppliesMixedGenericStructDefaultsAndDefaultSelectors(
        NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Configuration<const int32 N = 3,
                const int32 Next = N + 1, T = int32>
            {
                T Value;
                public Configuration(T value) { this.Value = value; }
                public int32 Read() { return N * 10 + Next + this.Value; }
            }
            int32 Main() {
                Configuration first = new Configuration(1);
                Configuration<default, default, default> second
                    = new Configuration<default, default, default>(2);
                Configuration<4> third = new Configuration<4>(3);
                return first.Read() * 100 + second.Read() * 10
                    + third.Read();
            }
            """), new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(System.Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new System.InvalidOperationException(
                "The mixed generic struct defaults did not bind.");
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(3908));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void AppliesTypeDefaultsUsingEarlierConstantFormals(
        NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Container<const int32 Count = 3, T = Box<Count>> {
                T Value;
                public Container(T value) { this.Value = value; }
                public int32 Read() { return this.Value.Read(); }
            }
            struct Box<const int32 Value> {
                public int32 Stored;
                public Box(int32 value) { Stored = value; }
                public int32 Read() { return this.Stored + Value; }
            }
            int32 Main() {
                Container<default, default> value
                    = new Container<default, default>(
                        new Box<3>(0));
                Container<4> explicitValue
                    = new Container<4>(new Box<4>(0));
                return value.Read() + explicitValue.Read();
            }
            """), new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(System.Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new System.InvalidOperationException(
                "The type default using an earlier constant did not bind.");
        Assert.That(NativeJit.Execute(program,
            (NativeRuntimePolicy)policy), Is.EqualTo(7));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void ForwardsEarlierConstantFormalsIntoLaterDefaults(
        NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Values<const int32 First = 3,
                const int32 Second = First>
            {
                int32 Value;
                public Values(int32 value) { this.Value = value; }
                public int32 Read() {
                    return First * 10 + Second + this.Value;
                }
            }
            int32 Main() {
                Values<default, default> defaults
                    = new Values<default, default>(0);
                Values<4> explicitFirst = new Values<4>(0);
                return defaults.Read() + explicitFirst.Read();
            }
            """), new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(System.Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new System.InvalidOperationException(
                "The forwarded constant default did not bind.");
        Assert.That(NativeJit.Execute(program,
            (NativeRuntimePolicy)policy), Is.EqualTo(77));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void SubstitutesOpenWrapperDomainsInSourceGenericInstances(
        NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Transparent)]
            struct Tagged<T> {
                public int32 Value;
            }
            struct Config<const Tagged<T> Marker, T = int32> {
                public int32 Value;
            }
            int32 Accept<const Tagged<int32> Marker>(
                Config<Marker> value,
                Config<Marker, default> defaulted) {
                return 0;
            }
            int32 Main() { return 0; }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
            LayoutCoreRegistration =
                LayoutCoreRegistrationTestData.Create(),
        });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(System.Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new System.InvalidOperationException(
                "The source generic instance did not preserve its forwarded wrapper domain.");
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(0));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void AppliesEarlierTypeDefaultsInOpenGenericTypeTerms(
        NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Pair<T, U = T> {
                public T First;
                public U Second;
            }
            int32 Accept<T>(Pair<T> value) {
                return 0;
            }
            int32 Main() { return 0; }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
        });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(System.Environment.NewLine,
                compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }

    [Test]
    public void VerifiesOpenWrapperDomainsAndCallerMappings()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Transparent)]
            struct Tagged<T> { public int32 Value; }
            [Blittable] [Layout(LayoutKind.Transparent)]
            struct OtherTagged<T> { public int32 Value; }
            struct Cell<const int32 Count> { public int32 Value; }
            struct Config<const Tagged<Cell<7>> Marker, T = int32> {
                public T Value;
            }
            int32 Accept<const Tagged<Cell<7>> Marker>(
                Config<Marker> value) {
                return 0;
            }
            int32 Other<const Tagged<Cell<7>> Marker>(
                Config<Marker> value) {
                return 0;
            }
            int32 Main() { return 0; }
            """), new CompilationOptions {
            LayoutCoreRegistration =
                LayoutCoreRegistrationTestData.Create(),
        });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(System.Environment.NewLine,
                compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new System.InvalidOperationException(
                "The source generic wrapper proof did not bind.");
        var definition = program.GenericMethodDefinitions.Single(
            static method => method.Name == "Accept");
        var proofProgram = definition.ProofProgram;
        var config = proofProgram.SourceGenericTypeDefinitions.Single(
            static type => type.Name == "Config");
        var instance = proofProgram.SourceGenericTypeInstances.Single(
            candidate => candidate.DefinitionIndex == config.DefinitionIndex);
        var other = proofProgram.SourceGenericTypeDefinitions.Single(
            static type => type.Name == "OtherTagged");
        var argument = instance.ConstantArguments.Single();
        var rewrittenInstance = BoundTreeTraversal.RewriteTypes(instance,
            static type => type == PrimitiveType.Int32
                ? PrimitiveType.UInt32 : type);
        Assert.That(rewrittenInstance.TypeArguments,
            Is.EqualTo([PrimitiveType.UInt32]));
        var rewrittenArgument = rewrittenInstance.ConstantArguments.Single();
        var rewrittenCell = rewrittenArgument.Domain.TypeArguments.Single();
        Assert.That(rewrittenCell.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.SourceGeneric));
        var rewrittenCellCount = rewrittenCell.ConstantArguments.Single();
        Assert.That(rewrittenCellCount.Bits.Low, Is.EqualTo(7));
        Assert.That(rewrittenCellCount.DomainType,
            Is.EqualTo(PrimitiveType.UInt32));
        Assert.That(rewrittenCellCount.CarrierType,
            Is.EqualTo(PrimitiveType.UInt32));
        Assert.That(rewrittenArgument.CallerDefinitionOrdinal,
            Is.EqualTo(argument.CallerDefinitionOrdinal));
        Assert.That(rewrittenArgument.CallerFormalOrdinal,
            Is.EqualTo(argument.CallerFormalOrdinal));

        BoundProgram WithConstantArgument(
            BoundGenericMethodConstantArgument replacement)
        {
            var malformedProof = proofProgram with {
                SourceGenericTypeInstances = [
                    .. proofProgram.SourceGenericTypeInstances.Select(
                        candidate => candidate.StructureIndex
                            == instance.StructureIndex
                            ? candidate with {
                                ConstantArguments = [replacement],
                            }
                            : candidate),
                ],
            };
            return program with {
                GenericMethodDefinitions = [
                    .. program.GenericMethodDefinitions.Select(method =>
                        method.DefinitionOrdinal
                            == definition.DefinitionOrdinal
                            ? method with {
                                ProofProgram = malformedProof,
                            }
                            : method),
                ],
            };
        }

        var wrongDomainArgument = argument with {
            Domain = instance.ConstantArguments.Single().Domain with {
                SourceGenericTypeDefinitionIndex = other.DefinitionIndex,
            },
        };
        Assert.That(SemanticVerifier.TryVerifySource(
            WithConstantArgument(wrongDomainArgument),
            out _, out _, out _, out _), Is.False);
        Assert.That(SemanticVerifier.TryVerifySource(
            WithConstantArgument(argument with {
                CallerDefinitionOrdinal = int.MaxValue,
            }), out _, out _, out _, out _), Is.False);
        var otherMethod = program.GenericMethodDefinitions.Single(
            static method => method.Name == "Other");
        Assert.That(SemanticVerifier.TryVerifySource(
            WithConstantArgument(argument with {
                CallerDefinitionOrdinal = otherMethod.DefinitionOrdinal,
            }), out _, out _, out _, out _), Is.False);
        Assert.That(SemanticVerifier.TryVerifySource(
            WithConstantArgument(argument with {
                CallerFormalOrdinal = int.MaxValue,
                CallerConstantFormalOrdinal = int.MaxValue,
            }), out _, out _, out _, out _), Is.False);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void PropagatesSourceGenericOwnerConstantsIntoGenericMethods(
        NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Cell<const int32 Seed> {
                public int32 Value;
                public Cell(int32 value) { this.Value = value; }
                public int32 Add<const int32 Amount = Seed>(
                    int32 value) {
                    return Seed + Amount + value;
                }
            }
            int32 Main() {
                Cell<3> cell = new Cell<3>(1);
                return cell.Add<default>(1) + cell.Add<4>(1);
            }
            """), new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(System.Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new System.InvalidOperationException(
                "The generic owner constant method did not bind.");
        var method = program.GenericMethodDefinitions.Single(
            static definition => definition.Name.Contains(
                "Add", System.StringComparison.Ordinal));
        Assert.That(method.Owner?.ConstantArguments.Single().Bits.Low,
            Is.EqualTo(3));
        Assert.That(NativeJit.Execute(program,
            (NativeRuntimePolicy)policy), Is.EqualTo(15));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void SpecializesGenericStructsWithBooleanEnumAndWrapperConstants(
        NativeRuntimePolicy policy)
    {
        var options = CoreEvaluationFormTestHelpers.WithConstEval(
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        var compilation = Compilation.Create(SyntaxTree.Parse("""
                internal enum Mode : int32 { Off = 0, On = 1 }
            [Blittable] [Layout(LayoutKind.Transparent)]
            struct Flag {
                public bool Value;
                [ConstEval]
                public Flag(bool value) { Value = value; }
                [ConstEval]
                public static Flag Create(bool value) {
                    return new Flag(value);
                }
            }
            struct Settings<const bool Enabled, const Mode Selected,
                const Flag Wrapped>
            {
                int32 Value;
                public Settings(int32 value) { this.Value = value; }
                public int32 Read() {
                    return Enabled && Wrapped.Value ? this.Value : 0;
                }
            }
            int32 Main() {
                Settings<true, Mode.On, (Flag.Create(true))> enabled
                    = new Settings<true, Mode.On, (Flag.Create(true))>(42);
                Settings<true, Mode.On, (Flag.Create(false))> disabled
                    = new Settings<true, Mode.On, (Flag.Create(false))>(5);
                return enabled.Read() + disabled.Read();
            }
            """), options);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(System.Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new System.InvalidOperationException(
                "The boolean/enum/wrapper generic constants did not bind.");
        var definition = program.SourceGenericTypeDefinitions.Single(
            static candidate => candidate.Name == "Settings");
        var instances = program.SourceGenericTypeInstances
            .Where(instance => instance.DefinitionIndex
                == definition.DefinitionIndex)
            .ToArray();
        Assert.That(instances, Has.Length.EqualTo(2));
        Assert.That(instances.All(static instance =>
                instance.ConstantArguments[0].DomainType
                    == PrimitiveType.Bool
                && BoundEnumTypes.IsType(
                    instance.ConstantArguments[1].DomainType)),
            Is.True);
        var flag = program.Structures.Single(static structure
            => structure.Name == "Flag");
        Assert.That(instances.All(instance =>
                instance.ConstantArguments[2].DomainType
                    == (PrimitiveType)(100
                        + program.Structures.IndexOf(flag))),
            Is.True);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void PreservesConstantArgumentsInGenericStructWrapperDomains(
        NativeRuntimePolicy policy)
    {
        var options = CoreEvaluationFormTestHelpers.WithConstEval(
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Transparent)]
            struct Flag<const int32 Kind>
            {
                public bool Value;
                [ConstEval]
                public Flag(bool value) { Value = value; }
            }
            [ConstEval]
            Flag<7> CreateFlag(bool value) {
                return new Flag<7>(value);
            }
            bool Check<const Flag<7> Marker>(int32 input) {
                return input == 0;
            }
            int32 Main() {
                return Check<(CreateFlag(true))>(0) ? 0 : 1;
            }
            """), options);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(System.Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new System.InvalidOperationException(
                "The generic Transparent wrapper domain did not bind.");
        var definition = program.GenericMethodDefinitions.Single(
            static candidate => candidate.Name.Contains("Check",
                System.StringComparison.Ordinal));
        var domain = definition.ConstantFormals.Single().Domain;
        Assert.That(domain.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.SourceGeneric));
        Assert.That(domain.ConstantArguments.Single().Bits.Low,
            Is.EqualTo(7));
        var specialization = program.Functions.Single(function =>
            function.GenericMethodInstantiation?.DefinitionOrdinal
                == definition.DefinitionOrdinal);
        var sourceDefinition = program.SourceGenericTypeDefinitions.Single(
            static candidate => candidate.Name == "Flag");
        var flagInstance = program.SourceGenericTypeInstances.Single(
            instance => instance.DefinitionIndex
                    == sourceDefinition.DefinitionIndex
                && instance.ConstantArguments.Single().Bits.Low == 7);
        Assert.That(specialization.GenericMethodInstantiation!
                .ConstantArguments.Single().DomainType,
            Is.EqualTo((PrimitiveType)(100
                + flagInstance.StructureIndex)));
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(0));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void RejectsDifferentConstantArgumentsInGenericStructWrapperDomains(
        NativeRuntimePolicy policy)
    {
        var options = CoreEvaluationFormTestHelpers.WithConstEval(
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Transparent)]
            struct Flag<const int32 Kind>
            {
                public bool Value;
                [ConstEval]
                public Flag(bool value) { Value = value; }
            }
            [ConstEval]
            Flag<8> CreateOtherFlag(bool value) {
                return new Flag<8>(value);
            }
            bool Check<const Flag<7> Marker>(int32 input) {
                return input == 0;
            }
            int32 Main() {
                return Check<(CreateOtherFlag(true))>(0) ? 0 : 1;
            }
            """), options);

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True,
            string.Join(System.Environment.NewLine,
                compilation.Diagnostics));
    }

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void SubstitutesGenericStructConstantsIntoFixedArrayExtents(
        BoundRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct Buffer<const uint64 Length>
            {
                public [int32; Length] Values;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct Wrapper<const uint64 Length,
                const uint64 CopiedLength = Length>
            {
                public Buffer<CopiedLength> Value;
            }
            int32 Main() {
                return int32(sizeof(Buffer<4>))
                    + int32(sizeof(Buffer<(2 + 2)>))
                    + int32(sizeof(Wrapper<4>));
            }
            """), new CompilationOptions {
                RuntimePolicy = policy,
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(System.Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new System.InvalidOperationException(
                "The fixed-array generic extent did not bind.");
        var bufferDefinition = program.SourceGenericTypeDefinitions.Single(
            static definition => definition.Name == "Buffer");
        var wrapperDefinition = program.SourceGenericTypeDefinitions.Single(
            static definition => definition.Name == "Wrapper");
        var bufferInstance = program.SourceGenericTypeInstances.Single(
            instance => instance.DefinitionIndex
                == bufferDefinition.DefinitionIndex);
        var wrapperInstance = program.SourceGenericTypeInstances.Single(
            instance => instance.DefinitionIndex
                == wrapperDefinition.DefinitionIndex);
        var buffer = program.Structures[bufferInstance.StructureIndex];
        var wrapper = program.Structures[wrapperInstance.StructureIndex];
        var fixedArray = program.NativeFixedArrayTypes.Single();
        Assert.That(fixedArray.ElementCount, Is.EqualTo(4));
        Assert.That(buffer.Fields.Single().Type,
            Is.EqualTo(NativeFixedArrayTypes.Type(0)));
        Assert.That(wrapper.Fields.Single().Type,
            Is.EqualTo((PrimitiveType)(100
                + program.Structures.IndexOf(buffer))));
        Assert.That(wrapperInstance.ConstantArguments.Select(
                static argument => argument.Bits.Low),
            Is.EqualTo([4UL, 4UL]));
        Assert.That(buffer.Size, Is.EqualTo(16));
        Assert.That(wrapper.Size, Is.EqualTo(16));
        Assert.That(NativeJit.Execute(program,
            (NativeRuntimePolicy)policy), Is.EqualTo(48));
    }

    [Test]
    public void AppliesGenericStructOwnershipAndBlittableFormalDomains()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct Bits { public int32 Value; }
            struct BitCell<T> where T : [Blittable] { public T Value; }
            struct UniqueMarker<T> where T : unique { public int32 Value; }
            struct FlexibleMarker<T> where T : unique? { public int32 Value; }
            struct NullableMarker<T> where T : null? { public int32 Value; }
            unique struct Owner { public int32 Value; }
            class Node { public int32 Value; public Node() { Value = 0; } }

            void ReadBits(BitCell<int32> value) { }
            void ReadStructBits(BitCell<Bits> value) { }
            void ReadUnique(UniqueMarker<Owner> value) { }
            void ReadFlexibleCopy(FlexibleMarker<int32> value) { }
            void ReadFlexibleUnique(FlexibleMarker<Owner> value) { }
            void ReadNullable(NullableMarker<Node?> value) { }
            int32 Main() { return 0; }
            """), new CompilationOptions {
                LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
            });

        Assert.That(compilation.Diagnostics, Is.Empty);
    }

    [Test]
    public void AppliesGenericMethodOwnershipDomainsToUniqueClassHandles()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            unique class Owner {
                public int32 Value;
                public Owner(int32 value) { Value = value; }
            }
            public T Transfer<T>(T value) where T : unique {
                return move value;
            }
            int32 Main() {
                Owner owner = new Owner(42);
                mut Owner moved = Transfer<Owner>(move owner);
                moved = new Owner(43);
                return moved.Value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(System.Environment.NewLine,
                compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(program.Classes.Single().IsUnique, Is.True);

        var copyCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            unique class Owner {
                public int32 Value;
                public Owner(int32 value) { Value = value; }
            }
            public T Copy<T>(T value) { return value; }
            int32 Main() {
                Owner owner = new Owner(42);
                Owner copy = Copy<Owner>(move owner);
                return copy.Value;
            }
            """));
        Assert.That(copyCompilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("ownership domain",
                    System.StringComparison.Ordinal)), Is.True,
            string.Join(System.Environment.NewLine,
                copyCompilation.Diagnostics));
    }

    [Test]
    public void VerifiesSourceGenericDefinitionAndInstanceIdentity()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Box<T> { public T Value; }
            int32 Read(Box<int32> value) { return value.Value; }
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Diagnostics, Is.Empty);
        var program = compilation.Program!;
        var instance = program.SourceGenericTypeInstances.Single();
        _ = Assert.Throws<System.ArgumentException>(() =>
            SemanticVerifier.Verify(program with {
                SourceGenericTypeInstances = [],
            }));
        _ = Assert.Throws<System.ArgumentException>(() =>
            SemanticVerifier.Verify(program with {
                SourceGenericTypeInstances = [
                    instance with {
                        TypeArguments = [PrimitiveType.Void],
                    },
                ],
            }));
    }

    [Test]
    public void RewritesSourceGenericActualsWithNormalizedNativeTypes()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct PointerBox<T> { public T Value; }
            unsafe void Use(PointerBox<ptr<int32>> value) { }
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Diagnostics, Is.Empty);
        var program = compilation.Program!;
        var instance = program.SourceGenericTypeInstances.Single();
        var fieldType = program.Structures[instance.StructureIndex]
            .Fields.Single().Type;
        Assert.That(instance.TypeArguments, Is.EqualTo([fieldType]));
        Assert.That(NativePointerTypeContract.IsValidType(fieldType,
            program.NativePointerTypes), Is.True);
    }

    [TestCase("struct CopyCell<T> { public T Value; } unique struct Owner { public int32 Value; } void Use(CopyCell<Owner> value) { } int32 Main() { return 0; }")]
    [TestCase("struct UniqueMarker<T> where T : unique { public int32 Value; } void Use(UniqueMarker<int32> value) { } int32 Main() { return 0; }")]
    [TestCase("unique struct Owner { public int32 Value; } struct CopyCell<T = Owner> { public T Value; } void Use(CopyCell value) { } int32 Main() { return 0; }")]
    public void RejectsGenericStructArgumentsOutsideTheirOwnershipDomain(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("ownership domain",
                    System.StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void RejectsGenericStructArgumentsThatDoNotSatisfyBlittableConstraint()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct NotBlittable { public int32 Value; }
            struct BitCell<T> where T : [Blittable] { public T Value; }
            void Use(BitCell<NotBlittable> value) { }
            int32 Main() { return 0; }
            """), new CompilationOptions {
                LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
            });

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("Blittable constraint",
                    System.StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void RejectsNullableGenericStructArgumentsWithoutNullabilityDomain()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Node { public int32 Value; public Node() { Value = 0; } }
            struct NonNullableMarker<T> { public int32 Value; }
            void Use(NonNullableMarker<Node?> value) { }
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("nullability domain",
                    System.StringComparison.Ordinal)), Is.True);
    }

    [TestCase("struct Cell<T = U, U = int32> { public T First; public U Second; } int32 Read(Cell value) { return value.Second; } int32 Main() { return 0; }")]
    [TestCase("struct Cell<T = T> { public T Value; } int32 Read(Cell value) { return value.Value; } int32 Main() { return 0; }")]
    public void RejectsGenericStructDefaultsThatReferenceCurrentOrForwardFormals(
        string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True);
    }

    [TestCase("public struct Box<T> { T value; }")]
    [TestCase("class Box<T> { int32 value; }")]
    [TestCase("interface I<T> {} struct Box<T> : I<T> { T value; }")]
    [TestCase("interface I<T> where T : unique { }")]
    [TestCase("struct Box<T> where T : I<T> { T value; }")]
    [TestCase("struct Box<T> where T : write { T value; }")]
    public void RejectsGenericNominalFormsOutsideTheClosedStructProfile(string declaration)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            declaration + " int32 Main() { return 0; }"));

        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3005"), Is.True);
    }

    [TestCase("Box")]
    [TestCase("Box<int32, uint32>")]
    public void RejectsMissingOrIncorrectGenericStructArity(string use)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            $"struct Box<T> {{ T value; }} int32 Read({use} item) {{ return 0; }} int32 Main() {{ return 0; }}"));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("requires its declared type argument count",
                    System.StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void DoesNotExportCompilationLocalGenericTypesThroughPublicCallables()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Box<T> { T value; public T Get() { return this.value; } }
            public int32 Read(Box<int32> value) { return value.Get(); }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("compilation-local generic struct",
                    System.StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void DoesNotExposeCompilationLocalGenericTypesThroughPublicMethods()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Box<T> { T value; public Box(T value) { this.value = value; } }
            public class Catalog
            {
                public Box<int32> Create() { return new Box<int32>(42); }
            }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("Public callables cannot expose a compilation-local generic struct",
                System.StringComparison.Ordinal)), Is.True);
    }

    [TestCase("struct Catalog { private Box<int32> value; }")]
    [TestCase("struct Inner { Box<int32> value; } struct Catalog { private Inner value; }")]
    [TestCase("class Catalog { private Box<int32> value; }")]
    public void DoesNotExposeCompilationLocalGenericTypesThroughPublicAggregateLayouts(string declaration)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"struct Box<T> {{ T value; }} public {declaration} int32 Main() {{ return 0; }}"));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("compilation-local generic struct",
                System.StringComparison.Ordinal)), Is.True);
    }
}
