using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmEnumTablesTests
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EnumReturn();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EnumIdentity(int value);

    [Test]
    public void RoundTripsNominalEnumAndDuplicateValuedMembers()
    {
        var fixture = CreateFixture();
        var (Flags, MemberCount, Values) = Validate(fixture);

        Assert.That(Flags, Is.EqualTo(1));
        Assert.That(MemberCount, Is.EqualTo(2));
        Assert.That(Values, Is.EqualTo([7, 7]));
    }

    [Test]
    public void AcceptsAnEmptyEnumWithoutAnEnumMemberSection()
    {
        var (Flags, MemberCount, Values) = Validate(CreateFixture(includeMembers: false, blittable: false));

        Assert.That(Flags, Is.EqualTo(1));
        Assert.That(MemberCount, Is.Zero);
        Assert.That(Values, Is.Empty);
    }

    [Test]
    public void RejectsWrongUnderlyingTypeAndReservedEnumFlags()
    {
        var fixture = CreateFixture();
        _ = Assert.Throws<InvalidDataException>(() => Validate(fixture with {
            EnumBytes = WriteUInt32(fixture.EnumBytes, 4, 0xF200_0007),
        }));
        _ = Assert.Throws<InvalidDataException>(() => Validate(fixture with {
            EnumBytes = WriteUInt32(fixture.EnumBytes, 8, 2),
        }));
    }

    [Test]
    public void RejectsDuplicateMemberKeysAndOrphanMemberRows()
    {
        var fixture = CreateFixture();
        _ = Assert.Throws<InvalidDataException>(() => Validate(fixture with {
            MemberBytes = WriteUInt32(fixture.MemberBytes, 20 + 8, 6),
        }));
        _ = Assert.Throws<InvalidDataException>(() => Validate(fixture with {
            EnumBytes = WriteUInt32(fixture.EnumBytes, 16, 1),
        }));
    }

    [Test]
    public void RejectsBlittableLayoutVariantsForAnotherTargetProfile()
    {
        var fixture = CreateFixture();
        var otherProfile = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(otherProfile, 2);
        BinaryPrimitives.WriteUInt32LittleEndian(otherProfile.AsSpan(4), 1);
        _ = Assert.Throws<InvalidDataException>(() => Validate(fixture with {
            TargetProfileBytes = otherProfile,
        }));
    }

    [Test]
    public void StructuralProfileRequiresEnumFeatureAndIsSelectedOutOfBand()
    {
        var artifact = CreateArtifact([
            TrRmFeatureRow.NativeBoundaryContracts,
            new TrRmFeatureRow(18, 1),
        ]);
        var (HasTypedFrozenValues, HasNominalUnionMetadata) = ValidateCandidateArtifact(artifact);
        Assert.That(HasTypedFrozenValues, Is.True);
        Assert.That(HasNominalUnionMetadata, Is.False);
        _ = Assert.Throws<InvalidDataException>(() => ValidateDefaultArtifact(artifact));
        _ = Assert.Throws<InvalidDataException>(() => ValidateCandidateArtifact(
            CreateArtifact([
                TrRmFeatureRow.NativeBoundaryContracts,
                TrRmFeatureRow.TypedFrozenValues,
            ])));
    }

    [Test]
    public void BindsImportedEnumToExactProviderSnapshot()
    {
        var consumer = CreateBoundEnumArtifact(isConsumer: true);
        var provider = CreateBoundEnumArtifact(isConsumer: false);
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftProviderBindings(
                    consumer, [provider]));

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftProviderBindings(
                    consumer, [CreateBoundEnumArtifact(
                        isConsumer: false, memberValue: 8)]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftProviderBindings(
                    consumer, [CreateBoundEnumArtifact(
                        isConsumer: false, declarationKey: "different-key")]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftProviderBindings(
                    consumer, [CreateBoundEnumArtifact(
                        isConsumer: false, providerExportKey: "other")]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftProviderBindings(
                    consumer, [CreateBoundEnumArtifact(
                        isConsumer: false, providerModuleIdentity: "other")]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftProviderBindings(
                    consumer, [CreateBoundEnumArtifact(
                        isConsumer: false, providerTargetRequirement: 1)]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftProviderBindings(
                    consumer, [CreateBoundEnumArtifact(
                        isConsumer: false, flags: 0)]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftProviderBindings(
                    consumer, [CreateBoundEnumArtifact(
                        isConsumer: false, blittable: false)]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftProviderBindings(
                    consumer, []));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftProviderBindings(
                    consumer, [provider, provider]));
    }

    [Test]
    public void EmitsOwnedNominalEnumDeclarationFromBoundProgram()
    {
        var enumIdentity = new CoreDeclarationIdentity(
            "enum-module", "Color", CoreDeclarationKind.EnumType);
        var member = new BoundEnumMemberDefinition(
            new BoundEnumMemberIdentity.Core(new CoreDeclarationIdentity(
                "enum-module", "color-red",
                CoreDeclarationKind.EnumMember)),
            new BoundEnumDeclarationIdentity.Core(enumIdentity), "Red", 7,
            true, "color-red");
        var definition = new BoundEnumDefinition(
            new BoundEnumDeclarationIdentity.Core(enumIdentity),
            "Example.Color",
            PrimitiveType.Int32, [member], true, true, false)
        {
            BlittableTargetRequirement
                = FixedLayoutStorage.WindowsX64TargetRequirement,
        };
        var program = new BoundProgram([], -1)
        {
            EnumDefinitions = [definition],
            RuntimePolicy = BoundRuntimePolicy.FailFast,
        };

        var artifact = TrRmExecutableCompiler
            .EmitNominalEnumsDraftDeclarationLibrary(
                program, "enum-module", moduleTargetRequirement: 1);
        Assert.That(TrRmExecutableCompiler.EmitStandard(program, "enum-module",
                "trrm/1-draft-nominal-enums-1", moduleTargetRequirement: 1),
            Is.EqualTo(artifact));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, "enum-module",
                "trrm/1-draft-nominal-enums-1", moduleTargetRequirement: 0));
        var tables = TrRmStructuralMetadataValidator
            .ValidateNominalEnumsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForNominalEnumsDraft(artifact));
        var providerDefinitionExported
            = tables.Exports.TryGetDefinitionForKey(1, "Color", out var localDefId);

        Assert.That(tables.TypeDefs.Count, Is.EqualTo(1));
        Assert.That(tables.EnumDefs.Count, Is.EqualTo(1));
        Assert.That(tables.EnumMembers.Count, Is.EqualTo(1));
        Assert.That(tables.Modules.GetRequirements(1).TargetRequirement,
            Is.EqualTo(1u));
        Assert.That(providerDefinitionExported, Is.True);
        Assert.That(localDefId, Is.EqualTo(0x1300_0001u));
        Assert.That(tables.EnumMembers.GetDeclarationKey(1), Is.EqualTo("color-red"));
        Assert.That(tables.EnumMembers.GetValue(1), Is.EqualTo(7));
    }

    [Test]
    public void EmitsConstantReturnWithNominalEnumFrozenValue()
    {
        var enumIdentity = new CoreDeclarationIdentity(
            "enum-module", "Color", CoreDeclarationKind.EnumType);
        var member = new BoundEnumMemberDefinition(
            new BoundEnumMemberIdentity.Core(new CoreDeclarationIdentity(
                "enum-module", "color-red",
                CoreDeclarationKind.EnumMember)),
            new BoundEnumDeclarationIdentity.Core(enumIdentity), "Red", 7,
            true, "color-red");
        var definition = new BoundEnumDefinition(
            new BoundEnumDeclarationIdentity.Core(enumIdentity),
            "Example.Color",
            PrimitiveType.Int32, [member], true, true, false)
        {
            BlittableTargetRequirement
                = FixedLayoutStorage.WindowsX64TargetRequirement,
        };
        var enumType = BoundEnumTypes.Type(0);
        var constant = new BoundConstant(
            ScalarBits.FromPayload(PrimitiveType.Int32, [7, 0, 0, 0]),
            enumType, default);
        var function = new BoundFunction("GetColor", enumType, [], 0,
            new BoundBlock([new BoundReturn(constant, default)], default),
            default);
        var program = new BoundProgram([function], -1)
        {
            EnumDefinitions = [definition],
            RuntimePolicy = BoundRuntimePolicy.FailFast,
        };

        var artifact = TrRmExecutableCompiler
            .EmitNominalEnumsDraftConstantReturns(
                program, "enum-module", moduleTargetRequirement: 1);
        Assert.That(TrRmExecutableCompiler.EmitStandard(program, "enum-module",
                "trrm/1-draft-nominal-enums-1", moduleTargetRequirement: 1),
            Is.EqualTo(artifact));
        var tables = TrRmStructuralMetadataValidator
            .ValidateNominalEnumsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForNominalEnumsDraft(artifact));
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(1), tables.Contracts.Count);

        Assert.That(tables.EnumDefs.Count, Is.EqualTo(1));
        Assert.That(tables.Methods.Count, Is.EqualTo(1));
        Assert.That(tables.TypeSpecs.Count, Is.EqualTo(1));
        Assert.That(tables.FrozenValueNodes.Count, Is.EqualTo(1));
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
            tables.Signatures.GetResultTypeSpec(
                tables.Methods.GetSignature(1)),
            BinaryPrimitives.ReadUInt32LittleEndian(body.ConstantMap)),
            Is.True);
        Assert.That(tables.FrozenValueNodes.GetTypeSpecRef(1),
            Is.EqualTo(BinaryPrimitives.ReadUInt32LittleEndian(
                body.ConstantMap)));
        var loaded = TrRmExecutableCompiler
            .LoadNominalEnumsDraftScalarReturns(artifact);
        var loadedConstant = (BoundConstant)((BoundReturn)
            loaded.Functions[0].Body.Statements[0]).Value!;
        Assert.That(loaded.EnumDefinitions.Length, Is.EqualTo(1));
        Assert.That(loadedConstant.Type, Is.EqualTo(enumType));
        Assert.That(loadedConstant.Bits.Low, Is.EqualTo(7UL));
        Assert.That(TrRmExecutableCompiler
                .EmitNominalEnumsDraftConstantReturns(
                    loaded, "enum-module", moduleTargetRequirement: 1),
            Is.EqualTo(artifact));
        var image = NativeEmitter.EmitLibrary(loaded, ["GetColor"],
            NativeRuntimePolicy.FailFast);
        Assert.That(image.Length, Is.GreaterThan(sizeof(ushort)));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(image),
            Is.EqualTo(0x5A4D));
    }

    [Test]
    public async Task EmitsAndExecutesNominalEnumIdentityMethod()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var colorIdentity = new CoreDeclarationIdentity(
            "enum-module", "Color", CoreDeclarationKind.EnumType);
        var colorMember = new BoundEnumMemberDefinition(
            new BoundEnumMemberIdentity.Core(new CoreDeclarationIdentity(
                "enum-module", "color-red",
                CoreDeclarationKind.EnumMember)),
            new BoundEnumDeclarationIdentity.Core(colorIdentity),
            "Red", 7, true, "color-red");
        var colorDefinition = new BoundEnumDefinition(
            new BoundEnumDeclarationIdentity.Core(colorIdentity),
            "Example.Color", PrimitiveType.Int32, [colorMember], true, true, false)
        {
            BlittableTargetRequirement
                = FixedLayoutStorage.WindowsX64TargetRequirement,
        };
        var sizeIdentity = new CoreDeclarationIdentity(
            "enum-module", "Size", CoreDeclarationKind.EnumType);
        var sizeMember = new BoundEnumMemberDefinition(
            new BoundEnumMemberIdentity.Core(new CoreDeclarationIdentity(
                "enum-module", "size-small",
                CoreDeclarationKind.EnumMember)),
            new BoundEnumDeclarationIdentity.Core(sizeIdentity), "Small", 1,
            true, "size-small");
        var sizeDefinition = new BoundEnumDefinition(
            new BoundEnumDeclarationIdentity.Core(sizeIdentity),
            "Example.Size", PrimitiveType.Int32, [sizeMember], true, true, false)
        {
            BlittableTargetRequirement
                = FixedLayoutStorage.WindowsX64TargetRequirement,
        };
        var enumType = BoundEnumTypes.Type(0);
        var function = new BoundFunction("IdentityColor", enumType,
            [new BoundVariable("value", enumType, false, default)], 1,
            new BoundBlock([
                new BoundReturn(new BoundRead(0, enumType, default), default),
            ], default), default);
        var program = new BoundProgram([function], -1)
        {
            EnumDefinitions = [colorDefinition, sizeDefinition],
            RuntimePolicy = BoundRuntimePolicy.FailFast,
        };

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "enum-module", "trrm/1-draft-nominal-enums-1",
            moduleTargetRequirement: 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateNominalEnumsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForNominalEnumsDraft(artifact));
        var (parameterStart, parameterCount) = tables.Signatures.GetParamRange(
            tables.Methods.GetSignature(1));
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(1), tables.Contracts.Count);

        Assert.That(parameterCount, Is.EqualTo(1));
        Assert.That(tables.Parameters.GetTypeSpec(parameterStart),
            Is.EqualTo(tables.Signatures.GetResultTypeSpec(
                tables.Methods.GetSignature(1))));
        Assert.That(body.MapMask, Is.EqualTo(0x03));
        Assert.That(tables.Features.GetFeature(1),
            Is.EqualTo(TrRmFeatureRow.NativeBoundaryContracts));
        Assert.That(tables.Features.GetFeature(2).FeatureId, Is.EqualTo(18));
        Assert.That(TrRmFeatureManifestValidator.Requires(tables.Features,
            tables.Modules, 1, TrRmFeatureTableReader.TypedFrozenValuesFeatureId,
            TrRmArtifactSchema.NominalEnumsDraft), Is.True);

        var loaded = TrRmExecutableCompiler
            .LoadNominalEnumsDraftIdentityLibrary(artifact);
        Assert.That(loaded.EnumDefinitions.Length, Is.EqualTo(2));
        Assert.That(loaded.Functions[0].ParameterCount, Is.EqualTo(1));
        Assert.That(((BoundReturn)loaded.Functions[0].Body.Statements[0]).Value,
            Is.TypeOf<BoundRead>());
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded, "enum-module",
                "trrm/1-draft-nominal-enums-1", moduleTargetRequirement: 1),
            Is.EqualTo(artifact));

        var mismatchedTypeMap = body.TypeMap.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(mismatchedTypeMap,
            0x1100_0002);
        var invalidBody = TrRmRegisterBodyWriter.Encode(
            body.HomeMap.ToArray(), mismatchedTypeMap, body.MethodMap.ToArray(),
            body.FieldMap.ToArray(), body.ConstantMap.ToArray(),
            [new TrRmRegisterBodyBlock(0, 4, 0)],
            [new ReadOnlyMemory<byte>(body.GetRecord(0).ToArray())],
            body.GetBlockCode(0).ToArray());
        var invalidBodyTable = TrRmVariableTableWriter.EncodeBodies([
            new ReadOnlyMemory<byte>(invalidBody),
        ]);
        var index = TrRmArtifactIndex.ForNominalEnumsDraft(artifact);
        var invalidSections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var count);
            invalidSections.Add(kind == 0x0020
                ? new TrRmSection(kind, flags, 1, invalidBodyTable)
                : new TrRmSection(kind, flags, checked((uint)count),
                    bytes.ToArray()));
        }

        var invalidArtifact = TrRmArtifactWriter.Encode(invalidSections,
            TrRmArtifactSchema.NominalEnumsDraft);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadNominalEnumsDraftIdentityLibrary(
                invalidArtifact));

        var image = NativeEmitter.EmitLibrary(loaded, ["IdentityColor"],
            NativeRuntimePolicy.FailFast);
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-enum-identity-{Guid.NewGuid():N}.dll");
        var handle = IntPtr.Zero;
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            handle = NativeLibrary.Load(path);
            var identity = Marshal.GetDelegateForFunctionPointer<EnumIdentity>(
                NativeLibrary.GetExport(handle, "IdentityColor"));
            Assert.That(identity(0x1234), Is.EqualTo(0x1234));
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeLibrary.Free(handle);
            }
            await TestFileCleanup.DeleteWithRetryAsync(path)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public void EmitsEnumFreeNominalEnumsDraftLibraryWithoutFeature18()
    {
        var constant = new BoundConstant(
            ScalarBits.FromPayload(PrimitiveType.Int32, [42, 0, 0, 0]),
            PrimitiveType.Int32, default);
        var function = new BoundFunction("GetValue", PrimitiveType.Int32,
            [], 0, new BoundBlock([new BoundReturn(constant, default)], default),
            default);
        var program = new BoundProgram([function], -1)
        {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
        };

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "scalar-module", "trrm/1-draft-nominal-enums-1",
            moduleTargetRequirement: 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateNominalEnumsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForNominalEnumsDraft(artifact));
        Assert.That(tables.TypeDefs.Count, Is.Zero);
        Assert.That(tables.EnumDefs.Count, Is.Zero);
        Assert.That(tables.Features.Count, Is.EqualTo(1));
        Assert.That(tables.Features.GetFeature(1),
            Is.EqualTo(TrRmFeatureRow.TypedFrozenValues));
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(1), tables.Contracts.Count);
        Assert.That(body.MapMask, Is.EqualTo(0x10));
        Assert.That(body.ConstantCount, Is.EqualTo(1));

        var noTargetArtifact = TrRmExecutableCompiler.EmitStandard(program,
            "portable-scalar", "trrm/1-draft-nominal-enums-1",
            moduleTargetRequirement: 0);
        var noTargetTables = TrRmStructuralMetadataValidator
            .ValidateNominalEnumsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForNominalEnumsDraft(noTargetArtifact));
        Assert.That(noTargetTables.Modules.GetRequirements(1).TargetRequirement,
            Is.Zero);

        var loaded = TrRmExecutableCompiler
            .LoadNominalEnumsDraftScalarReturns(artifact);
        var loadedConstant = (BoundConstant)((BoundReturn)
            loaded.Functions[0].Body.Statements[0]).Value!;
        Assert.That(loaded.EnumDefinitions, Is.Empty);
        Assert.That(loadedConstant.Type, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(loadedConstant.Bits.Low, Is.EqualTo(42UL));
    }

    [Test]
    public async Task ExecutesNominalEnumReturnFromLoadedCandidateLibrary()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var enumIdentity = new CoreDeclarationIdentity(
            "enum-module", "Color", CoreDeclarationKind.EnumType);
        var member = new BoundEnumMemberDefinition(
            new BoundEnumMemberIdentity.Core(new CoreDeclarationIdentity(
                "enum-module", "color-red",
                CoreDeclarationKind.EnumMember)),
            new BoundEnumDeclarationIdentity.Core(enumIdentity), "Red", 7,
            true, "color-red");
        var definition = new BoundEnumDefinition(
            new BoundEnumDeclarationIdentity.Core(enumIdentity),
            "Example.Color", PrimitiveType.Int32, [member], true, true,
            false);
        var enumType = BoundEnumTypes.Type(0);
        var constant = new BoundConstant(
            ScalarBits.FromPayload(PrimitiveType.Int32, [7, 0, 0, 0]),
            enumType, default);
        var function = new BoundFunction("GetColor", enumType, [], 0,
            new BoundBlock([new BoundReturn(constant, default)], default),
            default);
        var program = new BoundProgram([function], -1)
        {
            EnumDefinitions = [definition],
            RuntimePolicy = BoundRuntimePolicy.FailFast,
        };
        var artifact = TrRmExecutableCompiler
            .EmitNominalEnumsDraftConstantReturns(
                program, "enum-module", moduleTargetRequirement: 1);
        var loaded = TrRmExecutableCompiler
            .LoadNominalEnumsDraftScalarReturns(artifact);
        var image = NativeEmitter.EmitLibrary(loaded, ["GetColor"],
            NativeRuntimePolicy.FailFast);
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-enum-return-{Guid.NewGuid():N}.dll");
        var handle = IntPtr.Zero;
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            handle = NativeLibrary.Load(path);
            var getColor = Marshal.GetDelegateForFunctionPointer<EnumReturn>(
                NativeLibrary.GetExport(handle, "GetColor"));
            Assert.That(getColor(), Is.EqualTo(7));
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeLibrary.Free(handle);
            }
            await TestFileCleanup.DeleteWithRetryAsync(path)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task EmitsAndExecutesImportedNominalEnumReturn()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var registration
            = Terrum.CodeAnalysis.Tests.Semantics.NativeCoreRegistrationTests.CreateRegistration();
        var enumDefinitions = registration.EnumDefinitions
            .Select(BoundEnumDefinition.FromCore)
            .ToArray();
        var enumType = BoundEnumTypes.Type(0);
        var constant = new BoundConstant(
            ScalarBits.FromPayload(PrimitiveType.Int32, [2, 0, 0, 0]),
            enumType, default);
        var function = new BoundFunction("GetConvention", enumType, [], 0,
            new BoundBlock([new BoundReturn(constant, default)], default),
            default);
        var program = new BoundProgram([function], -1)
        {
            EnumDefinitions = [.. enumDefinitions],
            NativeCoreRegistration = registration,
            RuntimePolicy = BoundRuntimePolicy.FailFast,
        };
        var providerProgram = new BoundProgram([], -1)
        {
            EnumDefinitions = [.. enumDefinitions],
        };

        var providerArtifact = TrRmExecutableCompiler
            .EmitNominalEnumsDraftDeclarationLibrary(
                providerProgram, "core.native", moduleTargetRequirement: 0);
        var artifact = TrRmExecutableCompiler
            .EmitNominalEnumsDraftConstantReturns(
                program, registration.UsingModuleIdentity,
                moduleTargetRequirement: 1);
        TrRmStructuralMetadataValidator
            .ValidateNominalEnumsDraftProviderBindings(
                artifact, [providerArtifact]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateNominalEnumsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForNominalEnumsDraft(artifact));

        Assert.That(tables.Imports.Count, Is.EqualTo(3));
        Assert.That(tables.ModuleRefs.Count, Is.EqualTo(1));
        Assert.That(tables.TypeSpecs.Count, Is.EqualTo(3));
        Assert.That(tables.TypeSpecs.GetTypeSpecRef(1).Form, Is.EqualTo(0x03));
        Assert.That(tables.FrozenValueNodes.GetTypeSpecRef(1), Is.EqualTo(1u));
        var loaded = TrRmExecutableCompiler
            .LoadNominalEnumsDraftScalarReturns(artifact);
        var loadedConstant = (BoundConstant)((BoundReturn)
            loaded.Functions[0].Body.Statements[0]).Value!;
        Assert.That(loaded.EnumDefinitions.Length, Is.EqualTo(3));
        Assert.That(loadedConstant.Type, Is.EqualTo(enumType));
        Assert.That(loadedConstant.Bits.Low, Is.EqualTo(2UL));
        var reemitted = TrRmExecutableCompiler
            .EmitNominalEnumsDraftConstantReturns(
                loaded, registration.UsingModuleIdentity,
                moduleTargetRequirement: 1);
        Assert.That(reemitted, Is.EqualTo(artifact));

        var image = NativeEmitter.EmitLibrary(loaded, ["GetConvention"],
            NativeRuntimePolicy.FailFast);
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-imported-enum-return-{Guid.NewGuid():N}.dll");
        var handle = IntPtr.Zero;
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            handle = NativeLibrary.Load(path);
            var getConvention = Marshal.GetDelegateForFunctionPointer<EnumReturn>(
                NativeLibrary.GetExport(handle, "GetConvention"));
            Assert.That(getConvention(), Is.EqualTo(2));
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeLibrary.Free(handle);
            }
            await TestFileCleanup.DeleteWithRetryAsync(path)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task EmitsAndExecutesImportedNominalEnumIdentityMethod()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var registration
            = Terrum.CodeAnalysis.Tests.Semantics.NativeCoreRegistrationTests.CreateRegistration();
        var enumDefinitions = registration.EnumDefinitions
            .Select(BoundEnumDefinition.FromCore)
            .ToArray();
        var enumType = BoundEnumTypes.Type(0);
        var function = new BoundFunction("IdentityConvention", enumType,
            [new BoundVariable("value", enumType, false, default)], 1,
            new BoundBlock([
                new BoundReturn(new BoundRead(0, enumType, default), default),
            ], default), default);
        var program = new BoundProgram([function], -1)
        {
            EnumDefinitions = [.. enumDefinitions],
            NativeCoreRegistration = registration,
            RuntimePolicy = BoundRuntimePolicy.FailFast,
        };
        var providerProgram = new BoundProgram([], -1)
        {
            EnumDefinitions = [.. enumDefinitions],
        };
        var providerArtifact = TrRmExecutableCompiler
            .EmitNominalEnumsDraftDeclarationLibrary(
                providerProgram, "core.native", moduleTargetRequirement: 0);
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            registration.UsingModuleIdentity,
            "trrm/1-draft-nominal-enums-1", moduleTargetRequirement: 1);
        TrRmStructuralMetadataValidator
            .ValidateNominalEnumsDraftProviderBindings(
                artifact, [providerArtifact]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateNominalEnumsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForNominalEnumsDraft(artifact));

        Assert.That(tables.Imports.Count, Is.EqualTo(3));
        Assert.That(tables.TypeSpecs.Count, Is.EqualTo(3));
        Assert.That(tables.TypeSpecs.GetTypeSpecRef(1).Form, Is.EqualTo(0x03));
        var loaded = TrRmExecutableCompiler
            .LoadNominalEnumsDraftIdentityLibrary(artifact);
        Assert.That(loaded.EnumDefinitions.Length, Is.EqualTo(3));
        Assert.That(loaded.Functions[0].Variables[0].Type, Is.EqualTo(enumType));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                registration.UsingModuleIdentity,
                "trrm/1-draft-nominal-enums-1", moduleTargetRequirement: 1),
            Is.EqualTo(artifact));

        var image = NativeEmitter.EmitLibrary(loaded, ["IdentityConvention"],
            NativeRuntimePolicy.FailFast);
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-imported-enum-identity-{Guid.NewGuid():N}.dll");
        var handle = IntPtr.Zero;
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            handle = NativeLibrary.Load(path);
            var identity = Marshal.GetDelegateForFunctionPointer<EnumIdentity>(
                NativeLibrary.GetExport(handle, "IdentityConvention"));
            Assert.That(identity(2), Is.EqualTo(2));
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeLibrary.Free(handle);
            }
            await TestFileCleanup.DeleteWithRetryAsync(path)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public void RejectsExternalEnumEmissionWithoutSelectedProviderOrigin()
    {
        var enumIdentity = new CoreDeclarationIdentity(
            "provider-module", "Color", CoreDeclarationKind.EnumType);
        var member = new BoundEnumMemberDefinition(
            new BoundEnumMemberIdentity.Core(new CoreDeclarationIdentity(
                "provider-module", "color-red",
                CoreDeclarationKind.EnumMember)),
            new BoundEnumDeclarationIdentity.Core(enumIdentity), "Red", 7,
            true, "color-red");
        var program = new BoundProgram([], -1)
        {
            EnumDefinitions = [
                new BoundEnumDefinition(
                    new BoundEnumDeclarationIdentity.Core(enumIdentity),
                    "Example.Color",
                    PrimitiveType.Int32, [member], true, true, false),
            ],
        };

        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitNominalEnumsDraftDeclarationLibrary(
                program, "consumer-module", moduleTargetRequirement: 0));
    }

    [Test]
    public void EmitsExternalNominalEnumWithSelectedProviderModuleOrigin()
    {
        var registration
            = Terrum.CodeAnalysis.Tests.Semantics.NativeCoreRegistrationTests.CreateRegistration();
        var enumIdentity = new CoreDeclarationIdentity(
            "core.native", "Color", CoreDeclarationKind.EnumType);
        var member = new BoundEnumMemberDefinition(
            new BoundEnumMemberIdentity.Core(new CoreDeclarationIdentity(
                "core.native", "color-red",
                CoreDeclarationKind.EnumMember)),
            new BoundEnumDeclarationIdentity.Core(enumIdentity), "Red", 7,
            true, "color-red");
        var importedDefinition = new BoundEnumDefinition(
            new BoundEnumDeclarationIdentity.Core(enumIdentity),
            "Example.Color", PrimitiveType.Int32, [member], true, true, false);
        var definitions = registration.EnumDefinitions
            .Select(BoundEnumDefinition.FromCore)
            .Append(importedDefinition)
            .ToArray();
        var providerProgram = new BoundProgram([], -1)
        {
            EnumDefinitions = [.. definitions],
        };
        var program = new BoundProgram([], -1)
        {
            EnumDefinitions = [.. definitions],
            NativeCoreRegistration = registration,
        };

        var providerArtifact = TrRmExecutableCompiler
            .EmitNominalEnumsDraftDeclarationLibrary(
                providerProgram, "core.native", moduleTargetRequirement: 0);
        var artifact = TrRmExecutableCompiler
            .EmitNominalEnumsDraftDeclarationLibrary(
                program, registration.UsingModuleIdentity,
                moduleTargetRequirement: 0);
        Assert.DoesNotThrow(() =>
            TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftProviderBindings(
                    artifact, [providerArtifact]));
        var tables = TrRmStructuralMetadataValidator
            .ValidateNominalEnumsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForNominalEnumsDraft(artifact));

        Assert.That(tables.EnumDefs.Count, Is.EqualTo(4));
        Assert.That(tables.Imports.Count, Is.EqualTo(4));
        Assert.That(tables.ModuleRefs.GetProducerIdentity(1),
            Is.EqualTo("core.native"));
        Assert.That(tables.ModuleRefs.GetRequirements(1).TargetRequirement,
            Is.EqualTo(0u));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ValidatesAndMaterializesEnumFrozenScalarWithNominalType(
        bool imported)
    {
        var artifact = CreateNominalEnumValueArtifact(isImported: imported);
        var tables = TrRmStructuralMetadataValidator
            .ValidateNominalEnumsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForNominalEnumsDraft(artifact));

        if (imported)
        {
            TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftProviderBindings(
                    artifact, [CreateBoundEnumArtifact(isConsumer: false)]);
        }
        TrRmConstantSourceValueValidator.ValidateFrozenScalar(
            1, 1, 1, tables.ConstantSources, tables.FrozenValueNodes,
            tables.Blobs, tables.TypeSpecs, tables.Features, tables.Modules,
            tables.TypeDefs, tables.EnumDefs, tables.Imports);
        var value = TrRmConstantSourceValueValidator.MaterializeFrozenScalar(
            1, 1, 1, 1, tables.ConstantSources, tables.FrozenValueNodes,
            tables.Blobs, tables.TypeSpecs, tables.Features, tables.Modules,
            tables.TypeDefs, tables.EnumDefs, tables.Imports);

        Assert.That(value.Type, Is.EqualTo(BoundEnumTypes.Type(0)));
        Assert.That(value.Bits.Low, Is.EqualTo(7UL));
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateNominalEnumFrozenScalar(artifact, 0xF200_0008));
        var invalidPayload = CreateNominalEnumValueArtifact(
            blobLength: 8, isImported: imported);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateNominalEnumFrozenScalar(invalidPayload, 1));
    }

    private static void ValidateNominalEnumFrozenScalar(byte[] artifact,
        uint expectedTypeSpecRef)
    {
        var tables = TrRmStructuralMetadataValidator
            .ValidateNominalEnumsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForNominalEnumsDraft(artifact));
        TrRmConstantSourceValueValidator.ValidateFrozenScalar(
            1, expectedTypeSpecRef, 1, tables.ConstantSources,
            tables.FrozenValueNodes, tables.Blobs, tables.TypeSpecs,
            tables.Features, tables.Modules, tables.TypeDefs,
            tables.EnumDefs, tables.Imports);
    }

    private static EnumFixture CreateFixture(bool includeMembers = true, bool blittable = true)
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("enum-module", 0), ("Color", 0), ("Red", 0),
            ("AlsoRed", 0), ("color-red", 0), ("color-also-red", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 7);
        var modules = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 1, 0, 0);
        var moduleReader = new TrRmModuleTableReader(modules, 1, strings,
            0, 1, 0, 0);
        var layoutCount = blittable ? 2 : 1;
        var typeBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 0x12, 0, 0, 0, 0, 1, layoutCount, 0, 0),
        ], strings, moduleReader, 0, 0, layoutCount, 0,
            TrRmArtifactSchema.NominalEnumsDraft);
        var types = new TrRmTypeDefTableReader(typeBytes, 1, strings,
            moduleReader, 0, 0, layoutCount, 0,
            TrRmArtifactSchema.NominalEnumsDraft);
        var targetProfileBytes = blittable
            ? TrRmTargetProfileTableWriter.Encode([new TrRmTargetProfileRow(1, 1)])
            : [];
        var targetProfiles = new TrRmTargetProfileTableReader(targetProfileBytes,
            blittable ? 1 : 0);
        var layoutRows = blittable
            ? new[] {
                new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
                new TrRmLayoutRow(1, 1, 0, 1, 0, 0, 0, 0),
            }
            : [new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0)];
        var layoutBytes = TrRmLayoutTableWriter.Encode(layoutRows, types,
            targetProfiles, 0);
        var layouts = new TrRmLayoutTableReader(layoutBytes, layoutCount,
            types, targetProfiles, 0);
        var typeSpecs = default(TrRmTypeSpecTableReader);
        var memberRows = includeMembers
            ? new[] {
                new TrRmEnumMemberDefRow(1, 0, 6, 4, 7),
                new TrRmEnumMemberDefRow(1, 1, 7, 5, 7),
            }
            : [];
        var memberBytes = TrRmEnumTablesWriter.EncodeMembers(memberRows,
            strings, types);
        var members = new TrRmEnumMemberDefTableReader(memberBytes,
            memberRows.Length, strings, types);
        var enumRows = new[] {
            new TrRmEnumDefRow(1, 0xF200_0008, 1,
                includeMembers ? 1 : 0, includeMembers ? 2 : 0),
        };
        var enumBytes = TrRmEnumTablesWriter.Encode(enumRows, types, typeSpecs,
            layouts, targetProfiles, members);
        return new EnumFixture(stringBytes, modules, typeBytes, targetProfileBytes,
            layoutBytes, memberBytes, enumBytes, layoutCount, memberRows.Length);
    }

    private static (uint Flags, int MemberCount, int[] Values) Validate(EnumFixture fixture)
    {
        var strings = new TrRmStringTableReader(fixture.StringBytes, 7);
        var modules = new TrRmModuleTableReader(fixture.ModuleBytes, 1, strings,
            0, 1, 0, 0);
        var types = new TrRmTypeDefTableReader(fixture.TypeBytes, 1, strings,
            modules, 0, 0, fixture.LayoutCount, 0,
            TrRmArtifactSchema.NominalEnumsDraft);
        var targetProfiles = new TrRmTargetProfileTableReader(
            fixture.TargetProfileBytes, fixture.TargetProfileBytes.Length / 8);
        var layouts = new TrRmLayoutTableReader(fixture.LayoutBytes,
            fixture.LayoutCount, types, targetProfiles, 0);
        var typeSpecs = default(TrRmTypeSpecTableReader);
        var members = new TrRmEnumMemberDefTableReader(fixture.MemberBytes,
            fixture.MemberCount, strings, types);
        var enums = new TrRmEnumDefTableReader(fixture.EnumBytes, 1, types,
            typeSpecs, layouts, targetProfiles, members);
        var (First, Count) = enums.GetMemberRange(1);
        var values = new int[Count];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = members.GetValue(First + index);
        }

        return (enums.GetFlags(1), Count, values);
    }

    private static byte[] CreateArtifact(TrRmFeatureRow[] features,
        uint moduleTargetRequirement = 0)
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("enum-module", 0), ("Color", 0), ("Red", 0),
            ("AlsoRed", 0), ("color-red", 0), ("color-also-red", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 7);
        var featureBytes = TrRmFeatureTableWriter.Encode(features,
            TrRmArtifactSchema.NominalEnumsDraft);
        var modules = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, moduleTargetRequirement, 0, 0,
                1, 1, 1, 1, 1, features.Length),
        ], strings, 0, 1, 0, 0, features.Length);
        var moduleReader = new TrRmModuleTableReader(modules, 1, strings,
            0, 1, 0, 0, features.Length);
        var targetProfileBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ]);
        var targetProfiles = new TrRmTargetProfileTableReader(targetProfileBytes, 1);
        var typeBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 0x12, 0, 0, 0, 0, 1, 2, 0, 0),
        ], strings, moduleReader, 0, 0, 2, 0,
            TrRmArtifactSchema.NominalEnumsDraft);
        var types = new TrRmTypeDefTableReader(typeBytes, 1, strings,
            moduleReader, 0, 0, 2, 0,
            TrRmArtifactSchema.NominalEnumsDraft);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
            new TrRmLayoutRow(1, 1, 0, 1, 0, 0, 0, 0),
        ], types, targetProfiles, 0);
        var layouts = new TrRmLayoutTableReader(layoutBytes, 2, types,
            targetProfiles, 0);
        var members = TrRmEnumTablesWriter.EncodeMembers([
            new TrRmEnumMemberDefRow(1, 0, 6, 4, 7),
            new TrRmEnumMemberDefRow(1, 1, 7, 5, 7),
        ], strings, types);
        var memberReader = new TrRmEnumMemberDefTableReader(members, 2,
            strings, types);
        var enumDefinitions = TrRmEnumTablesWriter.Encode([
            new TrRmEnumDefRow(1, 0xF200_0008, 1, 1, 2),
        ], types, default, layouts, targetProfiles, memberReader);

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, modules),
            new TrRmSection(0x0002, 0, 7, stringBytes),
            new TrRmSection(0x0004, 0, (uint)features.Length, featureBytes),
            new TrRmSection(0x0013, 0, 1, typeBytes),
            new TrRmSection(0x0015, 0, 2, layoutBytes),
            new TrRmSection(0x0048, 0, 1, targetProfileBytes),
            new TrRmSection(0x0053, 0, 1, enumDefinitions),
            new TrRmSection(0x0054, 0, 2, members),
        ], TrRmArtifactSchema.NominalEnumsDraft);
    }

    private static byte[] CreateNominalEnumValueArtifact(int blobLength = 4,
        bool isImported = false)
    {
        var baseArtifact = isImported
            ? CreateBoundEnumArtifact(isConsumer: true,
                includeTypedFrozenValues: true, consumerTargetRequirement: 1)
            : CreateArtifact([
                TrRmFeatureRow.NativeBoundaryContracts,
                TrRmFeatureRow.TypedFrozenValues,
                new TrRmFeatureRow(18, 1),
            ], moduleTargetRequirement: 1);
        var index = TrRmArtifactIndex.ForNominalEnumsDraft(baseArtifact);
        var sections = new List<TrRmSection>(index.SectionCount + 1);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)count), bytes.ToArray()));
        }

        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            new ReadOnlyMemory<byte>(
                TrRmTypeSpecTableWriter.Reference(
                    isImported ? (byte)0x03 : (byte)0x02, 1)),
        ], 1, isImported ? 1 : 0, 0, 0,
            TrRmArtifactSchema.NominalEnumsDraft);
        sections.Add(new TrRmSection(0x0011, 0, 1, typeSpecBytes));
        sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
        var typeSpecArtifact = TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.NominalEnumsDraft);
        var tables = TrRmStructuralMetadataValidator
            .ValidateNominalEnumsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForNominalEnumsDraft(typeSpecArtifact));
        var builder = new TrRmFrozenScalarValueBuilder();
        var payload = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(payload, 7);
        var value = new BoundConstant(
            ScalarBits.FromPayload(PrimitiveType.Int32, payload),
            BoundEnumTypes.Type(0), default);
        var valueReference = builder.AddNominalEnum(value, 1);
        if (valueReference != new TrRmFrozenScalarValueReference(1, 1))
        {
            throw new InvalidOperationException(
                "The first nominal enum frozen value must use row 1.");
        }

        var valueTables = builder.EncodeNominalEnumsDraft(tables.TypeSpecs,
            tables.TypeDefs, tables.EnumDefs, tables.Imports);
        var finalIndex = TrRmArtifactIndex.ForNominalEnumsDraft(typeSpecArtifact);
        var finalSections = new List<TrRmSection>(finalIndex.SectionCount + 3);
        for (var section = 0; section < finalIndex.SectionCount; section++)
        {
            finalIndex.GetSection(section, out var kind, out var flags,
                out var bytes, out var count);
            finalSections.Add(new TrRmSection(kind, flags,
                checked((uint)count), bytes.ToArray()));
        }
        var blobSection = valueTables.BlobSection;
        if (blobLength != sizeof(int))
        {
            blobSection = TrRmVariableTableWriter.EncodeBlobs([
                new ReadOnlyMemory<byte>(new byte[blobLength]),
            ]);
        }
        finalSections.Add(new TrRmSection(0x0003, 0,
            (uint)valueTables.BlobCount, blobSection));
        finalSections.Add(new TrRmSection(0x0030, 0,
            (uint)valueTables.ConstantSourceCount,
            valueTables.ConstantSourceSection));
        finalSections.Add(new TrRmSection(0x003F, 0,
            (uint)valueTables.FrozenValueNodeCount,
            valueTables.FrozenValueNodeSection));
        finalSections.Sort(static (left, right)
            => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(finalSections,
            TrRmArtifactSchema.NominalEnumsDraft);
    }

    private static byte[] CreateBoundEnumArtifact(bool isConsumer,
        int memberValue = 7, string declarationKey = "color-red",
        bool blittable = true, uint flags = 1,
        string providerModuleIdentity = "provider",
        uint providerTargetRequirement = 0, uint providerRuntimePolicy = 0,
        string providerExportKey = "color",
        bool includeTypedFrozenValues = false,
        uint consumerTargetRequirement = 0)
    {
        const string providerIdentity = "provider";
        const string exportKey = "color";
        var moduleIdentity = isConsumer ? "consumer" : providerModuleIdentity;
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), (moduleIdentity, 0), (providerIdentity, 0),
            ("Color", 0), ("Red", 0), ("AlsoRed", 0),
            (declarationKey, 0), ("color-also-red", 0),
            (isConsumer ? exportKey : providerExportKey, 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 9);
        var featureRows = includeTypedFrozenValues
            ? new[] {
                TrRmFeatureRow.NativeBoundaryContracts,
                TrRmFeatureRow.TypedFrozenValues,
                new TrRmFeatureRow(18, 1),
            }
            : [
                TrRmFeatureRow.NativeBoundaryContracts,
                new TrRmFeatureRow(18, 1),
            ];
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows,
            TrRmArtifactSchema.NominalEnumsDraft);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2,
                isConsumer ? consumerTargetRequirement : providerTargetRequirement,
                isConsumer ? 0 : providerRuntimePolicy,
                0, 1, 1, 1, 1, 1, featureRows.Length),
        ], strings, moduleRefCount: isConsumer ? 1 : 0, typeDefCount: 1,
            methodDefCount: 0, sourceFileCount: 0,
            featureCount: featureRows.Length);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            moduleRefCount: isConsumer ? 1 : 0, typeDefCount: 1,
            methodDefCount: 0, sourceFileCount: 0,
            featureCount: featureRows.Length);
        var targetProfileBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(1, 1),
        ]);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfileBytes, 1);
        var layoutCount = blittable ? 2 : 1;
        var typeBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 4, 0x12, 0, 0, 0, 0,
                1, layoutCount, 0, 0),
        ], strings, modules, fieldCount: 0, genericParamCount: 0,
            layoutCount, contractCount: 0,
            TrRmArtifactSchema.NominalEnumsDraft);
        var types = new TrRmTypeDefTableReader(typeBytes, 1, strings,
            modules, 0, 0, layoutCount, 0,
            TrRmArtifactSchema.NominalEnumsDraft);
        var layoutRows = blittable
            ? new[] {
                new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
                new TrRmLayoutRow(1, 1, 0, 1, 0, 0, 0, 0),
            }
            : [new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0)];
        var layoutBytes = TrRmLayoutTableWriter.Encode(layoutRows, types,
            targetProfiles, layoutFieldCount: 0);
        var layouts = new TrRmLayoutTableReader(layoutBytes, layoutCount,
            types, targetProfiles, layoutFieldCount: 0);
        var membersBytes = TrRmEnumTablesWriter.EncodeMembers([
            new TrRmEnumMemberDefRow(1, 0, 7, 5, memberValue),
            new TrRmEnumMemberDefRow(1, 1, 8, 6, 7),
        ], strings, types);
        var members = new TrRmEnumMemberDefTableReader(
            membersBytes, 2, strings, types);
        var enumBytes = TrRmEnumTablesWriter.Encode([
            new TrRmEnumDefRow(1, 0xF200_0008, flags, 1, 2),
        ], types, default, layouts, targetProfiles, members);
        var moduleRefsBytes = isConsumer
            ? TrRmModuleRefTableWriter.Encode([
                new TrRmModuleRefRow(3, 0, 0),
            ], strings, modules)
            : [];
        var moduleRefs = new TrRmModuleRefTableReader(
            moduleRefsBytes, isConsumer ? 1 : 0, strings, modules);
        var features = new TrRmFeatureTableReader(featureBytes,
            featureRows.Length, TrRmArtifactSchema.NominalEnumsDraft);
        var importsBytes = isConsumer
            ? TrRmImportTableWriter.Encode([
                new TrRmImportRow(1, 9, 0x1300_0001),
            ], strings, moduleRefs, types, default, default, default,
                default, default, features, modules,
                TrRmArtifactSchema.NominalEnumsDraft)
            : [];
        var imports = new TrRmImportTableReader(importsBytes,
            isConsumer ? 1 : 0, strings, moduleRefs, types, default, default,
            default, default, default, features, modules,
            TrRmArtifactSchema.NominalEnumsDraft);
        var exportsBytes = isConsumer
            ? []
            : TrRmExportTableWriter.Encode([
                new TrRmExportRow(0x1300_0001, 9),
            ], strings, types, default, default, imports,
                default, default, default, features, modules,
                TrRmArtifactSchema.NominalEnumsDraft);

        var sections = new List<TrRmSection> {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, 9, stringBytes),
            new(0x0004, 0, (uint)featureRows.Length, featureBytes),
            new(0x0013, 0, 1, typeBytes),
            new(0x0015, 0, (uint)layoutCount, layoutBytes),
            new(0x0048, 0, 1, targetProfileBytes),
            new(0x0053, 0, 1, enumBytes),
            new(0x0054, 0, 2, membersBytes),
        };
        if (isConsumer)
        {
            sections.Add(new TrRmSection(0x0010, 0, 1, moduleRefsBytes));
            sections.Add(new TrRmSection(0x001C, 0, 1, importsBytes));
        }
        else
        {
            sections.Add(new TrRmSection(0x001D, 0, 1, exportsBytes));
        }

        sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.NominalEnumsDraft);
    }

    private static (bool HasTypedFrozenValues, bool HasNominalUnionMetadata)
        ValidateCandidateArtifact(byte[] artifact)
    {
        var index = TrRmArtifactIndex.ForNominalEnumsDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateNominalEnumsDraftAndGetExecutionTables(index);
        return (
            TrRmFeatureManifestValidator.Requires(tables.Features, tables.Modules, 1,
                TrRmFeatureTableReader.TypedFrozenValuesFeatureId, index.Schema),
            TrRmFeatureManifestValidator.Requires(tables.Features, tables.Modules, 1,
                TrRmFeatureTableReader.NominalUnionMetadataFeatureId, index.Schema));
    }

    private static void ValidateDefaultArtifact(byte[] artifact)
        => TrRmStructuralMetadataValidator.Validate(artifact);

    private static byte[] WriteUInt32(byte[] source, int offset, uint value)
    {
        var result = (byte[])source.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), value);
        return result;
    }

    private sealed record EnumFixture(
        byte[] StringBytes, byte[] ModuleBytes, byte[] TypeBytes,
        byte[] TargetProfileBytes, byte[] LayoutBytes, byte[] MemberBytes,
        byte[] EnumBytes, int LayoutCount, int MemberCount);
}
