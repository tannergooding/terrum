using System;
using System.Buffers.Binary;
using System.Text;
using System.Collections.Immutable;
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
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var producer = ModuleCompiler.CreateProducer(compilation.Program!,
            ["Read", "Form", "Set", "IsReady"]);
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
        Assert.That(FindEnvelopeOffset(producer, 49, 0, 0,
                initializationReferenceType),
            Is.GreaterThanOrEqualTo(0));
        Assert.That(FindEnvelopeOffset(producer, 51, 0, 0,
                PrimitiveType.Bool),
            Is.GreaterThanOrEqualTo(0));
        Assert.That(FindEnvelopeOffset(producer, 53, 0, 0,
                PrimitiveType.Void),
            Is.GreaterThanOrEqualTo(0));
        Assert.That(FindEnvelopeOffset(producer, 52, 0, 0,
                ordinaryReferenceType),
            Is.GreaterThanOrEqualTo(0));
        Assert.That(FindEnvelopeOffset(producer, 54, 0, 0,
                ordinaryReferenceType),
            Is.GreaterThanOrEqualTo(0));
        _ = ModuleCompiler.CreateReference(producer);

        var malformed = (byte[])producer.Clone();
        var attenuationOffset = FindEnvelopeOffset(malformed, 52, 0, 0,
            ordinaryReferenceType);
        BinaryPrimitives.WriteInt32LittleEndian(
            malformed.AsSpan(attenuationOffset + 1, sizeof(int)),
            (int)initializationReferenceType);
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));

        malformed = (byte[])producer.Clone();
        var materializeOffset = FindEnvelopeOffset(malformed, 54, 0, 0,
            ordinaryReferenceType);
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
