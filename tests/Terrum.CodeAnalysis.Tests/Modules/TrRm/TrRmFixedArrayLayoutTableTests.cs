// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmFixedArrayLayoutTableTests
{
    private const string A2R1SchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-copy-origin-return-coverage-1";

    private sealed class TrackingMemoryManager(int length)
        : MemoryManager<byte>
    {
        private readonly byte[] _buffer = new byte[length];

        internal int SpanAccessCount { get; private set; }

        public override Span<byte> GetSpan()
        {
            SpanAccessCount++;
            return _buffer;
        }

        public override MemoryHandle Pin(int elementIndex = 0)
            => throw new NotSupportedException();

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
        }

        internal void ResetSpanAccessCount() => SpanAccessCount = 0;
    }

    private sealed class OversizedProviderList(int count)
        : IReadOnlyList<TrRmStandardArtifactInput>
    {
        internal int IndexerAccessCount { get; private set; }

        public int Count => count;

        public TrRmStandardArtifactInput this[int index]
        {
            get
            {
                IndexerAccessCount++;
                throw new InvalidOperationException(
                    "The provider list must be bounded before indexing.");
            }
        }

        public IEnumerator<TrRmStandardArtifactInput> GetEnumerator()
            => throw new InvalidOperationException(
                "The provider list must be bounded before enumeration.");

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Test]
    public void ReadsFixedArrayLayoutWithItsModuleFeatureAndExactProfile()
    {
        var artifact = CreateArtifact(count: 3, stride: 4, extent: 12,
            alignment: 4);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));

        Assert.That(tables.FixedArrayLayouts.Count, Is.EqualTo(1));
        Assert.That(tables.FixedArrayLayouts.GetRow(1),
            Is.EqualTo(new TrRmFixedArrayLayoutRow(1, 1, 1, 4, 12, 4)));
        Assert.That(TrRmFeatureManifestValidator.Requires(tables.Features,
            tables.Modules, 1,
            TrRmFeatureTableReader.FixedInlineArrayStorageFeatureId,
            tables.Schema), Is.True);
    }

    [Test]
    public void ValidatesElementGeometryWhenArrayCountIsZero()
    {
        var artifact = CreateArtifact(count: 0, stride: 4, extent: 0,
            alignment: 4);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));

        Assert.That(tables.FixedArrayLayouts.GetRow(1).ElementStride,
            Is.EqualTo(4));
        Assert.That(tables.FixedArrayLayouts.GetRow(1).ByteExtent, Is.Zero);
    }

    [Test]
    public void DerivesArrayStrideFromCopiedSequentialTypeLayout()
    {
        var artifact = CreateAggregateArrayArtifact(declaredCopy: true,
            stride: 8, extent: 16, alignment: 4);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));

        Assert.That(tables.A3LayoutResourceLedger, Is.Null);
        Assert.That(tables.FixedArrayLayouts.GetRow(1).ElementStride,
            Is.EqualTo(8));
        Assert.That(tables.FixedArrayLayouts.GetRow(1).ByteExtent,
            Is.EqualTo(16));
    }

    [Test]
    public void DerivesUnionArrayStrideFromLargestRootlessAlternative()
    {
        var artifact = CreateAggregateArrayArtifact(declaredCopy: true,
            stride: 4, extent: 8, alignment: 4, layoutKind: 2);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));

        Assert.That(tables.FixedArrayLayouts.GetRow(1).ElementStride,
            Is.EqualTo(4));
        Assert.That(tables.FixedArrayLayouts.GetRow(1).ByteExtent,
            Is.EqualTo(8));
        Assert.That(tables.FixedArrayLayouts.GetRow(1).Alignment,
            Is.EqualTo(4));
    }

    [Test]
    public void StandardA2SourceFreeRoundTripsRawUnionArrayFields()
        => VerifyRawUnionArrayTypeLibrary(
            "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2");

    [Test]
    public void StandardA2R1SourceFreeRoundTripsRawUnionArrayFields()
        => VerifyRawUnionArrayTypeLibrary(A2R1SchemaIdentity);

    private static void VerifyRawUnionArrayTypeLibrary(string schemaIdentity)
    {
        const string moduleIdentity = "a2-raw-union-array-types";
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct RawWord {
                public uint32 Narrow;
                public ptr<int32> Pointer;
            }

            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Envelope {
                public [RawWord; 2] Values;
            }
            """;
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;

        var artifact = TrRmExecutableCompiler.EmitStandard(
            program, moduleIdentity, schemaIdentity, 1);
        var isA2R1 = StringComparer.Ordinal.Equals(schemaIdentity,
            A2R1SchemaIdentity);
        var tables = isA2R1
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayCopyReturnCoverageDraft(
                            artifact))
            : TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));
        var rawWordRow = 0;
        for (var row = 1; row <= tables.TypeDefs.Count; row++)
        {
            if (tables.TypeDefs.GetName(row) == "RawWord")
            {
                rawWordRow = row;
                break;
            }
        }
        Assert.That(rawWordRow, Is.GreaterThan(0));
        var rawWordLayout = tables.Layouts.GetLayoutForProfile(rawWordRow, 1);
        Assert.That(tables.Layouts.GetLayoutKind(rawWordLayout),
            Is.EqualTo(2));
        Assert.That(tables.FixedArrayLayouts.Count, Is.EqualTo(1));
        var arrayLayout = tables.FixedArrayLayouts.GetRow(1);
        Assert.That(arrayLayout.OwnerModuleRow, Is.EqualTo(1));
        Assert.That(arrayLayout.TargetProfileRef, Is.EqualTo(1));
        Assert.That(arrayLayout.ElementStride, Is.EqualTo(8));
        Assert.That(arrayLayout.ByteExtent, Is.EqualTo(16));
        Assert.That(arrayLayout.Alignment, Is.EqualTo(8));

        var input = new TrRmStandardArtifactInput(schemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        var loadedUnion = loaded.Structures.Single(static structure =>
            structure.Name == "RawWord");
        Assert.That(loadedUnion.IsUnion, Is.True);
        Assert.That(loadedUnion.Size, Is.EqualTo(8));
        Assert.That(loadedUnion.Fields[0].Offset, Is.Zero);
        Assert.That(loadedUnion.Fields[1].Offset, Is.Zero);
        Assert.That(loadedUnion.Fields[0].Size, Is.EqualTo(4));
        Assert.That(loadedUnion.Fields[1].Size, Is.EqualTo(8));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
    }

    [Test]
    public void A3RootLedgerAccountsForASelectedA2Provider()
    {
        var artifact = CreateAggregateArrayArtifact(declaredCopy: true,
            stride: 8, extent: 16, alignment: 4);
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyDraft(artifact);
        var ledger = new TrRmA3LayoutResourceLedger("A3Root");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                index, ledger);

        Assert.That(tables.Schema,
            Is.EqualTo(TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft));
        Assert.That(tables.A3LayoutResourceLedger, Is.SameAs(ledger));
        Assert.That(ledger.WorkRemaining,
            Is.LessThan(TrRmA3LayoutResourceLedger.WorkLimit));

        ledger.ConsumeWork(ledger.WorkRemaining);
        TrRmResourceLimitException? exception = null;
        try
        {
            _ = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                    index, ledger);
        }
        catch (TrRmResourceLimitException caught)
        {
            exception = caught;
        }

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.ModuleIdentity, Is.EqualTo("A3Root"));
        Assert.That(TrRmExecutableCompiler.TryGetA3AdmissionFailure(exception,
            out var kind), Is.True);
        Assert.That(kind,
            Is.EqualTo(TrRmA3AdmissionFailureKind.ResourceLimit));
    }

    [Test]
    public void A3RootLedgerValidatesSelectedA2ProviderAtExactWorkBoundary()
    {
        var artifact = CreateAggregateArrayArtifact(declaredCopy: true,
            stride: 8, extent: 16, alignment: 4);

        TrRmA3ResourceLedgerTestHelper.AssertExactWork(ledger =>
        {
            var tables = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayCopyDraft(
                            artifact),
                    ledger);

            Assert.That(tables.A3LayoutResourceLedger, Is.SameAs(ledger));
            Assert.That(tables.FixedArrayLayouts.GetRow(1).ByteExtent,
                Is.EqualTo(16));
        });
    }

    [Test]
    public void A3RegisterBodyRecordAccessChargesItsRangeAndOffsetFields()
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft;
        var entryState = new byte[8];
        entryState[0] = 0x01;
        byte[] code = [0x17, 0];
        var bodyBytes = TrRmRegisterBodyWriter.Encode(default, default,
            default, default, default,
            [new TrRmRegisterBodyBlock(0, (uint)code.Length, 0)],
            [entryState], code, schema: schema);
        var ledger = new TrRmA3LayoutResourceLedger();
        var body = new TrRmRegisterBodyReader(bodyBytes, schema: schema,
            resourceLedger: ledger);

        ledger.ConsumeWork(ledger.WorkRemaining - 12);
        Assert.That(ledger.WorkRemaining, Is.EqualTo(12));
        _ = body.GetRecord(0);
        Assert.That(ledger.WorkRemaining, Is.Zero);

        TrRmResourceLimitException? exception = null;
        try
        {
            _ = body.GetRecord(0);
        }
        catch (TrRmResourceLimitException caught)
        {
            exception = caught;
        }

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.Dimension, Is.EqualTo("work"));
        Assert.That(ledger.WorkRemaining, Is.Zero);
        Assert.That(exception.Required,
            Is.GreaterThan(new System.Numerics.BigInteger(
                TrRmA3LayoutResourceLedger.WorkLimit)));
    }

    [Test]
    public void DerivesA2PointerCellLayoutForNominalPointee()
    {
        var artifact = CreateAggregateArrayArtifact(declaredCopy: true,
            stride: 8, extent: 16, alignment: 8, pointerElement: true);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));

        Assert.That(tables.FixedArrayLayouts.GetRow(1).ElementStride,
            Is.EqualTo(8));
        Assert.That(tables.FixedArrayLayouts.GetRow(1).ByteExtent,
            Is.EqualTo(16));
    }

    [Test]
    public void RejectsGcQualifiedA2PointerCell()
    {
        var artifact = CreateAggregateArrayArtifact(declaredCopy: true,
            stride: 8, extent: 16, alignment: 8, pointerElement: true,
            pointerQualifiers: 0x02);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayCopyDraft(
                            artifact)));
    }

    [Test]
    public void A2RejectsDeclaredUniqueTypeDefFlag()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            CreateAggregateArrayArtifact(declaredCopy: false,
                stride: 4, extent: 8, alignment: 4,
                declaredUniqueFlag: true));
        Assert.That(TrRmExecutableCompiler.TryGetA3AdmissionFailure(
            exception!, out var kind), Is.False);
        Assert.That(kind, Is.EqualTo(TrRmA3AdmissionFailureKind.None));
    }

    [Test]
    public void DerivesPackedArrayStride()
    {
        var artifact = CreateAggregateArrayArtifact(declaredCopy: true,
            stride: 5, extent: 10, alignment: 1, pack: 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));

        Assert.That(tables.FixedArrayLayouts.GetRow(1).ElementStride,
            Is.EqualTo(5));
    }

    [Test]
    public void DerivesOverAlignedArrayStride()
    {
        var artifact = CreateAggregateArrayArtifact(declaredCopy: true,
            stride: 16, extent: 32, alignment: 16, explicitAlignment: 16);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));

        Assert.That(tables.FixedArrayLayouts.GetRow(1).Alignment,
            Is.EqualTo(16));
        Assert.That(tables.FixedArrayLayouts.GetRow(1).ElementStride,
            Is.EqualTo(16));
    }

    [Test]
    public void DistinguishesTransparentOverVoidFromUnitAtMaximumZeroExtent()
    {
        var artifact = CreateTransparentVoidArrayArtifact();
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));

        Assert.That(tables.FixedArrayLayouts.GetRow(1),
            Is.EqualTo(new TrRmFixedArrayLayoutRow(1, 2, 1, 0, 0, 1)));
        Assert.That(tables.FixedArrayLayouts.GetRow(2),
            Is.EqualTo(new TrRmFixedArrayLayoutRow(1, 3, 1, 0, 0, 1)));
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(2, 3),
            Is.False);
    }

    [Test]
    public void RejectsNominalArrayElementWithoutDeclaredCopy()
    {
        var artifact = CreateAggregateArrayArtifact(declaredCopy: false,
            stride: 8, extent: 16, alignment: 4);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayCopyDraft(artifact)));
    }

    [Test]
    public void RejectsUnionArrayElementWithoutDeclaredCopy()
    {
        var artifact = CreateAggregateArrayArtifact(declaredCopy: false,
            stride: 4, extent: 8, alignment: 4, layoutKind: 2);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayCopyDraft(
                            artifact)));
    }

    [Test]
    public void ValidatesA2PlaceSelectionAgainstModuleLayoutAndHomeTypes()
    {
        var artifact = CreateAggregateArrayArtifact(declaredCopy: true,
            stride: 8, extent: 16, alignment: 4,
            includePlaceSelectionBody: true);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));

        tables.ValidateRegisterBodyMapsForMethod(1);
    }

    [Test]
    public void RejectsA2PlaceSelectionWithUnknownLayoutRow()
    {
        var artifact = CreateAggregateArrayArtifact(declaredCopy: true,
            stride: 8, extent: 16, alignment: 4,
            includePlaceSelectionBody: true, targetLayoutRow: 2);

        _ = Assert.Throws<InvalidDataException>(
            () => ValidateBodyMaps(artifact));
    }

    [Test]
    public void ValidatesA2PlaceSelectionWithIntegerRegisterIndex()
    {
        var artifact = CreateAggregateArrayArtifact(declaredCopy: true,
            stride: 8, extent: 16, alignment: 4,
            includePlaceSelectionBody: true, registerIndex: true);
        ValidateBodyMaps(artifact);
    }

    [Test]
    public void NestedArraySelectionFormsCountedAddressFromCapturedPlace()
    {
        var artifact = CreateNestedArrayPlaceArtifact();
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));

        tables.ValidateRegisterBodyMapsForMethod(1);
    }

    [Test]
    public void PropagatesNestedArrayPlaceHandleAcrossBlocks()
    {
        var artifact = CreateNestedArrayPlaceArtifact(
            crossBlockPlaceSelection: true);

        ValidateBodyMaps(artifact);
    }

    [Test]
    public void PropagatesArrayIndexValueAcrossBlocks()
    {
        var artifact = CreateNestedArrayPlaceArtifact(
            crossBlockRegisterIndex: true);

        ValidateBodyMaps(artifact);
    }

    [Test]
    public void RejectsMismatchedArrayIndexValueAtBlockEntry()
    {
        var artifact = CreateNestedArrayPlaceArtifact(
            crossBlockRegisterIndex: true,
            mismatchedCrossBlockIndexType: true);

        _ = Assert.Throws<InvalidDataException>(
            () => ValidateBodyMaps(artifact));
    }

    [Test]
    public void MergesNestedArrayPlaceHandleOriginsAcrossBranches()
    {
        var artifact = CreateNestedArrayPlaceArtifact(
            branchJoinPlaceSelection: true);

        ValidateBodyMaps(artifact);
    }

    [Test]
    public void RejectsBranchJoinWithIncompatiblePlaceAuthority()
    {
        var artifact = CreateNestedArrayPlaceArtifact(
            branchJoinPlaceSelection: true,
            alternateFirstSelectionAccessKind: 2);

        _ = Assert.Throws<InvalidDataException>(
            () => ValidateBodyMaps(artifact));
    }

    [Test]
    public void RejectsBranchJoinWhenAnIncomingPathHasNoPlaceHandle()
    {
        var artifact = CreateNestedArrayPlaceArtifact(
            branchJoinPlaceSelection: true,
            missingBranchPlaceHandle: true);

        _ = Assert.Throws<InvalidDataException>(
            () => ValidateBodyMaps(artifact));
    }

    [Test]
    public void PropagatesNestedArrayPlaceHandleOriginsAroundLoopBackEdge()
    {
        var artifact = CreateNestedArrayPlaceArtifact(
            loopPlaceSelection: true);

        ValidateBodyMaps(artifact);
    }

    [Test]
    public void RejectsCountedAddressLargerThanTheSelectedArray()
    {
        var artifact = CreateNestedArrayPlaceArtifact(
            requiredElementCount: 3);
        _ = Assert.Throws<InvalidDataException>(
            () => ValidateBodyMaps(artifact));
    }

    [Test]
    public void AcceptsMaximumU32AddressCountForArrayLargerThanU32()
    {
        var artifact = CreateNestedArrayPlaceArtifact(
            requiredElementCount: uint.MaxValue,
            selectedArrayElementCount: (ulong)uint.MaxValue + 100);

        ValidateBodyMaps(artifact);
    }

    [Test]
    public void RejectsEmptyCountedAddressFromAnArrayElement()
    {
        var artifact = CreateNestedArrayPlaceArtifact(
            zeroRequiredElementCount: true);
        _ = Assert.Throws<InvalidDataException>(
            () => ValidateBodyMaps(artifact));
    }

    [TestCase(3UL, 4UL, 11UL, 4UL)]
    [TestCase(ulong.MaxValue, 4UL, 0UL, 4UL)]
    [TestCase(3UL, 4UL, 12UL, 3UL)]
    [TestCase(3UL, 8UL, 24UL, 4UL)]
    [TestCase(3UL, 4UL, 12UL, 2UL)]
    [TestCase(0UL, 0UL, 0UL, 1UL)]
    public void RejectsInvalidArrayExtentOrAlignment(ulong count,
        ulong stride, ulong extent, ulong alignment)
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            CreateAndValidateArtifact(count, stride, extent, alignment));
    }

    [Test]
    public void RejectsDuplicateNormalizedArrayTypesPerModule()
    {
        _ = Assert.Throws<InvalidDataException>(
            CreateDuplicateNormalizedArrayLayouts);
    }

    [Test]
    public void BindsImportedArrayElementToSelectedCopyProvider()
    {
        var artifact = CreateImportedArrayProviderArtifact();
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));
        var modules = ImmutableArray.Create(
            new TrRmStandardModuleSelection(0, 1, "Root", 1, 0, 0, 1, 1),
            new TrRmStandardModuleSelection(0, 2, "Provider", 1, 0, 0, 1, 1));

        var bindings = TrRmFixedArrayProviderBindingValidator.Validate(
            tables, [artifact],
            ["trrm/1-draft-foreign-invocation-storage-fixed-arrays-2"],
            modules, 1);

        Assert.That(bindings, Has.Length.EqualTo(1));
        Assert.That(bindings[0].ExpectedTypeDefRow, Is.EqualTo(1));
        Assert.That(bindings[0].ProviderModuleRow, Is.EqualTo(2));
        Assert.That(bindings[0].ProviderTypeDefRow, Is.EqualTo(2));
    }

    [Test]
    public void BindsImportedArrayElementToExternalCopyProvider()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        var consumerArtifact = CreateImportedArrayConsumerArtifact();
        var providerArtifact = CreateCopyProviderArtifact(
            prependUnrelatedType: true);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(
                        consumerArtifact));
        var modules = ImmutableArray.Create(
            new TrRmStandardModuleSelection(0, 1, "Root", 1, 0, 0, 1, 1),
            new TrRmStandardModuleSelection(1, 1, "Provider", 1, 0, 0, 1, 1));

        var bindings = TrRmFixedArrayProviderBindingValidator.Validate(
            tables, [consumerArtifact, providerArtifact],
            [schemaIdentity, schemaIdentity], modules, 1);

        Assert.That(bindings, Has.Length.EqualTo(1));
        Assert.That(bindings[0].ProviderArtifactIndex, Is.EqualTo(1));
        Assert.That(bindings[0].ProviderModuleRow, Is.EqualTo(1));
        Assert.That(bindings[0].ProviderTypeDefRow, Is.EqualTo(2));
    }

    [Test]
    public void BindsImportedArrayElementToExternalUnionCopyProvider()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        var consumerArtifact = CreateA2SourceTypeReferenceConsumerArtifact(
            copyLayoutKind: 2);
        var providerArtifact = CreateCopyProviderArtifact(
            prependUnrelatedType: true, providerLayoutKind: 2);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(
                        consumerArtifact));
        var modules = ImmutableArray.Create(
            new TrRmStandardModuleSelection(0, 1, "Root", 1, 0, 0, 1, 1),
            new TrRmStandardModuleSelection(1, 1, "Provider", 1, 0, 0, 1, 1));

        var bindings = TrRmFixedArrayProviderBindingValidator.Validate(
            tables, [consumerArtifact, providerArtifact],
            [schemaIdentity, schemaIdentity], modules, 1);

        Assert.That(bindings, Has.Length.EqualTo(1));
        Assert.That(bindings[0].ProviderArtifactIndex, Is.EqualTo(1));
        Assert.That(bindings[0].ProviderModuleRow, Is.EqualTo(1));
        Assert.That(bindings[0].ProviderTypeDefRow, Is.EqualTo(2));
    }

    [Test]
    public void RejectsSequentialProviderForExpectedUnionArrayElement()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        var consumerArtifact = CreateA2SourceTypeReferenceConsumerArtifact(
            copyLayoutKind: 2);
        var providerArtifact = CreateCopyProviderArtifact(
            providerLayoutKind: 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(
                        consumerArtifact));
        var modules = ImmutableArray.Create(
            new TrRmStandardModuleSelection(0, 1, "Root", 1, 0, 0, 1, 1),
            new TrRmStandardModuleSelection(1, 1, "Provider", 1, 0, 0, 1, 1));

        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateA2ProviderBinding(consumerArtifact, providerArtifact,
                schemaIdentity, modules));
    }

    [Test]
    public void A2SourceReferencePreservesUnionProviderLayout()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        var consumerArtifact = CreateA2SourceTypeReferenceConsumerArtifact(
            copyLayoutKind: 2);
        var providerArtifact = CreateCopyProviderArtifact(
            providerLayoutKind: 2);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "Root",
            [
                new TrRmStandardArtifactInput(schemaIdentity,
                    providerArtifact),
            ],
            ["ContainerKey"]);

        var importedUnion = reference.Structures.Single(
            static structure => structure.Name == "Pair");
        Assert.That(importedUnion.IsUnion, Is.True);
        var sourceConsumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Envelope {
                public Container Value;
            }
            """), reference);
        Assert.That(sourceConsumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, sourceConsumer.Diagnostics));
    }

    [Test]
    public void StandardA2SourceReferenceRetainsImportedCopyOriginAndExpectation()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        var consumerArtifact = CreateA2SourceTypeReferenceConsumerArtifact();
        var providerArtifact = CreateCopyProviderArtifact();
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "Root",
            [
                new TrRmStandardArtifactInput(schemaIdentity,
                    providerArtifact),
            ],
            ["ContainerKey"]);

        Assert.That(reference.Structures, Has.Length.EqualTo(2));
        var container = reference.Structures.Single(static structure
            => structure.Name == "Container");
        Assert.That(container.StandardTypeOrigin!.IsLookupRoot, Is.True);
        Assert.That(container.StandardTypeOrigin.Provider.ModuleIdentity,
            Is.EqualTo("Root"));
        var pair = reference.Structures.Single(static structure
            => structure.Name == "Pair");
        var pairOrigin = pair.StandardTypeOrigin;
        Assert.That(pairOrigin, Is.Not.Null);
        Assert.That(pairOrigin!.IsLookupRoot, Is.False);
        Assert.That(pairOrigin.Provider.ModuleIdentity, Is.EqualTo("Provider"));
        Assert.That(pairOrigin.DefinitionId, Is.EqualTo(0x1300_0001u));
        Assert.That(pairOrigin.ExportKeys, Has.Length.EqualTo(1));
        Assert.That(pairOrigin.ExportKeys[0], Is.EqualTo("PairKey"));
        Assert.That(pairOrigin.Provider.ArtifactSnapshot.AsSpan()
            .SequenceEqual(providerArtifact), Is.True);
        Assert.That(pairOrigin.Expectations, Has.Length.EqualTo(1));
        Assert.That(pairOrigin.Expectations[0].UsingModule.ModuleIdentity,
            Is.EqualTo("Root"));
        Assert.That(pairOrigin.Expectations[0].ImportRow, Is.EqualTo(1));
        Assert.That(pairOrigin.Expectations[0].ExpectedDefinitionId,
            Is.EqualTo(0x1300_0001u));
        Assert.That(pairOrigin.Expectations[0].ProducerIdentity,
            Is.EqualTo("Provider"));
        Assert.That(pairOrigin.Expectations[0].ExportKey, Is.EqualTo("PairKey"));
        Assert.That(pairOrigin.Expectations[0].TargetRequirement, Is.EqualTo(1));
        Assert.That(pairOrigin.Expectations[0].RuntimePolicy, Is.Zero);
        var expectedSnapshot
            = pairOrigin.Expectations[0].UsingModule.ArtifactSnapshot;
        var actualSnapshot = pairOrigin.Provider.ArtifactSnapshot;

        var sourceConsumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Consumer {
                public Container Value;
            }
            """), reference);
        Assert.That(sourceConsumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, sourceConsumer.Diagnostics));
        var hiddenImport = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Consumer {
                public Pair Value;
            }
            """), reference);
        Assert.That(hiddenImport.HasErrors, Is.True);

        var relocatedProvider = CreateCopyProviderArtifact(
            prependUnrelatedType: true);
        var relocatedReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "Root",
            [
                new TrRmStandardArtifactInput(schemaIdentity,
                    relocatedProvider),
            ],
            ["ContainerKey"]);
        var relocatedPair = relocatedReference.Structures.Single(
            static structure => structure.Name == "Pair");
        Assert.That(relocatedPair.StandardTypeOrigin!.DefinitionId,
            Is.EqualTo(0x1300_0002u));
        Assert.That(relocatedPair.StandardTypeOrigin.Expectations[0]
            .ExpectedDefinitionId, Is.EqualTo(0x1300_0001u));

        var directReferenceArtifact
            = CreateA2SourceTypeReferenceConsumerArtifact(
                includeArrayField: false);
        var directReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity,
                directReferenceArtifact),
            "Root",
            [
                new TrRmStandardArtifactInput(schemaIdentity,
                    CreateCopyProviderArtifact()),
            ],
            ["ContainerKey"]);
        Assert.That(directReference.NativeFixedArrayTypes, Is.Empty);
        Assert.That(directReference.Structures.Single(
            static structure => structure.Name == "Pair")
            .StandardTypeOrigin?.Provider.ModuleIdentity,
            Is.EqualTo("Provider"));

        var incompatibleProvider = CreateCopyProviderArtifact(providerPack: 8);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
                "Root",
                [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleProvider),
                ],
                ["ContainerKey"]));
        consumerArtifact[0] = 0;
        providerArtifact[0] = 0;
        Assert.That(pairOrigin.Expectations[0].UsingModule.ArtifactSnapshot
            .AsSpan().SequenceEqual(expectedSnapshot.AsSpan()), Is.True);
        Assert.That(pairOrigin.Provider.ArtifactSnapshot.AsSpan()
            .SequenceEqual(actualSnapshot.AsSpan()), Is.True);
    }

    [TestCase(
        "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1",
        "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2")]
    [TestCase(
        "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2",
        "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1")]
    public void BindsNongenericCopyProvidersAcrossExplicitArraySchemas(
        string consumerSchemaIdentity, string providerSchemaIdentity)
    {
        const string genericSchemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1";
        var consumerArtifact = CreateImportedArrayConsumerArtifact();
        var providerArtifact = CreateCopyProviderArtifact(
            prependUnrelatedType: true);
        var usesGenericSchema = StringComparer.Ordinal.Equals(
            consumerSchemaIdentity, genericSchemaIdentity);
        var consumerIndex = usesGenericSchema
            ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                    consumerArtifact)
            : TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyDraft(
                    consumerArtifact);
        var tables = usesGenericSchema
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    consumerIndex)
            : TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                    consumerIndex);
        var modules = ImmutableArray.Create(
            new TrRmStandardModuleSelection(0, 1, "Root", 1, 0, 0, 1, 1),
            new TrRmStandardModuleSelection(1, 1, "Provider", 1, 0, 0, 1, 1));

        var bindings = TrRmFixedArrayProviderBindingValidator.Validate(
            tables, [consumerArtifact, providerArtifact],
            [consumerSchemaIdentity, providerSchemaIdentity], modules, 1);

        Assert.That(bindings, Has.Length.EqualTo(1));
        Assert.That(bindings[0].ProviderArtifactIndex, Is.EqualTo(1));
        Assert.That(bindings[0].ProviderModuleRow, Is.EqualTo(1));
        Assert.That(bindings[0].ProviderTypeDefRow, Is.EqualTo(2));
    }

    [Test]
    public void BindsConstructedArrayElementToSelectedA3GenericProvider()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1";
        var consumerArtifact = CreateA3GenericArrayConsumerArtifact();
        var providerArtifact = CreateA3GenericCopyProviderArtifact();
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        consumerArtifact));
        var ledger = tables.A3LayoutResourceLedger
            ?? throw new InvalidOperationException(
                "The A3 consumer has no root resource ledger.");
        var workRemainingBeforeBinding = ledger.WorkRemaining;
        var modules = ImmutableArray.Create(
            new TrRmStandardModuleSelection(0, 1, "Root", 1, 0, 0, 1, 1),
            new TrRmStandardModuleSelection(1, 1, "Provider", 1, 0, 0, 1, 1));

        var bindings = TrRmFixedArrayProviderBindingValidator.Validate(
            tables, [consumerArtifact, providerArtifact],
            [schemaIdentity, schemaIdentity], modules, 1);

        Assert.That(bindings, Has.Length.EqualTo(1));
        Assert.That(bindings[0].ExpectedTypeDefRow, Is.EqualTo(1));
        Assert.That(bindings[0].ProviderArtifactIndex, Is.EqualTo(1));
        Assert.That(bindings[0].ProviderTypeDefRow, Is.EqualTo(1));
        Assert.That(ledger.WorkRemaining, Is.LessThan(workRemainingBeforeBinding));
    }

    [Test]
    public void A3ProviderBindingUsesTheRootResourceLedger()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1";
        var consumerArtifact = CreateA3GenericArrayConsumerArtifact();
        var providerArtifact = CreateA3GenericCopyProviderArtifact();
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        consumerArtifact));
        var ledger = tables.A3LayoutResourceLedger
            ?? throw new InvalidOperationException(
                "The A3 consumer has no root resource ledger.");
        ledger.ConsumeWork(ledger.WorkRemaining);
        var modules = ImmutableArray.Create(
            new TrRmStandardModuleSelection(0, 1, "Root", 1, 0, 0, 1, 1),
            new TrRmStandardModuleSelection(1, 1, "Provider", 1, 0, 0, 1, 1));

        TrRmResourceLimitException? exception = null;
        try
        {
            _ = TrRmFixedArrayProviderBindingValidator.Validate(
                tables, [consumerArtifact, providerArtifact],
                [schemaIdentity, schemaIdentity], modules, 1);
        }
        catch (TrRmResourceLimitException caught)
        {
            exception = caught;
        }

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.Required,
            Is.EqualTo(new System.Numerics.BigInteger(
                TrRmA3LayoutResourceLedger.WorkLimit + 4)));
        Assert.That(TrRmExecutableCompiler.TryGetA3AdmissionFailure(exception,
            out var kind), Is.True);
        Assert.That(kind,
            Is.EqualTo(TrRmA3AdmissionFailureKind.ResourceLimit));
    }

    [Test]
    public void A3ProviderBindingWorkLimitIsExact()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1";
        var consumerArtifact = CreateA3GenericArrayConsumerArtifact(
            uniqueNominalActual: true);
        var providerArtifact = CreateA3GenericCopyProviderArtifact(
            ownershipDomain: 2, includeBlittableConstraint: false,
            pointerField: true);
        var uniqueProviderArtifact = CreateA3UniqueNominalProviderArtifact(
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft,
            declaredUnique: true);
        byte[][] artifacts =
        [
            consumerArtifact,
            providerArtifact,
            uniqueProviderArtifact,
        ];
        string[] schemaIdentities =
        [
            schemaIdentity,
            schemaIdentity,
            schemaIdentity,
        ];
        var modules = ImmutableArray.Create(
            new TrRmStandardModuleSelection(0, 1, "Root", 1, 0, 0, 1, 1),
            new TrRmStandardModuleSelection(1, 1, "Provider", 1, 0, 0, 1, 1),
            new TrRmStandardModuleSelection(2, 1, "UniqueProvider", 1, 0,
                0, 1, 1));

        static TrRmStructuralMetadataValidator.ValidatedExecutionTables
            CreateTables(byte[] artifact,
                TrRmA3LayoutResourceLedger resourceLedger)
        {
            return TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                            artifact),
                    resourceLedger);
        }

        var measuredLedger = new TrRmA3LayoutResourceLedger();
        var measuredTables = CreateTables(consumerArtifact, measuredLedger);
        var workBeforeBinding = measuredLedger.WorkRemaining;
        var bindings = TrRmFixedArrayProviderBindingValidator.Validate(
            measuredTables, artifacts, schemaIdentities, modules, 1);
        var bindingWork = workBeforeBinding - measuredLedger.WorkRemaining;
        Assert.That(bindings, Has.Length.EqualTo(2));
        Assert.That(bindings[0].ImportRow,
            Is.LessThan(bindings[1].ImportRow));

        var exactLedger = new TrRmA3LayoutResourceLedger();
        var exactTables = CreateTables(consumerArtifact, exactLedger);
        exactLedger.ConsumeWork(
            exactLedger.WorkRemaining - bindingWork);
        _ = TrRmFixedArrayProviderBindingValidator.Validate(exactTables,
            artifacts, schemaIdentities, modules, 1);
        Assert.That(exactLedger.WorkRemaining, Is.Zero);

        var exhaustedLedger = new TrRmA3LayoutResourceLedger();
        var exhaustedTables = CreateTables(consumerArtifact, exhaustedLedger);
        exhaustedLedger.ConsumeWork(
            exhaustedLedger.WorkRemaining - (bindingWork - 1));
        TrRmResourceLimitException? exception = null;
        try
        {
            _ = TrRmFixedArrayProviderBindingValidator.Validate(
                exhaustedTables, artifacts, schemaIdentities, modules, 1);
        }
        catch (TrRmResourceLimitException caught)
        {
            exception = caught;
        }

        Assert.That(exception!.Required,
            Is.EqualTo(new System.Numerics.BigInteger(
                TrRmA3LayoutResourceLedger.WorkLimit + 1)));
    }

    [Test]
    public void A3BindingPlanRevalidationWorkLimitIsExact()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1";
        var consumerArtifact = CreateA3GenericArrayConsumerArtifact();
        var providerArtifact = CreateA3GenericCopyProviderArtifact();
        byte[][] artifacts = [consumerArtifact, providerArtifact];
        string[] schemaIdentities = [schemaIdentity, schemaIdentity];
        var modules = ImmutableArray.Create(
            new TrRmStandardModuleSelection(0, 1, "Root", 1, 0, 1, 1, 2),
            new TrRmStandardModuleSelection(1, 1, "Provider", 1, 0, 0, 1, 1));
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return 42; }"));
        Assert.That(compilation.HasErrors, Is.False);
        var rootProgram = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");

        TrRmA3ResourceLedgerTestHelper.AssertExactWork(resourceLedger =>
        {
            var tables = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                            consumerArtifact),
                    resourceLedger);
            var typeBindings = TrRmFixedArrayProviderBindingValidator.Validate(
                tables, artifacts, schemaIdentities, modules, 1);
            var plan = new TrRmStandardNativeBindingPlan(rootProgram, "Root",
                artifacts, schemaIdentities, modules, 1,
                TrRmStandardNativeOperation.EmitExecutable,
                NativeRuntimePolicy.FailFast, [], typeBindings,
                resourceLedger);

            try
            {
                _ = plan.CreateLoweringInputs(
                    TrRmStandardNativeOperation.EmitExecutable);
                Assert.Fail("Expected a no-import rejection.");
            }
            catch (NotSupportedException exception)
            {
                if (exception is TrRmResourceLimitException)
                {
                    throw;
                }

                Assert.That(exception.Message,
                    Does.Contain("no direct NativeImport"));
            }
        });
    }

    [Test]
    public void A3ProviderReplacementRevalidationWorkLimitIsExact()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1";
        var consumerArtifact = CreateA3GenericArrayConsumerArtifact();
        var providerArtifact = CreateA3GenericCopyProviderArtifact();
        byte[][] artifacts = [consumerArtifact, providerArtifact];
        string[] schemaIdentities = [schemaIdentity, schemaIdentity];
        var modules = ImmutableArray.Create(
            new TrRmStandardModuleSelection(0, 1, "Root", 1, 0, 1, 1, 2),
            new TrRmStandardModuleSelection(1, 1, "Provider", 1, 0, 0, 1, 1));
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        consumerArtifact));
        var typeBindings = TrRmFixedArrayProviderBindingValidator.Validate(
            tables, artifacts, schemaIdentities, modules, 1);
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return 42; }"));
        Assert.That(compilation.HasErrors, Is.False);
        var rootProgram = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var replacement = CreateA3GenericCopyProviderArtifact(
            declaredCopy: false);
        Assert.That(replacement.Length, Is.EqualTo(providerArtifact.Length));
        Array.Copy(replacement, providerArtifact, providerArtifact.Length);

        TrRmA3ResourceLedgerTestHelper.AssertExactWork(resourceLedger =>
        {
            var plan = new TrRmStandardNativeBindingPlan(rootProgram, "Root",
                artifacts, schemaIdentities, modules, 1,
                TrRmStandardNativeOperation.EmitExecutable,
                NativeRuntimePolicy.FailFast, [], typeBindings,
                resourceLedger);

            try
            {
                _ = plan.CreateLoweringInputs(
                    TrRmStandardNativeOperation.EmitExecutable);
                Assert.Fail("Expected changed provider bindings to be rejected.");
            }
            catch (TrRmResourceLimitException)
            {
                throw;
            }
            catch (InvalidDataException exception)
            {
                Assert.That(exception.Message,
                    Does.Contain("fixed-array TypeDef provider binding"));
            }
        });
    }

    [Test]
    public void StandardNativeBindingPlanRetainsAndRevalidatesA2TypeBindings()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        var consumerArtifact = CreateImportedArrayConsumerArtifact();
        var providerArtifact = CreateCopyProviderArtifact(
            prependUnrelatedType: true);
        var artifacts = new[] { consumerArtifact, providerArtifact };
        var schemaIdentities = new[] { schemaIdentity, schemaIdentity };
        var rootTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(
                        consumerArtifact));
        var modules = ImmutableArray.Create(
            new TrRmStandardModuleSelection(0, 1, "Root", 1, 0, 0, 1, 1),
            new TrRmStandardModuleSelection(1, 1, "Provider", 1, 0, 0, 1, 1));
        var typeBindings = TrRmFixedArrayProviderBindingValidator.Validate(
            rootTables, artifacts, schemaIdentities, modules, 1);
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return 42; }"));
        Assert.That(compilation.HasErrors, Is.False);
        var rootProgram = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var plan = new TrRmStandardNativeBindingPlan(rootProgram, "Root",
            artifacts, schemaIdentities, modules, 1,
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast, [], typeBindings, null);

        var noImport = Assert.Throws<NotSupportedException>(() =>
            plan.CreateLoweringInputs(
                TrRmStandardNativeOperation.EmitExecutable));
        Assert.That(noImport!.Message, Does.Contain("no direct NativeImport"));

        var replacement = CreateCopyProviderArtifact(
            prependUnrelatedType: true, providerPack: 8);
        Assert.That(replacement.Length, Is.EqualTo(providerArtifact.Length));
        Array.Copy(replacement, providerArtifact, providerArtifact.Length);
        var mismatch = Assert.Throws<InvalidDataException>(() =>
            plan.CreateLoweringInputs(
                TrRmStandardNativeOperation.EmitExecutable));
        Assert.That(mismatch!.Message,
            Does.Contain("fixed-array TypeDef provider binding"));
    }

    [Test]
    public void A3ValueAndLayoutReadersUseTheRootResourceLedger()
    {
        var artifact = CreateA3GenericArrayConsumerArtifact(
            includeMeteredReaderTables: true);
        var ledger = new TrRmA3LayoutResourceLedger();
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        artifact),
                ledger);

        var workRemaining = ledger.WorkRemaining;
        Assert.That(tables.Blobs.GetRow(1).Length, Is.EqualTo(4));
        Assert.That(ledger.WorkRemaining, Is.LessThan(workRemaining));

        workRemaining = ledger.WorkRemaining;
        Assert.That(tables.FrozenValueNodes.GetKind(1), Is.EqualTo((byte)1));
        Assert.That(ledger.WorkRemaining, Is.LessThan(workRemaining));

        workRemaining = ledger.WorkRemaining;
        Assert.That(tables.ConstantSources.GetKind(1), Is.EqualTo(1u));
        Assert.That(ledger.WorkRemaining, Is.LessThan(workRemaining));

        workRemaining = ledger.WorkRemaining;
        Assert.That(tables.TargetLayoutDependencies.GetQueryKind(1),
            Is.EqualTo((byte)3));
        Assert.That(ledger.WorkRemaining, Is.LessThan(workRemaining));

        workRemaining = ledger.WorkRemaining;
        Assert.That(tables.TargetLayoutDependencyFields.GetFieldReference(1),
            Is.EqualTo(0x1400_0001u));
        Assert.That(ledger.WorkRemaining, Is.LessThan(workRemaining));

        workRemaining = ledger.WorkRemaining;
        Assert.That(tables.FieldPaths.GetFieldReference(1, 0),
            Is.EqualTo(0x1400_0001u));
        Assert.That(ledger.WorkRemaining, Is.LessThan(workRemaining));

        workRemaining = ledger.WorkRemaining;
        Assert.That(tables.PredicateGraphs.Count, Is.Zero);
        Assert.That(ledger.WorkRemaining, Is.LessThan(workRemaining));

        workRemaining = ledger.WorkRemaining;
        Assert.That(tables.PredicateReadFootprints.Count, Is.Zero);
        Assert.That(ledger.WorkRemaining, Is.LessThan(workRemaining));

        workRemaining = ledger.WorkRemaining;
        Assert.That(tables.PredicateReadRoots.Count, Is.Zero);
        Assert.That(ledger.WorkRemaining, Is.LessThan(workRemaining));

        workRemaining = ledger.WorkRemaining;
        Assert.That(tables.PredicateReadDependencies.Count, Is.Zero);
        Assert.That(ledger.WorkRemaining, Is.LessThan(workRemaining));

        workRemaining = ledger.WorkRemaining;
        Assert.That(tables.PredicateChildEdges.Count, Is.Zero);
        Assert.That(ledger.WorkRemaining, Is.LessThan(workRemaining));

        workRemaining = ledger.WorkRemaining;
        Assert.That(tables.PredicateCaptures.Count, Is.Zero);
        Assert.That(ledger.WorkRemaining, Is.LessThan(workRemaining));

        workRemaining = ledger.WorkRemaining;
        Assert.That(tables.PredicateNodes.Count, Is.Zero);
        Assert.That(ledger.WorkRemaining, Is.LessThan(workRemaining));

        workRemaining = ledger.WorkRemaining;
        Assert.That(tables.PredicateNodeOperands.Count, Is.Zero);
        Assert.That(ledger.WorkRemaining, Is.LessThan(workRemaining));
    }

    [Test]
    public void A3InterfaceBaseReadersUseTheRootResourceLedger()
    {
        var artifact = CreateA3GenericArrayConsumerArtifact(
            includeInterfaceHierarchy: true);
        var ledger = new TrRmA3LayoutResourceLedger();
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        artifact),
                ledger);

        var workRemaining = ledger.WorkRemaining;
        Assert.That(tables.Interfaces.GetInterfaceDefForTypeDef(3),
            Is.EqualTo(2));
        Assert.That(ledger.WorkRemaining, Is.LessThan(workRemaining));

        workRemaining = ledger.WorkRemaining;
        Assert.That(tables.InterfaceBases.GetBaseTypeSpecRef(1),
            Is.EqualTo(6u));
        Assert.That(ledger.WorkRemaining, Is.LessThan(workRemaining));
    }

    [Test]
    public void A3PublishedSelectorReservesItsEmptyStateAtExactWorkBoundary()
    {
        TrRmA3ResourceLedgerTestHelper.AssertExactWork(ledger =>
        {
            var contracts = new TrRmContractTableReader(
                [], 0, default, default, default,
                default, default,
                TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft,
                ledger);
            var selectorKinds
                = TrRmPublishedPredicateSelectorValidator.Validate(
                    [], 0,
                    [], 0,
                    [], 0, 0,
                    default, default, default, default,
                    default, default, default, contracts, ledger);
            Assert.That(selectorKinds, Is.EqualTo(new byte[1]));
        });
    }

    [Test]
    public void BindsA3ProviderWithMatchingPackedArrayGeometry()
    {
        var consumerArtifact = CreateA3GenericArrayConsumerArtifact(pack: 1);
        var providerArtifact = CreateA3GenericCopyProviderArtifact(pack: 1);

        var bindings = ValidateA3GenericArrayProviderBinding(
            consumerArtifact, providerArtifact);

        Assert.That(bindings, Has.Length.EqualTo(1));
    }

    [Test]
    public void BindsCompatibleA3ProviderAfterTypeDefRelocation()
    {
        var consumerArtifact = CreateA3GenericArrayConsumerArtifact();
        var providerArtifact = CreateA3GenericCopyProviderArtifact(
            prependUnrelatedType: true);

        var bindings = ValidateA3GenericArrayProviderBinding(
            consumerArtifact, providerArtifact);

        Assert.That(bindings, Has.Length.EqualTo(1));
        Assert.That(bindings[0].ProviderTypeDefRow, Is.EqualTo(2));
    }

    [Test]
    public void StandardNativeBindingPlanRetainsAndRevalidatesA3TypeBindings()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1";
        var consumerArtifact = CreateA3GenericArrayConsumerArtifact();
        var providerArtifact = CreateA3GenericCopyProviderArtifact();
        var artifacts = new[] { consumerArtifact, providerArtifact };
        var schemaIdentities = new[] { schemaIdentity, schemaIdentity };
        var rootTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        consumerArtifact));
        var modules = ImmutableArray.Create(
            new TrRmStandardModuleSelection(0, 1, "Root", 1, 0, 1, 1, 2),
            new TrRmStandardModuleSelection(1, 1, "Provider", 1, 0, 0, 1, 1));
        var typeBindings = TrRmFixedArrayProviderBindingValidator.Validate(
            rootTables, artifacts, schemaIdentities, modules, 1);
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return 42; }"));
        Assert.That(compilation.HasErrors, Is.False);
        var rootProgram = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var plan = new TrRmStandardNativeBindingPlan(
            rootProgram, "Root", artifacts, schemaIdentities, modules, 1,
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast, [], typeBindings,
            rootTables.A3LayoutResourceLedger);
        var ledger = rootTables.A3LayoutResourceLedger
            ?? throw new InvalidOperationException(
                "The A3 consumer has no root resource ledger.");
        Assert.That(plan.A3AdmissionScope,
            Is.SameAs(ledger.AdmissionScope));

        var noImport = Assert.Throws<NotSupportedException>(() =>
            plan.CreateLoweringInputs(
                TrRmStandardNativeOperation.EmitExecutable));
        Assert.That(noImport!.Message, Does.Contain("no direct NativeImport"));
        Assert.That(TrRmExecutableCompiler.TryGetA3AdmissionFailure(noImport,
            out var noImportKind), Is.True);
        Assert.That(noImportKind,
            Is.EqualTo(TrRmA3AdmissionFailureKind.UnsupportedAdmission));

        var replacement = CreateA3GenericCopyProviderArtifact(
            declaredCopy: false);
        Array.Copy(replacement, providerArtifact, providerArtifact.Length);
        var workBeforeRevalidation = ledger.WorkRemaining;
        var revalidationLimit = Assert.Throws<TrRmResourceLimitException>(() =>
            plan.CreateLoweringInputs(
                TrRmStandardNativeOperation.EmitExecutable));
        Assert.That(revalidationLimit!.Required,
            Is.GreaterThan(new System.Numerics.BigInteger(
                TrRmA3LayoutResourceLedger.WorkLimit)));
        Assert.That(revalidationLimit.Dimension, Is.EqualTo("work"));
        Assert.That(ledger.WorkRemaining,
            Is.LessThan(workBeforeRevalidation));

        ledger.ConsumeWork(ledger.WorkRemaining);
        var limitException = Assert.Throws<TrRmResourceLimitException>(() =>
            plan.CreateLoweringInputs(
                TrRmStandardNativeOperation.EmitExecutable));
        Assert.That(limitException!.Required,
            Is.GreaterThan(TrRmA3LayoutResourceLedger.WorkLimit));
        Assert.That(limitException.Dimension, Is.EqualTo("work"));
        Assert.That(TrRmExecutableCompiler.TryGetA3AdmissionFailure(
            limitException, out var kind), Is.True);
        Assert.That(kind,
            Is.EqualTo(TrRmA3AdmissionFailureKind.ResourceLimit));
    }

    [Test]
    public void RejectsA3ProviderWithDifferentDerivedArrayAlignment()
    {
        var consumerArtifact = CreateA3GenericArrayConsumerArtifact(
            pack: 1, explicitAlignment: 8);
        var providerArtifact = CreateA3GenericCopyProviderArtifact(pack: 16);

        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateA3GenericArrayProviderBinding(consumerArtifact,
                providerArtifact));
    }

    [Test]
    public void RejectsA2ProviderForConstructedA3GenericType()
    {
        var consumerArtifact = CreateA3GenericArrayConsumerArtifact();
        var providerArtifact = CreateA3GenericCopyProviderArtifact(
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);

        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateA3GenericArrayProviderBinding(consumerArtifact,
                providerArtifact,
                "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2"));
    }

    [Test]
    public void RejectsGenericProviderWithDifferentFormalDomain()
    {
        var consumerArtifact = CreateA3GenericArrayConsumerArtifact();
        var providerArtifact = CreateA3GenericCopyProviderArtifact(
            ownershipDomain: 2);

        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateA3GenericArrayProviderBinding(consumerArtifact,
                providerArtifact));
    }

    [Test]
    public void BindsGenericProviderWithMatchingEarlierFormalDefault()
    {
        var consumerArtifact = CreateA3GenericArrayConsumerArtifact(
            twoFormals: true);
        var providerArtifact = CreateA3GenericCopyProviderArtifact(
            twoFormals: true);

        var bindings = ValidateA3GenericArrayProviderBinding(
            consumerArtifact, providerArtifact);

        Assert.That(bindings, Has.Length.EqualTo(1));
    }

    [Test]
    public void RejectsGenericProviderWithDifferentEarlierFormalDefault()
    {
        var consumerArtifact = CreateA3GenericArrayConsumerArtifact(
            twoFormals: true);
        var providerArtifact = CreateA3GenericCopyProviderArtifact(
            twoFormals: true, includeSecondFormalDefault: false);

        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateA3GenericArrayProviderBinding(consumerArtifact,
                providerArtifact));
    }

    [Test]
    public void RejectsGenericProviderWithDifferentBlittableConstraint()
    {
        var consumerArtifact = CreateA3GenericArrayConsumerArtifact();
        var providerArtifact = CreateA3GenericCopyProviderArtifact(
            includeBlittableConstraint: false);

        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateA3GenericArrayProviderBinding(consumerArtifact,
                providerArtifact));
    }

    [Test]
    public void BindsNestedConstructedFieldsAcrossA3Providers()
    {
        var (consumerArtifact, providerArtifact)
            = CreateA3NestedGenericArrayProviderArtifacts();
        var bindings = ValidateA3GenericArrayProviderBinding(
            consumerArtifact, providerArtifact);

        Assert.That(bindings, Has.Length.EqualTo(2));
        Assert.That(bindings[0].ExportKey, Is.EqualTo("CellKey"));
        Assert.That(bindings[1].ExportKey, Is.EqualTo("InnerKey"));
    }

    [Test]
    public void RejectsNestedConstructedProviderFieldMismatch()
    {
        var (consumerArtifact, providerArtifact)
            = CreateA3NestedGenericArrayProviderArtifacts(
                providerInnerFieldUsesFormal: false);

        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateA3GenericArrayProviderBinding(consumerArtifact,
                providerArtifact));
    }

    [Test]
    public void RejectsGenericProviderWithDifferentFieldTemplate()
    {
        var consumerArtifact = CreateA3GenericArrayConsumerArtifact();
        var providerArtifact = CreateA3GenericCopyProviderArtifact(
            storeFormal: false);

        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateA3GenericArrayProviderBinding(consumerArtifact,
                providerArtifact));
    }

    [Test]
    public void BindsUniqueGenericActualToSelectedA3Provider()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1";
        var consumerArtifact = CreateA3GenericArrayConsumerArtifact(
            uniqueNominalActual: true);
        var providerArtifact = CreateA3GenericCopyProviderArtifact(
            ownershipDomain: 2, includeBlittableConstraint: false,
            pointerField: true);
        var uniqueProviderArtifact
            = CreateA3UniqueNominalProviderArtifact(schema:
                TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft,
                declaredUnique: true);

        var bindings = ValidateA3GenericArrayUniqueProviderBinding(
            consumerArtifact, providerArtifact, uniqueProviderArtifact,
            schemaIdentity);

        Assert.That(bindings, Has.Length.EqualTo(2));
        Assert.That(bindings[0].ExportKey, Is.EqualTo("CellKey"));
        Assert.That(bindings[1].ExportKey, Is.EqualTo("UniqueKey"));
        Assert.That(bindings[1].ProviderArtifactIndex, Is.EqualTo(2));
    }

    [Test]
    public void RejectsUniqueGenericActualWithoutSelectedProviderUniqueEvidence()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1";
        var consumerArtifact = CreateA3GenericArrayConsumerArtifact(
            uniqueNominalActual: true);
        var providerArtifact = CreateA3GenericCopyProviderArtifact(
            ownershipDomain: 2, includeBlittableConstraint: false,
            pointerField: true);
        var uniqueProviderArtifact
            = CreateA3UniqueNominalProviderArtifact(schema:
                TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft,
                declaredUnique: false);

        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateA3GenericArrayUniqueProviderBinding(consumerArtifact,
                providerArtifact, uniqueProviderArtifact, schemaIdentity));
    }

    [Test]
    public void RejectsA2ProviderForUniqueGenericActual()
    {
        const string a2SchemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        var consumerArtifact = CreateA3GenericArrayConsumerArtifact(
            uniqueNominalActual: true);
        var providerArtifact = CreateA3GenericCopyProviderArtifact(
            ownershipDomain: 2, includeBlittableConstraint: false,
            pointerField: true);
        var uniqueProviderArtifact = CreateA3UniqueNominalProviderArtifact(
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft,
            declaredUnique: false);

        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateA3GenericArrayUniqueProviderBinding(consumerArtifact,
                providerArtifact, uniqueProviderArtifact, a2SchemaIdentity));
    }

    [Test]
    public void A3AcceptsTypeOnlyCopyGenericDeclarationWithBlittableConstraint()
    {
        var artifact = CreateA3GenericDeclarationArtifact();
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        artifact));

        Assert.That(tables.GenericParams.Count, Is.EqualTo(2));
        Assert.That(tables.Constraints.Count, Is.EqualTo(1));
        Assert.That(tables.TypeDefs.GetGenericRange(1).Count, Is.EqualTo(2));
    }

    [Test]
    public void A3SubstitutesClosedGenericFieldsBeforeArrayGeometry()
    {
        var artifact = CreateA3GenericDeclarationArtifact(includeArray: true);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        artifact));

        Assert.That(tables.FixedArrayLayouts.GetRow(1),
            Is.EqualTo(new TrRmFixedArrayLayoutRow(1, 6, 1, 4, 12, 4)));
    }

    [Test]
    public void A3SubstitutesNestedGenericFieldsBeforeArrayGeometry()
    {
        var artifact = CreateA3NestedGenericArrayArtifact();
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        artifact));

        Assert.That(tables.FixedArrayLayouts.GetRow(1),
            Is.EqualTo(new TrRmFixedArrayLayoutRow(1, 9, 1, 4, 12, 4)));
    }

    [Test]
    public void A3AcceptsFiniteRepeatedGenericDefinitionInNestedLayout()
    {
        var artifact = CreateA3NestedGenericArrayArtifact(
            nestedConstructedActual: true);
        TrRmA3ResourceLedgerTestHelper.AssertExactWork(resourceLedger =>
        {
            var tables = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                            artifact),
                    resourceLedger);

            Assert.That(tables.FixedArrayLayouts.GetRow(1),
                Is.EqualTo(new TrRmFixedArrayLayoutRow(1, 9, 1, 4, 12, 4)));
        });
    }

    [Test]
    public void A3LayoutWorkLimitIsInclusive()
    {
        var ledger = new TrRmA3LayoutResourceLedger();
        for (ulong unit = 0; unit < TrRmA3LayoutResourceLedger.WorkLimit;
            unit++)
        {
            ledger.ConsumeWork();
        }

        var exception = Assert.Throws<TrRmResourceLimitException>(
            () => ledger.ConsumeWork());

        Assert.That(exception!.Operation,
            Is.EqualTo("trrm.a3-fixed-array-layout/1"));
        Assert.That(exception.Scope, Is.EqualTo("root-request"));
        Assert.That(exception.Dimension, Is.EqualTo("work"));
        Assert.That(exception.Limit,
            Is.EqualTo(TrRmA3LayoutResourceLedger.WorkLimit));
        Assert.That(exception.Required, Is.EqualTo(new System.Numerics.BigInteger(
            TrRmA3LayoutResourceLedger.WorkLimit + 1)));
        Assert.That(TrRmExecutableCompiler.TryGetA3AdmissionFailure(exception,
            out var kind), Is.True);
        Assert.That(kind,
            Is.EqualTo(TrRmA3AdmissionFailureKind.ResourceLimit));
    }

    [Test]
    public void A3LayoutDepthLimitIsInclusive()
    {
        var ledger = new TrRmA3LayoutResourceLedger();
        var frames = new List<TrRmA3LayoutResourceLedger.FrameLease>(
            checked((int)TrRmA3LayoutResourceLedger.DepthLimit));
        try
        {
            for (ulong depth = 0;
                depth < TrRmA3LayoutResourceLedger.DepthLimit; depth++)
            {
                frames.Add(ledger.EnterFrame());
            }

            var workRemaining = ledger.WorkRemaining;
            var exception = Assert.Throws<TrRmResourceLimitException>(
                () => ledger.EnterFrame());

            Assert.That(exception!.Dimension, Is.EqualTo("depth"));
            Assert.That(exception.Limit,
                Is.EqualTo(TrRmA3LayoutResourceLedger.DepthLimit));
            Assert.That(exception.Required, Is.EqualTo(new System.Numerics.BigInteger(
                TrRmA3LayoutResourceLedger.DepthLimit + 1)));
            Assert.That(ledger.WorkRemaining, Is.EqualTo(workRemaining));
            Assert.That(TrRmExecutableCompiler.TryGetA3AdmissionFailure(
                exception, out var kind), Is.True);
            Assert.That(kind,
                Is.EqualTo(TrRmA3AdmissionFailureKind.ResourceLimit));
        }
        finally
        {
            for (var index = frames.Count - 1; index >= 0; index--)
            {
                frames[index].Dispose();
            }
        }
    }

    [Test]
    public void A3DepthLimitIncludesASelectedProviderLayoutContext()
    {
        var ledger = new TrRmA3LayoutResourceLedger();
        using var depthContext = ledger.EnterDepthContext(
            TrRmA3LayoutResourceLedger.DepthLimit);
        using var outerFrame = ledger.EnterFrame();
        var workRemaining = ledger.WorkRemaining;

        var exception = Assert.Throws<TrRmResourceLimitException>(
            () => ledger.EnterFrame());

        Assert.That(exception!.Dimension, Is.EqualTo("depth"));
        Assert.That(exception.Required,
            Is.EqualTo(new System.Numerics.BigInteger(
                TrRmA3LayoutResourceLedger.DepthLimit + 1)));
        Assert.That(ledger.WorkRemaining, Is.EqualTo(workRemaining));
        Assert.That(TrRmExecutableCompiler.TryGetA3AdmissionFailure(exception,
            out var kind), Is.True);
        Assert.That(kind,
            Is.EqualTo(TrRmA3AdmissionFailureKind.ResourceLimit));
    }

    [Test]
    public void A3DepthAggregationDoesNotWrap()
    {
        var ledger = new TrRmA3LayoutResourceLedger();
        using var outerFrame = ledger.EnterFrame();
        using var innerFrame = ledger.EnterFrame();
        var workRemaining = ledger.WorkRemaining;
        var maximum = new System.Numerics.BigInteger(ulong.MaxValue);

        var contextException = Assert.Throws<TrRmResourceLimitException>(
            () => ledger.EnterDepthContext(ulong.MaxValue));
        Assert.That(contextException!.Required, Is.EqualTo(maximum + 1));
        Assert.That(ledger.WorkRemaining, Is.EqualTo(workRemaining));

        var relativeException = Assert.Throws<TrRmResourceLimitException>(
            () => ledger.CheckDepthFromCurrent(ulong.MaxValue));
        Assert.That(relativeException!.Required, Is.EqualTo(maximum + 2));
        Assert.That(ledger.WorkRemaining, Is.EqualTo(workRemaining));

        var reservationException = Assert.Throws<TrRmResourceLimitException>(
            () => ledger.ReserveWorkAtDepth(1, ulong.MaxValue));
        Assert.That(reservationException!.Required,
            Is.EqualTo(maximum + 2));
        Assert.That(ledger.WorkRemaining, Is.EqualTo(workRemaining));
    }

    [TestCase("LoadStandard")]
    [TestCase("CreateStandardReference")]
    [TestCase("LoadStandardNativeBindingPlan")]
    public void A3StandardOperationsReserveArtifactSnapshotBytesBeforeCopy(
        string operation)
    {
        using var artifactMemory = new TrackingMemoryManager(
            checked((int)TrRmA3LayoutResourceLedger.WorkLimit + 1));
        var artifact = artifactMemory.Memory;
        artifactMemory.ResetSpanAccessCount();
        var input = new TrRmStandardArtifactInput(
            "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1",
            artifact);

        Action invoke = operation switch
        {
            "LoadStandard" => () =>
                _ = TrRmExecutableCompiler.LoadStandard(
                    input, "SnapshotRoot", []),
            "CreateStandardReference" => () =>
                _ = TrRmExecutableCompiler.CreateStandardReference(
                    input, "SnapshotRoot", []),
            "LoadStandardNativeBindingPlan" => () =>
                _ = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
                    input, "SnapshotRoot", [],
                    TrRmStandardNativeOperation.EmitExecutable,
                    NativeRuntimePolicy.FailFast),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        var exception
            = Assert.Throws<TrRmResourceLimitException>(invoke);

        Assert.That(exception!.Dimension, Is.EqualTo("work"));
        Assert.That(exception.Required,
            Is.GreaterThan(TrRmA3LayoutResourceLedger.WorkLimit));
        Assert.That(artifactMemory.SpanAccessCount, Is.Zero);
        Assert.That(TrRmExecutableCompiler.TryGetA3AdmissionFailure(
            exception, out var failureKind), Is.True);
        Assert.That(failureKind,
            Is.EqualTo(TrRmA3AdmissionFailureKind.ResourceLimit));
    }

    [Test]
    public void A3LoadBoundsProviderSnapshotBeforeAllocating()
    {
        var emptyArtifact = Array.Empty<byte>();
        var input = new TrRmStandardArtifactInput(
            "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1",
            emptyArtifact);
        var providers = new OversizedProviderList(int.MaxValue);

        var exception = Assert.Throws<TrRmResourceLimitException>(() =>
            TrRmExecutableCompiler.LoadStandard(input, "SnapshotRoot",
                providers));

        Assert.That(exception!.Dimension, Is.EqualTo("work"));
        Assert.That(exception.Required,
            Is.GreaterThan(TrRmA3LayoutResourceLedger.WorkLimit));
        Assert.That(providers.IndexerAccessCount, Is.Zero);
        Assert.That(TrRmExecutableCompiler.TryGetA3AdmissionFailure(
            exception, out var failureKind), Is.True);
        Assert.That(failureKind,
            Is.EqualTo(TrRmA3AdmissionFailureKind.ResourceLimit));
    }

    [Test]
    public void A3AdmissionClassifierRequiresAnExactMarkedException()
    {
        var exception = new InvalidDataException(
            "Invalid TRRM FixedArrayLayout element or target geometry.");

        Assert.That(TrRmExecutableCompiler.TryGetA3AdmissionFailure(exception,
            out var kind), Is.False);
        Assert.That(kind, Is.EqualTo(TrRmA3AdmissionFailureKind.None));
        _ = Assert.Throws<ArgumentNullException>(() =>
            TrRmExecutableCompiler.TryGetA3AdmissionFailure(null!, out _));
    }

    [Test]
    public void A3AdmissionMarkerPreservesOnlyItsOriginalRootScope()
    {
        var exception = new InvalidDataException("Invalid A3 artifact.");
        var rootScope = new TrRmA3LayoutResourceLedger().AdmissionScope;
        var otherScope = new TrRmA3LayoutResourceLedger().AdmissionScope;

        rootScope.Mark(exception,
            TrRmA3AdmissionFailureKind.InvalidArtifact);
        rootScope.Mark(exception,
            TrRmA3AdmissionFailureKind.UnsupportedAdmission);
        Assert.That(TrRmExecutableCompiler.TryGetA3AdmissionFailure(exception,
            out var kind), Is.True);
        Assert.That(kind,
            Is.EqualTo(TrRmA3AdmissionFailureKind.InvalidArtifact));

        otherScope.Mark(exception,
            TrRmA3AdmissionFailureKind.UnsupportedAdmission);
        Assert.That(TrRmExecutableCompiler.TryGetA3AdmissionFailure(exception,
            out kind), Is.False);
        Assert.That(kind, Is.EqualTo(TrRmA3AdmissionFailureKind.None));
    }

    [Test]
    public void A3ExecutionHandoffRemovesMarkersFromEscapingExceptions()
    {
        var scope = new TrRmA3LayoutResourceLedger().AdmissionScope;
        var exception = new InvalidDataException("Execution failure.");
        scope.Mark(exception,
            TrRmA3AdmissionFailureKind.InvalidArtifact);
        Assert.That(TrRmExecutableCompiler.TryGetA3AdmissionFailure(exception,
            out _), Is.True);

        var escaped = Assert.Throws<InvalidDataException>(() =>
            scope.ExecuteAfterHandoff<int>(() => throw exception));

        Assert.That(escaped, Is.SameAs(exception));
        Assert.That(scope.IsBeforeExecutionHandoff, Is.False);
        Assert.That(TrRmExecutableCompiler.TryGetA3AdmissionFailure(escaped!,
            out var kind), Is.False);
        Assert.That(kind, Is.EqualTo(TrRmA3AdmissionFailureKind.None));
    }

    [Test]
    public void A3RejectsDirectConstructedInlineLayoutCycle()
    {
        var artifact = CreateA3GenericDeclarationArtifact(
            includeArray: true, recursiveField: true);

        var exception = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                            artifact)));
        Assert.That(TrRmExecutableCompiler.TryGetA3AdmissionFailure(
            exception!, out var kind), Is.True);
        Assert.That(kind,
            Is.EqualTo(TrRmA3AdmissionFailureKind.InvalidArtifact));
    }

    [Test]
    public void A3ReportsResourceLimitForGrowingConstructedLayoutExpansion()
    {
        var artifact = CreateA3GenericDeclarationArtifact(
            includeArray: true, growingRecursiveField: true);

        var exception = Assert.Throws<TrRmResourceLimitException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                            artifact)));

        Assert.That(exception!.Operation,
            Is.EqualTo("trrm.a3-fixed-array-layout/1"));
        Assert.That(exception.Scope, Is.EqualTo("root-request"));
        Assert.That(exception.Dimension, Is.EqualTo("work"));
        Assert.That(exception.Limit,
            Is.EqualTo(TrRmA3LayoutResourceLedger.WorkLimit));
        Assert.That(exception.Required,
            Is.GreaterThan(TrRmA3LayoutResourceLedger.WorkLimit));
        Assert.That(TrRmExecutableCompiler.TryGetA3AdmissionFailure(exception,
            out var kind), Is.True);
        Assert.That(kind,
            Is.EqualTo(TrRmA3AdmissionFailureKind.ResourceLimit));
    }

    [Test]
    public void A3PublicLoadResourceLimitIncludesSelectedModule()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1";
        var input = new TrRmStandardArtifactInput(schemaIdentity,
            CreateA3GrowingRecursiveLayoutArtifact());

        var exception = Assert.Throws<TrRmResourceLimitException>(() =>
            TrRmExecutableCompiler.LoadStandard(input, "Module", []));

        Assert.That(exception!.ModuleIdentity, Is.EqualTo("Module"));
        Assert.That(exception.Message, Does.Contain("Selected Module: 'Module'"));

        var validTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        CreateA3GenericDeclarationArtifact(
                            includeArray: true)));
        Assert.That(validTables.FixedArrayLayouts.Count, Is.EqualTo(1));
    }

    [Test]
    public void A3DerivesPackedConstructedLayoutGeometry()
    {
        var artifact = CreateA3GenericDeclarationArtifact(
            includeArray: true, pack: 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        artifact));

        Assert.That(tables.FixedArrayLayouts.GetRow(1),
            Is.EqualTo(new TrRmFixedArrayLayoutRow(1, 6, 1, 4, 12, 1)));
    }

    [Test]
    public void A3DerivesExplicitlyAlignedConstructedLayoutGeometry()
    {
        var artifact = CreateA3GenericDeclarationArtifact(
            includeArray: true, explicitAlignment: 8);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        artifact));

        Assert.That(tables.FixedArrayLayouts.GetRow(1),
            Is.EqualTo(new TrRmFixedArrayLayoutRow(1, 6, 1, 8, 24, 8)));
    }

    [Test]
    public void A3DerivesTransparentZeroPayloadGeometry()
    {
        var artifact = CreateA3GenericDeclarationArtifact(
            includeArray: true, voidActual: true, transparentLayout: true);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        artifact));

        Assert.That(tables.FixedArrayLayouts.GetRow(1),
            Is.EqualTo(new TrRmFixedArrayLayoutRow(1, 6, 1, 0, 0, 1)));
    }

    [Test]
    public void A3DerivesNestedClosedArrayFieldGeometry()
    {
        var artifact = CreateA3GenericDeclarationArtifact(
            includeArray: true, closedArrayField: true);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        artifact));

        Assert.That(tables.FixedArrayLayouts.Count, Is.EqualTo(2));
        Assert.That(tables.FixedArrayLayouts.GetRow(1),
            Is.EqualTo(new TrRmFixedArrayLayoutRow(1, 5, 1, 4, 8, 4)));
        Assert.That(tables.FixedArrayLayouts.GetRow(2),
            Is.EqualTo(new TrRmFixedArrayLayoutRow(1, 7, 1, 8, 24, 4)));
    }

    [Test]
    public void A3ValidatesConstructedElementAtZeroArrayExtent()
    {
        var artifact = CreateA3GenericDeclarationArtifact(
            includeArray: true, arrayCount: 0);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        artifact));

        Assert.That(tables.FixedArrayLayouts.GetRow(1),
            Is.EqualTo(new TrRmFixedArrayLayoutRow(1, 6, 1, 4, 0, 4)));
    }

    [Test]
    public void A3RejectsMissingCopyForZeroExtentArray()
    {
        var artifact = CreateA3GenericDeclarationArtifact(
            includeArray: true, declaredCopy: false, arrayCount: 0);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                            artifact)));
    }

    [Test]
    public void A3RejectsConstructedArrayExtentOverflow()
    {
        _ = Assert.Throws<InvalidDataException>(
            () => CreateA3GenericDeclarationArtifact(includeArray: true,
                arrayCount: ulong.MaxValue, arrayExtentOverride: 0));
    }

    [Test]
    public void A3RejectsNestedClosedArrayExtentOverflow()
    {
        var artifact = CreateA3GenericDeclarationArtifact(
            includeArray: true, closedArrayField: true,
            nestedArrayCount: ulong.MaxValue / sizeof(uint),
            arrayCount: 1, secondField: true, pack: 1);

        var exception = Assert.Throws<OverflowException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                            artifact)));
        Assert.That(TrRmExecutableCompiler.TryGetA3AdmissionFailure(
            exception!, out var kind), Is.True);
        Assert.That(kind,
            Is.EqualTo(TrRmA3AdmissionFailureKind.TargetLayoutOverflow));
    }

    [Test]
    public void A3RetainsWritablePointerActualQualification()
    {
        var artifact = CreateA3GenericDeclarationArtifact(
            includeArray: true, writablePointerActual: true);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        artifact));

        Assert.That(tables.FixedArrayLayouts.GetRow(1),
            Is.EqualTo(new TrRmFixedArrayLayoutRow(1, 6, 1, 8, 24, 8)));
    }

    [Test]
    public void A3SubstitutesWritableIndexedPointerActual()
    {
        var artifact = CreateA3GenericDeclarationArtifact(
            includeArray: true, indexedWritablePointerActual: true);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        artifact));

        Assert.That(tables.FixedArrayLayouts.GetRow(1),
            Is.EqualTo(new TrRmFixedArrayLayoutRow(1, 7, 1, 8, 24, 8)));
    }

    [Test]
    public void A3RejectsWriteQualificationOnInlineValueActual()
    {
        var artifact = CreateA3GenericDeclarationArtifact(
            includeArray: true, storeFormal: false,
            writableValueActual: true);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                            artifact)));
    }

    [Test]
    public void A3RejectsGcQualificationOnPointerActual()
    {
        var artifact = CreateA3GenericDeclarationArtifact(
            includeArray: true, storeFormal: false,
            gcPointerActual: true);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                            artifact)));
    }

    [Test]
    public void A3AcceptsSpanInitActualWithoutDroppingItsElementQualification()
    {
        var artifact = CreateA3GenericDeclarationArtifact(
            includeArray: true, includeBlittableConstraint: false,
            storeFormal: false, spanInitActual: true);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        artifact));

        Assert.That(tables.FixedArrayLayouts.GetRow(1),
            Is.EqualTo(new TrRmFixedArrayLayoutRow(1, 8, 1, 4, 12, 4)));
        var constructedRef = tables.TypeSpecs.GetConstructedArgument(7, 0);
        var span = tables.TypeSpecs.GetTypeSpecRef(constructedRef);
        Assert.That(span.Form, Is.EqualTo((byte)0x0C));
        Assert.That(span.TypeCode, Is.EqualTo((byte)0x8A));
        Assert.That(tables.TypeSpecs.GetTypeSpecRef(5,
            allowInitQualifier: true).Qualifiers, Is.EqualTo((byte)0x04));
    }

    [Test]
    public void A3RejectsActualOutsideItsUniqueOnlyDomain()
    {
        var artifact = CreateA3GenericDeclarationArtifact(includeArray: true,
            ownershipDomain: 2, includeBlittableConstraint: false);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                            artifact)));
    }

    [Test]
    public void A3RejectsBlittableConstraintForBoxActual()
    {
        var artifact = CreateA3GenericDeclarationArtifact(
            includeArray: true, boxedActual: true);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                            .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                                artifact)));
    }

    [Test]
    public void A3AllowsUniqueActualBehindCopyablePointerCell()
    {
        var artifact = CreateA3GenericDeclarationArtifact(includeArray: true,
            ownershipDomain: 2, includeBlittableConstraint: false,
            pointerField: true, uniqueActual: true);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        artifact));

        Assert.That(tables.FixedArrayLayouts.GetRow(1),
            Is.EqualTo(new TrRmFixedArrayLayoutRow(1, 6, 1, 8, 24, 8)));
    }

    [Test]
    public void A3AllowsUniqueNominalActualBehindCopyablePointerCell()
    {
        var artifact = CreateA3GenericDeclarationArtifact(includeArray: true,
            ownershipDomain: 2, includeBlittableConstraint: false,
            pointerField: true, uniqueNominalActual: true);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        artifact));

        Assert.That(tables.TypeDefs.HasDeclaredUnique(2), Is.True);
        Assert.That(tables.FixedArrayLayouts.GetRow(1),
            Is.EqualTo(new TrRmFixedArrayLayoutRow(1, 7, 1, 8, 24, 8)));
    }

    [Test]
    public void A3AllowsUniqueNominalPhantomActual()
    {
        var artifact = CreateA3GenericDeclarationArtifact(includeArray: true,
            ownershipDomain: 3, includeBlittableConstraint: false,
            storeFormal: false, uniqueNominalActual: true);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        artifact));

        Assert.That(tables.FixedArrayLayouts.GetRow(1),
            Is.EqualTo(new TrRmFixedArrayLayoutRow(1, 7, 1, 4, 12, 4)));
    }

    [Test]
    public void A3RejectsUniqueBlittableNominalActualInDefaultDomain()
    {
        var artifact = CreateA3GenericDeclarationArtifact(includeArray: true,
            storeFormal: false, uniqueNominalActual: true);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                            artifact)));
    }

    [Test]
    public void A3RejectsUnclassifiedNominalActualInUniqueOnlyDomain()
    {
        var artifact = CreateA3GenericDeclarationArtifact(includeArray: true,
            ownershipDomain: 2, includeBlittableConstraint: false,
            pointerField: true, unclassifiedNominalActual: true);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                            artifact)));
    }

    [Test]
    public void A3RejectsDirectStorageOfUniqueNominalActual()
    {
        var artifact = CreateA3GenericDeclarationArtifact(includeArray: true,
            ownershipDomain: 2, includeBlittableConstraint: false,
            uniqueNominalActual: true);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                            artifact)));
    }

    [Test]
    public void A3RejectsSimultaneousDeclaredCopyAndUniqueFlags()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            CreateA3GenericDeclarationArtifact(
                uniqueNominalActual: true,
                conflictingNominalCopyUniqueFlags: true));
    }

    [Test]
    public void A3RejectsUniqueActualStoredDirectlyDespiteUniqueOnlyDomain()
    {
        var artifact = CreateA3GenericDeclarationArtifact(includeArray: true,
            ownershipDomain: 2, includeBlittableConstraint: false,
            uniqueActual: true);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                            artifact)));
    }

    [Test]
    public void A3RequiresRootlessFeatureForUniqueTypeDefEvidence()
    {
        var artifact = CreateA3GenericDeclarationArtifact(
            declaredCopy: false, declaredBlittable: false,
            uniqueNominalActual: true, omitRootlessFeature: true);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                            artifact)));
    }

    [Test]
    public void A3AllowsCleanupOnDeclaredUniqueType()
    {
        var artifact = CreateUniqueCleanupContractArtifact(
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft,
            declaredUnique: true);

        _ = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        artifact));
    }

    [Test]
    public void A3RequiresDeclaredUniqueForCleanupType()
    {
        var artifact = CreateUniqueCleanupContractArtifact(
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft,
            declaredUnique: false);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                            artifact)));
    }

    [Test]
    public void A2CleanupRowsRemainUnchangedWithoutUniqueTypeFlag()
    {
        var artifact = CreateUniqueCleanupContractArtifact(
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft,
            declaredUnique: false);

        _ = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));
    }

    [Test]
    public void A3AllowsNonRootlessUniquePhantomActual()
    {
        var artifact = CreateA3GenericDeclarationArtifact(includeArray: true,
            ownershipDomain: 3, includeBlittableConstraint: false,
            storeFormal: false, uniqueActual: true);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        artifact));

        Assert.That(tables.FixedArrayLayouts.GetRow(1),
            Is.EqualTo(new TrRmFixedArrayLayoutRow(1, 6, 1, 4, 12, 4)));
    }

    [Test]
    public void A3RejectsNullableActualInNonNullFormalDomain()
    {
        var artifact = CreateA3GenericDeclarationArtifact(includeArray: true,
            includeBlittableConstraint: false, pointerField: true,
            nullableActual: true);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                            artifact)));
    }

    [Test]
    public void A3AllowsNullableActualInNullablePointerFormalDomain()
    {
        var artifact = CreateA3GenericDeclarationArtifact(includeArray: true,
            includeBlittableConstraint: false, pointerField: true,
            nullabilityDomain: 2, nullableActual: true);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        artifact));

        Assert.That(tables.FixedArrayLayouts.GetRow(1),
            Is.EqualTo(new TrRmFixedArrayLayoutRow(1, 6, 1, 8, 24, 8)));
    }

    [Test]
    public void A3RequiresDeclaredCopyForStoredGenericInstance()
    {
        var artifact = CreateA3GenericDeclarationArtifact(
            declaredCopy: false, includeArray: true);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                            artifact)));
    }

    [Test]
    public void A3RequiresBlittableLayoutForStoredGenericInstance()
    {
        var artifact = CreateA3GenericDeclarationArtifact(
            declaredBlittable: false, includeArray: true);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                            artifact)));
    }

    [Test]
    public void A2DoesNotAdmitConstructedGenericArrayElements()
    {
        var artifact = CreateA3GenericDeclarationArtifact(includeArray: true);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayCopyDraft(
                            artifact)));
    }

    [Test]
    public void A3RejectsOpenConstructedArrayElementRecipes()
    {
        var artifact = CreateA3GenericDeclarationArtifact(
            includeArray: true, openArrayElement: true);

        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                            artifact)));
    }

    [Test]
    public void A3RejectsConstantGenericFormals()
    {
        var artifact = CreateA3GenericDeclarationArtifact(formalKind: 2);

        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                            artifact)));
    }

    [Test]
    public void A3RejectsUnsupportedGenericConstraints()
    {
        var artifact = CreateA3GenericDeclarationArtifact(
            constraintPayload: 1);

        var exception = Assert.Throws<NotSupportedException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                            artifact)));
        Assert.That(TrRmExecutableCompiler.TryGetA3AdmissionFailure(
            exception!, out var kind), Is.True);
        Assert.That(kind,
            Is.EqualTo(TrRmA3AdmissionFailureKind.UnsupportedAdmission));
    }

    [Test]
    public void A3RejectsDefaultsThatReferToTheCurrentOrLaterFormal()
    {
        var artifact = CreateA3GenericDeclarationArtifact(
            secondDefaultReference: 4);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                            artifact)));
    }

    [Test]
    public void A3RetainsUnstoredGenericTypeMetadataWithoutCopyLayout()
    {
        var artifact = CreateA3GenericDeclarationArtifact(
            declaredCopy: false, declaredBlittable: false);

        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        artifact));

        Assert.That(tables.TypeDefs.HasDeclaredCopy(1), Is.False);
    }

    [Test]
    public void ValidatesProviderModulesWithTheirOwnTypeImports()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        var artifact = CreateChainedArrayProviderArtifact(
            providerLeafUsesFloat: false);
        var bindings = ValidateChainedArrayProviderArtifact(artifact,
            schemaIdentity);

        Assert.That(bindings, Has.Length.EqualTo(3));
        Assert.That(bindings.Any(static binding =>
            binding.ConsumerModuleRow == 2 && binding.ImportRow == 3
            && binding.ProviderModuleRow == 3
            && binding.ProviderTypeDefRow == 5), Is.True);
    }

    [Test]
    public void RejectsAProviderModuleWithAnIncompatibleImportExpectation()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        var artifact = CreateChainedArrayProviderArtifact(
            providerLeafUsesFloat: true);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateChainedArrayProviderArtifact(artifact, schemaIdentity));
    }

    [Test]
    public void BindsNestedArrayFieldInSelectedCopyProvider()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        var artifact = CreateNestedArrayProviderArtifact(
            providerUsesDifferentArray: false);

        var bindings = ValidateNestedArrayProviderArtifact(artifact,
            schemaIdentity);

        Assert.That(bindings, Has.Length.EqualTo(1));
        Assert.That(bindings[0].ProviderModuleRow, Is.EqualTo(2));
        Assert.That(bindings[0].ProviderTypeDefRow, Is.EqualTo(2));
    }

    [Test]
    public void RejectsSameLayoutProviderWithDifferentNestedArrayIdentity()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        var artifact = CreateNestedArrayProviderArtifact(
            providerUsesDifferentArray: true);

        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateNestedArrayProviderArtifact(artifact, schemaIdentity));
    }

    [Test]
    public void BindsProviderFunctionPointerAnchorsBySignatureNotMethodRow()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        var artifact = CreateFunctionPointerArrayProviderArtifact(
            providerUsesUInt64: false);

        var bindings = ValidateFunctionPointerArrayProviderArtifact(
            artifact, schemaIdentity);

        Assert.That(bindings, Has.Length.EqualTo(1));
        Assert.That(bindings[0].ProviderModuleRow, Is.EqualTo(2));
    }

    [Test]
    public void RejectsProviderFunctionPointerAnchorWithDifferentSignature()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        var artifact = CreateFunctionPointerArrayProviderArtifact(
            providerUsesUInt64: true);

        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateFunctionPointerArrayProviderArtifact(artifact,
                schemaIdentity));
    }

    [Test]
    public void RejectsImportedArrayElementWithoutMatchingProviderCopyLayout()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateImportedArrayProvider(
                CreateImportedArrayProviderArtifact(providerCopy: false)));
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateImportedArrayProvider(
                CreateImportedArrayProviderArtifact(providerTypeCode: 0x15)));
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateExternalImportedArrayProvider(
                CreateCopyProviderArtifact(providerPack: 8)));
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateExternalImportedArrayProvider(
                CreateCopyProviderArtifact(providerAlignment: 4)));
    }

    [Test]
    public void DeclaredCopyEvidenceIsA2ScopedAndRequiresRootlessFeature()
    {
        var artifact = CreateCopyTypeArtifact(includeRootlessFeature: true);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));
        Assert.That(tables.TypeDefs.HasDeclaredCopy(1), Is.True);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                new TrRmArtifactIndex(artifact)));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForForeignInvocationStorageFixedArrayCopyDraft(
                        CreateCopyTypeArtifact(includeRootlessFeature: false))));
    }

    private static byte[] CreateArtifact(ulong count, ulong stride,
        ulong extent, ulong alignment)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("Module", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 1);
        var featuresBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.FixedInlineArrayStorageCandidate,
        ], schema);
        var features = new TrRmFeatureTableReader(featuresBytes, 1, schema);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: 1),
        ], strings, 0, 0, 0, 0, 1);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            0, 0, 0, 0, 1);
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.FixedInlineArray(0xF200_000D, count),
        ], 0, 0, 0, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes, 1, 0, 0,
            0, 0, schema);
        var targetProfilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1, schema);
        var fixedArrayLayoutsBytes = TrRmFixedArrayLayoutTableWriter.Encode([
            new TrRmFixedArrayLayoutRow(1, 1, 1, stride, extent, alignment),
        ], typeSpecs, targetProfiles, features, modules);

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, modulesBytes),
            new TrRmSection(0x0002, 0, 1, stringsBytes),
            new TrRmSection(0x0004, 0, 1, featuresBytes),
            new TrRmSection(0x0011, 0, 1, typeSpecsBytes),
            new TrRmSection(0x0012, 0, 1, fixedArrayLayoutsBytes),
            new TrRmSection(0x0048, 0, 1, targetProfilesBytes),
        ], schema);
    }

    private static void CreateAndValidateArtifact(ulong count, ulong stride,
        ulong extent, ulong alignment)
    {
        var artifact = CreateArtifact(count, stride, extent, alignment);
        _ = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));
    }

    private static void CreateDuplicateNormalizedArrayLayouts()
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([("Module", 0)]);
        var strings = new TrRmStringTableReader(stringsBytes, 1);
        var featuresBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.FixedInlineArrayStorageCandidate,
        ], schema);
        var features = new TrRmFeatureTableReader(featuresBytes, 1, schema);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1,
                    FeatureFirst: 1, FeatureCount: 1),
        ], strings, 0, 0, 0, 0, 1);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            0, 0, 0, 0, 1);
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.FixedInlineArray(0xF200_000D, 3),
            TrRmTypeSpecTableWriter.FixedInlineArray(0xF200_000D, 3),
        ], 0, 0, 0, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes, 2, 0, 0,
            0, 0, schema);
        var targetProfilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1, schema);

        _ = TrRmFixedArrayLayoutTableWriter.Encode([
            new TrRmFixedArrayLayoutRow(1, 1, 1, 4, 12, 4),
            new TrRmFixedArrayLayoutRow(1, 2, 1, 4, 12, 4),
        ], typeSpecs, targetProfiles, features, modules);
    }

    private static byte[] CreateImportedArrayProviderArtifact(
        bool providerCopy = true, byte providerTypeCode = 0x0D)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Root", 0), ("Provider", 0), ("Pair", 0),
            ("Value", 0), ("PairKey", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 6);
        var featureRows = new[]
        {
            TrRmFeatureRow.RootlessNativeValues,
            TrRmFeatureRow.FixedInlineArrayStorageCandidate,
        };
        var featuresBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var features = new TrRmFeatureTableReader(featuresBytes, 2, schema);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: 2),
            new TrRmModuleRow(3, 1, 0, 0, 2, 2, 1, 1,
                FeatureFirst: 1, FeatureCount: 1),
        ], strings, 1, 2, 0, 0, 2);
        var modules = new TrRmModuleTableReader(modulesBytes, 2, strings,
            1, 2, 0, 0, 2);
        var moduleRefsBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(3, 1, 0),
        ], strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefsBytes, 1,
            strings, modules);
        ReadOnlyMemory<byte>[] typeSpecRows = providerTypeCode == 0x0D
            ? [
                TrRmTypeSpecTableWriter.Builtin(0x0D),
                TrRmTypeSpecTableWriter.Reference(0x03, 1),
                TrRmTypeSpecTableWriter.FixedInlineArray(2, 3),
            ]
            : [
                TrRmTypeSpecTableWriter.Builtin(0x0D),
                TrRmTypeSpecTableWriter.Reference(0x03, 1),
                TrRmTypeSpecTableWriter.FixedInlineArray(2, 3),
                TrRmTypeSpecTableWriter.Builtin(providerTypeCode),
            ];
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            2, 1, 0, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes,
            typeSpecRows.Length, 2, 1, 0, 0, schema);
        var typeDefsBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 4, 0x26, 1, 1, 0, 0, 1, 2, 0, 0),
            new TrRmTypeDefRow(1, 4, providerCopy ? 0x26u : 0x06u,
                2, 1, 0, 0, 3, 2, 0, 0),
        ], strings, modules, 2, 0, 4, 0, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes, 2,
            strings, modules, 2, 0, 4, 0, schema);
        var fieldsBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 5, 1, 0),
            new TrRmFieldDefRow(2, 5,
                providerTypeCode == 0x0D ? 1u : 4u, 0),
        ], strings, typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldsBytes, 2, strings,
            typeDefs, typeSpecs);
        var targetProfilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1, schema);
        var layoutsBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 1, 0, 0, 16, 1, 1),
            new TrRmLayoutRow(1, 1, 1, 1, 0, 16, 2, 1),
            new TrRmLayoutRow(2, 0, 1, 0, 0, 16, 3, 1),
            new TrRmLayoutRow(2, 1, 1, 1, 0, 16, 4, 1),
        ], typeDefs, targetProfiles, 4);
        var layouts = new TrRmLayoutTableReader(layoutsBytes, 4,
            typeDefs, targetProfiles, 4);
        var layoutFieldsBytes = TrRmLayoutFieldTableWriter.Encode([
            new TrRmLayoutFieldRow(1, 0, 1),
            new TrRmLayoutFieldRow(2, 0, 1),
            new TrRmLayoutFieldRow(3, 0, 2),
            new TrRmLayoutFieldRow(4, 0, 2),
        ], layouts, typeDefs, fields);
        var layoutFields = new TrRmLayoutFieldTableReader(
            layoutFieldsBytes, 4, layouts, typeDefs, fields);
        var importsBytes = TrRmImportTableWriter.Encode([
            new TrRmImportRow(1, 6, 0x1300_0001),
        ], strings, moduleRefs, typeDefs, default, default);
        var imports = new TrRmImportTableReader(importsBytes, 1, strings,
            moduleRefs, typeDefs, default, default);
        var exportsBytes = TrRmExportTableWriter.Encode([
            new TrRmExportRow(0x1300_0002, 6),
        ], strings, typeDefs, default, default, imports);
        var exports = new TrRmExportTableReader(exportsBytes, 1, strings,
            typeDefs, default, default, imports);
        var fixedArrayLayoutsBytes = TrRmFixedArrayLayoutTableWriter.Encode([
            new TrRmFixedArrayLayoutRow(1, 3, 1, 4, 12, 4),
        ], typeSpecs, targetProfiles, features, modules);
        _ = exports;
        _ = layoutFields;

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 2, modulesBytes),
            new TrRmSection(0x0002, 0, 6, stringsBytes),
            new TrRmSection(0x0004, 0, 2, featuresBytes),
            new TrRmSection(0x0010, 0, 1, moduleRefsBytes),
            new TrRmSection(0x0011, 0, (uint)typeSpecRows.Length,
                typeSpecsBytes),
            new TrRmSection(0x0012, 0, 1, fixedArrayLayoutsBytes),
            new TrRmSection(0x0013, 0, 2, typeDefsBytes),
            new TrRmSection(0x0014, 0, 2, fieldsBytes),
            new TrRmSection(0x0015, 0, 4, layoutsBytes),
            new TrRmSection(0x001C, 0, 1, importsBytes),
            new TrRmSection(0x001D, 0, 1, exportsBytes),
            new TrRmSection(0x0023, 0, 4, layoutFieldsBytes),
            new TrRmSection(0x0048, 0, 1, targetProfilesBytes),
        ], schema);
    }

    private static byte[] CreateA2SourceTypeReferenceConsumerArtifact(
        bool includeArrayField = true, byte copyLayoutKind = 1)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Root", 0), ("Provider", 0), ("Pair", 0),
            ("Value", 0), ("PairKey", 0), ("Container", 0), ("Direct", 0),
            ("Values", 0), ("ContainerKey", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 10);
        var featureRows = includeArrayField
            ? new[]
            {
                TrRmFeatureRow.RootlessNativeValues,
                TrRmFeatureRow.FixedInlineArrayStorageCandidate,
            }
            : [TrRmFeatureRow.RootlessNativeValues];
        var featuresBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var features = new TrRmFeatureTableReader(featuresBytes,
            featureRows.Length, schema);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: featureRows.Length),
        ], strings, 1, 2, 0, 0, featureRows.Length);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            1, 2, 0, 0, featureRows.Length);
        var moduleRefsBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(3, 1, 0),
        ], strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefsBytes, 1,
            strings, modules);
        ReadOnlyMemory<byte>[] typeSpecRows = includeArrayField
            ? [
                TrRmTypeSpecTableWriter.Builtin(0x0D),
                TrRmTypeSpecTableWriter.Reference(0x03, 1),
                TrRmTypeSpecTableWriter.FixedInlineArray(2, 2),
            ]
            : [
                TrRmTypeSpecTableWriter.Builtin(0x0D),
                TrRmTypeSpecTableWriter.Reference(0x03, 1),
            ];
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            2, 1, 0, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes,
            typeSpecRows.Length, 2,
            1, 0, 0, schema);
        var containerFieldCount = includeArrayField ? 2 : 1;
        var fieldCount = 1 + containerFieldCount;
        var layoutFieldCount = 2 + 2 * containerFieldCount;
        var typeDefsBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 4, 0x26, 1, 1, 0, 0, 1, 2, 0, 0),
            new TrRmTypeDefRow(1, 7, 0x26, 2, containerFieldCount,
                0, 0, 3, 2, 0, 0),
        ], strings, modules, fieldCount, 0, 4, 0, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes, 2, strings,
            modules, fieldCount, 0, 4, 0, schema);
        var fieldRows = new List<TrRmFieldDefRow>
        {
            new(1, 5, 1, 0),
            new(2, 8, 2, 0),
        };
        if (includeArrayField)
        {
            fieldRows.Add(new TrRmFieldDefRow(2, 9, 3, 0));
        }

        var fieldsBytes = TrRmFieldDefTableWriter.Encode(fieldRows, strings,
            typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldsBytes, fieldCount, strings,
            typeDefs, typeSpecs);
        var profilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], schema);
        var profiles = new TrRmTargetProfileTableReader(profilesBytes, 1,
            schema);
        var profileContainerFieldFirst = 3 + containerFieldCount;
        var copyPack = copyLayoutKind is 1 or 2 ? 16u : 0u;
        var layoutsBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, copyLayoutKind, 0, 0, copyPack,
                1, 1),
            new TrRmLayoutRow(1, 1, copyLayoutKind, 1, 0, copyPack,
                2, 1),
            new TrRmLayoutRow(2, 0, 1, 0, 0, 16, 3,
                containerFieldCount),
            new TrRmLayoutRow(2, 1, 1, 1, 0, 16,
                profileContainerFieldFirst, containerFieldCount),
        ], typeDefs, profiles, layoutFieldCount);
        var layouts = new TrRmLayoutTableReader(layoutsBytes, 4, typeDefs,
            profiles, layoutFieldCount);
        var layoutFieldRows = new List<TrRmLayoutFieldRow>
        {
            new(1, 0, 1),
            new(2, 0, 1),
            new(3, 0, 2),
        };
        if (includeArrayField)
        {
            layoutFieldRows.Add(new TrRmLayoutFieldRow(3, 1, 3));
        }
        layoutFieldRows.Add(new TrRmLayoutFieldRow(4, 0, 2));
        if (includeArrayField)
        {
            layoutFieldRows.Add(new TrRmLayoutFieldRow(4, 1, 3));
        }

        var layoutFieldBytes = TrRmLayoutFieldTableWriter.Encode(
            layoutFieldRows, layouts, typeDefs, fields);
        var layoutFields = new TrRmLayoutFieldTableReader(
            layoutFieldBytes, layoutFieldCount, layouts, typeDefs, fields);
        var importsBytes = TrRmImportTableWriter.Encode([
            new TrRmImportRow(1, 6, 0x1300_0001),
        ], strings, moduleRefs, typeDefs, default, default);
        var imports = new TrRmImportTableReader(importsBytes, 1, strings,
            moduleRefs, typeDefs, default, default);
        var exportsBytes = TrRmExportTableWriter.Encode([
            new TrRmExportRow(0x1300_0002, 10),
        ], strings, typeDefs, default, default, imports);
        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 1, modulesBytes),
            new(0x0002, 0, 10, stringsBytes),
            new(0x0004, 0, (uint)featureRows.Length, featuresBytes),
            new(0x0010, 0, 1, moduleRefsBytes),
            new(0x0011, 0, (uint)typeSpecRows.Length, typeSpecsBytes),
        };
        if (includeArrayField)
        {
            var fixedArrayLayoutsBytes
                = TrRmFixedArrayLayoutTableWriter.Encode([
                    new TrRmFixedArrayLayoutRow(1, 3, 1, 4, 8, 4),
                ], typeSpecs, profiles, features, modules);
            sections.Add(new TrRmSection(0x0012, 0, 1,
                fixedArrayLayoutsBytes));
        }

        sections.Add(new TrRmSection(0x0013, 0, 2, typeDefsBytes));
        sections.Add(new TrRmSection(0x0014, 0, (uint)fieldCount, fieldsBytes));
        sections.Add(new TrRmSection(0x0015, 0, 4, layoutsBytes));
        sections.Add(new TrRmSection(0x001C, 0, 1, importsBytes));
        sections.Add(new TrRmSection(0x001D, 0, 1, exportsBytes));
        sections.Add(new TrRmSection(0x0023, 0, (uint)layoutFieldCount,
            layoutFieldBytes));
        sections.Add(new TrRmSection(0x0048, 0, 1, profilesBytes));
        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static byte[] CreateImportedArrayConsumerArtifact()
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Root", 0), ("Provider", 0), ("Pair", 0),
            ("Value", 0), ("PairKey", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 6);
        var featuresBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.RootlessNativeValues,
            TrRmFeatureRow.FixedInlineArrayStorageCandidate,
        ], schema);
        var features = new TrRmFeatureTableReader(featuresBytes, 2, schema);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: 2),
        ], strings, 1, 1, 0, 0, 2);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            1, 1, 0, 0, 2);
        var moduleRefsBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(3, 1, 0),
        ], strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefsBytes, 1,
            strings, modules);
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(0x0D),
            TrRmTypeSpecTableWriter.Reference(0x03, 1),
            TrRmTypeSpecTableWriter.FixedInlineArray(2, 3),
        ], 1, 1, 0, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes, 3, 1,
            1, 0, 0, schema);
        var typeDefsBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 4, 0x26, 1, 1, 0, 0, 1, 2, 0, 0),
        ], strings, modules, 1, 0, 2, 0, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes, 1, strings,
            modules, 1, 0, 2, 0, schema);
        var fieldsBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 5, 1, 0),
        ], strings, typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldsBytes, 1, strings,
            typeDefs, typeSpecs);
        var profilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], schema);
        var profiles = new TrRmTargetProfileTableReader(profilesBytes, 1,
            schema);
        var layoutsBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 1, 0, 0, 16, 1, 1),
            new TrRmLayoutRow(1, 1, 1, 1, 0, 16, 2, 1),
        ], typeDefs, profiles, 2);
        var layouts = new TrRmLayoutTableReader(layoutsBytes, 2, typeDefs,
            profiles, 2);
        var layoutFieldsBytes = TrRmLayoutFieldTableWriter.Encode([
            new TrRmLayoutFieldRow(1, 0, 1),
            new TrRmLayoutFieldRow(2, 0, 1),
        ], layouts, typeDefs, fields);
        var importBytes = TrRmImportTableWriter.Encode([
            new TrRmImportRow(1, 6, 0x1300_0001),
        ], strings, moduleRefs, typeDefs, default, default);
        var fixedArrayLayoutsBytes = TrRmFixedArrayLayoutTableWriter.Encode([
            new TrRmFixedArrayLayoutRow(1, 3, 1, 4, 12, 4),
        ], typeSpecs, profiles, features, modules);

        List<TrRmSection> sections =
        [
            new TrRmSection(0x0001, 0, 1, modulesBytes),
            new TrRmSection(0x0002, 0, 6, stringsBytes),
            new TrRmSection(0x0004, 0, 2, featuresBytes),
            new TrRmSection(0x0010, 0, 1, moduleRefsBytes),
            new TrRmSection(0x0011, 0, 3, typeSpecsBytes),
            new TrRmSection(0x0012, 0, 1, fixedArrayLayoutsBytes),
            new TrRmSection(0x0013, 0, 1, typeDefsBytes),
            new TrRmSection(0x0014, 0, 1, fieldsBytes),
            new TrRmSection(0x0015, 0, 2, layoutsBytes),
        ];
        sections.Add(new TrRmSection(0x001C, 0, 1, importBytes));
        sections.Add(new TrRmSection(0x0023, 0, 2, layoutFieldsBytes));
        sections.Add(new TrRmSection(0x0048, 0, 1, profilesBytes));
        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static byte[] CreateA3GenericArrayConsumerArtifact(
        bool uniqueNominalActual = false, bool twoFormals = false,
        uint pack = 16, uint explicitAlignment = 0,
        bool includeMeteredReaderTables = false,
        bool includeInterfaceHierarchy = false)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft;
        var stringRows = new List<(string Text, int Encoding)>
        {
            ("", 0), ("Root", 0), ("Provider", 0), ("UniqueProvider", 0),
            ("Cell", 0), ("Value", 0), ("T", 0), ("U", 0), ("CellKey", 0),
            ("UniquePayload", 0), ("UniqueKey", 0),
        };
        var baseInterfaceNameRow = 0;
        var derivedInterfaceNameRow = 0;
        if (includeInterfaceHierarchy)
        {
            stringRows.Add(("IBase", 0));
            baseInterfaceNameRow = stringRows.Count;
            stringRows.Add(("IDerived", 0));
            derivedInterfaceNameRow = stringRows.Count;
        }

        var stringsBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var strings = new TrRmStringTableReader(stringsBytes,
            stringRows.Count);
        var featureRows = new[]
        {
            TrRmFeatureRow.RootlessNativeValues,
            TrRmFeatureRow.FixedInlineArrayStorageCandidate,
        };
        var featuresBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var features = new TrRmFeatureTableReader(featuresBytes,
            featureRows.Length, schema);
        var typeDefCount = (uniqueNominalActual ? 2 : 1)
            + (includeInterfaceHierarchy ? 2 : 0);
        var genericParamCount = twoFormals ? 2 : 1;
        var moduleRefCount = uniqueNominalActual ? 2 : 1;
        var importCount = moduleRefCount;
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: 2),
        ], strings, moduleRefCount, typeDefCount, 0, 0,
            featureRows.Length);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            moduleRefCount, typeDefCount, 0, 0, featureRows.Length);
        var moduleRefRows = new List<TrRmModuleRefRow>
        {
            new TrRmModuleRefRow(3, 1, 0),
        };
        if (uniqueNominalActual)
        {
            moduleRefRows.Add(new TrRmModuleRefRow(4, 1, 0));
        }

        var moduleRefsBytes = TrRmModuleRefTableWriter.Encode(moduleRefRows,
            strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefsBytes,
            moduleRefRows.Count, strings, modules);
        var typeSpecRows = new List<ReadOnlyMemory<byte>>
        {
            TrRmTypeSpecTableWriter.Builtin(0x0D),
            TrRmTypeSpecTableWriter.Reference(0x03, 1),
        };
        uint uniqueActualTypeSpecRef = 0;
        if (uniqueNominalActual)
        {
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x03, 2));
            uniqueActualTypeSpecRef = (uint)typeSpecRows.Count;
        }

        var constructedTypeSpecRow = (uint)typeSpecRows.Count + 1;
        var actualTypeSpecRef
            = uniqueNominalActual ? uniqueActualTypeSpecRef : 0xF200_000D;
        typeSpecRows.Add(twoFormals
            ? TrRmTypeSpecTableWriter.Constructed(2,
                [actualTypeSpecRef, actualTypeSpecRef])
            : TrRmTypeSpecTableWriter.Constructed(2, [actualTypeSpecRef]));
        var arrayTypeSpecRow = (uint)typeSpecRows.Count + 1;
        typeSpecRows.Add(TrRmTypeSpecTableWriter.FixedInlineArray(
            constructedTypeSpecRow, 3));
        var formalTypeSpecRef = (uint)typeSpecRows.Count + 1;
        typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x04, 1));
        if (twoFormals)
        {
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x04, 2));
        }

        var fieldTypeSpecRef = formalTypeSpecRef;
        if (uniqueNominalActual)
        {
            fieldTypeSpecRef = (uint)typeSpecRows.Count + 1;
            typeSpecRows.Add(TrRmTypeSpecTableWriter.IndexedFamily(0x8C,
                formalTypeSpecRef));
        }

        var baseInterfaceTypeSpecRef = 0u;
        var baseInterfaceTypeDefRow = uniqueNominalActual ? 3 : 2;
        var derivedInterfaceTypeDefRow = baseInterfaceTypeDefRow + 1;
        if (includeInterfaceHierarchy)
        {
            baseInterfaceTypeSpecRef = (uint)typeSpecRows.Count + 1;
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x02,
                (uint)baseInterfaceTypeDefRow));
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x02,
                (uint)derivedInterfaceTypeDefRow));
        }

        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            typeDefCount, importCount, genericParamCount, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes,
            typeSpecRows.Count, typeDefCount, importCount,
            genericParamCount, 0, schema);
        var layoutCount = (uniqueNominalActual ? 4 : 2)
            + (includeInterfaceHierarchy ? 2 : 0);
        var interfaceLayoutFirst = uniqueNominalActual ? 5 : 3;
        var typeDefRows = new List<TrRmTypeDefRow>
        {
            new TrRmTypeDefRow(1, 5, 0x26, 1, 1, 1,
                genericParamCount, 1, 2, 0, 0),
        };
        if (uniqueNominalActual)
        {
            typeDefRows.Add(new TrRmTypeDefRow(1, 10, 0x46,
                0, 0, 0, 0, 3, 2, 0, 0));
        }
        if (includeInterfaceHierarchy)
        {
            typeDefRows.Add(new TrRmTypeDefRow(1, baseInterfaceNameRow, 14,
                0, 0, 0, 0, interfaceLayoutFirst, 1, 0, 0));
            typeDefRows.Add(new TrRmTypeDefRow(1, derivedInterfaceNameRow, 14,
                0, 0, 0, 0, interfaceLayoutFirst + 1, 1, 0, 0));
        }

        var typeDefsBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, 1, genericParamCount, layoutCount, 0, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes,
            typeDefRows.Count, strings, modules, 1, genericParamCount,
            layoutCount, 0, schema);
        var fieldsBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 6, fieldTypeSpecRef, 0),
        ], strings, typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldsBytes, 1, strings,
            typeDefs, typeSpecs);
        var constraintCount = uniqueNominalActual ? 0 : 1;
        var formalRows = new List<TrRmGenericParamRow>
        {
            new TrRmGenericParamRow(0x1300_0001, 0, 7,
                constraintCount == 0 ? 0 : 1, constraintCount,
                1, uniqueNominalActual ? (byte)2 : (byte)1, 1, 0, 0),
        };
        if (twoFormals)
        {
            formalRows.Add(new TrRmGenericParamRow(0x1300_0001, 1, 8,
                0, 0, 1, uniqueNominalActual ? (byte)2 : (byte)1,
                1, 0, formalTypeSpecRef));
        }

        var genericParamsBytes = TrRmGenericParamTableWriter.Encode(
            formalRows, strings, typeDefs, default, default, typeSpecs,
            constraintCount);
        var genericParams = new TrRmGenericParamTableReader(
            genericParamsBytes, genericParamCount, strings, typeDefs, default, default,
            typeSpecs, constraintCount);
        var constraintsBytes = constraintCount == 0
            ? []
            : TrRmConstraintTableWriter.Encode([
                new TrRmConstraintRow(1, 2, 3),
            ], genericParams, typeSpecs);
        var targetProfilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1, schema);
        var layoutRows = new List<TrRmLayoutRow>
        {
            new TrRmLayoutRow(1, 0, 1, 0, 0, 16, 1, 1),
            new TrRmLayoutRow(1, 1, 1, 1, explicitAlignment, pack, 2, 1),
        };
        if (uniqueNominalActual)
        {
            layoutRows.Add(new TrRmLayoutRow(2, 0, 1, 0, 0, 16, 0, 0));
            layoutRows.Add(new TrRmLayoutRow(2, 1, 1, 1, 0, 16, 0, 0));
        }
        if (includeInterfaceHierarchy)
        {
            layoutRows.Add(new TrRmLayoutRow(baseInterfaceTypeDefRow,
                0, 0, 0, 0, 0, 0, 0));
            layoutRows.Add(new TrRmLayoutRow(derivedInterfaceTypeDefRow,
                0, 0, 0, 0, 0, 0, 0));
        }

        var layoutsBytes = TrRmLayoutTableWriter.Encode(layoutRows,
            typeDefs, targetProfiles, 2);
        var layouts = new TrRmLayoutTableReader(layoutsBytes,
            layoutRows.Count, typeDefs, targetProfiles, 2);
        var layoutFieldsBytes = TrRmLayoutFieldTableWriter.Encode([
            new TrRmLayoutFieldRow(1, 0, 1),
            new TrRmLayoutFieldRow(2, 0, 1),
        ], layouts, typeDefs, fields);
        var importRows = new List<TrRmImportRow>
        {
            new TrRmImportRow(1, 9, 0x1300_0001),
        };
        if (uniqueNominalActual)
        {
            importRows.Add(new TrRmImportRow(2, 11, 0x1300_0002));
        }

        var importsBytes = TrRmImportTableWriter.Encode(importRows, strings,
            moduleRefs, typeDefs, default, default);
        var fieldSize = uniqueNominalActual ? 8UL : 4UL;
        var fieldAlignment = fieldSize;
        var packLimit = pack == 0 ? 16UL : pack;
        var alignment = Math.Max(Math.Min(fieldAlignment, packLimit), 1UL);
        if (explicitAlignment != 0)
        {
            alignment = explicitAlignment;
        }

        var stride = checked((fieldSize + alignment - 1)
            & ~(alignment - 1));
        var arrayLayoutsBytes = TrRmFixedArrayLayoutTableWriter.Encode([
            new TrRmFixedArrayLayoutRow(1,
                checked((int)arrayTypeSpecRow), 1, stride,
                3 * stride, alignment),
        ], typeSpecs, targetProfiles, features, modules, schema);

        byte[] interfaceDefBytes = [];
        byte[] interfaceBaseBytes = [];
        if (includeInterfaceHierarchy)
        {
            interfaceDefBytes = TrRmInterfaceDefTableWriter.Encode([
                new TrRmInterfaceDefRow(baseInterfaceTypeDefRow,
                    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                new TrRmInterfaceDefRow(derivedInterfaceTypeDefRow,
                    0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0),
            ], typeDefs, genericParams, 1, 0, 0, 0, 0);
            var interfaces = new TrRmInterfaceDefTableReader(interfaceDefBytes,
                2, typeDefs, genericParams, 1, 0, 0, 0, 0);
            interfaceBaseBytes = TrRmInterfaceBaseTableWriter.Encode([
                new TrRmInterfaceBaseRow(2, 0, baseInterfaceTypeSpecRef),
            ], typeSpecs, interfaces);
        }

        var sections = new List<TrRmSection>
        {
            new TrRmSection(0x0001, 0, 1, modulesBytes),
            new TrRmSection(0x0002, 0, (uint)stringRows.Count, stringsBytes),
            new TrRmSection(0x0004, 0, (uint)featureRows.Length,
                featuresBytes),
            new TrRmSection(0x0010, 0, (uint)moduleRefRows.Count,
                moduleRefsBytes),
            new TrRmSection(0x0011, 0, (uint)typeSpecRows.Count,
                typeSpecsBytes),
            new TrRmSection(0x0012, 0, 1, arrayLayoutsBytes),
            new TrRmSection(0x0013, 0, (uint)typeDefRows.Count,
                typeDefsBytes),
            new TrRmSection(0x0014, 0, 1, fieldsBytes),
            new TrRmSection(0x0015, 0, (uint)layoutRows.Count, layoutsBytes),
            new TrRmSection(0x0019, 0, (uint)genericParamCount,
                genericParamsBytes),
            new TrRmSection(0x001C, 0, (uint)importCount, importsBytes),
            new TrRmSection(0x0023, 0, 2, layoutFieldsBytes),
            new TrRmSection(0x0048, 0, 1, targetProfilesBytes),
        };
        if (constraintCount != 0)
        {
            sections.Add(new TrRmSection(0x001A, 0,
                (uint)constraintCount, constraintsBytes));
        }

        if (includeMeteredReaderTables)
        {
            var blobBytes = TrRmVariableTableWriter.EncodeBlobs([
                new byte[] { 1, 2, 3, 4 },
            ]);
            var blobs = new TrRmBlobTableReader(blobBytes, 1);
            var frozenValueNodeBytes
                = TrRmFrozenValueNodeTableWriter.Encode([
                    new TrRmFrozenValueNodeRow(1, 1, 1, 0, 0, 0),
                ], typeSpecs, blobs, 0, 0);
            var constantSourceBytes = TrRmConstantSourceTableWriter.Encode([
                new TrRmConstantSourceRow(1, 1),
            ], 1, 0, 0);
            var dependencyFieldBytes
                = TrRmTargetLayoutDependencyFieldTableWriter.Encode([
                    0x1400_0001,
                ], fields, default);
            var dependencyBytes = TrRmTargetLayoutDependencyTableWriter.Encode([
                new TrRmTargetLayoutDependencyRow(3, 0, 0, 1, 1, 1, 1),
            ], typeSpecs, targetProfiles, 1);
            var fieldPathBytes = TrRmFieldPathTableWriter.Encode([
                new TrRmFieldPathRow(1, [0x1400_0001]),
            ], typeSpecs, fields, default);

            sections.Add(new TrRmSection(0x0003, 0, 1, blobBytes));
            sections.Add(new TrRmSection(0x0030, 0, 1,
                constantSourceBytes));
            sections.Add(new TrRmSection(0x003F, 0, 1,
                frozenValueNodeBytes));
            sections.Add(new TrRmSection(0x0045, 0, 1, dependencyBytes));
            sections.Add(new TrRmSection(0x004A, 0, 1,
                dependencyFieldBytes));
            sections.Add(new TrRmSection(0x004B, 0, 1, fieldPathBytes));
        }

        if (includeInterfaceHierarchy)
        {
            sections.Add(new TrRmSection(0x0025, 0, 2, interfaceDefBytes));
            sections.Add(new TrRmSection(0x0026, 0, 1, interfaceBaseBytes));
        }

        sections.Sort(static (left, right)
            => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static ImmutableArray<TrRmStandardTypeProviderBinding>
        ValidateA3GenericArrayProviderBinding(byte[] consumerArtifact,
            byte[] providerArtifact,
            string providerSchemaIdentity
                = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1")
    {
        const string consumerSchemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1";
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                consumerArtifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                index);
        var modules = ImmutableArray.Create(
            new TrRmStandardModuleSelection(0, 1, "Root", 1, 0, 0, 1, 1),
            new TrRmStandardModuleSelection(1, 1, "Provider", 1, 0, 0, 1, 1));
        return TrRmFixedArrayProviderBindingValidator.Validate(tables,
            [consumerArtifact, providerArtifact],
            [consumerSchemaIdentity, providerSchemaIdentity], modules, 1);
    }

    private static ImmutableArray<TrRmStandardTypeProviderBinding>
        ValidateA3GenericArrayUniqueProviderBinding(byte[] consumerArtifact,
            byte[] genericProviderArtifact, byte[] uniqueProviderArtifact,
            string uniqueProviderSchemaIdentity)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1";
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                consumerArtifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                index);
        var modules = ImmutableArray.Create(
            new TrRmStandardModuleSelection(0, 1, "Root", 1, 0, 0, 1, 1),
            new TrRmStandardModuleSelection(1, 1, "Provider", 1, 0, 0, 1, 1),
            new TrRmStandardModuleSelection(2, 1, "UniqueProvider", 1, 0, 0, 1, 1));
        return TrRmFixedArrayProviderBindingValidator.Validate(tables,
            [consumerArtifact, genericProviderArtifact, uniqueProviderArtifact],
            [schemaIdentity, schemaIdentity, uniqueProviderSchemaIdentity],
            modules, 1);
    }

    private static byte[] CreateA3GenericCopyProviderArtifact(
        TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft,
        byte ownershipDomain = 1, bool storeFormal = true,
        bool declaredCopy = true, bool declaredBlittable = true,
        bool pointerField = false, bool includeBlittableConstraint = true,
        bool twoFormals = false, bool includeSecondFormalDefault = true,
        uint pack = 16, uint explicitAlignment = 0,
        bool prependUnrelatedType = false)
    {
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Provider", 0), ("Cell", 0), ("Value", 0),
            ("T", 0), ("U", 0), ("CellKey", 0), ("Other", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 8);
        var featureBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.RootlessNativeValues,
        ], schema);
        var features = new TrRmFeatureTableReader(featureBytes, 1, schema);
        var typeDefCount = prependUnrelatedType ? 2 : 1;
        var typeDefRow = prependUnrelatedType ? 2 : 1;
        var cellLayoutFirst = prependUnrelatedType ? 3 : 1;
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: 1),
        ], strings, 0, typeDefCount, 0, 0, 1);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            0, typeDefCount, 0, 0, 1);
        var formalCount = twoFormals ? 2 : 1;
        var typeSpecRows = new List<ReadOnlyMemory<byte>>();
        if (storeFormal)
        {
            typeSpecRows.Add(
                TrRmTypeSpecTableWriter.Reference(0x04, 1));
            if (pointerField)
            {
                typeSpecRows.Add(
                    TrRmTypeSpecTableWriter.IndexedFamily(0x8C, 1));
            }
        }
        else
        {
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Builtin(0x0D));
        }
        var fieldTypeSpecRef = pointerField ? 2u : 1u;
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            typeDefCount, 0, formalCount, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes,
            typeSpecRows.Count,
            typeDefCount, 0, formalCount, 0, schema);
        var typeDefRows = new List<TrRmTypeDefRow>();
        if (prependUnrelatedType)
        {
            typeDefRows.Add(new TrRmTypeDefRow(1, 8, 0x06,
                0, 0, 0, 0, 1, 2, 0, 0));
        }
        typeDefRows.Add(new TrRmTypeDefRow(1, 3,
            declaredCopy ? 0x26u : 0x06u,
            1, 1, 1, formalCount, cellLayoutFirst, 2, 0, 0));
        var layoutCount = typeDefCount * 2;
        var typeDefsBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, 1, formalCount, layoutCount, 0, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes,
            typeDefRows.Count, strings, modules, 1, formalCount,
            layoutCount, 0, schema);
        var fieldsBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(typeDefRow, 4, fieldTypeSpecRef, 0),
        ], strings, typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldsBytes, 1, strings,
            typeDefs, typeSpecs);
        var constraintCount = includeBlittableConstraint ? 1 : 0;
        var formalRows = new List<TrRmGenericParamRow>
        {
            new TrRmGenericParamRow(0x1300_0000u | (uint)typeDefRow, 0, 5,
                constraintCount == 0 ? 0 : 1, constraintCount,
                1, ownershipDomain, 1, 0, 0),
        };
        if (twoFormals)
        {
            formalRows.Add(new TrRmGenericParamRow(
                0x1300_0000u | (uint)typeDefRow, 1, 6,
                0, 0, 1, ownershipDomain, 1, 0,
                includeSecondFormalDefault ? 1u : 0u));
        }

        var genericParamsBytes = TrRmGenericParamTableWriter.Encode(
            formalRows, strings, typeDefs, default, default, typeSpecs,
            constraintCount);
        var genericParams = new TrRmGenericParamTableReader(
            genericParamsBytes, formalCount, strings, typeDefs, default, default,
            typeSpecs, constraintCount);
        var constraintsBytes = constraintCount == 0
            ? []
            : TrRmConstraintTableWriter.Encode([
                new TrRmConstraintRow(1, 2, 3),
            ], genericParams, typeSpecs);
        var targetProfilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1, schema);
        var layoutRows = new List<TrRmLayoutRow>();
        if (prependUnrelatedType)
        {
            layoutRows.Add(new TrRmLayoutRow(1, 0, 1, 0, 0, 16, 0, 0));
            layoutRows.Add(new TrRmLayoutRow(1, 1, 1, 1, 0, 16, 0, 0));
        }
        layoutRows.Add(new TrRmLayoutRow(typeDefRow, 0, 1, 0, 0, 16, 1, 1));
        layoutRows.Add(new TrRmLayoutRow(typeDefRow, 1, 1,
            declaredBlittable ? (byte)1 : (byte)0,
            explicitAlignment, pack, 2, 1));
        var layoutsBytes = TrRmLayoutTableWriter.Encode(layoutRows,
            typeDefs, targetProfiles, 2);
        var layouts = new TrRmLayoutTableReader(layoutsBytes,
            layoutRows.Count, typeDefs, targetProfiles, 2);
        var layoutFieldsBytes = TrRmLayoutFieldTableWriter.Encode([
            new TrRmLayoutFieldRow(cellLayoutFirst, 0, 1),
            new TrRmLayoutFieldRow(cellLayoutFirst + 1, 0, 1),
        ], layouts, typeDefs, fields);
        var exportsBytes = TrRmExportTableWriter.Encode([
            new TrRmExportRow(0x1300_0000u | (uint)typeDefRow, 7),
        ], strings, typeDefs, default, default, default);

        var sections = new List<TrRmSection>
        {
            new TrRmSection(0x0001, 0, 1, modulesBytes),
            new TrRmSection(0x0002, 0, 8, stringsBytes),
            new TrRmSection(0x0004, 0, 1, featureBytes),
            new TrRmSection(0x0011, 0, (uint)typeSpecRows.Count,
                typeSpecsBytes),
            new TrRmSection(0x0013, 0, (uint)typeDefCount, typeDefsBytes),
            new TrRmSection(0x0014, 0, 1, fieldsBytes),
            new TrRmSection(0x0015, 0, (uint)layoutRows.Count, layoutsBytes),
            new TrRmSection(0x0019, 0, (uint)formalCount,
                genericParamsBytes),
            new TrRmSection(0x0023, 0, 2, layoutFieldsBytes),
            new TrRmSection(0x0048, 0, 1, targetProfilesBytes),
            new TrRmSection(0x001D, 0, 1, exportsBytes),
        };
        if (constraintCount != 0)
        {
            sections.Add(new TrRmSection(0x001A, 0,
                (uint)constraintCount, constraintsBytes));
        }

        sections.Sort(static (left, right)
            => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static byte[] CreateA3UniqueNominalProviderArtifact(
        TrRmArtifactSchema schema, bool declaredUnique)
    {
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("UniqueProvider", 0), ("UniquePayload", 0),
            ("UniqueKey", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 4);
        var featureBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.RootlessNativeValues,
        ], schema);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: 1),
        ], strings, 0, 1, 0, 0, 1);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            0, 1, 0, 0, 1);
        var typeDefsBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3,
                declaredUnique ? 0x46u : 0x06u,
                0, 0, 0, 0, 1, 2, 0, 0),
        ], strings, modules, 0, 0, 2, 0, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes, 1,
            strings, modules, 0, 0, 2, 0, schema);
        var targetProfilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1, schema);
        var layoutRows = new[]
        {
            new TrRmLayoutRow(1, 0, 1, 0, 0, 16, 0, 0),
            new TrRmLayoutRow(1, 1, 1, 1, 0, 16, 0, 0),
        };
        var layoutsBytes = TrRmLayoutTableWriter.Encode(layoutRows, typeDefs,
            targetProfiles, 0);
        var exportBytes = TrRmExportTableWriter.Encode([
            new TrRmExportRow(0x1300_0001, 4),
        ], strings, typeDefs, default, default, default);

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, modulesBytes),
            new TrRmSection(0x0002, 0, 4, stringsBytes),
            new TrRmSection(0x0004, 0, 1, featureBytes),
            new TrRmSection(0x0013, 0, 1, typeDefsBytes),
            new TrRmSection(0x0015, 0, 2, layoutsBytes),
            new TrRmSection(0x001D, 0, 1, exportBytes),
            new TrRmSection(0x0048, 0, 1, targetProfilesBytes),
        ], schema);
    }

    private static byte[] CreateUniqueCleanupContractArtifact(
        TrRmArtifactSchema schema, bool declaredUnique)
    {
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Resource", 0), ("Cleanup", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 4);
        var featureBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.RootlessNativeValues,
        ], schema);
        var features = new TrRmFeatureTableReader(featureBytes, 1, schema);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: 1),
        ], strings, 0, 1, 1, 0, 1);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            0, 1, 1, 0, 1);
        var typeSpecRows = new ReadOnlyMemory<byte>[]
        {
            TrRmTypeSpecTableWriter.Builtin(0x00),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
        };
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            1, 0, 0, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes, 2,
            1, 0, 0, 0, schema);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3,
                declaredUnique ? 0x46u : 0x06u,
                0, 0, 0, 0, 1, 2, 1, 1),
        ], strings, modules, 0, 0, 2, 1, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, 1,
            strings, modules, 0, 0, 2, 1, schema);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 2, 0, 0, 0, 0),
        ], typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 1,
            typeSpecs, 0, 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(1, 4, 1, 2, 0, 0, 0),
        ], strings, modules, typeDefs, signatures, 1, 0);
        var methods = new TrRmMethodDefTableReader(methodBytes, 1,
            strings, modules, typeDefs, signatures, 1, 0);
        var parameters = default(TrRmParamTableReader);
        var cleanupPayload = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(cleanupPayload, 1);
        var contractsBytes = TrRmContractTableWriter
            .EncodeForeignInvocationDraft([
                new TrRmContractRow(0x1300_0001, 5, cleanupPayload),
            ], typeDefs, methods, signatures, parameters, typeSpecs,
                features, modules, 0, schema);
        var targetProfilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1, schema);
        var layoutsBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 1, 0, 0, 16, 0, 0),
            new TrRmLayoutRow(1, 1, 1, 1, 0, 16, 0, 0),
        ], typeDefs, targetProfiles, 0);

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, modulesBytes),
            new TrRmSection(0x0002, 0, 4, stringsBytes),
            new TrRmSection(0x0004, 0, 1, featureBytes),
            new TrRmSection(0x0011, 0, 2, typeSpecsBytes),
            new TrRmSection(0x0013, 0, 1, typeDefBytes),
            new TrRmSection(0x0015, 0, 2, layoutsBytes),
            new TrRmSection(0x0016, 0, 1, methodBytes),
            new TrRmSection(0x0017, 0, 1, signatureBytes),
            new TrRmSection(0x001B, 0, 1, contractsBytes),
            new TrRmSection(0x0048, 0, 1, targetProfilesBytes),
        ], schema);
    }

    private static (byte[] ConsumerArtifact, byte[] ProviderArtifact)
        CreateA3NestedGenericArrayProviderArtifacts(
            bool providerInnerFieldUsesFormal = true)
        => (CreateA3NestedGenericArrayConsumerArtifact(),
            CreateA3NestedGenericProviderArtifact(
                providerInnerFieldUsesFormal));

    private static byte[] CreateA3NestedGenericArrayConsumerArtifact()
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft;
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Root", 0), ("Provider", 0), ("Cell", 0),
            ("Inner", 0), ("Value", 0), ("T", 0), ("U", 0),
            ("CellKey", 0), ("InnerKey", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 10);
        var featuresBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.RootlessNativeValues,
            TrRmFeatureRow.FixedInlineArrayStorageCandidate,
        ], schema);
        var features = new TrRmFeatureTableReader(featuresBytes, 2, schema);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: 2),
        ], strings, 1, 2, 0, 0, 2);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            1, 2, 0, 0, 2);
        var moduleRefsBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(3, 1, 0),
        ], strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefsBytes, 1,
            strings, modules);
        var typeSpecRows = new ReadOnlyMemory<byte>[]
        {
            TrRmTypeSpecTableWriter.Builtin(0x0D),
            TrRmTypeSpecTableWriter.Reference(0x03, 1),
            TrRmTypeSpecTableWriter.Reference(0x03, 2),
            TrRmTypeSpecTableWriter.Reference(0x04, 1),
            TrRmTypeSpecTableWriter.Reference(0x04, 2),
            TrRmTypeSpecTableWriter.Constructed(3, [4]),
            TrRmTypeSpecTableWriter.Constructed(2, [0xF200_000D]),
            TrRmTypeSpecTableWriter.FixedInlineArray(7, 3),
        };
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            2, 2, 2, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes,
            typeSpecRows.Length, 2, 2, 2, 0, schema);
        var typeDefsBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 4, 0x26, 1, 1, 1, 1, 1, 2, 0, 0),
            new TrRmTypeDefRow(1, 5, 0x26, 2, 1, 2, 1, 3, 2, 0, 0),
        ], strings, modules, 2, 2, 4, 0, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes, 2,
            strings, modules, 2, 2, 4, 0, schema);
        var fieldsBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 6, 6, 0),
            new TrRmFieldDefRow(2, 6, 5, 0),
        ], strings, typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldsBytes, 2, strings,
            typeDefs, typeSpecs);
        var genericRows = new[]
        {
            new TrRmGenericParamRow(0x1300_0001, 0, 7, 1, 1,
                1, 1, 1, 0, 0),
            new TrRmGenericParamRow(0x1300_0002, 0, 8, 2, 1,
                1, 1, 1, 0, 0),
        };
        var genericParamsBytes = TrRmGenericParamTableWriter.Encode(
            genericRows, strings, typeDefs, default, default, typeSpecs, 2);
        var genericParams = new TrRmGenericParamTableReader(
            genericParamsBytes, 2, strings, typeDefs, default, default,
            typeSpecs, 2);
        var constraintsBytes = TrRmConstraintTableWriter.Encode([
            new TrRmConstraintRow(1, 2, 3),
            new TrRmConstraintRow(2, 2, 3),
        ], genericParams, typeSpecs);
        var targetProfilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1, schema);
        var layoutRows = new[]
        {
            new TrRmLayoutRow(1, 0, 1, 0, 0, 16, 1, 1),
            new TrRmLayoutRow(1, 1, 1, 1, 0, 16, 2, 1),
            new TrRmLayoutRow(2, 0, 1, 0, 0, 16, 3, 1),
            new TrRmLayoutRow(2, 1, 1, 1, 0, 16, 4, 1),
        };
        var layoutsBytes = TrRmLayoutTableWriter.Encode(layoutRows,
            typeDefs, targetProfiles, 4);
        var layouts = new TrRmLayoutTableReader(layoutsBytes, 4,
            typeDefs, targetProfiles, 4);
        var layoutFieldsBytes = TrRmLayoutFieldTableWriter.Encode([
            new TrRmLayoutFieldRow(1, 0, 1),
            new TrRmLayoutFieldRow(2, 0, 1),
            new TrRmLayoutFieldRow(3, 0, 2),
            new TrRmLayoutFieldRow(4, 0, 2),
        ], layouts, typeDefs, fields);
        var importsBytes = TrRmImportTableWriter.Encode([
            new TrRmImportRow(1, 9, 0x1300_0001),
            new TrRmImportRow(1, 10, 0x1300_0002),
        ], strings, moduleRefs, typeDefs, default, default);
        var arrayLayoutsBytes = TrRmFixedArrayLayoutTableWriter.Encode([
            new TrRmFixedArrayLayoutRow(1, 8, 1, 4, 12, 4),
        ], typeSpecs, targetProfiles, features, modules, schema);

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, modulesBytes),
            new TrRmSection(0x0002, 0, 10, stringsBytes),
            new TrRmSection(0x0004, 0, 2, featuresBytes),
            new TrRmSection(0x0010, 0, 1, moduleRefsBytes),
            new TrRmSection(0x0011, 0, 8, typeSpecsBytes),
            new TrRmSection(0x0012, 0, 1, arrayLayoutsBytes),
            new TrRmSection(0x0013, 0, 2, typeDefsBytes),
            new TrRmSection(0x0014, 0, 2, fieldsBytes),
            new TrRmSection(0x0015, 0, 4, layoutsBytes),
            new TrRmSection(0x0019, 0, 2, genericParamsBytes),
            new TrRmSection(0x001A, 0, 2, constraintsBytes),
            new TrRmSection(0x001C, 0, 2, importsBytes),
            new TrRmSection(0x0023, 0, 4, layoutFieldsBytes),
            new TrRmSection(0x0048, 0, 1, targetProfilesBytes),
        ], schema);
    }

    private static byte[] CreateA3NestedGenericProviderArtifact(
        bool innerFieldUsesFormal)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft;
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Provider", 0), ("Cell", 0), ("Inner", 0),
            ("Value", 0), ("T", 0), ("U", 0), ("CellKey", 0),
            ("InnerKey", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 9);
        var featuresBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.RootlessNativeValues,
        ], schema);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: 1),
        ], strings, 0, 2, 0, 0, 1);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            0, 2, 0, 0, 1);
        var typeSpecRows = new ReadOnlyMemory<byte>[]
        {
            TrRmTypeSpecTableWriter.Reference(0x04, 1),
            TrRmTypeSpecTableWriter.Reference(0x02, 2),
            TrRmTypeSpecTableWriter.Reference(0x04, 2),
            TrRmTypeSpecTableWriter.Constructed(2, [1]),
            TrRmTypeSpecTableWriter.Builtin(0x0D),
        };
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            2, 0, 2, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes, 5,
            2, 0, 2, 0, schema);
        var typeDefsBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 0x26, 1, 1, 1, 1, 1, 2, 0, 0),
            new TrRmTypeDefRow(1, 4, 0x26, 2, 1, 2, 1, 3, 2, 0, 0),
        ], strings, modules, 2, 2, 4, 0, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes, 2,
            strings, modules, 2, 2, 4, 0, schema);
        var fieldsBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 5, 4, 0),
            new TrRmFieldDefRow(2, 5,
                innerFieldUsesFormal ? 3u : 5u, 0),
        ], strings, typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldsBytes, 2, strings,
            typeDefs, typeSpecs);
        var genericRows = new[]
        {
            new TrRmGenericParamRow(0x1300_0001, 0, 6, 1, 1,
                1, 1, 1, 0, 0),
            new TrRmGenericParamRow(0x1300_0002, 0, 7, 2, 1,
                1, 1, 1, 0, 0),
        };
        var genericParamsBytes = TrRmGenericParamTableWriter.Encode(
            genericRows, strings, typeDefs, default, default, typeSpecs, 2);
        var genericParams = new TrRmGenericParamTableReader(
            genericParamsBytes, 2, strings, typeDefs, default, default,
            typeSpecs, 2);
        var constraintsBytes = TrRmConstraintTableWriter.Encode([
            new TrRmConstraintRow(1, 2, 3),
            new TrRmConstraintRow(2, 2, 3),
        ], genericParams, typeSpecs);
        var targetProfilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1, schema);
        var layoutRows = new[]
        {
            new TrRmLayoutRow(1, 0, 1, 0, 0, 16, 1, 1),
            new TrRmLayoutRow(1, 1, 1, 1, 0, 16, 2, 1),
            new TrRmLayoutRow(2, 0, 1, 0, 0, 16, 3, 1),
            new TrRmLayoutRow(2, 1, 1, 1, 0, 16, 4, 1),
        };
        var layoutsBytes = TrRmLayoutTableWriter.Encode(layoutRows,
            typeDefs, targetProfiles, 4);
        var layouts = new TrRmLayoutTableReader(layoutsBytes, 4,
            typeDefs, targetProfiles, 4);
        var layoutFieldsBytes = TrRmLayoutFieldTableWriter.Encode([
            new TrRmLayoutFieldRow(1, 0, 1),
            new TrRmLayoutFieldRow(2, 0, 1),
            new TrRmLayoutFieldRow(3, 0, 2),
            new TrRmLayoutFieldRow(4, 0, 2),
        ], layouts, typeDefs, fields);
        var exportsBytes = TrRmExportTableWriter.Encode([
            new TrRmExportRow(0x1300_0001, 8),
            new TrRmExportRow(0x1300_0002, 9),
        ], strings, typeDefs, default, default, default);

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, modulesBytes),
            new TrRmSection(0x0002, 0, 9, stringsBytes),
            new TrRmSection(0x0004, 0, 1, featuresBytes),
            new TrRmSection(0x0011, 0, 5, typeSpecsBytes),
            new TrRmSection(0x0013, 0, 2, typeDefsBytes),
            new TrRmSection(0x0014, 0, 2, fieldsBytes),
            new TrRmSection(0x0015, 0, 4, layoutsBytes),
            new TrRmSection(0x0019, 0, 2, genericParamsBytes),
            new TrRmSection(0x001A, 0, 2, constraintsBytes),
            new TrRmSection(0x001D, 0, 2, exportsBytes),
            new TrRmSection(0x0023, 0, 4, layoutFieldsBytes),
            new TrRmSection(0x0048, 0, 1, targetProfilesBytes),
        ], schema);
    }

    private static byte[] CreateCopyProviderArtifact(
        bool prependUnrelatedType = false, uint providerPack = 16,
        uint providerAlignment = 0, byte providerLayoutKind = 1)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        var textRows = prependUnrelatedType
            ? new (string Text, int Encoding)[]
            {
                ("", 0), ("Provider", 0), ("Pair", 0), ("Value", 0),
                ("PairKey", 0), ("Other", 0),
            }
            : [
                ("", 0), ("Provider", 0), ("Pair", 0), ("Value", 0),
                ("PairKey", 0),
            ];
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows(textRows);
        var strings = new TrRmStringTableReader(stringsBytes,
            textRows.Length);
        var featuresBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.RootlessNativeValues,
        ], schema);
        _ = new TrRmFeatureTableReader(featuresBytes, 1, schema);
        var typeDefRows = new List<TrRmTypeDefRow>();
        var fieldRows = new List<TrRmFieldDefRow>();
        var layoutRows = new List<TrRmLayoutRow>();
        var layoutFieldRows = new List<TrRmLayoutFieldRow>();
        if (prependUnrelatedType)
        {
            typeDefRows.Add(new TrRmTypeDefRow(1, 6, 0x05, 1, 1,
                0, 0, 1, 2, 0, 0));
            fieldRows.Add(new TrRmFieldDefRow(1, 4, 1, 0));
            AddTypeLayouts(1, 1, providerPack, 0);
        }

        var pairTypeDefRow = typeDefRows.Count + 1;
        var pairFieldDefRow = fieldRows.Count + 1;
        var pairLayoutFirst = layoutRows.Count + 1;
        typeDefRows.Add(new TrRmTypeDefRow(1, 3, 0x26,
            pairFieldDefRow, 1, 0, 0, pairLayoutFirst, 2, 0, 0));
        fieldRows.Add(new TrRmFieldDefRow(pairTypeDefRow, 4, 1, 0));
        AddTypeLayouts(pairTypeDefRow, pairFieldDefRow, providerPack,
            providerAlignment);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: 1),
        ], strings, 0, typeDefRows.Count, 0, 0, 1);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            0, typeDefRows.Count, 0, 0, 1);
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(0x0D),
        ], typeDefRows.Count, 0, 0, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes, 1,
            typeDefRows.Count, 0, 0, 0, schema);
        var typeDefsBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, fieldRows.Count, 0, layoutRows.Count, 0,
            schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes,
            typeDefRows.Count, strings, modules, fieldRows.Count, 0,
            layoutRows.Count, 0, schema);
        var fieldsBytes = TrRmFieldDefTableWriter.Encode(fieldRows, strings,
            typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldsBytes,
            fieldRows.Count, strings,
            typeDefs, typeSpecs);
        var profilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], schema);
        var profiles = new TrRmTargetProfileTableReader(profilesBytes, 1,
            schema);
        var layoutsBytes = TrRmLayoutTableWriter.Encode(layoutRows, typeDefs,
            profiles, layoutFieldRows.Count);
        var layouts = new TrRmLayoutTableReader(layoutsBytes,
            layoutRows.Count, typeDefs, profiles, layoutFieldRows.Count);
        var layoutFieldsBytes = TrRmLayoutFieldTableWriter.Encode(
            layoutFieldRows, layouts, typeDefs, fields);
        _ = new TrRmLayoutFieldTableReader(layoutFieldsBytes,
            layoutFieldRows.Count, layouts, typeDefs, fields);
        var exportsBytes = TrRmExportTableWriter.Encode([
            new TrRmExportRow(
                0x1300_0000u | (uint)pairTypeDefRow, 5),
        ], strings, typeDefs, default, default, default);

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, modulesBytes),
            new TrRmSection(0x0002, 0, (uint)textRows.Length, stringsBytes),
            new TrRmSection(0x0004, 0, 1, featuresBytes),
            new TrRmSection(0x0011, 0, 1, typeSpecsBytes),
            new TrRmSection(0x0013, 0, (uint)typeDefRows.Count, typeDefsBytes),
            new TrRmSection(0x0014, 0, (uint)fieldRows.Count, fieldsBytes),
            new TrRmSection(0x0015, 0, (uint)layoutRows.Count, layoutsBytes),
            new TrRmSection(0x001D, 0, 1, exportsBytes),
            new TrRmSection(0x0023, 0, (uint)layoutFieldRows.Count,
                layoutFieldsBytes),
            new TrRmSection(0x0048, 0, 1, profilesBytes),
        ], schema);

        void AddTypeLayouts(int typeDefRow, int fieldDefRow, uint pack,
            uint alignment)
        {
            var defaultLayoutRow = layoutRows.Count + 1;
            var defaultFieldRow = layoutFieldRows.Count + 1;
            layoutRows.Add(new TrRmLayoutRow(typeDefRow, 0, providerLayoutKind,
                0, 0, pack,
                defaultFieldRow, 1));
            layoutFieldRows.Add(new TrRmLayoutFieldRow(defaultLayoutRow, 0,
                fieldDefRow));
            var profileLayoutRow = layoutRows.Count + 1;
            var profileFieldRow = layoutFieldRows.Count + 1;
            layoutRows.Add(new TrRmLayoutRow(typeDefRow, 1,
                providerLayoutKind, 1, alignment,
                pack, profileFieldRow, 1));
            layoutFieldRows.Add(new TrRmLayoutFieldRow(profileLayoutRow, 0,
                fieldDefRow));
        }
    }

    private static byte[] CreateChainedArrayProviderArtifact(
        bool providerLeafUsesFloat)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Root", 0), ("Provider", 0), ("LeafProvider", 0),
            ("Pair", 0), ("Leaf", 0), ("Value", 0), ("PairKey", 0),
            ("LeafKey", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 9);
        var featuresBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.RootlessNativeValues,
            TrRmFeatureRow.FixedInlineArrayStorageCandidate,
        ], schema);
        var features = new TrRmFeatureTableReader(featuresBytes, 2, schema);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: 2),
            new TrRmModuleRow(3, 1, 0, 0, 3, 3, 1, 1,
                FeatureFirst: 1, FeatureCount: 1),
            new TrRmModuleRow(4, 1, 0, 0, 4, 5, 1, 1,
                FeatureFirst: 1, FeatureCount: 1),
        ], strings, 3, 5, 0, 0, 2);
        var modules = new TrRmModuleTableReader(modulesBytes, 3, strings,
            3, 5, 0, 0, 2);
        var moduleRefsBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(3, 1, 0),
            new TrRmModuleRefRow(4, 1, 0),
            new TrRmModuleRefRow(4, 1, 0),
        ], strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefsBytes, 3,
            strings, modules);
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(0x0D),
            TrRmTypeSpecTableWriter.Reference(0x03, 1),
            TrRmTypeSpecTableWriter.FixedInlineArray(2, 3),
            TrRmTypeSpecTableWriter.Reference(0x03, 2),
            TrRmTypeSpecTableWriter.Reference(0x03, 3),
            TrRmTypeSpecTableWriter.Builtin(
                providerLeafUsesFloat ? (byte)0x14 : (byte)0x0D),
        ], 5, 3, 0, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes, 6, 5,
            3, 0, 0, schema);
        var typeDefRows = new TrRmTypeDefRow[5];
        for (var index = 0; index < typeDefRows.Length; index++)
        {
            var typeDefRow = index + 1;
            var nameStringRow = typeDefRow is 1 or 3 ? 5 : 6;
            typeDefRows[index] = new TrRmTypeDefRow(1, nameStringRow, 0x26,
                typeDefRow, 1, 0, 0, (index * 2) + 1, 2, 0, 0);
        }
        var typeDefsBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, 5, 0, 10, 0, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes, 5, strings,
            modules, 5, 0, 10, 0, schema);
        var fieldsBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 7, 4, 0),
            new TrRmFieldDefRow(2, 7, 1, 0),
            new TrRmFieldDefRow(3, 7, 5, 0),
            new TrRmFieldDefRow(4, 7,
                providerLeafUsesFloat ? 6u : 1u, 0),
            new TrRmFieldDefRow(5, 7, 1, 0),
        ], strings, typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldsBytes, 5, strings,
            typeDefs, typeSpecs);
        var targetProfilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1, schema);
        var layoutRows = new List<TrRmLayoutRow>(10);
        var layoutFieldRows = new List<TrRmLayoutFieldRow>(10);
        for (var typeDefRow = 1; typeDefRow <= typeDefRows.Length;
            typeDefRow++)
        {
            for (var variant = 0; variant < 2; variant++)
            {
                var layoutRow = layoutRows.Count + 1;
                var layoutField = layoutFieldRows.Count + 1;
                layoutRows.Add(new TrRmLayoutRow(typeDefRow,
                    variant == 0 ? 0u : 1u, 1,
                    variant == 0 ? (byte)0 : (byte)1, 0, 16,
                    layoutField, 1));
                layoutFieldRows.Add(new TrRmLayoutFieldRow(layoutRow, 0,
                    typeDefRow));
            }
        }
        var layoutsBytes = TrRmLayoutTableWriter.Encode(layoutRows, typeDefs,
            targetProfiles, layoutFieldRows.Count);
        var layouts = new TrRmLayoutTableReader(layoutsBytes,
            layoutRows.Count, typeDefs, targetProfiles,
            layoutFieldRows.Count);
        var layoutFieldsBytes = TrRmLayoutFieldTableWriter.Encode(
            layoutFieldRows, layouts, typeDefs, fields);
        var layoutFields = new TrRmLayoutFieldTableReader(
            layoutFieldsBytes, layoutFieldRows.Count, layouts, typeDefs,
            fields);
        var importsBytes = TrRmImportTableWriter.Encode([
            new TrRmImportRow(1, 8, 0x1300_0001),
            new TrRmImportRow(2, 9, 0x1300_0002),
            new TrRmImportRow(3, 9, 0x1300_0004),
        ], strings, moduleRefs, typeDefs, default, default);
        var imports = new TrRmImportTableReader(importsBytes, 3, strings,
            moduleRefs, typeDefs, default, default);
        var exportsBytes = TrRmExportTableWriter.Encode([
            new TrRmExportRow(0x1300_0003, 8),
            new TrRmExportRow(0x1300_0005, 9),
        ], strings, typeDefs, default, default, imports);
        var exports = new TrRmExportTableReader(exportsBytes, 2, strings,
            typeDefs, default, default, imports);
        var fixedArrayLayoutsBytes = TrRmFixedArrayLayoutTableWriter.Encode([
            new TrRmFixedArrayLayoutRow(1, 3, 1, 4, 12, 4),
        ], typeSpecs, targetProfiles, features, modules);
        _ = layoutFields;
        _ = exports;

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 3, modulesBytes),
            new TrRmSection(0x0002, 0, 9, stringsBytes),
            new TrRmSection(0x0004, 0, 2, featuresBytes),
            new TrRmSection(0x0010, 0, 3, moduleRefsBytes),
            new TrRmSection(0x0011, 0, 6, typeSpecsBytes),
            new TrRmSection(0x0012, 0, 1, fixedArrayLayoutsBytes),
            new TrRmSection(0x0013, 0, 5, typeDefsBytes),
            new TrRmSection(0x0014, 0, 5, fieldsBytes),
            new TrRmSection(0x0015, 0, 10, layoutsBytes),
            new TrRmSection(0x001C, 0, 3, importsBytes),
            new TrRmSection(0x001D, 0, 2, exportsBytes),
            new TrRmSection(0x0023, 0, 10, layoutFieldsBytes),
            new TrRmSection(0x0048, 0, 1, targetProfilesBytes),
        ], schema);
    }

    private static ImmutableArray<TrRmStandardTypeProviderBinding>
        ValidateChainedArrayProviderArtifact(byte[] artifact,
            string schemaIdentity)
    {
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));
        var modules = ImmutableArray.Create(
            new TrRmStandardModuleSelection(0, 1, "Root", 1, 0, 0, 1, 1),
            new TrRmStandardModuleSelection(0, 2, "Provider", 1, 0, 0, 1, 1),
            new TrRmStandardModuleSelection(0, 3, "LeafProvider", 1, 0, 0, 1, 1));
        return TrRmFixedArrayProviderBindingValidator.Validate(tables,
            [artifact], [schemaIdentity], modules, 1);
    }

    private static byte[] CreateNestedArrayProviderArtifact(
        bool providerUsesDifferentArray)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Root", 0), ("Provider", 0), ("Pair", 0),
            ("Value", 0), ("PairKey", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 6);
        var featuresBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.RootlessNativeValues,
            TrRmFeatureRow.FixedInlineArrayStorageCandidate,
        ], schema);
        var features = new TrRmFeatureTableReader(featuresBytes, 2, schema);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: 2),
            new TrRmModuleRow(3, 1, 0, 0, 2, 2, 1, 1,
                FeatureFirst: 1, FeatureCount: 2),
        ], strings, 1, 2, 0, 0, 2);
        var modules = new TrRmModuleTableReader(modulesBytes, 2, strings,
            1, 2, 0, 0, 2);
        var moduleRefsBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(3, 1, 0),
        ], strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefsBytes, 1,
            strings, modules);
        var typeSpecRows = providerUsesDifferentArray
            ? new ReadOnlyMemory<byte>[]
            {
                TrRmTypeSpecTableWriter.Builtin(0x0D),
                TrRmTypeSpecTableWriter.FixedInlineArray(1, 2),
                TrRmTypeSpecTableWriter.Reference(0x03, 1),
                TrRmTypeSpecTableWriter.FixedInlineArray(3, 3),
                TrRmTypeSpecTableWriter.Builtin(0x0C),
                TrRmTypeSpecTableWriter.FixedInlineArray(5, 4),
            }
            : [
                TrRmTypeSpecTableWriter.Builtin(0x0D),
                TrRmTypeSpecTableWriter.FixedInlineArray(1, 2),
                TrRmTypeSpecTableWriter.Reference(0x03, 1),
                TrRmTypeSpecTableWriter.FixedInlineArray(3, 3),
            ];
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            2, 1, 0, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes,
            typeSpecRows.Length, 2, 1, 0, 0, schema);
        var typeDefsBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 4, 0x26, 1, 1, 0, 0, 1, 2, 0, 0),
            new TrRmTypeDefRow(1, 4, 0x26, 2, 1, 0, 0, 3, 2, 0, 0),
        ], strings, modules, 2, 0, 4, 0, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes, 2, strings,
            modules, 2, 0, 4, 0, schema);
        var fieldsBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 5, 2, 0),
            new TrRmFieldDefRow(2, 5,
                providerUsesDifferentArray ? 6u : 2u, 0),
        ], strings, typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldsBytes, 2, strings,
            typeDefs, typeSpecs);
        var targetProfilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1, schema);
        var layoutsBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 1, 0, 0, 16, 1, 1),
            new TrRmLayoutRow(1, 1, 1, 1, 4, 16, 2, 1),
            new TrRmLayoutRow(2, 0, 1, 0, 0, 16, 3, 1),
            new TrRmLayoutRow(2, 1, 1, 1, 4, 16, 4, 1),
        ], typeDefs, targetProfiles, 4);
        var layouts = new TrRmLayoutTableReader(layoutsBytes, 4, typeDefs,
            targetProfiles, 4);
        var layoutFieldsBytes = TrRmLayoutFieldTableWriter.Encode([
            new TrRmLayoutFieldRow(1, 0, 1),
            new TrRmLayoutFieldRow(2, 0, 1),
            new TrRmLayoutFieldRow(3, 0, 2),
            new TrRmLayoutFieldRow(4, 0, 2),
        ], layouts, typeDefs, fields);
        var layoutFields = new TrRmLayoutFieldTableReader(
            layoutFieldsBytes, 4, layouts, typeDefs, fields);
        var importsBytes = TrRmImportTableWriter.Encode([
            new TrRmImportRow(1, 6, 0x1300_0001),
        ], strings, moduleRefs, typeDefs, default, default);
        var imports = new TrRmImportTableReader(importsBytes, 1, strings,
            moduleRefs, typeDefs, default, default);
        var exportsBytes = TrRmExportTableWriter.Encode([
            new TrRmExportRow(0x1300_0002, 6),
        ], strings, typeDefs, default, default, imports);
        var exports = new TrRmExportTableReader(exportsBytes, 1, strings,
            typeDefs, default, default, imports);
        var fixedArrayLayoutRows = new List<TrRmFixedArrayLayoutRow> {
            new(1, 2, 1, 4, 8, 4),
            new(1, 4, 1, 8, 24, 4),
            providerUsesDifferentArray
                ? new TrRmFixedArrayLayoutRow(2, 6, 1, 2, 8, 2)
                : new TrRmFixedArrayLayoutRow(2, 2, 1, 4, 8, 4),
        };
        var fixedArrayLayoutsBytes = TrRmFixedArrayLayoutTableWriter.Encode(
            fixedArrayLayoutRows, typeSpecs, targetProfiles, features,
            modules);
        _ = layoutFields;
        _ = exports;

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 2, modulesBytes),
            new TrRmSection(0x0002, 0, 6, stringsBytes),
            new TrRmSection(0x0004, 0, 2, featuresBytes),
            new TrRmSection(0x0010, 0, 1, moduleRefsBytes),
            new TrRmSection(0x0011, 0, (uint)typeSpecRows.Length,
                typeSpecsBytes),
            new TrRmSection(0x0012, 0,
                (uint)fixedArrayLayoutRows.Count, fixedArrayLayoutsBytes),
            new TrRmSection(0x0013, 0, 2, typeDefsBytes),
            new TrRmSection(0x0014, 0, 2, fieldsBytes),
            new TrRmSection(0x0015, 0, 4, layoutsBytes),
            new TrRmSection(0x001C, 0, 1, importsBytes),
            new TrRmSection(0x001D, 0, 1, exportsBytes),
            new TrRmSection(0x0023, 0, 4, layoutFieldsBytes),
            new TrRmSection(0x0048, 0, 1, targetProfilesBytes),
        ], schema);
    }

    private static ImmutableArray<TrRmStandardTypeProviderBinding>
        ValidateNestedArrayProviderArtifact(byte[] artifact,
            string schemaIdentity)
    {
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));
        var modules = ImmutableArray.Create(
            new TrRmStandardModuleSelection(0, 1, "Root", 1, 0, 0, 1, 1),
            new TrRmStandardModuleSelection(0, 2, "Provider", 1, 0, 0, 1, 1));
        return TrRmFixedArrayProviderBindingValidator.Validate(tables,
            [artifact], [schemaIdentity], modules, 1);
    }

    private static byte[] CreateFunctionPointerArrayProviderArtifact(
        bool providerUsesUInt64)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Root", 0), ("Provider", 0), ("Pair", 0),
            ("Pointer", 0), ("PairKey", 0), ("RootAnchor", 0),
            ("ProviderAnchor", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 8);
        var featuresBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.NativeBoundaryContracts,
            TrRmFeatureRow.RootlessNativeValues,
            TrRmFeatureRow.FixedInlineArrayStorageCandidate,
        ], schema);
        var features = new TrRmFeatureTableReader(featuresBytes, 3, schema);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: 3),
            new TrRmModuleRow(3, 1, 0, 0, 2, 2, 2, 1,
                FeatureFirst: 1, FeatureCount: 2),
        ], strings, 1, 2, 2, 0, 3);
        var modules = new TrRmModuleTableReader(modulesBytes, 2, strings,
            1, 2, 2, 0, 3);
        var moduleRefsBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(3, 1, 0),
        ], strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefsBytes, 1,
            strings, modules);
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.NativeFunctionPointer(1, 1),
            TrRmTypeSpecTableWriter.Reference(0x03, 1),
            TrRmTypeSpecTableWriter.FixedInlineArray(2, 3),
            TrRmTypeSpecTableWriter.NativeFunctionPointer(1, 2),
        ], 2, 1, 0, 0, schema, methodDefCount: 2);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes, 4, 2,
            1, 0, 0, schema, methodDefCount: 2);
        var typeDefsBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 4, 0x26, 1, 1, 0, 0, 1, 2, 0, 0),
            new TrRmTypeDefRow(1, 4, 0x26, 2, 1, 0, 0, 3, 2, 0, 0),
        ], strings, modules, 2, 0, 4, 0, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes, 2, strings,
            modules, 2, 0, 4, 0, schema);
        var fieldsBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 5, 1, 0),
            new TrRmFieldDefRow(2, 5, 4, 0),
        ], strings, typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldsBytes, 2, strings,
            typeDefs, typeSpecs);
        var signaturesBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_000D, 0, 1, 1, 0, 0),
            new TrRmMethodSigRow(0xF200_000D, 0, 2, 1, 0, 0),
        ], typeSpecs, 2, 0);
        var signatures = new TrRmMethodSigTableReader(signaturesBytes, 2,
            typeSpecs, 2, 0);
        var providerParameterType = providerUsesUInt64
            ? 0xF200_000E : 0xF200_000D;
        var parametersBytes = TrRmParamTableWriter.Encode([
            new TrRmParamRow(1, 0, 0xF200_000D, 0),
            new TrRmParamRow(2, 0, providerParameterType, 0),
        ], signatures, typeSpecs, schema);
        _ = new TrRmParamTableReader(parametersBytes, 2,
            signatures, typeSpecs, schema);
        var methodDefsBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 7, 1, 0, 0, 0, 0),
            new TrRmMethodDefRow(0, 8, 2, 0, 0, 0, 0),
        ], strings, modules, typeDefs, signatures, 0, 0);
        _ = new TrRmMethodDefTableReader(methodDefsBytes, 2,
            strings, modules, typeDefs, signatures, 0, 0);
        var targetProfilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1, schema);
        var layoutsBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 1, 0, 0, 16, 1, 1),
            new TrRmLayoutRow(1, 1, 1, 1, 0, 16, 2, 1),
            new TrRmLayoutRow(2, 0, 1, 0, 0, 16, 3, 1),
            new TrRmLayoutRow(2, 1, 1, 1, 0, 16, 4, 1),
        ], typeDefs, targetProfiles, 4);
        var layouts = new TrRmLayoutTableReader(layoutsBytes, 4, typeDefs,
            targetProfiles, 4);
        var layoutFieldsBytes = TrRmLayoutFieldTableWriter.Encode([
            new TrRmLayoutFieldRow(1, 0, 1),
            new TrRmLayoutFieldRow(2, 0, 1),
            new TrRmLayoutFieldRow(3, 0, 2),
            new TrRmLayoutFieldRow(4, 0, 2),
        ], layouts, typeDefs, fields);
        var layoutFields = new TrRmLayoutFieldTableReader(
            layoutFieldsBytes, 4, layouts, typeDefs, fields);
        var importsBytes = TrRmImportTableWriter.Encode([
            new TrRmImportRow(1, 6, 0x1300_0001),
        ], strings, moduleRefs, typeDefs, default, default);
        var imports = new TrRmImportTableReader(importsBytes, 1, strings,
            moduleRefs, typeDefs, default, default);
        var exportsBytes = TrRmExportTableWriter.Encode([
            new TrRmExportRow(0x1300_0002, 6),
        ], strings, typeDefs, default, default, imports);
        var exports = new TrRmExportTableReader(exportsBytes, 1, strings,
            typeDefs, default, default, imports);
        var fixedArrayLayoutsBytes = TrRmFixedArrayLayoutTableWriter.Encode([
            new TrRmFixedArrayLayoutRow(1, 3, 1, 8, 24, 8),
        ], typeSpecs, targetProfiles, features, modules);
        _ = layoutFields;
        _ = exports;

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 2, modulesBytes),
            new TrRmSection(0x0002, 0, 8, stringsBytes),
            new TrRmSection(0x0004, 0, 3, featuresBytes),
            new TrRmSection(0x0010, 0, 1, moduleRefsBytes),
            new TrRmSection(0x0011, 0, 4, typeSpecsBytes),
            new TrRmSection(0x0012, 0, 1, fixedArrayLayoutsBytes),
            new TrRmSection(0x0013, 0, 2, typeDefsBytes),
            new TrRmSection(0x0014, 0, 2, fieldsBytes),
            new TrRmSection(0x0015, 0, 4, layoutsBytes),
            new TrRmSection(0x0016, 0, 2, methodDefsBytes),
            new TrRmSection(0x0017, 0, 2, signaturesBytes),
            new TrRmSection(0x0018, 0, 2, parametersBytes),
            new TrRmSection(0x001C, 0, 1, importsBytes),
            new TrRmSection(0x001D, 0, 1, exportsBytes),
            new TrRmSection(0x0023, 0, 4, layoutFieldsBytes),
            new TrRmSection(0x0048, 0, 1, targetProfilesBytes),
        ], schema);
    }

    private static ImmutableArray<TrRmStandardTypeProviderBinding>
        ValidateFunctionPointerArrayProviderArtifact(byte[] artifact,
            string schemaIdentity)
    {
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));
        var modules = ImmutableArray.Create(
            new TrRmStandardModuleSelection(0, 1, "Root", 1, 0, 0, 1, 2),
            new TrRmStandardModuleSelection(0, 2, "Provider", 1, 0, 0, 2, 4));
        return TrRmFixedArrayProviderBindingValidator.Validate(tables,
            [artifact], [schemaIdentity], modules, 1);
    }

    private static void ValidateImportedArrayProvider(byte[] artifact)
    {
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));
        var modules = ImmutableArray.Create(
            new TrRmStandardModuleSelection(0, 1, "Root", 1, 0, 0, 1, 1),
            new TrRmStandardModuleSelection(0, 2, "Provider", 1, 0, 0, 1, 1));

        _ = TrRmFixedArrayProviderBindingValidator.Validate(
            tables, [artifact],
            ["trrm/1-draft-foreign-invocation-storage-fixed-arrays-2"],
            modules, 1);
    }

    private static void ValidateExternalImportedArrayProvider(
        byte[] providerArtifact)
    {
        var consumerArtifact = CreateImportedArrayConsumerArtifact();
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(
                        consumerArtifact));
        var modules = ImmutableArray.Create(
            new TrRmStandardModuleSelection(0, 1, "Root", 1, 0, 0, 1, 1),
            new TrRmStandardModuleSelection(1, 1, "Provider", 1, 0, 0, 1, 1));

        _ = TrRmFixedArrayProviderBindingValidator.Validate(
            tables, [consumerArtifact, providerArtifact],
            [
                "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2",
                "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2",
            ], modules, 1);
    }

    private static byte[] CreateA3NestedGenericArrayArtifact(
        bool nestedConstructedActual = false)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft;
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Cell", 0), ("Outer", 0),
            ("value", 0), ("inner", 0), ("T", 0), ("U", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 8);
        var featuresBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.RootlessNativeValues,
            TrRmFeatureRow.FixedInlineArrayStorageCandidate,
        ], schema);
        var features = new TrRmFeatureTableReader(featuresBytes, 2, schema);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: 2),
        ], strings, 0, 2, 0, 0, 2);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            0, 2, 0, 0, 2);
        var typeSpecRows = new ReadOnlyMemory<byte>[]
        {
            TrRmTypeSpecTableWriter.Builtin(0x0D),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Reference(0x02, 2),
            TrRmTypeSpecTableWriter.Reference(0x04, 1),
            TrRmTypeSpecTableWriter.Reference(0x04, 2),
            TrRmTypeSpecTableWriter.Constructed(2, [5]),
            TrRmTypeSpecTableWriter.Constructed(2, [1]),
            TrRmTypeSpecTableWriter.Constructed(3,
                [nestedConstructedActual ? 7u : 1u]),
            TrRmTypeSpecTableWriter.FixedInlineArray(8, 3),
        };
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            2, 0, 2, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes, 9, 2, 0,
            2, 0, schema);
        var typeDefsBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 0x26, 1, 1, 1, 1, 1, 2, 0, 0),
            new TrRmTypeDefRow(1, 4, 0x26, 2, 1, 2, 1, 3, 2, 0, 0),
        ], strings, modules, 2, 2, 4, 0, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes, 2, strings,
            modules, 2, 2, 4, 0, schema);
        var fieldsBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 5, 4, 0),
            new TrRmFieldDefRow(2, 6, 6, 0),
        ], strings, typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldsBytes, 2, strings,
            typeDefs, typeSpecs);
        var genericParamsBytes = TrRmGenericParamTableWriter.Encode([
            new TrRmGenericParamRow(0x1300_0001, 0, 7, 1, 1,
                1, 1, 1, 0, 0),
            new TrRmGenericParamRow(0x1300_0002, 0, 8, 2, 1,
                1, 1, 1, 0, 0),
        ], strings, typeDefs, default, default, typeSpecs, 2);
        var genericParams = new TrRmGenericParamTableReader(
            genericParamsBytes, 2, strings, typeDefs, default, default,
            typeSpecs, 2);
        var constraintsBytes = TrRmConstraintTableWriter.Encode([
            new TrRmConstraintRow(1, 2, 3),
            new TrRmConstraintRow(2, 2, 3),
        ], genericParams, typeSpecs);
        var targetProfilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1, schema);
        var layoutsBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 1, 1),
            new TrRmLayoutRow(1, 1, 1, 1, 0, 16, 2, 1),
            new TrRmLayoutRow(2, 0, 0, 0, 0, 0, 3, 1),
            new TrRmLayoutRow(2, 1, 1, 1, 0, 16, 4, 1),
        ], typeDefs, targetProfiles, 4);
        var layouts = new TrRmLayoutTableReader(layoutsBytes, 4,
            typeDefs, targetProfiles, 4);
        var layoutFieldsBytes = TrRmLayoutFieldTableWriter.Encode([
            new TrRmLayoutFieldRow(1, 0, 1),
            new TrRmLayoutFieldRow(2, 0, 1),
            new TrRmLayoutFieldRow(3, 0, 2),
            new TrRmLayoutFieldRow(4, 0, 2),
        ], layouts, typeDefs, fields);
        var fixedArrayLayoutsBytes = TrRmFixedArrayLayoutTableWriter.Encode([
            new TrRmFixedArrayLayoutRow(1, 9, 1, 4, 12, 4),
        ], typeSpecs, targetProfiles, features, modules, schema);

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, modulesBytes),
            new TrRmSection(0x0002, 0, 8, stringsBytes),
            new TrRmSection(0x0004, 0, 2, featuresBytes),
            new TrRmSection(0x0011, 0, 9, typeSpecsBytes),
            new TrRmSection(0x0012, 0, 1, fixedArrayLayoutsBytes),
            new TrRmSection(0x0013, 0, 2, typeDefsBytes),
            new TrRmSection(0x0014, 0, 2, fieldsBytes),
            new TrRmSection(0x0015, 0, 4, layoutsBytes),
            new TrRmSection(0x0019, 0, 2, genericParamsBytes),
            new TrRmSection(0x001A, 0, 2, constraintsBytes),
            new TrRmSection(0x0023, 0, 4, layoutFieldsBytes),
            new TrRmSection(0x0048, 0, 1, targetProfilesBytes),
        ], schema);
    }

    private static byte[] CreateA3GenericDeclarationArtifact(
        byte formalKind = 1, ushort constraintKind = 2,
        uint constraintPayload = 3, uint secondDefaultReference = 3,
        bool declaredCopy = true, bool declaredBlittable = true,
        bool includeArray = false, byte ownershipDomain = 1,
        bool includeBlittableConstraint = true,
        bool storeFormal = true, bool pointerField = false,
        bool uniqueActual = false, byte nullabilityDomain = 1,
        bool nullableActual = false, bool boxedActual = false,
        bool openArrayElement = false, bool writablePointerActual = false,
        bool writableValueActual = false, bool spanInitActual = false,
        bool indexedWritablePointerActual = false,
        bool gcPointerActual = false, bool uniqueNominalActual = false,
        bool unclassifiedNominalActual = false,
        bool conflictingNominalCopyUniqueFlags = false,
        bool omitRootlessFeature = false, bool recursiveField = false,
        bool transparentLayout = false, bool voidActual = false,
        bool closedArrayField = false, ulong arrayCount = 3,
        ulong? arrayExtentOverride = null, ulong nestedArrayCount = 2,
        ulong? nestedArrayExtentOverride = null, uint pack = 16,
        uint explicitAlignment = 0, bool secondField = false,
        bool growingRecursiveField = false)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft;
        var typeDefCount = uniqueNominalActual || unclassifiedNominalActual
            ? 2 : 1;
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Cell", 0), ("value", 0),
            ("T", 0), ("U", 0), ("UniquePayload", 0), ("tail", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 8);
        var featureRows = new List<TrRmFeatureRow>();
        if (!omitRootlessFeature)
        {
            featureRows.Add(TrRmFeatureRow.RootlessNativeValues);
            featureRows.Add(TrRmFeatureRow.FixedInlineArrayStorageCandidate);
        }
        else if (includeArray || closedArrayField)
        {
            featureRows.Add(TrRmFeatureRow.FixedInlineArrayStorageCandidate);
        }
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var features = new TrRmFeatureTableReader(featureBytes,
            featureRows.Count, schema);
        var featureFirst = featureRows.Count == 0 ? 0 : 1;
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: featureFirst,
                FeatureCount: featureRows.Count),
        ], strings, 0, typeDefCount, 0, 0, featureRows.Count);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            0, typeDefCount, 0, 0, featureRows.Count);
        var formalCount = formalKind == 2 ? 1 : 2;
        var typeSpecRows = new List<ReadOnlyMemory<byte>>
        {
            TrRmTypeSpecTableWriter.Builtin(voidActual ? (byte)0x00 : (byte)0x0D),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
        };
        uint arrayTypeSpecRow = 0;
        uint recursiveFieldTypeSpecRef = 0;
        uint closedArrayFieldTypeSpecRef = 0;
        uint nominalActualTypeSpecRef = 0;
        if (formalKind == 1)
        {
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x04, 1));
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x04, 2));
            if (typeDefCount == 2)
            {
                typeSpecRows.Add(
                    TrRmTypeSpecTableWriter.Reference(0x02, 2));
                nominalActualTypeSpecRef = (uint)typeSpecRows.Count;
            }

            uint spanInitActualTypeSpecRef = 0;
            if (spanInitActual)
            {
                typeSpecRows.Add(
                    TrRmTypeSpecTableWriter.Builtin(0x0D, 0x04));
                typeSpecRows.Add(TrRmTypeSpecTableWriter.IndexedFamily(
                    0x8A, (uint)typeSpecRows.Count));
                spanInitActualTypeSpecRef = (uint)typeSpecRows.Count;
            }
            else if (indexedWritablePointerActual)
            {
                typeSpecRows.Add(TrRmTypeSpecTableWriter.IndexedFamily(
                    0x8C, 0xF200_000D, 1));
            }
            else if (gcPointerActual)
            {
                typeSpecRows.Add(
                    TrRmTypeSpecTableWriter.Wrapped(0x08,
                        0xF200_000D, 2));
            }

            var actualType = uniqueActual || nullableActual
                || boxedActual ? 7u : 1u;
            if (spanInitActual)
            {
                actualType = spanInitActualTypeSpecRef;
            }
            else if (indexedWritablePointerActual || gcPointerActual)
            {
                actualType = (uint)typeSpecRows.Count;
            }
            else if (writablePointerActual)
            {
                actualType = 0xF301_8C0D;
            }
            else if (writableValueActual)
            {
                actualType = 0xF301_8E0D;
            }
            else if (typeDefCount == 2)
            {
                actualType = nominalActualTypeSpecRef;
            }

            if (recursiveField)
            {
                recursiveFieldTypeSpecRef = (uint)typeSpecRows.Count + 1;
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Constructed(2,
                    formalCount == 1 ? [3u] : [3u, 4u]));
            }
            else if (growingRecursiveField)
            {
                var innerConstructedTypeSpecRef
                    = (uint)typeSpecRows.Count + 1;
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Constructed(2,
                    formalCount == 1 ? [3u] : [3u, 4u]));
                recursiveFieldTypeSpecRef = (uint)typeSpecRows.Count + 1;
                typeSpecRows.Add(TrRmTypeSpecTableWriter.Constructed(2,
                    formalCount == 1
                        ? [innerConstructedTypeSpecRef]
                        : [innerConstructedTypeSpecRef, 4u]));
            }

            if (closedArrayField)
            {
                closedArrayFieldTypeSpecRef = (uint)typeSpecRows.Count + 1;
                typeSpecRows.Add(
                    TrRmTypeSpecTableWriter.FixedInlineArray(1,
                        nestedArrayCount));
            }

            var constructedTypeSpecRow = (uint)typeSpecRows.Count + 1;
            typeSpecRows.Add(includeArray
                ? TrRmTypeSpecTableWriter.Constructed(2,
                    openArrayElement ? [3u, 4u]
                        : [actualType, actualType])
                : TrRmTypeSpecTableWriter.Constructed(2, [3, 4]));
            if (includeArray)
            {
                arrayTypeSpecRow = (uint)typeSpecRows.Count + 1;
                typeSpecRows.Add(
                    TrRmTypeSpecTableWriter.FixedInlineArray(
                        constructedTypeSpecRow, arrayCount));
                if (uniqueActual)
                {
                    typeSpecRows.Add(
                        TrRmTypeSpecTableWriter.InlineFamily(0x8F, 0x0D));
                }
                else if (nullableActual)
                {
                    typeSpecRows.Add(
                        TrRmTypeSpecTableWriter.Wrapped(0x06, 1));
                }
                else if (boxedActual)
                {
                    typeSpecRows.Add(
                        TrRmTypeSpecTableWriter.InlineFamily(0x8D, 0x0D));
                }
                if (pointerField)
                {
                    typeSpecRows.Add(
                        TrRmTypeSpecTableWriter.IndexedFamily(0x8C, 3));
                }
            }
        }
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            typeDefCount, 0, formalCount, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes,
            typeSpecRows.Count, typeDefCount, 0, formalCount, 0, schema);
        var fieldCount = secondField ? 2 : 1;
        var typeDefRows = new List<TrRmTypeDefRow>
        {
            new TrRmTypeDefRow(1, 3,
                declaredCopy ? 0x26u : 0x06u,
                1, fieldCount, 1, formalCount, 1, 2, 0, 0),
        };
        if (typeDefCount == 2)
        {
            typeDefRows.Add(new TrRmTypeDefRow(1, 7,
                0x06u | (uniqueNominalActual ? 0x40u : 0)
                    | (conflictingNominalCopyUniqueFlags ? 0x20u : 0),
                0, 0, 0, 0, 3, 2, 0, 0));
        }

        var typeDefsBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, fieldCount,
            formalCount, typeDefCount == 2 ? 4 : 2, 0, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes,
            typeDefRows.Count, strings, modules, fieldCount, formalCount,
            typeDefCount == 2 ? 4 : 2, 0, schema);
        var fieldRows = new List<TrRmFieldDefRow>
        {
            new(1, 4,
                recursiveField || growingRecursiveField
                    ? recursiveFieldTypeSpecRef
                    : closedArrayField ? closedArrayFieldTypeSpecRef
                    : !storeFormal || formalKind == 2 ? 1u
                    : pointerField ? (uint)typeSpecRows.Count : 3u, 0),
        };
        if (secondField)
        {
            fieldRows.Add(new TrRmFieldDefRow(1, 8, 1, 0));
        }

        var fieldsBytes = TrRmFieldDefTableWriter.Encode(fieldRows, strings,
            typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldsBytes, fieldRows.Count,
            strings, typeDefs, typeSpecs);
        var signatures = default(TrRmMethodSigTableReader);
        var methods = default(TrRmMethodDefTableReader);
        var constraintsForType = formalKind == 2
            || !includeBlittableConstraint ? 0 : 1;
        var genericParamRows = new List<TrRmGenericParamRow>
        {
            new TrRmGenericParamRow(0x1300_0001, 0, 5,
                constraintsForType == 0 ? 0 : 1, constraintsForType,
                formalKind,
                formalKind == 1 ? ownershipDomain : (byte)0,
                formalKind == 1 ? nullabilityDomain : (byte)0,
                formalKind == 2 ? 1u : 0u, 0),
        };
        if (formalCount == 2)
        {
            genericParamRows.Add(new TrRmGenericParamRow(
                0x1300_0001, 1, 6, 0, 0, 1,
                ownershipDomain, nullabilityDomain, 0,
                secondDefaultReference));
        }

        var genericParamsBytes = TrRmGenericParamTableWriter.Encode(
            genericParamRows, strings, typeDefs, methods, signatures, typeSpecs,
            constraintsForType);
        var genericParams = new TrRmGenericParamTableReader(
            genericParamsBytes, formalCount, strings, typeDefs, methods,
            signatures, typeSpecs, constraintsForType);
        var constraintBytes = constraintsForType == 0
            ? []
            : TrRmConstraintTableWriter.Encode([
                new TrRmConstraintRow(1, constraintKind,
                    constraintPayload),
            ], genericParams, typeSpecs);
        var targetProfilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1, schema);
        var layoutRows = new List<TrRmLayoutRow>
        {
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 1, fieldCount),
            new TrRmLayoutRow(1, 1,
                transparentLayout ? (byte)3 : (byte)1,
                declaredBlittable ? (byte)1 : (byte)0,
                transparentLayout ? 0 : explicitAlignment,
                transparentLayout ? 0 : pack, fieldCount + 1,
                fieldCount),
        };
        if (typeDefCount == 2)
        {
            layoutRows.Add(new TrRmLayoutRow(2, 0, 0, 0, 0, 0, 0, 0));
            layoutRows.Add(new TrRmLayoutRow(2, 1, 1, 1, 0, 16, 0, 0));
        }

        var layoutFieldRows = new List<TrRmLayoutFieldRow>();
        for (var ordinal = 0; ordinal < fieldCount; ordinal++)
        {
            layoutFieldRows.Add(new TrRmLayoutFieldRow(1, ordinal,
                ordinal + 1));
        }
        for (var ordinal = 0; ordinal < fieldCount; ordinal++)
        {
            layoutFieldRows.Add(new TrRmLayoutFieldRow(2, ordinal,
                ordinal + 1));
        }

        var layoutBytes = TrRmLayoutTableWriter.Encode(layoutRows,
            typeDefs, targetProfiles, layoutFieldRows.Count);
        var layouts = new TrRmLayoutTableReader(layoutBytes,
            layoutRows.Count, typeDefs, targetProfiles,
            layoutFieldRows.Count);
        var layoutFieldsBytes = TrRmLayoutFieldTableWriter.Encode(
            layoutFieldRows, layouts, typeDefs, fields);
        var pointerValue = (pointerField || writablePointerActual
            || indexedWritablePointerActual) && storeFormal;
        var fieldSize = closedArrayField
            ? nestedArrayExtentOverride
                ?? checked(nestedArrayCount * sizeof(uint))
            : pointerValue ? sizeof(ulong)
                : voidActual && storeFormal ? 0UL : sizeof(uint);
        var fieldAlignment = closedArrayField ? sizeof(uint)
            : pointerValue ? sizeof(ulong)
                : fieldSize == 0 ? 0UL : sizeof(uint);
        var layoutSize = fieldSize;
        var layoutAlignment = fieldAlignment;
        if (!transparentLayout)
        {
            if (fieldSize == 0)
            {
                layoutSize = 1;
                layoutAlignment = 1;
            }
            else
            {
                var packLimit = pack == 0 ? 16UL : pack;
                layoutAlignment = Math.Max(Math.Min(fieldAlignment,
                    packLimit), 1UL);
                if (explicitAlignment != 0)
                {
                    layoutAlignment = explicitAlignment;
                }

                layoutSize = checked((fieldSize + layoutAlignment - 1)
                    & ~(layoutAlignment - 1));
            }
        }

        var elementAlignment = Math.Max(1UL, layoutAlignment);
        var elementStride = checked((layoutSize + elementAlignment - 1)
            & ~(elementAlignment - 1));
        var arrayLayoutRows = new List<TrRmFixedArrayLayoutRow>();
        if (closedArrayField)
        {
            arrayLayoutRows.Add(new TrRmFixedArrayLayoutRow(1,
                checked((int)closedArrayFieldTypeSpecRef), 1, sizeof(uint),
                nestedArrayExtentOverride
                    ?? checked(nestedArrayCount * sizeof(uint)),
                sizeof(uint)));
        }
        if (includeArray)
        {
            arrayLayoutRows.Add(new TrRmFixedArrayLayoutRow(1,
                checked((int)arrayTypeSpecRow), 1, elementStride,
                arrayExtentOverride
                    ?? checked(arrayCount * elementStride),
                elementAlignment));
        }

        var arrayLayoutBytes = arrayLayoutRows.Count == 0 ? []
            : TrRmFixedArrayLayoutTableWriter.Encode(arrayLayoutRows,
                typeSpecs, targetProfiles, features, modules, schema);

        var sections = new List<TrRmSection>
        {
            new TrRmSection(0x0001, 0, 1, modulesBytes),
            new TrRmSection(0x0002, 0, 8, stringsBytes),
            new TrRmSection(0x0011, 0, (uint)typeSpecRows.Count,
                typeSpecsBytes),
            new TrRmSection(0x0013, 0, (uint)typeDefRows.Count,
                typeDefsBytes),
            new TrRmSection(0x0014, 0, (uint)fieldCount, fieldsBytes),
            new TrRmSection(0x0015, 0, (uint)layoutRows.Count, layoutBytes),
            new TrRmSection(0x0019, 0, (uint)formalCount,
                genericParamsBytes),
            new TrRmSection(0x0023, 0, (uint)layoutFieldRows.Count,
                layoutFieldsBytes),
            new TrRmSection(0x0048, 0, 1, targetProfilesBytes),
        };
        if (featureRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0004, 0,
                (uint)featureRows.Count, featureBytes));
        }
        if (arrayLayoutRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0012, 0,
                (uint)arrayLayoutRows.Count,
                arrayLayoutBytes));
        }
        if (constraintsForType != 0)
        {
            sections.Add(new TrRmSection(0x001A, 0,
                (uint)constraintsForType, constraintBytes));
        }

        sections.Sort(static (left, right)
            => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, schema);
    }

    internal static byte[] CreateA3NestedClosedArrayExtentOverflowArtifact()
        => CreateA3GenericDeclarationArtifact(includeArray: true,
            closedArrayField: true,
            nestedArrayCount: ulong.MaxValue / sizeof(uint),
            arrayCount: 1, secondField: true, pack: 1);

    internal static byte[] CreateA3GrowingRecursiveLayoutArtifact()
        => CreateA3GenericDeclarationArtifact(includeArray: true,
            growingRecursiveField: true);

    internal static byte[] CreateA3DirectCycleArtifact()
        => CreateA3GenericDeclarationArtifact(includeArray: true,
            recursiveField: true);

    internal static byte[] CreateA3UnsupportedConstraintArtifact()
        => CreateA3GenericDeclarationArtifact(constraintPayload: 1);

    private static byte[] CreateAggregateArrayArtifact(bool declaredCopy,
        ulong stride, ulong extent, ulong alignment,
        bool includePlaceSelectionBody = false, uint targetLayoutRow = 1,
        bool registerIndex = false, uint pack = 16,
        uint explicitAlignment = 0, bool pointerElement = false,
        byte pointerQualifiers = 0, bool declaredUniqueFlag = false,
        byte layoutKind = 1)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Pair", 0), ("Low", 0), ("High", 0),
            ("Main", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 6);
        var featuresBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.FixedInlineArrayStorageCandidate,
        ], schema);
        var features = new TrRmFeatureTableReader(featuresBytes, 1, schema);
        var methodCount = includePlaceSelectionBody ? 1 : 0;
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                    FeatureFirst: 1, FeatureCount: 1),
        ], strings, 0, 1, methodCount, 0, 1);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            0, 1, methodCount, 0, 1);
        ReadOnlyMemory<byte>[] typeSpecRows = pointerElement
            ? [
                TrRmTypeSpecTableWriter.Builtin(0x0B),
                TrRmTypeSpecTableWriter.Builtin(0x0D),
                TrRmTypeSpecTableWriter.Reference(0x02, 1),
                TrRmTypeSpecTableWriter.Wrapped(0x08, 3,
                    pointerQualifiers),
                TrRmTypeSpecTableWriter.FixedInlineArray(4, 2),
            ]
            : [
                TrRmTypeSpecTableWriter.Builtin(0x0B),
                TrRmTypeSpecTableWriter.Builtin(0x0D),
                TrRmTypeSpecTableWriter.Reference(0x02, 1),
                TrRmTypeSpecTableWriter.FixedInlineArray(3, 2),
            ];
        var arrayTypeSpecRow = pointerElement ? 5 : 4;
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            1, 0, 0, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes,
            typeSpecRows.Length, 1, 0, 0, 0, schema);
        var typeDefsBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3,
                    (declaredCopy ? 0x26u : 0x06u)
                        | (declaredUniqueFlag ? 0x40u : 0),
                    1, 2, 0, 0, 1, 2, 0, 0),
        ], strings, modules, 2, 0, 2, 0, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes, 1,
            strings, modules, 2, 0, 2, 0, schema);
        var fieldBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 4, 1, 0),
            new TrRmFieldDefRow(1, 5, 2, 0),
        ], strings, typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldBytes, 2, strings,
            typeDefs, typeSpecs);
        var targetProfilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1, schema);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 1, 0, 0, 16, 1, 2),
            new TrRmLayoutRow(1, 1, layoutKind, 1, explicitAlignment, pack,
                3, 2),
        ], typeDefs, targetProfiles, 4);
        var layouts = new TrRmLayoutTableReader(layoutBytes, 2,
            typeDefs, targetProfiles, 4);
        var layoutFieldBytes = TrRmLayoutFieldTableWriter.Encode([
            new TrRmLayoutFieldRow(1, 0, 1),
            new TrRmLayoutFieldRow(1, 1, 2),
            new TrRmLayoutFieldRow(2, 0, 1),
            new TrRmLayoutFieldRow(2, 1, 2),
        ], layouts, typeDefs, fields);
        var layoutFields = new TrRmLayoutFieldTableReader(layoutFieldBytes, 4,
            layouts, typeDefs, fields);
        var arrayLayoutBytes = TrRmFixedArrayLayoutTableWriter.Encode([
            new TrRmFixedArrayLayoutRow(1, arrayTypeSpecRow, 1, stride,
                extent, alignment),
        ], typeSpecs, targetProfiles, features, modules);
        byte[]? methodSignatureBytes = null;
        byte[]? methodDefinitionBytes = null;
        byte[]? bodyBytes = null;
        if (includePlaceSelectionBody)
        {
            methodSignatureBytes = TrRmMethodSigTableWriter.Encode([
                new TrRmMethodSigRow(0xF200_0000, 0, 0, 0, 0, 0),
            ], typeSpecs, 0, 0);
            var signatures = new TrRmMethodSigTableReader(
                methodSignatureBytes, 1, typeSpecs, 0, 0);
            methodDefinitionBytes = TrRmMethodDefTableWriter.Encode([
                new TrRmMethodDefRow(0, 6, 1, 0, 0, 0, 1),
            ], strings, modules, typeDefs, signatures, 0, 1);
            var homeMap = new byte[]
            {
                3, 1, 0, 0, 0, 0, 0, 0,
                3, 0, 1, 0, 0, 0, 0, 0,
            };
            var typeMap = new byte[3 * sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(typeMap, 0x1100_0004);
            BinaryPrimitives.WriteUInt32LittleEndian(
                typeMap.AsSpan(sizeof(uint)), 0xF200_000D);
            BinaryPrimitives.WriteUInt32LittleEndian(
                typeMap.AsSpan(2 * sizeof(uint)), 0x1100_0003);
            var selection = TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                targetKind: 3, accessKind: 1, destinationKind: 1, baseKind: 2,
                destinationIndex: 0, baseIndex: 0,
                indexKind: registerIndex ? (byte)1 : (byte)2,
                targetRefKind: 3, indexIndex: 1, targetRef: targetLayoutRow,
                referentTypeMapIndex: 2, schema);
            byte[] code = registerIndex
                ? [0xD5, 0x01, 0x01, 0, 0xD8, 0, 1, 0, 0xB5, 0]
                : [0xD8, 0, 1, 0, 0xB5, 0];
            bodyBytes = TrRmRegisterBodyWriter.Encode(homeMap, typeMap,
                default, default, default,
                [new TrRmRegisterBodyBlock(0, (uint)code.Length, 0)],
                [
                    new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
                    selection,
                ],
                code,
                schema: schema);
        }

        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 1, modulesBytes),
            new(0x0002, 0, 6, stringsBytes),
            new(0x0004, 0, 1, featuresBytes),
            new(0x0011, 0, (uint)typeSpecRows.Length, typeSpecsBytes),
            new(0x0012, 0, 1, arrayLayoutBytes),
            new(0x0013, 0, 1, typeDefsBytes),
            new(0x0014, 0, 2, fieldBytes),
            new(0x0015, 0, 2, layoutBytes),
            new(0x0023, 0, 4, layoutFieldBytes),
            new(0x0048, 0, 1, targetProfilesBytes),
        };
        if (includePlaceSelectionBody)
        {
            sections.Add(new TrRmSection(0x0016, 0, 1,
                methodDefinitionBytes!));
            sections.Add(new TrRmSection(0x0017, 0, 1,
                methodSignatureBytes!));
            sections.Add(new TrRmSection(0x0020, 0, 1,
                TrRmVariableTableWriter.EncodeBodies([bodyBytes!])));
        }
        sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static void ValidateBodyMaps(byte[] artifact)
    {
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));
        tables.ValidateRegisterBodyMapsForMethod(1);
    }

    private static void ValidateA2ProviderBinding(byte[] consumerArtifact,
        byte[] providerArtifact, string schemaIdentity,
        ImmutableArray<TrRmStandardModuleSelection> modules)
    {
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(
                        consumerArtifact));
        _ = TrRmFixedArrayProviderBindingValidator.Validate(
            tables, [consumerArtifact, providerArtifact],
            [schemaIdentity, schemaIdentity], modules, 1);
    }

    private static byte[] CreateNestedArrayPlaceArtifact(
        ulong requiredElementCount = 1,
        ulong selectedArrayElementCount = 2,
        bool zeroRequiredElementCount = false,
        bool crossBlockPlaceSelection = false,
        bool crossBlockRegisterIndex = false,
        bool branchJoinPlaceSelection = false,
        bool loopPlaceSelection = false,
        bool missingBranchPlaceHandle = false,
        bool mismatchedCrossBlockIndexType = false,
        byte alternateFirstSelectionAccessKind = 1)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Main", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 3);
        var featuresBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.FixedInlineArrayStorageCandidate,
        ], schema);
        var features = new TrRmFeatureTableReader(featuresBytes, 1, schema);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: 1),
        ], strings, 0, 0, 1, 0, 1);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            0, 0, 1, 0, 1);
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.FixedInlineArray(0xF200_000D,
                selectedArrayElementCount),
            TrRmTypeSpecTableWriter.FixedInlineArray(1, 3),
            TrRmTypeSpecTableWriter.InlineFamily(0x8C, 0x0D),
        ], 0, 0, 0, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes, 3, 0, 0,
            0, 0, schema);
        var targetProfilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1, schema);
        var selectedArrayByteExtent = checked(selectedArrayElementCount
            * sizeof(uint));
        var outerArrayByteExtent = checked(selectedArrayByteExtent * 3);
        var fixedArrayLayoutsBytes = TrRmFixedArrayLayoutTableWriter.Encode([
            new TrRmFixedArrayLayoutRow(1, 1, 1, sizeof(uint),
                selectedArrayByteExtent, sizeof(uint)),
            new TrRmFixedArrayLayoutRow(1, 2, 1,
                selectedArrayByteExtent, outerArrayByteExtent, sizeof(uint)),
        ], typeSpecs, targetProfiles, features, modules);
        var hasControlFlowParameter = crossBlockRegisterIndex
            || branchJoinPlaceSelection || loopPlaceSelection;
        var controlFlowParameterType = crossBlockRegisterIndex
            ? mismatchedCrossBlockIndexType
                ? 0xF200_0001u
                : 0xF200_000Du
            : 0xF200_0001u;
        var methodSignatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_0000, 0,
                hasControlFlowParameter ? 1 : 0,
                hasControlFlowParameter ? 1 : 0, 0, 0),
        ], typeSpecs, hasControlFlowParameter ? 1 : 0, 0);
        var signatures = new TrRmMethodSigTableReader(methodSignatureBytes,
            1, typeSpecs, hasControlFlowParameter ? 1 : 0, 0);
        var parameterBytes = hasControlFlowParameter
            ? TrRmParamTableWriter.Encode([
                new TrRmParamRow(1, 0, controlFlowParameterType, 0),
            ], signatures, typeSpecs, schema)
            : [];
        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, 0, schema);
        var methodDefinitionBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 3, 1, 0, 0, 0, 1),
        ], strings, modules, typeDefs, signatures, 0, 1);

        var homeMap = branchJoinPlaceSelection || loopPlaceSelection
            ? new byte[]
            {
                3, 1, 0, 0, 0, 0, 0, 0,
                3, 0, 2, 0, 0, 0, 0, 0,
                3, 0, 2, 0, 0, 0, 0, 0,
                3, 1, 0, 0, 0, 0, 0, 0,
                3, 0, 2, 0, 0, 0, 0, 0,
            }
            : [
                3, 1, 0, 0, 0, 0, 0, 0,
                3, 0, 2, 0, 0, 0, 0, 0,
                3, 0, 2, 0, 0, 0, 0, 0,
            ];
        var typeMap = new byte[4 * sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(typeMap, 0x1100_0002);
        BinaryPrimitives.WriteUInt32LittleEndian(
            typeMap.AsSpan(sizeof(uint)), 0x1100_0001);
        BinaryPrimitives.WriteUInt32LittleEndian(
            typeMap.AsSpan(2 * sizeof(uint)), 0xF200_000D);
        BinaryPrimitives.WriteUInt32LittleEndian(
            typeMap.AsSpan(3 * sizeof(uint)), 0x1100_0003);
        var firstSelection
            = TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                targetKind: 3, accessKind: 1, destinationKind: 1, baseKind: 2,
                destinationIndex: 0, baseIndex: 0, indexKind: 2,
                targetRefKind: 3, indexIndex: 1, targetRef: 2,
                referentTypeMapIndex: 1, schema);
        var alternateFirstSelection
            = TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                targetKind: 3, accessKind: alternateFirstSelectionAccessKind,
                destinationKind: 1, baseKind: 2,
                destinationIndex: 0, baseIndex: 3, indexKind: 2,
                targetRefKind: 3, indexIndex: 4, targetRef: 2,
                referentTypeMapIndex: 1, schema);
        var secondSelection
            = TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                targetKind: 3, accessKind: 1, destinationKind: 1, baseKind: 1,
                destinationIndex: 1, baseIndex: 0, indexKind: 2,
                targetRefKind: 3, indexIndex: 2, targetRef: 1,
                referentTypeMapIndex: 2, schema);
        var registerIndexSelection
            = TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                targetKind: 3, accessKind: 1, destinationKind: 1, baseKind: 2,
                destinationIndex: 0, baseIndex: 0, indexKind: 1,
                targetRefKind: 3, indexIndex: 1, targetRef: 2,
                referentTypeMapIndex: 1, schema);
        var address = TrRmRegisterBodyRecordWriter.EncodeStorageAddress(
            sourceKind: 1, resultKind: 1, sourceIndex: 1, resultIndex: 2,
            pointerTypeMapIndex: 3,
            requiredElementCount: checked((uint)(zeroRequiredElementCount
                ? 1UL : requiredElementCount)));
        TrRmRegisterBodyBlock[] blocks;
        ReadOnlyMemory<byte>[] records;
        byte[] code;
        if (crossBlockRegisterIndex)
        {
            blocks = [
                new TrRmRegisterBodyBlock(0, 4, 0),
                new TrRmRegisterBodyBlock(4, 6, 1),
            ];
            records = [
                new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
                new byte[] { 1, 0, 0, 0, 1, 0, 0, 0,
                    1, 0, 2, 0, 0 },
                registerIndexSelection,
            ];
            code = [
                0x11, 0x01,
                0xB0, 1,
                0xD8, 0, 2, 0,
                0xB5, 0,
            ];
        }
        else if (loopPlaceSelection)
        {
            blocks = [
                new TrRmRegisterBodyBlock(0, 6, 0),
                new TrRmRegisterBodyBlock(6, 4, 1),
                new TrRmRegisterBodyBlock(10, 8, 2),
                new TrRmRegisterBodyBlock(18, 6, 3),
                new TrRmRegisterBodyBlock(24, 10, 4),
            ];
            records = [
                new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
                new byte[] { 1, 0, 0, 0, 1, 0, 0, 0,
                    0, 1, 1, 0, 1 },
                new byte[] { 1, 0, 0, 0, 1, 0, 0, 0,
                    0, 1, 1, 0, 1 },
                new byte[] { 1, 0, 0, 0, 1, 0, 0, 0,
                    0, 1, 1, 0, 1 },
                new byte[] { 1, 0, 0, 0, 1, 0, 0, 0,
                    0, 1, 1, 0, 1 },
                new byte[] { 6, 0, 0, 0, 2, 0, 4, 0 },
                new byte[] { 6, 0, 0, 0, 3, 0, 1, 0 },
                firstSelection,
                alternateFirstSelection,
                secondSelection,
                address,
            ];
            code = [
                0xD8, 0, 7, 0,
                0xB0, 1,
                0x11, 2,
                0xB1, 0x52,
                0xD8, 0, 7, 0,
                0x11, 2,
                0xB1, 0x62,
                0xD8, 0, 8, 0,
                0xB0, 1,
                0xD8, 1, 9, 0,
                0xEC, 0, 10, 0,
                0xB5, 0,
            ];
        }
        else if (branchJoinPlaceSelection)
        {
            blocks = missingBranchPlaceHandle
                ? [
                    new TrRmRegisterBodyBlock(0, 4, 0),
                    new TrRmRegisterBodyBlock(4, 2, 1),
                    new TrRmRegisterBodyBlock(6, 6, 2),
                    new TrRmRegisterBodyBlock(12, 10, 3),
                ]
                : [
                    new TrRmRegisterBodyBlock(0, 4, 0),
                    new TrRmRegisterBodyBlock(4, 6, 1),
                    new TrRmRegisterBodyBlock(10, 6, 2),
                    new TrRmRegisterBodyBlock(16, 10, 3),
                ];
            records = [
                new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
                new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
                new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
                new byte[] { 1, 0, 0, 0, 1, 0, 0, 0,
                    0, 1, 1, 0, 1 },
                new byte[] { 6, 0, 0, 0, 1, 0, 2, 0 },
                firstSelection,
                alternateFirstSelection,
                secondSelection,
                address,
            ];
            code = missingBranchPlaceHandle
                ? [
                    0x11, 0,
                    0xB1, 0x40,
                    0xB0, 3,
                    0xD8, 0, 6, 0,
                    0xB0, 3,
                    0xD8, 1, 7, 0,
                    0xEC, 0, 8, 0,
                    0xB5, 0,
                ]
                : [
                    0x11, 0,
                    0xB1, 0x40,
                    0xD8, 0, 5, 0,
                    0xB0, 3,
                    0xD8, 0, 6, 0,
                    0xB0, 3,
                    0xD8, 1, 7, 0,
                    0xEC, 0, 8, 0,
                    0xB5, 0,
                ];
        }
        else if (crossBlockPlaceSelection)
        {
            blocks = [
                new TrRmRegisterBodyBlock(0, 6, 0),
                new TrRmRegisterBodyBlock(6, 10, 1),
            ];
            records = [
                new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
                new byte[] { 1, 0, 0, 0, 1, 0, 0, 0,
                    0, 1, 1, 0, 1 },
                firstSelection,
                secondSelection,
                address,
            ];
            code = [
                0xD8, 0, 2, 0,
                0xB0, 1,
                0xD8, 1, 3, 0,
                0xEC, 0, 4, 0,
                0xB5, 0,
            ];
        }
        else
        {
            blocks = [new TrRmRegisterBodyBlock(0, 14, 0)];
            records = [
                new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
                firstSelection,
                secondSelection,
                address,
            ];
            code = [
                0xD8, 0, 1, 0,
                0xD8, 1, 2, 0,
                0xEC, 0, 3, 0,
                0xB5, 0,
            ];
        }
        var bodyBytes = TrRmRegisterBodyWriter.Encode(homeMap, typeMap,
            default, default, default,
            blocks, records, code,
            schema: schema);
        if (zeroRequiredElementCount)
        {
            var addressRecordOffset = bodyBytes.AsSpan().IndexOf(address);
            if (addressRecordOffset < 0)
            {
                throw new InvalidOperationException(
                    "The encoded Body must retain its storage-address record.");
            }

            bodyBytes.AsSpan(addressRecordOffset + 12, sizeof(uint)).Clear();
        }

        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 1, modulesBytes),
            new(0x0002, 0, 3, stringsBytes),
            new(0x0004, 0, 1, featuresBytes),
            new(0x0011, 0, 3, typeSpecsBytes),
            new(0x0012, 0, 2, fixedArrayLayoutsBytes),
            new(0x0016, 0, 1, methodDefinitionBytes),
            new(0x0017, 0, 1, methodSignatureBytes),
            new(0x0020, 0, 1,
                TrRmVariableTableWriter.EncodeBodies([bodyBytes])),
            new(0x0048, 0, 1, targetProfilesBytes),
        };
        if (parameterBytes.Length != 0)
        {
            sections.Add(new TrRmSection(0x0018, 0, 1, parameterBytes));
        }
        sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static byte[] CreateCopyTypeArtifact(bool includeRootlessFeature)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("CopyValue", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 3);
        TrRmFeatureRow[] featureRows = includeRootlessFeature
            ? [TrRmFeatureRow.RootlessNativeValues]
            : [];
        var featuresBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var featureCount = featureRows.Length;
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: featureCount == 0 ? 0 : 1,
                FeatureCount: featureCount),
        ], strings, 0, 1, 0, 0, featureCount);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            0, 1, 0, 0, featureCount);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 0x26, 0, 0, 0, 0, 1, 1, 0, 0),
        ], strings, modules, 0, 0, 1, 0, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, 1, strings,
            modules, 0, 0, 1, 0, schema);
        var layoutsBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
        ], typeDefs, default, 0);

        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 1, modulesBytes),
            new(0x0002, 0, 3, stringsBytes),
            new(0x0013, 0, 1, typeDefBytes),
            new(0x0015, 0, 1, layoutsBytes),
        };
        if (featureCount != 0)
        {
            sections.Add(new TrRmSection(0x0004, 0,
                checked((uint)featureCount), featuresBytes));
        }

        sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static byte[] CreateTransparentVoidArrayArtifact()
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Empty", 0), ("VoidField", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 4);
        var featuresBytes = TrRmFeatureTableWriter.Encode([
            TrRmFeatureRow.FixedInlineArrayStorageCandidate,
        ], schema);
        var features = new TrRmFeatureTableReader(featuresBytes, 1, schema);
        var modulesBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1,
                FeatureFirst: 1, FeatureCount: 1),
        ], strings, 0, 1, 0, 0, 1);
        var modules = new TrRmModuleTableReader(modulesBytes, 1, strings,
            0, 1, 0, 0, 1);
        var typeSpecsBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.FixedInlineArray(1, ulong.MaxValue),
            TrRmTypeSpecTableWriter.FixedInlineArray(0xF200_0000,
                ulong.MaxValue),
        ], 1, 0, 0, 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecsBytes, 3, 1, 0,
            0, 0, schema);
        var typeDefsBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 0x26, 1, 1, 0, 0, 1, 2, 0, 0),
        ], strings, modules, 1, 0, 2, 0, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefsBytes, 1,
            strings, modules, 1, 0, 2, 0, schema);
        var fieldBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 4, 0xF200_0000, 0),
        ], strings, typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldBytes, 1, strings,
            typeDefs, typeSpecs);
        var targetProfilesBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ], schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfilesBytes, 1, schema);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 3, 0, 0, 0, 1, 1),
            new TrRmLayoutRow(1, 1, 3, 1, 0, 0, 2, 1),
        ], typeDefs, targetProfiles, 2);
        var layouts = new TrRmLayoutTableReader(layoutBytes, 2, typeDefs,
            targetProfiles, 2);
        var layoutFieldBytes = TrRmLayoutFieldTableWriter.Encode([
            new TrRmLayoutFieldRow(1, 0, 1),
            new TrRmLayoutFieldRow(2, 0, 1),
        ], layouts, typeDefs, fields);
        var layoutFields = new TrRmLayoutFieldTableReader(layoutFieldBytes, 2,
            layouts, typeDefs, fields);
        var fixedArrayLayoutsBytes = TrRmFixedArrayLayoutTableWriter.Encode([
            new TrRmFixedArrayLayoutRow(1, 2, 1, 0, 0, 1),
            new TrRmFixedArrayLayoutRow(1, 3, 1, 0, 0, 1),
        ], typeSpecs, targetProfiles, features, modules);

        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 1, modulesBytes),
            new(0x0002, 0, 4, stringsBytes),
            new(0x0004, 0, 1, featuresBytes),
            new(0x0011, 0, 3, typeSpecsBytes),
            new(0x0012, 0, 2, fixedArrayLayoutsBytes),
            new(0x0013, 0, 1, typeDefsBytes),
            new(0x0014, 0, 1, fieldBytes),
            new(0x0015, 0, 2, layoutBytes),
            new(0x0023, 0, 2, layoutFieldBytes),
            new(0x0048, 0, 1, targetProfilesBytes),
        };
        sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, schema);
    }
}
