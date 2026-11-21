// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private static byte[] EmitForeignNativeInterfaceDeclarations(
        BoundProgram program, string moduleIdentity, string schemaIdentity,
        uint moduleTargetRequirement,
        IReadOnlyList<TrRmSourceTypeExport>? sourceTypeExports)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        var schema = GetForeignInvocationSchema(schemaIdentity);
        if (!schema.UsesForeignNativeInterface()
            || moduleTargetRequirement
                != TrRmModuleTableReader.WindowsX64NativeTargetRequirement
            || program.EntryPoint >= 0
            || !program.Functions.IsEmpty
            || !program.Structures.IsEmpty
            || !program.Classes.IsEmpty
            || !program.Buffers.IsEmpty
            || !program.NativeFixedArrayTypes.IsEmpty
            || !program.GenericMethodDefinitions.IsEmpty
            || !program.GenericMethodSignatures.IsEmpty
            || !program.GenericMethodOperatorProtocols.IsEmpty
            || !program.GenericMethodTypeFormalOwners.IsEmpty
            || !program.RetainedGenericMethodRoots.IsEmpty
            || !program.SourceGenericTypeDefinitions.IsEmpty
            || !program.SourceGenericTypeInstances.IsEmpty
            || !program.GenericNominalTypeDefinitions.IsEmpty
            || !program.GenericNominalTypeInstances.IsEmpty
            || !program.SpanTypes.IsEmpty
            || !program.CheckedReferenceTypes.IsEmpty
            || !program.PrivateNominalTypeOrigins.IsEmpty
            || !program.PrivateClassReferences.IsEmpty
            || !program.StandardClassDeclarations.IsEmpty
            || !program.InterfaceWitnesses.IsEmpty
            || !program.EnumDefinitions.IsEmpty
            || !ForeignNativeInterfaceContract.IsValidTable(program))
        {
            throw Unsupported(
                "The current F+I source producer requires a declaration-only native interface library.");
        }

        var localInterfaceCount = program.Interfaces.Count(
            static definition => definition.ModuleImport is null);
        if (program.Interfaces.IsDefaultOrEmpty
            || (sourceTypeExports?.Count ?? 0) != localInterfaceCount
            || program.Interfaces.Any(static definition =>
                definition.ForeignNativeInterface is null))
        {
            throw Unsupported(
                "The F+I declaration library must contain only local or imported ForeignNativeInterface definitions.");
        }

        if (!schema.UsesForeignNativeInterfaceParameterSurface()
            && program.Interfaces.Any(static definition =>
                definition.Members.Any(static member =>
                    !member.Parameters.IsEmpty)))
        {
            throw Unsupported(
                "Parameterized ForeignNativeInterface members require the exact F+I+SF1+PS1 schema.");
        }

        SemanticVerifier.Verify(program);
        var hasNativeOutputs = program.Interfaces.Any(static definition =>
            definition.Members.Any(static member =>
                !member.NativeOutputContracts.IsDefaultOrEmpty));
        var hasFunctionPointerPredicates
            = GetForeignNativeFunctionPointerSignatureIndexes(
                program.Interfaces, program).Any(index =>
                !program.NativeFunctionPointerSignatures[index]
                    .Selectors.IsEmpty);
        var hasScalarDefaults = schema
            .UsesForeignNativeInterfaceParameterSurface()
            && program.Interfaces.Any(static definition =>
                definition.Members.Any(static member =>
                    member.Parameters.Any(static parameter =>
                        parameter.DefaultValue is not null)));
        var featureRows = new List<TrRmFeatureRow> {
            TrRmFeatureRow.NativeBoundaryContracts,
            TrRmFeatureRow.RootlessNativeValues,
            TrRmFeatureRow.NativeInterfaceCalls,
        };
        if (hasNativeOutputs)
        {
            featureRows.Add(TrRmFeatureRow.NativeOutputCellsCandidate);
            featureRows.Add(TrRmFeatureRow.PublishedPredicates);
        }
        else if (hasFunctionPointerPredicates)
        {
            featureRows.Add(TrRmFeatureRow.PublishedPredicates);
        }
        if (hasScalarDefaults)
        {
            featureRows.Add(TrRmFeatureRow.TypedFrozenValues);
        }
        if (schema.UsesForeignNativeInterfaceParameterSurface())
        {
            featureRows.Add(TrRmFeatureRow.MethodParameterLabelsCandidate);
        }

        var moduleNameRows = TrRmStringTableWriter.EncodeTextRows([
            (moduleIdentity, 0),
        ]);
        var strings = new TrRmStringTableReader(moduleNameRows, 1);
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1,
                moduleTargetRequirement, 0, 0, 1, 1, 1, 1, 1,
                featureRows.Count),
        ], strings, 0, 0, 0, 0, featureRows.Count);
        var targetProfileBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader
                    .WindowsX64NativeProfileRuleRevision),
        ]);
        var sections = new List<TrRmSection> {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, 1, moduleNameRows),
            new(0x0004, 0, checked((uint)featureRows.Count), featureBytes),
            new(0x0048, 0, 1, targetProfileBytes),
        };
        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var shell = TrRmArtifactWriter.Encode(sections, schema);
        var sourceMethodExports = SourceGenericMethodExports.Capture(
            program, moduleIdentity, []);
        var artifact = EmitGenericMethodCallsDraftSource(program,
            moduleIdentity, moduleTargetRequirement, sourceMethodExports,
            sourceTypeExports ?? [], schema, baseSchema: schema,
            baseArtifact: shell,
            frozenScalarValueBuilder: schema
                .UsesForeignNativeInterfaceParameterSurface()
                    ? new TrRmFrozenScalarValueBuilder() : null);
        artifact = AddForeignNativeInterfaceRows(artifact, program,
            sourceTypeExports ?? [], schema);
        _ = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
            new TrRmArtifactIndex(artifact, schema));
        return artifact;
    }

    private static byte[] AddForeignNativeInterfaceRows(byte[] artifact,
        BoundProgram program, IReadOnlyList<TrRmSourceTypeExport> typeExports,
        TrRmArtifactSchema schema)
    {
        var index = new TrRmArtifactIndex(artifact, schema);
        _ = index.TryGetSection(0x0001, out var moduleBytes,
            out var moduleCount);
        _ = index.TryGetSection(0x0002, out var stringBytes,
            out var stringCount);
        _ = index.TryGetSection(0x0004, out var featureBytes,
            out var featureCount);
        _ = index.TryGetSection(0x0010, out var moduleRefBytes,
            out var moduleRefCount);
        _ = index.TryGetSection(0x0011, out var typeSpecBytes,
            out var typeSpecCount);
        _ = index.TryGetSection(0x0013, out var typeDefBytes,
            out var typeDefCount);
        _ = index.TryGetSection(0x0014, out var fieldBytes,
            out var fieldCount);
        _ = index.TryGetSection(0x0015, out var layoutBytes,
            out var layoutCount);
        _ = index.TryGetSection(0x0016, out var methodBytes,
            out var methodCount);
        _ = index.TryGetSection(0x0017, out var signatureBytes,
            out var signatureCount);
        _ = index.TryGetSection(0x0018, out _,
            out var parameterCount);
        _ = index.TryGetSection(0x0019, out var genericParamBytes,
            out var genericParamCount);
        _ = index.TryGetSection(0x001B, out var contractBytes,
            out var contractCount);
        _ = index.TryGetSection(0x0024, out var interfaceMethodBytes,
            out var interfaceMethodCount);
        _ = index.TryGetSection(0x0025, out var interfaceBytes,
            out var interfaceCount);
        _ = index.TryGetSection(0x004C,
            out var foreignNativeInterfaceBytes,
            out var foreignNativeInterfaceCount);
        _ = index.TryGetSection(0x0048, out var targetProfileBytes,
            out var targetProfileCount);
        var strings = new TrRmStringTableReader(stringBytes, stringCount);
        var features = new TrRmFeatureTableReader(featureBytes,
            featureCount, schema);
        var modules = new TrRmModuleTableReader(moduleBytes, moduleCount,
            strings, moduleRefCount, typeDefCount, methodCount, 0,
            featureCount);
        var typeSpecs = typeSpecCount == 0 ? default
            : new TrRmTypeSpecTableReader(typeSpecBytes, typeSpecCount,
                typeDefCount, 0, genericParamCount, 0, schema, methodCount);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, typeDefCount,
            strings, modules, fieldCount, genericParamCount, layoutCount,
            contractCount, schema);
        var signatures = new TrRmMethodSigTableReader(signatureBytes,
            signatureCount, typeSpecs, parameterCount, genericParamCount);
        var methods = new TrRmMethodDefTableReader(methodBytes, methodCount,
            strings, modules, typeDefs, signatures, contractCount, 0, schema);
        var genericParams = new TrRmGenericParamTableReader(
            genericParamBytes, genericParamCount, strings, typeDefs,
            methods, signatures, typeSpecs, 0);
        var interfaces = new TrRmInterfaceDefTableReader(interfaceBytes,
            interfaceCount, typeDefs, genericParams, 0,
            interfaceMethodCount, 0, 0, 0);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfileBytes, targetProfileCount, schema);
        var foreignInterfaceRows = new List<TrRmForeignNativeInterfaceRow>(
            checked(typeExports.Count + foreignNativeInterfaceCount));
        if (foreignNativeInterfaceCount != 0)
        {
            var importedForeignInterfaces
                = new TrRmForeignNativeInterfaceTableReader(
                    foreignNativeInterfaceBytes,
                    foreignNativeInterfaceCount, interfaces,
                    targetProfiles, schema);
            for (var row = 1;
                row <= importedForeignInterfaces.Count; row++)
            {
                foreignInterfaceRows.Add(
                    new TrRmForeignNativeInterfaceRow(
                        importedForeignInterfaces.GetOwnerInterfaceDef(row),
                        importedForeignInterfaces.GetTargetProfile(row),
                        importedForeignInterfaces.GetVTableByteOffset(row)));
            }
        }

        for (var exportIndex = 0; exportIndex < typeExports.Count;
            exportIndex++)
        {
            var export = typeExports[exportIndex];
            var qualifiedName = string.IsNullOrEmpty(export.Namespace)
                ? export.Name : $"{export.Namespace}.{export.Name}";
            var definition = program.Interfaces.SingleOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.QualifiedName,
                    qualifiedName)
                && candidate.ModuleImport is null);
            if (definition?.ForeignNativeInterface is not
                    BoundForeignNativeInterfaceLayout layout
                || exportIndex >= interfaces.Count
                || !StringComparer.Ordinal.Equals(
                    typeDefs.GetNamespace(
                        interfaces.GetOwnerTypeDef(exportIndex + 1)),
                    export.Namespace)
                || !StringComparer.Ordinal.Equals(
                    typeDefs.GetName(
                        interfaces.GetOwnerTypeDef(exportIndex + 1)),
                    export.Name))
            {
                throw Invalid(
                    $"ForeignNativeInterface '{qualifiedName}' has no emitted InterfaceDef row.");
            }

            foreignInterfaceRows.Add(new TrRmForeignNativeInterfaceRow(
                checked((uint)(exportIndex + 1)), 1,
                layout.VTableByteOffset));
        }

        var bytes = TrRmForeignNativeInterfaceTableWriter.Encode(
            foreignInterfaceRows, interfaces, targetProfiles);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var sectionBytes, out var count);
            if (kind != 0x004C)
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)count), sectionBytes.ToArray()));
            }
        }

        sections.Add(new TrRmSection(0x004C, 0,
            checked((uint)foreignInterfaceRows.Count), bytes));
        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, schema);
    }

}
