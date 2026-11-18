// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private sealed class GenericMethodNominalTypeWriter
    {
        private readonly List<TrRmSection> _sections;
        private readonly List<(string Text, int Encoding)> _names;
        private readonly List<ReadOnlyMemory<byte>> _typeSpecs;
        private readonly List<TrRmGenericParamRow> _formals;
        private readonly List<TrRmConstraintRow> _constraints;
        private readonly TrRmArtifactSchema _schema;
        private readonly string _moduleIdentity;
        private readonly Dictionary<BoundGenericNominalTypeIdentity,
            BoundGenericNominalTypeDefinition> _definitions = [];
        private readonly Dictionary<BoundGenericNominalTypeIdentity, uint>
            _definitionReferences = [];
        private readonly Dictionary<BoundSourceTypeProviderSnapshot, byte[]>
            _artifacts = [];
        private readonly Dictionary<string, BoundSourceTypeProviderSnapshot>
            _modules = new(StringComparer.Ordinal);
        private readonly Dictionary<BoundSourceTypeProviderSnapshot,
            BoundSourceTypeProviderSnapshot> _snapshots = [];
        private readonly Dictionary<ushort, List<byte[]>> _rows = [];
        private readonly Dictionary<(BoundSourceTypeProviderSnapshot Snapshot,
            ushort Kind, int Row), uint> _remappedRows = [];
        private readonly HashSet<BoundSourceTypeProviderSnapshot>
            _usedSnapshots = [];
        private readonly List<ReadOnlyMemory<byte>> _blobs;
        private int _remainingVisits = SemanticVerifier.MaximumAnalysisVisits;

        internal GenericMethodNominalTypeWriter(List<TrRmSection> sections,
            List<(string Text, int Encoding)> names,
            List<ReadOnlyMemory<byte>> typeSpecs,
            List<TrRmGenericParamRow> formals,
            List<TrRmConstraintRow> constraints, BoundProgram program,
            IReadOnlyList<GenericMethodEmission> emissions,
            TrRmArtifactSchema schema)
        {
            _sections = sections;
            _names = names;
            _typeSpecs = typeSpecs;
            _formals = formals;
            _constraints = constraints;
            _schema = schema;
            var strings = GetSection(sections, 0x0002);
            var modules = GetSection(sections, 0x0001);
            var stringTable = new TrRmStringTableReader(strings.Bytes.Span,
                checked((int)strings.RecordCount));
            _moduleIdentity = stringTable.DecodeName(ReadInt(
                modules.Bytes.Span, 0));
            _blobs = ReadVariableRows(sections, 0x0003, 0, 1);
            foreach (var definition in program.GenericNominalTypeDefinitions
                .Concat(emissions.SelectMany(static emission =>
                    emission.Definition.ProofProgram
                        .GenericNominalTypeDefinitions)))
            {
                if (_definitions.TryGetValue(definition.Identity,
                        out var existing))
                {
                    if (!BoundGenericNominalTypeContract
                        .AreEquivalentDefinitions(existing, definition))
                    {
                        throw Invalid(
                            "G1 nominal source types have conflicting declaration snapshots.");
                    }
                    _definitions[definition.Identity] = existing with {
                        ProviderExpectations = [
                            .. existing.ProviderExpectations,
                            .. definition.ProviderExpectations.Where(
                                expectation => !existing.ProviderExpectations
                                    .Any(candidate =>
                                        BoundGenericNominalTypeContract
                                            .AreEquivalentProviderExpectations(
                                                candidate, expectation))),
                        ],
                    };
                }
                else
                {
                    _definitions.Add(definition.Identity, definition);
                }
            }
        }

        internal List<TrRmExportRow> Exports { get; } = [];

        internal void Prepare(BoundGenericMethodDefinition method,
            BoundFunction function)
        {
            PrepareType(function.ReturnType);
            foreach (var variable in function.Variables)
            {
                PrepareType(variable.Type);
            }
            foreach (var formal in method.Formals)
            {
                if (formal.TypeDefault is { } typeDefault)
                {
                    PrepareTerm(typeDefault.Term, 0);
                }
            }
            if (!BoundTreeTraversal.VisitExpressions(function.Body,
                    expression =>
                    {
                        PrepareType(expression.Type);
                        if (expression is BoundCall {
                            GenericMethodCall: { } call,
                        })
                        {
                            foreach (var argument in call.TypeArguments)
                            {
                                PrepareType(argument);
                            }
                        }
                    }))
            {
                throw Unsupported("A G1 nominal source Body cannot be traversed.");
            }

            void PrepareType(PrimitiveType type)
            {
                if (!SemanticVerifier.TryGetGenericMethodActualTypeTerm(type,
                        function, method.ProofProgram, 0, out var term))
                {
                    throw Unsupported(
                        "A G1 nominal source type has no complete checked term.");
                }
                PrepareTerm(term, 0);
            }
        }

        private void PrepareTerm(BoundGenericMethodTypeTerm term, int depth)
        {
            Visit(depth);
            if (term.NominalTypeIdentity is { } identity)
            {
                _ = DefinitionReference(identity, depth + 1);
            }
            foreach (var argument in term.TypeArguments)
            {
                PrepareTerm(argument, depth + 1);
            }
        }

        internal uint TypeTermRef(BoundGenericMethodTypeTerm term,
            BoundGenericMethodDefinition method,
            Dictionary<PrimitiveType, uint> formalRows,
            Dictionary<PrimitiveType, uint> builtInRows, int depth)
        {
            Visit(depth);
            if (term.IsGarbageCollected || term.IsOptional)
            {
                throw Unsupported(
                    "G1 nominal source terms do not select optional or GC types.");
            }
            uint result;
            switch (term.Kind)
            {
                case BoundGenericMethodTypeTermKind.Formal:
                    result = formalRows[method.Formals[
                        term.FormalOrdinal].PlaceholderType];
                    return term.IsWritable
                        ? AddGenericTypeSpecQualifiers(result, 0x01, _typeSpecs)
                        : result;
                case BoundGenericMethodTypeTermKind.Concrete:
                    return GenericMethodTypeSpecRef(term.ConcreteType,
                        formalRows, builtInRows, _typeSpecs,
                        method.ProofProgram.SpanTypes,
                        method.ProofProgram.CheckedReferenceTypes,
                        method.DefinitionOrdinal, method,
                        allowSpanType: true, nominalTypes: this,
                        depth: depth + 1);
                case BoundGenericMethodTypeTermKind.ImportedGeneric:
                    if (term.NominalTypeIdentity is not { } identity
                        || term.IsWritable || term.IsInitialization)
                    {
                        throw Unsupported(
                            "G1 nominal source terms require an unqualified TypeDef identity.");
                    }
                    var definitionRef = DefinitionReference(identity, depth + 1);
                    var definition = _definitions[identity];
                    if (definition.Formals.IsEmpty)
                    {
                        return definitionRef;
                    }
                    var arguments = new uint[definition.Formals.Length];
                    var typeOrdinal = 0;
                    var constantOrdinal = 0;
                    for (var ordinal = 0; ordinal < arguments.Length; ordinal++)
                    {
                        if (definition.Formals[ordinal].Kind
                            == BoundGenericArgumentKind.Type)
                        {
                            arguments[ordinal] = TypeTermRef(
                                term.TypeArguments[typeOrdinal++], method,
                                formalRows, builtInRows, depth + 1);
                        }
                        else
                        {
                            var constant = term.ConstantArguments[
                                constantOrdinal++];
                            if (constant.Kind
                                != BoundGenericMethodTypeConstantArgumentKind.Value)
                            {
                                throw Unsupported(
                                    "G1 nominal source terms require closed constant actuals.");
                            }
                            arguments[ordinal] = ConstantArgument(
                                BoundGenericNominalTypeArgument.ForConstant(
                                    constant.DomainType, constant.CarrierType,
                                    constant.Bits.Low, constant.Bits.High),
                                method, formalRows, builtInRows, depth + 1);
                        }
                    }
                    _typeSpecs.Add(TrRmTypeSpecTableWriter.Constructed(
                        definitionRef, arguments));
                    return checked((uint)_typeSpecs.Count);
                case BoundGenericMethodTypeTermKind.CheckedReference:
                case BoundGenericMethodTypeTermKind.Span:
                    result = TypeTermRef(term.TypeArguments.Single(), method,
                        formalRows, builtInRows, depth + 1);
                    if (term.Kind == BoundGenericMethodTypeTermKind.Span)
                    {
                        var qualifiers = (byte)(
                            (term.IsInitialization ? 0x04 : 0)
                            | (term.IsElementWritable ? 0x01 : 0));
                        if (qualifiers != 0)
                        {
                            result = AddGenericTypeSpecQualifiers(result,
                                qualifiers, _typeSpecs);
                        }
                        _typeSpecs.Add(TrRmTypeSpecTableWriter.IndexedFamily(
                            0x8A, result, term.IsWritable ? (byte)0x01 : (byte)0));
                    }
                    else
                    {
                        _typeSpecs.Add(term.IsInitialization
                            ? TrRmTypeSpecTableWriter.InitializationReference(
                                result, term.IsWritable)
                            : TrRmTypeSpecTableWriter.Wrapped(0x07, result,
                                term.IsWritable ? (byte)0x01 : (byte)0));
                    }
                    return checked((uint)_typeSpecs.Count);
                default:
                    throw Unsupported(
                        "A G1 nominal source term uses an unsupported type constructor.");
            }
        }

        internal uint TypeSpecRef(PrimitiveType type,
            BoundGenericMethodDefinition method,
            Dictionary<PrimitiveType, uint> formalRows,
            Dictionary<PrimitiveType, uint> builtInRows, int depth)
        {
            Visit(depth);
            var program = method.ProofProgram;
            if (!BoundGenericNominalTypeContract.IsValidType(type, program))
            {
                throw Invalid("A G1 nominal source type has no checked instance.");
            }
            var instance = program.GenericNominalTypeInstances[
                BoundGenericNominalTypeTypes.Index(type)];
            if (instance.Qualifiers != 0 || instance.TypeSpecForm != 0)
            {
                throw Unsupported(
                    "The G1 source writer does not emit qualified nominal wrappers.");
            }
            var definitionRef = DefinitionReference(instance.Definition,
                depth + 1);
            if (!_definitions.TryGetValue(instance.Definition,
                    out var definition)
                || instance.Arguments.Length != definition.Formals.Length)
            {
                throw Invalid(
                    "A G1 nominal construction has no exact formal declaration.");
            }
            if (instance.Arguments.IsEmpty)
            {
                return definitionRef;
            }

            var arguments = new uint[instance.Arguments.Length];
            for (var ordinal = 0; ordinal < arguments.Length; ordinal++)
            {
                var argument = instance.Arguments[ordinal];
                if (argument.Kind != definition.Formals[ordinal].Kind)
                {
                    throw Invalid(
                        "A G1 nominal construction has an argument of the wrong kind.");
                }
                if (argument.Kind == BoundGenericArgumentKind.Constant)
                {
                    arguments[ordinal] = ConstantArgument(argument, method,
                        formalRows, builtInRows, depth + 1);
                    continue;
                }
                if ((argument.Qualifiers & ~0x01) != 0
                    || (argument.HasTypeFormalOwnerIdentity
                        && argument.TypeFormalDefinitionOrdinal
                            != method.FormalTypeOwnerOrdinal))
                {
                    throw Unsupported(
                        "A G1 nominal argument has unsupported qualifiers or a foreign formal owner.");
                }
                arguments[ordinal] = argument.NominalTypeIdentity
                    is BoundGenericNominalTypeIdentity nominal
                        ? DefinitionReference(nominal, depth + 1)
                        : GenericMethodTypeSpecRef(argument.Type, formalRows,
                            builtInRows, _typeSpecs, program.SpanTypes,
                            program.CheckedReferenceTypes,
                            method.DefinitionOrdinal, method,
                            allowSpanType: true, nominalTypes: this,
                            depth: depth + 1);
                if (argument.Qualifiers != 0)
                {
                    if (argument.NominalTypeIdentity is not null
                        || BoundGenericNominalTypeTypes.IsType(argument.Type))
                    {
                        throw Unsupported(
                            "G1 nominal actuals do not select qualified nominal TypeSpecs.");
                    }
                    arguments[ordinal] = AddGenericTypeSpecQualifiers(
                        arguments[ordinal], argument.Qualifiers, _typeSpecs);
                }
            }
            _typeSpecs.Add(TrRmTypeSpecTableWriter.Constructed(
                definitionRef, arguments));
            return checked((uint)_typeSpecs.Count);
        }

        private uint DefinitionReference(
            BoundGenericNominalTypeIdentity identity, int depth)
        {
            Visit(depth);
            if (_definitionReferences.TryGetValue(identity, out var reference))
            {
                return reference;
            }
            if (!_definitions.TryGetValue(identity, out var definition)
                || definition.Provider is not { } provider
                || definition.ExportKeys.IsDefaultOrEmpty
                || identity.ProviderIdentity != provider.ModuleIdentity
                || identity.ModuleIdentity != provider.ModuleIdentity)
            {
                throw Unsupported(
                    "G1 nominal source emission requires an exact checked public TypeDef snapshot.");
            }
            var providerTables = Tables(provider);
            var providerRow = checked((int)(identity.DefinitionId
                & 0x00FF_FFFF));
            foreach (var key in definition.ExportKeys)
            {
                if (!providerTables.Exports.TryGetDefinitionForKey(
                        provider.ModuleRow, key, out var exported)
                    || exported != identity.DefinitionId)
                {
                    throw Invalid(
                        "A G1 nominal source type lost its exact provider ExportKey.");
                }
            }

            byte form;
            uint row;
            if (!definition.ProviderExpectations.IsDefaultOrEmpty)
            {
                row = 0;
                foreach (var expectation in definition.ProviderExpectations)
                {
                    if (expectation.ProviderDefinitionId
                            != identity.DefinitionId
                        || !StringComparer.Ordinal.Equals(
                            expectation.ProducerIdentity,
                            provider.ModuleIdentity)
                        || !definition.ExportKeys.Contains(
                            expectation.ExportKey, StringComparer.Ordinal))
                    {
                        throw Invalid(
                            "A G1 nominal TypeDef expectation changed its provider identity.");
                    }

                    ValidateExpectation(expectation);
                    var importRow = CopyImport(expectation.UsingModule,
                        expectation.ImportRow, depth + 1);
                    if (row == 0)
                    {
                        row = importRow;
                    }
                }
                form = 0x03;
            }
            else
            {
                row = CopyDefinition(provider, providerRow, depth + 1);
                if (StringComparer.Ordinal.Equals(_moduleIdentity,
                        provider.ModuleIdentity))
                {
                    form = 0x02;
                }
                else
                {
                    if (definition.ExportKeys.Length != 1)
                    {
                        throw Unsupported(
                            "G1 nominal source emission requires an exact TypeDef ExportKey when no Import expectation is retained.");
                    }

                    row = AddImport(provider.ModuleIdentity,
                        definition.ExportKeys[0], row,
                        provider.TargetRequirement, provider.RuntimePolicy);
                    form = 0x03;
                }
            }
            _typeSpecs.Add(TrRmTypeSpecTableWriter.Reference(form, row));
            reference = checked((uint)_typeSpecs.Count);
            _definitionReferences.Add(identity, reference);
            return reference;
        }

        private void ValidateExpectation(BoundSourceTypeExpectation expectation)
        {
            var snapshots = _definitions.Values.SelectMany(
                    static definition => definition.ProviderExpectations
                        .SelectMany(static view => new[] {
                            view.UsingModule, view.Provider,
                        }).Concat(definition.Provider is { } provider
                            ? [provider] : []))
                .Prepend(expectation.UsingModule);
            var artifacts = new List<TrRmStandardArtifactInput>();
            foreach (var snapshot in snapshots)
            {
                var artifact = Artifact(snapshot);
                if (!artifacts.Any(input =>
                        input.Artifact.Span.SequenceEqual(artifact)))
                {
                    artifacts.Add(new TrRmStandardArtifactInput(
                        snapshot.SchemaIdentity, artifact));
                }
            }
            var context = new GenericMethodReadContext(
                artifacts[0], artifacts.Skip(1).ToArray());
            var tables = Tables(expectation.UsingModule);
            var identities = new string[context.Artifacts.Length];
            Array.Fill(identities, context.SchemaIdentity);
            var binding = TrRmFixedArrayProviderBindingValidator
                .ValidateGenericMethodTypeDefImport(tables,
                    context.Artifacts, identities, context.ModuleSelections,
                    0, expectation.UsingModule.ModuleRow,
                    expectation.ImportRow);
            if (binding.ActualDefinitionId != expectation.ProviderDefinitionId
                || binding.ProducerIdentity != expectation.ProducerIdentity
                || binding.ExportKey != expectation.ExportKey)
            {
                throw Invalid(
                    "A G1 nominal source Import changed its selected provider.");
            }
        }

        private uint CopyDefinition(BoundSourceTypeProviderSnapshot snapshot,
            int oldRow, int depth)
        {
            Visit(depth);
            snapshot = CanonicalSnapshot(snapshot);
            var key = (snapshot, (ushort)0x0013, oldRow);
            if (_remappedRows.TryGetValue(key, out var existing))
            {
                return existing;
            }
            var tables = Tables(snapshot);
            if (tables.TypeDefs.GetOwnerModule(oldRow) != snapshot.ModuleRow
                || tables.TypeDefs.GetContractRange(oldRow).Count != 0
                || tables.Interfaces.GetInterfaceDefForTypeDef(oldRow) != 0
                || tables.TypeDefs.GetKind(oldRow) == 4)
            {
                throw Unsupported(
                    "The G1 nominal declaration copier does not select cross-Module, interface, enum or contracted TypeDefs.");
            }

            var row = SourceRow(snapshot, 0x0013, oldRow, 44);
            var newRow = AddRow(0x0013, row);
            _remappedRows.Add(key, newRow);
            Write(row, 0, Name(snapshot, ReadInt(row, 0)));
            Write(row, 4, Name(snapshot, ReadInt(row, 4)));
            var (firstFormal, formalCount)
                = tables.TypeDefs.GetGenericRange(oldRow);
            var newFormalFirst = formalCount == 0 ? 0 : _formals.Count + 1;
            for (var ordinal = 0; ordinal < formalCount; ordinal++)
            {
                _remappedRows.Add((snapshot, 0x0019, firstFormal + ordinal),
                    checked((uint)_formals.Count + 1));
                _formals.Add(default);
            }
            Write(row, 20, checked((uint)newFormalFirst));
            var (firstField, endField)
                = tables.TypeDefs.GetFieldRange(oldRow);
            var fieldCount = endField - firstField;
            var newFieldFirst = fieldCount == 0 ? 0 : Rows(0x0014).Count + 1;
            for (var ordinal = 0; ordinal < fieldCount; ordinal++)
            {
                var field = SourceRow(snapshot, 0x0014, firstField + ordinal, 16);
                var fieldRow = AddRow(0x0014, field);
                _remappedRows.Add((snapshot, 0x0014, firstField + ordinal),
                    fieldRow);
            }
            Write(row, 12, checked((uint)newFieldFirst));
            var (firstLayout, layoutCount)
                = tables.TypeDefs.GetLayoutRange(oldRow);
            var newLayoutFirst = layoutCount == 0 ? 0 : Rows(0x0015).Count + 1;
            for (var ordinal = 0; ordinal < layoutCount; ordinal++)
            {
                var layout = SourceRow(snapshot, 0x0015,
                    firstLayout + ordinal, 28);
                var layoutRow = AddRow(0x0015, layout);
                _remappedRows.Add((snapshot, 0x0015,
                    firstLayout + ordinal), layoutRow);
            }
            Write(row, 28, checked((uint)newLayoutFirst));

            for (var ordinal = 0; ordinal < formalCount; ordinal++)
            {
                var oldFormal = firstFormal + ordinal;
                var newFormal = newFormalFirst + ordinal;
                var (firstConstraint, constraintCount)
                    = tables.GenericParams.GetConstraintRange(oldFormal);
                var newConstraintFirst = constraintCount == 0
                    ? 0 : _constraints.Count + 1;
                for (var constraint = 0; constraint < constraintCount;
                    constraint++)
                {
                    _constraints.Add(default);
                }
                for (var constraint = 0; constraint < constraintCount;
                    constraint++)
                {
                    var kind = tables.Constraints.GetKind(
                        firstConstraint + constraint);
                    var payload = tables.Constraints.GetPayload(
                        firstConstraint + constraint);
                    if (kind == 1)
                    {
                        payload = CopyTypeSpec(snapshot, payload, depth + 1);
                    }
                    else if (kind != 2)
                    {
                        throw Unsupported(
                            "G1 nominal source emission does not select associated-relation constraints.");
                    }
                    _constraints[newConstraintFirst + constraint - 1]
                        = new TrRmConstraintRow(newFormal, kind, payload);
                }
                var formalKind = tables.GenericParams.GetFormalKind(oldFormal);
                var defaultRef = tables.GenericParams.GetDefaultArgumentRef(
                    oldFormal);
                _formals[newFormal - 1] = new TrRmGenericParamRow(
                    0x1300_0000u | newRow, ordinal,
                    checked((int)Name(snapshot,
                        tables.GenericParams.GetNameStringRow(oldFormal))),
                    newConstraintFirst, constraintCount, formalKind,
                    tables.GenericParams.GetOwnershipDomain(oldFormal),
                    tables.GenericParams.GetNullabilityDomain(oldFormal),
                    formalKind == 2 ? CopyTypeSpec(snapshot,
                        tables.GenericParams.GetConstantTypeSpecRef(oldFormal),
                        depth + 1) : 0,
                    defaultRef == 0 ? 0 : formalKind == 1
                        ? CopyTypeSpec(snapshot, defaultRef, depth + 1)
                        : CopyConstantSource(snapshot,
                            checked((int)defaultRef), depth + 1));
            }
            for (var ordinal = 0; ordinal < fieldCount; ordinal++)
            {
                var field = Rows(0x0014)[newFieldFirst + ordinal - 1];
                Write(field, 0, newRow);
                Write(field, 4, Name(snapshot, ReadInt(field, 4)));
                Write(field, 8, CopyTypeSpec(snapshot,
                    ReadUInt32(field, 8), depth + 1));
            }
            for (var ordinal = 0; ordinal < layoutCount; ordinal++)
            {
                var oldLayout = firstLayout + ordinal;
                var newLayout = newLayoutFirst + ordinal;
                var layout = Rows(0x0015)[newLayout - 1];
                Write(layout, 0, newRow);
                var profile = ReadInt(layout, 4);
                Write(layout, 4, profile == 0 ? 0
                    : CopyTargetProfile(snapshot, profile));
                var (firstMember, memberCount)
                    = tables.Layouts.GetLayoutFieldRange(oldLayout);
                Write(layout, 20, memberCount == 0 ? 0
                    : checked((uint)Rows(0x0023).Count + 1));
                for (var member = 0; member < memberCount; member++)
                {
                    var layoutField = SourceRow(snapshot, 0x0023,
                        firstMember + member, 12);
                    Write(layoutField, 0, checked((uint)newLayout));
                    Write(layoutField, 4, _remappedRows[
                        (snapshot, 0x0014, ReadInt(layoutField, 4))]);
                    _ = AddRow(0x0023, layoutField);
                }
            }
            if (StringComparer.Ordinal.Equals(snapshot.ModuleIdentity,
                    _moduleIdentity))
            {
                foreach (var (definition, exportKey)
                    in tables.Exports.GetExportsForModule(snapshot.ModuleRow))
                {
                    var kind = (ushort)(definition >> 24);
                    if (kind is 0x13 or 0x14
                        && _remappedRows.TryGetValue((snapshot, kind,
                            checked((int)(definition & 0x00FF_FFFF))),
                            out var mapped)
                        && (kind == 0x13 ? mapped == newRow
                            : mapped >= newFieldFirst
                                && mapped < newFieldFirst + fieldCount))
                    {
                        Exports.Add(new TrRmExportRow(
                            ((uint)kind << 24) | mapped,
                            checked((int)AddName(exportKey))));
                    }
                }
            }
            return newRow;
        }

        private uint CopyTypeSpec(BoundSourceTypeProviderSnapshot snapshot,
            uint reference, int depth)
        {
            Visit(depth);
            snapshot = CanonicalSnapshot(snapshot);
            var tables = Tables(snapshot);
            var type = tables.TypeSpecs.GetTypeSpecRef(reference,
                allowInitQualifier: true);
            if (!type.IsRow)
            {
                return reference;
            }
            var key = (snapshot, (ushort)0x0011, type.Row);
            if (_remappedRows.TryGetValue(key, out var existing))
            {
                return existing;
            }
            var row = tables.TypeSpecs.GetRow(type.Row).ToArray();
            // Declaration edges can return to this TypeSpec through a
            // recursive nominal field without forming a TypeSpec cycle.
            _typeSpecs.Add(row);
            var result = checked((uint)_typeSpecs.Count);
            _remappedRows.Add(key, result);
            var child = row.Length >= 6 ? ReadUInt32(row, 2) : 0;
            switch (type.Form)
            {
                case 0x01:
                    break;
                case 0x02:
                    Write(row, 2, CopyDefinition(snapshot,
                        checked((int)child), depth + 1));
                    break;
                case 0x03:
                    Write(row, 2, CopyImport(snapshot,
                        checked((int)child), depth + 1));
                    break;
                case 0x04:
                    if (!_remappedRows.TryGetValue((snapshot, 0x0019,
                            checked((int)child)), out var formal))
                    {
                        throw Unsupported(
                            "A G1 declaration TypeSpec refers to a foreign formal owner.");
                    }
                    Write(row, 2, formal);
                    break;
                case 0x05:
                    Write(row, 2, CopyTypeSpec(snapshot, child, depth + 1));
                    var owner = tables.TypeSpecs.GetNominalTypeDefRow(
                        child, tables.TypeDefs, tables.Imports);
                    var (first, count)
                        = tables.TypeDefs.GetGenericRange(owner);
                    for (var ordinal = 0; ordinal < count; ordinal++)
                    {
                        var argument = ReadUInt32(row, 6 + (ordinal * 4));
                        Write(row, 6 + (ordinal * 4),
                            tables.GenericParams.GetFormalKind(
                                first + ordinal) == 1
                                ? CopyTypeSpec(snapshot, argument, depth + 1)
                                : CopyConstantSource(snapshot,
                                    checked((int)argument), depth + 1));
                    }
                    break;
                case >= 0x06 and <= 0x0B:
                case 0x0E:
                    Write(row, 2, CopyTypeSpec(snapshot, child, depth + 1));
                    break;
                case 0x0C:
                    Write(row, 3, CopyTypeSpec(snapshot,
                        ReadUInt32(row, 3), depth + 1));
                    break;
                default:
                    throw Unsupported(
                        "A G1 nominal declaration uses an unsupported TypeSpec form.");
            }
            return result;
        }

        private uint CopyImport(BoundSourceTypeProviderSnapshot snapshot,
            int oldRow, int depth)
        {
            Visit(depth);
            snapshot = CanonicalSnapshot(snapshot);
            var key = (snapshot, (ushort)0x001C, oldRow);
            if (_remappedRows.TryGetValue(key, out var existing))
            {
                return existing;
            }
            var tables = Tables(snapshot);
            var binding = tables.Imports.GetBindingForImportRow(oldRow);
            var expected = tables.Imports.GetExpectedDefinitionForImportRow(
                oldRow);
            if ((expected >> 24) != 0x13)
            {
                throw Unsupported(
                    "A G1 nominal declaration dependency is not a TypeDef Import.");
            }
            var row = new byte[12];
            var result = AddRow(0x001C, row);
            _remappedRows.Add(key, result);
            Write(row, 0, ModuleReference(binding.ProducerIdentity,
                binding.TargetRequirement, binding.RuntimePolicy));
            Write(row, 4, AddName(binding.ExportKey));
            Write(row, 8, 0x1300_0000u | CopyDefinition(snapshot,
                checked((int)(expected & 0x00FF_FFFF)), depth + 1));
            return result;
        }

        private uint AddImport(string identity, string exportKey,
            uint expectedRow, uint targetRequirement, uint runtimePolicy)
        {
            var row = new byte[12];
            Write(row, 0, ModuleReference(identity, targetRequirement,
                runtimePolicy));
            Write(row, 4, AddName(exportKey));
            Write(row, 8, 0x1300_0000u | expectedRow);
            return AddRow(0x001C, row);
        }

        private uint ModuleReference(string identity, uint targetRequirement,
            uint runtimePolicy)
        {
            var rows = Rows(0x0010);
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                if (_names[ReadInt(row, 0) - 1].Text != identity)
                {
                    continue;
                }
                if (ReadUInt32(row, 4) != targetRequirement
                    || ReadUInt32(row, 8) != runtimePolicy)
                {
                    throw Invalid(
                        "G1 nominal Imports disagree on provider requirements.");
                }
                return checked((uint)index + 1);
            }
            var added = new byte[12];
            Write(added, 0, AddName(identity));
            Write(added, 4, targetRequirement);
            Write(added, 8, runtimePolicy);
            return AddRow(0x0010, added);
        }

        private uint ConstantArgument(BoundGenericNominalTypeArgument argument,
            BoundGenericMethodDefinition method,
            Dictionary<PrimitiveType, uint> formalRows,
            Dictionary<PrimitiveType, uint> builtInRows, int depth)
        {
            if (argument.HasConstantFormalIdentity
                || !argument.HasDecodedConstant
                || argument.ConstantDomainType is not PrimitiveType domain
                || argument.ConstantCarrierType != domain
                || !PrimitiveTypeFacts.IsBuiltInScalar(domain)
                || domain == PrimitiveType.Void)
            {
                throw Unsupported(
                    "G1 nominal source emission requires a closed scalar constant actual.");
            }
            var bits = new ScalarBits(argument.ConstantLow,
                argument.ConstantHigh);
            if (!bits.IsCanonicalFor(domain))
            {
                throw Invalid("A G1 nominal constant actual has invalid scalar bits.");
            }
            var payload = new byte[PrimitiveTypeFacts.ScalarWidth(domain)];
            bits.WritePayload(domain, payload);
            _blobs.Add(payload);
            var node = new byte[24];
            Write(node, 0, GenericMethodTypeSpecRef(domain, formalRows,
                builtInRows, _typeSpecs, method.ProofProgram.SpanTypes,
                method.ProofProgram.CheckedReferenceTypes,
                method.DefinitionOrdinal, method,
                nominalTypes: this, depth: depth + 1));
            node[4] = 1;
            Write(node, 8, checked((uint)_blobs.Count));
            var source = new byte[8];
            Write(source, 0, 1);
            Write(source, 4, AddRow(0x003F, node));
            return AddRow(0x0030, source);
        }

        private uint CopyConstantSource(
            BoundSourceTypeProviderSnapshot snapshot, int oldRow, int depth)
        {
            Visit(depth);
            snapshot = CanonicalSnapshot(snapshot);
            var key = (snapshot, (ushort)0x0030, oldRow);
            if (_remappedRows.TryGetValue(key, out var existing))
            {
                return existing;
            }
            var tables = Tables(snapshot);
            if (tables.ConstantSources.GetKind(oldRow) != 1)
            {
                throw Unsupported(
                    "G1 nominal declaration defaults require frozen scalar ConstantSources.");
            }
            var nodeRow = checked((int)tables.ConstantSources
                .GetPayloadReference(oldRow));
            var payload = tables.FrozenValueNodes.GetScalarPayload(
                nodeRow, tables.Blobs).ToArray();
            var node = SourceRow(snapshot, 0x003F, nodeRow, 24);
            Write(node, 0, CopyTypeSpec(snapshot, ReadUInt32(node, 0), depth + 1));
            if (payload.Length != 0)
            {
                _blobs.Add(payload);
            }
            Write(node, 8, payload.Length == 0 ? 0
                : checked((uint)_blobs.Count));
            var source = new byte[8];
            Write(source, 0, 1);
            Write(source, 4, AddRow(0x003F, node));
            var result = AddRow(0x0030, source);
            _remappedRows.Add(key, result);
            return result;
        }

        private uint CopyTargetProfile(BoundSourceTypeProviderSnapshot snapshot,
            int oldRow)
        {
            var source = SourceRow(snapshot, 0x0048, oldRow, 8);
            var rows = Rows(0x0048);
            var index = rows.FindIndex(row => row.AsSpan().SequenceEqual(source));
            return index < 0 ? AddRow(0x0048, source)
                : checked((uint)index + 1);
        }

        private byte[] Artifact(BoundSourceTypeProviderSnapshot snapshot)
        {
            snapshot = CanonicalSnapshot(snapshot);
            if (!_artifacts.TryGetValue(snapshot, out var artifact))
            {
                var expectedSchema = _schema switch {
                    TrRmArtifactSchema.GenericMethodCallsDraft
                        => StandardGenericMethodCallsSchemaIdentity,
                    TrRmArtifactSchema.GenericMethodCallsParameterLabelsDraft
                        => StandardGenericMethodCallsParameterLabelsSchemaIdentity,
                    TrRmArtifactSchema.GenericMethodCallsParameterLabelsReceiverAccessDraft
                        => StandardGenericMethodCallsParameterLabelsReceiverAccessSchemaIdentity,
                    _ => throw Unsupported(
                        "Nominal generic source types require an exact G1 schema."),
                };
                if (snapshot.SchemaIdentity != expectedSchema)
                {
                    throw Unsupported(
                        "G1 nominal declaration snapshots require the selected exact schema.");
                }
                artifact = snapshot.ArtifactSnapshot.ToArray();
                _artifacts.Add(snapshot, artifact);
            }
            return artifact;
        }

        private TrRmStructuralMetadataValidator.ValidatedExecutionTables Tables(
            BoundSourceTypeProviderSnapshot snapshot)
        {
            snapshot = CanonicalSnapshot(snapshot);
            var tables = TrRmStructuralMetadataValidator
                .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                    new TrRmArtifactIndex(Artifact(snapshot), _schema));
            if (tables.Modules.GetIdentity(snapshot.ModuleRow)
                    != snapshot.ModuleIdentity
                || tables.Modules.GetRequirements(snapshot.ModuleRow)
                    != (snapshot.TargetRequirement, snapshot.RuntimePolicy)
                || tables.Modules.GetGoverningRuleEdition(snapshot.ModuleRow)
                    != snapshot.GoverningRuleEdition)
            {
                throw Invalid("A G1 nominal declaration snapshot changed its Module.");
            }
            _ = _usedSnapshots.Add(snapshot);
            return tables;
        }

        private BoundSourceTypeProviderSnapshot CanonicalSnapshot(
            BoundSourceTypeProviderSnapshot snapshot)
        {
            if (_snapshots.TryGetValue(snapshot, out var canonical))
            {
                return canonical;
            }
            if (_modules.TryGetValue(snapshot.ModuleIdentity, out canonical))
            {
                if (canonical.SchemaIdentity != snapshot.SchemaIdentity
                    || canonical.ModuleRow != snapshot.ModuleRow
                    || !canonical.ArtifactSnapshot.AsSpan().SequenceEqual(
                        snapshot.ArtifactSnapshot.AsSpan()))
                {
                    throw Invalid(
                        "G1 nominal source types select conflicting snapshots of one Module.");
                }
            }
            else
            {
                canonical = snapshot;
                _modules.Add(snapshot.ModuleIdentity, canonical);
            }
            _snapshots.Add(snapshot, canonical);
            return canonical;
        }

        private byte[] SourceRow(BoundSourceTypeProviderSnapshot snapshot,
            ushort kind, int row, int width)
        {
            var index = new TrRmArtifactIndex(Artifact(snapshot), _schema);
            if (!index.TryGetSection(kind, out var bytes, out var count)
                || row <= 0 || row > count || bytes.Length != count * width)
            {
                throw Invalid("A G1 nominal declaration row is missing.");
            }
            return bytes.Slice((row - 1) * width, width).ToArray();
        }

        private List<byte[]> Rows(ushort kind)
        {
            if (!_rows.TryGetValue(kind, out var rows))
            {
                var width = kind switch {
                    0x0048 or 0x0030 => 8,
                    0x0010 or 0x001C or 0x0023 => 12,
                    0x0013 => 44,
                    0x0014 => 16,
                    0x0015 => 28,
                    0x003F => 24,
                    _ => throw new InvalidOperationException(),
                };
                rows = [];
                if (TryGetSection(_sections, kind, out var section))
                {
                    for (var index = 0; index < section.RecordCount; index++)
                    {
                        rows.Add(section.Bytes.Slice(
                            checked(index * width), width).ToArray());
                    }
                }
                _rows.Add(kind, rows);
            }
            return rows;
        }

        private uint AddRow(ushort kind, byte[] row)
        {
            var rows = Rows(kind);
            if (rows.Count == 0x00FF_FFFF)
            {
                throw Unsupported("A G1 nominal declaration table exceeds its row limit.");
            }
            rows.Add(row);
            return checked((uint)rows.Count);
        }

        private uint Name(BoundSourceTypeProviderSnapshot snapshot, int row)
        {
            var index = new TrRmArtifactIndex(Artifact(snapshot), _schema);
            _ = index.TryGetSection(0x0002, out var bytes, out var count);
            var strings = new TrRmStringTableReader(bytes, count);
            return AddName(strings.DecodeNamespace(row));
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset)
            => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);

        private uint AddName(string text)
        {
            _names.Add((text, 1));
            return checked((uint)_names.Count);
        }

        private void Visit(int depth)
        {
            if (depth > SemanticVerifier.MaximumDepth
                || --_remainingVisits < 0)
            {
                throw Unsupported(
                    "G1 nominal source emission exceeds its declaration traversal limit.");
            }
        }

        internal void Complete()
        {
            foreach (var (kind, rows) in _rows)
            {
                SetSection(_sections, kind, checked((uint)rows.Count),
                    rows.SelectMany(static row => row).ToArray());
            }
            if (_blobs.Count != 0)
            {
                SetSection(_sections, 0x0003, checked((uint)_blobs.Count),
                    TrRmVariableTableWriter.EncodeBlobs(_blobs));
            }
        }

        internal void MergeFeatures(List<TrRmFeatureRow> features)
        {
            if (GetCount(_sections, 0x0030) != 0
                && !features.Any(static feature => feature.FeatureId
                    == TrRmFeatureTableReader.TypedFrozenValuesFeatureId))
            {
                features.Add(TrRmFeatureRow.TypedFrozenValues);
            }
            foreach (var snapshot in _usedSnapshots)
            {
                foreach (var feature in snapshot.Features)
                {
                    var existing = features.FindIndex(candidate =>
                        candidate.FeatureId == feature.FeatureId);
                    if (existing < 0)
                    {
                        features.Add(new TrRmFeatureRow(feature.FeatureId,
                            feature.RuleRevision));
                    }
                    else if (features[existing].RuleRevision
                        != feature.RuleRevision)
                    {
                        throw Unsupported(
                            "G1 nominal declaration manifests disagree on a Feature revision.");
                    }
                }
            }
        }
    }
}
