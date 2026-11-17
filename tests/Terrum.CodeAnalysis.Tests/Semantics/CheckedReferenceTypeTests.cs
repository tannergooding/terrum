using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class CheckedReferenceTypeTests
{
    [TestCase("ref<int32>")]
    [TestCase("ref<ref<int32>>")]
    [TestCase("ref<int32>?")]
    [TestCase("ref<init int32>")]
    [TestCase("write ref<init int32>")]
    [TestCase("gc ref<int32>")]
    public void BindsCompleteReferenceTypeParameters(string type)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($"void Accept({type} value) {{}}"));

        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void RejectsGarbageCollectedInitializationReferences()
    {
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("void Accept(gc ref<init int32> value) {}"));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsGarbageCollectionQualificationOnScalarTypes()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("void Accept(gc int32 value) {}"));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void PrivateRevision106RoundTripsCheckedReferenceTypesAndRejectsMalformedRows()
    {
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public void Accept(ref<int32> value) {}
                public unsafe void AcceptPointer(ref<ptr<int32>> value) {}
                public void AcceptNested(ref<ref<int32>> value) {}
                public void AcceptOptional(ref<int32>? value) {}
                public void AcceptNestedOptional(ref<ref<int32>?>? value) {}
                """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var producer = ModuleCompiler.CreateProducer(compilation.Program!,
            ["Accept", "AcceptPointer", "AcceptNested", "AcceptOptional",
                "AcceptNestedOptional"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            producer.AsSpan(sizeof(int), sizeof(int))), Is.EqualTo(106));

        var reference = ModuleCompiler.CreateReference(producer);
        var declarations = reference.Functions;
        var scalarParameter = global::System.Linq.Enumerable.Single(
            declarations, static function => function.Name == "Accept")
            .Variables[0].Type;
        var pointerParameter = global::System.Linq.Enumerable.Single(
            declarations, static function => function.Name == "AcceptPointer")
            .Variables[0].Type;
        var nestedParameter = global::System.Linq.Enumerable.Single(
            declarations, static function => function.Name == "AcceptNested")
            .Variables[0].Type;
        var optionalParameter = global::System.Linq.Enumerable.Single(
            declarations, static function => function.Name == "AcceptOptional")
            .Variables[0].Type;
        var nestedOptionalParameter = global::System.Linq.Enumerable.Single(
            declarations,
            static function => function.Name == "AcceptNestedOptional")
            .Variables[0].Type;
        Assert.That((int)scalarParameter, Is.LessThan(0));
        Assert.That((int)pointerParameter, Is.LessThan(0));
        Assert.That((int)nestedParameter, Is.LessThan(0));
        Assert.That((int)optionalParameter, Is.LessThan(0));
        Assert.That((int)nestedOptionalParameter, Is.LessThan(0));
        Assert.That(pointerParameter, Is.Not.EqualTo(scalarParameter));
        Assert.That(nestedParameter, Is.Not.EqualTo(scalarParameter));
        Assert.That(optionalParameter, Is.Not.EqualTo(scalarParameter));
        Assert.That(nestedOptionalParameter,
            Is.Not.EqualTo(optionalParameter));

        var malformed = (byte[])producer.Clone();
        var typeRowOffset = FindPrivate106CheckedReferenceTypeRowOffset(
            malformed);
        var expectedScalarRow = new byte[8];
        expectedScalarRow[0] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(
            expectedScalarRow.AsSpan(4), (int)PrimitiveType.Int32);
        Assert.That(producer.AsSpan(typeRowOffset, expectedScalarRow.Length)
            .SequenceEqual(expectedScalarRow), Is.True);
        malformed[typeRowOffset + sizeof(byte) + sizeof(byte)] = 1;
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));

        malformed = (byte[])producer.Clone();
        typeRowOffset = FindPrivate106CheckedReferenceTypeRowOffset(
            malformed);
        malformed[typeRowOffset] = 0;
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));

        malformed = (byte[])producer.Clone();
        typeRowOffset = FindPrivate106CheckedReferenceTypeRowOffset(
            malformed);
        malformed[typeRowOffset + sizeof(byte)] = 0x80;
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));

        malformed = (byte[])producer.Clone();
        typeRowOffset = FindPrivate106CheckedReferenceTypeRowOffset(
            malformed);
        BinaryPrimitives.WriteUInt16LittleEndian(
            malformed.AsSpan(typeRowOffset + (2 * sizeof(byte)),
                sizeof(ushort)), 1);
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));

        malformed = (byte[])producer.Clone();
        typeRowOffset = FindPrivate106CheckedReferenceTypeRowOffset(
            malformed);
        BinaryPrimitives.WriteInt32LittleEndian(
            malformed.AsSpan(typeRowOffset + 4, sizeof(int)), -5);
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));

        malformed = (byte[])producer.Clone();
        typeRowOffset = FindPrivate106CheckedReferenceTypeRowOffset(
            malformed);
        BinaryPrimitives.WriteInt32LittleEndian(
            malformed.AsSpan(typeRowOffset + 4, sizeof(int)),
            (int)CheckedReferenceTypes.Type(0));
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));

        var neutralCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Neutral() { return 0; }
            """));
        Assert.That(neutralCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                neutralCompilation.Diagnostics));
        var neutralProducer = ModuleCompiler.CreateProducer(
            neutralCompilation.Program!, ["Neutral"]);
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReferenceSet(
                [malformed, neutralProducer]));
    }

    [Test]
    public void PrivateRevision106RejectsCyclesAcrossPointerAndReferenceRows()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public unsafe void Accept(ref<ptr<int32>> value) {}
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var parameterType = global::System.Linq.Enumerable.Single(
            program.Functions,
            static function => function.Name == "Accept").Variables[0].Type;
        var referenceType = CheckedReferenceTypes.Type(
            CheckedReferenceTypes.Index(parameterType));
        var producer = ModuleCompiler.CreateProducer(program, ["Accept"]);

        var malformedProgram = program with {
            NativePointerTypes = [
                .. program.NativePointerTypes.SetItem(0,
                    program.NativePointerTypes[0] with {
                        PointeeType = referenceType,
                    }),
            ],
        };
        _ = Assert.Throws<ModuleContractException>(
            () => ModuleCompiler.CreateProducer(malformedProgram, ["Accept"]));

        var malformed = (byte[])producer.Clone();
        var pointerTypeRowOffset
            = FindPrivate106NativePointerTypeRowOffset(malformed);
        BinaryPrimitives.WriteInt32LittleEndian(
            malformed.AsSpan(pointerTypeRowOffset, sizeof(int)),
            (int)referenceType);
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));
    }

    [Test]
    public void PrivateRevision106RejectsCyclesAcrossPointerAndFunctionPointerRows()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public unsafe void Accept(
                [Terrum.Interop.NativeCallConv(
                    Terrum.Interop.NativeCallingConvention.Unmanaged)]
                fnptr<ptr<int32>, int32> callback) {}
            """), NativeCoreRegistrationTests
                .CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var functionPointerType = NativeFunctionPointerTypes.Type(0);
        var malformedProgram = program with {
            NativePointerTypes = program.NativePointerTypes.SetItem(0,
                program.NativePointerTypes[0] with {
                    PointeeType = functionPointerType,
                }),
        };
        _ = Assert.Throws<ModuleContractException>(
            () => ModuleCompiler.CreateProducer(malformedProgram, ["Accept"]));
    }

    [Test]
    public void PrivateOptionalValueRowsRejectNestedAndManagedPayloads()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public void AcceptOptional(ref<int32>? value) {}
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        foreach (var innerType in new[] {
            CheckedReferenceTypes.Type(0),
            PrimitiveType.ManagedNode,
            PrimitiveType.OptionalManagedNode,
        })
        {
            var invalidProgram = program with {
                CheckedReferenceTypes = [
                    new CheckedReferenceTypeDefinition(innerType,
                        IsWritable: false, IsGarbageCollected: false,
                        IsInitialization: false, IsOptional: true,
                        IsOptionalValue: true),
                ],
                Functions = [
                    .. program.Functions.Select(function =>
                        function.Name == "AcceptOptional"
                            ? function with {
                                Variables = function.Variables.SetItem(0,
                                    function.Variables[0] with {
                                        Type = CheckedReferenceTypes.Type(0),
                                    }),
                            }
                            : function),
                ],
            };
            _ = Assert.Throws<ModuleContractException>(
                () => ModuleCompiler.CreateProducer(invalidProgram,
                    ["AcceptOptional"]));
        }
    }

    [Test]
    public void BindsAndPersistsNormalizedOptionalValueTypeForms()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            alias Maybe<T> = T?;
            alias Nested<T> = option<Maybe<T>>;
            public void AcceptSuffix(int32? value) {}
            public void AcceptConstructor(option<int32> value) {}
            public void AcceptNested(option<option<int32>> value) {}
            public void AcceptAlias(Maybe<int32> value) {}
            public void AcceptNestedAlias(Nested<int32> value) {}
            public void AcceptUnit(option<void> value) {}
            public unsafe void AcceptPointer(ptr<int32>? value) {}
            public unsafe void AcceptWritablePointer(
                write option<ptr<int32>> value) {}
            public unsafe void AcceptWritablePointerPayload(
                option<write ptr<int32>> value) {}
            public void AcceptReference(option<ref<int32>> value) {}
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        PrimitiveType ParameterType(string name)
        {
            return global::System.Linq.Enumerable.Single(program.Functions,
                function => function.Name == name).Variables[0].Type;
        }

        var scalarType = ParameterType("AcceptSuffix");
        Assert.That(ParameterType("AcceptConstructor"), Is.EqualTo(scalarType));
        Assert.That(ParameterType("AcceptNested"), Is.EqualTo(scalarType));
        Assert.That(ParameterType("AcceptAlias"), Is.EqualTo(scalarType));
        Assert.That(ParameterType("AcceptNestedAlias"),
            Is.EqualTo(scalarType));
        var writablePointerType = ParameterType("AcceptWritablePointer");
        Assert.That(ParameterType("AcceptWritablePointerPayload"),
            Is.EqualTo(writablePointerType));
        var writablePointerOptional = program.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(writablePointerType)];
        Assert.That(writablePointerOptional.IsOptionalValue, Is.True);
        Assert.That(program.NativePointerTypes[
                NativePointerTypes.Index(
                    writablePointerOptional.ReferentType)].IsWritable,
            Is.True);
        var scalarDefinition = program.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(scalarType)];
        Assert.That(scalarDefinition.IsOptionalValue, Is.True);
        Assert.That(scalarDefinition.ReferentType,
            Is.EqualTo(PrimitiveType.Int32));

        var unitType = ParameterType("AcceptUnit");
        var unitDefinition = program.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(unitType)];
        Assert.That(unitDefinition.IsOptionalValue, Is.True);
        Assert.That(unitDefinition.ReferentType,
            Is.EqualTo(PrimitiveType.Void));

        var pointerType = ParameterType("AcceptPointer");
        Assert.That(program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(pointerType)].IsOptionalValue,
            Is.True);
        Assert.That(ValueLayout.TryGetOptionalValueLayout(pointerType,
                program.Structures, program.CheckedReferenceTypes,
                program.NativeLoweringAgreement,
                program.NativeFixedArrayTypes, out var pointerOptionalLayout),
            Is.True);
        Assert.That(pointerOptionalLayout.UsesNullNiche, Is.True);
        Assert.That(pointerOptionalLayout.PayloadOffset, Is.Zero);
        Assert.That(pointerOptionalLayout.Size,
            Is.EqualTo((ulong)NativeTargetProfile.PointerSize));
        var referenceType = ParameterType("AcceptReference");
        Assert.That(program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(referenceType)].IsOptional,
            Is.True);
        Assert.That(program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(referenceType)].IsOptionalValue,
            Is.False);

        var producer = ModuleCompiler.CreateProducer(program, [
            "AcceptSuffix", "AcceptConstructor", "AcceptNested",
            "AcceptAlias", "AcceptNestedAlias", "AcceptUnit",
            "AcceptPointer", "AcceptWritablePointer",
            "AcceptWritablePointerPayload", "AcceptReference",
        ]);
        var reference = ModuleCompiler.CreateReference(producer);
        var persistedScalarType = global::System.Linq.Enumerable.Single(
            reference.Functions,
            static function => function.Name == "AcceptSuffix")
            .Variables[0].Type;
        Assert.That(reference.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(persistedScalarType)].IsOptionalValue,
            Is.True);
    }

    [Test]
    public void OptionalValueLayoutPreservesRequiredAlignmentBeyondNaturalAlignment()
    {
        var payloadType = (PrimitiveType)100;
        var payload = new BoundStruct("Payload", [], 32) {
            Alignment = 8,
            RequiredAlignment = 32,
        };
        var optionalPayload = new CheckedReferenceTypeDefinition(
            payloadType, IsWritable: false, IsGarbageCollected: false,
            IsInitialization: false, IsOptional: true,
            IsOptionalValue: true);
        var referenceTypes = ImmutableArray.Create(optionalPayload);

        Assert.That(ValueLayout.TryGetOptionalValueLayout(
                CheckedReferenceTypes.Type(0), [payload], referenceTypes,
                null, [], out var layout),
            Is.True);
        Assert.That(layout.Alignment, Is.EqualTo(8));
        Assert.That(layout.RequiredAlignment, Is.EqualTo(32));
        Assert.That(layout.PayloadOffset, Is.EqualTo(32));
        Assert.That(layout.Size, Is.EqualTo(64));
    }

    [Test]
    public void OptionalIntegerBackedEnumUsesTaggedLayout()
    {
        var enumIdentity = new BoundEnumDeclarationIdentity.Core(
            new CoreDeclarationIdentity("module", "OptionalInteger",
                CoreDeclarationKind.EnumType));
        var enumDefinition = new BoundEnumDefinition(enumIdentity,
            "OptionalInteger", PrimitiveType.Int32, [], true, true, false);
        var optionalEnum = new CheckedReferenceTypeDefinition(
            BoundEnumTypes.Type(0), IsWritable: false,
            IsGarbageCollected: false, IsInitialization: false,
            IsOptional: true, IsOptionalValue: true);
        var referenceTypes = ImmutableArray.Create(optionalEnum);

        Assert.That(ValueLayout.TryGetOptionalValueLayout(
                CheckedReferenceTypes.Type(0), [], referenceTypes, null, [],
                out var layout, [enumDefinition]),
            Is.True);
        Assert.That(layout.PayloadOffset, Is.EqualTo(4));
        Assert.That(layout.Size, Is.EqualTo(8));
        Assert.That(layout.Alignment, Is.EqualTo(4));
        Assert.That(layout.UsesNullNiche, Is.False);
    }

    [Test]
    public void OptionalClassHandlesDoNotUseGeneralTaggedValueLayout()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            class Node {
                public int32 Value;
                public Node(int32 value) { Value = value; }
            }
            void Accept(Node? value) {}
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var optionalType = program.Functions.Single(
            static function => function.Name == "Accept")
            .Variables[0].Type;
        Assert.That(ManagedClassTypes.IsOptional(optionalType), Is.True);
        Assert.That(ValueLayout.TryGetOptionalValueLayout(optionalType,
                program.Structures, program.CheckedReferenceTypes,
                program.NativeLoweringAgreement,
                program.NativeFixedArrayTypes, out _),
            Is.False);
        Assert.That(ValueLayout.Size(optionalType, program.Structures,
                program.CheckedReferenceTypes,
                program.NativeLoweringAgreement,
                program.NativeFixedArrayTypes),
            Is.EqualTo((ulong)NativeTargetProfile.PointerSize));
    }

    [Test]
    public void OptionalValueFieldsUseCompletedTaggedLayouts()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct OptionalValues {
                bool? Flag;
                int32? Value;
            }
            void Accept(OptionalValues values) {}
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var structure = global::System.Linq.Enumerable.Single(
            compilation.Program!.Structures,
            static candidate => candidate.Name == "OptionalValues");
        Assert.That(structure.Fields.Select(static field =>
                (field.Offset, field.Size)),
            Is.EqualTo([(0UL, 2UL), (4UL, 8UL)]));
        Assert.That(structure.Size, Is.EqualTo(12));
        Assert.That(structure.Alignment, Is.EqualTo(4));
    }

    [Test]
    public void OptionalScalarFieldsCarryTheirBlittableTagLayout()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct OptionalValues {
                public bool? Flag;
                public int32? Value;
            }
            public void Accept(OptionalValues values) {}
            """), NativeCoreRegistrationTests
                .CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var structure = global::System.Linq.Enumerable.Single(
            compilation.Program!.Structures,
            static candidate => candidate.Name == "OptionalValues");
        Assert.That(structure.BlittableTargetRequirement,
            Is.EqualTo(FixedLayoutStorage.WindowsX64TargetRequirement));
        Assert.That(structure.Fields.Select(static field =>
                (field.Offset, field.Size)),
            Is.EqualTo([(0UL, 2UL), (4UL, 8UL)]));
        Assert.That(structure.Size, Is.EqualTo(12));
        Assert.That(structure.Alignment, Is.EqualTo(4));
    }

    [Test]
    public void OptionalLayoutsComposeWithAdmittedFixedArrays()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            alias Values = [int32; 2];
            alias Maybe<T> = T?;
            public int32 Accept() {
                init Maybe<Values> values;
                return 0;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var arrayType = NativeFixedArrayTypes.Type(0);
        Assert.That(NativeFixedArrayContract.TryGetLayout(
            arrayType, program.Structures,
            program.NativeFixedArrayTypes, out var elementArrayLayout,
            program.CheckedReferenceTypes), Is.True);
        Assert.That(elementArrayLayout.ElementStride, Is.EqualTo(4));
        Assert.That(elementArrayLayout.ByteExtent, Is.EqualTo(8));

        var optionalArrayType = global::System.Linq.Enumerable.Single(
            program.Functions,
            static function => function.Name == "Accept").Variables.Single(
            static variable => variable.Name == "values").Type;
        var optionalArrayDefinition = program.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(optionalArrayType)];
        Assert.That(optionalArrayDefinition.IsOptionalValue, Is.True);
        Assert.That(optionalArrayDefinition.ReferentType,
            Is.EqualTo(arrayType));
        Assert.That(ValueLayout.TryGetOptionalValueLayout(optionalArrayType,
                program.Structures, program.CheckedReferenceTypes,
                program.NativeLoweringAgreement,
                program.NativeFixedArrayTypes, out var optionalLayout),
            Is.True);
        Assert.That(optionalLayout.PayloadOffset, Is.EqualTo(4));
        Assert.That(optionalLayout.Size, Is.EqualTo(12));

        var elementCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct OptionalElementArray {
                [int32?; 2] Values;
            }
            void Accept() {}
            """));
        Assert.That(elementCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                elementCompilation.Diagnostics));
        var elementProgram = elementCompilation.Program!;
        var optionalElementType = global::System.Linq.Enumerable.Single(
            elementProgram.CheckedReferenceTypes,
            static referenceType => referenceType.IsOptionalValue
                && referenceType.ReferentType == PrimitiveType.Int32);
        var optionalElementTypeIndex
            = elementProgram.CheckedReferenceTypes.IndexOf(
                optionalElementType);
        var optionalElementArray = global::System.Linq.Enumerable.Single(
            elementProgram.NativeFixedArrayTypes,
            array => array.ElementType == CheckedReferenceTypes.Type(
                optionalElementTypeIndex));
        Assert.That(optionalElementArray.ElementStride, Is.EqualTo(8));
        Assert.That(optionalElementArray.ByteExtent, Is.EqualTo(16));
        var optionalElementArrayIndex
            = elementProgram.NativeFixedArrayTypes.IndexOf(
                optionalElementArray);
        var optionalElementArrayType
            = NativeFixedArrayTypes.Type(optionalElementArrayIndex);
        Assert.That(ValueLayout.Size(optionalElementArrayType,
                elementProgram.Structures,
                elementProgram.CheckedReferenceTypes,
                elementProgram.NativeLoweringAgreement,
                elementProgram.NativeFixedArrayTypes),
            Is.EqualTo(16));
        Assert.That(ValueLayout.Alignment(optionalElementArrayType,
                elementProgram.Structures,
                elementProgram.CheckedReferenceTypes,
                elementProgram.NativeLoweringAgreement,
                elementProgram.NativeFixedArrayTypes),
            Is.EqualTo(4));
        Assert.That(ValueLayout.RequiredAlignment(optionalElementArrayType,
                elementProgram.Structures,
                elementProgram.NativeFixedArrayTypes,
                elementProgram.CheckedReferenceTypes),
            Is.EqualTo(1));
    }

    private static int FindPrivate106CheckedReferenceTypeRowOffset(
        byte[] module)
    {
        var offset = 2 * sizeof(int);
        SkipPrivateString(module, ref offset);
        offset += 3 * sizeof(byte);
        SkipPrivateString(module, ref offset);
        Assert.That(ReadInt32(module, ref offset), Is.Zero);
        offset += sizeof(int);

        for (var tableIndex = 0; tableIndex < 4; tableIndex++)
        {
            Assert.That(ReadInt32(module, ref offset), Is.Zero);
        }

        var pointerTypeCount = ReadInt32(module, ref offset);
        offset = checked(offset + (pointerTypeCount
            * (sizeof(int) + sizeof(byte))));
        Assert.That(ReadInt32(module, ref offset), Is.Zero);
        Assert.That(ReadInt32(module, ref offset), Is.EqualTo(6));
        return offset;
    }

    private static int FindPrivate106NativePointerTypeRowOffset(
        byte[] module)
    {
        var offset = 2 * sizeof(int);
        SkipPrivateString(module, ref offset);
        offset += 3 * sizeof(byte);
        SkipPrivateString(module, ref offset);
        Assert.That(ReadInt32(module, ref offset), Is.Zero);
        offset += sizeof(int);

        for (var tableIndex = 0; tableIndex < 4; tableIndex++)
        {
            Assert.That(ReadInt32(module, ref offset), Is.Zero);
        }

        Assert.That(ReadInt32(module, ref offset),
            Is.GreaterThanOrEqualTo(1));
        return offset;
    }

    private static int ReadInt32(byte[] data, ref int offset)
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(
            data.AsSpan(offset, sizeof(int)));
        offset += sizeof(int);
        return value;
    }

    private static void SkipPrivateString(byte[] data, ref int offset)
    {
        var length = ReadInt32(data, ref offset);
        offset = checked(offset + length);
    }

    [Test]
    public void RejectsWritesThroughReadonlyReferences()
    {
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("void Set(ref<int32> value) { *value = int32(7); }"));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsWritableReferenceFormationFromImmutablePlaces()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            void Set(write ref<int32> value) {}
            int32 Main() {
                int32 local = int32(1);
                Set(ref local);
                return 42;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsReturningReferenceToLocalStorage()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            ref<int32> Escape() {
                int32 local = int32(1);
                return ref local;
            }
            int32 Main() { return *Escape(); }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsReferenceToLocalStorageEscapingItsScope()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut int32 outer = int32(0);
                mut write ref<int32> alias = ref outer;
                {
                    mut int32 inner = int32(1);
                    alias = ref inner;
                }
                return *alias;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsReferenceReborrowThatAmplifiesWriteAuthority()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut int32 local = int32(1);
                ref<int32> readonlyReference = ref local;
                write ref<int32> writableReference = ref *readonlyReference;
                return *writableReference;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsReborrowEscapingItsSourceDescriptorLifetime()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut int32 local = int32(1);
                mut ref<int32> outer = ref local;
                {
                    ref<int32> inner = ref local;
                    outer = ref *inner;
                }
                return *outer;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsWritableReferenceToReadonlyField()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Cell {
                public int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            int32 Main() {
                mut Cell cell = new Cell(int32(1));
                write ref<int32> value = ref cell.Value;
                return *value;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsFieldReferenceEscapingItsStorageScope()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Cell {
                public mut int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            int32 Main() {
                mut int32 result = int32(0);
                mut ref<int32> escaped = ref result;
                {
                    mut Cell cell = new Cell(int32(42));
                    escaped = ref cell.Value;
                }
                return *escaped;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.HasErrors, Is.True);
    }
    [Test]
    public void RejectsFixedArrayElementReferenceEscapingItsStorageScope()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut [uint32; 3] outer = [1, 2, 3];
                mut ref<uint32> escaped = ref outer[0];
                {
                    mut [uint32; 3] inner = [4, 5, 6];
                    escaped = ref inner[1];
                }
                return int32(*escaped);
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsRetiringUniqueOwnerWhileItsFieldReferenceIsLive()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct ReferenceOwner {
                public mut int32 Value;
                public ReferenceOwner(int32 value) { Value = value; }
            }
            int32 Main() {
                mut ReferenceOwner owner = new ReferenceOwner(int32(1));
                ref<int32> value = ref owner.Value;
                owner = _;
                return *value;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void PreservesSiblingFieldReferenceAcrossConditionalPartialOwnerMove()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            unique struct Pair {
                public mut OwnedInt32Buffer Owned;
                public mut int32 Value;
                public Pair(OwnedInt32Buffer owned, int32 value) {
                    Owned = move owned;
                    Value = value;
                }
            }
            int32 ReadAfterOptionalMove(bool shouldMove, OwnedInt32Buffer owned) {
                mut Pair pair = new Pair(move owned, int32(42));
                ref<int32> value = ref pair.Value;
                if (shouldMove) {
                    OwnedInt32Buffer extracted = move pair.Owned;
                }
                return *value;
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void RejectsReferenceToIncompleteUniqueReceiverAsAWhole()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct PartialCell {
                public mut int32 First;
                public mut int32 Second;
                public PartialCell(int32 first, int32 second) {
                    First = first;
                    ref<PartialCell> whole = ref this;
                    Second = second;
                }
            }
            int32 Main() { return 42; }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }
}
