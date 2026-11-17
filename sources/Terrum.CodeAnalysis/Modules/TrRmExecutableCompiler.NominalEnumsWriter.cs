// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    internal static byte[] EmitNominalEnumsDraftSource(
        BoundProgram program, string moduleIdentity,
        uint moduleTargetRequirement,
        IReadOnlyList<TrRmSourceTypeExport>? sourceTypeExports = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        if (BoundEnumTypes.HasStandardDeclarations(
                program.EnumDefinitions))
        {
            throw Unsupported(
                "NominalEnumsDraft cannot persist Standard enum origins.");
        }

        var sourceEnumExports = sourceTypeExports is null
            ? null
            : NominalEnumsDraftSourceEnumExports.Capture(program,
                moduleIdentity, sourceTypeExports);
        if (sourceEnumExports is null
            && program.EnumDefinitions.Any(static definition =>
                definition.Identity
                    is BoundEnumDeclarationIdentity.Source))
        {
            throw Unsupported(
                "NominalEnumsDraft source enums require explicit TypeDef and member-key assignments.");
        }

        if (program.Functions.IsEmpty)
        {
            return EmitNominalEnumsDraftDeclarationLibrary(program,
                moduleIdentity, moduleTargetRequirement,
                sourceEnumExports);
        }

        return IsNominalEnumsDraftIdentityLibrary(program)
            ? EmitNominalEnumsDraftIdentityLibrary(program, moduleIdentity,
                moduleTargetRequirement, sourceEnumExports)
            : EmitNominalEnumsDraftConstantReturns(program, moduleIdentity,
                moduleTargetRequirement, sourceEnumExports);
    }

    internal sealed record NominalEnumsDraftSourceEnumExport(
        BoundEnumDefinition Definition,
        BoundSourceEnumDeclarationHandle Declaration,
        string ExportKey,
        ImmutableArray<BoundSourceEnumMemberHandle> MemberDeclarations,
        ImmutableArray<string> MemberDeclarationKeys);

    internal sealed class NominalEnumsDraftSourceEnumExports
    {
        private readonly Dictionary<BoundSourceEnumDeclarationHandle,
            NominalEnumsDraftSourceEnumExport> _byDeclaration;

        private NominalEnumsDraftSourceEnumExports(BoundProgram program,
            ImmutableArray<BoundEnumDefinition> enumDefinitions,
            ImmutableArray<NominalEnumsDraftSourceEnumExport> exports)
        {
            Program = program;
            EnumDefinitions = enumDefinitions;
            Exports = exports;
            _byDeclaration = [];
            foreach (var export in exports)
            {
                _byDeclaration.Add(export.Declaration, export);
            }
        }

        private BoundProgram Program { get; }
        private ImmutableArray<BoundEnumDefinition> EnumDefinitions { get; }
        private ImmutableArray<NominalEnumsDraftSourceEnumExport> Exports { get; }

        internal static NominalEnumsDraftSourceEnumExports Capture(
            BoundProgram program, string moduleIdentity,
            IReadOnlyList<TrRmSourceTypeExport> typeExports)
        {
            if (!BoundEnumTypes.IsValidTable(program.EnumDefinitions))
            {
                throw new ArgumentException(
                    "The source enum table is invalid.",
                    nameof(program));
            }

            var sourceDefinitions
                = new Dictionary<(string Namespace, string Name),
                    List<BoundEnumDefinition>>();
            var allSourceDefinitions
                = ImmutableArray.CreateBuilder<BoundEnumDefinition>();
            foreach (var definition in program.EnumDefinitions)
            {
                if (definition.Identity
                    is not BoundEnumDeclarationIdentity.Source source)
                {
                    continue;
                }

                var declaration = source.Value;
                if (!declaration.IsValid
                    || !StringComparer.Ordinal.Equals(
                        declaration.QualifiedName, definition.Name))
                {
                    throw new ArgumentException(
                        "A source enum declaration does not match its checked program snapshot.",
                        nameof(program));
                }

                var separator = declaration.QualifiedName.LastIndexOf(
                    '.', StringComparison.Ordinal);
                var selector = separator < 0
                    ? (string.Empty, declaration.QualifiedName)
                    : (declaration.QualifiedName[..separator],
                        declaration.QualifiedName[(separator + 1)..]);
                if (!sourceDefinitions.TryGetValue(selector,
                        out var declarations))
                {
                    declarations = [];
                    sourceDefinitions.Add(selector, declarations);
                }
                declarations.Add(definition);
                allSourceDefinitions.Add(definition);
            }

            var existingExportKeys = new HashSet<string>(
                StringComparer.Ordinal);
            foreach (var definition in program.EnumDefinitions)
            {
                if (definition.Identity
                        is BoundEnumDeclarationIdentity.Core core
                    && StringComparer.Ordinal.Equals(
                        core.Value.ProviderIdentity, moduleIdentity)
                    && !existingExportKeys.Add(core.Value.ExportKey))
                {
                    throw new ArgumentException(
                        "The checked program contains duplicate local enum ExportKeys.",
                        nameof(program));
                }
            }
            foreach (var function in program.Functions)
            {
                if (!function.IsPrivate && !function.IsInternal
                    && !existingExportKeys.Add(function.Name))
                {
                    throw new ArgumentException(
                        "The checked program contains duplicate exported names.",
                        nameof(program));
                }
            }

            var selected = new HashSet<BoundSourceEnumDeclarationHandle>();
            var exports
                = ImmutableArray.CreateBuilder<
                    NominalEnumsDraftSourceEnumExport>(
                    typeExports.Count);
            foreach (var export in typeExports)
            {
                if (export is null || export.Namespace is null
                    || string.IsNullOrEmpty(export.Name)
                    || string.IsNullOrEmpty(export.ExportKey)
                    || export.Namespace.Contains('\0',
                        StringComparison.Ordinal)
                    || export.Name.Contains('\0', StringComparison.Ordinal)
                    || export.ExportKey.Contains('\0',
                        StringComparison.Ordinal)
                    || export.EnumMemberDeclarationKeys.IsDefault)
                {
                    throw new ArgumentException(
                        "A NominalEnumsDraft source enum mapping is invalid.",
                        nameof(typeExports));
                }

                if (!IsValidKeyText(export.ExportKey))
                {
                    throw new ArgumentException(
                        "A NominalEnumsDraft ExportKey must be well-formed scalar text within Standard String limits.",
                        nameof(typeExports));
                }
                if (!sourceDefinitions.TryGetValue(
                        (export.Namespace, export.Name),
                        out var declarations)
                    || declarations.Count != 1)
                {
                    throw new ArgumentException(
                        "A NominalEnumsDraft mapping must select one source enum in the checked program.",
                        nameof(typeExports));
                }

                var definition = declarations[0];
                if (definition.Identity
                    is not BoundEnumDeclarationIdentity.Source source)
                {
                    throw new ArgumentException(
                        "A NominalEnumsDraft mapping must select a source enum in the checked program.",
                        nameof(typeExports));
                }

                var declaration = source.Value;
                if (!definition.IsPublic || !declaration.IsPublic
                    || declaration.IsFileLocal)
                {
                    throw new ArgumentException(
                        "A NominalEnumsDraft source enum mapping must select a public namespace-level declaration.",
                        nameof(typeExports));
                }
                if (definition.UnderlyingType != PrimitiveType.Int32
                    || !definition.IsPrimary
                    || definition.ModuleImport is not null)
                {
                    throw new NotSupportedException(
                        "NominalEnumsDraft supports only complete public primary int32 source enums.");
                }
                if (!selected.Add(declaration)
                    || !existingExportKeys.Add(export.ExportKey))
                {
                    throw new ArgumentException(
                        "NominalEnumsDraft source enum declarations and ExportKeys must be unique.",
                        nameof(typeExports));
                }
                if (export.EnumMemberDeclarationKeys.Length
                    != definition.Members.Length)
                {
                    throw new ArgumentException(
                        "A NominalEnumsDraft source enum requires one explicit key for every declared member.",
                        nameof(typeExports));
                }

                var memberKeys
                    = ImmutableArray.CreateBuilder<string>(
                        export.EnumMemberDeclarationKeys.Length);
                var memberDeclarations
                    = ImmutableArray.CreateBuilder<
                        BoundSourceEnumMemberHandle>(
                        export.EnumMemberDeclarationKeys.Length);
                var seenMemberKeys = new HashSet<string>(
                    StringComparer.Ordinal);
                for (var memberIndex = 0;
                    memberIndex < definition.Members.Length;
                    memberIndex++)
                {
                    var member = definition.Members[memberIndex];
                    var key = export.EnumMemberDeclarationKeys[memberIndex];
                    if (member.Identity
                            is not BoundEnumMemberIdentity.Source memberSource
                        || member.DeclaringEnum
                            is not BoundEnumDeclarationIdentity.Source memberOwner
                        || !ReferenceEquals(memberSource.Value.Owner,
                            declaration)
                        || !ReferenceEquals(memberOwner.Value, declaration)
                        || string.IsNullOrEmpty(key)
                        || key.Contains('\0', StringComparison.Ordinal)
                        || !seenMemberKeys.Add(key))
                    {
                        throw new ArgumentException(
                            "A NominalEnumsDraft member-key assignment does not match its unique source member.",
                            nameof(typeExports));
                    }

                    if (!IsValidKeyText(key))
                    {
                        throw new ArgumentException(
                            "A NominalEnumsDraft member key must be well-formed scalar text within Standard String limits.",
                            nameof(typeExports));
                    }
                    memberDeclarations.Add(memberSource.Value);
                    memberKeys.Add(key);
                }

                exports.Add(new NominalEnumsDraftSourceEnumExport(
                    definition, declaration, export.ExportKey,
                    memberDeclarations.MoveToImmutable(),
                    memberKeys.MoveToImmutable()));
            }

            foreach (var definition in allSourceDefinitions)
            {
                if (!definition.IsPublic
                    || definition.Identity
                        is not BoundEnumDeclarationIdentity.Source source
                    || source.Value.IsFileLocal
                    || definition.UnderlyingType != PrimitiveType.Int32
                    || !definition.IsPrimary)
                {
                    throw new NotSupportedException(
                        "NominalEnumsDraft cannot retain a nonpublic, file-local, non-primary or non-int32 source enum.");
                }
                if (!selected.Contains(source.Value))
                {
                    throw new NotSupportedException(
                        "Every retained source enum requires an explicit NominalEnumsDraft export and complete member-key assignment.");
                }
            }

            return new NominalEnumsDraftSourceEnumExports(program,
                program.EnumDefinitions, exports.MoveToImmutable());
        }

        private static bool IsValidKeyText(string key)
        {
            try
            {
                var byteCount = s_sourceTypeExportKeyEncoding.GetByteCount(key);
                return byteCount is >= 1 and <= 0xFF_FFFF;
            }
            catch (EncoderFallbackException)
            {
                return false;
            }
        }

        internal bool TryGet(BoundEnumDefinition definition,
            out NominalEnumsDraftSourceEnumExport export)
        {
            if (definition.Identity
                    is BoundEnumDeclarationIdentity.Source source
                && _byDeclaration.TryGetValue(source.Value, out export!)
                && ReferenceEquals(export.Definition, definition))
            {
                return true;
            }

            export = null!;
            return false;
        }

        internal void ValidateProgram(BoundProgram program)
        {
            if (!ReferenceEquals(Program, program)
                || program.EnumDefinitions.Length
                    != EnumDefinitions.Length
                || program.EnumDefinitions.Where((definition, index) =>
                    !ReferenceEquals(definition, EnumDefinitions[index]))
                    .Any())
            {
                throw new ArgumentException(
                    "NominalEnumsDraft source enum assignments belong to another checked program snapshot.",
                    nameof(program));
            }
        }

        internal NominalEnumsDraftSourceEnumExports Rebase(
            BoundProgram program)
        {
            if (program.EnumDefinitions.Length != EnumDefinitions.Length
                || program.EnumDefinitions.Where((definition, index) =>
                    !ReferenceEquals(definition, EnumDefinitions[index]))
                    .Any())
            {
                throw new ArgumentException(
                    "NominalEnumsDraft source enum assignments cannot be rebased to changed declarations.",
                    nameof(program));
            }

            return new NominalEnumsDraftSourceEnumExports(program,
                EnumDefinitions, Exports);
        }
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
        uint moduleTargetRequirement,
        NominalEnumsDraftSourceEnumExports? sourceEnumExports)
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
        var declarationSourceEnumExports
            = sourceEnumExports?.Rebase(declarationProgram);
        var declarationArtifact = EmitNominalEnumsDraftDeclarationLibrary(
            declarationProgram, moduleIdentity, moduleTargetRequirement,
            declarationSourceEnumExports);
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
        uint moduleTargetRequirement,
        NominalEnumsDraftSourceEnumExports? sourceEnumExports = null)
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
        var declarationSourceEnumExports
            = sourceEnumExports?.Rebase(declarationProgram);
        var declarationArtifact = EmitNominalEnumsDraftDeclarationLibrary(
            declarationProgram, moduleIdentity, moduleTargetRequirement,
            declarationSourceEnumExports);
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
