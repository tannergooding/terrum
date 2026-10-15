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
        var nativeComCatalog = program.NativeImportCatalog;
        var nativeComInterfaces = nativeComCatalog?.ComInterfaces ?? [];
        var nativeComMethods = nativeComCatalog?.ComMethods ?? [];
        if (program.Functions.Any(static function =>
            function.IsExternal && function.DirectNativeImport is null
                && function.NativeComMethod is null))
        {
            throw Unsupported("Only direct native imports and source-authored COM methods are supported as external declarations.");
        }
        if (!nativeComInterfaces.IsEmpty
            && (nativeComCatalog?.Identity != "source-native-com.v1"
                || !nativeComCatalog.ComOwners.IsEmpty
                || !nativeComCatalog.ComSignatureTypes.IsEmpty))
        {
            throw Unsupported("TRRM COM persistence requires source-authored interfaces and currently excludes owners and custom signature types.");
        }

        if (string.IsNullOrEmpty(moduleIdentity))
        {
            throw new ArgumentException("A module identity is required.", nameof(moduleIdentity));
        }

        if (program.EntryPoint == -1)
        {
            SemanticVerifier.VerifyLibraryReferences(program);
        }
        else
        {
            SemanticVerifier.Verify(program);
        }

        if (program.RuntimePolicy != BoundRuntimePolicy.FailFast
            || !program.Structures.IsEmpty || !program.Classes.IsEmpty || !program.Buffers.IsEmpty)
        {
            throw Unsupported("Only fail-fast scalar modules are executable.");
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
        foreach (var method in nativeComMethods)
        {
            if (!NativeComCatalog.TryParseSignature(method.Signature, out _, out var signatureParameters)
                || signatureParameters.Length == 0)
            {
                throw Unsupported($"Native COM method '{method.Identity}' has an invalid signature.");
            }
            parameterCount = checked(parameterCount + signatureParameters.Length - 1);
        }
        var effectCount = 0;
        var exportCount = 0;
        var nativeImportCount = 0;
        for (var index = 0; index < program.Functions.Length; index++)
        {
            var function = program.Functions[index];
            if (function.IsExternal && !DirectNativeImportContract.IsValid(function))
            {
                throw Unsupported($"Function '{function.Name}' has an invalid direct native import contract.");
            }
            var features = function.IsExternal ? default : AnalyzeFunction(function);
            functionFeatures[index] = features;
            outputFeatures[index] = !function.IsExternal && HasOutputFeature(function);
            hasFloatingTypes |= features.HasFloatingTypes;
            if (function.DirectNativeImport is DirectNativeImport directImport)
            {
                hasFloatingTypes |= directImport.Outputs.Any(static output => IsFloat(output.PointeeType));
                hasFloatingTypes |= function.Variables.Take(function.ParameterCount)
                    .Any(static variable => IsFloat(variable.Type));
            }
            parameterCount = checked(parameterCount + function.ParameterCount);
            effectCount += function.IsNoThrow || function.DoesNotReturn || function.RequiresUnsafeCaller ? 1 : 0;
            exportCount += !function.IsPrivate && !function.IsInternal ? 1 : 0;
            nativeImportCount += function.DirectNativeImport is not null ? 1 : 0;
        }
        var names = new List<(string Text, int Encoding)>(program.Functions.Length + 1)
        {
            (moduleIdentity, 1),
        };
        var interfaceRows = nativeComInterfaces
            .OrderBy(static definition => definition.NominalIdentity, StringComparer.Ordinal).ToArray();
        var interfaceTypeRows = new Dictionary<string, int>(interfaceRows.Length, StringComparer.Ordinal);
        var interfaceNamespaceRows = new int[interfaceRows.Length];
        var interfaceNameRows = new int[interfaceRows.Length];
        for (var index = 0; index < interfaceRows.Length; index++)
        {
            var identity = interfaceRows[index].NominalIdentity;
            var separator = identity.LastIndexOf('.');
            var typeNamespace = separator < 0 ? "" : identity[..separator];
            var typeName = separator < 0 ? identity : identity[(separator + 1)..];
            if (typeName.Length == 0 || !interfaceTypeRows.TryAdd(identity, index + 1))
            {
                throw Unsupported($"Native COM interface identity '{identity}' cannot be represented in TRRM.");
            }
            interfaceNamespaceRows[index] = names.Count + 1;
            names.Add((typeNamespace, 1));
            interfaceNameRows[index] = names.Count + 1;
            names.Add((typeName, 1));
        }
        var nameRows = new int[program.Functions.Length];
        var nativeLibraryRows = new int[program.Functions.Length];
        var nativeExportRows = new int[program.Functions.Length];
        var seen = new HashSet<string>(program.Functions.Length, StringComparer.Ordinal);
        var bodies = new List<ReadOnlyMemory<byte>>(program.Functions.Length + nativeComMethods.Length);
        var signatures = new List<TrRmMethodSigRow>(program.Functions.Length + nativeComMethods.Length);
        var parameters = new List<TrRmParamRow>(parameterCount);
        var methods = new List<TrRmMethodDefRow>(program.Functions.Length + nativeComMethods.Length);
        var contracts = new List<TrRmContractRow>(effectCount + nativeImportCount + interfaceRows.Length);
        var interfaceContractFirst = new int[interfaceRows.Length];
        for (var index = 0; index < interfaceRows.Length; index++)
        {
            var definition = interfaceRows[index];
            var baseTypeRow = definition.ImmediateBaseIdentity is string baseIdentity
                ? interfaceTypeRows.TryGetValue(baseIdentity, out var baseRow) ? baseRow
                    : throw Unsupported($"Native COM interface '{definition.NominalIdentity}' has an unrepresented base.")
                : 0;
            interfaceContractFirst[index] = contracts.Count + 1;
            contracts.Add(new TrRmContractRow(0x1300_0000u | (uint)(index + 1), 7,
                NativeComInterfacePayload(definition.SdkIid, baseTypeRow)));
        }
        var exports = new List<TrRmExportRow>(exportCount);
        var typeRowsList = (hasFloatingTypes ? s_typeRows : s_nonFloatTypeRows).ToList();
        var nativeOutputTypeRows = new Dictionary<PrimitiveType, int>();
        var interfacePointerTypeRows = new Dictionary<string, int>(interfaceRows.Length,
            StringComparer.Ordinal);
        foreach (var (identity, typeRow) in interfaceTypeRows)
        {
            var typeDefReference = typeRowsList.Count + 1;
            typeRowsList.Add(TrRmTypeSpecTableWriter.Reference(0x02, (uint)typeRow));
            var pointerType = typeRowsList.Count + 1;
            typeRowsList.Add(TrRmTypeSpecTableWriter.IndexedFamily(0x8C, (uint)typeDefReference));
            interfacePointerTypeRows.Add(identity, pointerType);
        }
        for (var index = 0; index < program.Functions.Length; index++)
        {
            var function = program.Functions[index];
            if (!seen.Add(function.Name) || function.IsConstructor
                || function.IsClassConstructor || function.ClassConstructor != -1
                || function.DeclaringStruct is not null || function.PropertyName is not null
                || function.IsStatic || function.Intrinsic != BoundIntrinsic.None || function.IsCleanup
                || !Scalar(function.ReturnType))
            {
                throw Unsupported($"Function '{function.Name}' has an unsupported declaration or effect.");
            }
            if (!function.IsExternal && functionFeatures[index].HasNonScalarStorage)
            {
                throw Unsupported($"Function '{function.Name}' contains non-scalar storage.");
            }
            if (function.DirectNativeImport is null && HasOutputParameters(function)
                && !function.IsPrivate && !function.IsInternal)
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

            var first = function.ParameterCount == 0 ? 0 : parameters.Count + 1;
            for (var parameter = 0; parameter < function.ParameterCount; parameter++)
            {
                var variable = function.Variables[parameter];
                var nativeOutput = DirectNativeImportContract.OutputForParameter(function, parameter);
                var isNativeOutput = nativeOutput is not null;
                var parameterType = isNativeOutput
                    ? NativeOutputTypeRow(typeRowsList, nativeOutputTypeRows, nativeOutput!.PointeeType)
                    : TypeRow(variable.Type);
                if (variable.Type == PrimitiveType.Void || variable.IsInitializationLocal
                    || (function.DirectNativeImport is null
                        && (!Scalar(variable.Type) || variable.IsOut != variable.IsInit
                            || variable.IsInit && !IsOutputScalarType(variable.Type)))
                    || (function.DirectNativeImport is not null
                        && (isNativeOutput
                            ? variable.Type != PrimitiveType.NativeStorageAddress
                                || !variable.IsOut || variable.IsInit
                            : !Scalar(variable.Type) || variable.IsOut || variable.IsInit)))
                {
                    throw Unsupported($"Function '{function.Name}' has a non-value scalar parameter.");
                }

                parameters.Add(new TrRmParamRow(index + 1, parameter, parameterType,
                    variable.IsInit && function.DirectNativeImport is null ? (byte)2 : (byte)0));
            }

            signatures.Add(new TrRmMethodSigRow(TypeRow(function.ReturnType), 0,
                first, function.ParameterCount, 0, 0));
            var bodyRow = 0;
            if (!function.IsExternal)
            {
                bodyRow = bodies.Count + 1;
                bodies.Add(outputFeatures[index]
                    ? EncodeOutputBody(function, program)
                    : functionFeatures[index].HasIndexedFlow
                        ? new IndexedBuilder(function, program, functionFeatures[index].HasFloatingTypes).Encode()
                        : EncodeBody(function, functionFeatures[index].HasFloatingTypes));
            }

            var effectMask = (function.IsNoThrow ? 1u : 0u)
                | (function.DoesNotReturn ? 2u : 0u)
                | (function.RequiresUnsafeCaller ? 4u : 0u);
            var contractFirst = effectMask != 0 || function.DirectNativeImport is not null
                ? contracts.Count + 1 : 0;
            if (effectMask != 0)
            {
                contracts.Add(new TrRmContractRow(0x1600_0000u | (uint)(index + 1), 1,
                    s_effectPayloads[effectMask]));
            }
            if (function.DirectNativeImport is DirectNativeImport nativeImport)
            {
                foreach (var output in nativeImport.Outputs)
                {
                    contracts.Add(new TrRmContractRow(0x1600_0000u | (uint)(index + 1), 6,
                        NativeOutputPayload(output.ParameterIndex, output.ResultCondition,
                            output.FailureDisposition)));
                }
            }

            var visibility = function.IsPrivate ? 0u : function.IsInternal ? 1u : 2u;
            methods.Add(new TrRmMethodDefRow(0, nameRows[index], index + 1, visibility,
                contractFirst, contractFirst == 0 ? 0 : contracts.Count - contractFirst + 1, bodyRow));
            if (visibility == 2)
            {
                exports.Add(new TrRmExportRow(0x1600_0000u | (uint)(index + 1), nameRows[index]));
            }
        }

        foreach (var method in nativeComMethods
            .OrderBy(static method => method.DeclaringInterfaceIdentity, StringComparer.Ordinal)
            .ThenBy(static method => method.Slot))
        {
            if (method.OwnerInterfaceIdentity != method.DeclaringInterfaceIdentity
                || method.OutputParameterIndex >= 0
                || method.BoundMethodName is not string methodName
                || !interfaceTypeRows.TryGetValue(method.DeclaringInterfaceIdentity, out var ownerTypeRow)
                || !NativeComCatalog.TryParseSignature(method.Signature,
                    out var returnIdentity, out var signatureParameters)
                || signatureParameters.Length == 0
                || signatureParameters[0] != method.DeclaringInterfaceIdentity + "*"
                || !interfacePointerTypeRows.TryGetValue(method.DeclaringInterfaceIdentity,
                    out var receiverTypeSpec)
                || !TryGetComPrimitiveType(returnIdentity, out var returnType))
            {
                throw Unsupported($"Native COM method '{method.Identity}' has an unsupported TRRM signature.");
            }

            var methodRow = methods.Count + 1;
            var methodNameRow = names.Count + 1;
            names.Add((methodName, 1));
            var firstParameter = signatureParameters.Length == 1 ? 0 : parameters.Count + 1;
            for (var parameterIndex = 1; parameterIndex < signatureParameters.Length; parameterIndex++)
            {
                if (!TryCreateComTypeSpecRow(signatureParameters[parameterIndex],
                    interfaceTypeRows, typeRowsList, out var parameterTypeSpec))
                {
                    throw Unsupported($"Native COM method '{method.Identity}' has an unsupported parameter type.");
                }
                parameters.Add(new TrRmParamRow(methodRow, parameterIndex - 1,
                    parameterTypeSpec, 0));
            }
            signatures.Add(new TrRmMethodSigRow(TypeRow(returnType), receiverTypeSpec,
                firstParameter, signatureParameters.Length - 1, 0, 0));

            var effectMask = (method.IsNoThrow ? 1u : 0u) | 4u;
            var contractFirst = contracts.Count + 1;
            contracts.Add(new TrRmContractRow(0x1600_0000u | (uint)methodRow, 1,
                s_effectPayloads[effectMask]));
            contracts.Add(new TrRmContractRow(0x1600_0000u | (uint)methodRow, 8,
                NativeComMethodPayload((uint)method.Slot, (uint)method.Effects)));
            foreach (var parameter in method.NativeParameters)
            {
                if (parameter.ParameterIndex <= 0
                    || parameter.ParameterIndex >= signatureParameters.Length
                    || parameter.Access is < NativeComParameterAccess.Read
                        or > NativeComParameterAccess.ReadWrite
                    || parameter.Retention is < NativeComParameterRetention.NoEscape
                        or > NativeComParameterRetention.MayEscape
                    || !signatureParameters[parameter.ParameterIndex].EndsWith("*",
                        StringComparison.Ordinal))
                {
                    throw Unsupported($"Native COM method '{method.Identity}' has invalid parameter metadata.");
                }
                contracts.Add(new TrRmContractRow(0x1600_0000u | (uint)methodRow, 10,
                    NativeParameterPayload(parameter.ParameterIndex - 1,
                        (byte)((byte)parameter.Access + 1), (byte)parameter.Retention,
                        (uint)parameter.Extent)));
            }
            methods.Add(new TrRmMethodDefRow(ownerTypeRow, methodNameRow, signatures.Count,
                2, contractFirst, contracts.Count - contractFirst + 1, 0));
        }

        var stringsBytes = names.Any(static name => name.Text.Length == 0)
            ? TrRmStringTableWriter.EncodeTextRows(names)
            : TrRmStringTableWriter.EncodeNames(names);
        var strings = new TrRmStringTableReader(stringsBytes, names.Count);
        var hasOutputFeatures = outputFeatures.Any(static enabled => enabled);
        var featureRows = new List<TrRmFeatureRow>(2);
        if (hasOutputFeatures)
        {
            featureRows.Add(TrRmFeatureRow.ScalarInitOutput);
        }
        if (nativeImportCount != 0)
        {
            featureRows.Add(TrRmFeatureRow.WindowsNamedNativeImports);
        }
        if (interfaceRows.Length != 0)
        {
            featureRows.Add(TrRmFeatureRow.NativeComContracts);
        }
        var featureCount = featureRows.Count;
        var featureBytes = featureCount == 0
            ? [] : TrRmFeatureTableWriter.Encode(featureRows);
        var moduleBytes = TrRmModuleTableWriter.Encode(
            [new TrRmModuleRow(1, 1, 0, program.EntryPoint + 1, 1, 1, 1, 1,
                featureCount == 0 ? 0 : 1, featureCount)],
            strings, 0, interfaceRows.Length, methods.Count, 0, featureCount);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings, 0, interfaceRows.Length,
            methods.Count, 0, featureCount);
        var typeRows = typeRowsList.ToArray();
        var typeBytes = TrRmTypeSpecTableWriter.Encode(typeRows, interfaceRows.Length, 0, 0, 0);
        var types = new TrRmTypeSpecTableReader(typeBytes, typeRows.Length,
            interfaceRows.Length, 0, 0, 0);
        var sigBytes = TrRmMethodSigTableWriter.Encode(signatures, types, parameters.Count, 0);
        var sigs = new TrRmMethodSigTableReader(sigBytes, signatures.Count, types, parameters.Count, 0);
        var paramBytes = parameters.Count == 0 ? [] : TrRmParamTableWriter.Encode(parameters, sigs, types);
        var typeDefRows = new TrRmTypeDefRow[interfaceRows.Length];
        for (var type = 0; type < typeDefRows.Length; type++)
        {
            typeDefRows[type] = new TrRmTypeDefRow(interfaceNamespaceRows[type], interfaceNameRows[type],
                0x0E, 0, 0, 0, 0, 0, interfaceContractFirst[type], 1);
        }
        var typeDefBytes = typeDefRows.Length == 0 ? []
            : TrRmTypeDefTableWriter.Encode(typeDefRows, strings, modules, 0, 0, 0, contracts.Count);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, typeDefRows.Length,
            strings, modules, 0, 0, 0, contracts.Count);
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
        var exportBytes = exports.Count == 0 ? []
            : TrRmExportTableWriter.Encode(exports, strings, typeDefs, defs, default, default);
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
        var sectionCount = 5 + (featureCount != 0 ? 1 : 0)
            + (typeDefRows.Length != 0 ? 1 : 0)
            + (parameters.Count != 0 ? 1 : 0)
            + (contracts.Count != 0 ? 1 : 0) + (exports.Count != 0 ? 1 : 0)
            + (bodies.Count != 0 ? 1 : 0) + (nativeImportRows.Count != 0 ? 1 : 0);
        var sections = new List<TrRmSection>(sectionCount)
        {
            new(1, 0, 1, moduleBytes),
            new(2, 0, (uint)names.Count, stringsBytes),
        };
        if (featureCount != 0)
        {
            sections.Add(new(4, 0, (uint)featureCount, featureBytes));
        }
        sections.Add(new(0x11, 0, (uint)typeRows.Length, typeBytes));
        if (typeDefRows.Length != 0)
        {
            sections.Add(new(0x13, 0, (uint)typeDefRows.Length, typeDefBytes));
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
            sections.Add(new(0x21, 0, (uint)nativeImportRows.Count, nativeImportBytes));
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
        var sigs = validatedTables.Signatures;
        var defs = validatedTables.Methods;
        var parameters = validatedTables.Parameters;
        var bodies = validatedTables.Bodies;
        var contractTable = validatedTables.Contracts;
        var nativeImports = validatedTables.NativeImports;
        var typeCount = types.Count;
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out _, out _, out _);
            if (kind is not (1 or 2 or 4 or 0x11 or 0x13 or 0x16 or 0x17 or 0x18
                or 0x1B or 0x1D or 0x20 or 0x21))
            {
                throw Invalid($"Section 0x{kind:X4} is not executable.");
            }
        }

        index.TryGetSection(1, out var moduleBytes, out var moduleCount);
        index.TryGetSection(0x16, out _, out var methodCount);
        index.TryGetSection(0x17, out _, out var sigCount);
        index.TryGetSection(0x18, out _, out _);
        index.TryGetSection(0x13, out _, out var typeDefCount);
        index.TryGetSection(4, out var featureBytes, out var featureCount);
        index.TryGetSection(0x1B, out var contractBytes, out _);
        index.TryGetSection(0x1D, out var exportBytes, out var exportCount);
        index.TryGetSection(0x20, out _, out var bodyCount);
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
        uint nativeImportRuleRevision = 0;
        uint nativeComRuleRevision = 0;
        for (var feature = firstFeature; feature < firstFeature + requiredFeatureCount; feature++)
        {
            var required = featureTable.GetFeature(feature);
            if (required.FeatureId == TrRmFeatureTableReader.ScalarInitOutputFeatureId)
            {
                scalarInitOutputRuleRevision = required.MinimumRuleRevision;
            }
            else if (required.FeatureId == TrRmFeatureTableReader.WindowsNamedNativeImportsFeatureId)
            {
                nativeImportRuleRevision = required.MinimumRuleRevision;
            }
            else if (required.FeatureId == TrRmFeatureTableReader.NativeComContractsFeatureId)
            {
                nativeComRuleRevision = required.MinimumRuleRevision;
            }
        }
        if ((nativeImports.Count != 0) != (nativeImportRuleRevision != 0))
        {
            throw Invalid("The Windows named native import feature does not match its table.");
        }
        var hasNativeComInterfaces = false;
        for (var typeRow = 1; typeRow <= typeDefCount; typeRow++)
        {
            hasNativeComInterfaces |= contractTable.GetNativeComInterface(typeRow) is not null;
        }
        if (hasNativeComInterfaces != (nativeComRuleRevision != 0))
        {
            throw Invalid("The native COM feature does not match its interface contracts.");
        }

        var declaredExports = new HashSet<int>(exportCount);
        for (var row = 0; row < exportCount; row++)
        {
            var id = BinaryPrimitives.ReadUInt32LittleEndian(exportBytes.Slice(row * 8));
            var name = BinaryPrimitives.ReadUInt32LittleEndian(exportBytes.Slice(row * 8 + 4));
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
        var usedSignatures = new bool[sigCount + 1];
        var usedSignatureCount = 0;
        for (var row = 1; row <= methodCount; row++)
        {
            var flags = defs.GetFlags(row);
            var name = defs.GetName(row);
            var signature = defs.GetSignature(row);
            var isNativeImport = nativeImports.HasImportForMethod(row);
            var isNativeComMethod = contractTable.GetNativeComMethod(row) is not null;
            if (isNativeComMethod)
            {
                if (isNativeImport || defs.GetOwnerType(row) == 0 || defs.GetBodyRow(row) != 0
                    || sigs.GetReceiverTypeSpec(signature) == 0
                    || flags != 2)
                {
                    throw Invalid($"Unsupported COM method declaration for method {row}.");
                }
                if (!usedSignatures[signature])
                {
                    usedSignatures[signature] = true;
                    usedSignatureCount++;
                }
                continue;
            }
            if (defs.GetOwnerType(row) != 0
                || isNativeImport == (defs.GetBodyRow(row) != 0)
                || sigs.GetReceiverTypeSpec(signature) != 0
                || sigs.GetGenericCount(signature) != 0
                || !seen.Add(name))
            {
                throw Invalid($"Unsupported declaration for method {row}.");
            }
            if ((flags == 2) != declaredExports.Contains(row))
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
            var nativeOutputs = isNativeImport
                ? contractTable.GetNativeOutputs(row) : [];
            for (var param = 0; param < count; param++)
            {
                var nativeOutput = FindNativeOutput(nativeOutputs, param);
                var type = nativeOutput is null
                    ? FromRow(types, parameters.GetTypeSpec(first + param))
                    : PrimitiveType.NativeStorageAddress;
                var mode = parameters.GetMode(first + param);
                if (type == PrimitiveType.Void
                    || mode != 0 && mode != 2
                    || isNativeImport && (mode != 0
                        || nativeOutput is null && !Scalar(type)
                        || nativeOutput is not null && type != PrimitiveType.NativeStorageAddress)
                    || !isNativeImport && mode == 2 && (scalarInitOutputRuleRevision == 0
                        || scalarInitOutputRuleRevision == TrRmFeatureTableReader.ScalarInitOutputInitialRuleRevision
                            && type != PrimitiveType.Int32
                        || flags == 2))
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
                    if (kind == 6 && isNativeImport)
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
            if (isNativeImport)
            {
                var directOutputs = ImmutableArray.CreateBuilder<DirectNativeOutput>(nativeOutputs.Length);
                foreach (var output in nativeOutputs)
                {
                    directOutputs.Add(new DirectNativeOutput(output.ParameterOrdinal,
                        output.PointeeType,
                        output.ResultCondition, output.FailureDisposition));
                }
                directNativeImport = new DirectNativeImport(nativeImports.GetLibraryName(row),
                    nativeImports.GetExportName(row), directOutputs.MoveToImmutable());
            }
            functionIndexByMethodRow[row] = functions.Count;
            functions.Add(new BoundFunction(name, FromRow(types, sigs.GetResultTypeSpec(signature)),
                variables.ToImmutable(), count, new BoundBlock([], default), default)
            {
                IsNoThrow = noThrow,
                DoesNotReturn = doesNotReturn,
                RequiresUnsafeCaller = requiresUnsafeCaller,
                IsExternal = isNativeImport,
                DirectNativeImport = directNativeImport,
                IsPrivate = flags == 0,
                IsInternal = flags == 1,
            });
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
            if (declaration.IsExternal)
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
                    parameters, types);
                indexed = IndexedBuilder.DecodeIndexedBody(body, count,
                    declaration.ReturnType, noThrow, declaration.DoesNotReturn, types, functions);
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
                _ = new TrRmCompactLinearBodyReader(bodies, row, modules, defs, sigs, parameters, types);
                statements = DecodeBody(body, count, variables, noThrow, types, functions);
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
                        [new TrRmScalarBlock(Enumerable.Range(1, count).ToImmutableArray(),
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
                    scalarInitOutputRuleRevision);
                if (outputBody.Places.IsEmpty)
                {
                    throw Invalid("The executable general Body profile requires places.");
                }

                variables = declaration.Variables.ToBuilder();
                statements = DecodeOutputBody(outputBody, types, variables, functions,
                    scalarInitOutputRuleRevision);
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
        var nativeComCatalog = LoadNativeComCatalog(typeDefs, contractTable,
            defs, sigs, parameters, types);
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
            Structures = hasOptionalScalarTypes ? TrRmOptionalScalar.Structures : [],
            NativeImportCatalog = nativeComCatalog,
            NativeImportCatalogId = nativeComCatalog?.Identity,
        };
        try
        {
            if (entry == -1)
            {
                SemanticVerifier.VerifyLibraryReferences(program);
            }
            else
            {
                SemanticVerifier.Verify(program);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException($"TRRM executable semantic audit failed: {exception.Message}", exception);
        }
        return program;
    }

    /// <summary>Reads a validated TRRM library and exposes its public direct native imports to source binding.</summary>
    public static CompilationReference CreateReference(byte[] providerArtifact)
    {
        ArgumentNullException.ThrowIfNull(providerArtifact);
        var program = Load(providerArtifact);
        var hasNativeComContracts = program.NativeImportCatalog?.SupportsNativeComContracts == true;
        if (program.EntryPoint != -1 || !program.Structures.IsEmpty
            || !program.Classes.IsEmpty || !program.Buffers.IsEmpty
            || program.Functions.Any(static function =>
                !function.IsPrivate && !function.IsInternal
                    && (!function.IsExternal || function.DirectNativeImport is null
                        && function.NativeComMethod is null)))
        {
            throw Unsupported("A TRRM native import reference requires a library of public external declarations.");
        }

        var functions = program.Functions
            .Where(static function => !function.IsPrivate && !function.IsInternal)
            .ToImmutableArray();
        if (functions.IsEmpty && !hasNativeComContracts)
        {
            throw Unsupported("A TRRM reference requires at least one public declaration or COM interface.");
        }

        return new CompilationReference([], functions, program.RuntimePolicy,
            nativeImportCatalog: program.NativeImportCatalog);
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
        foreach (var variable in function.Variables)
        {
            hasFloatingTypes |= IsFloat(variable.Type);
            hasNonScalarStorage |= !Scalar(variable.Type) || variable.Type == PrimitiveType.Void;
        }

        var hasIndexedFlow = false;
        AnalyzeStatement(function.Body, false, ref hasFloatingTypes, ref hasIndexedFlow);
        return (hasFloatingTypes, hasIndexedFlow, hasNonScalarStorage);
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
            case BoundUpdate or BoundCompoundAssignment:
                hasIndexedFlow = true;
                break;
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

    private static ReadOnlyMemory<byte> NativeOutputPayload(int parameterIndex,
        uint resultCondition, uint failureDisposition)
    {
        var payload = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)parameterIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), resultCondition);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), failureDisposition);
        return payload;
    }

    private static ReadOnlyMemory<byte> NativeComInterfacePayload(Guid iid, int baseTypeDefRow)
    {
        var payload = new byte[20];
        _ = iid.TryWriteBytes(payload.AsSpan(0, 16), bigEndian: true, out _);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16), (uint)baseTypeDefRow);
        return payload;
    }

    private static ReadOnlyMemory<byte> NativeComMethodPayload(uint slot, uint effects)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, slot);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), effects);
        return payload;
    }

    private static ReadOnlyMemory<byte> NativeParameterPayload(int ordinal, byte access,
        byte retention, uint extent)
    {
        var payload = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)ordinal);
        payload[4] = access;
        payload[5] = retention;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), extent);
        return payload;
    }

    private static bool TryGetComPrimitiveType(string identity, out PrimitiveType type)
    {
        type = identity switch {
            "void" => PrimitiveType.Void,
            "bool" => PrimitiveType.Bool,
            "char16" => PrimitiveType.Char16,
            "int32" or "HRESULT" or "INT" => PrimitiveType.Int32,
            "uint32" or "UINT" or "ULONG" => PrimitiveType.UInt32,
            "int64" => PrimitiveType.Int64,
            "uint64" or "SIZE_T" => PrimitiveType.UInt64,
            "float32" or "float" or "FLOAT" => PrimitiveType.Float32,
            "float64" or "double" or "DOUBLE" => PrimitiveType.Float64,
            _ => PrimitiveType.Void,
        };
        return identity is "void" or "bool" or "char16" or "int32" or "HRESULT" or "INT"
            or "uint32" or "UINT" or "ULONG" or "int64" or "uint64" or "SIZE_T"
            or "float32" or "float" or "FLOAT" or "float64" or "double" or "DOUBLE";
    }

    private static bool TryCreateComTypeSpecRow(string identity,
        IReadOnlyDictionary<string, int> interfaceTypeRows,
        List<ReadOnlyMemory<byte>> typeRows, out int typeSpecRow)
    {
        identity = identity.Trim();
        if (identity.EndsWith("*", StringComparison.Ordinal))
        {
            if (!TryCreateComTypeSpecRow(identity[..^1], interfaceTypeRows, typeRows,
                out var elementTypeSpec))
            {
                typeSpecRow = 0;
                return false;
            }
            typeSpecRow = typeRows.Count + 1;
            var elementRow = typeRows[elementTypeSpec - 1].Span;
            typeRows.Add(elementRow.Length == 3 && elementRow[0] == 0x01 && elementRow[1] == 0
                    ? TrRmTypeSpecTableWriter.InlineFamily(0x8C, elementRow[2])
                    : TrRmTypeSpecTableWriter.IndexedFamily(0x8C, (uint)elementTypeSpec));
            return true;
        }
        if (TryGetComPrimitiveType(identity, out var primitiveType))
        {
            typeSpecRow = TypeRow(primitiveType);
            return true;
        }
        if (interfaceTypeRows.TryGetValue(identity, out var typeDefRow))
        {
            typeSpecRow = typeRows.Count + 1;
            typeRows.Add(TrRmTypeSpecTableWriter.Reference(0x02, (uint)typeDefRow));
            return true;
        }
        typeSpecRow = 0;
        return false;
    }

    private static NativeImportCatalog? LoadNativeComCatalog(TrRmTypeDefTableReader typeDefs,
        TrRmContractTableReader contracts, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader typeSpecs)
    {
        var identityRows = new Dictionary<int, string>();
        var interfaceRows = new List<int>();
        for (var row = 1; row <= typeDefs.Count; row++)
        {
            if (contracts.GetNativeComInterface(row) is not TrRmNativeComInterfaceContract)
            {
                continue;
            }
            var typeNamespace = typeDefs.GetNamespace(row);
            var identity = typeNamespace.Length == 0
                ? typeDefs.GetName(row) : typeNamespace + "." + typeDefs.GetName(row);
            identityRows.Add(row, identity);
            interfaceRows.Add(row);
        }
        if (interfaceRows.Count == 0)
        {
            return null;
        }

        var methodsByInterface = new Dictionary<int, List<NativeComMethodDefinition>>();
        var allMethods = ImmutableArray.CreateBuilder<NativeComMethodDefinition>();
        var interfaceContracts = new Dictionary<int, TrRmNativeComInterfaceContract>();
        foreach (var row in interfaceRows)
        {
            interfaceContracts.Add(row, contracts.GetNativeComInterface(row)
                ?? throw Invalid("A native COM interface TypeDef lost its contract."));
        }
        for (var methodRow = 1; methodRow <= methods.Count; methodRow++)
        {
            if (contracts.GetNativeComMethod(methodRow) is not TrRmNativeComMethodContract comMethod)
            {
                continue;
            }
            var ownerType = methods.GetOwnerType(methodRow);
            if (!identityRows.TryGetValue(ownerType, out var interfaceIdentity)
                || comMethod.VTableSlot > int.MaxValue
                || !contracts.GetNativeOutputs(methodRow).IsDefaultOrEmpty)
            {
                throw Invalid("Unsupported TRRM native COM method owner, slot, or output contract.");
            }
            var signatureRow = methods.GetSignature(methodRow);
            var (parameterFirst, parameterCount) = signatures.GetParamRange(signatureRow);
            var receiverType = ReadComSignatureType(typeSpecs,
                signatures.GetReceiverTypeSpec(signatureRow), typeDefs);
            if (receiverType != interfaceIdentity + "*")
            {
                throw Invalid("A native COM MethodSig receiver differs from its declaring interface.");
            }
            var returnType = ReadComSignatureType(typeSpecs,
                signatures.GetResultTypeSpec(signatureRow), typeDefs);
            var effectMask = contracts.GetEffectsMask(methodRow);
            var effects = (NativeComEffects)comMethod.Effects;
            if (effects.HasFlag(NativeComEffects.SignedHResult) && returnType == "int32")
            {
                returnType = "HRESULT";
            }
            var nativeParameters = ImmutableArray.CreateBuilder<NativeComParameterDefinition>();
            var nativeParameterContracts = contracts.GetNativeParameters(methodRow);
            if (nativeParameterContracts.IsDefault)
            {
                nativeParameterContracts = [];
            }
            foreach (var parameter in nativeParameterContracts)
            {
                if (parameter.ParameterOrdinal >= parameterCount || parameter.Extent > int.MaxValue)
                {
                    throw Invalid("A native COM NativeParameter is outside its signature.");
                }
                nativeParameters.Add(new NativeComParameterDefinition(
                    parameter.ParameterOrdinal + 1,
                    (NativeComParameterAccess)(parameter.Access - 1),
                    (NativeComParameterRetention)parameter.Retention,
                    (int)parameter.Extent));
            }
            var parameterTypes = new string[parameterCount + 1];
            parameterTypes[0] = receiverType;
            for (var parameterIndex = 0; parameterIndex < parameterCount; parameterIndex++)
            {
                if (parameters.GetMode(parameterFirst + parameterIndex) != 0)
                {
                    throw Invalid("TRRM COM method parameter modes require explicit NativeParameter metadata.");
                }
                parameterTypes[parameterIndex + 1] = ReadComSignatureType(typeSpecs,
                    parameters.GetTypeSpec(parameterFirst + parameterIndex), typeDefs);
            }
            var signature = returnType + "(" + string.Join(",", parameterTypes) + ")";
            if (!NativeComCatalog.TryCreateAbi(signature, [], out var abi))
            {
                throw Invalid("A native COM method signature has no supported reconstructed ABI.");
            }
            var methodName = methods.GetName(methodRow);
            var methodIdentity = interfaceIdentity + "::" + methodName;
            var definition = new NativeComMethodDefinition(methodIdentity,
                interfaceIdentity, interfaceIdentity, (int)comMethod.VTableSlot,
                signature, abi, effects, IsCallable: true) {
                VTableMethodIdentity = methodIdentity,
                BoundMethodName = methodName,
                IsNoThrow = (effectMask & 1) != 0,
                RequiresNativeParameterMetadata = true,
                NativeParameters = nativeParameters.ToImmutable(),
                BorrowedOwnerParameters = [],
                HiddenParameters = [],
                StorageRecordParameters = [],
            };
            if (!methodsByInterface.TryGetValue(ownerType, out var declaredMethods))
            {
                declaredMethods = [];
                methodsByInterface.Add(ownerType, declaredMethods);
            }
            declaredMethods.Add(definition);
            allMethods.Add(definition);
        }

        var interfacesByRow = new Dictionary<int, NativeComInterfaceDefinition>();
        var visiting = new HashSet<int>();
        NativeComInterfaceDefinition BuildInterface(int row)
        {
            if (interfacesByRow.TryGetValue(row, out var existing))
            {
                return existing;
            }
            if (!visiting.Add(row))
            {
                throw Invalid("TRRM native COM interface ancestry is cyclic.");
            }
            var contract = interfaceContracts[row];
            NativeComInterfaceDefinition? baseDefinition = null;
            string? baseIdentity = null;
            if (contract.BaseTypeDefRow != 0)
            {
                baseDefinition = BuildInterface(contract.BaseTypeDefRow);
                baseIdentity = baseDefinition.NominalIdentity;
            }
            var declaredMethods = methodsByInterface.TryGetValue(row, out var declared)
                ? declared.OrderBy(static method => method.Slot)
                    .Select(static method => method.VTableMethodIdentity).ToImmutableArray()
                : ImmutableArray<string>.Empty;
            var flattenedMethods = (baseDefinition?.FlattenedMethods ?? [])
                .AddRange(declaredMethods);
            var definition = new NativeComInterfaceDefinition(identityRows[row], contract.Iid,
                baseIdentity, flattenedMethods, declaredMethods);
            visiting.Remove(row);
            interfacesByRow.Add(row, definition);
            return definition;
        }

        foreach (var row in interfaceRows)
        {
            _ = BuildInterface(row);
        }

        var interfaces = ImmutableArray.CreateBuilder<NativeComInterfaceDefinition>(interfaceRows.Count);
        var baseEdges = ImmutableArray.CreateBuilder<(string Source, string Target)>();
        foreach (var row in interfaceRows)
        {
            var definition = interfacesByRow[row];
            if (definition.ImmediateBaseIdentity is string baseIdentity)
            {
                baseEdges.Add((definition.NominalIdentity, baseIdentity));
            }
            interfaces.Add(definition);
        }

        try
        {
            return new NativeImportCatalog("trrm-native-com.v1", [], comInterfaces: interfaces.ToImmutable(),
                comOwners: [], comMethods: allMethods.ToImmutable(),
                comBaseEdges: baseEdges.ToImmutable(),
                comSignatureTypes: []);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Invalid reconstructed TRRM native COM interfaces.", exception);
        }
    }

    private static string ReadComSignatureType(TrRmTypeSpecTableReader typeSpecs, int row,
        TrRmTypeDefTableReader typeDefs)
    {
        var typeSpec = typeSpecs.GetRow(row);
        if (typeSpec[0] == 0x01)
        {
            var code = typeSpec[2];
            if (typeSpec.Length == 3)
            {
                return code switch {
                    0 => "void",
                    1 => "bool",
                    3 => "uint16",
                    8 => "int32",
                    0x0D => "uint32",
                    9 => "int64",
                    0x0E => "uint64",
                    0x14 => "float",
                    0x15 => "double",
                    _ => throw Invalid("Unsupported native COM scalar TypeSpec."),
                };
            }
            if (typeSpec.Length == 4 && code == 0x8C)
            {
                var innerType = typeSpec[3] switch {
                    0 => "void",
                    1 => "bool",
                    3 => "uint16",
                    8 => "int32",
                    0x0D => "uint32",
                    9 => "int64",
                    0x0E => "uint64",
                    0x14 => "float",
                    0x15 => "double",
                    _ => throw Invalid("Unsupported inline native COM pointer TypeSpec."),
                };
                return innerType + "*";
            }
        }
        else if (typeSpec[0] == 0x02)
        {
            var typeRow = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(typeSpec[2..]));
            return TrRmTypeIdentity(typeDefs, typeRow);
        }
        else if (typeSpec[0] == 0x0C && typeSpec[2] == 0x8C)
        {
            var elementRow = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(typeSpec[3..]));
            return ReadComSignatureType(typeSpecs, elementRow, typeDefs) + "*";
        }
        throw Invalid("Unsupported native COM signature TypeSpec.");
    }

    private static string TrRmTypeIdentity(TrRmTypeDefTableReader typeDefs, int row)
    {
        var typeNamespace = typeDefs.GetNamespace(row);
        return typeNamespace.Length == 0 ? typeDefs.GetName(row)
            : typeNamespace + "." + typeDefs.GetName(row);
    }

    private static PrimitiveType BodyTypeFromRow(TrRmTypeSpecTableReader types, int row)
    {
        var typeSpec = types.GetRow(row);
        if (typeSpec.Length != 6 || typeSpec[0] != 0x06)
        {
            return FromRow(types, row);
        }

        var resolved = TrRmCompactLinearBodyReader.ResolveType(types, row);
        var payloadCode = (byte)(resolved & 0xFF);
        var payloadIndex = Array.IndexOf(s_codes, payloadCode);
        return payloadIndex > 0
            ? TrRmOptionalScalar.StorageType(s_types[payloadIndex])
            : throw Invalid("Unsupported optional scalar payload.");
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
    private static NotSupportedException Unsupported(string reason) => new(reason);
    private static InvalidDataException Invalid(string reason) => new($"Invalid or unsupported executable TRRM: {reason}");
}
