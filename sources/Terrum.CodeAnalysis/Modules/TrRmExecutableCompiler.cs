// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Emits and audits root-free fail-fast scalar compact TRRM bodies.</summary>
public static partial class TrRmExecutableCompiler
{
    private static readonly PrimitiveType[] s_types =
    [
        PrimitiveType.Void, PrimitiveType.Bool, PrimitiveType.Char16,
        PrimitiveType.Int32, PrimitiveType.UInt32, PrimitiveType.Int64, PrimitiveType.UInt64,
        PrimitiveType.Float32, PrimitiveType.Float64,
    ];
    private static readonly byte[] s_codes = [0, 1, 3, 8, 0x0D, 9, 0x0E, 0x14, 0x15];
    private static readonly ReadOnlyMemory<byte>[] s_typeRows = CreateTypeRows(s_codes);
    private static readonly ReadOnlyMemory<byte>[] s_nonFloatTypeRows = CreateTypeRows(s_codes[..^2]);
    private static readonly ReadOnlyMemory<byte>[] s_effectPayloads =
    [
        ReadOnlyMemory<byte>.Empty,
        new byte[] { 1, 0, 0, 0 },
        new byte[] { 2, 0, 0, 0 },
        new byte[] { 3, 0, 0, 0 },
        new byte[] { 4, 0, 0, 0 },
        new byte[] { 5, 0, 0, 0 },
        new byte[] { 6, 0, 0, 0 },
        new byte[] { 7, 0, 0, 0 },
    ];

    public static byte[] Emit(BoundProgram program, string moduleIdentity)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (program.Functions.Any(static function =>
            function.IsExternal && function.DirectNativeImport is null
                && (function.NativeFunctionPointerType is null || function.ModuleImport is null)))
        {
            throw Unsupported("External TRRM declarations require a direct import or retained native callable dependency.");
        }

        if (string.IsNullOrEmpty(moduleIdentity))
        {
            throw new ArgumentException("A module identity is required.", nameof(moduleIdentity));
        }

        if (program.EntryPoint == -1)
        {
            SemanticVerifier.VerifyLibraryReferences(program);
        }
        else if (program.Functions.Any(static function => function.IsExternal))
        {
            SemanticVerifier.VerifyReferences(program);
        }
        else
        {
            SemanticVerifier.Verify(program);
        }

        if (!NativePointerTypeContract.IsValidTable(program.NativePointerTypes,
            program.Structures, program.NativeFunctionPointerSignatures))
        {
            throw Unsupported("The native pointer or function-pointer type table is invalid.");
        }
        foreach (var function in program.Functions)
        {
            if (function.NativeFunctionPointerType is not PrimitiveType nativeFunctionPointerType)
            {
                continue;
            }

            if (function.DirectNativeImport is not null
                || !NativeFunctionPointerContract.MatchesFunction(function,
                    nativeFunctionPointerType, program.NativeFunctionPointerSignatures))
            {
                throw Unsupported($"Function '{function.Name}' has an invalid native callable contract.");
            }

            var signatureIndex = NativeFunctionPointerTypes.Index(nativeFunctionPointerType);
            var signature = program.NativeFunctionPointerSignatures[signatureIndex];
            if ((signature.ReturnType != PrimitiveType.Void
                    && !NativeFunctionPointerContract.IsSupportedValueType(
                        signature.ReturnType, program.NativeFunctionPointerSignatures))
                || signature.ParameterTypes.Any(type =>
                    !NativeFunctionPointerContract.IsSupportedValueType(
                        type, program.NativeFunctionPointerSignatures)))
            {
                throw Unsupported($"Function '{function.Name}' has a native callable type outside the TRRM value subset.");
            }
        }
        var blittableStructureIndexes = CollectNativeBlittableStructureIndexes(program);
        if (program.RuntimePolicy != BoundRuntimePolicy.FailFast
            || !program.Classes.IsEmpty || !program.Buffers.IsEmpty
            || (blittableStructureIndexes.IsEmpty
                ? !program.Structures.IsEmpty
                : program.EntryPoint != -1
                    || program.Functions.Any(static function =>
                        !function.IsExternal && function.NativeVTableMethod is null)))
        {
            throw Unsupported("Executable TRRM supports fail-fast scalar bodies and declaration-only native boundary libraries.");
        }
        if (program.Functions.Any(static function => function.IndexedBody is not null))
        {
            throw Unsupported("An audited indexed Body cannot be reserialized as structured source.");
        }
        if (program.EntryPoint >= 0
            && (program.Functions[program.EntryPoint].ReturnType != PrimitiveType.Int32
                || program.Functions[program.EntryPoint].ParameterCount != 0))
        {
            throw Unsupported("Executable entry must return int32 and take no parameters.");
        }
        if (program.Functions.Any(static function =>
                function.Variables.Any(static variable =>
                    variable.IsImplicitRef || variable.IsImplicitReferenceTemporary)))
        {
            throw Unsupported("Executable TRRM does not persist implicit reference parameter contracts.");
        }

        var functionFeatures = new (bool HasFloatingTypes, bool HasIndexedFlow,
            bool HasNonScalarStorage)[program.Functions.Length];
        var outputFeatures = new bool[program.Functions.Length];
        var hasFloatingTypes = false;
        var parameterCount = 0;
        var effectCount = 0;
        var exportCount = 0;
        var nativeImportCount = 0;
        var nativeCallableCount = 0;
        var nativeCallableContractCount = 0;
        var nativeVTableCount = 0;
        for (var index = 0; index < program.Functions.Length; index++)
        {
            var function = program.Functions[index];
            if (function.IsExternal && function.DirectNativeImport is not null
                && !DirectNativeImportContract.IsValid(function, program.Structures,
                    program.NativeFunctionPointerSignatures, program.NativePointerTypes))
            {
                throw Unsupported($"Function '{function.Name}' has an invalid direct native import contract.");
            }
            if (function.ModuleImport is BoundModuleImport moduleImport
                && (!((function.IsExternal && function.NativeFunctionPointerType is not null)
                        || (!function.IsExternal && function.NativeVTableMethod is not null
                            && function.Intrinsic == BoundIntrinsic.NativeVTableCall))
                    || string.IsNullOrEmpty(moduleImport.ProducerIdentity)
                    || moduleImport.ExportKey != function.Name
                    || moduleImport.TargetRequirement != 1 || moduleImport.RuntimePolicy != 0))
            {
                throw Unsupported($"Function '{function.Name}' has an invalid native dependency.");
            }
            var hasNativeVTable = function.NativeVTableMethod is not null;
            if (hasNativeVTable
                && (!NativeVTableCallContract.IsValid(function, program)
                    || function.Intrinsic != BoundIntrinsic.NativeVTableCall))
            {
                throw Unsupported($"Function '{function.Name}' has an invalid native vtable contract.");
            }
            var features = function.IsExternal || hasNativeVTable ? default : AnalyzeFunction(function);
            functionFeatures[index] = features;
            outputFeatures[index] = !function.IsExternal && !hasNativeVTable && HasOutputFeature(function);
            hasFloatingTypes |= features.HasFloatingTypes;
            if (function.DirectNativeImport is DirectNativeImport directImport)
            {
                hasFloatingTypes |= IsFloat(function.ReturnType);
                hasFloatingTypes |= directImport.Outputs.Any(static output => IsFloat(output.PointeeType));
                hasFloatingTypes |= directImport.Parameters.Any(static parameter => IsFloat(parameter.PointeeType));
                hasFloatingTypes |= function.Variables.Take(function.ParameterCount)
                    .Any(static variable => IsFloat(variable.Type));
            }
            if (hasNativeVTable)
            {
                hasFloatingTypes |= IsFloat(function.ReturnType)
                    || function.Variables.Take(function.ParameterCount)
                        .Any(static variable => IsFloat(variable.Type));
            }
            parameterCount = checked(parameterCount + function.ParameterCount);
            effectCount += function.IsNoThrow || function.DoesNotReturn || function.RequiresUnsafeCaller ? 1 : 0;
            exportCount += !function.IsPrivate && !function.IsInternal
                && function.ModuleImport is null ? 1 : 0;
            nativeImportCount += function.DirectNativeImport is not null ? 1 : 0;
            nativeCallableCount += function.NativeFunctionPointerType is not null
                && !function.IsExternal ? 1 : 0;
            nativeCallableContractCount += function.NativeFunctionPointerType is not null ? 1 : 0;
            nativeVTableCount += hasNativeVTable ? 1 : 0;
        }
        foreach (var signature in program.NativeFunctionPointerSignatures)
        {
            hasFloatingTypes |= IsFloat(signature.ReturnType)
                || signature.ParameterTypes.Any(IsFloat);
        }
        foreach (var structureIndex in blittableStructureIndexes)
        {
            hasFloatingTypes |= program.Structures[structureIndex].Fields
                .Any(static field => IsFloat(field.Type));
        }
        hasFloatingTypes |= program.NativePointerTypes.Any(
            static pointer => IsFloat(pointer.PointeeType));
        var names = new List<(string Text, int Encoding)>(
            program.Functions.Length + program.NativeFunctionPointerSignatures.Length + 1)
        {
            (moduleIdentity, 1),
        };
        var moduleImportIdentities = program.Functions
            .Where(static function => function.ModuleImport is not null)
            .Select(static function => function.ModuleImport!.ProducerIdentity)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static identity => identity, StringComparer.Ordinal)
            .ToArray();
        var moduleReferenceStringRows = new Dictionary<string, int>(
            moduleImportIdentities.Length, StringComparer.Ordinal);
        foreach (var identity in moduleImportIdentities)
        {
            moduleReferenceStringRows.Add(identity, names.Count + 1);
            names.Add((identity, 1));
        }
        var structureTypeDefRows = new Dictionary<int, int>(blittableStructureIndexes.Length);
        var structureNamespaceRows = new int[blittableStructureIndexes.Length];
        var structureNameRows = new int[blittableStructureIndexes.Length];
        var structureFieldNameRows = new int[blittableStructureIndexes.Length][];
        for (var index = 0; index < blittableStructureIndexes.Length; index++)
        {
            var structureIndex = blittableStructureIndexes[index];
            var structure = program.Structures[structureIndex];
            var separator = structure.Name.LastIndexOf('.', StringComparison.Ordinal);
            var typeNamespace = separator < 0 ? "" : structure.Name[..separator];
            var typeName = separator < 0 ? structure.Name : structure.Name[(separator + 1)..];
            if (typeName.Length == 0 || !structureTypeDefRows.TryAdd(structureIndex, index + 1))
            {
                throw Unsupported($"Native Blittable structure identity '{structure.Name}' cannot be represented in TRRM.");
            }
            structureNamespaceRows[index] = names.Count + 1;
            names.Add((typeNamespace, 1));
            structureNameRows[index] = names.Count + 1;
            names.Add((typeName, 1));
            structureFieldNameRows[index] = new int[structure.Fields.Length];
            for (var field = 0; field < structure.Fields.Length; field++)
            {
                structureFieldNameRows[index][field] = names.Count + 1;
                names.Add((structure.Fields[field].Name, 1));
            }
        }

        var nameRows = new int[program.Functions.Length];
        var nativeLibraryRows = new int[program.Functions.Length];
        var nativeExportRows = new int[program.Functions.Length];
        var nativeCallableEntryNameRows = new int[program.Functions.Length];
        var nativeCallableEntryNames = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(program.Functions.Length, StringComparer.Ordinal);
        var bodies = new List<ReadOnlyMemory<byte>>(program.Functions.Length);
        var signatures = new List<TrRmMethodSigRow>(program.Functions.Length);
        var parameters = new List<TrRmParamRow>(parameterCount);
        var methods = new List<TrRmMethodDefRow>(
            program.Functions.Length + program.NativeFunctionPointerSignatures.Length);
        var contracts = new List<TrRmContractRow>(
            effectCount + nativeImportCount + nativeCallableCount + nativeVTableCount
                + blittableStructureIndexes.Length
            + (2 * program.NativeFunctionPointerSignatures.Length));
        var structureContractFirst = new int[blittableStructureIndexes.Length];
        for (var index = 0; index < blittableStructureIndexes.Length; index++)
        {
            structureContractFirst[index] = contracts.Count + 1;
            contracts.Add(new TrRmContractRow(0x1300_0000u | (uint)(index + 1), 0x13,
                NativeBlittableLayoutPayload(FixedLayoutStorage.WindowsX64TargetRequirement)));
        }
        var exports = new List<TrRmExportRow>(exportCount);
        var typeRowsList = (hasFloatingTypes ? s_typeRows : s_nonFloatTypeRows).ToList();
        var hasNativeRawPointerType = program.Functions.Any(static function =>
            function.ReturnType == PrimitiveType.NativeRawPointer
            || function.Variables.Any(static variable => variable.Type == PrimitiveType.NativeRawPointer))
            || program.NativeFunctionPointerSignatures.Any(static signature =>
                signature.ReturnType == PrimitiveType.NativeRawPointer
                || signature.ParameterTypes.Contains(PrimitiveType.NativeRawPointer))
            || program.NativePointerTypes.Any(static type =>
                type.PointeeType == PrimitiveType.NativeRawPointer)
            || program.Structures.Any(static structure => structure.Fields.Any(
                static field => field.Type == PrimitiveType.NativeRawPointer));
        var nativeRawPointerTypeRow = 0;
        if (hasNativeRawPointerType)
        {
            nativeRawPointerTypeRow = typeRowsList.Count + 1;
            typeRowsList.Add(TrRmTypeSpecTableWriter.InlineFamily(0x8C, 0));
        }
        var nativeFunctionPointerTypeRows =
            new Dictionary<PrimitiveType, int>(program.NativeFunctionPointerSignatures.Length);
        for (var signature = 0;
            signature < program.NativeFunctionPointerSignatures.Length; signature++)
        {
            var row = typeRowsList.Count + 1;
            typeRowsList.Add(TrRmTypeSpecTableWriter.Reference(0x0D,
                checked((uint)(signature + 1))));
            nativeFunctionPointerTypeRows.Add(NativeFunctionPointerTypes.Type(signature), row);
        }
        var nativePointerTypeRows = new Dictionary<PrimitiveType, int>(program.NativePointerTypes.Length);
        var nativeOutputTypeRows = new Dictionary<PrimitiveType, int>();
        var nativeParameterTypeRows = new Dictionary<PrimitiveType, int>();
        if (nativeRawPointerTypeRow != 0)
        {
            nativeParameterTypeRows.Add(PrimitiveType.Void, nativeRawPointerTypeRow);
        }
        var blittableParameterTypeRows = new Dictionary<PrimitiveType, int>();
        var typeDefTypeSpecRows = new Dictionary<int, int>();
        var structureFieldFirst = new int[blittableStructureIndexes.Length];
        var fieldRows = new List<TrRmFieldDefRow>();
        var layoutRows = new List<TrRmLayoutRow>(blittableStructureIndexes.Length);
        var fixedLayoutAlignments = blittableStructureIndexes.IsEmpty
            ? [] : FixedLayoutStorage.Validate(program.Structures);
        for (var index = 0; index < blittableStructureIndexes.Length; index++)
        {
            var structureIndex = blittableStructureIndexes[index];
            var structure = program.Structures[structureIndex];
            structureFieldFirst[index] = structure.Fields.IsEmpty ? 0 : fieldRows.Count + 1;
            for (var field = 0; field < structure.Fields.Length; field++)
            {
                var fieldDefinition = structure.Fields[field];
                var fieldTypeRow = NativeBlittableFieldTypeRow(typeRowsList,
                    typeDefTypeSpecRows, structureTypeDefRows, fieldDefinition.Type,
                    nativeRawPointerTypeRow, nativeFunctionPointerTypeRows,
                    nativePointerTypeRows, program.NativePointerTypes);
                var visibility = fieldDefinition.IsPrivate ? 0u
                    : fieldDefinition.IsInternal ? 1u : 2u;
                var fieldFlags = visibility | (fieldDefinition.IsMutable ? 4u : 0u);
                fieldRows.Add(new TrRmFieldDefRow(index + 1, structureFieldNameRows[index][field],
                    fieldTypeRow, fieldFlags, (uint)fieldDefinition.Offset));
            }

            layoutRows.Add(new TrRmLayoutRow(index + 1,
                FixedLayoutStorage.WindowsX64TargetRequirement, (uint)structure.Size,
                (uint)fixedLayoutAlignments[structureIndex], 0, 0));
        }
        foreach (var function in program.Functions)
        {
            foreach (var variable in function.Variables)
            {
                if (NativePointerTypes.IsType(variable.Type))
                {
                    _ = NativeValueTypeRow(variable.Type, nativeRawPointerTypeRow,
                        nativeFunctionPointerTypeRows, typeRowsList, nativePointerTypeRows,
                        program.NativePointerTypes, structureTypeDefRows, typeDefTypeSpecRows);
                }
            }
            if (NativePointerTypes.IsType(function.ReturnType))
            {
                _ = NativeValueTypeRow(function.ReturnType, nativeRawPointerTypeRow,
                    nativeFunctionPointerTypeRows, typeRowsList, nativePointerTypeRows,
                    program.NativePointerTypes, structureTypeDefRows, typeDefTypeSpecRows);
            }
        }
        for (var index = 0; index < program.Functions.Length; index++)
        {
            var function = program.Functions[index];
            var hasNativeVTable = function.NativeVTableMethod is not null;
            if (!seen.Add(function.Name) || function.IsConstructor
                || function.IsClassConstructor || function.ClassConstructor != -1
                || function.DeclaringStruct is not null || function.PropertyName is not null
                || function.IsStatic
                || (function.Intrinsic != BoundIntrinsic.None
                    && !(hasNativeVTable && function.Intrinsic == BoundIntrinsic.NativeVTableCall))
                || function.IsCleanup
                || (!Scalar(function.ReturnType)
                    && !(function.IsExternal && function.DirectNativeImport is not null
                        && function.ReturnType == PrimitiveType.NativeRawPointer)
                    && !NativeFunctionPointerTypes.IsType(function.ReturnType)
                    && !NativePointerTypes.IsType(function.ReturnType)
                    && !(hasNativeVTable && function.ReturnType == PrimitiveType.NativeRawPointer)))
            {
                throw Unsupported($"Function '{function.Name}' has an unsupported declaration or effect.");
            }
            if (!function.IsExternal && functionFeatures[index].HasNonScalarStorage
                && !outputFeatures[index])
            {
                throw Unsupported($"Function '{function.Name}' contains non-scalar storage.");
            }
            var hasOnlyNativeVTableOutputs = function.NativeVTableMethod
                    is NativeVTableMethodDefinition vtableMethod
                && !vtableMethod.Outputs.IsDefaultOrEmpty
                && vtableMethod.Outputs.Length == function.Variables
                    .Take(function.ParameterCount)
                    .Count(static parameter => parameter.IsOut);
            if (function.DirectNativeImport is null && !hasOnlyNativeVTableOutputs
                && HasOutputParameters(function) && !function.IsPrivate && !function.IsInternal)
            {
                throw Unsupported($"Output function '{function.Name}' must not be exported.");
            }

            nameRows[index] = names.Count + 1;
            names.Add((function.Name, 1));
            if (function.DirectNativeImport is DirectNativeImport directImport)
            {
                nativeLibraryRows[index] = names.Count + 1;
                names.Add((directImport.LibraryName, 0));
                nativeExportRows[index] = names.Count + 1;
                names.Add((directImport.ExportName, 0));
            }
            if (function.NativeFunctionPointerType is not null
                && function.NativeCallableEntryName is string entryName)
            {
                if (!DirectNativeImportContract.IsValidExportName(entryName)
                    || !nativeCallableEntryNames.Add(entryName))
                {
                    throw Unsupported($"Function '{function.Name}' has an invalid or duplicate native callable entry name.");
                }

                nativeCallableEntryNameRows[index] = names.Count + 1;
                names.Add((entryName, 0));
            }
            else if (function.NativeCallableEntryName is not null)
            {
                throw Unsupported($"Function '{function.Name}' has a native callable entry name without a native callable contract.");
            }

            var first = function.ParameterCount == 0 ? 0 : parameters.Count + 1;
            for (var parameter = 0; parameter < function.ParameterCount; parameter++)
            {
                var variable = function.Variables[parameter];
                var nativeOutput = DirectNativeImportContract.OutputForParameter(function, parameter)
                    ?? NativeVTableCallContract.OutputForParameter(function, parameter);
                var nativeParameter = function.NativeVTableMethod is not null
                    ? NativeVTableCallContract.ParameterForParameter(function, parameter)
                    : DirectNativeImportContract.ParameterForParameter(function, parameter);
                var isNativeOutput = nativeOutput is not null;
                var isNativeCallableValue = function.NativeFunctionPointerType is not null
                    && NativeFunctionPointerContract.IsSupportedValueType(variable.Type,
                        program.NativeFunctionPointerSignatures);
                var isNativeFunctionPointerValue = NativeFunctionPointerTypes.IsType(variable.Type);
                var isNativePointerValue = NativePointerTypes.IsType(variable.Type);
                var parameterType = isNativeOutput
                    ? NativeOutputTypeRow(typeRowsList, nativeOutputTypeRows, nativeOutput!.PointeeType)
                    : nativeParameter is not null
                        ? NativeParameterTypeRow(typeRowsList, nativeParameterTypeRows,
                            nativeParameter.PointeeType, structureTypeDefRows,
                            typeDefTypeSpecRows, blittableParameterTypeRows)
                        : NativeValueTypeRow(variable.Type, nativeRawPointerTypeRow,
                            nativeFunctionPointerTypeRows, typeRowsList, nativePointerTypeRows,
                            program.NativePointerTypes, structureTypeDefRows, typeDefTypeSpecRows);
                if (variable.Type == PrimitiveType.Void || variable.IsInitializationLocal
                    || (function.DirectNativeImport is null && !hasNativeVTable
                        && !outputFeatures[index]
                        && ((!Scalar(variable.Type) && !isNativeCallableValue
                                && !isNativeFunctionPointerValue && !isNativePointerValue)
                            || variable.IsOut != variable.IsInit
                            || (variable.IsInit && !IsOutputScalarType(variable.Type))))
                    || (hasNativeVTable
                        && (isNativeOutput
                            ? variable.Type != PrimitiveType.NativeStorageAddress
                                || !variable.IsOut || variable.IsInit
                            : variable.IsOut || variable.IsInit
                                || (!Scalar(variable.Type)
                                    && variable.Type != PrimitiveType.NativeRawPointer
                                    && !isNativePointerValue
                                    && !NativeFunctionPointerTypes.IsType(variable.Type))))
                    || (function.DirectNativeImport is not null
                        && (isNativeOutput
                            ? (variable.Type != PrimitiveType.NativeStorageAddress
                                || !variable.IsOut || variable.IsInit)
                            : nativeParameter is not null
                                ? (variable.Type != PrimitiveType.NativeRawPointer
                                    || variable.IsOut || variable.IsInit)
                                : ((!Scalar(variable.Type)
                                        && variable.Type != PrimitiveType.NativeRawPointer
                                        && !isNativePointerValue
                                        && !isNativeFunctionPointerValue)
                                    || variable.IsOut || variable.IsInit))))
                {
                    throw Unsupported($"Function '{function.Name}' has a non-value scalar parameter.");
                }

                parameters.Add(new TrRmParamRow(index + 1, parameter, parameterType,
                    variable.IsInit && function.DirectNativeImport is null ? (byte)2 : (byte)0));
            }

            var resultTypeRow = function.DirectNativeImport is not null
                && function.ReturnType == PrimitiveType.NativeRawPointer
                    ? NativeParameterTypeRow(typeRowsList, nativeParameterTypeRows, PrimitiveType.Void)
                : NativeValueTypeRow(function.ReturnType, nativeRawPointerTypeRow,
                    nativeFunctionPointerTypeRows, typeRowsList, nativePointerTypeRows,
                    program.NativePointerTypes, structureTypeDefRows, typeDefTypeSpecRows);
            signatures.Add(new TrRmMethodSigRow(resultTypeRow, 0,
                first, function.ParameterCount, 0, 0));
            var bodyRow = 0;
            if (!function.IsExternal && !hasNativeVTable)
            {
                bodyRow = bodies.Count + 1;
                bodies.Add(outputFeatures[index]
                    ? EncodeOutputBody(function, program, typeRowsList,
                        nativeParameterTypeRows, nativeOutputTypeRows, nativePointerTypeRows,
                        nativeRawPointerTypeRow)
                    : functionFeatures[index].HasIndexedFlow
                        ? new IndexedBuilder(function, program,
                            functionFeatures[index].HasFloatingTypes, nativeRawPointerTypeRow,
                            nativeFunctionPointerTypeRows, nativePointerTypeRows).Encode()
                        : EncodeBody(function, functionFeatures[index].HasFloatingTypes,
                            nativeRawPointerTypeRow, nativeFunctionPointerTypeRows,
                            nativePointerTypeRows));
            }

            var effectMask = (function.IsNoThrow ? 1u : 0u)
                | (function.DoesNotReturn ? 2u : 0u)
                | (function.RequiresUnsafeCaller ? 4u : 0u);
            var contractFirst = effectMask != 0 || function.DirectNativeImport is not null
                || function.NativeFunctionPointerType is not null || hasNativeVTable
                    ? contracts.Count + 1 : 0;
            if (effectMask != 0)
            {
                contracts.Add(new TrRmContractRow(0x1600_0000u | (uint)(index + 1), 1,
                    s_effectPayloads[effectMask]));
            }
            if (function.DirectNativeImport is DirectNativeImport nativeImport)
            {
                contracts.Add(new TrRmContractRow(0x1600_0000u | (uint)(index + 1), 0x10,
                    NativeAbiPayload()));
                foreach (var output in nativeImport.Outputs)
                {
                    contracts.Add(new TrRmContractRow(0x1600_0000u | (uint)(index + 1), 0x12,
                        NativeOutputPayload(output.ParameterIndex, output.ResultCondition,
                            output.FailureDisposition)));
                }
                foreach (var parameter in nativeImport.Parameters)
                {
                    contracts.Add(new TrRmContractRow(0x1600_0000u | (uint)(index + 1), 0x11,
                        NativeParameterPayload(parameter.ParameterIndex,
                            (byte)((byte)parameter.Access + 1), (byte)parameter.Retention,
                            (uint)parameter.Extent)));
                }
            }
            else if (function.NativeFunctionPointerType is not null)
            {
                contracts.Add(new TrRmContractRow(0x1600_0000u | (uint)(index + 1), 0x10,
                    NativeAbiPayload(2)));
            }
            else if (hasNativeVTable)
            {
                contracts.Add(new TrRmContractRow(0x1600_0000u | (uint)(index + 1), 0x10,
                    NativeAbiPayload(4)));
                var method = function.NativeVTableMethod!;
                foreach (var output in method.Outputs)
                {
                    contracts.Add(new TrRmContractRow(0x1600_0000u | (uint)(index + 1), 0x12,
                        NativeOutputPayload(output.ParameterIndex, output.ResultCondition,
                            output.FailureDisposition)));
                }
                foreach (var parameter in method.Parameters)
                {
                    contracts.Add(new TrRmContractRow(0x1600_0000u | (uint)(index + 1), 0x11,
                        NativeParameterPayload(parameter.ParameterIndex,
                            (byte)((byte)parameter.Access + 1), (byte)parameter.Retention,
                            (uint)parameter.Extent)));
                }
            }

            var visibility = function.IsPrivate ? 0u : function.IsInternal ? 1u : 2u;
            methods.Add(new TrRmMethodDefRow(0, nameRows[index], index + 1, visibility,
                contractFirst, contractFirst == 0 ? 0 : contracts.Count - contractFirst + 1, bodyRow));
            if (visibility == 2 && function.ModuleImport is null)
            {
                exports.Add(new TrRmExportRow(0x1600_0000u | (uint)(index + 1), nameRows[index]));
            }
        }

        var nativeFunctionPointerSignatureRows =
            new List<TrRmNativeFunctionPointerSignatureRow>(
                program.NativeFunctionPointerSignatures.Length);
        for (var index = 0; index < program.NativeFunctionPointerSignatures.Length; index++)
        {
            var signature = program.NativeFunctionPointerSignatures[index];
            var methodRow = methods.Count + 1;
            var first = signature.ParameterTypes.IsEmpty ? 0 : parameters.Count + 1;
            foreach (var parameterType in signature.ParameterTypes)
            {
                parameters.Add(new TrRmParamRow(methodRow, parameters.Count - first + 1,
                    NativeValueTypeRow(parameterType, nativeRawPointerTypeRow,
                        nativeFunctionPointerTypeRows, typeRowsList, nativePointerTypeRows,
                        program.NativePointerTypes, structureTypeDefRows, typeDefTypeSpecRows), 0));
            }

            signatures.Add(new TrRmMethodSigRow(
                NativeValueTypeRow(signature.ReturnType, nativeRawPointerTypeRow,
                    nativeFunctionPointerTypeRows, typeRowsList, nativePointerTypeRows,
                    program.NativePointerTypes, structureTypeDefRows, typeDefTypeSpecRows),
                0, first, signature.ParameterTypes.Length, 0, 0));
            var contractFirst = contracts.Count + 1;
            contracts.Add(new TrRmContractRow(0x1600_0000u | (uint)methodRow, 1,
                s_effectPayloads[5]));
            contracts.Add(new TrRmContractRow(0x1600_0000u | (uint)methodRow, 0x10,
                NativeAbiPayload(3)));
            var nameRow = names.Count + 1;
            names.Add(($"__trrm_fnptr_signature_{index}", 1));
            methods.Add(new TrRmMethodDefRow(0, nameRow, signatures.Count, 0,
                contractFirst, 2, 0));
            nativeFunctionPointerSignatureRows.Add(
                new TrRmNativeFunctionPointerSignatureRow(methodRow));
        }

        var stringsBytes = names.Any(static name => name.Text.Length == 0)
            ? TrRmStringTableWriter.EncodeTextRows(names)
            : TrRmStringTableWriter.EncodeNames(names);
        var strings = new TrRmStringTableReader(stringsBytes, names.Count);
        var hasOutputFeatures = outputFeatures.Any(static enabled => enabled);
        var featureRows = new List<TrRmFeatureRow>(8);
        if (hasOutputFeatures)
        {
            featureRows.Add(TrRmFeatureRow.ScalarInitOutput);
        }
        if (nativeImportCount != 0 || nativeCallableCount != 0 || nativeVTableCount != 0
            || nativeFunctionPointerSignatureRows.Count != 0
            || nativeRawPointerTypeRow != 0
            || !program.NativePointerTypes.IsEmpty
            || !blittableStructureIndexes.IsEmpty)
        {
            featureRows.Add(TrRmFeatureRow.NativeBoundaryContracts);
        }
        var hasNativeFunctionPointerBodyOperations = program.Functions
            .Any(HasNativeFunctionPointerBodyOperation);
        var hasRootlessNativeValues = nativeImportCount != 0 || nativeCallableCount != 0
            || nativeVTableCount != 0
            || nativeFunctionPointerSignatureRows.Count != 0
            || nativeRawPointerTypeRow != 0
            || !program.NativePointerTypes.IsEmpty
            || hasNativeFunctionPointerBodyOperations
            || Enumerable.Range(0, program.Functions.Length).Any(index =>
                outputFeatures[index] && !program.Functions[index].IsExternal
                    && HasRootlessNativeValues(program.Functions[index]));
        if (hasRootlessNativeValues)
        {
            featureRows.Add(TrRmFeatureRow.RootlessNativeValues);
        }
        if (nativeImportCount != 0 || nativeCallableContractCount != 0
            || hasNativeFunctionPointerBodyOperations)
        {
            featureRows.Add(TrRmFeatureRow.DirectNativeCalls);
        }
        if (nativeCallableContractCount != 0 || hasNativeFunctionPointerBodyOperations)
        {
            featureRows.Add(TrRmFeatureRow.NativeCallableEntries);
        }
        if (nativeVTableCount != 0)
        {
            featureRows.Add(TrRmFeatureRow.NativeVTableCalls);
        }
        var featureCount = featureRows.Count;
        var featureBytes = featureCount == 0
            ? [] : TrRmFeatureTableWriter.Encode(featureRows);
        var typeDefCount = blittableStructureIndexes.Length;
        var moduleRefRows = new List<TrRmModuleRefRow>(moduleImportIdentities.Length);
        foreach (var identity in moduleImportIdentities)
        {
            var moduleImport = program.Functions
                .Select(static function => function.ModuleImport)
                .First(import => import?.ProducerIdentity == identity);
            moduleRefRows.Add(new TrRmModuleRefRow(moduleReferenceStringRows[identity],
                moduleImport!.TargetRequirement, moduleImport.RuntimePolicy));
        }
        var moduleReferenceRowByIdentity = new Dictionary<string, int>(
            moduleImportIdentities.Length, StringComparer.Ordinal);
        for (var index = 0; index < moduleImportIdentities.Length; index++)
        {
            moduleReferenceRowByIdentity.Add(moduleImportIdentities[index], index + 1);
        }
        var moduleBytes = TrRmModuleTableWriter.Encode(
            [new TrRmModuleRow(1, 1, 0, program.EntryPoint + 1, 1, 1, 1, 1,
                featureCount == 0 ? 0 : 1, featureCount)],
            strings, moduleRefRows.Count, typeDefCount, methods.Count, 0, featureCount);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings, moduleRefRows.Count, typeDefCount,
            methods.Count, 0, featureCount);
        var moduleRefBytes = moduleRefRows.Count == 0 ? []
            : TrRmModuleRefTableWriter.Encode(moduleRefRows, strings, modules);
        var moduleRefs = moduleRefRows.Count == 0 ? default
            : new TrRmModuleRefTableReader(moduleRefBytes, moduleRefRows.Count, strings, modules);
        var typeRows = typeRowsList.ToArray();
        var typeBytes = TrRmTypeSpecTableWriter.Encode(typeRows, typeDefCount, 0, 0, 0,
            nativeFunctionPointerSignatureRows.Count);
        var types = new TrRmTypeSpecTableReader(typeBytes, typeRows.Length,
            typeDefCount, 0, 0, 0, nativeFunctionPointerSignatureRows.Count);
        var sigBytes = TrRmMethodSigTableWriter.Encode(signatures, types, parameters.Count, 0);
        var sigs = new TrRmMethodSigTableReader(sigBytes, signatures.Count, types, parameters.Count, 0);
        var paramBytes = parameters.Count == 0 ? [] : TrRmParamTableWriter.Encode(parameters, sigs, types);
        var typeDefRows = new TrRmTypeDefRow[typeDefCount];
        for (var type = 0; type < blittableStructureIndexes.Length; type++)
        {
            var structure = program.Structures[blittableStructureIndexes[type]];
            var visibility = structure.IsPrivate ? 0u : structure.IsInternal ? 1u : 2u;
            var layoutKind = structure.IsSequential ? 1u : 3u;
            typeDefRows[type] = new TrRmTypeDefRow(structureNamespaceRows[type],
                structureNameRows[type], visibility | 4u | (layoutKind << 4),
                structureFieldFirst[type], structure.Fields.Length, 0, 0, type + 1,
                structureContractFirst[type], 1);
        }
        var typeDefBytes = typeDefRows.Length == 0 ? []
            : TrRmTypeDefTableWriter.Encode(typeDefRows, strings, modules, fieldRows.Count,
                0, layoutRows.Count, contracts.Count);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, typeDefRows.Length,
            strings, modules, fieldRows.Count, 0, layoutRows.Count, contracts.Count);
        var layoutBytes = layoutRows.Count == 0 ? []
            : TrRmLayoutTableWriter.Encode(layoutRows, typeDefs);
        var layouts = layoutRows.Count == 0 ? default
            : new TrRmLayoutTableReader(layoutBytes, layoutRows.Count, typeDefs);
        var fieldBytes = fieldRows.Count == 0 ? []
            : TrRmFieldDefTableWriter.Encode(fieldRows, strings, typeDefs, types, layouts);
        var methodBytes = TrRmMethodDefTableWriter.Encode(methods, strings, modules, typeDefs,
            sigs, contracts.Count, bodies.Count);
        var defs = new TrRmMethodDefTableReader(methodBytes, methods.Count, strings, modules,
            typeDefs, sigs, contracts.Count, bodies.Count);
        var paramReader = parameters.Count == 0 ? default
            : new TrRmParamTableReader(paramBytes, parameters.Count, sigs, types);
        var contractBytes = contracts.Count == 0 ? []
            : TrRmContractTableWriter.Encode(contracts, typeDefs, defs, sigs, paramReader, types);
        var contractReader = new TrRmContractTableReader(contractBytes, contracts.Count,
            typeDefs, defs, sigs, paramReader, types);
        var moduleImportRows = new List<TrRmImportRow>(moduleImportIdentities.Length);
        for (var index = 0; index < program.Functions.Length; index++)
        {
            if (program.Functions[index].ModuleImport is BoundModuleImport moduleImport)
            {
                moduleImportRows.Add(new TrRmImportRow(
                    moduleReferenceRowByIdentity[moduleImport.ProducerIdentity],
                    nameRows[index], 0x1600_0000u | (uint)(index + 1)));
            }
        }
        moduleImportRows.Sort(static (left, right) =>
        {
            var moduleOrder = left.ModuleRefRow.CompareTo(right.ModuleRefRow);
            return moduleOrder != 0 ? moduleOrder
                : left.ExpectedLocalDefId.CompareTo(right.ExpectedLocalDefId);
        });
        var importBytes = moduleImportRows.Count == 0 ? []
            : TrRmImportTableWriter.Encode(moduleImportRows, strings, moduleRefs,
                typeDefs, defs, default);
        var imports = moduleImportRows.Count == 0 ? default
            : new TrRmImportTableReader(importBytes, moduleImportRows.Count,
                strings, moduleRefs, typeDefs, defs, default);
        var exportBytes = exports.Count == 0 ? []
            : TrRmExportTableWriter.Encode(exports, strings, typeDefs, defs, default, imports);
        var nativeImportRows = new List<TrRmNativeImportRow>(nativeImportCount);
        for (var index = 0; index < program.Functions.Length; index++)
        {
            if (program.Functions[index].DirectNativeImport is not null)
            {
                nativeImportRows.Add(new TrRmNativeImportRow(index + 1,
                    nativeLibraryRows[index], nativeExportRows[index], 1));
            }
        }
        var nativeImportBytes = nativeImportRows.Count == 0 ? []
            : TrRmNativeImportTableWriter.Encode(nativeImportRows, strings, defs, sigs,
                paramReader, types, contractReader);
        var nativeFunctionPointerSignatureBytes = nativeFunctionPointerSignatureRows.Count == 0 ? []
            : TrRmNativeFunctionPointerSignatureTableWriter.Encode(
                nativeFunctionPointerSignatureRows, defs, sigs, paramReader, types,
                contractReader);
        var nativeFunctionPointerSignatureReader =
            new TrRmNativeFunctionPointerSignatureTableReader(
                nativeFunctionPointerSignatureBytes, nativeFunctionPointerSignatureRows.Count,
                defs, sigs, paramReader, types, contractReader);
        var nativeCallableRows = new List<TrRmNativeCallableRow>(nativeCallableCount);
        for (var index = 0; index < program.Functions.Length; index++)
        {
            if (program.Functions[index].NativeFunctionPointerType is not null
                && !program.Functions[index].IsExternal)
            {
                nativeCallableRows.Add(new TrRmNativeCallableRow(index + 1,
                    nativeCallableEntryNameRows[index]));
            }
        }
        var nativeCallableBytes = nativeCallableRows.Count == 0 ? []
            : TrRmNativeCallableTableWriter.Encode(nativeCallableRows, strings, defs, sigs,
                paramReader, types, contractReader, nativeFunctionPointerSignatureReader, imports);
        var nativeVTableRows = new List<TrRmNativeVTableRow>(nativeVTableCount);
        for (var index = 0; index < program.Functions.Length; index++)
        {
            if (program.Functions[index].NativeVTableMethod is NativeVTableMethodDefinition method)
            {
                nativeVTableRows.Add(new TrRmNativeVTableRow(index + 1, 0,
                    method.VTableByteOffset, method.Slot));
            }
        }
        var nativeVTableBytes = nativeVTableRows.Count == 0 ? []
            : TrRmNativeVTableTableWriter.Encode(nativeVTableRows, defs, sigs, paramReader,
                types, contractReader);
        var sectionCount = 5 + (featureCount != 0 ? 1 : 0)
            + (moduleRefRows.Count != 0 ? 1 : 0)
            + (typeDefRows.Length != 0 ? 1 : 0)
            + (fieldRows.Count != 0 ? 1 : 0) + (layoutRows.Count != 0 ? 1 : 0)
            + (parameters.Count != 0 ? 1 : 0)
            + (contracts.Count != 0 ? 1 : 0) + (exports.Count != 0 ? 1 : 0)
            + (moduleImportRows.Count != 0 ? 1 : 0)
            + (bodies.Count != 0 ? 1 : 0) + (nativeImportRows.Count != 0 ? 1 : 0)
            + (nativeCallableRows.Count != 0 ? 1 : 0) + (nativeVTableRows.Count != 0 ? 1 : 0);
        sectionCount += nativeFunctionPointerSignatureRows.Count != 0 ? 1 : 0;
        var sections = new List<TrRmSection>(sectionCount)
        {
            new(1, 0, 1, moduleBytes),
            new(2, 0, (uint)names.Count, stringsBytes),
        };
        if (featureCount != 0)
        {
            sections.Add(new(4, 0, (uint)featureCount, featureBytes));
        }
        if (moduleRefRows.Count != 0)
        {
            sections.Add(new(0x10, 0, (uint)moduleRefRows.Count, moduleRefBytes));
        }
        sections.Add(new(0x11, 0, (uint)typeRows.Length, typeBytes));
        if (typeDefRows.Length != 0)
        {
            sections.Add(new(0x13, 0, (uint)typeDefRows.Length, typeDefBytes));
        }
        if (fieldRows.Count != 0)
        {
            sections.Add(new(0x14, 0, (uint)fieldRows.Count, fieldBytes));
        }
        if (layoutRows.Count != 0)
        {
            sections.Add(new(0x15, 0, (uint)layoutRows.Count, layoutBytes));
        }
        sections.Add(new(0x16, 0, (uint)methods.Count, methodBytes));
        sections.Add(new(0x17, 0, (uint)signatures.Count, sigBytes));
        if (parameters.Count != 0)
        {
            sections.Add(new(0x18, 0, (uint)parameters.Count, paramBytes));
        }
        if (contracts.Count != 0)
        {
            sections.Add(new(0x1B, 0, (uint)contracts.Count, contractBytes));
        }
        if (moduleImportRows.Count != 0)
        {
            sections.Add(new(0x1C, 0, (uint)moduleImportRows.Count, importBytes));
        }
        if (exports.Count != 0)
        {
            sections.Add(new(0x1D, 0, (uint)exports.Count, exportBytes));
        }
        if (bodies.Count != 0)
        {
            sections.Add(new(0x20, 0, (uint)bodies.Count, TrRmVariableTableWriter.EncodeBodies(bodies)));
        }
        if (nativeImportRows.Count != 0)
        {
            sections.Add(new(0x30, 0, (uint)nativeImportRows.Count, nativeImportBytes));
        }
        if (nativeCallableRows.Count != 0)
        {
            sections.Add(new(0x31, 0, (uint)nativeCallableRows.Count, nativeCallableBytes));
        }
        if (nativeVTableRows.Count != 0)
        {
            sections.Add(new(0x32, 0, (uint)nativeVTableRows.Count, nativeVTableBytes));
        }
        if (nativeFunctionPointerSignatureRows.Count != 0)
        {
            sections.Add(new(0x33, 0, (uint)nativeFunctionPointerSignatureRows.Count,
                nativeFunctionPointerSignatureBytes));
        }
        var artifact = TrRmArtifactWriter.Encode(sections);
        _ = Load(artifact);
        return artifact;
    }

    private static ReadOnlyMemory<byte>[] CreateTypeRows(byte[] codes)
    {
        var rows = new ReadOnlyMemory<byte>[codes.Length];
        for (var index = 0; index < codes.Length; index++)
        {
            rows[index] = TrRmTypeSpecTableWriter.Builtin(codes[index]);
        }
        return rows;
    }

    public static BoundProgram Load(ReadOnlySpan<byte> artifact)
    {
        var index = new TrRmArtifactIndex(artifact);
        var validatedTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(index);
        var modules = validatedTables.Modules;
        var typeDefs = validatedTables.TypeDefs;
        var strings = validatedTables.Strings;
        var types = validatedTables.TypeSpecs;
        var fields = validatedTables.Fields;
        var layouts = validatedTables.Layouts;
        var sigs = validatedTables.Signatures;
        var defs = validatedTables.Methods;
        var parameters = validatedTables.Parameters;
        var bodies = validatedTables.Bodies;
        var contractTable = validatedTables.Contracts;
        var nativeImports = validatedTables.NativeImports;
        var nativeCallables = validatedTables.NativeCallables;
        var nativeFunctionPointerSignatureTable = validatedTables.NativeFunctionPointerSignatures;
        var nativeVTables = validatedTables.NativeVTables;
        var imports = validatedTables.Imports;
        var typeCount = types.Count;
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out _, out _, out _);
            if (kind is not (1 or 2 or 4 or 0x10 or 0x11 or 0x13 or 0x14 or 0x15
                or 0x16 or 0x17 or 0x18
                or 0x1B or 0x1C or 0x1D or 0x20
                or 0x30 or 0x31 or 0x32 or 0x33))
            {
                throw Invalid($"Section 0x{kind:X4} is not executable.");
            }
        }

        _ = index.TryGetSection(1, out var moduleBytes, out var moduleCount);
        _ = index.TryGetSection(0x16, out _, out var methodCount);
        _ = index.TryGetSection(0x17, out _, out var sigCount);
        _ = index.TryGetSection(0x18, out _, out _);
        _ = index.TryGetSection(4, out var featureBytes, out var featureCount);
        _ = index.TryGetSection(0x1B, out var contractBytes, out _);
        _ = index.TryGetSection(0x1C, out _, out var importCount);
        _ = index.TryGetSection(0x1D, out var exportBytes, out var exportCount);
        _ = index.TryGetSection(0x20, out _, out var bodyCount);
        if (moduleCount != 1 || methodCount == 0 || bodyCount > methodCount
            || sigCount == 0 || typeCount == 0)
        {
            throw Invalid("Expected one self-contained scalar module with one Body per method.");
        }

        var featureTable = new TrRmFeatureTableReader(featureBytes, featureCount);
        var hasOptionalScalarTypes = false;
        for (var row = 1; row <= typeCount; row++)
        {
            hasOptionalScalarTypes |= types.GetRow(row)[0] == 0x06
                && TrRmOptionalScalar.IsOptionalType(BodyTypeFromRow(types, row));
        }
        if (modules.GetRequirements(1) != (1u, 0u))
        {
            throw Invalid("Only fail-fast Windows x64 target requirement 1 is executable.");
        }
        var (firstFeature, requiredFeatureCount) = modules.GetFeatureRange(1);
        uint scalarInitOutputRuleRevision = 0;
        uint rootlessNativeValuesRuleRevision = 0;
        for (var feature = firstFeature; feature < firstFeature + requiredFeatureCount; feature++)
        {
            var required = featureTable.GetFeature(feature);
            if (required.FeatureId == TrRmFeatureTableReader.ScalarInitOutputFeatureId)
            {
                scalarInitOutputRuleRevision = required.MinimumRuleRevision;
            }
            else if (required.FeatureId == TrRmFeatureTableReader.RootlessNativeValuesFeatureId)
            {
                rootlessNativeValuesRuleRevision = required.MinimumRuleRevision;
            }
        }
        if (nativeFunctionPointerSignatureTable.Count != 0
            && rootlessNativeValuesRuleRevision == 0)
        {
            throw Invalid("Native function-pointer signatures require the RootlessNativeValues feature.");
        }

        var optionalStructures = hasOptionalScalarTypes ? TrRmOptionalScalar.Structures : [];
        var nativePointerTypeDefinitions = new List<NativePointerTypeDefinition>();
        var structures = LoadBlittableStructures(typeDefs, fields, layouts, contractTable,
            types, optionalStructures, nativePointerTypeDefinitions,
            out var structureTypeByTypeDef);

        var declaredExports = new HashSet<int>(exportCount);
        for (var row = 0; row < exportCount; row++)
        {
            var id = BinaryPrimitives.ReadUInt32LittleEndian(exportBytes.Slice(row * 8));
            var name = BinaryPrimitives.ReadUInt32LittleEndian(exportBytes.Slice((row * 8) + 4));
            var method = id & 0xFF_FFFF;
            if (id >> 24 != 0x16 || method == 0 || method > (uint)methodCount
                || !declaredExports.Add((int)method)
                || strings.DecodeName((int)name) != defs.GetName((int)method))
            {
                throw Invalid("Unsupported export identity.");
            }
        }

        var functions = ImmutableArray.CreateBuilder<BoundFunction>(methodCount);
        var seen = new HashSet<string>(methodCount, StringComparer.Ordinal);
        var functionIndexByMethodRow = new int[methodCount + 1];
        Array.Fill(functionIndexByMethodRow, -1);
        var nativeFunctionPointerSignatureDefinitions =
            ImmutableArray.CreateBuilder<NativeFunctionPointerSignatureDefinition>(
                nativeFunctionPointerSignatureTable.Count);
        for (var signatureRow = 1;
            signatureRow <= nativeFunctionPointerSignatureTable.Count; signatureRow++)
        {
            var methodRow = nativeFunctionPointerSignatureTable.GetMethodDefRow(signatureRow);
            var signature = defs.GetSignature(methodRow);
            var resultType = FromNativeValueRow(types, sigs.GetResultTypeSpec(signature),
                nativePointerTypeDefinitions, structureTypeByTypeDef);
            var (first, count) = sigs.GetParamRange(signature);
            var parameterTypes = ImmutableArray.CreateBuilder<PrimitiveType>(count);
            for (var parameter = 0; parameter < count; parameter++)
            {
                parameterTypes.Add(FromNativeValueRow(types,
                    parameters.GetTypeSpec(first + parameter),
                    nativePointerTypeDefinitions, structureTypeByTypeDef));
            }

            nativeFunctionPointerSignatureDefinitions.Add(
                new NativeFunctionPointerSignatureDefinition(
                    NativeCallingConvention.Native, resultType, parameterTypes.MoveToImmutable()));
        }
        var nativeFunctionPointerSignatureArray = nativeFunctionPointerSignatureDefinitions.ToImmutable();
        if (!NativePointerTypeContract.IsValidTable(
            [.. nativePointerTypeDefinitions], structures,
            nativeFunctionPointerSignatureArray))
        {
            throw Invalid("Invalid native pointer or function-pointer type table.");
        }

        var nativeCallableTypeByMethod = new PrimitiveType?[methodCount + 1];
        for (var row = 1; row <= methodCount; row++)
        {
            if (nativeCallables.HasCallableForMethod(row))
            {
                nativeCallableTypeByMethod[row] = NativeFunctionPointerTypes.Type(
                    nativeCallables.GetFunctionPointerSignatureIndex(row));
            }
        }
        var usedSignatures = new bool[sigCount + 1];
        var usedSignatureCount = 0;
        var importedNativeCallableCount = 0;
        var importedNativeVTableCount = 0;
        for (var signatureRow = 1;
            signatureRow <= nativeFunctionPointerSignatureTable.Count; signatureRow++)
        {
            var methodRow = nativeFunctionPointerSignatureTable.GetMethodDefRow(signatureRow);
            var signature = defs.GetSignature(methodRow);
            if (!usedSignatures[signature])
            {
                usedSignatures[signature] = true;
                usedSignatureCount++;
            }
        }
        for (var row = 1; row <= methodCount; row++)
        {
            if (nativeFunctionPointerSignatureTable.HasSignatureForMethod(row))
            {
                continue;
            }

            var flags = defs.GetFlags(row);
            var name = defs.GetName(row);
            var signature = defs.GetSignature(row);
            var isNativeImport = nativeImports.HasImportForMethod(row);
            var isNativeCallable = nativeCallables.HasCallableForMethod(row);
            var isNativeVTable = nativeVTables.HasVTableForMethod(row);
            var importDefinition = imports.IsExpectedDefinition(0x1600_0000u | (uint)row);
            var isImportedNativeCallable = importDefinition && isNativeCallable;
            var isImportedNativeVTable = importDefinition && isNativeVTable;
            importedNativeCallableCount += isImportedNativeCallable ? 1 : 0;
            importedNativeVTableCount += isImportedNativeVTable ? 1 : 0;
            if (defs.GetOwnerType(row) != 0
                || (importDefinition && !isImportedNativeCallable && !isImportedNativeVTable)
                || (isNativeImport || isNativeVTable || isImportedNativeCallable)
                    != (defs.GetBodyRow(row) == 0)
                || sigs.GetReceiverTypeSpec(signature) != 0
                || sigs.GetGenericCount(signature) != 0
                || !seen.Add(name))
            {
                throw Invalid($"Unsupported declaration for method {row}.");
            }
            if ((flags == 2 && !importDefinition) != declaredExports.Contains(row))
            {
                throw Invalid("Every public method requires its matching named export.");
            }
            if (!usedSignatures[signature])
            {
                usedSignatures[signature] = true;
                usedSignatureCount++;
            }

            var (first, count) = sigs.GetParamRange(signature);
            var variables = ImmutableArray.CreateBuilder<BoundVariable>(count);
            var nativeOutputs = isNativeImport || isNativeVTable
                ? contractTable.GetNativeOutputs(row) : [];
            var nativeParameters = isNativeImport || isNativeVTable
                ? contractTable.GetNativeParameters(row) : [];
            for (var param = 0; param < count; param++)
            {
                var nativeOutput = FindNativeOutput(nativeOutputs, param);
                var nativeParameter = FindNativeParameter(nativeParameters, param);
                var typeSpecRow = parameters.GetTypeSpec(first + param);
                var type = nativeOutput is not null
                    ? PrimitiveType.NativeStorageAddress
                    : nativeParameter is not null
                        ? PrimitiveType.NativeRawPointer
                        : FromNativeValueRow(types, typeSpecRow,
                            nativePointerTypeDefinitions, structureTypeByTypeDef);
                var mode = parameters.GetMode(first + param);
                if (type == PrimitiveType.Void
                    || (mode != 0 && mode != 2)
                    || (isNativeImport && (mode != 0
                        || (nativeOutput is null && nativeParameter is null
                            && !NativeVTableCallContract.IsNativeValueType(type,
                                nativeFunctionPointerSignatureArray,
                                [.. nativePointerTypeDefinitions]))
                        || (nativeOutput is not null && type != PrimitiveType.NativeStorageAddress)
                        || (nativeParameter is not null && type != PrimitiveType.NativeRawPointer)))
                    || (isNativeVTable && (mode != 0
                        || (nativeOutput is not null
                            ? type != PrimitiveType.NativeStorageAddress
                                || nativeParameter is not null
                            : nativeParameter is null
                            && !NativeVTableCallContract.IsNativeValueType(type,
                                nativeFunctionPointerSignatureArray,
                                [.. nativePointerTypeDefinitions]))
                        || (nativeParameter is not null && type != PrimitiveType.NativeRawPointer)))
                    || (!isNativeImport && mode == 2 && (scalarInitOutputRuleRevision == 0
                        || (scalarInitOutputRuleRevision == TrRmFeatureTableReader.ScalarInitOutputInitialRuleRevision
                            && type != PrimitiveType.Int32)
                        || flags == 2)))
                {
                    throw Invalid("Unsupported parameter type or mode.");
                }
                variables.Add(new BoundVariable($"arg{param}", type,
                    mode == 2 || nativeOutput is not null, default)
                {
                    IsOut = mode == 2 || nativeOutput is not null,
                    IsInit = mode == 2,
                });
            }

            var (contractFirst, contracts) = defs.GetContractRange(row);
            var noThrow = false;
            var doesNotReturn = false;
            var requiresUnsafeCaller = false;
            if (contracts != 0)
            {
                for (var contractIndex = 0; contractIndex < contracts; contractIndex++)
                {
                    var clause = contractTable.GetRow(contractFirst + contractIndex);
                    var kind = BinaryPrimitives.ReadUInt16LittleEndian(clause[4..]);
                    if ((kind is 0x10 or 0x11 or 0x12)
                        && (isNativeImport || isNativeCallable || isNativeVTable
                            || nativeFunctionPointerSignatureTable.HasSignatureForMethod(row)))
                    {
                        continue;
                    }

                    var effectMask = clause.Length == 12
                        ? BinaryPrimitives.ReadUInt32LittleEndian(clause[8..]) : 0;
                    if (clause.Length != 12 || kind != 1 || effectMask is < 1 or > 7)
                    {
                        throw Invalid("Unsupported method contract.");
                    }
                    noThrow = (effectMask & 1) != 0;
                    doesNotReturn = (effectMask & 2) != 0;
                    requiresUnsafeCaller = (effectMask & 4) != 0;
                }
            }

            DirectNativeImport? directNativeImport = null;
            NativeVTableMethodDefinition? nativeVTableMethod = null;
            BoundModuleImport? moduleImport = null;
            if (isNativeImport)
            {
                var directOutputs = ImmutableArray.CreateBuilder<DirectNativeOutput>(nativeOutputs.Length);
                foreach (var output in nativeOutputs)
                {
                    directOutputs.Add(new DirectNativeOutput(output.ParameterOrdinal,
                        output.PointeeType,
                        output.ResultCondition, output.FailureDisposition));
                }
                var directParameters = ImmutableArray.CreateBuilder<DirectNativeParameter>(
                    nativeParameters.IsDefault ? 0 : nativeParameters.Length);
                if (!nativeParameters.IsDefaultOrEmpty)
                {
                    foreach (var parameter in nativeParameters)
                    {
                        var typeSpecRow = parameters.GetTypeSpec(first + parameter.ParameterOrdinal);
                        var pointeeType = NativeParameterPointee(types, typeSpecRow,
                            contractTable, structureTypeByTypeDef);
                        if (parameter.Access is < 1 or > 3
                            || parameter.Retention != 1 || parameter.ExtentKind is not (0 or 1)
                            || parameter.PointerDepth != 1 || parameter.RequiredAlignment != 0
                            || parameter.Extent > int.MaxValue)
                        {
                            throw Invalid("Invalid direct native parameter contract.");
                        }

                        directParameters.Add(new DirectNativeParameter(parameter.ParameterOrdinal,
                            pointeeType, (NativeParameterAccess)(parameter.Access - 1),
                            NativeParameterRetention.NoEscape, (int)parameter.Extent));
                    }
                }
                directNativeImport = new DirectNativeImport(nativeImports.GetLibraryName(row),
                    nativeImports.GetExportName(row), directOutputs.MoveToImmutable())
                {
                    Parameters = directParameters.MoveToImmutable(),
                };
            }
            else if (isNativeVTable)
            {
                var directOutputs = ImmutableArray.CreateBuilder<DirectNativeOutput>(
                    nativeOutputs.Length);
                foreach (var output in nativeOutputs)
                {
                    directOutputs.Add(new DirectNativeOutput(output.ParameterOrdinal,
                        output.PointeeType, output.ResultCondition, output.FailureDisposition));
                }
                var directParameters = ImmutableArray.CreateBuilder<DirectNativeParameter>(
                    nativeParameters.IsDefault ? 0 : nativeParameters.Length);
                if (!nativeParameters.IsDefaultOrEmpty)
                {
                    foreach (var parameter in nativeParameters)
                    {
                        var typeSpecRow = parameters.GetTypeSpec(first + parameter.ParameterOrdinal);
                        var pointeeType = NativeParameterPointee(types, typeSpecRow,
                            contractTable, structureTypeByTypeDef);
                        directParameters.Add(new DirectNativeParameter(parameter.ParameterOrdinal,
                            pointeeType, (NativeParameterAccess)(parameter.Access - 1),
                            NativeParameterRetention.NoEscape, checked((int)parameter.Extent)));
                    }
                }

                nativeVTableMethod = new NativeVTableMethodDefinition(
                    nativeVTables.GetSlot(row), directParameters.MoveToImmutable(),
                    nativeVTables.GetVTableByteOffset(row))
                {
                    Outputs = directOutputs.MoveToImmutable(),
                };
            }
            if (isImportedNativeCallable || isImportedNativeVTable)
            {
                if (!imports.TryGetExpectedBinding(0x1600_0000u | (uint)row,
                    out var importBinding))
                {
                    throw Invalid("Imported native declaration has no retained provider binding.");
                }

                moduleImport = new BoundModuleImport(importBinding.ProducerIdentity,
                    importBinding.ExportKey, importBinding.TargetRequirement,
                    importBinding.RuntimePolicy);
            }
            functionIndexByMethodRow[row] = functions.Count;
            var resultTypeRow = sigs.GetResultTypeSpec(signature);
            var returnType = isNativeImport
                ? TrRmNativeImportTypeContract.TryGetDirectNativeImportResultType(
                    types, resultTypeRow, out _)
                        ? FromNativeValueRow(types, resultTypeRow,
                            nativePointerTypeDefinitions, structureTypeByTypeDef)
                        : throw Invalid("Unsupported direct native import result type.")
                : FromNativeValueRow(types, resultTypeRow,
                    nativePointerTypeDefinitions, structureTypeByTypeDef);
            functions.Add(new BoundFunction(name, returnType,
                variables.ToImmutable(), count, new BoundBlock([], default), default)
            {
                IsNoThrow = noThrow,
                DoesNotReturn = doesNotReturn,
                RequiresUnsafeCaller = requiresUnsafeCaller,
                IsExternal = isNativeImport || isImportedNativeCallable,
                Intrinsic = isNativeVTable ? BoundIntrinsic.NativeVTableCall : BoundIntrinsic.None,
                DirectNativeImport = directNativeImport,
                ModuleImport = moduleImport,
                NativeVTableMethod = nativeVTableMethod,
                NativeFunctionPointerType = nativeCallableTypeByMethod[row],
                NativeCallableEntryName = isNativeCallable
                    && nativeCallables.HasCallableEntryForMethod(row)
                        ? nativeCallables.GetEntryName(row) : null,
                IsPrivate = flags == 0,
                IsInternal = flags == 1,
            });
        }
        if (importedNativeCallableCount + importedNativeVTableCount != imports.Count)
        {
            throw Invalid("Executable TRRM supports imports only for native callable and vtable declarations.");
        }
        if (usedSignatureCount != sigCount)
        {
            throw Invalid("Orphan MethodSig is not part of a defined function.");
        }

        var entry = (int)BinaryPrimitives.ReadUInt32LittleEndian(moduleBytes.Slice(12, 4)) - 1;
        if (entry >= 0 && (entry >= methodCount || functionIndexByMethodRow[entry + 1] < 0))
        {
            throw Invalid("Entry method must be a local int32 no-argument function.");
        }

        for (var row = 1; row <= methodCount; row++)
        {
            var functionIndex = functionIndexByMethodRow[row];
            if (functionIndex < 0)
            {
                continue;
            }
            var declaration = functions[functionIndex];
            if (declaration.IsExternal || declaration.NativeVTableMethod is not null)
            {
                continue;
            }
            var count = declaration.ParameterCount;
            var noThrow = declaration.IsNoThrow;
            var body = bodies.GetRow(defs.GetBodyRow(row));
            TrRmScalarGraph? indexed = null;
            ImmutableArray<BoundVariable>.Builder variables;
            ImmutableArray<BoundStatement> statements;
            var bodyForm = body[0] & 0x0C;
            if (bodyForm == 0x08)
            {
                TrRmCompactIndexedBodyReader.ValidateOnly(bodies, row, modules, defs, sigs,
                    parameters, types, featureTable, nativeCallables, nativeImports,
                    nativeFunctionPointerSignatureTable, nativeVTables);
                indexed = IndexedBuilder.DecodeIndexedBody(body, count,
                    declaration.ReturnType, noThrow, declaration.DoesNotReturn, types, functions,
                    functionIndexByMethodRow,
                    nativeFunctionPointerSignatureDefinitions.ToImmutable(),
                    nativePointerTypeDefinitions, structureTypeByTypeDef);
                variables = ImmutableArray.CreateBuilder<BoundVariable>(indexed.Types.Length);
                variables.AddRange(declaration.Variables);
                for (var value = count; value < indexed.Types.Length; value++)
                {
                    variables.Add(new BoundVariable($"value{value + 1}", indexed.Types[value], false, default));
                }
                statements = [];
            }
            else if (bodyForm == 0x04)
            {
                variables = declaration.Variables.ToBuilder();
                _ = new TrRmCompactLinearBodyReader(bodies, row, modules, defs, sigs,
                    parameters, types, typeDefs, imports, nativeImports);
                statements = DecodeBody(body, count, variables, noThrow, types, functions,
                    functionIndexByMethodRow,
                    nativeFunctionPointerSignatureDefinitions.ToImmutable(),
                    nativePointerTypeDefinitions: nativePointerTypeDefinitions,
                    structureTypeByTypeDef: structureTypeByTypeDef);
                if (statements.Any(static statement => statement is BoundLocal
                    { Initializer: TrRmScalarReinterpret or TrRmScalarConvert
                        or TrRmScalarWrappingBinary or TrRmScalarOptionalNone
                        or TrRmScalarOptionalLift or TrRmScalarOptionalPresence
                        or TrRmScalarOptionalEquality or BoundBinary
                        { Operator: TokenKind.ShiftLeft or TokenKind.ShiftRight } }
                    or BoundLocal { Initializer: BoundCall }
                    or BoundExpressionStatement { Expression: BoundCall }
                    or TrRmScalarDivRem))
                {
                    var operations = statements[..^1];
                    var returned = (BoundReturn)statements[^1];
                    var variableTypes = ImmutableArray.CreateBuilder<PrimitiveType>(variables.Count);
                    for (var variableIndex = 0; variableIndex < variables.Count; variableIndex++)
                    {
                        variableTypes.Add(variables[variableIndex].Type);
                    }
                    indexed = TrRmScalarGraph.FromAuditedBody(
                        [new TrRmScalarBlock([.. Enumerable.Range(1, count)],
                            operations, 0, null, null,
                            returned.Value is BoundRead read ? read.Variable + 1 : 0, 0)],
                        variableTypes.MoveToImmutable(),
                        count, declaration.ReturnType, noThrow, declaration.DoesNotReturn);
                    statements = [];
                }
            }
            else if (bodyForm == 0)
            {
                if (scalarInitOutputRuleRevision == 0)
                {
                    throw Invalid("A general place Body requires the ScalarInitOutput feature.");
                }

                var outputBody = TrRmScalarInitOutputBodyReader.Read(bodies, row, modules,
                    defs, sigs, parameters, types, contractBytes,
                    contractTable, nativeImports, nativeVTables, scalarInitOutputRuleRevision,
                    rootlessNativeValuesRuleRevision);
                if (outputBody.Places.IsEmpty)
                {
                    throw Invalid("The executable general Body profile requires places.");
                }

                variables = declaration.Variables.ToBuilder();
                statements = DecodeOutputBody(outputBody, types, variables, functions,
                    functionIndexByMethodRow,
                    structures, scalarInitOutputRuleRevision);
            }
            else
            {
                throw Invalid("Unsupported executable Body form.");
            }

            functions[functionIndex] = declaration with
            {
                Variables = variables.ToImmutable(),
                Body = new BoundBlock(statements, default),
                IndexedBody = indexed,
            };
        }
        if (entry >= 0)
        {
            var executableEntry = functions[functionIndexByMethodRow[entry + 1]];
            if (executableEntry.ReturnType != PrimitiveType.Int32
                || executableEntry.ParameterCount != 0 || executableEntry.IsExternal)
            {
                throw Invalid("Entry method must be a local int32 no-argument function.");
            }
        }
        var programFunctions = functions.ToImmutable();
        var entryFunctionIndex = entry < 0 ? -1 : functionIndexByMethodRow[entry + 1];
        var program = new BoundProgram(programFunctions, entryFunctionIndex)
        {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            Structures = structures,
            NativeFunctionPointerSignatures = nativeFunctionPointerSignatureArray,
            NativePointerTypes = [.. nativePointerTypeDefinitions],
        };
        try
        {
            if (entry == -1)
            {
                SemanticVerifier.VerifyLibraryReferences(program);
            }
            else
            {
                SemanticVerifier.VerifyReferences(program);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException($"TRRM executable semantic audit failed: {exception.Message}", exception);
        }
        return program;
    }

    /// <summary>Reads a validated TRRM library and exposes its public native declarations to source binding.</summary>
    public static CompilationReference CreateReference(byte[] providerArtifact)
    {
        ArgumentNullException.ThrowIfNull(providerArtifact);
        var program = Load(providerArtifact);
        var validatedTables = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
            new TrRmArtifactIndex(providerArtifact));
        var providerIdentity = validatedTables.Modules.GetIdentity(1);
        var (targetRequirement, runtimePolicy) = validatedTables.Modules.GetRequirements(1);
        if (program.EntryPoint != -1
            || !program.Classes.IsEmpty || !program.Buffers.IsEmpty
            || program.Functions.Any(static function =>
                !function.IsPrivate && !function.IsInternal
                    && (function.IsExternal
                        ? function.DirectNativeImport is null
                            && function.NativeFunctionPointerType is null
                            && function.NativeVTableMethod is null
                        : function.NativeFunctionPointerType is null
                            && function.NativeVTableMethod is null)))
        {
            throw Unsupported("A TRRM native reference requires public native declarations.");
        }

        var structureMap = new int[program.Structures.Length];
        Array.Fill(structureMap, -1);
        var includedStructureIndexes = new List<int>();
        var structures = ImmutableArray.CreateBuilder<BoundStruct>();
        for (var index = 0; index < program.Structures.Length; index++)
        {
            if (program.Structures[index].BlittableTargetRequirement == 0)
            {
                continue;
            }

            structureMap[index] = includedStructureIndexes.Count;
            includedStructureIndexes.Add(index);
        }
        foreach (var index in includedStructureIndexes)
        {
            var structure = program.Structures[index];
            var fields = ImmutableArray.CreateBuilder<BoundStructField>(structure.Fields.Length);
            foreach (var field in structure.Fields)
            {
                fields.Add(field with { Type = RemapStructureType(field.Type, structureMap) });
            }
            structures.Add(structure with { Fields = fields.MoveToImmutable() });
        }

        var nativePointerTypes = ImmutableArray.CreateBuilder<NativePointerTypeDefinition>(
            program.NativePointerTypes.Length);
        foreach (var pointerType in program.NativePointerTypes)
        {
            nativePointerTypes.Add(pointerType with
            {
                PointeeType = RemapStructureType(pointerType.PointeeType, structureMap),
            });
        }

        var functions = program.Functions
            .Where(static function => !function.IsPrivate && !function.IsInternal)
            .Select(function => RemapNativeImportStructureTypes(function, structureMap))
            .Select(function => function.NativeFunctionPointerType is not null
                || function.NativeVTableMethod is not null
                ? function with {
                    IsExternal = true,
                    Intrinsic = BoundIntrinsic.None,
                    Body = new BoundBlock([], default),
                    IndexedBody = null,
                    NativeCallableEntryName = null,
                    ModuleImport = function.ModuleImport ?? new BoundModuleImport(providerIdentity,
                        function.Name, targetRequirement, runtimePolicy),
                }
                : function)
            .Select(static function => function.NativeVTableMethod is not null
                ? function with {
                    IsExternal = false,
                    Intrinsic = BoundIntrinsic.NativeVTableCall,
                }
                : function)
            .ToImmutableArray();
        if (functions.IsEmpty)
        {
            throw Unsupported("A TRRM reference requires at least one public declaration.");
        }

        return new CompilationReference(structures.ToImmutable(), functions, program.RuntimePolicy,
            nativeFunctionPointerSignatures: program.NativeFunctionPointerSignatures,
            nativePointerTypes: nativePointerTypes.MoveToImmutable());
    }

    private static BoundFunction RemapNativeImportStructureTypes(BoundFunction function,
        int[] structureMap)
    {
        var variables = function.Variables.Select(variable => variable with
        {
            Type = RemapStructureType(variable.Type, structureMap),
        }).ToImmutableArray();
        var remappedImport = function.DirectNativeImport;
        if (remappedImport is DirectNativeImport import && !import.Parameters.IsDefaultOrEmpty)
        {
            remappedImport = import with
            {
                Parameters = [.. import.Parameters.Select(parameter => parameter with
                {
                    PointeeType = RemapStructureType(parameter.PointeeType, structureMap),
                })],
            };
        }

        var remappedVTableMethod = function.NativeVTableMethod;
        if (remappedVTableMethod is NativeVTableMethodDefinition method
            && (!method.Parameters.IsDefaultOrEmpty || !method.Outputs.IsDefaultOrEmpty))
        {
            remappedVTableMethod = method with
            {
                Parameters = [.. method.Parameters.Select(parameter => parameter with
                {
                    PointeeType = RemapStructureType(parameter.PointeeType, structureMap),
                })],
                Outputs = [.. method.Outputs.Select(output => output with
                {
                    PointeeType = RemapStructureType(output.PointeeType, structureMap),
                })],
            };
        }

        return function with
        {
            ReturnType = RemapStructureType(function.ReturnType, structureMap),
            Variables = variables,
            DirectNativeImport = remappedImport,
            NativeVTableMethod = remappedVTableMethod,
        };
    }

    private static PrimitiveType RemapStructureType(PrimitiveType type, int[] structureMap)
    {
        if ((int)type is < 100 or >= ManagedClassTypes.First)
        {
            return type;
        }

        var sourceIndex = (int)type - 100;
        if (sourceIndex >= structureMap.Length || structureMap[sourceIndex] < 0)
        {
            throw Unsupported("A native import reference has an unrepresentable aggregate type.");
        }
        return (PrimitiveType)(100 + structureMap[sourceIndex]);
    }

    private static bool Scalar(PrimitiveType type) => Array.IndexOf(s_types, type) >= 0;

    private static bool CanReinterpret(PrimitiveType source, PrimitiveType destination)
        => (source is PrimitiveType.Int32 or PrimitiveType.UInt32
                && destination is PrimitiveType.Int32 or PrimitiveType.UInt32)
            || (source is PrimitiveType.Int64 or PrimitiveType.UInt64
                && destination is PrimitiveType.Int64 or PrimitiveType.UInt64);

    private static bool IsInteger(PrimitiveType type)
        => type is PrimitiveType.Int32 or PrimitiveType.UInt32
            or PrimitiveType.Int64 or PrimitiveType.UInt64;

    private static bool IsFloat(PrimitiveType type)
        => type is PrimitiveType.Float32 or PrimitiveType.Float64;

    private static (bool HasFloatingTypes, bool HasIndexedFlow, bool HasNonScalarStorage) AnalyzeFunction(
        BoundFunction function)
    {
        var hasFloatingTypes = IsFloat(function.ReturnType);
        var hasNonScalarStorage = false;
        for (var index = 0; index < function.Variables.Length; index++)
        {
            var variable = function.Variables[index];
            hasFloatingTypes |= IsFloat(variable.Type);
            var isNativeCallableParameter = function.NativeFunctionPointerType is not null
                && index < function.ParameterCount
                && (variable.Type == PrimitiveType.NativeRawPointer
                    || NativeFunctionPointerTypes.IsType(variable.Type));
            hasNonScalarStorage |= (!Scalar(variable.Type) && !isNativeCallableParameter
                    && !NativeFunctionPointerTypes.IsType(variable.Type)
                    && !NativePointerTypes.IsType(variable.Type))
                || variable.Type == PrimitiveType.Void;
        }

        var hasIndexedFlow = false;
        AnalyzeStatement(function.Body, false, ref hasFloatingTypes, ref hasIndexedFlow);
        return (hasFloatingTypes, hasIndexedFlow, hasNonScalarStorage);
    }

    private static bool HasNativeFunctionPointerBodyOperation(BoundFunction function)
    {
        var hasNativeFunctionPointerOperation = false;
        _ = BoundTreeTraversal.RewriteExpressions(function, expression =>
        {
            hasNativeFunctionPointerOperation |= expression is BoundNativeFunctionAddress
                or BoundIndirectCall;
            return expression;
        });
        return hasNativeFunctionPointerOperation;
    }

    private static void AnalyzeStatement(BoundStatement statement, bool nested,
        ref bool hasFloatingTypes, ref bool hasIndexedFlow)
    {
        if (hasFloatingTypes && hasIndexedFlow)
        {
            return;
        }

        switch (statement)
        {
            case BoundBlock block:
                if (nested && !hasIndexedFlow)
                {
                    foreach (var child in block.Statements)
                    {
                        if (child is BoundLocal)
                        {
                            hasIndexedFlow = true;
                            break;
                        }
                    }
                }
                foreach (var child in block.Statements)
                {
                    AnalyzeStatement(child, true, ref hasFloatingTypes, ref hasIndexedFlow);
                    if (hasFloatingTypes && hasIndexedFlow)
                    {
                        return;
                    }
                }
                break;
            case BoundLocal local:
                AnalyzeExpression(local.Initializer, ref hasFloatingTypes, ref hasIndexedFlow);
                break;
            case BoundAssignment assignment:
                AnalyzeExpression(assignment.Value, ref hasFloatingTypes, ref hasIndexedFlow);
                break;
            case BoundExpressionStatement expression:
                AnalyzeExpression(expression.Expression, ref hasFloatingTypes, ref hasIndexedFlow);
                break;
            case BoundReturn { Value: BoundExpression value }:
                AnalyzeExpression(value, ref hasFloatingTypes, ref hasIndexedFlow);
                break;
            case BoundIf conditional:
                hasIndexedFlow = true;
                AnalyzeExpression(conditional.Condition, ref hasFloatingTypes, ref hasIndexedFlow);
                AnalyzeStatement(conditional.Then, true, ref hasFloatingTypes, ref hasIndexedFlow);
                if (conditional.Else is not null)
                {
                    AnalyzeStatement(conditional.Else, true, ref hasFloatingTypes, ref hasIndexedFlow);
                }
                break;
            case BoundWhile loop:
                hasIndexedFlow = true;
                AnalyzeExpression(loop.Condition, ref hasFloatingTypes, ref hasIndexedFlow);
                AnalyzeStatement(loop.Body, true, ref hasFloatingTypes, ref hasIndexedFlow);
                if (loop.Iterator is not null)
                {
                    AnalyzeStatement(loop.Iterator, true, ref hasFloatingTypes, ref hasIndexedFlow);
                }
                break;
            case BoundUnsafe unsafeBlock:
                hasIndexedFlow = true;
                AnalyzeStatement(unsafeBlock.Body, true, ref hasFloatingTypes, ref hasIndexedFlow);
                break;
            case BoundRequire require:
                hasIndexedFlow = true;
                AnalyzeExpression(require.Condition, ref hasFloatingTypes, ref hasIndexedFlow);
                break;
        }
    }

    private static void AnalyzeExpression(BoundExpression expression,
        ref bool hasFloatingTypes, ref bool hasIndexedFlow)
    {
        if (hasFloatingTypes && hasIndexedFlow)
        {
            return;
        }
        hasFloatingTypes |= IsFloat(expression.Type);
        if (hasFloatingTypes && hasIndexedFlow)
        {
            return;
        }

        switch (expression)
        {
            case BoundUnsafeExpression unsafeExpression:
                hasIndexedFlow = true;
                AnalyzeExpression(unsafeExpression.Expression,
                    ref hasFloatingTypes, ref hasIndexedFlow);
                break;
            case BoundBinary binary:
                hasIndexedFlow |= binary.Operator is TokenKind.ShiftLeft or TokenKind.ShiftRight
                    or TokenKind.ShiftRightUnsigned or TokenKind.AmpersandAmpersand or TokenKind.PipePipe;
                AnalyzeExpression(binary.Left, ref hasFloatingTypes, ref hasIndexedFlow);
                AnalyzeExpression(binary.Right, ref hasFloatingTypes, ref hasIndexedFlow);
                break;
            case BoundUnary unary:
                AnalyzeExpression(unary.Operand, ref hasFloatingTypes, ref hasIndexedFlow);
                break;
            case BoundCast cast:
                AnalyzeExpression(cast.Operand, ref hasFloatingTypes, ref hasIndexedFlow);
                break;
            case BoundConditional conditional:
                hasIndexedFlow = true;
                AnalyzeExpression(conditional.Condition, ref hasFloatingTypes, ref hasIndexedFlow);
                AnalyzeExpression(conditional.WhenTrue, ref hasFloatingTypes, ref hasIndexedFlow);
                AnalyzeExpression(conditional.WhenFalse, ref hasFloatingTypes, ref hasIndexedFlow);
                break;
            case BoundCall call:
                hasIndexedFlow = true;
                foreach (var argument in call.Arguments)
                {
                    AnalyzeExpression(argument, ref hasFloatingTypes, ref hasIndexedFlow);
                    if (hasFloatingTypes && hasIndexedFlow)
                    {
                        return;
                    }
                }
                break;
            case BoundIndirectCall call:
                hasIndexedFlow = true;
                AnalyzeExpression(call.Target, ref hasFloatingTypes, ref hasIndexedFlow);
                foreach (var argument in call.Arguments)
                {
                    AnalyzeExpression(argument, ref hasFloatingTypes, ref hasIndexedFlow);
                    if (hasFloatingTypes && hasIndexedFlow)
                    {
                        return;
                    }
                }
                break;
            case BoundNativeFunctionAddress:
                hasIndexedFlow = true;
                break;
            case BoundUpdate or BoundCompoundAssignment:
                hasIndexedFlow = true;
                break;
        }
    }

    private static ImmutableArray<int> CollectNativeBlittableStructureIndexes(BoundProgram program)
    {
        var required = new bool[program.Structures.Length];
        var visiting = new bool[program.Structures.Length];
        var pointerVisited = new bool[program.NativePointerTypes.Length];
        var functionPointerVisited = new bool[program.NativeFunctionPointerSignatures.Length];
        foreach (var function in program.Functions)
        {
            if (function.DirectNativeImport is DirectNativeImport import)
            {
                foreach (var parameter in import.Parameters)
                {
                    VisitType(parameter.PointeeType, throughPointer: true);
                }
            }

            if (function.NativeVTableMethod is NativeVTableMethodDefinition vtableMethod)
            {
                foreach (var parameter in vtableMethod.Parameters)
                {
                    VisitType(parameter.PointeeType, throughPointer: true);
                }
            }
        }
        for (var index = 0; index < program.NativePointerTypes.Length; index++)
        {
            VisitType(NativePointerTypes.Type(index), throughPointer: true);
        }
        for (var index = 0; index < program.NativeFunctionPointerSignatures.Length; index++)
        {
            var signature = program.NativeFunctionPointerSignatures[index];
            VisitType(signature.ReturnType, throughPointer: true);
            foreach (var parameterType in signature.ParameterTypes)
            {
                VisitType(parameterType, throughPointer: true);
            }
        }

        var indexes = ImmutableArray.CreateBuilder<int>();
        for (var index = 0; index < required.Length; index++)
        {
            if (required[index])
            {
                indexes.Add(index);
            }
        }
        return indexes.ToImmutable();

        void VisitType(PrimitiveType type, bool throughPointer)
        {
            if ((int)type is >= 100 and < ManagedClassTypes.First)
            {
                Visit((int)type - 100, throughPointer);
                return;
            }
            if (NativePointerTypes.IsType(type))
            {
                var index = NativePointerTypes.Index(type);
                if ((uint)index >= (uint)program.NativePointerTypes.Length)
                {
                    throw Unsupported("A native pointer type is outside the pointer type table.");
                }
                if (pointerVisited[index])
                {
                    return;
                }

                pointerVisited[index] = true;
                VisitType(program.NativePointerTypes[index].PointeeType, throughPointer: true);
                return;
            }
            if (NativeFunctionPointerTypes.IsType(type))
            {
                var index = NativeFunctionPointerTypes.Index(type);
                if ((uint)index >= (uint)program.NativeFunctionPointerSignatures.Length)
                {
                    throw Unsupported("A native function-pointer type is outside its signature table.");
                }
                if (functionPointerVisited[index])
                {
                    return;
                }

                functionPointerVisited[index] = true;
                var signature = program.NativeFunctionPointerSignatures[index];
                VisitType(signature.ReturnType, throughPointer: true);
                foreach (var parameterType in signature.ParameterTypes)
                {
                    VisitType(parameterType, throughPointer: true);
                }
                return;
            }
            if (DirectNativeImportContract.IsScalar(type)
                || type is PrimitiveType.NativeRawPointer or PrimitiveType.Void)
            {
                return;
            }

            throw Unsupported($"Native value type '{type}' has no provisional TRRM encoding.");
        }

        void Visit(int index, bool throughPointer)
        {
            if (index < 0 || index >= program.Structures.Length)
            {
                throw Unsupported("A native aggregate pointee is outside the structure table.");
            }
            if (required[index])
            {
                return;
            }
            if (visiting[index])
            {
                if (throughPointer)
                {
                    return;
                }
                throw Unsupported("A native Blittable structure graph is recursive.");
            }

            var structure = program.Structures[index];
            if (structure.BlittableTargetRequirement != FixedLayoutStorage.WindowsX64TargetRequirement
                || structure.IsSequential == structure.IsTransparent
                || structure.IsUnique || structure.CleanupMethod is not null
                || (structure.IsPrivate && structure.IsInternal)
                || structure.Fields.IsDefaultOrEmpty)
            {
                throw Unsupported($"Native aggregate '{structure.Name}' lacks a supported fixed Blittable contract.");
            }

            visiting[index] = true;
            foreach (var field in structure.Fields)
            {
                if (field is null || field.RequiresUnsafeCaller
                    || (field.IsPrivate && field.IsInternal))
                {
                    throw Unsupported($"Native Blittable field in '{structure.Name}' has unsupported access metadata.");
                }

                if (DirectNativeImportContract.IsScalar(field.Type))
                {
                    continue;
                }
                VisitType(field.Type, throughPointer: NativePointerTypes.IsType(field.Type)
                    || NativeFunctionPointerTypes.IsType(field.Type));
            }

            visiting[index] = false;
            required[index] = true;
        }
    }

    private static int TypeRow(PrimitiveType type)
    {
        var row = Array.IndexOf(s_types, type) + 1;
        if (row == 0)
        {
            throw Unsupported($"Unsupported scalar type {type}.");
        }
        return row;
    }

    private static TrRmNativeOutputContract? FindNativeOutput(
        ImmutableArray<TrRmNativeOutputContract> outputs, int parameterIndex)
    {
        foreach (var output in outputs)
        {
            if (output.ParameterOrdinal == parameterIndex)
            {
                return output;
            }
        }

        return null;
    }

    private static TrRmNativeParameterContract? FindNativeParameter(
        ImmutableArray<TrRmNativeParameterContract> parameters, int parameterIndex)
    {
        if (parameters.IsDefaultOrEmpty)
        {
            return null;
        }

        foreach (var parameter in parameters)
        {
            if (parameter.ParameterOrdinal == parameterIndex)
            {
                return parameter;
            }
        }

        return null;
    }

    private static int NativeOutputTypeRow(List<ReadOnlyMemory<byte>> typeRows,
        Dictionary<PrimitiveType, int> typeRowMap, PrimitiveType pointeeType)
    {
        if (typeRowMap.TryGetValue(pointeeType, out var row))
        {
            return row;
        }

        if (pointeeType == PrimitiveType.NativeRawPointer)
        {
            var innerRow = typeRows.Count + 1;
            typeRows.Add(TrRmTypeSpecTableWriter.InlineFamily(0x8C, 0));
            row = typeRows.Count + 1;
            typeRows.Add(TrRmTypeSpecTableWriter.IndexedFamily(0x8C, (uint)innerRow, 1));
            typeRowMap.Add(pointeeType, row);
            return row;
        }

        var typeIndex = Array.IndexOf(s_types, pointeeType);
        if (typeIndex < 0)
        {
            throw Unsupported($"Unsupported native output pointee type {pointeeType}.");
        }
        row = typeRows.Count + 1;
        typeRows.Add(TrRmTypeSpecTableWriter.InlineFamily(0x8C, s_codes[typeIndex], 1));
        typeRowMap.Add(pointeeType, row);
        return row;
    }

    private static int NativeParameterTypeRow(List<ReadOnlyMemory<byte>> typeRows,
        Dictionary<PrimitiveType, int> typeRowMap, PrimitiveType pointeeType)
    {
        if (typeRowMap.TryGetValue(pointeeType, out var row))
        {
            return row;
        }

        if (pointeeType == PrimitiveType.Void)
        {
            row = typeRows.Count + 1;
            typeRows.Add(TrRmTypeSpecTableWriter.InlineFamily(0x8C, 0));
        }
        else if (pointeeType == PrimitiveType.NativeRawPointer)
        {
            var innerRow = typeRows.Count + 1;
            typeRows.Add(TrRmTypeSpecTableWriter.InlineFamily(0x8C, 0));
            row = typeRows.Count + 1;
            typeRows.Add(TrRmTypeSpecTableWriter.IndexedFamily(0x8C, (uint)innerRow));
        }
        else
        {
            var typeIndex = Array.IndexOf(s_types, pointeeType);
            if (typeIndex < 0)
            {
                throw Unsupported($"Unsupported native parameter pointee type {pointeeType}.");
            }
            row = typeRows.Count + 1;
            typeRows.Add(TrRmTypeSpecTableWriter.InlineFamily(0x8C, s_codes[typeIndex]));
        }

        typeRowMap.Add(pointeeType, row);
        return row;
    }

    private static int NativeParameterTypeRow(List<ReadOnlyMemory<byte>> typeRows,
        Dictionary<PrimitiveType, int> typeRowMap, PrimitiveType pointeeType,
        Dictionary<int, int> structureTypeDefRows, Dictionary<int, int> typeDefTypeSpecRows,
        Dictionary<PrimitiveType, int> structurePointerTypeRows)
    {
        if ((int)pointeeType is >= 100 and < ManagedClassTypes.First)
        {
            if (structurePointerTypeRows.TryGetValue(pointeeType, out var existing))
            {
                return existing;
            }

            var structureIndex = (int)pointeeType - 100;
            if (!structureTypeDefRows.TryGetValue(structureIndex, out var typeDefRow))
            {
                throw Unsupported($"Unsupported native parameter aggregate pointee {pointeeType}.");
            }

            var typeDefTypeSpecRow = TypeDefTypeSpecRow(typeRows, typeDefTypeSpecRows, typeDefRow);
            var pointerTypeSpecRow = typeRows.Count + 1;
            typeRows.Add(TrRmTypeSpecTableWriter.IndexedFamily(0x8C,
                (uint)typeDefTypeSpecRow));
            structurePointerTypeRows.Add(pointeeType, pointerTypeSpecRow);
            return pointerTypeSpecRow;
        }

        return NativeParameterTypeRow(typeRows, typeRowMap, pointeeType);
    }

    private static int NativeValueTypeRow(PrimitiveType type, int nativeRawPointerTypeRow,
        Dictionary<PrimitiveType, int> nativeFunctionPointerTypeRows)
    {
        if (NativeFunctionPointerTypes.IsType(type))
        {
            return nativeFunctionPointerTypeRows.TryGetValue(type, out var row)
                ? row : throw Unsupported("A native function-pointer type has no TypeSpec row.");
        }

        if (type != PrimitiveType.NativeRawPointer)
        {
            return TypeRow(type);
        }

        return nativeRawPointerTypeRow != 0
            ? nativeRawPointerTypeRow
            : throw Unsupported("A native vtable pointer type has no raw-pointer TypeSpec row.");
    }

    private static int NativeValueTypeRow(PrimitiveType type, int nativeRawPointerTypeRow,
        Dictionary<PrimitiveType, int> nativeFunctionPointerTypeRows,
        List<ReadOnlyMemory<byte>> typeRows,
        Dictionary<PrimitiveType, int> nativePointerTypeRows,
        ImmutableArray<NativePointerTypeDefinition> nativePointerTypes,
        Dictionary<int, int> structureTypeDefRows,
        Dictionary<int, int> typeDefTypeSpecRows)
    {
        if (NativePointerTypes.IsType(type))
        {
            return NativePointerTypeRow(type, nativeRawPointerTypeRow,
                nativeFunctionPointerTypeRows, typeRows, nativePointerTypeRows,
                nativePointerTypes, structureTypeDefRows, typeDefTypeSpecRows);
        }

        if (NativeFunctionPointerTypes.IsType(type))
        {
            return nativeFunctionPointerTypeRows.TryGetValue(type, out var row)
                ? row : throw Unsupported("A native function-pointer type has no TypeSpec row.");
        }

        if (type == PrimitiveType.NativeRawPointer)
        {
            return nativeRawPointerTypeRow != 0
                ? nativeRawPointerTypeRow
                : throw Unsupported("A native vtable pointer type has no raw-pointer TypeSpec row.");
        }

        if ((int)type is >= 100 and < ManagedClassTypes.First)
        {
            var structureIndex = (int)type - 100;
            if (!structureTypeDefRows.TryGetValue(structureIndex, out var typeDefRow))
            {
                throw Unsupported($"A native pointer refers to an unrepresented structure type {type}.");
            }

            return TypeDefTypeSpecRow(typeRows, typeDefTypeSpecRows, typeDefRow);
        }

        return TypeRow(type);
    }

    private static int NativePointerTypeRow(PrimitiveType pointerType,
        int nativeRawPointerTypeRow,
        Dictionary<PrimitiveType, int> nativeFunctionPointerTypeRows,
        List<ReadOnlyMemory<byte>> typeRows,
        Dictionary<PrimitiveType, int> nativePointerTypeRows,
        ImmutableArray<NativePointerTypeDefinition> nativePointerTypes,
        Dictionary<int, int> structureTypeDefRows,
        Dictionary<int, int> typeDefTypeSpecRows)
    {
        if (nativePointerTypeRows.TryGetValue(pointerType, out var row))
        {
            return row;
        }

        var pointerIndex = NativePointerTypes.Index(pointerType);
        if ((uint)pointerIndex >= (uint)nativePointerTypes.Length)
        {
            throw Unsupported("A native pointer type has no definition.");
        }

        var definition = nativePointerTypes[pointerIndex];
        var qualifiers = definition.IsWritable ? (byte)1 : (byte)0;
        if (definition.PointeeType == PrimitiveType.Void)
        {
            row = typeRows.Count + 1;
            typeRows.Add(TrRmTypeSpecTableWriter.InlineFamily(0x8C, 0, qualifiers));
        }
        else if (Array.IndexOf(s_types, definition.PointeeType) is var scalarIndex
            && scalarIndex >= 0)
        {
            row = typeRows.Count + 1;
            typeRows.Add(TrRmTypeSpecTableWriter.InlineFamily(0x8C,
                s_codes[scalarIndex], qualifiers));
        }
        else
        {
            var pointeeRow = NativeValueTypeRow(definition.PointeeType,
                nativeRawPointerTypeRow, nativeFunctionPointerTypeRows,
                typeRows, nativePointerTypeRows, nativePointerTypes,
                structureTypeDefRows, typeDefTypeSpecRows);
            row = typeRows.Count + 1;
            typeRows.Add(TrRmTypeSpecTableWriter.IndexedFamily(0x8C,
                checked((uint)pointeeRow), qualifiers));
        }

        nativePointerTypeRows.Add(pointerType, row);
        return row;
    }

    private static int NativeBlittableFieldTypeRow(List<ReadOnlyMemory<byte>> typeRows,
        Dictionary<int, int> typeDefTypeSpecRows, Dictionary<int, int> structureTypeDefRows,
        PrimitiveType fieldType, int nativeRawPointerTypeRow,
        Dictionary<PrimitiveType, int> nativeFunctionPointerTypeRows,
        Dictionary<PrimitiveType, int> nativePointerTypeRows,
        ImmutableArray<NativePointerTypeDefinition> nativePointerTypes)
    {
        if ((int)fieldType is >= 100 and < ManagedClassTypes.First)
        {
            var structureIndex = (int)fieldType - 100;
            if (!structureTypeDefRows.TryGetValue(structureIndex, out var typeDefRow))
            {
                throw Unsupported($"Unsupported native Blittable field type {fieldType}.");
            }
            return TypeDefTypeSpecRow(typeRows, typeDefTypeSpecRows, typeDefRow);
        }

        return NativeValueTypeRow(fieldType, nativeRawPointerTypeRow,
            nativeFunctionPointerTypeRows, typeRows, nativePointerTypeRows,
            nativePointerTypes, structureTypeDefRows, typeDefTypeSpecRows);
    }

    private static int TypeDefTypeSpecRow(List<ReadOnlyMemory<byte>> typeRows,
        Dictionary<int, int> typeDefTypeSpecRows, int typeDefRow)
    {
        if (typeDefTypeSpecRows.TryGetValue(typeDefRow, out var row))
        {
            return row;
        }

        row = typeRows.Count + 1;
        typeRows.Add(TrRmTypeSpecTableWriter.Reference(0x02, (uint)typeDefRow));
        typeDefTypeSpecRows.Add(typeDefRow, row);
        return row;
    }

    private static ReadOnlyMemory<byte> NativeBlittableLayoutPayload(int targetRequirement)
    {
        var payload = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)targetRequirement);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), 1);
        return payload;
    }

    private static ReadOnlyMemory<byte> NativeAbiPayload(byte role = 1)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, 1);
        payload[4] = role;
        return payload;
    }

    private static ReadOnlyMemory<byte> NativeOutputPayload(int parameterIndex,
        uint resultCondition, uint failureDisposition)
    {
        var condition = resultCondition switch
        {
            DirectNativeImportContract.Int32NonZeroResultCondition
                or DirectNativeImportContract.UInt32NonZeroResultCondition => (byte)2,
            DirectNativeImportContract.Int32NonNegativeResultCondition => (byte)4,
            _ => throw Unsupported("Unsupported native output result condition."),
        };
        if (failureDisposition != 1)
        {
            throw Unsupported("Unsupported native output failure disposition.");
        }

        var payload = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)parameterIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), 1);
        payload[8] = condition;
        payload[9] = 2;
        return payload;
    }

    private static ReadOnlyMemory<byte> NativeParameterPayload(int ordinal, byte access,
        byte retention, uint extent)
    {
        var payload = new byte[20];
        if (retention != (byte)NativeParameterRetention.NoEscape)
        {
            throw Unsupported("Only call-extent native parameter retention is supported.");
        }

        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)ordinal);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), 1);
        payload[8] = access;
        payload[9] = 1;
        payload[10] = extent == 0 ? (byte)0 : (byte)1;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12), extent);
        return payload;
    }

    private static ImmutableArray<BoundStruct> LoadBlittableStructures(
        TrRmTypeDefTableReader typeDefs, TrRmFieldDefTableReader fields,
        TrRmLayoutTableReader layouts, TrRmContractTableReader contracts,
        TrRmTypeSpecTableReader typeSpecs, ImmutableArray<BoundStruct> optionalStructures,
        List<NativePointerTypeDefinition> nativePointerTypes,
        out int[] structureTypeByTypeDef)
    {
        var structureRows = new List<int>();
        structureTypeByTypeDef = new int[typeDefs.Count + 1];
        Array.Fill(structureTypeByTypeDef, -1);
        for (var row = 1; row <= typeDefs.Count; row++)
        {
            if (contracts.GetNativeBlittableTargetRequirement(row) == 0)
            {
                continue;
            }

            structureTypeByTypeDef[row] = optionalStructures.Length + structureRows.Count;
            structureRows.Add(row);
        }

        var structures = optionalStructures.ToList();
        foreach (var row in structureRows)
        {
            var flags = typeDefs.GetFlags(row);
            var layoutRow = typeDefs.GetLayoutRow(row);
            var size = layouts.GetSize(layoutRow);
            if (size is 0 or > ValueLayout.MaximumStructureSize)
            {
                throw Invalid("A native Blittable TypeDef has an unsupported size.");
            }

            var typeNamespace = typeDefs.GetNamespace(row);
            var name = typeNamespace.Length == 0
                ? typeDefs.GetName(row) : typeNamespace + "." + typeDefs.GetName(row);
            var layoutKind = (flags >> 4) & 0x03;
            structures.Add(new BoundStruct(name, [], (int)size)
            {
                IsSequential = layoutKind == 1,
                IsTransparent = layoutKind == 3,
                BlittableTargetRequirement = (int)contracts.GetNativeBlittableTargetRequirement(row),
                IsPrivate = (flags & 3) == 0,
                IsInternal = (flags & 3) == 1,
            });
        }

        foreach (var row in structureRows)
        {
            var structureIndex = structureTypeByTypeDef[row];
            var (firstField, endField) = typeDefs.GetFieldRange(row);
            if (firstField == 0 || endField <= firstField)
            {
                throw Invalid("A native Blittable TypeDef must have fields.");
            }

            var structureFields = ImmutableArray.CreateBuilder<BoundStructField>(
                endField - firstField);
            for (var fieldRow = firstField; fieldRow < endField; fieldRow++)
            {
                var fieldType = FromNativeValueRow(typeSpecs, fields.GetTypeSpecRow(fieldRow),
                    nativePointerTypes, structureTypeByTypeDef);

                var fieldFlags = fields.GetFlags(fieldRow);
                var fieldOffset = fields.GetByteOffset(fieldRow);
                var fieldSize = ValueLayout.Size(fieldType, structures);
                if (fieldOffset == uint.MaxValue || fieldOffset > int.MaxValue
                    || fieldSize <= 0)
                {
                    throw Invalid("A native Blittable field has an unsupported fixed layout.");
                }

                structureFields.Add(new BoundStructField(fields.GetName(fieldRow), fieldType,
                    (int)fieldOffset, fieldSize)
                {
                    IsPrivate = (fieldFlags & 3) == 0,
                    IsInternal = (fieldFlags & 3) == 1,
                    IsMutable = (fieldFlags & 4) != 0,
                });
            }

            structures[structureIndex] = structures[structureIndex] with
            {
                Fields = structureFields.MoveToImmutable(),
            };
        }

        var result = structures.ToImmutableArray();
        ImmutableArray<int> alignments;
        try
        {
            alignments = FixedLayoutStorage.Validate(result);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException(
                $"TRRM native Blittable-layout audit failed: {exception.Message}", exception);
        }

        foreach (var row in structureRows)
        {
            var structureIndex = structureTypeByTypeDef[row];
            var layoutRow = typeDefs.GetLayoutRow(row);
            if (layouts.GetSize(layoutRow) != result[structureIndex].Size
                || layouts.GetAlignment(layoutRow) != (uint)alignments[structureIndex])
            {
                throw Invalid("A native Blittable Layout row differs from its recomputed structure.");
            }
        }

        return result;
    }

    private static PrimitiveType FromNativeValueRow(TrRmTypeSpecTableReader types,
        int row, List<NativePointerTypeDefinition> nativePointerTypes,
        int[] structureTypeByTypeDef)
    {
        var type = types.GetRow(row);
        if (type.Length == 4 && type[0] == 0x01 && type[2] == 0x8C)
        {
            var pointeeIndex = Array.IndexOf(s_codes, type[3]);
            if (pointeeIndex < 0)
            {
                throw Invalid("Unsupported native pointer pointee TypeSpec.");
            }

            return GetNativePointerType(nativePointerTypes, s_types[pointeeIndex],
                type[1] != 0);
        }

        if (type.Length == 7 && type[0] == 0x0C && type[2] == 0x8C)
        {
            var pointeeRow = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(type[3..]));
            var pointeeType = FromNativeValueRow(types, pointeeRow,
                nativePointerTypes, structureTypeByTypeDef);
            return GetNativePointerType(nativePointerTypes, pointeeType, type[1] != 0);
        }

        if (type.Length == 6 && type[0] == 0x02 && type[1] == 0)
        {
            var typeDefRow = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(type[2..]));
            if ((uint)typeDefRow < (uint)structureTypeByTypeDef.Length
                && structureTypeByTypeDef[typeDefRow] >= 0)
            {
                return (PrimitiveType)(100 + structureTypeByTypeDef[typeDefRow]);
            }

            throw Invalid("A native pointer refers to an unrepresented structure type.");
        }

        if (type.Length == 6 && type[0] == 0x0D && type[1] == 0)
        {
            var signatureRow = BinaryPrimitives.ReadUInt32LittleEndian(type[2..]);
            if (signatureRow == 0 || signatureRow > types.NativeFunctionPointerSignatureCount)
            {
                throw Invalid("Invalid native function-pointer TypeSpec.");
            }

            return NativeFunctionPointerTypes.Type(checked((int)signatureRow - 1));
        }

        return FromRow(types, row);
    }

    private static PrimitiveType GetNativePointerType(
        List<NativePointerTypeDefinition> nativePointerTypes,
        PrimitiveType pointeeType, bool isWritable)
    {
        if (pointeeType == PrimitiveType.Void && !isWritable)
        {
            return PrimitiveType.NativeRawPointer;
        }

        for (var index = 0; index < nativePointerTypes.Count; index++)
        {
            var existing = nativePointerTypes[index];
            if (existing.PointeeType == pointeeType && existing.IsWritable == isWritable)
            {
                return NativePointerTypes.Type(index);
            }
        }

        if (nativePointerTypes.Count >= SemanticVerifier.MaximumAnalysisVisits)
        {
            throw Invalid("The native pointer type table exceeds the analysis limit.");
        }

        var type = NativePointerTypes.Type(nativePointerTypes.Count);
        nativePointerTypes.Add(new NativePointerTypeDefinition(pointeeType, isWritable));
        return type;
    }

    private static PrimitiveType NativeParameterPointee(TrRmTypeSpecTableReader typeSpecs,
        int typeSpecRow, TrRmContractTableReader contracts, int[] structureTypeByTypeDef)
    {
        if (TrRmNativeImportTypeContract.TryGetNativeParameterPointee(
            typeSpecs, typeSpecRow, out var pointeeType))
        {
            return pointeeType;
        }

        if (TrRmNativeImportTypeContract.TryGetBlittableAggregateParameterTypeDef(
            typeSpecs, typeSpecRow, contracts, out var typeDefRow)
            && typeDefRow < structureTypeByTypeDef.Length
            && structureTypeByTypeDef[typeDefRow] >= 0)
        {
            return (PrimitiveType)(100 + structureTypeByTypeDef[typeDefRow]);
        }

        throw Invalid("Invalid native parameter pointee TypeSpec.");
    }

    private static PrimitiveType BodyTypeFromRow(TrRmTypeSpecTableReader types, int row)
    {
        var typeSpec = types.GetRow(row);
        if (typeSpec.Length != 6 || typeSpec[0] != 0x06)
        {
            if (typeSpec.Length == 4 && typeSpec[0] == 0x01
                && typeSpec[1] == 0 && typeSpec[2] == 0x8C)
            {
                return PrimitiveType.NativeRawPointer;
            }

            return FromNativeValueRow(types, row);
        }

        var resolved = TrRmCompactLinearBodyReader.ResolveType(types, row);
        var payloadCode = (byte)(resolved & 0xFF);
        var payloadIndex = Array.IndexOf(s_codes, payloadCode);
        return payloadIndex > 0
            ? TrRmOptionalScalar.StorageType(s_types[payloadIndex])
            : throw Invalid("Unsupported optional scalar payload.");
    }

    private static PrimitiveType BodyTypeFromRow(TrRmTypeSpecTableReader types, int row,
        List<NativePointerTypeDefinition> nativePointerTypeDefinitions,
        int[] structureTypeByTypeDef)
    {
        var typeSpec = types.GetRow(row);
        if (typeSpec.Length == 6 && typeSpec[0] == 0x06)
        {
            var resolved = TrRmCompactLinearBodyReader.ResolveType(types, row);
            var payloadCode = (byte)(resolved & 0xFF);
            var payloadIndex = Array.IndexOf(s_codes, payloadCode);
            return payloadIndex > 0
                ? TrRmOptionalScalar.StorageType(s_types[payloadIndex])
                : throw Invalid("Unsupported optional scalar payload.");
        }

        return FromNativeValueRow(types, row, nativePointerTypeDefinitions,
            structureTypeByTypeDef);
    }

    private static PrimitiveType FromRow(TrRmTypeSpecTableReader types, int row)
    {
        var bytes = types.GetRow(row);
        if (bytes.Length != 3 || bytes[0] != 1 || bytes[1] != 0)
        {
            throw Invalid("Unsupported qualified or constructed TypeSpec.");
        }
        var index = Array.IndexOf(s_codes, bytes[2]);
        return index >= 0 ? s_types[index] : throw Invalid("Unsupported scalar TypeSpec.");
    }

    private static PrimitiveType FromNativeValueRow(TrRmTypeSpecTableReader types, int row)
    {
        var bytes = types.GetRow(row);
        if (bytes.Length == 4 && bytes[0] == 1 && bytes[1] == 0
            && bytes[2] == 0x8C && bytes[3] <= 0x19)
        {
            return PrimitiveType.NativeRawPointer;
        }
        if (bytes.Length == 6 && bytes[0] == 0x0D && bytes[1] == 0)
        {
            var signatureRow = BinaryPrimitives.ReadUInt32LittleEndian(bytes[2..]);
            if (signatureRow == 0 || signatureRow > types.NativeFunctionPointerSignatureCount)
            {
                throw Invalid("Invalid native function-pointer TypeSpec.");
            }
            return NativeFunctionPointerTypes.Type(checked((int)signatureRow - 1));
        }

        return FromRow(types, row);
    }
    private static NotSupportedException Unsupported(string reason) => new(reason);
    private static InvalidDataException Invalid(string reason) => new($"Invalid or unsupported executable TRRM: {reason}");
}
