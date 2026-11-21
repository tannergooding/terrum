using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Tests.Modules;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class PrivateRevision106BodyTests
{
    [Test]
    public void CheckedReferenceBodyOperationsRoundTripAndRejectMalformedPlaces()
    {
        const string source = """
            public int32 Read(ref<int32> value) { return *value; }
            public int32 Form() {
                mut int32 value = int32(40);
                return Read(ref value);
            }
            public void Set(write ref<int32> value) {
                *value = int32(7);
            }
            public bool IsReady() {
                init int32 value;
                return value is init;
            }
            internal struct Cell {
                public mut int32 Value;
                public Cell(int32 value) {
                    Value = value;
                }
            }
            public int32 SetField(int32 initial) {
                mut Cell cell = new Cell(initial);
                write ref<Cell> borrowedCell = ref cell;
                (*borrowedCell).Value = int32(42);
                return cell.Value;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var cellType = (PrimitiveType)(100
            + global::System.Linq.Enumerable.Single(
                global::System.Linq.Enumerable.Select(
                    program.Structures,
                    static (structure, index) => (structure, index)),
                static item => item.structure.Name == "Cell").index);
        var exports = new[] {
            "Read", "Form", "Set", "IsReady", "SetField",
        };
        var identityInputs = PrivateModuleIdentityInputs.Create(program,
            "provider",
            [new PrivateNominalDefinition(cellType,
                "types.cell", IsExported: false)], []);
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(program, exports));
        var producer = ModuleCompiler.CreateProducer(program, exports,
            identityInputs);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            producer.AsSpan(sizeof(int), sizeof(int))), Is.EqualTo(106));
        _ = ModuleCompiler.CreateReference(producer);

        var formOffset = FindEnvelopeOffset(producer, 49,
            source.IndexOf("ref value", StringComparison.Ordinal),
            "ref value".Length);
        var readOffset = FindEnvelopeOffset(producer, 50,
            source.IndexOf("*value", StringComparison.Ordinal),
            "*value".Length);
        var storeOffset = FindEnvelopeOffset(producer, 53, -1, -1);
        var initializedText = "value is init";
        var initializedOffset = FindEnvelopeOffset(producer, 51,
            source.IndexOf(initializedText, StringComparison.Ordinal),
            initializedText.Length);

        Assert.That(formOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(readOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(storeOffset, Is.GreaterThanOrEqualTo(0));
        var aliasStoreText = "(*borrowedCell).Value = int32(42);";
        var aliasStoreOffset = FindEnvelopeOffset(producer, 53,
            source.IndexOf(aliasStoreText, StringComparison.Ordinal),
            aliasStoreText.Length);
        Assert.That(aliasStoreOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(initializedOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(producer[formOffset + 14], Is.EqualTo(1));
        Assert.That(producer[readOffset + 13], Is.EqualTo(1));
        Assert.That(producer[readOffset + 14], Is.EqualTo(4));
        Assert.That(producer[readOffset + 15], Is.EqualTo(50));
        Assert.That(producer[readOffset + 28], Is.EqualTo(3));
        Assert.That(producer[storeOffset + 13], Is.EqualTo(2));
        Assert.That(producer[storeOffset + 14], Is.EqualTo(4));
        Assert.That(producer[storeOffset + 28], Is.EqualTo(3));

        var malformed = (byte[])producer.Clone();
        malformed[formOffset + 14] = 0xff;
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));

        malformed = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(
            malformed.AsSpan(formOffset + 15, sizeof(int)), int.MaxValue);
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));

        malformed = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(
            malformed.AsSpan(formOffset + 1, sizeof(int)),
            (int)PrimitiveType.Bool);
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));
    }

    [Test]
    public void PF111SelectionIsExactAndPreservesOrdinaryModuleBaseline()
    {
        const string schemaIdentity
            = "terrum/private-semantic-111-foreign-native-interfaces-1";
        const string providerSource = """
            public struct Cell {
                public int32 Value;
                public Cell(int32 value) { Value = value; }
            }

            public int32 Read(Cell value) { return value.Value; }
            """;
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program
            ?? throw new AssertionException("Expected a bound provider.");
        var defaultProducer = ModuleCompiler.CreateProducer(providerProgram,
            ["Read"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
                defaultProducer.AsSpan(sizeof(int), sizeof(int))),
            Is.AnyOf(104, 106));

        var producer = ModuleCompiler.CreateProducerWithSchema(
            providerProgram, ["Read"], schemaIdentity);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
                producer.AsSpan(sizeof(int), sizeof(int))),
            Is.EqualTo(111));
        var reference = ModuleCompiler.CreateReference(producer);
        var sourceCell = providerProgram.Structures.Single(
            static structure => structure.Name == "Cell");
        var referenceCell = reference.Structures.Single(
            static structure => structure.Name == "Cell");
        Assert.That(referenceCell.DeclaredCopyEvidence,
            Is.EqualTo(sourceCell.DeclaredCopyEvidence == true));

        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                int32 Main() { return Read(new Cell(41)); }
                """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new AssertionException("Expected a bound consumer.");
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateConsumer(consumerProgram, producer));
        var consumer = ModuleCompiler.CreateConsumerWithSchema(
            consumerProgram, producer, schemaIdentity);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
                consumer.AsSpan(sizeof(int), sizeof(int))),
            Is.EqualTo(111));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateConsumerWithSchema(consumerProgram,
                defaultProducer, schemaIdentity));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducerWithSchema(providerProgram,
                ["Read"], "terrum/private-semantic-111-foreign-native-interfaces"));
    }

    [Test]
    public void PF111ReferenceSetsRequireOneExactRevision()
    {
        const string schemaIdentity
            = "terrum/private-semantic-111-foreign-native-interfaces-1";
        var firstCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 First() { return 1; }"));
        Assert.That(firstCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, firstCompilation.Diagnostics));
        var firstProgram = firstCompilation.Program
            ?? throw new AssertionException("Expected a first provider.");
        var secondCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Second() { return 2; }"));
        Assert.That(secondCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, secondCompilation.Diagnostics));
        var secondProgram = secondCompilation.Program
            ?? throw new AssertionException("Expected a second provider.");
        var first = ModuleCompiler.CreateProducerWithSchema(firstProgram,
            ["First"], schemaIdentity);
        var second = ModuleCompiler.CreateProducerWithSchema(secondProgram,
            ["Second"], schemaIdentity);
        var reference = ModuleCompiler.CreateReferenceSetWithSchema(
            [first, second], schemaIdentity);
        Assert.That(reference.Functions.Select(static function => function.Name)
            .OrderBy(static name => name, StringComparer.Ordinal),
            Is.EqualTo(new[] { "First", "Second" }));

        var legacySecond = ModuleCompiler.CreateProducer(secondProgram,
            ["Second"]);
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateReferenceSet([first, legacySecond]));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateReferenceSetWithSchema([first, legacySecond],
                schemaIdentity));
    }

    [Test]
    public void PF111NativePackageSetAndGraphSelectorsPreserveExactRevision()
    {
        const string schemaIdentity
            = "terrum/private-semantic-111-foreign-native-interfaces-1";
        const NativeRuntimePolicy runtimePolicy = NativeRuntimePolicy.FailFast;
        var firstCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Read(int32 value) { return value + 1; }"));
        Assert.That(firstCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, firstCompilation.Diagnostics));
        var firstProgram = firstCompilation.Program
            ?? throw new AssertionException("Expected the first provider.");
        var firstPackage = NativeModuleCompiler.CreateProducerWithSchema(
            firstProgram, ["Read"], "PF111First", schemaIdentity,
            runtimePolicy);
        var firstReference = NativeModuleCompiler.CreateReferenceWithSchema(
            firstPackage, schemaIdentity);
        var directConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Read(41); }"),
            firstReference);
        Assert.That(directConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                directConsumerCompilation.Diagnostics));
        var directConsumerProgram = directConsumerCompilation.Program
            ?? throw new AssertionException(
                "Expected the direct native consumer.");
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.CreateConsumer(directConsumerProgram,
                firstPackage, runtimePolicy));
        var directConsumer = NativeModuleCompiler.CreateConsumerWithSchema(
            directConsumerProgram, firstPackage, schemaIdentity,
            runtimePolicy);
        NativeModuleCompiler.Validate(directConsumer, firstPackage);

        var secondCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Double(int32 value) { return value * 2; }"));
        Assert.That(secondCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, secondCompilation.Diagnostics));
        var secondPackage = NativeModuleCompiler.CreateProducerWithSchema(
            secondCompilation.Program
                ?? throw new AssertionException("Expected the second provider."),
            ["Double"], "PF111Second", schemaIdentity, runtimePolicy);
        var referenceSet = NativeModuleCompiler.CreateReferenceSetWithSchema(
            [firstPackage, secondPackage], schemaIdentity);
        var artifactReferenceSet = NativeModuleCompiler.CreateReferenceSet(
            [firstPackage, secondPackage]);
        Assert.That(artifactReferenceSet.Functions.Select(
                static function => function.Name).OrderBy(static name => name,
                StringComparer.Ordinal),
            Is.EqualTo(new[] { "Double", "Read" }));
        Assert.That(referenceSet.Functions.Select(static function =>
                function.Name).OrderBy(static name => name,
                StringComparer.Ordinal),
            Is.EqualTo(new[] { "Double", "Read" }));
        var legacySecondPackage = NativeModuleCompiler.CreateProducer(
            secondCompilation.Program
                ?? throw new AssertionException("Expected the second provider."),
            ["Double"], "PF111Legacy", runtimePolicy);
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.CreateReferenceSet(
                [firstPackage, legacySecondPackage]));
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.CreateReferenceSetWithSchema(
                [firstPackage, legacySecondPackage], schemaIdentity));
        var setConsumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Read(Double(20)); }"), referenceSet);
        Assert.That(setConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                setConsumerCompilation.Diagnostics));
        var setConsumerProgram = setConsumerCompilation.Program
            ?? throw new AssertionException(
                "Expected a set consumer program.");
        var setConsumer = NativeModuleCompiler.CreateConsumerSetWithSchema(
            setConsumerProgram,
            [firstPackage, secondPackage], schemaIdentity, runtimePolicy);
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.CreateConsumerSet(
                setConsumerProgram,
                [firstPackage, secondPackage], runtimePolicy));
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.CreateConsumerSetWithSchema(
                setConsumerProgram, [firstPackage, legacySecondPackage],
                schemaIdentity, runtimePolicy));
        NativeModuleCompiler.ValidateConsumerSet(setConsumer,
            [firstPackage, secondPackage]);

        var graphProducerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(
                "public int32 Use(int32 value) { return Read(value); }"),
            firstReference);
        Assert.That(graphProducerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                graphProducerCompilation.Diagnostics));
        var graphProducerProgram = graphProducerCompilation.Program
            ?? throw new AssertionException("Expected a graph producer.");
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.CreateGraphProducer(graphProducerProgram,
                ["Use"], "PF111GraphUnselected", [firstPackage],
                runtimePolicy));
        var graphProducer = NativeModuleCompiler
            .CreateGraphProducerWithSchema(
                graphProducerProgram, ["Use"], "PF111Graph",
                [firstPackage], schemaIdentity, runtimePolicy);
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.CreateGraphReferenceWithSchema(
                [graphProducer, firstPackage, legacySecondPackage],
                schemaIdentity));
        var graphReference = NativeModuleCompiler
            .CreateGraphReferenceWithSchema(
                [graphProducer, firstPackage], schemaIdentity);
        var artifactGraphReference = NativeModuleCompiler.CreateGraphReference(
            [graphProducer, firstPackage]);
        Assert.That(artifactGraphReference.Functions.Select(
                static function => function.Name).OrderBy(static name => name,
                StringComparer.Ordinal),
            Is.EqualTo(new[] { "Read", "Use" }));
        var graphConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Use(41); }"),
            graphReference);
        Assert.That(graphConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                graphConsumerCompilation.Diagnostics));
        var graphConsumerProgram = graphConsumerCompilation.Program
            ?? throw new AssertionException("Expected a graph consumer.");
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.CreateGraphConsumer(graphConsumerProgram,
                [graphProducer, firstPackage], runtimePolicy));
        var graphConsumer = NativeModuleCompiler
            .CreateGraphConsumerWithSchema(
                graphConsumerProgram, [graphProducer, firstPackage],
                schemaIdentity, runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer,
            [graphProducer, firstPackage]);
    }

    [Test]
    public void GeneralDependencyRowsRoundTripAndConstrainProviderReplacement()
    {
        static byte[] CreateProvider(string dependencyAttribute)
        {
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
                $$"""
                public struct Receiver {
                    private int32 Value;
                    {{dependencyAttribute}}
                    public static int32 Independent() { return int32(0); }
                }
                """));
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            if (!string.IsNullOrEmpty(dependencyAttribute))
            {
                Assert.That(global::System.Linq.Enumerable.Single(
                        compilation.Program!.Functions,
                        static function =>
                            function.Name == "Receiver.Independent")
                        .DependencyContracts, Is.Not.Empty);
            }
            var producer = ModuleCompiler.CreateProducer(
                compilation.Program!, ["Receiver.Independent"]);
            if (!string.IsNullOrEmpty(dependencyAttribute))
            {
                Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
                    producer.AsSpan(sizeof(int), sizeof(int))),
                    Is.EqualTo(106));
            }
            return producer;
        }

        const string consumerSource = """
            public int32 Main() {
                return Receiver.Independent();
            }
            """;
        var wideProvider = CreateProvider("[return: DependsOn()]");
        var wideReference = ModuleCompiler.CreateReference(wideProvider);
        var persistedFunction = global::System.Linq.Enumerable.Single(
            wideReference.Functions,
            static function => function.Name == "Receiver.Independent");
        Assert.That(persistedFunction.DependencyContracts, Has.Length.EqualTo(1));
        var persistedDependency =
            global::System.Linq.Enumerable.Single(
                persistedFunction.DependencyContracts);
        Assert.That(persistedDependency.Relation,
            Is.EqualTo(BoundDependencyRelation.DependsOn));
        Assert.That(persistedDependency.Destination,
            Is.EqualTo(BoundDependencyDestination.Result));
        Assert.That(persistedDependency.DestinationOrdinal, Is.Null);
        Assert.That(persistedDependency.Sources, Is.Empty);
        var wideConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse(consumerSource), wideReference);
        Assert.That(wideConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                wideConsumerCompilation.Diagnostics));
        var wideConsumer = ModuleCompiler.CreateConsumer(
            wideConsumerCompilation.Program!, wideProvider);
        ModuleCompiler.ValidateConsumerAgainstReference(wideConsumer,
            wideReference);
        var neutralCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Neutral() { return 0; }
            """));
        Assert.That(neutralCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, neutralCompilation.Diagnostics));
        var neutralProvider = ModuleCompiler.CreateProducer(
            neutralCompilation.Program!, ["Neutral"]);
        byte[][] providerSet = [wideProvider, neutralProvider];
        var wideConsumerSet = ModuleCompiler.CreateConsumerSet(
            wideConsumerCompilation.Program!, providerSet);
        ModuleCompiler.ValidateConsumerSet(wideConsumerSet, providerSet);

        var unannotatedProvider = CreateProvider(string.Empty);
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.ValidateConsumerAgainstReference(wideConsumer,
                ModuleCompiler.CreateReference(unannotatedProvider)));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.ValidateConsumerSet(wideConsumerSet,
                [unannotatedProvider, neutralProvider]));

        var graphCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public int32 Wrapper() {
                    return Receiver.Independent();
                }
                """), wideReference);
        Assert.That(graphCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                graphCompilation.Diagnostics));
        var graphProgram = graphCompilation.Program!;
        var graph = ModuleCompiler.CreateGraphLibrary(graphProgram,
            ["Wrapper"], wideReference);
        _ = ModuleCompiler.CreateGraphReference(graph, ["Wrapper"]);
    }

    [Test]
    public void GeneralDependencyStorageOnlyRelationshipRoundTrips()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.CodeAnalysis;
            [return: DependsOn(nameof(value))]
            public int32 Select(int32 ignored, int32 value) {
                return value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var producer = ModuleCompiler.CreateProducer(
            compilation.Program!, ["Select"]);
        var storageOnlyProducer
            = RewritePrivateGeneralDependencyRelationship(producer, [
                1, 1, 3, 1, 0, 0, 0, 0, 1, 0, 0, 0,
            ], 1);
        var reference = ModuleCompiler.CreateReference(storageOnlyProducer);
        var function = global::System.Linq.Enumerable.Single(
            reference.Functions,
            static item => item.Name == "Select");
        Assert.That(function.DependencyContracts, Has.Length.EqualTo(2));
        Assert.That(function.DependencyContracts[0].Relation,
            Is.EqualTo(BoundDependencyRelation.DependsOn));
        Assert.That(function.DependencyContracts[1].Relation,
            Is.EqualTo(BoundDependencyRelation.BorrowSourceStorage));
        Assert.That(function.DependencyContracts[1].Sources.Single(),
            Is.EqualTo(new BoundDependencySource(
                BoundDependencySourceKind.Formal, 1)));
    }

    [Test]
    public void GeneralDependencyCompatibilityAllowsOnlySourceNarrowing()
    {
        static BoundDependencySource Formal(int ordinal)
        {
            return new(BoundDependencySourceKind.Formal, ordinal);
        }

        static BoundDependencyContract Dependency(
            BoundDependencyRelation relation, params int[] sourceOrdinals)
        {
            return new(relation, BoundDependencyDestination.Result, null,
                        [.. sourceOrdinals.Select(Formal)]);
        }

        const string contract = "Select|4|F|E|S|-|E|2|4|4";
        var broad = ImmutableArray.Create(
            Dependency(BoundDependencyRelation.DependsOn, 0, 1));
        var narrowed = ImmutableArray.Create(
            Dependency(BoundDependencyRelation.DependsOn, 0));
        Assert.That(ModuleCompiler.PrivateGeneralDependencyContractsSatisfy(
            broad, narrowed, contract), Is.True);
        Assert.That(ModuleCompiler.PrivateGeneralDependencyContractsSatisfy(
            narrowed, broad, contract), Is.False);

        var backingOnlyExpected = ImmutableArray.Create(
            Dependency(BoundDependencyRelation.DependsOn, 0, 1),
            Dependency(BoundDependencyRelation.ForwardsBacking, 0));
        var backingOnlyProvider = ImmutableArray.Create(
            Dependency(BoundDependencyRelation.DependsOn, 0),
            Dependency(BoundDependencyRelation.ForwardsBacking, 0));
        var unionProvider = ImmutableArray.Create(
            Dependency(BoundDependencyRelation.DependsOn, 0));
        Assert.That(ModuleCompiler.PrivateGeneralDependencyContractsSatisfy(
            backingOnlyExpected, backingOnlyProvider, contract), Is.True);
        Assert.That(ModuleCompiler.PrivateGeneralDependencyContractsSatisfy(
            backingOnlyExpected, unionProvider, contract), Is.False);

        var storageOnlyExpected = ImmutableArray.Create(
            Dependency(BoundDependencyRelation.DependsOn, 0, 1),
            Dependency(BoundDependencyRelation.BorrowSourceStorage, 0));
        var storageOnlyProvider = ImmutableArray.Create(
            Dependency(BoundDependencyRelation.DependsOn, 0),
            Dependency(BoundDependencyRelation.BorrowSourceStorage, 0));
        var excludesStorageSource = ImmutableArray.Create(
            Dependency(BoundDependencyRelation.DependsOn, 1));
        var forwardsStorageSource = ImmutableArray.Create(
            Dependency(BoundDependencyRelation.DependsOn, 0),
            Dependency(BoundDependencyRelation.ForwardsBacking, 0));
        var broadExpected = ImmutableArray.Create(
            Dependency(BoundDependencyRelation.DependsOn, 0, 1));
        Assert.That(ModuleCompiler.PrivateGeneralDependencyContractsSatisfy(
            storageOnlyExpected, storageOnlyProvider, contract), Is.True);
        Assert.That(ModuleCompiler.PrivateGeneralDependencyContractsSatisfy(
            storageOnlyExpected, excludesStorageSource, contract), Is.True);
        Assert.That(ModuleCompiler.PrivateGeneralDependencyContractsSatisfy(
            storageOnlyExpected, forwardsStorageSource, contract), Is.False);
        Assert.That(ModuleCompiler.PrivateGeneralDependencyContractsSatisfy(
            broadExpected, storageOnlyProvider, contract), Is.True);
        Assert.That(ModuleCompiler.PrivateGeneralDependencyContractsSatisfy(
            broadExpected, backingOnlyProvider, contract), Is.True);
        var conflictingRefinements = ImmutableArray.Create(
            Dependency(BoundDependencyRelation.DependsOn, 0),
            Dependency(BoundDependencyRelation.BorrowSourceStorage, 0),
            Dependency(BoundDependencyRelation.ForwardsBacking, 0));
        Assert.That(ModuleCompiler.PrivateGeneralDependencyContractsSatisfy(
            conflictingRefinements, conflictingRefinements, contract),
            Is.False);

        var explicitEmpty = ImmutableArray.Create(
            Dependency(BoundDependencyRelation.DependsOn));
        Assert.That(ModuleCompiler.PrivateGeneralDependencyContractsSatisfy(
            explicitEmpty, explicitEmpty, contract), Is.True);
        Assert.That(ModuleCompiler.PrivateGeneralDependencyContractsSatisfy(
            explicitEmpty, [], contract), Is.False);

        const string instanceContract
            = "Receiver.Select|4|F|E|I|Receiver|E|3|100|4|4";
        var instanceBroad = ImmutableArray.Create(
            new BoundDependencyContract(
                BoundDependencyRelation.DependsOn,
                BoundDependencyDestination.Result, null,
                [
                    new BoundDependencySource(
                        BoundDependencySourceKind.Receiver, null),
                    Formal(0),
                    Formal(1),
                ]));
        var instanceNarrow = ImmutableArray.Create(
            new BoundDependencyContract(
                BoundDependencyRelation.DependsOn,
                BoundDependencyDestination.Result, null,
                [
                    new BoundDependencySource(
                        BoundDependencySourceKind.Receiver, null),
                    Formal(0),
                ]));
        var invalidInstanceSource = ImmutableArray.Create(
            new BoundDependencyContract(
                BoundDependencyRelation.DependsOn,
                BoundDependencyDestination.Result, null,
                [Formal(2)]));
        Assert.That(ModuleCompiler.PrivateGeneralDependencyContractsSatisfy(
            instanceBroad, instanceNarrow, instanceContract), Is.True);
        Assert.That(ModuleCompiler.PrivateGeneralDependencyContractsSatisfy(
            instanceBroad, invalidInstanceSource, instanceContract), Is.False);
    }

    [Test]
    public async Task CheckedOutputDefaultsAndCapturesRoundTripAndRejectMalformedRowsAsync()
    {
        const string source = """
            [param(output): Captures(nameof(source))]
            public bool Initialize(bool publish, ref<init int32> output,
                write ref<int32> source) {
                if (!publish) {
                    return false;
                }
                *output = *source;
                return true;
            }
            public bool InitializeRuntime(bool publish,
                ref<init int32> output) {
                if (!publish) {
                    return false;
                }
                *output = int32(41);
                return true;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var producer = ModuleCompiler.CreateProducer(
            compilation.Program!, ["Initialize", "InitializeRuntime"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var function = global::System.Linq.Enumerable.Single(
            reference.Functions,
            static item => item.Name == "Initialize");
        var storageOnlyProducer = RewritePrivateGeneralDependencyRelationship(
            producer, [
                2, 1, 3, 2, 1, 0, 0, 0, 2, 0, 0, 0,
            ], 1);
        var storageOnlyFunction = global::System.Linq.Enumerable.Single(
            ModuleCompiler.CreateReference(storageOnlyProducer).Functions,
            static item => item.Name == "Initialize");
        Assert.That(storageOnlyFunction.DependencyContracts,
            Has.Length.EqualTo(2));
        Assert.That(storageOnlyFunction.DependencyContracts[0].Relation,
            Is.EqualTo(BoundDependencyRelation.Captures));
        Assert.That(storageOnlyFunction.DependencyContracts[1].Relation,
            Is.EqualTo(BoundDependencyRelation.BorrowSourceStorage));
        Assert.That(function.CheckedOutputSelectors,
            Is.EqualTo([
                new NativeFunctionPointerSelector(3, 0, 1),
                new NativeFunctionPointerSelector(3, 1, 2),
            ]));
        Assert.That(function.CheckedOutputStates,
            Is.EqualTo([
                new NativeFunctionPointerOutputState(1, 0, 1),
                new NativeFunctionPointerOutputState(1, 1, 0),
            ]));
        var capture = global::System.Linq.Enumerable.Single(
            function.DependencyContracts,
            static dependency =>
                dependency.Relation == BoundDependencyRelation.Captures);
        Assert.That(capture.Destination,
            Is.EqualTo(BoundDependencyDestination.InitializedOutput));
        Assert.That(capture.DestinationOrdinal, Is.EqualTo(1));
        var nativePackage = NativeModuleCompiler.CreateProducer(
            compilation.Program!, ["Initialize", "InitializeRuntime"],
            "output_state_provider",
            NativeRuntimePolicy.FailFast);
        var nativeReference =
            NativeModuleCompiler.CreateReference(nativePackage);
        var nativeFunction = global::System.Linq.Enumerable.Single(
            nativeReference.Functions,
            static item => item.Name == "Initialize");
        Assert.That(nativeFunction.CheckedOutputSelectors,
            Is.EqualTo(function.CheckedOutputSelectors));
        Assert.That(nativeFunction.CheckedOutputStates,
            Is.EqualTo(function.CheckedOutputStates));
        var nativeGraphReference =
            NativeModuleCompiler.CreateGraphReference([nativePackage]);
        var runtimeCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public bool InitializeRuntime(bool publish,
                ref<init int32> output) {
                if (!publish) { return false; }
                *output = int32(41);
                return true;
            }
            """));
        Assert.That(runtimeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                runtimeCompilation.Diagnostics));
        var runtimePackage = NativeModuleCompiler.CreateProducer(
            runtimeCompilation.Program!, ["InitializeRuntime"],
            "output_state_runtime", NativeRuntimePolicy.FailFast);
        var runtimeGraphReference =
            NativeModuleCompiler.CreateGraphReference([runtimePackage]);

        var trueSelector = new byte[] {
            3, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0,
        };
        var selectorOffset = FindBytesOffset(producer, trueSelector);
        Assert.That(selectorOffset, Is.GreaterThanOrEqualTo(sizeof(int)));
        var noSelectors = new byte[producer.Length - (2 * 12)];
        Array.Copy(producer, 0, noSelectors, 0, selectorOffset);
        Array.Copy(producer, selectorOffset + (2 * 12), noSelectors,
            selectorOffset, producer.Length - selectorOffset - (2 * 12));
        BinaryPrimitives.WriteInt32LittleEndian(
            noSelectors.AsSpan(selectorOffset - sizeof(int), sizeof(int)), 0);
        BinaryPrimitives.WriteInt32LittleEndian(
            noSelectors.AsSpan(selectorOffset, sizeof(int)), 0);
        var noOutputStates = new byte[noSelectors.Length - (2 * 12)];
        var outputStateRowsOffset = selectorOffset + sizeof(int);
        Array.Copy(noSelectors, 0, noOutputStates, 0,
            outputStateRowsOffset);
        Array.Copy(noSelectors, outputStateRowsOffset + (2 * 12),
            noOutputStates, outputStateRowsOffset,
            noSelectors.Length - outputStateRowsOffset - (2 * 12));
        var unconditionalReference =
            ModuleCompiler.CreateReference(noOutputStates);
        var unconditionalFunction =
            global::System.Linq.Enumerable.Single(
                unconditionalReference.Functions,
                static item => item.Name == "Initialize");
        Assert.That(unconditionalFunction.CheckedOutputSelectors,
            Is.Empty);
        Assert.That(unconditionalFunction.CheckedOutputStates, Is.Empty);
        Assert.That(
            unconditionalFunction.HasPrivateCheckedOutputStateMetadata,
            Is.True);

        var malformed = (byte[])producer.Clone();
        malformed[selectorOffset + 1] = 1;
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));

        malformed = (byte[])producer.Clone();
        var firstOutputStateOffset = selectorOffset + (2 * 12) + sizeof(int);
        malformed[firstOutputStateOffset + 9] = 1;
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));
        malformed = (byte[])producer.Clone();
        malformed[firstOutputStateOffset + 8] = 2;
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));

        const string consumerSource = """
            public int32 Main() {
                mut int32 backing = 41;
                write ref<int32> source = ref backing;
                init int32 captured;
                require Initialize(true, ref init captured, source);
                init int32 result;
                require InitializeRuntime(true, ref init result);
                return captured + result;
            }
            """;
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse(consumerSource), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, producer);
        ModuleCompiler.ValidateConsumerAgainstReference(consumer, reference);
        var outputType = reference.Functions.Single(
            static item => item.Name == "Initialize").Variables[1].Type;
        var outputFormalTypeToken = Encoding.ASCII.GetBytes(
            "init:ctype#" + CheckedReferenceTypes.Index(outputType).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        var invalidOutputFormalTypeToken = Encoding.ASCII.GetBytes(
            "init:ctype#" + reference.CheckedReferenceTypes.Length.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        var outputFormalTypeOffset = FindBytesOffset(consumer,
            outputFormalTypeToken);
        Assert.That(outputFormalTypeOffset,
            Is.GreaterThanOrEqualTo(sizeof(int)));
        var malformedConsumer = new byte[consumer.Length
            - outputFormalTypeToken.Length
            + invalidOutputFormalTypeToken.Length];
        Array.Copy(consumer, 0, malformedConsumer, 0,
            outputFormalTypeOffset);
        Array.Copy(invalidOutputFormalTypeToken, 0, malformedConsumer,
            outputFormalTypeOffset, invalidOutputFormalTypeToken.Length);
        Array.Copy(consumer,
            outputFormalTypeOffset + outputFormalTypeToken.Length,
            malformedConsumer,
            outputFormalTypeOffset + invalidOutputFormalTypeToken.Length,
            consumer.Length - outputFormalTypeOffset
                - outputFormalTypeToken.Length);
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.ValidateConsumerAgainstReference(
                malformedConsumer, reference));
        var nativeConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse(consumerSource), nativeGraphReference);
        Assert.That(nativeConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                nativeConsumerCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            nativeConsumerCompilation.Program!, [nativePackage],
            NativeRuntimePolicy.FailFast);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer,
                [nativePackage], TimeSpan.FromSeconds(10),
                NativeRuntimePolicy.FailFast).ConfigureAwait(true),
            Is.EqualTo(82));
        var runtimeConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                public int32 Main() {
                    init int32 result;
                    require InitializeRuntime(true, ref init result);
                    return result;
                }
                """), runtimeGraphReference);
        Assert.That(runtimeConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                runtimeConsumerCompilation.Diagnostics));
        var runtimeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            runtimeConsumerCompilation.Program!, [runtimePackage],
            NativeRuntimePolicy.FailFast);
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(
                runtimeConsumer, [runtimePackage], CompilerApphost(),
                TimeSpan.FromSeconds(30),
                NativeRuntimePolicy.FailFast).ConfigureAwait(true),
            Is.EqualTo(41));

        var failureConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                public int32 Main() {
                    init int32 result;
                    if (InitializeRuntime(false, ref init result)) {
                        return 1;
                    }
                    return result is init ? 2 : 42;
                }
                """), reference);
        Assert.That(failureConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                failureConsumerCompilation.Diagnostics));
        var failureConsumer = ModuleCompiler.CreateConsumer(
            failureConsumerCompilation.Program!, producer);
        ModuleCompiler.ValidateConsumerAgainstReference(
            failureConsumer, reference);
        var nativeFailureCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                public int32 Main() {
                    init int32 result;
                    if (InitializeRuntime(false, ref init result)) {
                        return 1;
                    }
                    return result is init ? 2 : 42;
                }
                """), nativeGraphReference);
        Assert.That(nativeFailureCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                nativeFailureCompilation.Diagnostics));
        var nativeFailureConsumer = NativeModuleCompiler.CreateGraphConsumer(
            nativeFailureCompilation.Program!, [nativePackage],
            NativeRuntimePolicy.FailFast);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(
                nativeFailureConsumer, [nativePackage],
                TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast)
                .ConfigureAwait(true),
            Is.EqualTo(42));
        var runtimeFailureCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                public int32 Main() {
                    init int32 result;
                    if (InitializeRuntime(false, ref init result)) {
                        return 1;
                    }
                    return result is init ? 2 : 42;
                }
                """), runtimeGraphReference);
        Assert.That(runtimeFailureCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                runtimeFailureCompilation.Diagnostics));
        var runtimeFailureConsumer = NativeModuleCompiler.CreateGraphConsumer(
            runtimeFailureCompilation.Program!, [runtimePackage],
            NativeRuntimePolicy.FailFast);
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(
                runtimeFailureConsumer, [runtimePackage],
                CompilerApphost(), TimeSpan.FromSeconds(30),
                NativeRuntimePolicy.FailFast).ConfigureAwait(true),
            Is.EqualTo(42));

        var neutralCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Neutral() { return 0; }"));
        Assert.That(neutralCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                neutralCompilation.Diagnostics));
        var neutralProducer = ModuleCompiler.CreateProducer(
            neutralCompilation.Program!, ["Neutral"]);
        byte[][] providers = [producer, neutralProducer];
        var consumerSet = ModuleCompiler.CreateConsumerSet(
            consumerCompilation.Program!, providers);
        ModuleCompiler.ValidateConsumerSet(consumerSet, providers);
        var providerSetReference =
            ModuleCompiler.CreateReferenceSet(providers);

        var graphCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Wrapper() {
                mut int32 backing = 41;
                write ref<int32> source = ref backing;
                init int32 captured;
                require Initialize(true, ref init captured, source);
                init int32 result;
                require InitializeRuntime(true, ref init result);
                return captured + result;
            }
            """), reference);
        Assert.That(graphCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                graphCompilation.Diagnostics));
        var graph = ModuleCompiler.CreateGraphLibrary(
            graphCompilation.Program!, ["Wrapper"], providerSetReference);
        _ = ModuleCompiler.CreateGraphReference(graph, ["Wrapper"]);
    }

    [TestCase(PrimitiveType.Int32, 3, 4)]
    [TestCase(PrimitiveType.Int32, 5, 6)]
    [TestCase(PrimitiveType.UInt32, 3, 4)]
    public void NonBooleanCheckedOutputPairsRoundTripAndRejectWrongTests(
        PrimitiveType returnType, byte completeNodeKind,
        byte vacantNodeKind)
    {
        var returnTypeName = returnType == PrimitiveType.Int32
            ? "int32" : "uint32";
        var returnValue = returnType == PrimitiveType.Int32
            ? "int32(1)" : "uint32(1)";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"public {returnTypeName} Initialize(ref<init int32> output) "
            + $"{{ *output = int32(42); return {returnValue}; }}"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var functionIndex = program.Functions.IndexOf(
            global::System.Linq.Enumerable.Single(program.Functions,
                static item => item.Name == "Initialize"));
        var function = program.Functions[functionIndex] with {
            CheckedOutputSelectors = [
                new NativeFunctionPointerSelector(3, 0,
                    completeNodeKind),
                new NativeFunctionPointerSelector(3, 1,
                    vacantNodeKind),
            ],
            CheckedOutputStates = [
                new NativeFunctionPointerOutputState(0, 0, 1),
                new NativeFunctionPointerOutputState(0, 1, 0),
            ],
        };
        var producer = ModuleCompiler.CreateProducer(
            program with {
                Functions = program.Functions.SetItem(functionIndex,
                    function),
            }, ["Initialize"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var imported = global::System.Linq.Enumerable.Single(
            reference.Functions,
            static item => item.Name == "Initialize");
        Assert.That(imported.CheckedOutputSelectors,
            Is.EqualTo(function.CheckedOutputSelectors));
        Assert.That(imported.CheckedOutputStates,
            Is.EqualTo(function.CheckedOutputStates));

        var firstSelector = new byte[12];
        firstSelector[0] = 3;
        BinaryPrimitives.WriteUInt32LittleEndian(
            firstSelector.AsSpan(8, sizeof(uint)), completeNodeKind);
        var selectorOffset = FindBytesOffset(producer, firstSelector);
        Assert.That(selectorOffset, Is.GreaterThanOrEqualTo(sizeof(int)));
        var malformed = (byte[])producer.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformed.AsSpan(selectorOffset + 12 + 8, sizeof(uint)), 2);
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));
    }

    [Test]
    public async Task GenericReferenceSpecializationSurvivesPrivateTypeTablesAsync()
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            bool Touch<T>(ref<T> storage)  {
                _ = *storage;
                return true;
            }
            int32 Use<T>(ref<T> storage)  {
                return int32(42);
            }
            public int32 Read(ref<int32> value) {
                _ = Touch(value);
                return Use(value);
            }
            public int32 ReadNested(ref<ref<int32>> value) {
                _ = Touch(value);
                return Use(value);
            }
            public int32 ReadOptionalReference(
                ref<ref<int32>?> value) {
                return Use(value);
            }
            """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program!;
        var provider = ModuleCompiler.CreateProducer(providerProgram,
            ["Read", "ReadNested", "ReadOptionalReference"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            provider.AsSpan(sizeof(int), sizeof(int))), Is.EqualTo(106));
        var reference = ModuleCompiler.CreateReference(provider);
        var read = global::System.Linq.Enumerable.Single(
            reference.Functions, static function => function.Name == "Read");
        var referenceType = reference.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(read.Variables[0].Type)];
        Assert.That(referenceType.ReferentType,
            Is.EqualTo(PrimitiveType.Int32));
        var nestedReferenceType = global::System.Linq.Enumerable.Single(
            reference.CheckedReferenceTypes,
            item => item.ReferentType == CheckedReferenceTypes.Type(0));
        Assert.That(nestedReferenceType.ReferentType,
            Is.EqualTo(CheckedReferenceTypes.Type(0)));
        var optionalReferenceType = global::System.Linq.Enumerable.Single(
            reference.CheckedReferenceTypes,
            static item => item.IsOptional && !item.IsOptionalValue);
        Assert.That(global::System.Linq.Enumerable.Any(
            reference.CheckedReferenceTypes,
            item => item.ReferentType
                == CheckedReferenceTypes.Type(
                    reference.CheckedReferenceTypes.IndexOf(
                        optionalReferenceType))),
            Is.True);

        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                int32 Main() {
                    mut int32 value = int32(42);
                    ref<int32> borrowed = ref value;
                    ref<ref<int32>> nested = ref borrowed;
                    return Read(borrowed) + ReadNested(nested);
                }
                """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, provider);
        ModuleCompiler.ValidateConsumerAgainstReference(consumer, reference);
        var linked = ModuleCompiler.Link(consumer, provider);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,
            static function => function.Name == "Read"), Is.True);

        var nativeProvider = NativeModuleCompiler.CreateProducer(
            providerProgram, ["Read", "ReadNested",
                "ReadOptionalReference"],
            "generic_reference_provider",
            NativeRuntimePolicy.FailFast);
        var nativeReference =
            NativeModuleCompiler.CreateGraphReference([nativeProvider]);
        var nativeConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                int32 Main() {
                    mut int32 value = int32(42);
                    ref<int32> borrowed = ref value;
                    ref<ref<int32>> nested = ref borrowed;
                    return Read(borrowed) + ReadNested(nested);
                }
                """), nativeReference);
        Assert.That(nativeConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                nativeConsumerCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            nativeConsumerCompilation.Program!, [nativeProvider],
            NativeRuntimePolicy.FailFast);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer,
                [nativeProvider], TimeSpan.FromSeconds(10),
                NativeRuntimePolicy.FailFast).ConfigureAwait(true),
            Is.EqualTo(84));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task GenericReferenceResultDependenciesSurviveProvidersAsync(
        NativeRuntimePolicy runtimePolicy)
    {
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
            using Terrum.CodeAnalysis;
            [return: DependsOn(nameof(left), nameof(right))]
            ref<T> Select<T>(
                ref<T> left, ref<T> right, bool useLeft)  {
                return useLeft ? left : right;
            }
            public int32 ReadSelected() {
                mut int32 first = int32(7);
                mut int32 second = int32(42);
                ref<int32> firstReference = ref first;
                ref<int32> secondReference = ref second;
                return *Select(firstReference, secondReference, false);
            }
            """), (BoundRuntimePolicy)runtimePolicy);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program!;
        var privateProvider = ModuleCompiler.CreateProducer(
            providerProgram, ["ReadSelected"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            privateProvider.AsSpan(sizeof(int), sizeof(int))),
            Is.EqualTo(106));
        var privateReference = ModuleCompiler.CreateReference(privateProvider);
        Assert.That(privateReference.RuntimePolicy,
            Is.EqualTo((BoundRuntimePolicy)runtimePolicy));
        var privateConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                int32 Main() { return ReadSelected(); }
                """), privateReference,
            runtimePolicy: (BoundRuntimePolicy)runtimePolicy);
        Assert.That(privateConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                privateConsumerCompilation.Diagnostics));
        var privateConsumer = ModuleCompiler.CreateConsumer(
            privateConsumerCompilation.Program!, privateProvider);
        ModuleCompiler.ValidateConsumerAgainstReference(privateConsumer,
            privateReference);
        var linked = ModuleCompiler.Link(privateConsumer, privateProvider);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,
            static function => function.Name == "ReadSelected"), Is.True);

        var nativeProvider = NativeModuleCompiler.CreateProducer(
            providerProgram, ["ReadSelected"],
            "generic_reference_result_provider", runtimePolicy);
        var nativeReference =
            NativeModuleCompiler.CreateGraphReference([nativeProvider]);
        var nativeConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                int32 Main() { return ReadSelected(); }
                """), nativeReference,
            runtimePolicy: (BoundRuntimePolicy)runtimePolicy);
        Assert.That(nativeConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                nativeConsumerCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            nativeConsumerCompilation.Program!, [nativeProvider],
            runtimePolicy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer,
                [nativeProvider], TimeSpan.FromSeconds(10), runtimePolicy)
                .ConfigureAwait(true),
            Is.EqualTo(42));
    }

    [TestCase("""
        [return: DependsOn(nameof(left))]
        """, "TRM3007")]
    [TestCase("""
        [return: DependsOn(nameof(left), nameof(right))]
        [return: ForwardsBacking(nameof(useLeft))]
        """, "TRM3023")]
    public void GenericReferenceResultRejectsIncompleteDependencyContracts(
        string annotations, string expectedDiagnostic)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "using Terrum.CodeAnalysis;\n"
            + annotations + "\n"
            + """
            ref<T> Select<T>(
                ref<T> left, ref<T> right, bool useLeft)  {
                return useLeft ? left : right;
            }
            """));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(
            compilation.Diagnostics,
            diagnostic => diagnostic.Code == expectedDiagnostic), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CheckedReferenceSignatureRowsRemapAcrossProviderSetsAndGraphsAsync(
        NativeRuntimePolicy runtimePolicy)
    {
        var scalarCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public int32 Read(ref<int32> value) {
                    return *value;
                }
                """), (BoundRuntimePolicy)runtimePolicy);
        var nestedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public int32 ReadNested(ref<ref<int32>> value) {
                    return *(*value);
                }
                """), (BoundRuntimePolicy)runtimePolicy);
        Assert.That(scalarCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, scalarCompilation.Diagnostics));
        Assert.That(nestedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, nestedCompilation.Diagnostics));
        var scalarProgram = scalarCompilation.Program!;
        var nestedProgram = nestedCompilation.Program!;
        var scalarProvider = ModuleCompiler.CreateProducer(scalarProgram,
            ["Read"], PrivateModuleIdentityInputs.Create(scalarProgram,
                "ReferenceScalarProvider", [], []));
        var nestedProvider = ModuleCompiler.CreateProducer(nestedProgram,
            ["ReadNested"], PrivateModuleIdentityInputs.Create(nestedProgram,
                "ReferenceNestedProvider", [], []));
        var scalarReference = ModuleCompiler.CreateReference(scalarProvider);
        var nestedReference = ModuleCompiler.CreateReference(nestedProvider);
        Assert.That(scalarReference.CheckedReferenceTypes, Has.Length.EqualTo(1));
        Assert.That(nestedReference.CheckedReferenceTypes, Has.Length.EqualTo(2));

        byte[][] providers = [nestedProvider, scalarProvider];
        var providerSetReference = ModuleCompiler.CreateReferenceSet(providers);
        Assert.That(providerSetReference.CheckedReferenceTypes,
            Has.Length.EqualTo(2));
        var scalarFormal = global::System.Linq.Enumerable.Single(
            providerSetReference.Functions,
            static function => function.Name == "Read").Variables[0];
        var nestedFormal = global::System.Linq.Enumerable.Single(
            providerSetReference.Functions,
            static function => function.Name == "ReadNested").Variables[0];
        Assert.That(providerSetReference.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(scalarFormal.Type)].ReferentType,
            Is.EqualTo(PrimitiveType.Int32));
        var nestedReferenceType = providerSetReference.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(nestedFormal.Type)];
        Assert.That(nestedReferenceType.ReferentType,
            Is.EqualTo(scalarFormal.Type));

        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                int32 Main() {
                    mut int32 value = int32(42);
                    ref<int32> inner = ref value;
                    ref<ref<int32>> outer = ref inner;
                    return Read(inner) + ReadNested(outer);
                }
                """), providerSetReference,
            runtimePolicy: (BoundRuntimePolicy)runtimePolicy);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumerSet(
            consumerCompilation.Program!, providers);
        ModuleCompiler.ValidateConsumerSet(consumer, providers);

        var replacementCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public int32 AAcceptUInt32(ref<uint32> value) {
                    return 0;
                }
                public int32 Read(ref<int32> value) {
                    return *value;
                }
                """), (BoundRuntimePolicy)runtimePolicy);
        Assert.That(replacementCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                replacementCompilation.Diagnostics));
        var replacementProgram = replacementCompilation.Program!;
        var replacement = ModuleCompiler.CreateProducer(replacementProgram,
            ["AAcceptUInt32", "Read"],
            PrivateModuleIdentityInputs.Create(replacementProgram,
                "ReferenceScalarProvider", [], []));
        var replacementReference = ModuleCompiler.CreateReference(replacement);
        Assert.That(replacementReference.CheckedReferenceTypes,
            Has.Length.EqualTo(2));
        var originalReadType = global::System.Linq.Enumerable.Single(
            scalarReference.Functions,
            static function => function.Name == "Read").Variables[0].Type;
        var replacementReadType = global::System.Linq.Enumerable.Single(
            replacementReference.Functions,
            static function => function.Name == "Read").Variables[0].Type;
        Assert.That(replacementReadType, Is.Not.EqualTo(originalReadType));
        Assert.That(replacementReference.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(replacementReadType)]
                .ReferentType,
            Is.EqualTo(PrimitiveType.Int32));
        ModuleCompiler.ValidateConsumerSet(consumer,
            [nestedProvider, replacement]);

        var incompatibleCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public int32 Read(ref<uint32> value) {
                    return 0;
                }
                """), (BoundRuntimePolicy)runtimePolicy);
        Assert.That(incompatibleCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                incompatibleCompilation.Diagnostics));
        var incompatible = ModuleCompiler.CreateProducer(
            incompatibleCompilation.Program!, ["Read"],
            PrivateModuleIdentityInputs.Create(
                incompatibleCompilation.Program!,
                "ReferenceScalarProvider",
                [], []));
        _ = Assert.Throws<ModuleContractException>(
            () => ModuleCompiler.ValidateConsumerSet(consumer,
                [nestedProvider, incompatible]));

        var nativeScalar = NativeModuleCompiler.CreateProducer(
            scalarProgram, ["Read"], "native_checked_ref_scalar",
            runtimePolicy);
        var nativeNested = NativeModuleCompiler.CreateProducer(
            nestedProgram, ["ReadNested"], "native_checked_ref_nested",
            runtimePolicy);
        var nativeProviders = new[] { nativeNested, nativeScalar };
        var graphReference =
            NativeModuleCompiler.CreateGraphReference(nativeProviders);
        Assert.That(graphReference.CheckedReferenceTypes,
            Has.Length.EqualTo(2));
        var graphConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                int32 Main() {
                    mut int32 value = int32(42);
                    ref<int32> inner = ref value;
                    ref<ref<int32>> outer = ref inner;
                    return Read(inner) + ReadNested(outer);
                }
                """), graphReference,
            runtimePolicy: (BoundRuntimePolicy)runtimePolicy);
        Assert.That(graphConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                graphConsumerCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            graphConsumerCompilation.Program!, nativeProviders, runtimePolicy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(graphConsumer,
                nativeProviders, TimeSpan.FromSeconds(10), runtimePolicy)
                .ConfigureAwait(true),
            Is.EqualTo(84));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task StoredReferenceFieldAliasRetainsConstructorCaptureDependencyAsync(
        NativeRuntimePolicy runtimePolicy)
    {
        const string source = """
            internal struct Cell {
                public mut write ref<int32> Value;
                [this: Captures(nameof(value), nameof(other))]
                public Cell(write ref<int32> value,
                    write ref<int32> other) {
                    Value = value;
                }
            }
            public int32 MutateAlias() {
                mut int32 first = int32(7);
                mut int32 second = int32(42);
                mut Cell cell = new Cell(ref first, ref second);
                write ref<Cell> borrowedCell = ref cell;
                (*borrowedCell).Value = ref second;
                return *cell.Value;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            (BoundRuntimePolicy)runtimePolicy);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var cellType = (PrimitiveType)(100
            + global::System.Linq.Enumerable.Single(
                global::System.Linq.Enumerable.Select(
                    program.Structures,
                    static (structure, index) => (structure, index)),
                static item => item.structure.Name == "Cell").index);
        var identityInputs = PrivateModuleIdentityInputs.Create(program,
            "provider",
            [new PrivateNominalDefinition(cellType, "types.cell",
                IsExported: false)], []);
        var producer = ModuleCompiler.CreateProducer(program,
            ["MutateAlias"], identityInputs);
        var sourceConstructor = global::System.Linq.Enumerable.Single(
            program.Functions,
            static function => function.IsConstructor);
        Assert.That(sourceConstructor.DependencyContracts,
            Has.Length.EqualTo(1));
        var sourceCapture = global::System.Linq.Enumerable.Single(
            sourceConstructor.DependencyContracts);
        Assert.That(sourceCapture.Relation,
            Is.EqualTo(BoundDependencyRelation.Captures));
        Assert.That(sourceCapture.Destination,
            Is.EqualTo(BoundDependencyDestination.Receiver));
        Assert.That(sourceCapture.DestinationOrdinal, Is.Null);
        Assert.That(sourceCapture.Sources, Has.Length.EqualTo(2));
        Assert.That(sourceCapture.Sources[0].Kind,
            Is.EqualTo(BoundDependencySourceKind.Formal));
        Assert.That(sourceCapture.Sources[0].FormalOrdinal, Is.EqualTo(0));
        Assert.That(sourceCapture.Sources[1].Kind,
            Is.EqualTo(BoundDependencySourceKind.Formal));
        Assert.That(sourceCapture.Sources[1].FormalOrdinal, Is.EqualTo(1));

        byte[] captureRow = [
            4, 1, 3, 2, 0, 0, 0, 0, 0, 0, 0, 0,
        ];
        var captureRowOffset = producer.AsSpan().IndexOf(captureRow);
        Assert.That(captureRowOffset, Is.GreaterThanOrEqualTo(0));
        var rowCountOffset = captureRowOffset - sizeof(int);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            producer.AsSpan(rowCountOffset, sizeof(int))), Is.EqualTo(2));

        var malformed = (byte[])producer.Clone();
        malformed[captureRowOffset] = byte.MaxValue;
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));
        malformed = (byte[])producer.Clone();
        malformed[captureRowOffset + 1] = byte.MaxValue;
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));
        malformed = (byte[])producer.Clone();
        malformed[captureRowOffset + 2] = 1;
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));
        malformed = (byte[])producer.Clone();
        malformed[captureRowOffset + 3] = 3;
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));
        malformed = (byte[])producer.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformed.AsSpan(captureRowOffset + 4, sizeof(uint)), 1);
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));
        malformed = (byte[])producer.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformed.AsSpan(captureRowOffset + 8, sizeof(uint)), 2);
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));

        malformed = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(
            malformed.AsSpan(rowCountOffset, sizeof(int)), 3);
        Buffer.BlockCopy(malformed, captureRowOffset,
            malformed, captureRowOffset + (2 * 12), 12);
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));

        malformed = (byte[])producer.Clone();
        malformed[captureRowOffset + 1] = 0;
        malformed[captureRowOffset + 2] = 0;
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));

        var reference = ModuleCompiler.CreateReference(producer);
        var aliasStoreText = "(*borrowedCell).Value = ref second;";
        Assert.That(FindEnvelopeOffset(producer, 53,
            source.IndexOf(aliasStoreText, StringComparison.Ordinal),
            aliasStoreText.Length), Is.GreaterThanOrEqualTo(0));
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            public int32 Main() { return MutateAlias(); }
            """), reference,
            runtimePolicy: (BoundRuntimePolicy)runtimePolicy);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, producer);
        ModuleCompiler.ValidateConsumerAgainstReference(consumer, reference);
        _ = ModuleCompiler.Link(consumer, producer);

        var neutralCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public int32 Neutral() { return 0; }
                """), (BoundRuntimePolicy)runtimePolicy);
        Assert.That(neutralCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                neutralCompilation.Diagnostics));
        var neutralProducer = ModuleCompiler.CreateProducer(
            neutralCompilation.Program!, ["Neutral"]);
        byte[][] providers = [producer, neutralProducer];
        var consumerSet = ModuleCompiler.CreateConsumerSet(
            consumerCompilation.Program!, providers);
        ModuleCompiler.ValidateConsumerSet(consumerSet, providers);
        var providerSetReference = ModuleCompiler.CreateReferenceSet(
            providers);
        var restoredConsumer =
            ModuleCompiler.RestoreConsumerAgainstReference(consumerSet,
                providerSetReference);
        var reemittedConsumer = ModuleCompiler.CreateConsumerSet(
            restoredConsumer, providers);
        Assert.That(reemittedConsumer, Is.EqualTo(consumerSet));

        var graphCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public int32 Wrapper() { return MutateAlias(); }
                """), providerSetReference,
            (BoundRuntimePolicy)runtimePolicy);
        Assert.That(graphCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                graphCompilation.Diagnostics));
        var graph = ModuleCompiler.CreateGraphLibrary(
            graphCompilation.Program!, ["Wrapper"], providerSetReference);
        _ = ModuleCompiler.CreateGraphReference(graph, ["Wrapper"]);

        var nativeProvider = NativeModuleCompiler.CreateProducer(program,
            ["MutateAlias"], "native_stored_reference_alias",
            runtimePolicy, identityInputs);
        var nativeReference =
            NativeModuleCompiler.CreateGraphReference([nativeProvider]);
        var nativeConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                public int32 Main() { return MutateAlias(); }
                """), nativeReference,
            runtimePolicy: (BoundRuntimePolicy)runtimePolicy);
        Assert.That(nativeConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                nativeConsumerCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            nativeConsumerCompilation.Program!, [nativeProvider],
            runtimePolicy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer,
                [nativeProvider], TimeSpan.FromSeconds(10), runtimePolicy)
                .ConfigureAwait(true),
            Is.EqualTo(42));
    }

    [Test]
    public void PrivateOptionalReferenceProjectionRetainsItsRequiredRow()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public void AcceptOptional(ref<int32>? value) {}
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var sourceOptionalFunction = global::System.Linq.Enumerable.Single(
            program.Functions,
            static function => function.Name == "AcceptOptional");
        Assert.That(program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(
                    sourceOptionalFunction.Variables[0].Type)].IsOptional,
            Is.True);

        var producer = ModuleCompiler.CreateProducer(program,
            ["AcceptOptional"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var persistedOptionalFunction =
            global::System.Linq.Enumerable.Single(reference.Functions,
                static function => function.Name == "AcceptOptional");
        Assert.That(reference.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(
                    persistedOptionalFunction.Variables[0].Type)].IsOptional,
            Is.True);
        Assert.That(reference.CheckedReferenceTypes, Has.Length.EqualTo(2));
        Assert.That(reference.CheckedReferenceTypes[0],
            Is.EqualTo(new CheckedReferenceTypeDefinition(
                PrimitiveType.Int32, IsWritable: false,
                IsGarbageCollected: false, IsInitialization: false,
                IsOptional: false)));
        Assert.That(reference.CheckedReferenceTypes[1],
            Is.EqualTo(reference.CheckedReferenceTypes[0] with {
                IsOptional = true,
            }));
        Assert.That(global::System.Linq.Enumerable.Single(
                reference.Functions,
                static function => function.Name == "AcceptOptional")
                .Variables[0].Type,
            Is.EqualTo(CheckedReferenceTypes.Type(1)));
    }

    [Test]
    public void PrivateOptionalValueTypesUseTaggedLayoutInPrivateGraphs()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public void AcceptScalar(int32? value) {}
            public void AcceptBoolean(bool? value) {}
            public void AcceptWide(int64? value) {}
            public void AcceptUnit(option<void> value) {}
            public unsafe void AcceptPointer(ptr<int32>? value) {}
            public unsafe void AcceptRawPointer(ptr<void>? value) {}
            public unsafe void AcceptRawPointerConstructor(option<ptr<void>> value) {}
            public unsafe void AcceptFunctionPointer(option<fnptr<int32, int32>> value) {}
            public unsafe void AcceptFunctionPointerSuffix(fnptr<int32, int32>? value) {}
            public void AcceptReference(option<ref<int32>> value) {}
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var producer = ModuleCompiler.CreateProducer(program,
            ["AcceptScalar", "AcceptBoolean", "AcceptWide", "AcceptUnit",
                "AcceptPointer", "AcceptRawPointer",
                "AcceptRawPointerConstructor", "AcceptFunctionPointer",
                "AcceptFunctionPointerSuffix", "AcceptReference"]);
        var reference = ModuleCompiler.CreateReference(producer);

        var scalarType = global::System.Linq.Enumerable.Single(
            reference.Functions,
            static function => function.Name == "AcceptScalar")
            .Variables[0].Type;
        var scalarDefinition = reference.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(scalarType)];
        Assert.That(scalarDefinition.IsOptionalValue, Is.True);
        Assert.That(scalarDefinition.ReferentType,
            Is.EqualTo(PrimitiveType.Int32));
        var unitType = global::System.Linq.Enumerable.Single(
            reference.Functions,
            static function => function.Name == "AcceptUnit")
            .Variables[0].Type;
        var unitDefinition = reference.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(unitType)];
        Assert.That(unitDefinition.IsOptionalValue, Is.True);
        Assert.That(unitDefinition.ReferentType,
            Is.EqualTo(PrimitiveType.Void));

        Assert.That(ValueLayout.Size(scalarType, [],
                reference.CheckedReferenceTypes),
            Is.EqualTo(8));
        Assert.That(ValueLayout.Alignment(scalarType, [],
                reference.CheckedReferenceTypes),
            Is.EqualTo(4));
        Assert.That(ValueLayout.RequiredAlignment(scalarType, [],
                checkedReferenceTypes: reference.CheckedReferenceTypes),
            Is.EqualTo(1));
        Assert.That(ValueLayout.TryGetOptionalValueLayout(scalarType, [],
                reference.CheckedReferenceTypes, null, [],
                out var scalarLayout),
            Is.True);
        Assert.That(scalarLayout.PayloadOffset, Is.EqualTo(4));
        Assert.That(scalarLayout.UsesNullNiche, Is.False);
        var booleanType = global::System.Linq.Enumerable.Single(
            reference.Functions,
            static function => function.Name == "AcceptBoolean")
            .Variables[0].Type;
        Assert.That(ValueLayout.Size(booleanType, [],
                reference.CheckedReferenceTypes),
            Is.EqualTo(2));
        Assert.That(ValueLayout.TryGetOptionalValueLayout(booleanType, [],
                reference.CheckedReferenceTypes, null, [],
                out var booleanLayout),
            Is.True);
        Assert.That(booleanLayout.PayloadOffset, Is.EqualTo(1));
        var wideType = global::System.Linq.Enumerable.Single(
            reference.Functions,
            static function => function.Name == "AcceptWide")
            .Variables[0].Type;
        Assert.That(ValueLayout.Size(wideType, [],
                reference.CheckedReferenceTypes),
            Is.EqualTo(16));
        Assert.That(ValueLayout.TryGetOptionalValueLayout(wideType, [],
                reference.CheckedReferenceTypes, null, [],
                out var wideLayout),
            Is.True);
        Assert.That(wideLayout.PayloadOffset, Is.EqualTo(8));
        Assert.That(ValueLayout.Alignment(unitType, [],
                reference.CheckedReferenceTypes),
            Is.EqualTo(1));
        Assert.That(ValueLayout.Size(unitType, [],
                reference.CheckedReferenceTypes),
            Is.EqualTo(1));
        Assert.That(ValueLayout.TryGetOptionalValueLayout(unitType, [],
                reference.CheckedReferenceTypes, null, [],
                out var unitLayout),
            Is.True);
        Assert.That(unitLayout.PayloadOffset, Is.EqualTo(1));
        Assert.That(unitLayout.Size, Is.EqualTo(1));
        var pointerType = global::System.Linq.Enumerable.Single(
            reference.Functions,
            static function => function.Name == "AcceptPointer")
            .Variables[0].Type;
        Assert.That(ValueLayout.TryGetOptionalValueLayout(pointerType, [],
                reference.CheckedReferenceTypes, null, [],
                out var pointerLayout),
            Is.True);
        Assert.That(pointerLayout.UsesNullNiche, Is.True);
        Assert.That(pointerLayout.Size,
            Is.EqualTo((ulong)NativeTargetProfile.PointerSize));
        var rawPointerType = global::System.Linq.Enumerable.Single(
            reference.Functions,
            static function => function.Name == "AcceptRawPointer")
            .Variables[0].Type;
        var rawPointerConstructorType
            = global::System.Linq.Enumerable.Single(reference.Functions,
                static function => function.Name
                    == "AcceptRawPointerConstructor")
                .Variables[0].Type;
        Assert.That(rawPointerType, Is.EqualTo(rawPointerConstructorType));
        Assert.That(ValueLayout.TryGetOptionalValueLayout(rawPointerType,
                reference.Structures, reference.CheckedReferenceTypes, null,
                reference.NativeFixedArrayTypes, out var rawPointerLayout),
            Is.True);
        Assert.That(rawPointerLayout.UsesNullNiche, Is.True);
        Assert.That(rawPointerLayout.Size,
            Is.EqualTo((ulong)NativeTargetProfile.PointerSize));
        var functionPointerType = global::System.Linq.Enumerable.Single(
            reference.Functions,
            static function => function.Name == "AcceptFunctionPointer")
            .Variables[0].Type;
        Assert.That(CheckedReferenceTypes.IsType(functionPointerType),
            Is.True, functionPointerType.ToString());
        var functionPointerDefinition = reference.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(functionPointerType)];
        Assert.That(functionPointerDefinition.IsOptionalValue,
            Is.True, functionPointerDefinition.ToString());
        var functionPointerSuffixType
            = global::System.Linq.Enumerable.Single(
                reference.Functions,
                static function => function.Name
                    == "AcceptFunctionPointerSuffix")
                .Variables[0].Type;
        Assert.That(functionPointerSuffixType,
            Is.EqualTo(functionPointerType));
        Assert.That(NativeFunctionPointerTypes.IsType(
                functionPointerDefinition.ReferentType),
            Is.True, functionPointerDefinition.ToString());
        Assert.That(ValueLayout.TryGetOptionalValueLayout(
                functionPointerType, reference.Structures,
                reference.CheckedReferenceTypes, null,
                reference.NativeFixedArrayTypes,
                out var functionPointerLayout),
            Is.True);
        Assert.That(functionPointerLayout.UsesNullNiche, Is.False);
        Assert.That(functionPointerLayout.PayloadOffset,
            Is.EqualTo((ulong)NativeTargetProfile.PointerSize));
        Assert.That(functionPointerLayout.Size,
            Is.EqualTo(2UL * (ulong)NativeTargetProfile.PointerSize));
        var optionalReferenceType = global::System.Linq.Enumerable.Single(
            reference.Functions,
            static function => function.Name == "AcceptReference")
            .Variables[0].Type;
        Assert.That(reference.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(optionalReferenceType)]
                .IsOptionalValue,
            Is.False);
        Assert.That(ValueLayout.TryGetOptionalValueLayout(
                optionalReferenceType, reference.Structures,
                reference.CheckedReferenceTypes, null,
                reference.NativeFixedArrayTypes, out _),
            Is.False);
        var neutralCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Other() { return int32(1); }"));
        Assert.That(neutralCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                neutralCompilation.Diagnostics));
        var neutralProducer = ModuleCompiler.CreateProducer(
            neutralCompilation.Program!, ["Other"]);
        var setReference = ModuleCompiler.CreateReferenceSetFromReferences([
            reference, ModuleCompiler.CreateReference(neutralProducer),
        ]);
        Assert.That(setReference.CheckedReferenceTypes,
            Is.EqualTo(reference.CheckedReferenceTypes));

        var dependencies = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast);
        var graph = ModuleCompiler.CreateGraphLibrary(program,
            ["AcceptScalar", "AcceptBoolean", "AcceptWide", "AcceptUnit",
                "AcceptPointer", "AcceptRawPointer",
                "AcceptRawPointerConstructor", "AcceptFunctionPointer",
                "AcceptFunctionPointerSuffix", "AcceptReference"],
            dependencies);
        var graphReference = ModuleCompiler.CreateGraphReference(graph,
            ["AcceptScalar", "AcceptBoolean", "AcceptWide", "AcceptUnit",
                "AcceptPointer", "AcceptRawPointer",
                "AcceptRawPointerConstructor", "AcceptFunctionPointer",
                "AcceptFunctionPointerSuffix", "AcceptReference"]);
        Assert.That(graphReference.CheckedReferenceTypes,
            Is.EqualTo(reference.CheckedReferenceTypes));

        var aggregateCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                struct Pair { int32 First; int32 Second; }
                void Accept(Pair? value) {}
                """));
        Assert.That(aggregateCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                aggregateCompilation.Diagnostics));
        var aggregateProgram = aggregateCompilation.Program!;
        var aggregateType = aggregateProgram.Functions.Single().Variables[0]
            .Type;
        Assert.That(ValueLayout.TryGetOptionalValueLayout(aggregateType,
                aggregateProgram.Structures,
                aggregateProgram.CheckedReferenceTypes, null,
                aggregateProgram.NativeFixedArrayTypes,
                out var aggregateLayout),
            Is.True);
        Assert.That(aggregateLayout.PayloadOffset, Is.EqualTo(4));
        Assert.That(aggregateLayout.Size, Is.EqualTo(12));
    }

    [Test]
    public void OptionalRowsInArrayAndPointerClosuresRemapAcrossProviderSets()
    {
        static CompilationReference CreateProvider(PrimitiveType first,
            PrimitiveType second)
        {
            var referenceTypes = ImmutableArray.Create(
                new CheckedReferenceTypeDefinition(first,
                    IsWritable: false, IsGarbageCollected: false,
                    IsInitialization: false, IsOptional: true,
                    IsOptionalValue: true),
                new CheckedReferenceTypeDefinition(second,
                    IsWritable: false, IsGarbageCollected: false,
                    IsInitialization: false, IsOptional: true,
                    IsOptionalValue: true));
            var fixedArrays = new List<BoundNativeFixedArrayType>();
            foreach (var index in Enumerable.Range(0, referenceTypes.Length))
            {
                Assert.That(NativeFixedArrayContract.TryCreateType(
                        CheckedReferenceTypes.Type(index), 2, [],
                        fixedArrays, out _, out _, referenceTypes),
                    Is.True);
            }

            var pointerTypes = ImmutableArray.Create(
                new NativePointerTypeDefinition(
                    CheckedReferenceTypes.Type(0), IsWritable: false),
                new NativePointerTypeDefinition(
                    CheckedReferenceTypes.Type(1), IsWritable: false));
            return new CompilationReference([], [],
                BoundRuntimePolicy.FailFast,
                nativePointerTypes: pointerTypes,
                checkedReferenceTypes: referenceTypes,
                nativeFixedArrayTypes: [.. fixedArrays]);
        }

        static void AssertMerged(CompilationReference merged)
        {
            Assert.That(merged.CheckedReferenceTypes.Select(
                    static type => type.ReferentType),
                Is.EquivalentTo(new[] {
                    PrimitiveType.Bool, PrimitiveType.Char8,
                }));
            Assert.That(merged.NativeFixedArrayTypes, Has.Length.EqualTo(2));
            Assert.That(merged.NativeFixedArrayTypes.Select(array =>
                    merged.CheckedReferenceTypes[
                        CheckedReferenceTypes.Index(array.ElementType)]
                        .ReferentType),
                Is.EquivalentTo(new[] {
                    PrimitiveType.Bool, PrimitiveType.Char8,
                }));
            Assert.That(merged.NativeFixedArrayTypes.Select(
                    static array => (array.ElementStride,
                        array.ByteExtent)),
                Is.All.EqualTo((2UL, 4UL)));
            Assert.That(merged.NativePointerTypes, Has.Length.EqualTo(2));
            Assert.That(merged.NativePointerTypes.Select(pointer =>
                    merged.CheckedReferenceTypes[
                        CheckedReferenceTypes.Index(pointer.PointeeType)]
                        .ReferentType),
                Is.EquivalentTo(new[] {
                    PrimitiveType.Bool, PrimitiveType.Char8,
                }));
            Assert.That(NativePointerTypeContract.IsValidTable(
                    merged.NativePointerTypes, merged.Structures,
                    merged.NativeFunctionPointerSignatures,
                    merged.NativeFixedArrayTypes,
                    merged.CheckedReferenceTypes),
                Is.True);
        }

        var booleanFirst = CreateProvider(PrimitiveType.Bool,
            PrimitiveType.Char8);
        var characterFirst = CreateProvider(PrimitiveType.Char8,
            PrimitiveType.Bool);

        AssertMerged(ModuleCompiler.CreateReferenceSetFromReferences([
            booleanFirst, characterFirst,
        ]));
        AssertMerged(ModuleCompiler.MergeGraphReferences([
            booleanFirst, characterFirst,
        ]));
        AssertMerged(ModuleCompiler.CreateReferenceSetFromReferences([
            characterFirst, booleanFirst,
        ]));
        AssertMerged(ModuleCompiler.MergeGraphReferences([
            characterFirst, booleanFirst,
        ]));
    }

    [Test]
    public void OptionalEnumRowsInArrayAndPointerClosuresRemapAcrossProviderSets()
    {
        static BoundEnumDefinition CreateEnum(string name)
        {
            var enumIdentity = new BoundEnumDeclarationIdentity.Core(
                new CoreDeclarationIdentity("CoreNative", name,
                    CoreDeclarationKind.EnumType));
            var memberIdentity = new BoundEnumMemberIdentity.Core(
                new CoreDeclarationIdentity("CoreNative",
                    $"{name}.First", CoreDeclarationKind.EnumMember));
            return new BoundEnumDefinition(enumIdentity, name,
                PrimitiveType.Int32, [
                    new BoundEnumMemberDefinition(memberIdentity,
                        enumIdentity, "First", 1, true,
                        $"{name.ToUpperInvariant()}.first"),
                ], true, true, false);
        }

        static CompilationReference CreateProvider(
            BoundEnumDefinition enumDefinition)
        {
            var referenceTypes = ImmutableArray.Create(
                new CheckedReferenceTypeDefinition(BoundEnumTypes.Type(0),
                    IsWritable: false, IsGarbageCollected: false,
                    IsInitialization: false, IsOptional: true,
                    IsOptionalValue: true));
            var fixedArrays = new List<BoundNativeFixedArrayType>();
            Assert.That(NativeFixedArrayContract.TryCreateType(
                    CheckedReferenceTypes.Type(0), 2, [], fixedArrays,
                    out _, out _, referenceTypes, [enumDefinition]),
                Is.True);
            return new CompilationReference([], [],
                BoundRuntimePolicy.FailFast,
                nativePointerTypes: [
                    new NativePointerTypeDefinition(
                        CheckedReferenceTypes.Type(0), IsWritable: false),
                ],
                enumDefinitions: [enumDefinition],
                checkedReferenceTypes: referenceTypes,
                nativeFixedArrayTypes: [.. fixedArrays]);
        }

        var merged = ModuleCompiler.CreateReferenceSetFromReferences([
            CreateProvider(CreateEnum("FlagA")),
            CreateProvider(CreateEnum("FlagB")),
        ]);
        Assert.That(merged.EnumDefinitions, Has.Length.EqualTo(2));
        Assert.That(merged.CheckedReferenceTypes, Has.Length.EqualTo(2));
        Assert.That(merged.NativeFixedArrayTypes, Has.Length.EqualTo(2));
        Assert.That(merged.NativePointerTypes, Has.Length.EqualTo(2));
        Assert.That(merged.CheckedReferenceTypes.Select(
                static type => type.ReferentType),
            Is.EquivalentTo(new[] {
                BoundEnumTypes.Type(0), BoundEnumTypes.Type(1),
            }));
        Assert.That(merged.NativeFixedArrayTypes.Select(array =>
                merged.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(array.ElementType)]
                    .ReferentType),
            Is.EquivalentTo(new[] {
                BoundEnumTypes.Type(0), BoundEnumTypes.Type(1),
            }));
        Assert.That(merged.NativePointerTypes.Select(pointer =>
                merged.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(pointer.PointeeType)]
                    .ReferentType),
            Is.EquivalentTo(new[] {
                BoundEnumTypes.Type(0), BoundEnumTypes.Type(1),
            }));
        var graphMerged = ModuleCompiler.MergeGraphReferences([
            CreateProvider(CreateEnum("FlagA")),
            CreateProvider(CreateEnum("FlagB")),
        ]);
        Assert.That(graphMerged.NativeFixedArrayTypes, Is.EqualTo(
            merged.NativeFixedArrayTypes));
        Assert.That(graphMerged.NativePointerTypes, Is.EqualTo(
            merged.NativePointerTypes));
        Assert.That(graphMerged.CheckedReferenceTypes, Is.EqualTo(
            merged.CheckedReferenceTypes));
    }

    [Test]
    public void OptionalElementArraysRoundTripThroughPrivateProducerAndGraph()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct OptionalArray {
                public [int32?; 2] Values;
                public ptr<int32?> Pointer;
            }
            public void Accept() {}
            """), Terrum.CodeAnalysis.Tests.Semantics
                .NativeCoreRegistrationTests
                .CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var optionalArray = program.Structures.Single(
            static structure => structure.Name == "OptionalArray");
        var optionalArrayType = (PrimitiveType)(100
            + program.Structures.IndexOf(optionalArray));
        var identityInputs = PrivateModuleIdentityInputs.Create(program,
            "provider", [
                new PrivateNominalDefinition(optionalArrayType,
                    "types.optional.array", IsExported: true),
            ], []);
        var producer = ModuleCompiler.CreateProducer(program, ["Accept"],
            identityInputs);
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.That(reference.NativeFixedArrayTypes, Has.Length.EqualTo(1));
        var array = reference.NativeFixedArrayTypes[0];
        Assert.That(CheckedReferenceTypes.IsType(array.ElementType),
            Is.True);
        Assert.That(reference.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(array.ElementType)]
                .ReferentType,
            Is.EqualTo(PrimitiveType.Int32));
        Assert.That((array.ElementStride, array.ByteExtent),
            Is.EqualTo((8UL, 16UL)));
        Assert.That(reference.NativePointerTypes, Has.Length.EqualTo(1));
        var pointerPointee = reference.NativePointerTypes[0].PointeeType;
        Assert.That(CheckedReferenceTypes.IsType(pointerPointee), Is.True);
        Assert.That(reference.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(pointerPointee)].ReferentType,
            Is.EqualTo(PrimitiveType.Int32));

        var dependencies = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast);
        var graph = ModuleCompiler.CreateGraphLibrary(program, ["Accept"],
            dependencies, identityInputs);
        var graphReference = ModuleCompiler.CreateGraphReference(graph,
            ["Accept"]);
        Assert.That(graphReference.NativeFixedArrayTypes,
            Is.EqualTo(reference.NativeFixedArrayTypes));
        Assert.That(graphReference.CheckedReferenceTypes,
            Is.EqualTo(reference.CheckedReferenceTypes));

        var neutralCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(
                "public int32 Other() { return int32(1); }"));
        Assert.That(neutralCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                neutralCompilation.Diagnostics));
        var neutralProducer = ModuleCompiler.CreateProducer(
            neutralCompilation.Program!, ["Other"]);
        byte[][] providers = [producer, neutralProducer];
        var providerSet = ModuleCompiler.CreateReferenceSet(providers);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                int32 Main() {
                    init ptr<int32?> pointer;
                    return 0;
                }
                """), providerSet, BoundRuntimePolicy.FailFast);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerSet = ModuleCompiler.CreateConsumerSet(
            consumerCompilation.Program!, providers);
        Assert.DoesNotThrow(() =>
            ModuleCompiler.ValidateConsumerSet(consumerSet, providers));

    }

    [Test]
    public void PrivateReferenceProjectionInternsAndOrdersCompleteRows()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public void Use(ref<int32> first, ref<int32> duplicate,
                ref<ref<int32>> nested) {}
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var ordinaryReference = global::System.Linq.Enumerable.Single(
            program.CheckedReferenceTypes,
            static referenceType => !referenceType.IsOptional
                && !CheckedReferenceTypes.IsType(
                    referenceType.ReferentType));
        var nestedReference = global::System.Linq.Enumerable.Single(
            program.CheckedReferenceTypes,
            static referenceType => CheckedReferenceTypes.IsType(
                referenceType.ReferentType));
        var functionIndex = global::System.Linq.Enumerable.Single(
            global::System.Linq.Enumerable.Select(program.Functions,
                static (candidate, index) => (candidate, index)),
            static item => item.candidate.Name == "Use").index;
        var function = program.Functions[functionIndex];
        var variables = function.Variables
            .SetItem(0, function.Variables[0] with {
                Type = CheckedReferenceTypes.Type(1),
            })
            .SetItem(1, function.Variables[1] with {
                Type = CheckedReferenceTypes.Type(2),
            })
            .SetItem(2, function.Variables[2] with {
                Type = CheckedReferenceTypes.Type(0),
            });
        var reorderedProgram = program with {
            CheckedReferenceTypes = [
                nestedReference with {
                    ReferentType = CheckedReferenceTypes.Type(1),
                },
                ordinaryReference,
                ordinaryReference,
                new CheckedReferenceTypeDefinition(
                    PrimitiveType.UInt32, IsWritable: false,
                    IsGarbageCollected: false, IsInitialization: false,
                    IsOptional: false),
            ],
            Functions = program.Functions.SetItem(functionIndex,
                function with { Variables = variables }),
        };

        var producer = ModuleCompiler.CreateProducer(reorderedProgram,
            ["Use"]);
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.That(reference.CheckedReferenceTypes,
            Is.EqualTo(ImmutableArray.Create(ordinaryReference,
                nestedReference with {
                    ReferentType = CheckedReferenceTypes.Type(0),
                })));

        var persistedFunction = global::System.Linq.Enumerable.Single(
            reference.Functions,
            static candidate => candidate.Name == "Use");
        Assert.That(global::System.Linq.Enumerable.Select(
                persistedFunction.Variables,
                static variable => variable.Type),
            Is.EqualTo(ImmutableArray.Create(
                CheckedReferenceTypes.Type(0),
                CheckedReferenceTypes.Type(0),
                CheckedReferenceTypes.Type(1))));
    }

    [Test]
    public void PrivateReferenceProjectionRebasesBodyTypes()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Read(ref<int32> value) { return *value; }
            public int32 Use() {
                mut int32 value = int32(42);
                return Read(ref value);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var referenceType = program.CheckedReferenceTypes[0];
        var duplicateType = CheckedReferenceTypes.Type(
            program.CheckedReferenceTypes.Length);
        var remappedFunctions = ImmutableArray.CreateRange(
            program.Functions.Select(function =>
                BoundTreeTraversal.RewriteTypes(function,
                    type => type == CheckedReferenceTypes.Type(0)
                        ? duplicateType : type)));
        var duplicateProgram = program with {
            Functions = remappedFunctions,
            CheckedReferenceTypes = [.. program.CheckedReferenceTypes,
                referenceType],
        };
        var producer = ModuleCompiler.CreateProducer(duplicateProgram,
            ["Read", "Use"]);
        var reference = ModuleCompiler.CreateReference(producer);

        Assert.That(reference.CheckedReferenceTypes,
            Is.EqualTo(ImmutableArray.Create(referenceType)));
        var read = global::System.Linq.Enumerable.Single(
            reference.Functions,
            static function => function.Name == "Read");
        Assert.That(read.Variables[0].Type,
            Is.EqualTo(CheckedReferenceTypes.Type(0)));

        var consumer = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Use(); }"), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        Assert.That(ModuleCompiler.CreateConsumer(
                consumer.Program!, producer),
            Is.Not.Empty);
    }

    [Test]
    public void InitializationAttenuationAndMaterializationBodyOperationsRoundTrip()
    {
        var span = new TextSpan(0, 0);
        var ordinaryReference = new CheckedReferenceTypeDefinition(
            PrimitiveType.Int32, IsWritable: false,
            IsGarbageCollected: false, IsInitialization: false,
            IsOptional: false);
        var initializationReference = new CheckedReferenceTypeDefinition(
            PrimitiveType.Int32, IsWritable: false,
            IsGarbageCollected: false, IsInitialization: true,
            IsOptional: false);
        var checkedReferenceTypes = ImmutableArray.Create(
            ordinaryReference, initializationReference);
        var ordinaryReferenceType = CheckedReferenceTypes.Type(0);
        var initializationReferenceType = CheckedReferenceTypes.Type(1);
        var variables = ImmutableArray.Create(
            new BoundVariable("slot", PrimitiveType.Int32, false, span) {
                IsInitializationLocal = true,
            },
            new BoundVariable("capability", initializationReferenceType,
                false, span),
            new BoundVariable("completed", ordinaryReferenceType,
                false, span),
            new BoundVariable("temporary", PrimitiveType.Int32, true,
                span) {
                IsInitializationLocal = true,
                IsImplicitReferenceTemporary = true,
            },
            new BoundVariable("materialized", ordinaryReferenceType,
                false, span),
            new BoundVariable("isInitialized", PrimitiveType.Bool, false,
                span));

        BoundExpression borrowedCapability = new BoundPrivatePlaceRead(3,
            new BoundPrivateLocalPlace(1, initializationReferenceType),
            initializationReferenceType, span);
        var referent = new BoundPrivateReferentPlace(borrowedCapability,
            PrimitiveType.Int32);
        var body = new BoundBlock([
            new BoundVacantLocal(0, span),
            new BoundVacantLocal(3, span),
            new BoundLocal(1, new BoundPrivateReferenceForm(1,
                new BoundPrivateLocalPlace(0, PrimitiveType.Int32),
                initializationReferenceType, span), span),
            new BoundLocal(5, new BoundPrivatePlaceIsInitialized(
                referent, span), span),
            new BoundExpressionStatement(new BoundPrivateCheckedStore(1,
                new BoundPrivateReferentPlace(borrowedCapability,
                    PrimitiveType.Int32),
                new BoundConstant(41, PrimitiveType.Int32, span), span),
                span),
            new BoundLocal(2, new BoundInitializationAttenuation(
                borrowedCapability, ordinaryReferenceType, span), span),
            new BoundLocal(4, new BoundImplicitReferenceMaterialize(3,
                new BoundConstant(42, PrimitiveType.Int32, span),
                ordinaryReferenceType, span), span),
            new BoundReturn(null, span),
        ], span) {
            IsManagedLexicalScope = true,
        };
        var function = new BoundFunction("RoundTrip",
            PrimitiveType.Void, variables, 0, body, span);
        var remapVariables = ImmutableArray.Create(
            new BoundVariable("padding", PrimitiveType.Int32, false, span))
            .AddRange(variables);
        var remappedFunction = BoundTreeTraversal.RewriteVariables(
            function, remapVariables, static variable => variable + 1);
        var remappedFormation = (BoundPrivateReferenceForm)
            ((BoundLocal)remappedFunction.Body.Statements[2]).Initializer;
        Assert.That(((BoundPrivateLocalPlace)remappedFormation.Place)
            .Variable, Is.EqualTo(1));
        var remappedMaterialization =
            (BoundImplicitReferenceMaterialize)
                ((BoundLocal)remappedFunction.Body.Statements[6])
                    .Initializer;
        Assert.That(remappedMaterialization.BackingVariable,
            Is.EqualTo(4));
        var program = new BoundProgram([function], -1) {
            CheckedReferenceTypes = checkedReferenceTypes,
            RuntimePolicy = BoundRuntimePolicy.FailFast,
        };

        var producer = ModuleCompiler.CreateProducer(program, ["RoundTrip"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            producer.AsSpan(sizeof(int), sizeof(int))), Is.EqualTo(106));
        var persistedModule = ModuleCompiler
            .ReadProviderContractSnapshot(producer).ParsedModule;
        Assert.That(persistedModule.CheckedReferenceTypes.Any(
                static reference => reference.IsInitialization),
            Is.True, string.Join(Environment.NewLine,
                persistedModule.CheckedReferenceTypes));
        var persistedInitializationReferenceType
            = CheckedReferenceTypes.Type(
                persistedModule.CheckedReferenceTypes
                    .Select((reference, index) => (reference, index))
                    .Single(static item =>
                        item.reference.IsInitialization).index);
        var persistedOrdinaryReferenceType = CheckedReferenceTypes.Type(
            persistedModule.CheckedReferenceTypes
                .Select((reference, index) => (reference, index))
                .Single(static item =>
                    !item.reference.IsInitialization
                    && !item.reference.IsOptional).index);
        Assert.That(FindEnvelopeOffset(producer, 49, 0, 0,
                persistedInitializationReferenceType),
            Is.GreaterThanOrEqualTo(0));
        Assert.That(FindEnvelopeOffset(producer, 51, 0, 0,
                PrimitiveType.Bool),
            Is.GreaterThanOrEqualTo(0));
        Assert.That(FindEnvelopeOffset(producer, 53, 0, 0,
                PrimitiveType.Void),
            Is.GreaterThanOrEqualTo(0));
        Assert.That(FindEnvelopeOffset(producer, 52, 0, 0,
                persistedOrdinaryReferenceType),
            Is.GreaterThanOrEqualTo(0));
        Assert.That(FindEnvelopeOffset(producer, 54, 0, 0,
                persistedOrdinaryReferenceType),
            Is.GreaterThanOrEqualTo(0));

        var malformed = (byte[])producer.Clone();
        var attenuationOffset = FindEnvelopeOffset(malformed, 52, 0, 0,
            persistedOrdinaryReferenceType);
        BinaryPrimitives.WriteInt32LittleEndian(
            malformed.AsSpan(attenuationOffset + 1, sizeof(int)),
            (int)persistedInitializationReferenceType);
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));

        malformed = (byte[])producer.Clone();
        var materializeOffset = FindEnvelopeOffset(malformed, 54, 0, 0,
            persistedOrdinaryReferenceType);
        BinaryPrimitives.WriteInt32LittleEndian(
            malformed.AsSpan(materializeOffset + 13, sizeof(int)),
            int.MaxValue);
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));
    }

    [Test]
    public void ImplicitReferenceFormalsAndCallAdaptationsRoundTripCanonically()
    {
        const string source = """
            public struct Unused {
                internal int32 Field;
            }
            public struct Triple {
                internal int32 First;
            }
            public int32 Read(implicit ref<int32> value) { return value; }
            public int32 Forward(implicit ref<int32> value) { return Read(value); }
            public int32 FromPlace() {
                mut int32 value = int32(40);
                return Read(value);
            }
            public int32 FromValue() {
                return Read(int32(40) + int32(2));
            }
            public int32 ReadTriple(implicit ref<Triple> value) {
                return 0;
            }
            public void AcceptTriple(implicit ref<Triple> value) {}
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var tripleType = (PrimitiveType)(100
            + global::System.Linq.Enumerable.Single(
                global::System.Linq.Enumerable.Select(
                    program.Structures,
                    static (structure, index) => (structure, index)),
                static item => item.structure.Name == "Triple").index);
        var producerInputs = PrivateModuleIdentityInputs.Create(program,
            "provider",
            [new PrivateNominalDefinition(tripleType, "types.triple",
                IsExported: true)], []);
        var exports = new[] {
            "Read", "Forward", "FromPlace", "FromValue", "ReadTriple",
            "AcceptTriple",
        };
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(program, exports));
        _ = Assert.Throws<ArgumentException>(() =>
            ModuleCompiler.CreateProducer(program with { }, exports,
                producerInputs));
        var producer = ModuleCompiler.CreateProducer(program,
            exports, producerInputs);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            producer.AsSpan(sizeof(int), sizeof(int))), Is.EqualTo(106));
        Assert.That(FindEnvelopeOffset(producer, 49, -1, -1),
            Is.GreaterThanOrEqualTo(0));
        Assert.That(FindEnvelopeOffset(producer, 50, -1, -1),
            Is.GreaterThanOrEqualTo(0));
        Assert.That(FindEnvelopeOffset(producer, 54, -1, -1),
            Is.GreaterThanOrEqualTo(0));

        var reference = ModuleCompiler.CreateReference(producer);
        Assert.That(reference.Structures, Has.Length.EqualTo(1));
        Assert.That(reference.Structures[0].Name, Is.EqualTo("Triple"));
        var neutralCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Other() { return int32(1); }"));
        Assert.That(neutralCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, neutralCompilation.Diagnostics));
        var neutralProducer = ModuleCompiler.CreateProducer(
            neutralCompilation.Program!, ["Other"]);
        var setReference = ModuleCompiler.CreateReferenceSetFromReferences(
            [reference, ModuleCompiler.CreateReference(neutralProducer)]);
        Assert.That(setReference.Structures, Has.Length.EqualTo(1));
        Assert.That(setReference.CheckedReferenceTypes,
            Is.EqualTo(reference.CheckedReferenceTypes));
        Assert.That(setReference.PrivateNominalTypeOrigins,
            Has.Length.EqualTo(1));
        var nominalKey = Encoding.UTF8.GetBytes("types.triple");
        var nominalKeyOffset = FindBytesOffset(producer, nominalKey);
        Assert.That(nominalKeyOffset, Is.GreaterThanOrEqualTo(0));
        var malformed = (byte[])producer.Clone();
        var originKindOffset = nominalKeyOffset - 12
            - "provider".Length;
        Assert.That(originKindOffset, Is.GreaterThanOrEqualTo(0));
        malformed[originKindOffset] = byte.MaxValue;
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));
        var read = global::System.Linq.Enumerable.Single(
            reference.Functions, static function => function.Name == "Read");
        var formal = read.Variables[0];
        Assert.That(formal.IsImplicitRef, Is.True);
        Assert.That(formal.IsCanonicalImplicitReference, Is.True);
        Assert.That(CheckedReferenceTypes.IsType(formal.Type), Is.True);
        var referenceType = reference.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(formal.Type)];
        Assert.That(referenceType.ReferentType,
            Is.EqualTo(PrimitiveType.Int32));
        Assert.That(referenceType.IsWritable, Is.False);
        Assert.That(referenceType.IsGarbageCollected, Is.False);
        Assert.That(referenceType.IsInitialization, Is.False);
        Assert.That(referenceType.IsOptional, Is.False);
        var readTriple = global::System.Linq.Enumerable.Single(
            reference.Functions,
            static function => function.Name == "ReadTriple");
        var tripleReferenceType = reference.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(readTriple.Variables[0].Type)];
        Assert.That(tripleReferenceType.ReferentType,
            Is.EqualTo((PrimitiveType)100));

        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                public int32 Main() {
                    mut int32 value = int32(40);
                    return Forward(value);
                }
                """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program!;
        var consumerTripleType = (PrimitiveType)(100
            + global::System.Linq.Enumerable.Single(
                global::System.Linq.Enumerable.Select(
                    consumerProgram.Structures,
                    static (structure, index) => (structure, index)),
                static item => item.structure.Name == "Triple").index);
        _ = Assert.Throws<ArgumentException>(() =>
            PrivateModuleIdentityInputs.Create(consumerProgram, "consumer",
                    [],
                    [new PrivateNominalImport(consumerTripleType,
                        "types.triple.proxy", "other-provider",
                        "types.triple")]));
        var consumerInputs = PrivateModuleIdentityInputs.Create(
            consumerProgram, "consumer", [],
            [new PrivateNominalImport(consumerTripleType,
                "types.triple.proxy", "provider", "types.triple")]);
        var consumer = ModuleCompiler.CreateConsumer(
            consumerProgram, producer, consumerInputs);
        Assert.That(consumer.Length, Is.GreaterThan(0));

        var setConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                public int32 Main() {
                    mut int32 value = int32(40);
                    return Forward(value);
                }
                """), setReference);
        Assert.That(setConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                setConsumerCompilation.Diagnostics));
        var setConsumerProgram = setConsumerCompilation.Program!;
        var setConsumerTripleType = (PrimitiveType)(100
            + global::System.Linq.Enumerable.Single(
                global::System.Linq.Enumerable.Select(
                    setConsumerProgram.Structures,
                    static (structure, index) => (structure, index)),
                static item => item.structure.Name == "Triple").index);
        var setConsumerInputs = PrivateModuleIdentityInputs.Create(
            setConsumerProgram, "setconsumer", [],
            [new PrivateNominalImport(setConsumerTripleType,
                "types.triple.proxy", "provider", "types.triple")]);
        var consumerSet = ModuleCompiler.CreateConsumerSet(
            setConsumerProgram, [producer, neutralProducer],
            setConsumerInputs);
        ModuleCompiler.ValidateConsumerSet(consumerSet,
            [producer, neutralProducer]);

        var nativeCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public struct Triple {
                internal int32 First;
            }
            public int32 GetValue() { return int32(1); }
            """));
        Assert.That(nativeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, nativeCompilation.Diagnostics));
        var nativeProgram = nativeCompilation.Program!;
        var nativeTripleType = (PrimitiveType)(100
            + global::System.Linq.Enumerable.Single(
                global::System.Linq.Enumerable.Select(
                    nativeProgram.Structures,
                    static (structure, index) => (structure, index)),
                static item => item.structure.Name == "Triple").index);
        var nativeInputs = PrivateModuleIdentityInputs.Create(
            nativeProgram, "nativeprovider",
            [new PrivateNominalDefinition(nativeTripleType,
                "types.triple", IsExported: true)], []);
        var nativeSemanticProducer = ModuleCompiler.CreateProducer(
            nativeProgram, ["GetValue"], nativeInputs);
        Assert.That(ModuleCompiler.CreateReference(
                nativeSemanticProducer).PrivateNominalTypeOrigins,
            Has.Length.EqualTo(1));
        var nativeProducer = NativeModuleCompiler.CreateProducer(
            nativeProgram, ["GetValue"], "nativeprovider",
            NativeRuntimePolicy.FailFast, nativeInputs);
        var nativeNeutralProducer = NativeModuleCompiler.CreateProducer(
            neutralCompilation.Program!, ["Other"], "neutralprovider",
            NativeRuntimePolicy.FailFast);
        var nativeProviderReference =
            NativeModuleCompiler.CreateReference(nativeProducer);
        Assert.That(nativeProviderReference.PrivateNominalTypeOrigins,
            Has.Length.EqualTo(1));
        var nativeReferenceSet = NativeModuleCompiler.CreateReferenceSet(
            [nativeProducer, nativeNeutralProducer]);
        Assert.That(nativeReferenceSet.PrivateNominalTypeOrigins,
            Has.Length.EqualTo(1));
        var nativeConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                public int32 Main() { return GetValue(); }
                """), nativeReferenceSet);
        Assert.That(nativeConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                nativeConsumerCompilation.Diagnostics));
        var nativeConsumerProgram = nativeConsumerCompilation.Program!;
        var nativeConsumerTripleType = (PrimitiveType)(100
            + global::System.Linq.Enumerable.Single(
                global::System.Linq.Enumerable.Select(
                    nativeConsumerProgram.Structures,
                    static (structure, index) => (structure, index)),
                static item => item.structure.Name == "Triple").index);
        var nativeConsumerInputs = PrivateModuleIdentityInputs.Create(
            nativeConsumerProgram, "nativeconsumer", [],
            [new PrivateNominalImport(nativeConsumerTripleType,
                "types.triple.proxy", "nativeprovider", "types.triple")]);
        var nativeConsumerSet = NativeModuleCompiler.CreateConsumerSet(
            nativeConsumerProgram, [nativeProducer, nativeNeutralProducer],
            NativeRuntimePolicy.FailFast, nativeConsumerInputs);
        NativeModuleCompiler.ValidateConsumerSet(nativeConsumerSet,
            [nativeProducer, nativeNeutralProducer]);

        var graphProducerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public struct Triple {
                    internal int32 First;
                }
                public int32 Proxy() { return Other(); }
                """), NativeModuleCompiler.CreateReference(
                nativeNeutralProducer));
        Assert.That(graphProducerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                graphProducerCompilation.Diagnostics));
        var graphProgram = graphProducerCompilation.Program!;
        var graphTripleType = (PrimitiveType)(100
            + global::System.Linq.Enumerable.Single(
                global::System.Linq.Enumerable.Select(
                    graphProgram.Structures,
                    static (structure, index) => (structure, index)),
                static item => item.structure.Name == "Triple").index);
        var graphInputs = PrivateModuleIdentityInputs.Create(graphProgram,
            "graphmodule",
            [new PrivateNominalDefinition(graphTripleType,
                "types.triple", IsExported: true)], []);
        var graphProducer = NativeModuleCompiler.CreateGraphProducer(
            graphProgram, ["Proxy"], "graphprovider",
            [nativeNeutralProducer], NativeRuntimePolicy.FailFast,
            graphInputs);
        var graphReference = NativeModuleCompiler.CreateGraphReference(
            [graphProducer, nativeNeutralProducer]);
        var graphConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                public int32 Main() { return Proxy(); }
                """), graphReference);
        Assert.That(graphConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                graphConsumerCompilation.Diagnostics));
        var graphConsumerProgram = graphConsumerCompilation.Program!;
        var graphConsumerTripleType = (PrimitiveType)(100
            + global::System.Linq.Enumerable.Single(
                global::System.Linq.Enumerable.Select(
                    graphConsumerProgram.Structures,
                    static (structure, index) => (structure, index)),
                static item => item.structure.Name == "Triple").index);
        var graphConsumerInputs = PrivateModuleIdentityInputs.Create(
            graphConsumerProgram, "graphconsumer", [],
            [new PrivateNominalImport(graphConsumerTripleType,
                "types.triple.proxy", "graphmodule", "types.triple")]);
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            graphConsumerProgram, [graphProducer, nativeNeutralProducer],
            NativeRuntimePolicy.FailFast, graphConsumerInputs);
        NativeModuleCompiler.ValidateGraph(graphConsumer,
            [graphProducer, nativeNeutralProducer]);

        var classCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public class Widget {
                public int32 Value;
                public Widget(int32 value) { Value = value; }
            }
            public int32 GetValue() { return int32(1); }
            """));
        Assert.That(classCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, classCompilation.Diagnostics));
        var classProgram = classCompilation.Program!;
        var widgetType = (PrimitiveType)ManagedClassTypes.First;
        var classInputs = PrivateModuleIdentityInputs.Create(classProgram,
            "classprovider",
            [new PrivateNominalDefinition(widgetType, "types.widget",
                IsExported: true)], []);
        var classProducer = ModuleCompiler.CreateProducer(classProgram,
            ["GetValue"], classInputs);
        var classReference = ModuleCompiler.CreateReference(classProducer);
        Assert.That(classReference.Classes, Has.Length.EqualTo(1));
        Assert.That(classReference.Classes[0].Name, Is.EqualTo("Widget"));
        Assert.That(classReference.PrivateNominalTypeOrigins,
            Has.Length.EqualTo(1));
        var classConsumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            public int32 Main() { return GetValue(); }
            """), classReference);
        Assert.That(classConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                classConsumerCompilation.Diagnostics));
        var classConsumerInputs = PrivateModuleIdentityInputs.Create(
            classConsumerCompilation.Program!, "classconsumer", [],
            [new PrivateNominalImport(
                classReference.PrivateClassReferences[0].ClassType,
                "types.widget.proxy",
                "classprovider", "types.widget")]);
        var classConsumer = ModuleCompiler.CreateConsumer(
            classConsumerCompilation.Program!, classProducer,
            classConsumerInputs);
        ModuleCompiler.ValidateConsumerAgainstReference(classConsumer,
            classReference);

        var changedClassCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public class Widget {
                    public uint32 Value;
                    public Widget(uint32 value) { Value = value; }
                }
                public int32 GetValue() { return int32(1); }
                """));
        Assert.That(changedClassCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                changedClassCompilation.Diagnostics));
        var changedClassInputs = PrivateModuleIdentityInputs.Create(
            changedClassCompilation.Program!, "classprovider",
            [new PrivateNominalDefinition(widgetType, "types.widget",
                IsExported: true)], []);
        var changedClassProducer = ModuleCompiler.CreateProducer(
            changedClassCompilation.Program!, ["GetValue"],
            changedClassInputs);
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateConsumer(classConsumerCompilation.Program!,
                changedClassProducer, classConsumerInputs));

        var nestedClassCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Hidden {
                public int32 Value;
                public Hidden(int32 value) { Value = value; }
            }
            public class Gadget {
                private Hidden _value;
                public Gadget(int32 value) { _value = new Hidden(value); }
            }
            public int32 GetValue() { return int32(1); }
            """));
        Assert.That(nestedClassCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                nestedClassCompilation.Diagnostics));
        var nestedClassProgram = nestedClassCompilation.Program!;
        var hiddenType = (PrimitiveType)(100
            + global::System.Linq.Enumerable.Single(
                global::System.Linq.Enumerable.Select(
                    nestedClassProgram.Structures,
                    static (structure, index) => (structure, index)),
                static item => item.structure.Name == "Hidden").index);
        var nestedClassInputs = PrivateModuleIdentityInputs.Create(
            nestedClassProgram, "nestedprovider",
            [
                new PrivateNominalDefinition(widgetType,
                    "types.gadget", IsExported: true),
                new PrivateNominalDefinition(hiddenType,
                    "types.hidden", IsExported: false),
            ], []);
        var nestedClassProducer = ModuleCompiler.CreateProducer(
            nestedClassProgram, ["GetValue"], nestedClassInputs);
        var nestedClassReference = ModuleCompiler.CreateReference(
            nestedClassProducer);
        Assert.That(nestedClassReference.Structures, Has.Length.EqualTo(1));
        Assert.That(nestedClassReference.Structures[0].IsInternal, Is.True);
        Assert.That(nestedClassReference.PrivateClassReferences[0]
            .ExpectedSnapshot.Structures, Has.Length.EqualTo(1));
        var hiddenTypeCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main(Hidden value) { return 0; }"),
            nestedClassReference);
        Assert.That(hiddenTypeCompilation.HasErrors, Is.True,
            "A private class field dependency must not become a source lookup root.");
        var nestedConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return GetValue(); }"),
            nestedClassReference);
        Assert.That(nestedConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                nestedConsumerCompilation.Diagnostics));
        var nestedConsumerInputs = PrivateModuleIdentityInputs.Create(
            nestedConsumerCompilation.Program!, "nestedconsumer", [],
            [new PrivateNominalImport(
                nestedClassReference.PrivateClassReferences[0].ClassType,
                "types.gadget.proxy", "nestedprovider",
                "types.gadget")]);
        var nestedConsumer = ModuleCompiler.CreateConsumer(
            nestedConsumerCompilation.Program!, nestedClassProducer,
            nestedConsumerInputs);
        ModuleCompiler.ValidateConsumerAgainstReference(nestedConsumer,
            nestedClassReference);

        var changedNestedClassCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                struct Hidden {
                    public uint32 Value;
                    public Hidden(uint32 value) { Value = value; }
                }
                public class Gadget {
                    private Hidden _value;
                    public Gadget(int32 value) { _value = new Hidden(uint32(value)); }
                }
                public int32 GetValue() { return int32(1); }
                """));
        Assert.That(changedNestedClassCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                changedNestedClassCompilation.Diagnostics));
        var changedNestedProgram =
            changedNestedClassCompilation.Program!;
        var changedHiddenType = (PrimitiveType)(100
            + global::System.Linq.Enumerable.Single(
                global::System.Linq.Enumerable.Select(
                    changedNestedProgram.Structures,
                    static (structure, index) => (structure, index)),
                static item => item.structure.Name == "Hidden").index);
        var changedNestedInputs = PrivateModuleIdentityInputs.Create(
            changedNestedProgram, "nestedprovider",
            [
                new PrivateNominalDefinition(widgetType,
                    "types.gadget", IsExported: true),
                new PrivateNominalDefinition(changedHiddenType,
                    "types.hidden", IsExported: false),
            ], []);
        var changedNestedProducer = ModuleCompiler.CreateProducer(
            changedNestedProgram, ["GetValue"], changedNestedInputs);
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateConsumer(
                nestedConsumerCompilation.Program!,
                changedNestedProducer, nestedConsumerInputs));

        var classSetReference = ModuleCompiler.CreateReferenceSetFromReferences(
            [classReference, ModuleCompiler.CreateReference(neutralProducer)]);
        Assert.That(classSetReference.Classes, Has.Length.EqualTo(1));
        Assert.That(classSetReference.PrivateNominalTypeOrigins,
            Has.Length.EqualTo(1));

        var graphClassCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public class GraphWidget {
                    public int32 Value;
                    public GraphWidget(int32 value) { Value = value; }
                }
                public int32 Proxy() { return Other(); }
                """), NativeModuleCompiler.CreateReference(
                nativeNeutralProducer));
        Assert.That(graphClassCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                graphClassCompilation.Diagnostics));
        var graphClassProgram = graphClassCompilation.Program!;
        var graphWidgetType = (PrimitiveType)ManagedClassTypes.First;
        var graphClassInputs = PrivateModuleIdentityInputs.Create(
            graphClassProgram, "classgraphmodule",
            [new PrivateNominalDefinition(graphWidgetType,
                "types.graph-widget", IsExported: true)], []);
        var classGraphProducer = NativeModuleCompiler.CreateGraphProducer(
            graphClassProgram, ["Proxy"], "classgraph",
            [nativeNeutralProducer], NativeRuntimePolicy.FailFast,
            graphClassInputs);
        var classGraphReference = NativeModuleCompiler.CreateGraphReference(
            [classGraphProducer, nativeNeutralProducer]);
        Assert.That(classGraphReference.Classes, Has.Length.EqualTo(1));
        Assert.That(classGraphReference.PrivateNominalTypeOrigins,
            Has.Length.EqualTo(1));
        var nativeClassProducer = NativeModuleCompiler.CreateProducer(
            classProgram, ["GetValue"], "classnativeprovider",
            NativeRuntimePolicy.FailFast, classInputs);
        var nativeClassReferenceSet = NativeModuleCompiler.CreateReferenceSet(
            [nativeClassProducer, nativeNeutralProducer]);
        Assert.That(nativeClassReferenceSet.Classes,
            Has.Length.EqualTo(1));
        Assert.That(nativeClassReferenceSet.PrivateNominalTypeOrigins,
            Has.Length.EqualTo(1));
        var nativeClassConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                public int32 Main() { return GetValue(); }
                """), nativeClassReferenceSet);
        Assert.That(nativeClassConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                nativeClassConsumerCompilation.Diagnostics));
        var nativeClassConsumerProgram =
            nativeClassConsumerCompilation.Program!;
        var nativeClassConsumerInputs =
            PrivateModuleIdentityInputs.Create(
                nativeClassConsumerProgram, "classnativeconsumer", [],
                [new PrivateNominalImport(widgetType,
                    "types.widget.proxy", "classprovider",
                    "types.widget")]);
        var nativeClassConsumer = NativeModuleCompiler.CreateConsumerSet(
            nativeClassConsumerProgram,
            [nativeClassProducer, nativeNeutralProducer],
            NativeRuntimePolicy.FailFast, nativeClassConsumerInputs);
        NativeModuleCompiler.ValidateConsumerSet(nativeClassConsumer,
            [nativeClassProducer, nativeNeutralProducer]);
    }

    private static byte[] RewritePrivateGeneralDependencyRelationship(
        byte[] module, byte[] row, byte relationship)
    {
        if (relationship is not (1 or 2 or 3))
        {
            throw new ArgumentOutOfRangeException(nameof(relationship));
        }
        if (row.Length != 12 || row[2] != 3)
        {
            throw new ArgumentException(
                "The expected row must be a nonempty union Dependency.",
                nameof(row));
        }

        var offset = module.AsSpan().IndexOf(row);
        if (offset < 0 || module.AsSpan().LastIndexOf(row) != offset)
        {
            throw new InvalidDataException(
                "The private module has no unique general Dependency row to rewrite.");
        }

        var rewritten = module.ToArray();
        rewritten[offset + 2] = relationship;
        return rewritten;
    }

    private static int FindEnvelopeOffset(byte[] module, byte kind,
        int spanStart, int spanLength, PrimitiveType? type = null)
    {
        for (var offset = 0; offset <= module.Length - 13; offset++)
        {
            if (module[offset] == kind
                && (type is not PrimitiveType expectedType
                    || BinaryPrimitives.ReadInt32LittleEndian(
                        module.AsSpan(offset + 1, sizeof(int)))
                        == (int)expectedType)
                && (spanStart < 0
                    || BinaryPrimitives.ReadInt32LittleEndian(
                        module.AsSpan(offset + 5, sizeof(int))) == spanStart)
                && (spanLength < 0
                    || BinaryPrimitives.ReadInt32LittleEndian(
                        module.AsSpan(offset + 9, sizeof(int))) == spanLength))
            {
                return offset;
            }
        }

        return -1;
    }

    private static string CompilerApphost()
    {
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException(
                "Missing test configuration directory.");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        return Path.Combine(root, "artifacts", "bin", "sources",
            "terrumc", configuration, framework.Name, "terrumc.exe");
    }

    private static int FindBytesOffset(byte[] source, byte[] value)
    {
        for (var offset = 0; offset <= source.Length - value.Length;
            offset++)
        {
            if (source.AsSpan(offset, value.Length).SequenceEqual(value))
            {
                return offset;
            }
        }
        return -1;
    }
}
