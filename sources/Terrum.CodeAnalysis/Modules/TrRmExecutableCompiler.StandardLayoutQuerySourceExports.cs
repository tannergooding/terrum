// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private readonly record struct StandardSourceImportMapping(
        string ProducerIdentity, string ExportKey, uint ExpectedDefinitionId,
        int ExportKeyStringRow, BoundSourceTypeProviderSnapshot Provider);

    private readonly record struct PendingStandardImport(
        int ModuleRefRow, uint ExpectedDefinitionId, string ProducerIdentity,
        string ExportKey, int ExportKeyStringRow, int FunctionIndex,
        int ImportRow);

    private static void AddStandardLayoutQuerySourceExports(
        BoundProgram program,
        IReadOnlyList<TrRmSourceTypeExport>? typeExports,
        bool requireSequentialOrTransparentCopy,
        Dictionary<int, int> structureTypeDefRows,
        int[] structureFieldFirst,
        List<(string Text, int Encoding)> names,
        List<TrRmExportRow> exportRows)
    {
        if (typeExports is null || typeExports.Count == 0)
        {
            return;
        }
        if (!program.SourceTypePublications.IsDefault)
        {
            throw Unsupported(
                "Explicit Standard source type exports cannot replace a retained publication map.");
        }

        var sourceDeclarations = program.SourceTypeDeclarations;
        if (sourceDeclarations.IsDefault)
        {
            throw Unsupported(
                "Explicit Standard source type exports require source-bound local declarations.");
        }

        var declarationByName = new Dictionary<(string Namespace, string Name),
            BoundSourceTypeDeclaration>(sourceDeclarations.Length);
        var declaredStructures = new bool[program.Structures.Length];
        foreach (var declaration in sourceDeclarations)
        {
            if ((uint)declaration.StructureIndex >= (uint)program.Structures.Length
                || declaredStructures[declaration.StructureIndex])
            {
                throw new ArgumentException(
                    "The source TypeDef declaration map is inconsistent.",
                    nameof(program));
            }

            var structure = program.Structures[declaration.StructureIndex];
            var qualifiedName = declaration.Namespace.Length == 0
                ? declaration.Name
                : $"{declaration.Namespace}.{declaration.Name}";
            if (!StringComparer.Ordinal.Equals(structure.Name, qualifiedName)
                || declaration.DeclarationSnapshot != structure
                || structure.StandardTypeOrigin is not null
                || !declarationByName.TryAdd(
                    (declaration.Namespace, declaration.Name), declaration))
            {
                throw new ArgumentException(
                    "The source TypeDef declaration map no longer matches the checked program.",
                    nameof(program));
            }

            declaredStructures[declaration.StructureIndex] = true;
        }

        var exportKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in exportRows)
        {
            if ((uint)(row.ExportKeyStringRow - 1) >= (uint)names.Count
                || !exportKeys.Add(names[row.ExportKeyStringRow - 1].Text))
            {
                throw new ArgumentException(
                    "The Standard export table already contains an invalid or duplicate ExportKey.",
                    nameof(program));
            }
        }

        foreach (var export in typeExports)
        {
            if (export is null || export.Namespace is null
                || string.IsNullOrEmpty(export.Name)
                || string.IsNullOrEmpty(export.ExportKey)
                || export.Namespace.Contains('\0', StringComparison.Ordinal)
                || export.Name.Contains('\0', StringComparison.Ordinal)
                || export.ExportKey.Contains('\0', StringComparison.Ordinal)
                || !export.EnumMemberDeclarationKeys.IsDefault
                || export.GenericArity != -1
                || !export.InterfaceMethodExports.IsDefault
                || !export.InterfaceAssociatedTypeExports.IsDefault)
            {
                throw new ArgumentException(
                    "A Standard source TypeDef export mapping contains invalid text or enum data.",
                    nameof(typeExports));
            }

            try
            {
                _ = s_sourceTypeExportKeyEncoding.GetByteCount(export.ExportKey);
            }
            catch (EncoderFallbackException exception)
            {
                throw new ArgumentException(
                    "A source TypeDef ExportKey must be well-formed scalar text.",
                    nameof(typeExports), exception);
            }

            if (!declarationByName.TryGetValue(
                (export.Namespace, export.Name), out var declaration))
            {
                throw new ArgumentException(
                    "A source TypeDef export mapping does not select one source declaration.",
                    nameof(typeExports));
            }

            var structure = program.Structures[declaration.StructureIndex];
            if (structure.IsPrivate || structure.IsInternal || structure.IsUnique
                || structure.CleanupMethod is not null
                || structure.SourceGenericTypeDefinitionIndex >= 0
                || structure.BlittableTargetRequirement
                    != FixedLayoutStorage.WindowsX64TargetRequirement
                || (requireSequentialOrTransparentCopy
                    && !structure.IsSequential && !structure.IsTransparent)
                || !structureTypeDefRows.TryGetValue(
                    declaration.StructureIndex, out var typeDefRow)
                || !exportKeys.Add(export.ExportKey))
            {
                throw new ArgumentException(
                    "A Standard source TypeDef export must select one public nongeneric Copy struct with the selected Blittable layout.",
                    nameof(typeExports));
            }

            var typeExportKeyRow = names.Count + 1;
            names.Add((export.ExportKey, 1));
            exportRows.Add(new TrRmExportRow(
                0x1300_0000u | checked((uint)typeDefRow),
                typeExportKeyRow));

            if (export.FieldExports.IsDefaultOrEmpty)
            {
                continue;
            }

            var selectedFields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fieldExport in export.FieldExports)
            {
                if (fieldExport is null || string.IsNullOrEmpty(fieldExport.Name)
                    || string.IsNullOrEmpty(fieldExport.ExportKey)
                    || fieldExport.Name.Contains('\0', StringComparison.Ordinal)
                    || fieldExport.ExportKey.Contains('\0', StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "A source FieldDef export mapping contains invalid text.",
                        nameof(typeExports));
                }

                try
                {
                    _ = s_sourceTypeExportKeyEncoding.GetByteCount(
                        fieldExport.ExportKey);
                }
                catch (EncoderFallbackException exception)
                {
                    throw new ArgumentException(
                        "A source FieldDef ExportKey must be well-formed scalar text.",
                        nameof(typeExports), exception);
                }

                var fieldOrdinal = -1;
                for (var index = 0; index < structure.Fields.Length; index++)
                {
                    if (StringComparer.Ordinal.Equals(
                        structure.Fields[index].Name, fieldExport.Name))
                    {
                        fieldOrdinal = index;
                        break;
                    }
                }

                if (fieldOrdinal < 0
                    || structure.Fields[fieldOrdinal].IsPrivate
                    || structure.Fields[fieldOrdinal].IsInternal
                    || !selectedFields.Add(fieldExport.Name)
                    || !exportKeys.Add(fieldExport.ExportKey)
                    || structureFieldFirst[typeDefRow - 1] == 0)
                {
                    throw new ArgumentException(
                        "A source FieldDef export must select one public direct field with a unique ExportKey.",
                        nameof(typeExports));
                }

                var fieldRow = checked(structureFieldFirst[typeDefRow - 1]
                    + fieldOrdinal);
                var fieldExportKeyRow = names.Count + 1;
                names.Add((fieldExport.ExportKey, 1));
                exportRows.Add(new TrRmExportRow(
                    0x1400_0000u | checked((uint)fieldRow),
                    fieldExportKeyRow));
            }
        }
    }

    private static List<StandardSourceImportMapping>
        CreateStandardLayoutQuerySourceImportMappings(
        BoundProgram program, string schemaIdentity,
        Dictionary<int, int> structureTypeDefRows,
        int[] structureFieldFirst,
        List<(string Text, int Encoding)> names)
    {
            var expectedSchemaIdentity
                = IsSelectedInitializationReferenceLocalFieldLayoutSchemaIdentity(
                    schemaIdentity)
                ? schemaIdentity
                : StandardLayoutQueriesSchemaIdentity;
            var mappings = new List<StandardSourceImportMapping>();
            var providerByIdentity = new Dictionary<string,
                BoundSourceTypeProviderSnapshot>(StringComparer.Ordinal);

            void Add(string identity, string exportKey, uint definitionId,
                BoundSourceTypeProviderSnapshot provider)
            {
                if (!StringComparer.Ordinal.Equals(provider.ModuleIdentity,
                        identity)
                    || !StringComparer.Ordinal.Equals(provider.SchemaIdentity,
                        expectedSchemaIdentity)
                    || string.IsNullOrEmpty(exportKey)
                    || exportKey.Contains('\0', StringComparison.Ordinal))
                {
                    throw Unsupported(
                        "A source TypeDef Import origin is incomplete or uses another schema.");
                }

                if (providerByIdentity.TryGetValue(identity,
                        out var existingProvider)
                    && existingProvider != provider)
                {
                    throw Invalid(
                        "One selected Module identity has conflicting provider snapshots.");
                }
                providerByIdentity[identity] = provider;

                try
                {
                    _ = s_sourceTypeExportKeyEncoding.GetByteCount(exportKey);
                }
                catch (System.Text.EncoderFallbackException exception)
                {
                    throw new ArgumentException(
                        "A LayoutQueries Import ExportKey must be well-formed scalar text.",
                        nameof(program), exception);
                }

                var stringRow = names.Count + 1;
                names.Add((exportKey, 1));
                mappings.Add(new StandardSourceImportMapping(identity, exportKey,
                    definitionId, stringRow, provider));
            }

            for (var structureIndex = 0;
                structureIndex < program.Structures.Length; structureIndex++)
            {
                var structure = program.Structures[structureIndex];
                var typeOrigin = structure.StandardTypeOrigin;
                if (typeOrigin is null)
                {
                    if (structure.StandardLayoutOnly)
                    {
                        throw Unsupported(
                            "A query-only source type has no retained Standard TypeDef origin.");
                    }

                    continue;
                }

                var initializationReferenceLocalFieldLayout
                    = IsSelectedInitializationReferenceLocalFieldLayoutSchemaIdentity(
                        schemaIdentity);
                if ((!structure.StandardLayoutOnly
                        && !initializationReferenceLocalFieldLayout)
                    || !structureTypeDefRows.TryGetValue(structureIndex,
                        out var typeDefRow)
                    || typeOrigin.DefinitionId >> 24 != 0x13
                    || typeOrigin.ExportKeys.IsDefaultOrEmpty
                    || typeOrigin.ExpectedDeclaration is not BoundStruct expected)
                {
                    throw Unsupported(
                        "A Standard LayoutQueries TypeDef Import has no complete local declaration view.");
                }

                var actualDeclaration = structure with {
                    StandardTypeOrigin = null,
                    StandardLayoutOnly = false,
                    Fields = [.. structure.Fields.Select(static field => field with {
                        StandardFieldOrigin = null,
                    })],
                };
                var expectedDeclarations
                    = ImmutableArray.CreateBuilder<BoundStruct>(
                        1 + (typeOrigin.AdditionalExpectedDeclarations
                            .IsDefaultOrEmpty
                            ? 0
                            : typeOrigin.AdditionalExpectedDeclarations.Length));
                expectedDeclarations.Add(expected);
                if (!typeOrigin.AdditionalExpectedDeclarations.IsDefaultOrEmpty)
                {
                    expectedDeclarations.AddRange(
                        typeOrigin.AdditionalExpectedDeclarations);
                }

                if (expectedDeclarations.Any(expectedView =>
                        actualDeclaration with { Fields = default }
                            != expectedView with { Fields = default }
                        || !actualDeclaration.Fields.AsSpan().SequenceEqual(
                            expectedView.Fields.AsSpan())))
                {
                    throw Unsupported(
                        "A LayoutQueries TypeDef Import no longer matches its expected declaration.");
                }

                var expectedDefinition = 0x1300_0000u
                    | checked((uint)typeDefRow);
                var selectedTypeKeys
                    = typeOrigin.SelectedExportKeys.IsDefaultOrEmpty
                        ? typeOrigin.ExportKeys
                        : typeOrigin.SelectedExportKeys;
                if (StringComparer.Ordinal.Equals(schemaIdentity,
                        StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity))
                {
                    foreach (var selectedTypeKey in selectedTypeKeys)
                    {
                        Add(typeOrigin.Provider.ModuleIdentity,
                            selectedTypeKey, expectedDefinition,
                            typeOrigin.Provider);
                    }
                }
                else
                {
                    Add(typeOrigin.Provider.ModuleIdentity,
                        selectedTypeKeys[0], expectedDefinition,
                        typeOrigin.Provider);
                }

                for (var fieldOrdinal = 0;
                    fieldOrdinal < structure.Fields.Length; fieldOrdinal++)
                {
                    var field = structure.Fields[fieldOrdinal];
                    var fieldOrigin = field.StandardFieldOrigin;
                    if (fieldOrigin is null)
                    {
                        continue;
                    }

                    if ((StringComparer.Ordinal.Equals(schemaIdentity,
                            StandardInitReferenceBodiesLocalFieldLayoutSchemaIdentity)
                            && StringComparer.Ordinal.Equals(
                                fieldOrigin.Provider.SchemaIdentity,
                                StandardInitReferenceBodiesLocalFieldLayoutSchemaIdentity))
                        || (StringComparer.Ordinal.Equals(schemaIdentity,
                                StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity)
                            && !StringComparer.Ordinal.Equals(
                                fieldOrigin.Provider.SchemaIdentity,
                                StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity)))
                    {
                        throw Unsupported(
                            "The selected initialization-reference field-layout schema does not retain this FieldDef Import expectation.");
                    }

                    if (fieldOrigin.DefinitionId >> 24 != 0x14
                        || fieldOrigin.ExportKeys.IsDefaultOrEmpty
                        || fieldOrigin.ExpectedDeclaration is not
                            BoundStructField expectedField
                        || fieldOrigin.Provider != typeOrigin.Provider
                        || structureFieldFirst[typeDefRow - 1] == 0)
                    {
                        throw Unsupported(
                            "A LayoutQueries FieldDef Import has no exact owner/provider declaration.");
                    }

                    var actualField = field with {
                        StandardFieldOrigin = null,
                    };
                    var expectedFields
                        = ImmutableArray.CreateBuilder<BoundStructField>(
                            1 + (fieldOrigin.AdditionalExpectedDeclarations
                                .IsDefaultOrEmpty
                                ? 0
                                : fieldOrigin.AdditionalExpectedDeclarations
                                    .Length));
                    expectedFields.Add(expectedField);
                    if (!fieldOrigin.AdditionalExpectedDeclarations
                        .IsDefaultOrEmpty)
                    {
                        expectedFields.AddRange(
                            fieldOrigin.AdditionalExpectedDeclarations);
                    }

                    if (expectedFields.Any(expectedView =>
                            actualField != expectedView))
                    {
                        throw Unsupported(
                            "A LayoutQueries FieldDef Import no longer matches its expected declaration.");
                    }

                    var fieldRow = checked(structureFieldFirst[typeDefRow - 1]
                        + fieldOrdinal);
                    var expectedFieldDefinition = 0x1400_0000u
                        | checked((uint)fieldRow);
                    var selectedFieldKeys
                        = fieldOrigin.SelectedExportKeys.IsDefaultOrEmpty
                            ? fieldOrigin.ExportKeys
                            : fieldOrigin.SelectedExportKeys;
                    if (StringComparer.Ordinal.Equals(schemaIdentity,
                            StandardInitReferenceBodiesNestedFieldDestinationsSchemaIdentity))
                    {
                        foreach (var selectedFieldKey in selectedFieldKeys)
                        {
                            Add(fieldOrigin.Provider.ModuleIdentity,
                                selectedFieldKey, expectedFieldDefinition,
                                fieldOrigin.Provider);
                        }
                    }
                    else
                    {
                        Add(fieldOrigin.Provider.ModuleIdentity,
                            selectedFieldKeys[0], expectedFieldDefinition,
                            fieldOrigin.Provider);
                    }
                }
            }

            return mappings;
    }
}
