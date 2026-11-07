// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    internal static byte[] EmitNominalEnumsDraftSource(
        BoundProgram program, string moduleIdentity,
        uint moduleTargetRequirement)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        if (program.Functions.IsEmpty)
        {
            return EmitNominalEnumsDraftDeclarationLibrary(program,
                moduleIdentity, moduleTargetRequirement);
        }

        return IsNominalEnumsDraftIdentityLibrary(program)
            ? EmitNominalEnumsDraftIdentityLibrary(program, moduleIdentity,
                moduleTargetRequirement)
            : EmitNominalEnumsDraftConstantReturns(program, moduleIdentity,
                moduleTargetRequirement);
    }

    private static bool IsNominalEnumsDraftIdentityLibrary(
        BoundProgram program)
        => !program.EnumDefinitions.IsEmpty
            && !program.Functions.IsEmpty
            && program.Functions.All(function =>
                IsStandardScalarOperationFunction(function)
                && function.ParameterCount == 1
                && function.Variables.Length == 1
                && BoundEnumTypes.IsValidType(function.ReturnType,
                    program.EnumDefinitions)
                && function.Variables[0].Type == function.ReturnType
                && function.Body.Statements[0]
                    is BoundReturn { Value: BoundRead { Variable: 0 } });

    private static byte[] EmitNominalEnumsDraftIdentityLibrary(
        BoundProgram program, string moduleIdentity,
        uint moduleTargetRequirement)
    {
        if (program.EntryPoint != -1
            || program.RuntimePolicy != BoundRuntimePolicy.FailFast
            || !BoundEnumTypes.IsValidTable(program.EnumDefinitions)
            || moduleTargetRequirement > 1
            || !program.Structures.IsEmpty || !program.Classes.IsEmpty
            || !program.Buffers.IsEmpty
            || !program.NativeFunctionPointerSignatures.IsEmpty
            || !program.NativePointerTypes.IsEmpty
            || !program.NativeUtf16ZLiterals.IsEmpty
            || !program.NativeUtf8ZLiterals.IsEmpty
            || program.RequiresSourceGrant
            || program.InvocationProfileId is not null
            || program.IsInvocationSourceBound
            || !IsNominalEnumsDraftIdentityLibrary(program)
            || program.Functions.Any(static function =>
                function.IsPrivate && function.IsInternal))
        {
            throw Unsupported(
                "NominalEnumsDraft identity emission requires a fail-fast enum library of single-parameter identity methods.");
        }

        SemanticVerifier.VerifyLibrary(program);
        var coreRegistration = program.NativeCoreRegistration;
        if (coreRegistration is not null
            && (!coreRegistration.IsValid()
                || !BoundEnumTypes.ContainsRegistration(
                    program.EnumDefinitions, coreRegistration)
                || !StringComparer.Ordinal.Equals(
                    coreRegistration.UsingModuleIdentity, moduleIdentity)))
        {
            throw Unsupported(
                "NominalEnumsDraft identity emission requires the selected using-Module identity.");
        }

        var declarationProgram = new BoundProgram([], -1)
        {
            EnumDefinitions = program.EnumDefinitions,
            NativeCoreRegistration = coreRegistration,
            LayoutCoreRegistration = program.LayoutCoreRegistration,
            RuntimePolicy = program.RuntimePolicy,
        };
        var declarationArtifact = EmitNominalEnumsDraftDeclarationLibrary(
            declarationProgram, moduleIdentity, moduleTargetRequirement);
        var declarationIndex
            = TrRmArtifactIndex.ForNominalEnumsDraft(declarationArtifact);
        var declarationTables
            = TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForNominalEnumsDraft(declarationArtifact));
        var importRows = ReadImportRows(declarationIndex);
        var importedTypeDefs = new Dictionary<uint, int>();
        for (var index = 0; index < importRows.Count; index++)
        {
            importedTypeDefs.Add(importRows[index].ExpectedLocalDefId,
                index + 1);
        }

        var typeSpecRows = new List<ReadOnlyMemory<byte>>(
            program.EnumDefinitions.Length);
        for (var enumIndex = 0;
            enumIndex < program.EnumDefinitions.Length; enumIndex++)
        {
            var typeDefRow = enumIndex + 1;
            var expectedDefId = 0x1300_0000u | (uint)typeDefRow;
            typeSpecRows.Add(importedTypeDefs.TryGetValue(
                    expectedDefId, out var importRow)
                ? new ReadOnlyMemory<byte>(
                    TrRmTypeSpecTableWriter.Reference(0x03,
                        checked((uint)importRow)))
                : new ReadOnlyMemory<byte>(
                    TrRmTypeSpecTableWriter.Reference(0x02,
                        checked((uint)typeDefRow))));
        }

        var stringRows = new List<(string Text, int Encoding)>(
            declarationTables.Strings.Count + (program.Functions.Length * 2));
        var stringRowsByText = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var row = 1; row <= declarationTables.Strings.Count; row++)
        {
            var text = declarationTables.Strings.DecodeNamespace(row);
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

        var methodRows = new List<TrRmMethodDefRow>(program.Functions.Length);
        var signatureRows = new List<TrRmMethodSigRow>(
            program.Functions.Length);
        var parameterRows = new List<TrRmParamRow>(program.Functions.Length);
        var exports = ReadExportRows(declarationIndex);
        var bodies = new List<ReadOnlyMemory<byte>>(program.Functions.Length);
        for (var index = 0; index < program.Functions.Length; index++)
        {
            var function = program.Functions[index];
            var methodRow = index + 1;
            var nameRow = GetStringRow(function.Name);
            var typeSpecRef = checked((uint)(
                BoundEnumTypes.Index(function.ReturnType) + 1));
            signatureRows.Add(new TrRmMethodSigRow(typeSpecRef, 0,
                methodRow, 1, 0, 0));
            parameterRows.Add(new TrRmParamRow(methodRow, 0,
                typeSpecRef, 0));
            var visibility = function.IsPrivate ? 0u
                : function.IsInternal ? 1u : 2u;
            methodRows.Add(new TrRmMethodDefRow(0, nameRow,
                methodRow, visibility, 0, 0, methodRow));
            if (visibility == 2)
            {
                exports.Add(new TrRmExportRow(
                    0x1600_0000u | checked((uint)methodRow), nameRow));
            }

            var homeMap = new byte[8];
            homeMap[0] = 0x02;
            var typeMap = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(typeMap,
                0x1100_0000u | typeSpecRef);
            var entryState = new byte[8];
            entryState[0] = 0x01;
            bodies.Add(TrRmRegisterBodyWriter.Encode(homeMap, typeMap,
                default, default, default,
                [new TrRmRegisterBodyBlock(0, 4, 0)],
                [new ReadOnlyMemory<byte>(entryState)],
                new byte[] { 0x13, 0x00, 0x17, 0x00 }));
        }

        var stringBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var strings = new TrRmStringTableReader(stringBytes, stringRows.Count);
        var featureBytes = GetRequiredSection(declarationIndex, 0x0004);
        var featureRows = new List<TrRmFeatureRow>(
            declarationTables.Features.Count);
        for (var row = 1; row <= declarationTables.Features.Count; row++)
        {
            featureRows.Add(declarationTables.Features.GetFeature(row));
        }

        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    GetRequiredSection(declarationIndex, 0x0001))),
                moduleTargetRequirement, 0, 0, 1, 1, 1, 1,
                featureRows.Count == 0 ? 0 : 1, featureRows.Count),
        ], strings, declarationTables.ModuleRefs.Count,
            declarationTables.TypeDefs.Count, methodRows.Count, 0,
            featureRows.Count);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            declarationTables.ModuleRefs.Count, declarationTables.TypeDefs.Count,
            methodRows.Count, 0, featureRows.Count);
        var moduleRefBytes = GetOptionalSection(declarationIndex, 0x0010);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes,
            declarationTables.ModuleRefs.Count, strings, modules);
        var features = new TrRmFeatureTableReader(featureBytes,
            featureRows.Count, TrRmArtifactSchema.NominalEnumsDraft);
        var typeDefBytes = GetRequiredSection(declarationIndex, 0x0013);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes,
            declarationTables.TypeDefs.Count, strings, modules,
            fieldCount: 0, genericParamCount: 0,
            layoutCount: declarationTables.Layouts.Count, contractCount: 0,
            TrRmArtifactSchema.NominalEnumsDraft);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            typeDefs.Count, importRows.Count, genericParamCount: 0,
            nativeFunctionPointerSignatureCount: 0,
            TrRmArtifactSchema.NominalEnumsDraft);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, typeDefs.Count, importRows.Count,
            genericParamCount: 0, nativeFunctionPointerSignatureCount: 0,
            TrRmArtifactSchema.NominalEnumsDraft);
        var targetProfileBytes = GetOptionalSection(declarationIndex, 0x0048);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfileBytes, declarationIndex.TryGetSection(0x0048,
                out _, out var targetProfileCount) ? targetProfileCount : 0,
            TrRmArtifactSchema.NominalEnumsDraft);
        var layouts = new TrRmLayoutTableReader(
            GetOptionalSection(declarationIndex, 0x0015),
            declarationTables.Layouts.Count, typeDefs, targetProfiles,
            layoutFieldCount: 0);
        var enumMembers = new TrRmEnumMemberDefTableReader(
            GetOptionalSection(declarationIndex, 0x0054),
            declarationTables.EnumMembers.Count, strings, typeDefs);
        _ = new TrRmEnumDefTableReader(
            GetRequiredSection(declarationIndex, 0x0053),
            declarationTables.EnumDefs.Count, typeDefs, typeSpecs, layouts,
            targetProfiles, enumMembers);
        var signatureBytes = TrRmMethodSigTableWriter.Encode(signatureRows,
            typeSpecs, parameterRows.Count, genericParamCount: 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes,
            signatureRows.Count, typeSpecs, parameterRows.Count,
            genericParamCount: 0);
        var paramBytes = TrRmParamTableWriter.Encode(parameterRows,
            signatures, typeSpecs, TrRmArtifactSchema.NominalEnumsDraft);
        var methodsBytes = TrRmMethodDefTableWriter.Encode(methodRows, strings,
            modules, typeDefs, signatures, contractCount: 0,
            bodyCount: bodies.Count);
        var methods = new TrRmMethodDefTableReader(methodsBytes,
            methodRows.Count, strings, modules, typeDefs, signatures,
            contractCount: 0, bodyCount: bodies.Count);
        var importBytes = importRows.Count == 0
            ? []
            : TrRmImportTableWriter.Encode(importRows, strings, moduleRefs,
                typeDefs, methods, default, default, default, default,
                features, modules, TrRmArtifactSchema.NominalEnumsDraft);
        var imports = new TrRmImportTableReader(importBytes, importRows.Count,
            strings, moduleRefs, typeDefs, methods, default,
            default, default, default, features, modules,
            TrRmArtifactSchema.NominalEnumsDraft);
        var exportBytes = exports.Count == 0
            ? []
            : TrRmExportTableWriter.Encode(exports, strings, typeDefs,
                methods, default, imports, default, default, default,
                features, modules, TrRmArtifactSchema.NominalEnumsDraft);
        var sections = new List<TrRmSection>(
            declarationIndex.SectionCount + 7);
        for (var section = 0; section < declarationIndex.SectionCount; section++)
        {
            declarationIndex.GetSection(section, out var kind, out var flags,
                out var bytes, out var count);
            if (kind is 0x0001 or 0x0002 or 0x0004 or 0x0011
                or 0x0016 or 0x0017 or 0x0018 or 0x001C or 0x001D
                or 0x0020)
            {
                continue;
            }

            sections.Add(new TrRmSection(kind, flags,
                checked((uint)count), bytes.ToArray()));
        }

        sections.Add(new TrRmSection(0x0001, 0, 1, moduleBytes));
        sections.Add(new TrRmSection(0x0002, 0,
            checked((uint)stringRows.Count), stringBytes));
        sections.Add(new TrRmSection(0x0004, 0,
            checked((uint)featureRows.Count), featureBytes));
        sections.Add(new TrRmSection(0x0011, 0,
            checked((uint)typeSpecRows.Count), typeSpecBytes));
        sections.Add(new TrRmSection(0x0016, 0,
            checked((uint)methodRows.Count), methodsBytes));
        sections.Add(new TrRmSection(0x0017, 0,
            checked((uint)signatureRows.Count), signatureBytes));
        sections.Add(new TrRmSection(0x0018, 0,
            checked((uint)parameterRows.Count), paramBytes));
        if (importRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001C, 0,
                checked((uint)importRows.Count), importBytes));
        }

        if (exports.Count != 0)
        {
            sections.Add(new TrRmSection(0x001D, 0,
                checked((uint)exports.Count), exportBytes));
        }

        sections.Add(new TrRmSection(0x0020, 0,
            checked((uint)bodies.Count),
            TrRmVariableTableWriter.EncodeBodies(bodies)));
        sections.Sort(static (left, right)
            => left.Kind.CompareTo(right.Kind));
        var artifact = TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.NominalEnumsDraft);
        _ = TrRmStructuralMetadataValidator
            .ValidateNominalEnumsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForNominalEnumsDraft(artifact));
        return artifact;
    }

    internal static byte[] EmitNominalEnumsDraftConstantReturns(
        BoundProgram program, string moduleIdentity,
        uint moduleTargetRequirement)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        if (program.EntryPoint != -1 || program.Functions.IsDefaultOrEmpty
            || program.RuntimePolicy != BoundRuntimePolicy.FailFast
            || !BoundEnumTypes.IsValidTable(program.EnumDefinitions)
            || moduleTargetRequirement > 1
            || !program.Structures.IsEmpty || !program.Classes.IsEmpty
            || !program.Buffers.IsEmpty
            || !program.NativeFunctionPointerSignatures.IsEmpty
            || !program.NativePointerTypes.IsEmpty
            || !program.NativeUtf16ZLiterals.IsEmpty
            || !program.NativeUtf8ZLiterals.IsEmpty
            || program.RequiresSourceGrant
            || program.InvocationProfileId is not null
            || program.IsInvocationSourceBound
            || program.Functions.Any(static function =>
                !IsScalarFrozenValuesDraftConstantFunction(function)
                || (function.IsPrivate && function.IsInternal)))
        {
            throw Unsupported(
                "NominalEnumsDraft constant-return emission requires a fail-fast enum library of parameterless constant-return methods.");
        }

        SemanticVerifier.VerifyLibrary(program);
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
                "NominalEnumsDraft constant-return emission requires the selected using-Module identity.");
        }

        var declarationProgram = new BoundProgram([], -1)
        {
            EnumDefinitions = program.EnumDefinitions,
            NativeCoreRegistration = coreRegistration,
            LayoutCoreRegistration = program.LayoutCoreRegistration,
            RuntimePolicy = program.RuntimePolicy,
        };
        var declarationArtifact = EmitNominalEnumsDraftDeclarationLibrary(
            declarationProgram, moduleIdentity, moduleTargetRequirement);
        var declarationIndex
            = TrRmArtifactIndex.ForNominalEnumsDraft(declarationArtifact);
        var declarationTables
            = TrRmStructuralMetadataValidator
                .ValidateNominalEnumsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForNominalEnumsDraft(declarationArtifact));

        var stringRows = new List<(string Text, int Encoding)>(
            declarationTables.Strings.Count + program.Functions.Length);
        var stringRowsByText = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var row = 1; row <= declarationTables.Strings.Count; row++)
        {
            var text = declarationTables.Strings.DecodeNamespace(row);
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

        var importRows = ReadImportRows(declarationIndex);
        var importedTypeDefs = new Dictionary<uint, int>();
        for (var index = 0; index < importRows.Count; index++)
        {
            importedTypeDefs.Add(importRows[index].ExpectedLocalDefId,
                index + 1);
        }

        var typeSpecRows = new List<ReadOnlyMemory<byte>>(
            program.EnumDefinitions.Length);
        for (var enumIndex = 0;
            enumIndex < program.EnumDefinitions.Length; enumIndex++)
        {
            var typeDefRow = enumIndex + 1;
            var expectedDefId = 0x1300_0000u | (uint)typeDefRow;
            typeSpecRows.Add(importedTypeDefs.TryGetValue(
                    expectedDefId, out var importRow)
                ? new ReadOnlyMemory<byte>(
                    TrRmTypeSpecTableWriter.Reference(0x03,
                        checked((uint)importRow)))
                : new ReadOnlyMemory<byte>(
                    TrRmTypeSpecTableWriter.Reference(0x02,
                        checked((uint)typeDefRow))));
        }

        var featureRows = new List<TrRmFeatureRow>(
            declarationTables.Features.Count + 1);
        for (var row = 1; row <= declarationTables.Features.Count; row++)
        {
            featureRows.Add(declarationTables.Features.GetFeature(row));
        }
        if (!featureRows.Contains(TrRmFeatureRow.TypedFrozenValues))
        {
            featureRows.Add(TrRmFeatureRow.TypedFrozenValues);
        }
        featureRows.Sort(static (left, right)
            => left.FeatureId.CompareTo(right.FeatureId));

        var scalarValues = new TrRmFrozenScalarValueBuilder();
        var methodRows = new List<TrRmMethodDefRow>(program.Functions.Length);
        var signatureRows = new List<TrRmMethodSigRow>(program.Functions.Length);
        var bodies = new List<ReadOnlyMemory<byte>>(program.Functions.Length);
        var exports = ReadExportRows(declarationIndex);
        var nameRows = new int[program.Functions.Length];
        for (var index = 0; index < program.Functions.Length; index++)
        {
            var function = program.Functions[index];
            var returned = (BoundConstant)((BoundReturn)
                function.Body.Statements[0]).Value!;
            if (returned.Type != function.ReturnType
                || returned.Type == PrimitiveType.Void)
            {
                throw Unsupported(
                    "NominalEnumsDraft constant-return methods require matching non-unit results.");
            }

            var valueReference = BoundEnumTypes.IsType(returned.Type)
                ? scalarValues.AddNominalEnum(returned,
                    checked((uint)BoundEnumTypes.Index(returned.Type) + 1))
                : scalarValues.Add(returned);
            var constantMap = new byte[2 * sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(constantMap,
                valueReference.TypeSpecRef);
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(sizeof(uint)),
                valueReference.ConstantSourceRow);
            var entryState = new byte[8];
            entryState[0] = 0x01;
            bodies.Add(TrRmRegisterBodyWriter.Encode(default, default,
                default, default, constantMap,
                [new TrRmRegisterBodyBlock(0, 2, 0)],
                [new ReadOnlyMemory<byte>(entryState)], new byte[] { 0x09, 0x00 },
                schema: TrRmArtifactSchema.NominalEnumsDraft));

            var methodRow = index + 1;
            nameRows[index] = GetStringRow(function.Name);
            signatureRows.Add(new TrRmMethodSigRow(
                valueReference.TypeSpecRef, 0, 0, 0, 0, 0));
            var visibility = function.IsPrivate ? 0u
                : function.IsInternal ? 1u : 2u;
            methodRows.Add(new TrRmMethodDefRow(0, nameRows[index],
                methodRow, visibility, 0, 0, methodRow));
            if (visibility == 2)
            {
                exports.Add(new TrRmExportRow(
                    0x1600_0000u | checked((uint)methodRow),
                    nameRows[index]));
            }
        }

        var stringBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var strings = new TrRmStringTableReader(stringBytes, stringRows.Count);
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows,
            TrRmArtifactSchema.NominalEnumsDraft);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    GetRequiredSection(declarationIndex, 0x0001))),
                moduleTargetRequirement, 0, 0, 1, 1, 1, 1,
                1, featureRows.Count),
        ], strings, declarationTables.ModuleRefs.Count,
            declarationTables.TypeDefs.Count, methodRows.Count, 0,
            featureRows.Count);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            declarationTables.ModuleRefs.Count, declarationTables.TypeDefs.Count,
            methodRows.Count, 0, featureRows.Count);
        var moduleRefBytes = GetOptionalSection(declarationIndex, 0x0010);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes,
            declarationTables.ModuleRefs.Count, strings, modules);
        var features = new TrRmFeatureTableReader(featureBytes,
            featureRows.Count, TrRmArtifactSchema.NominalEnumsDraft);
        var typeDefBytes = GetOptionalSection(declarationIndex, 0x0013);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes,
            declarationTables.TypeDefs.Count, strings, modules,
            fieldCount: 0, genericParamCount: 0,
            layoutCount: declarationTables.Layouts.Count, contractCount: 0,
            TrRmArtifactSchema.NominalEnumsDraft);
        var typeSpecBytes = typeSpecRows.Count == 0
            ? []
            : TrRmTypeSpecTableWriter.Encode(typeSpecRows,
                typeDefs.Count, importRows.Count, genericParamCount: 0,
                nativeFunctionPointerSignatureCount: 0,
                TrRmArtifactSchema.NominalEnumsDraft);
        var typeSpecs = typeSpecRows.Count == 0
            ? default
            : new TrRmTypeSpecTableReader(typeSpecBytes,
                typeSpecRows.Count, typeDefs.Count, importRows.Count,
                genericParamCount: 0, nativeFunctionPointerSignatureCount: 0,
                TrRmArtifactSchema.NominalEnumsDraft);
        var targetProfileBytes = GetOptionalSection(declarationIndex, 0x0048);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfileBytes, declarationIndex.TryGetSection(0x0048,
                out _, out var targetProfileCount) ? targetProfileCount : 0,
            TrRmArtifactSchema.NominalEnumsDraft);
        var layoutBytes = GetOptionalSection(declarationIndex, 0x0015);
        var layouts = new TrRmLayoutTableReader(layoutBytes,
            declarationTables.Layouts.Count, typeDefs, targetProfiles,
            layoutFieldCount: 0);
        var enumMemberBytes = GetOptionalSection(declarationIndex, 0x0054);
        var enumMembers = new TrRmEnumMemberDefTableReader(enumMemberBytes,
            declarationTables.EnumMembers.Count, strings, typeDefs);
        var enumDefBytes = GetOptionalSection(declarationIndex, 0x0053);
        var enumDefs = new TrRmEnumDefTableReader(enumDefBytes,
            declarationTables.EnumDefs.Count, typeDefs, typeSpecs, layouts,
            targetProfiles, enumMembers);
        var methodSignatureBytes = TrRmMethodSigTableWriter.Encode(
            signatureRows, typeSpecs, paramCount: 0, genericParamCount: 0);
        var signatures = new TrRmMethodSigTableReader(methodSignatureBytes,
            signatureRows.Count, typeSpecs, paramCount: 0,
            genericParamCount: 0);
        var typeDefsForMethods = typeDefs;
        var methodBytes = TrRmMethodDefTableWriter.Encode(methodRows, strings,
            modules, typeDefsForMethods, signatures, contractCount: 0,
            bodyCount: bodies.Count);
        var methods = new TrRmMethodDefTableReader(methodBytes,
            methodRows.Count, strings, modules, typeDefsForMethods,
            signatures, contractCount: 0, bodyCount: bodies.Count);
        var moduleRefTableBytes = GetOptionalSection(declarationIndex, 0x0010);
        var validatedModuleRefs = new TrRmModuleRefTableReader(
            moduleRefTableBytes, declarationTables.ModuleRefs.Count, strings,
            modules);
        var importBytes = importRows.Count == 0
            ? []
            : TrRmImportTableWriter.Encode(importRows, strings,
                validatedModuleRefs, typeDefs, methods, default,
                default, default, default, features, modules,
                TrRmArtifactSchema.NominalEnumsDraft);
        var imports = new TrRmImportTableReader(importBytes, importRows.Count,
            strings, validatedModuleRefs, typeDefs, methods, default,
            default, default, default, features, modules,
            TrRmArtifactSchema.NominalEnumsDraft);
        var exportBytes = exports.Count == 0
            ? []
            : TrRmExportTableWriter.Encode(exports, strings, typeDefs,
                methods, default, imports, default, default, default,
                features, modules, TrRmArtifactSchema.NominalEnumsDraft);
        var values = scalarValues.EncodeNominalEnumsDraft(typeSpecs,
            typeDefs, enumDefs, imports);
        var sections = new List<TrRmSection>(
            declarationIndex.SectionCount + 8);
        for (var section = 0; section < declarationIndex.SectionCount; section++)
        {
            declarationIndex.GetSection(section, out var kind, out var flags,
                out var bytes, out var count);
            if (kind is 0x0001 or 0x0002 or 0x0004 or 0x001C or 0x001D)
            {
                continue;
            }

            sections.Add(new TrRmSection(kind, flags,
                checked((uint)count), bytes.ToArray()));
        }
        sections.Add(new TrRmSection(0x0001, 0, 1, moduleBytes));
        sections.Add(new TrRmSection(0x0002, 0,
            checked((uint)stringRows.Count), stringBytes));
        sections.Add(new TrRmSection(0x0004, 0,
            checked((uint)featureRows.Count), featureBytes));
        if (typeSpecRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0011, 0,
                checked((uint)typeSpecRows.Count), typeSpecBytes));
        }
        sections.Add(new TrRmSection(0x0016, 0,
            checked((uint)methodRows.Count), methodBytes));
        sections.Add(new TrRmSection(0x0017, 0,
            checked((uint)signatureRows.Count), methodSignatureBytes));
        if (importRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001C, 0,
                checked((uint)importRows.Count), importBytes));
        }
        if (exports.Count != 0)
        {
            sections.Add(new TrRmSection(0x001D, 0,
                checked((uint)exports.Count), exportBytes));
        }
        if (values.BlobCount != 0)
        {
            sections.Add(new TrRmSection(0x0003, 0,
                checked((uint)values.BlobCount), values.BlobSection));
        }
        sections.Add(new TrRmSection(0x0020, 0,
            checked((uint)bodies.Count),
            TrRmVariableTableWriter.EncodeBodies(bodies)));
        sections.Add(new TrRmSection(0x0030, 0,
            checked((uint)values.ConstantSourceCount),
            values.ConstantSourceSection));
        sections.Add(new TrRmSection(0x003F, 0,
            checked((uint)values.FrozenValueNodeCount),
            values.FrozenValueNodeSection));
        sections.Sort(static (left, right)
            => left.Kind.CompareTo(right.Kind));
        var artifact = TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.NominalEnumsDraft);
        _ = TrRmStructuralMetadataValidator
            .ValidateNominalEnumsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForNominalEnumsDraft(artifact));
        return artifact;
    }

    private static List<TrRmImportRow> ReadImportRows(
        TrRmArtifactIndex index)
    {
        if (!index.TryGetSection(0x001C, out var bytes, out var count))
        {
            return [];
        }

        var rows = new List<TrRmImportRow>(count);
        for (var row = 0; row < count; row++)
        {
            var data = bytes.Slice(row * 12, 12);
            rows.Add(new TrRmImportRow(
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data)),
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[4..])),
                BinaryPrimitives.ReadUInt32LittleEndian(data[8..])));
        }

        return rows;
    }

    private static List<TrRmExportRow> ReadExportRows(
        TrRmArtifactIndex index)
    {
        if (!index.TryGetSection(0x001D, out var bytes, out var count))
        {
            return [];
        }

        var rows = new List<TrRmExportRow>(count);
        for (var row = 0; row < count; row++)
        {
            var data = bytes.Slice(row * 8, 8);
            rows.Add(new TrRmExportRow(
                BinaryPrimitives.ReadUInt32LittleEndian(data),
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[4..]))));
        }

        return rows;
    }

    private static byte[] GetRequiredSection(TrRmArtifactIndex index,
        ushort kind)
    {
        if (!index.TryGetSection(kind, out var bytes, out _))
        {
            throw Unsupported($"NominalEnumsDraft requires section 0x{kind:X4}.");
        }

        return bytes.ToArray();
    }

    private static byte[] GetOptionalSection(TrRmArtifactIndex index,
        ushort kind)
        => index.TryGetSection(kind, out var bytes, out _)
            ? bytes.ToArray() : [];
}
