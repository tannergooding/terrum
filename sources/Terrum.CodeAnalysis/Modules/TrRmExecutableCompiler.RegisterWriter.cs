// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    internal static byte[] EmitNominalEnumsDraftDeclarationLibrary(
        BoundProgram program, string moduleIdentity,
        uint moduleTargetRequirement)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        if (program.EntryPoint != -1 || !program.Functions.IsEmpty
            || !program.Structures.IsEmpty || !program.Classes.IsEmpty
            || !program.Buffers.IsEmpty
            || !program.NativeFunctionPointerSignatures.IsEmpty
            || !program.NativePointerTypes.IsEmpty
            || program.RequiresSourceGrant
            || program.InvocationProfileId is not null
            || program.IsInvocationSourceBound
            || !BoundEnumTypes.IsValidTable(program.EnumDefinitions)
            || moduleTargetRequirement > 1
            || program.RuntimePolicy is not (BoundRuntimePolicy.FailFast
                or BoundRuntimePolicy.CheckedExceptions))
        {
            throw Unsupported(
                "NominalEnumsDraft declaration emission requires only valid public enum declarations.");
        }

        var coreRegistration = program.NativeCoreRegistration;
        if (coreRegistration is not null
            && (!coreRegistration.IsValid()
                || (!program.EnumDefinitions.IsEmpty
                    && !BoundEnumTypes.ContainsRegistration(
                        program.EnumDefinitions, coreRegistration))
                || !StringComparer.Ordinal.Equals(
                    coreRegistration.UsingModuleIdentity, moduleIdentity)))
        {
            throw Unsupported(
                "NominalEnumsDraft declarations require the selected using-Module identity.");
        }

        var stringRows = new List<(string Text, int Encoding)> {
            ("", 0), (moduleIdentity, 0),
        };
        var stringRowsByText = new Dictionary<string, int>(StringComparer.Ordinal) {
            [""] = 1,
            [moduleIdentity] = 2,
        };
        var moduleRefsToWrite = new List<TrRmModuleRefRow>();
        var moduleRefByProvider = new Dictionary<string, int>(StringComparer.Ordinal);
        var typeDefsToWrite = new List<TrRmTypeDefRow>(
            program.EnumDefinitions.Length);
        var layoutsToWrite = new List<TrRmLayoutRow>(
            program.EnumDefinitions.Length * 2);
        var enumDefsToWrite = new List<TrRmEnumDefRow>(
            program.EnumDefinitions.Length);
        var enumMembersToWrite = new List<TrRmEnumMemberDefRow>();
        var importsToWrite = new List<TrRmImportRow>();
        var exportsToWrite = new List<TrRmExportRow>();
        var hasBlittableEnum = false;

        int GetStringRow(string text)
        {
            if (!stringRowsByText.TryGetValue(text, out var row))
            {
                row = stringRows.Count + 1;
                stringRows.Add((text, 0));
                stringRowsByText.Add(text, row);
            }

            return row;
        }

        CoreProviderModuleOrigin GetProviderOrigin(string identity)
        {
            if (coreRegistration is null)
            {
                throw Unsupported(
                    "NominalEnumsDraft imports require explicit provider Module origins.");
            }

            foreach (var provider in coreRegistration.ModuleBindings.Providers)
            {
                if (StringComparer.Ordinal.Equals(provider.ModuleIdentity,
                    identity))
                {
                    return provider;
                }
            }

            throw Unsupported(
                "NominalEnumsDraft import has no selected provider Module origin.");
        }

        (uint TargetRequirement, uint RuntimePolicy) GetProviderRequirements(
            BoundEnumDefinition definition)
        {
            var coreIdentity = GetCoreIdentity(definition);
            var identity = coreIdentity.ProviderIdentity;
            if (coreRegistration is not null)
            {
                var origin = GetProviderOrigin(identity);
                return (origin.TargetRequirement, origin.RuntimePolicy);
            }

            if (definition.ModuleImport is BoundModuleImport moduleImport
                && StringComparer.Ordinal.Equals(
                    moduleImport.ProducerIdentity, identity)
                && StringComparer.Ordinal.Equals(
                    moduleImport.ExportKey, coreIdentity.ExportKey))
            {
                return (moduleImport.TargetRequirement,
                    moduleImport.RuntimePolicy);
            }

            throw Unsupported(
                "NominalEnumsDraft imports require preserved Module identity and requirements.");
        }

        CoreDeclarationIdentity GetCoreIdentity(
            BoundEnumDefinition definition)
        {
            if (definition.Identity
                is BoundEnumDeclarationIdentity.Core core)
            {
                return core.Value;
            }

            throw Unsupported(
                "NominalEnumsDraft cannot serialize a private-only enum identity.");
        }

        for (var index = 0; index < program.EnumDefinitions.Length; index++)
        {
            var definition = program.EnumDefinitions[index];
            var coreIdentity = GetCoreIdentity(definition);
            var separator = definition.Name.LastIndexOf(
                '.', StringComparison.Ordinal);
            var namespaceName = separator < 0
                ? ""
                : definition.Name[..separator];
            var typeName = separator < 0
                ? definition.Name
                : definition.Name[(separator + 1)..];
            if (string.IsNullOrWhiteSpace(typeName)
                || (separator == 0))
            {
                throw Unsupported(
                    "NominalEnumsDraft enum names must have valid namespace/type components.");
            }

            var typeDefRow = index + 1;
            var layoutFirst = layoutsToWrite.Count + 1;
            layoutsToWrite.Add(new TrRmLayoutRow(typeDefRow, 0, 0, 0,
                0, 0, 0, 0));
            var layoutCount = 1;
            if (definition.BlittableTargetRequirement != 0)
            {
                if (definition.BlittableTargetRequirement
                        != FixedLayoutStorage.WindowsX64TargetRequirement
                    || moduleTargetRequirement != 1)
                {
                    throw Unsupported(
                        "NominalEnumsDraft Blittable enums require the selected Windows x64 target.");
                }

                hasBlittableEnum = true;
                layoutsToWrite.Add(new TrRmLayoutRow(typeDefRow, 1, 0, 1,
                    0, 0, 0, 0));
                layoutCount++;
            }

            typeDefsToWrite.Add(new TrRmTypeDefRow(
                GetStringRow(namespaceName), GetStringRow(typeName), 0x12,
                0, 0, 0, 0, layoutFirst, layoutCount, 0, 0));

            var memberFirst = definition.Members.IsEmpty
                ? 0 : enumMembersToWrite.Count + 1;
            for (var ordinal = 0; ordinal < definition.Members.Length; ordinal++)
            {
                var member = definition.Members[ordinal];
                enumMembersToWrite.Add(new TrRmEnumMemberDefRow(typeDefRow,
                    ordinal,
                    GetStringRow(member.DeclarationKey),
                    GetStringRow(member.Name), member.Value));
            }
            var memberCount = definition.Members.Length;
            enumDefsToWrite.Add(new TrRmEnumDefRow(typeDefRow,
                0xF200_0008, definition.IsFlags ? 1u : 0u,
                memberFirst, memberCount));

            var exportKey = GetStringRow(coreIdentity.ExportKey);
            if (StringComparer.Ordinal.Equals(
                    coreIdentity.ProviderIdentity, moduleIdentity))
            {
                exportsToWrite.Add(new TrRmExportRow(
                    0x1300_0000u | (uint)typeDefRow, exportKey));
            }
            else
            {
                var providerIdentity = coreIdentity.ProviderIdentity;
                if (!moduleRefByProvider.TryGetValue(
                        providerIdentity, out var moduleRefRow))
                {
                    var (targetRequirement, runtimePolicy) =
                        GetProviderRequirements(definition);
                    moduleRefRow = moduleRefsToWrite.Count + 1;
                    moduleRefByProvider.Add(providerIdentity, moduleRefRow);
                    moduleRefsToWrite.Add(new TrRmModuleRefRow(
                        GetStringRow(providerIdentity),
                        targetRequirement,
                        runtimePolicy));
                }
                else
                {
                    var (targetRequirement, runtimePolicy) =
                        GetProviderRequirements(definition);
                    var existing = moduleRefsToWrite[moduleRefRow - 1];
                    if (existing.TargetRequirement
                            != targetRequirement
                        || existing.RuntimePolicy != runtimePolicy)
                    {
                        throw Unsupported(
                            "NominalEnumsDraft imports for one Module must preserve one exact requirements tuple.");
                    }
                }

                importsToWrite.Add(new TrRmImportRow(moduleRefRow, exportKey,
                    0x1300_0000u | (uint)typeDefRow));
            }
        }

        if (hasBlittableEnum && moduleTargetRequirement != 1)
        {
            throw Unsupported(
                "NominalEnumsDraft Blittable layouts require Windows x64.");
        }

        var featureRows = new List<TrRmFeatureRow>();
        if (enumDefsToWrite.Count != 0)
        {
            featureRows.Add(new TrRmFeatureRow(18, 1));
        }
        if (hasBlittableEnum)
        {
            featureRows.Add(TrRmFeatureRow.NativeBoundaryContracts);
        }
        featureRows.Sort(static (left, right)
            => left.FeatureId.CompareTo(right.FeatureId));
        var stringBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var strings = new TrRmStringTableReader(stringBytes, stringRows.Count);
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows,
            TrRmArtifactSchema.NominalEnumsDraft);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, moduleTargetRequirement,
                (uint)program.RuntimePolicy, 0, 1, 1, 1, 1,
                featureRows.Count == 0 ? 0 : 1,
                featureRows.Count),
        ], strings, moduleRefsToWrite.Count, typeDefsToWrite.Count, 0, 0,
            featureRows.Count);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            moduleRefsToWrite.Count, typeDefsToWrite.Count, 0, 0,
            featureRows.Count);
        var features = new TrRmFeatureTableReader(featureBytes,
            featureRows.Count, TrRmArtifactSchema.NominalEnumsDraft);
        var moduleRefBytes = moduleRefsToWrite.Count == 0
            ? []
            : TrRmModuleRefTableWriter.Encode(moduleRefsToWrite,
                strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes,
            moduleRefsToWrite.Count, strings, modules);
        var typeDefBytes = typeDefsToWrite.Count == 0
            ? []
            : TrRmTypeDefTableWriter.Encode(typeDefsToWrite,
                strings, modules, fieldCount: 0, genericParamCount: 0,
                layoutCount: layoutsToWrite.Count, contractCount: 0,
                TrRmArtifactSchema.NominalEnumsDraft);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes,
            typeDefsToWrite.Count, strings, modules, 0, 0,
            layoutsToWrite.Count, 0, TrRmArtifactSchema.NominalEnumsDraft);
        var targetProfileBytes = hasBlittableEnum
            ? TrRmTargetProfileTableWriter.Encode([
                new TrRmTargetProfileRow(
                    TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                    TrRmTargetProfileTableReader.WindowsX64NativeProfileRuleRevision),
            ])
            : [];
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfileBytes, hasBlittableEnum ? 1 : 0);
        var layoutBytes = TrRmLayoutTableWriter.Encode(layoutsToWrite,
            typeDefs, targetProfiles, layoutFieldCount: 0);
        var layouts = new TrRmLayoutTableReader(layoutBytes,
            layoutsToWrite.Count, typeDefs, targetProfiles,
            layoutFieldCount: 0);
        var typeSpecs = default(TrRmTypeSpecTableReader);
        var enumMemberBytes = TrRmEnumTablesWriter.EncodeMembers(
            enumMembersToWrite, strings, typeDefs);
        var enumMembers = new TrRmEnumMemberDefTableReader(enumMemberBytes,
            enumMembersToWrite.Count, strings, typeDefs);
        var enumDefBytes = TrRmEnumTablesWriter.Encode(enumDefsToWrite,
            typeDefs, typeSpecs, layouts, targetProfiles, enumMembers);
        var importBytes = importsToWrite.Count == 0
            ? []
            : TrRmImportTableWriter.Encode(importsToWrite, strings, moduleRefs,
                typeDefs, default, default, default, default, default,
                features, modules, TrRmArtifactSchema.NominalEnumsDraft);
        var imports = new TrRmImportTableReader(importBytes,
            importsToWrite.Count, strings, moduleRefs, typeDefs,
            default, default, default, default, default, features, modules,
            TrRmArtifactSchema.NominalEnumsDraft);
        var exportBytes = exportsToWrite.Count == 0
            ? []
            : TrRmExportTableWriter.Encode(exportsToWrite, strings, typeDefs,
                default, default, imports, default, default, default,
                features, modules, TrRmArtifactSchema.NominalEnumsDraft);

        var sections = new List<TrRmSection> {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, (uint)stringRows.Count, stringBytes),
        };
        if (featureRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0004, 0,
                (uint)featureRows.Count, featureBytes));
        }
        if (typeDefsToWrite.Count != 0)
        {
            sections.Add(new TrRmSection(0x0013, 0,
                (uint)typeDefsToWrite.Count, typeDefBytes));
        }
        if (layoutsToWrite.Count != 0)
        {
            sections.Add(new TrRmSection(0x0015, 0,
                (uint)layoutsToWrite.Count, layoutBytes));
        }
        if (enumDefsToWrite.Count != 0)
        {
            sections.Add(new TrRmSection(0x0053, 0,
                (uint)enumDefsToWrite.Count, enumDefBytes));
        }
        if (moduleRefsToWrite.Count != 0)
        {
            sections.Add(new TrRmSection(0x0010, 0,
                (uint)moduleRefsToWrite.Count, moduleRefBytes));
        }
        if (importsToWrite.Count != 0)
        {
            sections.Add(new TrRmSection(0x001C, 0,
                (uint)importsToWrite.Count, importBytes));
        }
        if (exportsToWrite.Count != 0)
        {
            sections.Add(new TrRmSection(0x001D, 0,
                (uint)exportsToWrite.Count, exportBytes));
        }
        if (hasBlittableEnum)
        {
            sections.Add(new TrRmSection(0x0048, 0, 1, targetProfileBytes));
        }
        if (enumMembersToWrite.Count != 0)
        {
            sections.Add(new TrRmSection(0x0054, 0,
                (uint)enumMembersToWrite.Count, enumMemberBytes));
        }

        sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
        var artifact = TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.NominalEnumsDraft);
        _ = TrRmStructuralMetadataValidator
            .ValidateNominalEnumsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForNominalEnumsDraft(artifact));
        return artifact;
    }

    internal static byte[] EmitScalarFrozenValuesDraftConstantReturn(
        BoundProgram program, string moduleIdentity)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        if (program.Functions.IsDefaultOrEmpty
            || (program.EntryPoint != -1
                && (uint)program.EntryPoint >= (uint)program.Functions.Length))
        {
            throw Unsupported("Scalar frozen-value emission requires a library or a valid local entry method.");
        }

        if (program.EntryPoint == -1)
        {
            SemanticVerifier.VerifyLibrary(program);
        }
        else
        {
            SemanticVerifier.Verify(program);
        }

        if (!program.Structures.IsEmpty || !program.EnumDefinitions.IsEmpty
            || !program.Classes.IsEmpty
            || !program.Buffers.IsEmpty || !program.NativeUtf16ZLiterals.IsEmpty
            || !program.NativeUtf8ZLiterals.IsEmpty || program.RequiresSourceGrant
            || program.InvocationProfileId is not null || program.IsInvocationSourceBound
            || !program.NativeFunctionPointerSignatures.IsEmpty
            || !program.NativePointerTypes.IsEmpty
            || program.RuntimePolicy != BoundRuntimePolicy.FailFast)
        {
            throw Unsupported("Scalar frozen-value emission requires self-contained fail-fast methods.");
        }

        var scalarValues = new TrRmFrozenScalarValueBuilder();
        var stringRows = new List<(string Text, int Encoding)>(program.Functions.Length + 2)
        {
            ("", 0),
            (moduleIdentity, 1),
        };
        var signaturesToWrite = new List<TrRmMethodSigRow>(program.Functions.Length);
        var methodsToWrite = new List<TrRmMethodDefRow>(program.Functions.Length);
        var exportsToWrite = new List<TrRmExportRow>(program.Functions.Length);
        var bodiesToWrite = new List<ReadOnlyMemory<byte>>(program.Functions.Length);
        for (var index = 0; index < program.Functions.Length; index++)
        {
            var function = program.Functions[index];
            if (!IsScalarFrozenValuesDraftConstantFunction(function)
                || (function.IsPrivate && function.IsInternal))
            {
                throw Unsupported("Scalar frozen-value emission supports only top-level parameterless constant-return methods.");
            }

            var returned = (BoundConstant)((BoundReturn)function.Body.Statements[0]).Value!;
            if (returned.Type != function.ReturnType || returned.Type == PrimitiveType.Void
                || (program.EntryPoint == index && returned.Type != PrimitiveType.Int32))
            {
                throw Unsupported("Scalar frozen-value emission requires matching non-unit scalar results and an int32 entry point.");
            }

            var valueReference = scalarValues.Add(returned);
            var constantMap = new byte[2 * sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(constantMap, valueReference.TypeSpecRef);
            BinaryPrimitives.WriteUInt32LittleEndian(constantMap.AsSpan(sizeof(uint)),
                valueReference.ConstantSourceRow);
            var entryState = new byte[8];
            entryState[0] = 0x01;
            bodiesToWrite.Add(TrRmRegisterBodyWriter.Encode(default, default,
                default, default, constantMap,
                [new TrRmRegisterBodyBlock(0, 2, 0)],
                [new ReadOnlyMemory<byte>(entryState)], new byte[] { 0x09, 0x00 }));

            var methodRow = index + 1;
            var nameStringRow = stringRows.Count + 1;
            stringRows.Add((function.Name, 1));
            signaturesToWrite.Add(new TrRmMethodSigRow(
                valueReference.TypeSpecRef, 0, 0, 0, 0, 0));
            var visibility = function.IsPrivate ? 0u : function.IsInternal ? 1u : 2u;
            methodsToWrite.Add(new TrRmMethodDefRow(0, nameStringRow,
                methodRow, visibility, 0, 0, methodRow));
            if (visibility == 2)
            {
                exportsToWrite.Add(new TrRmExportRow(
                    0x1600_0000u | checked((uint)methodRow), nameStringRow));
            }
        }

        var tables = scalarValues.Encode(default);
        var stringBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var strings = new TrRmStringTableReader(stringBytes, stringRows.Count);
        var featureBytes = TrRmFeatureTableWriter.Encode(
            [TrRmFeatureRow.TypedFrozenValues],
            TrRmArtifactSchema.ScalarFrozenValuesDraft);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0,
                program.EntryPoint == -1 ? 0 : program.EntryPoint + 1,
                1, 1, 1, 1, 1, 1, 1),
        ], strings, 0, 0, program.Functions.Length, 0, 1);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 0, program.Functions.Length, 0, 1);
        var typeSpecs = default(TrRmTypeSpecTableReader);
        var signatureBytes = TrRmMethodSigTableWriter.Encode(signaturesToWrite,
            typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes,
            signaturesToWrite.Count, typeSpecs, 0, 0);
        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode(methodsToWrite,
            strings, modules, typeDefs, signatures, 0, bodiesToWrite.Count);
        var methods = new TrRmMethodDefTableReader(methodBytes,
            methodsToWrite.Count, strings, modules, typeDefs, signatures, 0,
            bodiesToWrite.Count);
        var exports = exportsToWrite.Count == 0
            ? [] : TrRmExportTableWriter.Encode(exportsToWrite, strings,
                typeDefs, methods, default, default);
        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, checked((uint)stringRows.Count), stringBytes),
        };
        if (tables.BlobCount != 0)
        {
            sections.Add(new TrRmSection(0x0003, 0,
                checked((uint)tables.BlobCount), tables.BlobSection));
        }
        sections.Add(new TrRmSection(0x0004, 0, 1, featureBytes));
        sections.Add(new TrRmSection(0x0016, 0,
            checked((uint)methodsToWrite.Count), methodBytes));
        sections.Add(new TrRmSection(0x0017, 0,
            checked((uint)signaturesToWrite.Count), signatureBytes));
        if (exportsToWrite.Count != 0)
        {
            sections.Add(new TrRmSection(0x001D, 0,
                checked((uint)exportsToWrite.Count), exports));
        }
        sections.Add(new TrRmSection(0x0020, 0,
            checked((uint)bodiesToWrite.Count),
            TrRmVariableTableWriter.EncodeBodies(bodiesToWrite)));
        sections.Add(new TrRmSection(0x0030, 0,
            checked((uint)tables.ConstantSourceCount), tables.ConstantSourceSection));
        sections.Add(new TrRmSection(0x003F, 0,
            checked((uint)tables.FrozenValueNodeCount), tables.FrozenValueNodeSection));

        var artifact = TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.ScalarFrozenValuesDraft);
        var artifactIndex = new TrRmArtifactIndex(artifact,
            TrRmArtifactSchema.ScalarFrozenValuesDraft);
        _ = TrRmStructuralMetadataValidator
            .ValidateScalarFrozenValuesDraftAndGetExecutionTables(artifactIndex);
        _ = LoadScalarFrozenValuesDraft(artifact);
        return artifact;
    }

    internal static byte[] EmitStandardFloatingScalarLibrary(
        BoundProgram program, string moduleIdentity)
        => EmitStandardScalarOperationLibrary(program, moduleIdentity, floating: true);

    internal static byte[] EmitStandardIntegerScalarLibrary(
        BoundProgram program, string moduleIdentity)
        => EmitStandardScalarOperationLibrary(program, moduleIdentity, floating: false);

    private static byte[] EmitStandardScalarOperationLibrary(
        BoundProgram program, string moduleIdentity, bool floating)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        SemanticVerifier.VerifyLibrary(program);

        if (program.EntryPoint != -1 || program.Functions.Length != 1
            || !program.Structures.IsEmpty || !program.Classes.IsEmpty
            || !program.Buffers.IsEmpty || !program.NativeUtf16ZLiterals.IsEmpty
            || !program.NativeUtf8ZLiterals.IsEmpty || program.RequiresSourceGrant
            || program.InvocationProfileId is not null || program.IsInvocationSourceBound
            || !program.NativeFunctionPointerSignatures.IsEmpty
            || !program.NativePointerTypes.IsEmpty
            || program.RuntimePolicy != BoundRuntimePolicy.FailFast)
        {
            throw Unsupported("Standard scalar-operation Body emission requires one self-contained fail-fast library method.");
        }

        var function = program.Functions[0];
        if (!IsStandardScalarOperationFunction(function)
            || function.ParameterCount != 2 || function.Variables.Length != 2
            || (function.IsPrivate && function.IsInternal))
        {
            throw Unsupported("Standard scalar-operation Body emission supports one top-level two-parameter scalar function.");
        }

        var typeCode = floating
            ? function.ReturnType switch
            {
                PrimitiveType.Float32 => (byte)0x14,
                PrimitiveType.Float64 => (byte)0x15,
                _ => throw Unsupported("Standard floating Body emission requires float32 or float64."),
            }
            : StandardIntegerScalarTypeCode(function.ReturnType);
        var typeSpecRef = 0xF200_0000u | typeCode;
        var visibility = function.IsPrivate ? 0u : function.IsInternal ? 1u : 2u;
        var body = EncodeStandardScalarOperationBody(function, floating);

        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), (moduleIdentity, 1), (function.Name, 1),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 3);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1, 0, 0, 1),
        ], strings, 0, 0, 1, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 0, 1, 0, 0);
        var types = default(TrRmTypeSpecTableReader);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(typeSpecRef, 0, 1, 2, 0, 0),
        ], types, 2, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 1, types, 2, 0);
        var parameterBytes = TrRmParamTableWriter.Encode([
            new TrRmParamRow(1, 0, typeSpecRef, 0),
            new TrRmParamRow(1, 1, typeSpecRef, 0),
        ], signatures, types);
        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 3, 1, visibility, 0, 0, 1),
        ], strings, modules, typeDefs, signatures, 0, 1);
        var methods = new TrRmMethodDefTableReader(methodBytes, 1, strings, modules,
            typeDefs, signatures, 0, 1);
        var exports = visibility == 2
            ? TrRmExportTableWriter.Encode([
                new TrRmExportRow(0x1600_0001u, 3),
            ], strings, typeDefs, methods, default, default)
            : [];
        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, 3, stringBytes),
            new(0x0016, 0, 1, methodBytes),
            new(0x0017, 0, 1, signatureBytes),
            new(0x0018, 0, 2, parameterBytes),
        };
        if (visibility == 2)
        {
            sections.Add(new TrRmSection(0x001D, 0, 1, exports));
        }
        sections.Add(new TrRmSection(0x0020, 0, 1,
            TrRmVariableTableWriter.EncodeBodies([body])));
        var artifact = TrRmArtifactWriter.Encode(sections);
        TrRmStructuralMetadataValidator.Validate(artifact);
        if (floating)
        {
            _ = LoadStandardFloatingScalarFunction(artifact);
        }
        else
        {
            _ = LoadStandardIntegerScalarFunction(artifact);
        }
        return artifact;
    }

    private static byte StandardIntegerScalarTypeCode(PrimitiveType type)
    {
        var index = Array.IndexOf(s_types, type);
        if (!PrimitiveTypeFacts.IsIntegerOrWide(type)
            || index <= 0 || index >= s_codes.Length)
        {
            throw Unsupported($"Standard integer Body emission does not support {type}.");
        }

        return s_codes[index];
    }

    private static bool IsStandardScalarOperationFunction(BoundFunction function)
    {
        if (function.IsExternal || function.DirectNativeImport is not null
            || function.NativeOutputRole is not null
            || !function.NativeOutputContracts.IsEmpty
            || function.ModuleImport is not null || function.IsModuleReference
            || function.NativeFunctionPointerType is not null
            || function.NativeCallableEntryName is not null
            || function.NativeVTableMethod is not null || function.IndexedBody is not null
            || function.PropertyName is not null || function.IsCleanup || function.NoFailureProof
            || function.RequiresUnsafeCaller || function.DoesNotReturn || function.IsConstructor
            || function.DeclaringStruct is not null || function.IsStatic || function.IsOperator
            || function.Intrinsic != BoundIntrinsic.None || function.IsClassConstructor
            || function.ClassConstructor != -1 || !function.CapturedParameters.IsEmpty
            || function.ReturnsForwardedBacking || !function.ResultDependencyParameters.IsEmpty
            || function.ResultDependencyIncludesReceiver || !function.DependencyContracts.IsEmpty
            || !function.Body.RetiredManagedLocals.IsEmpty
            || function.Body.Statements.Length != 1
            || function.Body.Statements[0] is not BoundReturn { Value: not null })
        {
            return false;
        }

        foreach (var variable in function.Variables)
        {
            if (variable.Type != function.ReturnType || variable.IsMutable
                || variable.IsOut || variable.IsInit || variable.IsImplicitRef
                || variable.IsImplicitReferenceTemporary || variable.IsInitializationLocal
                || NativeFixedArrayTypes.IsType(variable.Type))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsScalarFrozenValuesDraftConstantFunction(BoundFunction function)
    {
        if (function.IsExternal || function.DirectNativeImport is not null
            || function.NativeOutputRole is not null
            || !function.NativeOutputContracts.IsEmpty
            || function.ModuleImport is not null || function.IsModuleReference
            || function.NativeFunctionPointerType is not null
            || function.NativeCallableEntryName is not null
            || function.NativeVTableMethod is not null || function.IndexedBody is not null
            || function.PropertyName is not null || function.IsCleanup || function.NoFailureProof
            || function.RequiresUnsafeCaller || function.DoesNotReturn || function.IsConstructor
            || function.DeclaringStruct is not null || function.IsStatic || function.IsOperator
            || function.Intrinsic != BoundIntrinsic.None || function.IsClassConstructor
            || function.ClassConstructor != -1 || !function.CapturedParameters.IsEmpty
            || function.ReturnsForwardedBacking || !function.ResultDependencyParameters.IsEmpty
            || function.ResultDependencyIncludesReceiver || !function.DependencyContracts.IsEmpty
            || !function.Body.RetiredManagedLocals.IsEmpty || function.ParameterCount != 0
            || !function.Variables.IsEmpty || function.Body.Statements.Length != 1
            || function.Body.Statements[0] is not BoundReturn { Value: BoundConstant })
        {
            return false;
        }

        return true;
    }

    private static byte[] EncodeStandardScalarOperationBody(
        BoundFunction function, bool floating)
    {
        var returned = ((BoundReturn)function.Body.Statements[0]).Value!;
        var code = new byte[8];
        code[0] = 0x11;
        code[1] = 0;
        code[2] = 0x11;
        code[3] = 0x11;

        byte resultRegister;
        if (returned is BoundUnary
            {
                Operator: TokenKind.Minus,
                Operand: BoundRead operand,
            } unary && unary.Type == function.ReturnType
            && (floating
                ? IsFloat(unary.Type)
                    && unary.OverflowBehavior == BoundIntegerOverflowBehavior.Checked
                : PrimitiveTypeFacts.IsIntegerOrWide(unary.Type)
                    && unary.OverflowBehavior == BoundIntegerOverflowBehavior.Wrapping))
        {
            resultRegister = GetParameterRegister(operand, function);
            code[4] = floating ? (byte)0x26 : (byte)0x20;
            code[5] = (byte)((resultRegister << 4) | resultRegister);
        }
        else if (returned is BoundBinary binary
            && binary.Type == function.ReturnType
            && (floating
                ? IsFloat(binary.Type)
                    && binary.OverflowBehavior == BoundIntegerOverflowBehavior.Checked
                    && binary.Operator is TokenKind.Plus or TokenKind.Minus
                        or TokenKind.Star or TokenKind.Slash or TokenKind.Percent
                : PrimitiveTypeFacts.IsIntegerOrWide(binary.Type)
                    && binary.Operator is TokenKind.Plus or TokenKind.Minus
                        or TokenKind.Star or TokenKind.Slash or TokenKind.Percent
                    && (binary.Operator is TokenKind.Plus or TokenKind.Minus or TokenKind.Star
                        ? binary.OverflowBehavior == BoundIntegerOverflowBehavior.Wrapping
                        : binary.OverflowBehavior == BoundIntegerOverflowBehavior.Checked)))
        {
            var left = GetParameterRegister(binary.Left, function);
            var right = GetParameterRegister(binary.Right, function);
            resultRegister = left;
            code[4] = floating ? binary.Operator switch
            {
                TokenKind.Plus => (byte)0x27,
                TokenKind.Minus => (byte)0x28,
                TokenKind.Star => (byte)0x29,
                TokenKind.Slash => (byte)0x2A,
                _ => (byte)0x2B,
            } : binary.Operator switch
            {
                TokenKind.Plus => (byte)0x21,
                TokenKind.Minus => (byte)0x22,
                TokenKind.Star => (byte)0x23,
                TokenKind.Slash => (byte)0x24,
                _ => (byte)0x25,
            };
            code[5] = (byte)((right << 4) | left);
        }
        else
        {
            throw Unsupported("Standard scalar-operation Body emission supports one direct, type-correct unary or binary operation.");
        }

        code[6] = 0x17;
        code[7] = resultRegister;
        var entryState = new byte[8];
        entryState[0] = 0x01;
        return TrRmRegisterBodyWriter.Encode(default, default, default, default,
            default, [new TrRmRegisterBodyBlock(0, (uint)code.Length, 0)],
            [entryState], code);
    }

    private static byte GetParameterRegister(BoundExpression expression, BoundFunction function)
    {
        if (expression is not BoundRead read || read.Type != function.ReturnType
            || (uint)read.Variable >= (uint)function.ParameterCount)
        {
            throw Unsupported("Standard scalar-operation Body emission requires parameter-only operands of the result type.");
        }

        return checked((byte)read.Variable);
    }
}
