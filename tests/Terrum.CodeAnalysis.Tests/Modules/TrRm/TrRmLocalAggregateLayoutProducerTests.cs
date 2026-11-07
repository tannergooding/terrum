// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmLocalAggregateLayoutProducerTests
{
    private const string SchemaIdentity
        = "trrm/1-draft-local-aggregate-layout-execution-1";

    [Test]
    public void EmitStandardProducesLocalAggregateRegisterBodies()
    {
        const string moduleIdentity = "l1-local-register-body-producer";
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1, Alignment: 32)]
            public struct Details {
                public mut [uint8; 2] Values;
            }

            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1, Alignment: 32)]
            public struct Packet {
                public mut uint8 Tag;
                public mut Details Details;

                internal Packet() {
                    Tag = uint8(0);
                    Details.Values = [uint8(0), uint8(1)];
                }
            }

            uint8 ProduceTag() {
                mut Packet staged = new Packet();
                staged.Tag = uint8(1);
                return staged.Tag;
            }

            uint8 Echo(uint8 value) {
                return value;
            }

            Packet CopyPacket(Packet value) {
                return value;
            }

            int32 Main() {
                mut Packet packet = CopyPacket(new Packet());
                packet.Tag = Echo(ProduceTag());
                mut uint8 observedTag = packet.Tag;
                mut [uint8; 2] values = [uint8(0), uint8(1)];
                values[0] = uint8(0);
                mut uint8 observedElement = values[0];
                packet.Details.Values[0] = ProduceTag();
                mut uint8 nestedElement = packet.Details.Values[0];
                mut int32 selectedResult = int32(0);
                if (observedElement == uint8(0)) {
                    selectedResult = int32(1);
                } else {
                    selectedResult = int32(2);
                }
                return selectedResult;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        if (compilation.HasErrors && compilation.Program is BoundProgram invalidProgram)
        {
            try
            {
                SemanticVerifier.Verify(invalidProgram);
            }
            catch (ArgumentException exception)
            {
                TestContext.WriteLine(exception);
            }
        }
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var sourceConstructor = program.Functions.Single(
            static function => function.IsConstructor);
        Assert.That((sourceConstructor.IsPrivate,
            sourceConstructor.IsInternal), Is.EqualTo((false, true)));

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, SchemaIdentity, 1);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        Assert.That(index.Schema,
            Is.EqualTo(TrRmArtifactSchema.LocalAggregateLayoutDraft));
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        Assert.That(tables.Methods.Count, Is.EqualTo(5));
        Assert.That(tables.Bodies.Count, Is.EqualTo(5));
        Assert.That(tables.Modules.GetEntryMethod(1), Is.GreaterThan(0));
        var constructorMethod = 0;
        var mainRow = 0;
        var produceTagRow = 0;
        var echoRow = 0;
        var copyPacketRow = 0;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            var name = tables.Methods.GetName(methodRow);
            if (name.EndsWith("#ctor", StringComparison.Ordinal))
            {
                constructorMethod = methodRow;
            }
            else if (name == "Main")
            {
                mainRow = methodRow;
            }
            else if (name == "ProduceTag")
            {
                produceTagRow = methodRow;
            }
            else if (name == "Echo")
            {
                echoRow = methodRow;
            }
            else if (name == "CopyPacket")
            {
                copyPacketRow = methodRow;
            }
        }
        Assert.That(constructorMethod, Is.GreaterThan(0));
        Assert.That(mainRow, Is.GreaterThan(0));
        Assert.That(produceTagRow, Is.GreaterThan(0));
        Assert.That(echoRow, Is.GreaterThan(0));
        Assert.That(copyPacketRow, Is.GreaterThan(0));
        Assert.That(tables.Methods.GetFlags(constructorMethod) & 3,
            Is.Zero, "A normalized local constructor helper must be private.");
        Assert.That((sourceConstructor.IsPrivate,
            sourceConstructor.IsInternal), Is.EqualTo((false, true)),
            "Normalization must not rewrite source visibility.");
        var mainBody = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(mainRow), tables.Contracts.Count);
        Assert.That(mainBody.BlockCount, Is.GreaterThan(1),
            "The conditional Body must retain its explicit CFG blocks.");
        var produceTagMethodMapIndex = -1;
        var echoMethodMapIndex = -1;
        for (var mapOffset = 0; mapOffset < mainBody.MethodMap.Length;
            mapOffset += sizeof(uint))
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(
                    mainBody.MethodMap[mapOffset..])
                == (0x1600_0000u | (uint)produceTagRow))
            {
                produceTagMethodMapIndex = mapOffset / sizeof(uint);
            }
            else if (BinaryPrimitives.ReadUInt32LittleEndian(
                    mainBody.MethodMap[mapOffset..])
                == (0x1600_0000u | (uint)echoRow))
            {
                echoMethodMapIndex = mapOffset / sizeof(uint);
            }
        }
        Assert.That(produceTagMethodMapIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(echoMethodMapIndex, Is.GreaterThanOrEqualTo(0));
        var tagWriteSelection = -1;
        for (var recordIndex = 0; recordIndex < mainBody.RecordCount;
            recordIndex++)
        {
            var record = mainBody.GetRecord(recordIndex);
            if (record[0] == 0x04 && record[4] == 2 && record[5] == 2)
            {
                tagWriteSelection = recordIndex;
                break;
            }
        }
        Assert.That(tagWriteSelection, Is.GreaterThanOrEqualTo(0));
        var nestedArrayWriteSelection = -1;
        for (var recordIndex = 0; recordIndex < mainBody.RecordCount;
            recordIndex++)
        {
            var record = mainBody.GetRecord(recordIndex);
            if (record[0] == 0x04 && record[4] == 3 && record[5] == 2
                && record[7] == 1)
            {
                nestedArrayWriteSelection = recordIndex;
                break;
            }
        }
        Assert.That(nestedArrayWriteSelection, Is.GreaterThanOrEqualTo(0));
        var tagWriteOffset = -1;
        var nestedArrayWriteOffset = -1;
        var produceCallOffsets = new System.Collections.Generic.List<int>();
        var echoCallOffset = -1;
        for (var offset = 0; offset < mainBody.Code.Length;)
        {
            var opcode = mainBody.Code[offset];
            if ((opcode == 0x91
                    && (mainBody.Code[offset + 1] >> 4)
                        == tagWriteSelection)
                || (opcode == 0xD8
                    && BinaryPrimitives.ReadUInt16LittleEndian(
                        mainBody.Code[(offset + 2)..])
                        == tagWriteSelection))
            {
                tagWriteOffset = offset;
            }
            if ((opcode == 0x91
                    && (mainBody.Code[offset + 1] >> 4)
                        == nestedArrayWriteSelection)
                || (opcode == 0xD8
                    && BinaryPrimitives.ReadUInt16LittleEndian(
                        mainBody.Code[(offset + 2)..])
                        == nestedArrayWriteSelection))
            {
                nestedArrayWriteOffset = offset;
            }
            else if (opcode == 0xE0)
            {
                var callRecordIndex
                    = BinaryPrimitives.ReadUInt16LittleEndian(
                        mainBody.Code[(offset + 2)..]);
                var callRecord = mainBody.GetRecord(callRecordIndex);
                if (callRecord[0] == 0x02
                    && BinaryPrimitives.ReadUInt16LittleEndian(
                        callRecord[8..])
                        == produceTagMethodMapIndex)
                {
                    produceCallOffsets.Add(offset);
                }
                else if (callRecord[0] == 0x02
                    && BinaryPrimitives.ReadUInt16LittleEndian(
                        callRecord[8..]) == echoMethodMapIndex)
                {
                    echoCallOffset = offset;
                }
            }

            offset += TrRmRegisterBodyReader.InstructionLength(opcode,
                TrRmArtifactSchema.LocalAggregateLayoutDraft);
        }
        Assert.That(tagWriteOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(nestedArrayWriteOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(produceCallOffsets.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(produceCallOffsets[0], Is.GreaterThan(tagWriteOffset),
            "The field destination must be selected before the effectful nested RHS call.");
        Assert.That(echoCallOffset, Is.GreaterThan(produceCallOffsets[0]),
            "Computed CallSite arguments must be evaluated and snapshotted before the outer call.");
        Assert.That(produceCallOffsets[1], Is.GreaterThan(nestedArrayWriteOffset),
            "The nested array destination and index must be selected before its effectful RHS call.");
        Assert.That(tables.NativeImports.Count, Is.Zero);
        Assert.That(tables.StandardNativeImports.Count, Is.Zero);
        Assert.That(tables.NativeFunctionPointerSignatures.Count, Is.Zero);
        Assert.That(index.TryGetSection(0x0021, out _, out var importCount)
            ? importCount : 0, Is.Zero);

        var foundCallSite = false;
        var foundFieldPlace = false;
        var foundArrayPlace = false;
        var foundSemanticStore = false;
        var foundReplacementStore = false;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            var bodyRow = tables.Methods.GetBodyRow(methodRow);
            Assert.That(bodyRow, Is.GreaterThan(0));
            tables.ValidateRegisterBodyMapsForMethod(methodRow);
            var body = tables.Bodies.GetRegisterBody(bodyRow,
                tables.Contracts.Count);
            Assert.That(body.HomeCount, Is.GreaterThan(0));
            for (var recordIndex = 0; recordIndex < body.RecordCount;
                recordIndex++)
            {
                var record = body.GetRecord(recordIndex);
                Assert.That(record[0], Is.Not.EqualTo(0x0D),
                    "L1 must not emit a NativeAddress record.");
                if (record[0] == 0x02)
                {
                    foundCallSite = true;
                }
                else if (record[0] == 0x04)
                {
                    foundFieldPlace |= record[4] == 2;
                    foundArrayPlace |= record[4] == 3;
                }
                else if (record[0] == 0x05)
                {
                    foundSemanticStore = true;
                    foundReplacementStore |= record[6] == 2
                        && record[7] == 1;
                }
            }

            var code = body.Code;
            for (var offset = 0; offset < code.Length;)
            {
                var opcode = code[offset];
                Assert.That(opcode, Is.Not.EqualTo(0xE9),
                    "L1 must not emit a NativeAddress instruction.");
                offset += TrRmRegisterBodyReader.InstructionLength(opcode,
                    TrRmArtifactSchema.LocalAggregateLayoutDraft);
            }
        }

        Assert.That(foundCallSite, Is.True,
            "The ordinary caller must target the local constructor MethodDef.");
        Assert.That(foundFieldPlace, Is.True,
            "The constructor and caller must retain typed FieldDef places.");
        Assert.That(foundArrayPlace, Is.True,
            "The Body must retain its fixed-array element selections.");
        Assert.That(foundSemanticStore, Is.True,
            "Construction and replacement must use SemanticStore records.");
        Assert.That(foundReplacementStore, Is.True,
            "Array replacement must retain transition 2 and CopyRead in SemanticStore.");
        for (var row = 1; row <= tables.TypeSpecs.Count; row++)
        {
            Assert.That(tables.TypeSpecs.GetRow(row)[0], Is.Not.EqualTo(0x0D),
                "L1 must not emit a function-pointer anchor TypeSpec.");
        }
    }

    [Test]
    public void RejectsL1FixedArrayExtentThatOverflowsTargetNuint()
    {
            const string source = """
                [Blittable]
                [Layout(LayoutKind.Sequential)]
                public struct Huge {
                    public [uint16; 18446744073709551615] Values;
                }
                """;
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
                new CompilationOptions {
                    LayoutCoreRegistration =
                        LayoutCoreRegistrationTestData.Create(),
                });

            Assert.That(compilation.HasErrors, Is.True);
            Assert.That(compilation.Diagnostics.Any(static diagnostic =>
                diagnostic.Code == "TRM3030"), Is.True,
                string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void StandardLayoutRoundTripsWideU64GeometryWithoutMaterializing()
    {
        const string moduleIdentity = "l1-wide-u64-layout-provider";
        const ulong arrayExtent = 0x1_0000_0001;
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Wide {
                public uint8 First;
                public [uint8; 4294967297] Payload;
                public uint8 Last;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        AssertWideGeometry(program.Structures, arrayExtent);

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, SchemaIdentity, 1,
            [new TrRmSourceTypeExport("", "Wide", "WideKey")]);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        Assert.That(index.Schema,
            Is.EqualTo(TrRmArtifactSchema.LocalAggregateLayoutDraft));
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        Assert.That(tables.FixedArrayLayouts.Count, Is.EqualTo(1));
        var arrayLayout = tables.FixedArrayLayouts.GetRow(1);
        Assert.That(arrayLayout.ElementStride, Is.EqualTo(1UL));
        Assert.That(arrayLayout.ByteExtent, Is.EqualTo(arrayExtent));
        Assert.That(arrayLayout.Alignment, Is.EqualTo(1UL));
        Assert.That(tables.TypeSpecs.TryGetFixedInlineArray(
            checked((uint)arrayLayout.ArrayTypeSpecRow), out _,
            out var elementCount), Is.True);
        Assert.That(elementCount, Is.EqualTo(arrayExtent));

        var loaded = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(SchemaIdentity, artifact),
            moduleIdentity, [], ["WideKey"]);
        AssertWideGeometry(loaded.Structures, arrayExtent);
        Assert.That(loaded.NativeFixedArrayTypes.Single(),
            Is.EqualTo(new BoundNativeFixedArrayType(PrimitiveType.UInt8,
                arrayExtent, 1, arrayExtent, 1, 1)));

        static void AssertWideGeometry(
            ImmutableArray<BoundStruct> structures,
            ulong extent)
        {
            var structure = structures.Single(
                static item => item.Name == "Wide");
            Assert.That(structure.Size, Is.EqualTo(extent + 2));
            Assert.That(structure.Fields.Select(
                    static field => field.Offset),
                Is.EqualTo(new ulong[] { 0, 1, extent + 1 }));
            Assert.That(structure.Fields.Select(
                    static field => field.Size),
                Is.EqualTo(new ulong[] { 1, extent, 1 }));
        }
    }

    [Test]
    public void StandardL1BodyRoundTripsPositiveStrideExtentAboveU32()
    {
        const string moduleIdentity = "l1-wide-array-body";
        const ulong arrayCount = 0x1_0000_0001;
        const string source = """
            int32 Main() {
                mut [uint8; 4294967297] values
                    = [uint8(0); 4294967297];
                unsafe { return int32(values[uint64(4294967296)]); }
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, SchemaIdentity, 1);

        static BoundProgram Load(byte[] bytes)
        {
            var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(bytes);
            var tables
                = TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                        index);
            Assert.That(tables.FixedArrayLayouts.Count, Is.EqualTo(1));
            var layout = tables.FixedArrayLayouts.GetRow(1);
            Assert.That(tables.TypeSpecs.TryGetFixedInlineArray(
                checked((uint)layout.ArrayTypeSpecRow), out _,
                out var count), Is.True);
            Assert.That(count, Is.EqualTo(arrayCount));
            Assert.That(layout.ElementStride, Is.EqualTo(1UL));
            Assert.That(layout.ByteExtent, Is.EqualTo(arrayCount));
            Assert.That(tables.Methods.Count, Is.EqualTo(1));
            var methodRow = 1;
            Assert.That(tables.Methods.GetName(methodRow),
                Is.EqualTo("Main"));
            Assert.That(tables.Methods.GetBodyRow(methodRow),
                Is.GreaterThan(0));
            tables.ValidateRegisterBodyMapsForMethod(methodRow);

            return TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity, bytes),
                moduleIdentity, []);
        }

        var loaded = Load(artifact);
        Assert.That(loaded.NativeFixedArrayTypes.Single(),
            Is.EqualTo(new BoundNativeFixedArrayType(PrimitiveType.UInt8,
                arrayCount, 1, arrayCount, 1, 1)));
        var main = loaded.Functions.Single(static function =>
            function.Name == "Main");
        BoundNativeFixedArrayElement? selectedElement = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundNativeFixedArrayElement element)
                {
                    selectedElement = element;
                }
            }), Is.True);
        Assert.That(selectedElement, Is.Not.Null);
        Assert.That(selectedElement!.Layout.ElementCount,
            Is.EqualTo(arrayCount));
        Assert.That(selectedElement.Index, Is.TypeOf<BoundRead>());
        var indexRead = (BoundRead)selectedElement.Index;
        Assert.That(indexRead.Type, Is.EqualTo(PrimitiveType.UInt64));
        Assert.That(main.Variables[indexRead.Variable].Type,
            Is.EqualTo(PrimitiveType.UInt64));
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, SchemaIdentity, 1);
        var reloaded = Load(reemitted);
        Assert.That(reloaded.NativeFixedArrayTypes.Single(),
            Is.EqualTo(new BoundNativeFixedArrayType(PrimitiveType.UInt8,
                arrayCount, 1, arrayCount, 1, 1)));
    }

    [Test]
    public void EmitStandardRejectsPublicConstructorWithoutErasingItsIdentity()
    {
        const string moduleIdentity = "l1-public-constructor-boundary";
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Packet {
                public mut uint8 Tag;

                public Packet() {
                    Tag = uint8(0);
                }
            }

            int32 Main() {
                mut Packet packet = new Packet();
                return int32(0);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var sourceConstructor = program.Functions.Single(
            static function => function.IsConstructor);
        Assert.That((sourceConstructor.IsPrivate,
            sourceConstructor.IsInternal), Is.EqualTo((false, false)));

        var exception = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, moduleIdentity,
                SchemaIdentity, 1));
        Assert.That(exception?.Message,
            Does.Contain("public constructor"));
        Assert.That((sourceConstructor.IsPrivate,
            sourceConstructor.IsInternal), Is.EqualTo((false, false)),
            "Rejecting constructor normalization must preserve its source visibility.");
    }

    [Test]
    public void EmitStandardPreservesArrayElementFieldPlaces()
    {
        const string moduleIdentity = "l1-array-element-field-place";
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Item {
                public mut uint8 Value;

                internal Item() {
                    Value = uint8(0);
                }
            }

            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Packet {
                public mut [Item; 2] Items;

                internal Packet() {
                    Items = [new Item(), new Item()];
                }
            }

            int32 Main() {
                mut Packet packet = new Packet();
                packet.Items[0].Value = uint8(1);
                mut uint8 observed = packet.Items[0].Value;
                return int32(0);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, SchemaIdentity, 1);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var mainRow = 0;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            if (tables.Methods.GetName(methodRow) == "Main")
            {
                mainRow = methodRow;
                break;
            }
        }
        Assert.That(mainRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(mainRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(mainRow), tables.Contracts.Count);
        var foundElementBaseField = false;
        var foundFieldBaseElement = false;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record[0] != 0x04)
            {
                continue;
            }

            foundElementBaseField |= record[4] == 2 && record[7] == 1;
            foundFieldBaseElement |= record[4] == 3 && record[7] == 1;
        }
        Assert.That(foundElementBaseField, Is.True,
            "Field access must compose from the selected array-element PlaceHandle.");
        Assert.That(foundFieldBaseElement, Is.True,
            "Array-element selection must compose from the selected field PlaceHandle.");
    }

    [Test]
    public void EmitStandardStagesNamedCallArgumentsInSourceOrder()
    {
        const string moduleIdentity = "l1-named-call-argument-order";
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Marker {
                public mut uint8 Value;

                internal Marker() {
                    Value = uint8(0);
                }
            }

            int32 Produce(int32 value) {
                return value;
            }

            int32 Pick(int32 first, int32 second) {
                return first;
            }

            int32 Main() {
                mut Marker marker = new Marker();
                return Pick(second: Produce(int32(2)),
                    first: Produce(int32(1)));
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, SchemaIdentity, 1);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var mainRow = 0;
        var pickRow = 0;
        var produceRow = 0;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            switch (tables.Methods.GetName(methodRow))
            {
                case "Main":
                    mainRow = methodRow;
                    break;
                case "Pick":
                    pickRow = methodRow;
                    break;
                case "Produce":
                    produceRow = methodRow;
                    break;
            }
        }
        Assert.That(mainRow, Is.GreaterThan(0));
        Assert.That(pickRow, Is.GreaterThan(0));
        Assert.That(produceRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(mainRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(mainRow), tables.Contracts.Count);
        var pickMapIndex = -1;
        var produceMapIndex = -1;
        for (var mapOffset = 0; mapOffset < body.MethodMap.Length;
            mapOffset += sizeof(uint))
        {
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                body.MethodMap[mapOffset..]);
            if (token == (0x1600_0000u | (uint)pickRow))
            {
                pickMapIndex = mapOffset / sizeof(uint);
            }
            else if (token == (0x1600_0000u | (uint)produceRow))
            {
                produceMapIndex = mapOffset / sizeof(uint);
            }
        }
        Assert.That(pickMapIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(produceMapIndex, Is.GreaterThanOrEqualTo(0));

        var produceResultHomes = new System.Collections.Generic.List<ushort>();
        var pickArgumentHomes = new ushort[2];
        var foundPick = false;
        for (var offset = 0; offset < body.Code.Length;)
        {
            var opcode = body.Code[offset];
            if (opcode == 0xE0)
            {
                var callRecordIndex
                    = BinaryPrimitives.ReadUInt16LittleEndian(
                        body.Code[(offset + 2)..]);
                var callRecord = body.GetRecord(callRecordIndex);
                var targetMapIndex
                    = BinaryPrimitives.ReadUInt16LittleEndian(
                        callRecord[8..]);
                if (targetMapIndex == pickMapIndex)
                {
                    Assert.That(produceResultHomes.Count,
                        Is.GreaterThanOrEqualTo(2));
                    pickArgumentHomes[0]
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            callRecord[22..]);
                    pickArgumentHomes[1]
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            callRecord[26..]);
                    Assert.That(pickArgumentHomes[0],
                        Is.EqualTo(produceResultHomes[^1]),
                        "Formal argument zero must use the second source argument's snapshot.");
                    Assert.That(pickArgumentHomes[1],
                        Is.EqualTo(produceResultHomes[^2]),
                        "Formal argument one must use the first source argument's snapshot.");
                    foundPick = true;
                    break;
                }

                if (targetMapIndex == produceMapIndex
                    && callRecord[5] == 2)
                {
                    produceResultHomes.Add(
                        BinaryPrimitives.ReadUInt16LittleEndian(
                            callRecord[10..]));
                }
            }

            offset += TrRmRegisterBodyReader.InstructionLength(opcode,
                TrRmArtifactSchema.LocalAggregateLayoutDraft);
        }

        Assert.That(foundPick, Is.True);
    }

    [Test]
    public void EmitStandardPreservesInheritedRawUnionReturnCoverage()
    {
        const string moduleIdentity = "l1-raw-union-return-coverage";
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct RawWord {
                public mut uint32 Narrow;
                public mut uint64 Wide;
            }

            RawWord MakeWide() {
                init mut RawWord value;
                unsafe { value.Wide = uint64(7); }
                return value;
            }

            int32 Main() {
                mut RawWord value = MakeWide();
                return int32(0);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, SchemaIdentity, 1);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        Assert.That(index.Schema,
            Is.EqualTo(TrRmArtifactSchema.LocalAggregateLayoutDraft));
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var makeWideMethod = 0;
        var mainMethod = 0;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            switch (tables.Methods.GetName(methodRow))
            {
                case "MakeWide":
                    makeWideMethod = methodRow;
                    break;
                case "Main":
                    mainMethod = methodRow;
                    break;
            }
        }

        Assert.That(makeWideMethod, Is.GreaterThan(0));
        Assert.That(mainMethod, Is.GreaterThan(0));
        Assert.That(tables.Methods.GetBodyRow(makeWideMethod),
            Is.GreaterThan(0));
        Assert.That(tables.Methods.GetBodyRow(mainMethod), Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(makeWideMethod);
        tables.ValidateRegisterBodyMapsForMethod(mainMethod);
        var coverage = tables.Contracts.GetReturnCoverageContracts(
            makeWideMethod);
        Assert.That(coverage, Has.Length.EqualTo(1));
        Assert.That(coverage[0].TargetProfileRow, Is.EqualTo(1));
        Assert.That(coverage[0].Regions, Has.Length.EqualTo(1));
        Assert.That(coverage[0].Regions[0].Offset, Is.Zero);
        Assert.That(coverage[0].Regions[0].Length, Is.EqualTo(8));
        Assert.That(tables.Contracts.GetReturnCoverageContracts(mainMethod),
            Is.Empty);
        Assert.That(TrRmFeatureManifestValidator.Requires(tables.Features,
            tables.Modules, 1,
            TrRmFeatureTableReader.RawUnionValueCoverageFeatureId,
            TrRmArtifactSchema.LocalAggregateLayoutDraft), Is.True);

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, artifact),
            moduleIdentity, []);
        var loadedMakeWide = loaded.Functions.Single(
            static function => function.Name == "MakeWide");
        Assert.That(loadedMakeWide.RawUnionReturnCoverage, Is.Not.Empty);
        var unsafeFieldWriteFound = false;
        Assert.That(BoundTreeTraversal.VisitStatements(
            loadedMakeWide.Body, statement =>
            {
                if (statement is BoundFieldAssignment assignment
                    && assignment.IsUnsafeAuthorized)
                {
                    unsafeFieldWriteFound = true;
                }
            }), Is.True);
        Assert.That(unsafeFieldWriteFound, Is.True);

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, SchemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, reemitted),
            moduleIdentity, []);
        Assert.That(reloaded.Functions.Single(
                static function => function.Name == "MakeWide")
            .RawUnionReturnCoverage,
            Is.EqualTo(loadedMakeWide.RawUnionReturnCoverage));
    }

    [Test]
    public void EmitStandardCapturesListOperandsBeforeArrayInstallation()
    {
        const string moduleIdentity = "l1-array-list-and-inherited-r-forms";
        const string source = """
            int32 Produce() {
                return int32(1);
            }

            int32 Main() {
                mut [int32; 2] explicitValues = [Produce(), Produce()];
                mut [int32; 2] repeatedValues = [Produce(); 2];
                mut [int32; 0] emptyValues = [];
                return int32(0);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, SchemaIdentity, 1);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var mainMethod = 0;
        var produceMethod = 0;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            switch (tables.Methods.GetName(methodRow))
            {
                case "Main":
                    mainMethod = methodRow;
                    break;
                case "Produce":
                    produceMethod = methodRow;
                    break;
            }
        }
        Assert.That(mainMethod, Is.GreaterThan(0));
        Assert.That(produceMethod, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(mainMethod);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(mainMethod), tables.Contracts.Count);
        var produceMapIndex = -1;
        for (var mapOffset = 0; mapOffset < body.MethodMap.Length;
            mapOffset += sizeof(uint))
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(
                    body.MethodMap[mapOffset..])
                == (0x1600_0000u | (uint)produceMethod))
            {
                produceMapIndex = mapOffset / sizeof(uint);
                break;
            }
        }
        Assert.That(produceMapIndex, Is.GreaterThanOrEqualTo(0));

        var firstListElementSelection = -1;
        var repeatStoreCount = 0;
        var emptyArrayStoreCount = 0;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record[0] == 0x04 && record[4] == 3 && record[5] == 3
                && record[7] == 2 && firstListElementSelection < 0)
            {
                firstListElementSelection = recordIndex;
            }
            if (record[0] == 0x05)
            {
                repeatStoreCount += record[7] == 6 ? 1 : 0;
                emptyArrayStoreCount += record[5] == 0 && record[6] == 1
                    && record[7] == 0 ? 1 : 0;
            }
        }
        Assert.That(firstListElementSelection, Is.GreaterThanOrEqualTo(0));
        Assert.That(repeatStoreCount, Is.EqualTo(1));
        Assert.That(emptyArrayStoreCount, Is.EqualTo(1));

        var listCallOffsets = new System.Collections.Generic.List<int>();
        var firstListStoreOffset = -1;
        for (var offset = 0; offset < body.Code.Length;)
        {
            var opcode = body.Code[offset];
            if (opcode == 0xE0)
            {
                var callRecordIndex
                    = BinaryPrimitives.ReadUInt16LittleEndian(
                        body.Code[(offset + 2)..]);
                var callRecord = body.GetRecord(callRecordIndex);
                if (callRecord[0] == 0x02
                    && BinaryPrimitives.ReadUInt16LittleEndian(
                        callRecord[8..]) == produceMapIndex)
                {
                    listCallOffsets.Add(offset);
                }
            }
            else if ((opcode == 0x91
                    && (body.Code[offset + 1] >> 4)
                        == firstListElementSelection)
                || (opcode == 0xD8
                    && BinaryPrimitives.ReadUInt16LittleEndian(
                        body.Code[(offset + 2)..])
                        == firstListElementSelection))
            {
                firstListStoreOffset = offset;
            }

            offset += TrRmRegisterBodyReader.InstructionLength(opcode,
                TrRmArtifactSchema.LocalAggregateLayoutDraft);
        }
        Assert.That(listCallOffsets, Has.Count.EqualTo(3));
        Assert.That(firstListStoreOffset, Is.GreaterThan(
            listCallOffsets[1]),
            "The explicit list must capture both effectful operands before installing its first element.");
    }

    [Test]
    public void EmitStandardJoinsConditionalHomeResultAcrossArms()
    {
        const string moduleIdentity = "l1-conditional-home-join";
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Marker {
                public uint8 Value;
            }

            int32 Main() {
                return true ? int32(1) : int32(2);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, SchemaIdentity, 1);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var mainMethod = 0;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            if (tables.Methods.GetName(methodRow) == "Main")
            {
                mainMethod = methodRow;
                break;
            }
        }
        Assert.That(mainMethod, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(mainMethod);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(mainMethod), tables.Contracts.Count);
        Assert.That(body.BlockCount, Is.EqualTo(4));
        var branchCount = 0;
        var conditionalEdgeCount = 0;
        for (var offset = 0; offset < body.Code.Length;)
        {
            var opcode = body.Code[offset];
            branchCount += opcode == 0xE3 ? 1 : 0;
            conditionalEdgeCount += opcode == 0xE4 ? 1 : 0;
            offset += TrRmRegisterBodyReader.InstructionLength(opcode,
                TrRmArtifactSchema.LocalAggregateLayoutDraft);
        }
        Assert.That(branchCount, Is.EqualTo(2));
        Assert.That(conditionalEdgeCount, Is.EqualTo(1));
    }

    [Test]
    public void EmitStandardProducesNoEntryLibraryWithRootMethodExports()
    {
        const string moduleIdentity = "l1-no-entry-library-producer";
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Marker {
                public uint8 Value;
            }

            public int32 RootMethod() {
                return int32(42);
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        Assert.That(program.EntryPoint, Is.EqualTo(-1));

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, SchemaIdentity, 1);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        Assert.That(tables.Modules.GetEntryMethod(1), Is.Zero);
        Assert.That(tables.Methods.Count, Is.EqualTo(1));
        Assert.That(tables.Bodies.Count, Is.EqualTo(1));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1, "RootMethod",
            out var definition), Is.True);
        Assert.That(definition >> 24, Is.EqualTo(0x16));
        var methodRow = checked((int)(definition & 0x00FF_FFFF));
        Assert.That(tables.Methods.GetOwnerModule(methodRow), Is.EqualTo(1));
        Assert.That(tables.Methods.GetBodyRow(methodRow),
            Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(methodRow);

        var rootFunction = program.Functions[0];
        var invalidProgram = program with {
            Functions = program.Functions.SetItem(0, rootFunction with {
                Body = new BoundBlock([], rootFunction.Body.Span),
            }),
        };
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(invalidProgram,
                moduleIdentity, SchemaIdentity, 1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EmitStandardReemitsBodylessMethodImportsAndImportedCalls(
        bool implicitReference)
    {
        var (rootArtifact, providerArtifact)
            = CreateL1MethodProviderArtifacts(implicitReference);
        var rootProgram = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
            "Root",
            [new TrRmStandardArtifactInput(SchemaIdentity, providerArtifact)]);
        Assert.That(rootProgram.StandardMethodProviderClosure!
            .MethodEdges, Has.Length.EqualTo(2));

        var importedProgram = TrRmExecutableCompiler.EmitStandard(
            rootProgram, "Root", SchemaIdentity, 1);
        var importedIndex
            = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(
                importedProgram);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    importedIndex);
        Assert.That(tables.Methods.Count, Is.EqualTo(5));
        Assert.That(tables.Bodies.Count, Is.EqualTo(3));
        Assert.That(tables.Imports.Count, Is.EqualTo(2));
        Assert.That(tables.ModuleRefs.Count, Is.EqualTo(1));
        Assert.That(tables.ModuleRefs.GetProducerIdentity(1),
            Is.EqualTo("Provider"));
        Assert.That(tables.ModuleRefs.GetRequirements(1),
            Is.EqualTo((1u, 0u)));
        var expectedMethodRow = FindMethodByName(tables, "Expected");
        var uncalledMethodRow = FindMethodByName(tables, "Uncalled");
        var firstMethodRow = FindMethodByName(tables, "First");
        var secondMethodRow = FindMethodByName(tables, "Second");
        var mainMethodRow = FindMethodByName(tables, "Main");
        Assert.That(tables.Imports.GetExpectedDefinitionForImportRow(1),
            Is.EqualTo(0x1600_0000u | (uint)expectedMethodRow));
        Assert.That(tables.Imports.GetExpectedDefinitionForImportRow(2),
            Is.EqualTo(0x1600_0000u | (uint)uncalledMethodRow));
        Assert.That(tables.Imports.GetBindingForImportRow(1).ExportKey,
            Is.EqualTo("Actual"));
        Assert.That(tables.Imports.GetBindingForImportRow(2).ExportKey,
            Is.EqualTo("UnusedActual"));
        Assert.That(tables.Methods.GetBodyRow(expectedMethodRow), Is.Zero);
        Assert.That(tables.Methods.GetBodyRow(uncalledMethodRow), Is.Zero);
        Assert.That(tables.Modules.GetEntryMethod(1),
            Is.EqualTo(mainMethodRow));
        Assert.That(importedIndex.TryGetSection(0x001D, out _, out _),
            Is.False,
            "Provider exports must not be activated in the root artifact.");
        Assert.That(tables.NativeImports.Count, Is.Zero);
        Assert.That(tables.StandardNativeImports.Count, Is.Zero);

        tables.ValidateRegisterBodyMapsForMethod(mainMethodRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(mainMethodRow), tables.Contracts.Count);
        var hasExpectedImportMap = false;
        var hasUncalledImportMap = false;
        var hasFirstLocalMap = false;
        var hasSecondLocalMap = false;
        for (var mapOffset = 0; mapOffset < body.MethodMap.Length;
            mapOffset += sizeof(uint))
        {
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                body.MethodMap[mapOffset..]);
            hasExpectedImportMap |= token == 0x1C00_0001u;
            hasUncalledImportMap |= token == 0x1C00_0002u;
            hasFirstLocalMap |= token
                == (0x1600_0000u | (uint)firstMethodRow);
            hasSecondLocalMap |= token
                == (0x1600_0000u | (uint)secondMethodRow);
            Assert.That(token, Is.Not.EqualTo(
                0x1600_0000u | (uint)expectedMethodRow));
        }
        Assert.That(hasExpectedImportMap, Is.True);
        Assert.That(hasUncalledImportMap, Is.False,
            "An uncalled Import expectation remains in Imports without entering the Body MethodMap.");
        Assert.That(hasFirstLocalMap, Is.True);
        Assert.That(hasSecondLocalMap, Is.True);

        var localCallNames = new List<string>();
        var callSiteCount = 0;
        var importedCallCount = 0;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record[0] == 0x02)
            {
                callSiteCount++;
                Assert.That(record[4], Is.EqualTo(1));
                var targetMapIndex
                    = BinaryPrimitives.ReadUInt16LittleEndian(record[8..]);
                var targetToken = BinaryPrimitives.ReadUInt32LittleEndian(
                    body.MethodMap[(targetMapIndex * sizeof(uint))..]);
                if (targetToken == 0x1C00_0001u)
                {
                    importedCallCount++;
                    Assert.That(record[5], Is.EqualTo(1));
                    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                        record[12..]), Is.EqualTo(2));
                    Assert.That((record[20], record[21]),
                        Is.EqualTo(((byte)2, implicitReference
                            ? (byte)3 : (byte)1)));
                    Assert.That((record[24], record[25]),
                        Is.EqualTo(((byte)2, (byte)1)));
                }
                else
                {
                    Assert.That(targetToken >> 24, Is.EqualTo(0x16));
                    var targetMethodRow = checked((int)(
                        targetToken & 0x00FF_FFFFu));
                    localCallNames.Add(
                        tables.Methods.GetName(targetMethodRow));
                }
            }
        }
        Assert.That(callSiteCount, Is.EqualTo(3));
        Assert.That(importedCallCount, Is.EqualTo(1));
        var expectedLocalCallNames = implicitReference
            ? new[] { "First", "Second" }
            : ["Second", "First"];
        Assert.That(localCallNames, Is.EqualTo(expectedLocalCallNames),
            "Effectful argument expressions must remain captured in source order before the imported call.");

        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity,
                importedProgram),
            "Root",
            [new TrRmStandardArtifactInput(SchemaIdentity, providerArtifact)]);
        Assert.That(reloaded.StandardMethodProviderClosure!
            .MethodEdges, Has.Length.EqualTo(2));

        var expectedStubIndex = -1;
        for (var functionIndex = 0;
            functionIndex < rootProgram.Functions.Length; functionIndex++)
        {
            if (rootProgram.Functions[functionIndex].Name == "Expected")
            {
                expectedStubIndex = functionIndex;
                break;
            }
        }
        Assert.That(expectedStubIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(rootProgram.Functions[expectedStubIndex].ModuleImport,
            Is.Not.Null);
        var mismatchedStub = rootProgram.Functions[expectedStubIndex] with {
            ModuleImport = rootProgram.Functions[expectedStubIndex]
                .ModuleImport! with { ExportKey = "DifferentKey" },
        };
        var mismatchedProgram = rootProgram with {
            Functions = rootProgram.Functions.SetItem(expectedStubIndex,
                mismatchedStub),
        };
        var mismatch = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(mismatchedProgram, "Root",
                SchemaIdentity, 1));
        Assert.That(mismatch!.Message,
            Does.Contain("exact retained provider edge"));
    }

    [Test]
    public void ReemissionRejectsNonBodyAndImportedStubEdits()
    {
        var (rootArtifact, providerArtifact)
            = CreateL1MethodProviderArtifacts();
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
            "Root",
            [new TrRmStandardArtifactInput(SchemaIdentity,
                providerArtifact)]);

        var markerIndex = loaded.Structures.IndexOf(
            loaded.Structures.Single(static structure =>
                structure.Name == "Marker"));
        var marker = loaded.Structures[markerIndex];
        var changedLayout = loaded with {
            Structures = loaded.Structures.SetItem(markerIndex,
                marker with { Size = checked(marker.Size + 1) }),
        };
        var changedLayoutFailure = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(changedLayout, "Root",
                SchemaIdentity, 1));
        Assert.That(changedLayoutFailure!.Message,
            Does.Contain("cannot discard changed root Module metadata"));

        var importedFunctionIndex = loaded.Functions.IndexOf(
            loaded.Functions.Single(static function =>
                function.Name == "Expected"
                    && function.ModuleImport is not null));
        var localBody = loaded.Functions.Single(static function =>
            function.Name == "Main").Body;
        var changedImportBody = loaded with {
            Functions = loaded.Functions.SetItem(importedFunctionIndex,
                loaded.Functions[importedFunctionIndex] with {
                    Body = localBody,
                }),
        };
        var changedImportBodyFailure
            = Assert.Throws<NotSupportedException>(() =>
                TrRmExecutableCompiler.EmitStandard(changedImportBody,
                    "Root", SchemaIdentity, 1));
        Assert.That(changedImportBodyFailure!.Message,
            Does.Contain("only to retained root function bodies"));
    }

    internal static (byte[] RootArtifact, byte[] ProviderArtifact)
        CreateL1MethodProviderArtifacts(bool implicitReference = false)
    {
        var layoutOptions = new CompilationOptions {
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        };
        var expectedParameter = implicitReference
            ? "implicit ref<int32> first" : "int32 first";
        var mainBody = implicitReference
            ? "mut int32 value = First(); return Expected(second: Second(), first: value);"
            : "return Expected(second: Second(), first: First());";
        var rootSource = $$"""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Marker {
                public uint8 Value;
            }

            internal int32 First() {
                return int32(1);
            }

            internal int32 Second() {
                return int32(2);
            }

            internal int32 Expected({{expectedParameter}}, int32 second) {
                return first;
            }

            internal int32 Uncalled(int32 value) {
                return value;
            }

            internal int32 Main() {
                {{mainBody}}
            }
            """;
        var rootCompilation = Compilation.Create(
            SyntaxTree.Parse(rootSource), layoutOptions);
        Assert.That(rootCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var rootTypeArtifact = TrRmExecutableCompiler.EmitStandard(
            rootCompilation.Program!, "Root", SchemaIdentity, 1);
        var rootArtifact = AddMethodImportsToRootArtifact(rootTypeArtifact,
            implicitReference);

        var actualParameter = implicitReference
            ? "implicit ref<int32> left" : "int32 left";
        var providerSource = $$"""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Marker {
                public uint8 Value;
            }

            public int32 Actual({{actualParameter}}, int32 right) {
                return left;
            }

            public int32 UnusedActual(int32 value) {
                return value;
            }
            """;
        var provider = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource), layoutOptions);
        Assert.That(provider.HasErrors, Is.False,
            string.Join(Environment.NewLine, provider.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            provider.Program!, "Provider", SchemaIdentity, 1);
        return (rootArtifact, providerArtifact);
    }

    private static byte[] AddMethodImportsToRootArtifact(
        byte[] localMethodArtifact, bool implicitReference = false)
    {
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(
            localMethodArtifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        Assert.That(tables.Modules.Count, Is.EqualTo(1));
        Assert.That(tables.Methods.Count, Is.EqualTo(5));
        Assert.That(tables.Imports.Count, Is.Zero);
        Assert.That(index.TryGetSection(0x001D, out _, out _), Is.False);

        var expectedMethodRow = 0;
        var uncalledMethodRow = 0;
        var mainMethodRow = 0;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            switch (tables.Methods.GetName(methodRow))
            {
                case "Expected":
                    expectedMethodRow = methodRow;
                    break;
                case "Uncalled":
                    uncalledMethodRow = methodRow;
                    break;
                case "Main":
                    mainMethodRow = methodRow;
                    break;
            }
        }
        Assert.That(expectedMethodRow, Is.GreaterThan(0));
        Assert.That(uncalledMethodRow, Is.GreaterThan(0));
        Assert.That(mainMethodRow, Is.GreaterThan(0));
        var firstMethodRow = FindMethodByName(tables, "First");
        var secondMethodRow = FindMethodByName(tables, "Second");
        var originalMainBody = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(mainMethodRow), tables.Contracts.Count);
        var firstResultHome = ushort.MaxValue;
        var secondResultHome = ushort.MaxValue;
        var expectedArgumentHomes = new ushort[2];
        var callOrder = new List<int>();
        for (var codeOffset = 0;
            codeOffset < originalMainBody.Code.Length;)
        {
            var opcode = originalMainBody.Code[codeOffset];
            if (opcode == 0xE0)
            {
                var callRecordIndex
                    = BinaryPrimitives.ReadUInt16LittleEndian(
                        originalMainBody.Code[(codeOffset + 2)..]);
                var callRecord = originalMainBody.GetRecord(
                    callRecordIndex);
                var targetMapIndex
                    = BinaryPrimitives.ReadUInt16LittleEndian(
                        callRecord[8..]);
                var targetToken
                    = BinaryPrimitives.ReadUInt32LittleEndian(
                        originalMainBody.MethodMap[
                            (targetMapIndex * sizeof(uint))..]);
                var targetRow = checked((int)(targetToken
                    & 0x00FF_FFFFu));
                callOrder.Add(targetRow);
                if (targetRow == firstMethodRow
                    || targetRow == secondMethodRow)
                {
                    if (targetRow == firstMethodRow
                        && implicitReference && callRecord[5] == 1)
                    {
                        Assert.That(callRecord[5], Is.EqualTo(1));
                    }
                    else
                    {
                        Assert.That(callRecord[5], Is.EqualTo(2));
                        var resultHome
                            = BinaryPrimitives.ReadUInt16LittleEndian(
                                callRecord[10..]);
                        if (targetRow == firstMethodRow)
                        {
                            firstResultHome = resultHome;
                        }
                        else
                        {
                            secondResultHome = resultHome;
                        }
                    }
                }
                else if (targetRow == expectedMethodRow)
                {
                    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                        callRecord[12..]), Is.EqualTo(2));
                    expectedArgumentHomes[0]
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            callRecord[22..]);
                    expectedArgumentHomes[1]
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            callRecord[26..]);
                }
            }

            codeOffset += TrRmRegisterBodyReader.InstructionLength(opcode,
                TrRmArtifactSchema.LocalAggregateLayoutDraft);
        }

        var expectedCallOrder = new List<int>();
        if (implicitReference)
        {
            expectedCallOrder.AddRange(
                [firstMethodRow, secondMethodRow, expectedMethodRow]);
        }
        else
        {
            expectedCallOrder.AddRange(
                [secondMethodRow, firstMethodRow, expectedMethodRow]);
        }
        Assert.That(callOrder, Is.EqualTo(expectedCallOrder));
        if (!implicitReference)
        {
            Assert.That(expectedArgumentHomes[0],
                Is.EqualTo(firstResultHome));
        }
        Assert.That(expectedArgumentHomes[1], Is.EqualTo(secondResultHome));

        var stringRows = new List<(string Text, int Encoding)>(
            tables.Strings.Count + 3);
        var stringRowsByText = new Dictionary<string, int>(
            tables.Strings.Count + 3, StringComparer.Ordinal);
        for (var row = 1; row <= tables.Strings.Count; row++)
        {
            var text = tables.Strings.DecodeNamespace(row);
            stringRows.Add((text, 1));
            _ = stringRowsByText.TryAdd(text, row);
        }

        int GetStringRow(string text)
        {
            if (!stringRowsByText.TryGetValue(text, out var row))
            {
                row = stringRows.Count + 1;
                stringRows.Add((text, 1));
                stringRowsByText.Add(text, row);
            }

            return row;
        }

        var moduleIdentityRow = GetStringRow(tables.Modules.GetIdentity(1));
        var providerIdentityRow = GetStringRow("Provider");
        var actualExportKeyRow = GetStringRow("Actual");
        var unusedExportKeyRow = GetStringRow("UnusedActual");
        var stringBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var strings = new TrRmStringTableReader(stringBytes,
            stringRows.Count);
        var (targetRequirement, runtimePolicy)
            = tables.Modules.GetRequirements(1);
        var (First, Count) = tables.Modules.GetFeatureRange(1);
        var (Start, End) = tables.Modules.GetTypeDefRange(1);
        var methodDefRange = tables.Modules.GetMethodDefRange(1);
        var sourceFileRange = tables.Modules.GetSourceFileRange(1);
        var moduleRows = new[] {
            new TrRmModuleRow(moduleIdentityRow, targetRequirement,
                runtimePolicy, mainMethodRow, 1, Start,
                methodDefRange.Start, sourceFileRange.Start,
                First, Count,
                tables.Modules.GetGoverningRuleEdition(1)),
        };
        var moduleBytes = TrRmModuleTableWriter.Encode(moduleRows,
            strings, 1, tables.TypeDefs.Count, tables.Methods.Count,
            tables.Modules.SourceFileCount, tables.Features.Count);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            1, tables.TypeDefs.Count, tables.Methods.Count,
            tables.Modules.SourceFileCount, tables.Features.Count);
        var moduleRefRows = new[] {
            new TrRmModuleRefRow(providerIdentityRow, targetRequirement,
                runtimePolicy),
        };
        var moduleRefBytes = TrRmModuleRefTableWriter.Encode(moduleRefRows,
            strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes, 1,
            strings, modules);

        Assert.That(index.TryGetSection(0x0013, out var typeDefSection,
            out var typeDefCount), Is.True);
        Assert.That(index.TryGetSection(0x0011, out var typeSpecSection,
            out var typeSpecCount), Is.True);
        Assert.That(index.TryGetSection(0x0014, out var fieldSection,
            out var fieldCount), Is.True);
        Assert.That(index.TryGetSection(0x0015, out var layoutSection,
            out var layoutCount), Is.True);
        var typeDefs = new TrRmTypeDefTableReader(typeDefSection,
            typeDefCount, strings, modules, fieldCount,
            tables.GenericParams.Count, layoutCount, tables.Contracts.Count,
            TrRmArtifactSchema.LocalAggregateLayoutDraft);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecSection,
            typeSpecCount, typeDefCount, 2, 0, 0,
            TrRmArtifactSchema.LocalAggregateLayoutDraft,
            methodDefCount: tables.Methods.Count);
        var fields = new TrRmFieldDefTableReader(fieldSection,
            fieldCount, strings, typeDefs, typeSpecs);
        var signatureBytes = GetSection(index, 0x0017);
        var signatures = new TrRmMethodSigTableReader(signatureBytes,
            tables.Signatures.Count, typeSpecs,
            tables.Signatures.ParamCount, 0);
        var parametersBytes = GetSection(index, 0x0018);

        var methodRows = new TrRmMethodDefRow[tables.Methods.Count];
        var bodies = new List<ReadOnlyMemory<byte>>();
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            var bodyRow = 0;
            if (methodRow != expectedMethodRow
                && methodRow != uncalledMethodRow)
            {
                var originalBodyRow = tables.Methods.GetBodyRow(methodRow);
                var originalBody = tables.Bodies.GetRegisterBody(
                    originalBodyRow, tables.Contracts.Count);
                bodies.Add(methodRow == mainMethodRow
                    ? RemapMethodImportCallTargets(originalBody,
                        0x1600_0000u
                            | checked((uint)expectedMethodRow),
                        0x1C00_0001u, tables.Contracts.Count)
                    : EncodeBody(originalBody, tables.Contracts.Count));
                bodyRow = bodies.Count;
            }

            var (contractFirst, contractCount)
                = tables.Methods.GetContractRange(methodRow);
            var importedStub = methodRow == expectedMethodRow
                || methodRow == uncalledMethodRow;
            var flags = importedStub ? 2u
                : tables.Methods.GetFlags(methodRow);
            methodRows[methodRow - 1] = new TrRmMethodDefRow(
                tables.Methods.GetOwnerType(methodRow),
                GetStringRow(tables.Methods.GetName(methodRow)),
                tables.Methods.GetSignature(methodRow), flags, contractFirst,
                contractCount, bodyRow);
        }

        var bodyCount = bodies.Count;
        var methodBytes = TrRmMethodDefTableWriter.Encode(methodRows,
            strings, modules, typeDefs, signatures, tables.Contracts.Count,
            bodyCount);
        var methods = new TrRmMethodDefTableReader(methodBytes,
            methodRows.Length, strings, modules, typeDefs, signatures,
            tables.Contracts.Count, bodyCount);
        var importRows = new[] {
            new TrRmImportRow(1, actualExportKeyRow,
                0x1600_0000u | checked((uint)expectedMethodRow)),
            new TrRmImportRow(1, unusedExportKeyRow,
                0x1600_0000u | checked((uint)uncalledMethodRow)),
        };
        var featuresBytes = GetSection(index, 0x0004);
        var features = new TrRmFeatureTableReader(featuresBytes,
            tables.Features.Count,
            TrRmArtifactSchema.LocalAggregateLayoutDraft);
        var importBytes = TrRmImportTableWriter.Encode(importRows,
            strings, moduleRefs, typeDefs, methods, default, default, default,
            fields, features, modules,
            TrRmArtifactSchema.LocalAggregateLayoutDraft);

        var sections = new List<TrRmSection>(index.SectionCount + 7);
        for (var sectionIndex = 0;
            sectionIndex < index.SectionCount; sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var data, out var count);
            if (kind is 0x0001 or 0x0002 or 0x0010 or 0x0016 or 0x0017
                or 0x0018 or 0x001C or 0x001D or 0x0020)
            {
                continue;
            }

            sections.Add(new TrRmSection(kind, flags,
                checked((uint)count), data.ToArray()));
        }

        sections.Add(new TrRmSection(0x0001, 0, 1, moduleBytes));
        sections.Add(new TrRmSection(0x0002, 0,
            checked((uint)stringRows.Count), stringBytes));
        sections.Add(new TrRmSection(0x0010, 0, 1, moduleRefBytes));
        sections.Add(new TrRmSection(0x0016, 0,
            checked((uint)methodRows.Length), methodBytes));
        sections.Add(new TrRmSection(0x0017, 0,
            checked((uint)tables.Signatures.Count), signatureBytes));
        if (tables.Signatures.ParamCount != 0)
        {
            sections.Add(new TrRmSection(0x0018, 0,
                checked((uint)tables.Signatures.ParamCount), parametersBytes));
        }
        sections.Add(new TrRmSection(0x001C, 0, 2, importBytes));
        sections.Add(new TrRmSection(0x0020, 0,
            checked((uint)bodyCount),
            TrRmVariableTableWriter.EncodeBodies(bodies)));
        sections.Sort(static (left, right)
            => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.LocalAggregateLayoutDraft);

        static byte[] EncodeBody(TrRmRegisterBodyReader body,
            int contractCount)
        {
            return RemapMethodImportCallTargets(body, null, null,
                        contractCount);
        }

        static byte[] RemapMethodImportCallTargets(
            TrRmRegisterBodyReader body, uint? oldTarget, uint? newTarget,
            int contractCount)
        {
            if (oldTarget.HasValue != newTarget.HasValue)
            {
                throw new ArgumentException(
                    "Both method-token selections must be supplied together.");
            }

            var typeTokens = new List<uint>(body.TypeMap.Length
                / sizeof(uint));
            for (var offset = 0; offset < body.TypeMap.Length;
                offset += sizeof(uint))
            {
                typeTokens.Add(BinaryPrimitives.ReadUInt32LittleEndian(
                    body.TypeMap[offset..]));
            }
            var methodTokens = new List<uint>(body.MethodMap.Length
                / sizeof(uint));
            for (var offset = 0; offset < body.MethodMap.Length;
                offset += sizeof(uint))
            {
                var token = BinaryPrimitives.ReadUInt32LittleEndian(
                    body.MethodMap[offset..]);
                methodTokens.Add(oldTarget.HasValue
                        && oldTarget.Value == token
                    ? newTarget!.Value : token);
            }
            if (oldTarget.HasValue
                && !methodTokens.Contains(newTarget!.Value))
            {
                throw new InvalidDataException(
                    "The test Body does not contain the expected local MethodMap token.");
            }
            var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens,
                methodTokens);
            var records = new List<ReadOnlyMemory<byte>>(body.RecordCount);
            for (var recordIndex = 0; recordIndex < body.RecordCount;
                recordIndex++)
            {
                var record = body.GetRecord(recordIndex);
                if (record[0] != 0x02 || record[4] != 1)
                {
                    records.Add(record.ToArray());
                    continue;
                }

                var targetIndex = BinaryPrimitives
                    .ReadUInt16LittleEndian(record[8..]);
                var targetToken = BinaryPrimitives
                    .ReadUInt32LittleEndian(body.MethodMap[
                            (targetIndex * sizeof(uint))..]);
                var remappedTarget = oldTarget.HasValue
                        && oldTarget.Value == targetToken
                    ? newTarget!.Value : targetToken;
                var (TargetKind, ResultKind, ResultIndex, Inputs, Preparations) = DecodeCallSite(record);
                records.Add(TrRmRegisterBodyRecordWriter.EncodeCallSite(
                    TargetKind, ResultKind,
                    maps.GetMethodMapIndex(remappedTarget),
                    ResultIndex, Inputs,
                    Preparations));
            }

            var blocks = new TrRmRegisterBodyBlock[body.BlockCount];
            for (var blockIndex = 0; blockIndex < body.BlockCount;
                blockIndex++)
            {
                var block = body.GetBlock(blockIndex);
                blocks[blockIndex] = new TrRmRegisterBodyBlock(
                    BinaryPrimitives.ReadUInt32LittleEndian(block),
                    BinaryPrimitives.ReadUInt32LittleEndian(block[4..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(block[8..]));
            }

            return TrRmRegisterBodyWriter.Encode(body.HomeMap.ToArray(),
                maps.TypeMap, maps.MethodMap, body.FieldMap.ToArray(),
                body.ConstantMap.ToArray(), blocks, records,
                body.Code.ToArray(), contractCount,
                TrRmArtifactSchema.LocalAggregateLayoutDraft);
        }

        static (byte TargetKind, byte ResultKind, ushort ResultIndex,
            TrRmRegisterBodyCallInput[] Inputs, ushort[] Preparations)
            DecodeCallSite(ReadOnlySpan<byte> record)
        {
            var inputCount = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(record[12..]));
            var preparationCount = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(record[16..]));
            var inputs = new TrRmRegisterBodyCallInput[inputCount];
            var offset = 20;
            for (var input = 0; input < inputCount; input++)
            {
                inputs[input] = new TrRmRegisterBodyCallInput(record[offset],
                    record[offset + 1],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        record[(offset + 2)..]));
                offset += 4;
            }
            var preparations = new ushort[preparationCount];
            for (var preparation = 0;
                preparation < preparationCount; preparation++)
            {
                preparations[preparation]
                    = BinaryPrimitives.ReadUInt16LittleEndian(
                        record[offset..]);
                offset += sizeof(ushort);
            }

            Assert.That(offset, Is.EqualTo(record.Length));
            return (record[4], record[5],
                BinaryPrimitives.ReadUInt16LittleEndian(record[10..]),
                inputs, preparations);
        }
    }

    private static byte[] GetSection(TrRmArtifactIndex index, ushort kind)
    {
        if (!index.TryGetSection(kind, out var data, out _))
        {
            throw new InvalidOperationException(
                $"The test fixture is missing section 0x{kind:X4}.");
        }

        return data.ToArray();
    }

    private static int FindMethodByName(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        string name)
    {
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            if (tables.Methods.GetName(methodRow) == name)
            {
                return methodRow;
            }
        }

        throw new InvalidOperationException(
            $"The artifact has no MethodDef named '{name}'.");
    }
}
