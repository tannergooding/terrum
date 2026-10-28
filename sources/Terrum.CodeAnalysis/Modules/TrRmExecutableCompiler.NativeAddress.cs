// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    internal static byte[] EmitForeignInvocationDraftNativeAddressLibrary(
        BoundProgram program, string moduleIdentity)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        SemanticVerifier.VerifyLibraryWithDirectNativeImports(program);

        if (program.EntryPoint != -1 || program.Functions.Length != 2
            || !program.Structures.IsEmpty || !program.Classes.IsEmpty
            || !program.Buffers.IsEmpty || !program.NativeUtf16ZLiterals.IsEmpty
            || !program.NativeUtf8ZLiterals.IsEmpty || program.RequiresSourceGrant
            || program.InvocationProfileId is not null || program.IsInvocationSourceBound
            || program.RuntimePolicy != BoundRuntimePolicy.FailFast
            || !program.NativePointerTypes.IsEmpty
            || program.NativeFunctionPointerSignatures.Length != 1
            || !NativeFunctionPointerContract.IsValidTable(
                program.NativeFunctionPointerSignatures)
            || !NativePointerTypeContract.IsValidTable(program.NativePointerTypes,
                program.Structures, program.NativeFunctionPointerSignatures))
        {
            throw Unsupported(
                "Foreign-invocation NativeAddress emission requires two fail-fast functions and one pointer signature.");
        }

        var callerIndex = -1;
        var targetIndex = -1;
        BoundNativeFunctionAddress? address = null;
        for (var index = 0; index < program.Functions.Length; index++)
        {
            var function = program.Functions[index];
            var addresses = new List<BoundNativeFunctionAddress>();
            if (!BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundNativeFunctionAddress nativeAddress)
                    {
                        addresses.Add(nativeAddress);
                    }
                }))
            {
                throw Unsupported("A function Body cannot be traversed for NativeAddress operations.");
            }

            if (addresses.Count == 0)
            {
                continue;
            }
            if (addresses.Count != 1 || address is not null)
            {
                throw Unsupported("This NativeAddress producer supports one formation operation.");
            }

            callerIndex = index;
            address = addresses[0];
            targetIndex = address.Function;
        }

        if (address is not BoundNativeFunctionAddress selectedAddress
            || targetIndex < 0 || targetIndex >= program.Functions.Length
            || callerIndex < 0 || callerIndex == targetIndex)
        {
            throw Unsupported("A NativeAddress Body must select one distinct target declaration.");
        }

        var target = program.Functions[targetIndex];
        var caller = program.Functions[callerIndex];
        if (target.DirectNativeImport is not DirectNativeImport import
            || !DirectNativeImportContract.IsValid(target, program.Structures,
                program.NativeFunctionPointerSignatures, program.NativePointerTypes)
            || !import.Outputs.IsEmpty || !import.Parameters.IsEmpty
            || import.CallingConvention is < NativeCallingConvention.Unmanaged
                or > NativeCallingConvention.Vectorcall
            || target.DoesNotReturn || target.ReturnType != PrimitiveType.Void
            || target.ParameterCount != 0
            || caller.IsExternal || caller.DirectNativeImport is not null
            || caller.ReturnType != PrimitiveType.Void || caller.ParameterCount != 0
            || caller.DoesNotReturn || caller.RequiresUnsafeCaller
            || !caller.NativeOutputContracts.IsEmpty
            || !NativeFunctionPointerTypes.IsType(selectedAddress.Type))
        {
            throw Unsupported(
                "The NativeAddress producer supports a zero-argument, unit NativeImport and unit caller.");
        }

        var signatureIndex = NativeFunctionPointerTypes.Index(selectedAddress.Type);
        var signature = program.NativeFunctionPointerSignatures[signatureIndex];
        if (signature.CallingConvention != import.CallingConvention
            || signature.ReturnType != PrimitiveType.Void
            || !signature.ParameterTypes.IsEmpty || signature.EffectsMask != 0
            || !signature.Selectors.IsEmpty || !signature.OutputStates.IsEmpty
            || !signature.Dependencies.IsEmpty || !signature.NativeParameters.IsEmpty)
        {
            throw Unsupported("The selected pointer signature exceeds the NativeAddress producer subset.");
        }
        if (!NativeFunctionPointerContract.TryCreateNativeImportSignature(target,
            program.NativeFunctionPointerSignatures, out var actualSignature)
            || actualSignature is null
            || !NativeFunctionPointerContract.AreEquivalent(signature, actualSignature))
        {
            throw Unsupported("The selected pointer signature differs from its NativeImport contract.");
        }
        if (!selectedAddress.IsUnsafeAuthorized)
        {
            throw Unsupported("NativeAddress formation requires bound unsafe authorization.");
        }
        if (!IsAddressOnlyVoidBody(caller, selectedAddress))
        {
            throw Unsupported("The caller Body is outside the single discarded NativeAddress profile.");
        }

        var targetMethodRow = targetIndex + 1;
        var callerMethodRow = callerIndex + 1;
        var anchorMethodRow = program.Functions.Length + 1;
        var featureRows = new[] { new TrRmFeatureRow(4, 1) };
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows,
            TrRmArtifactSchema.ForeignInvocationDraft);
        var featureReader = new TrRmFeatureTableReader(featureBytes,
            featureRows.Length, TrRmArtifactSchema.ForeignInvocationDraft);

        var stringRows = new List<(string Text, int Encoding)> { ("", 0) };
        var stringIndexes = new Dictionary<(string Text, int Encoding), int>
        {
            [("", 0)] = 1,
        };
        var moduleNameRow = AddString(moduleIdentity, 1);
        var methodNameRows = new int[program.Functions.Length];
        for (var index = 0; index < program.Functions.Length; index++)
        {
            methodNameRows[index] = AddString(program.Functions[index].Name, 1);
        }
        var anchorNameRow = AddString(CreateAnchorName(program.Functions), 1);
        var libraryNameRow = AddString(import.LibraryName, 0);
        var exportNameRow = AddString(import.ExportName, 0);
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var strings = new TrRmStringTableReader(stringsBytes, stringRows.Count);

        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(moduleNameRow, 1, 0, 0, 1, 1, 1, 1,
                1, featureRows.Length, 1),
        ], strings, 0, 0, program.Functions.Length + 1, 0, featureRows.Length);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 0, program.Functions.Length + 1, 0, featureRows.Length);
        var typeSpecRows = new ReadOnlyMemory<byte>[]
        {
            TrRmTypeSpecTableWriter.NativeFunctionPointer(
                (byte)signature.CallingConvention, checked((uint)anchorMethodRow)),
        };
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            0, 0, 0, 0, TrRmArtifactSchema.ForeignInvocationDraft,
            methodDefCount: program.Functions.Length + 1);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Length, 0, 0, 0, 0,
            TrRmArtifactSchema.ForeignInvocationDraft,
            methodDefCount: program.Functions.Length + 1);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_0000, 0, 0, 0, 0, 0),
        ], typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 1,
            typeSpecs, 0, 0);
        var contractPayload = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(contractPayload, 4);
        var contractRows = new[]
        {
            new TrRmContractRow(0x1600_0000u | (uint)targetMethodRow,
                1, contractPayload),
        };
        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, contractRows.Length);
        var methodRows = new List<TrRmMethodDefRow>(program.Functions.Length + 1);
        var exportRows = new List<TrRmExportRow>();
        for (var index = 0; index < program.Functions.Length; index++)
        {
            var function = program.Functions[index];
            var methodRow = index + 1;
            var visibility = function.IsPrivate ? 0u : function.IsInternal ? 1u : 2u;
            var isTarget = index == targetIndex;
            var isCaller = index == callerIndex;
            methodRows.Add(new TrRmMethodDefRow(0, methodNameRows[index], 1,
                visibility, isTarget ? 1 : 0, isTarget ? 1 : 0,
                isCaller ? 1 : 0));
            if (visibility == 2 && function.ModuleImport is null)
            {
                exportRows.Add(new TrRmExportRow(
                    0x1600_0000u | (uint)methodRow, methodNameRows[index]));
            }
        }
        methodRows.Add(new TrRmMethodDefRow(0, anchorNameRow, 1, 0, 0, 0, 0));
        var methodBytes = TrRmMethodDefTableWriter.Encode(methodRows, strings,
            modules, typeDefs, signatures, contractRows.Length, 1);
        var methods = new TrRmMethodDefTableReader(methodBytes,
            methodRows.Count, strings, modules, typeDefs, signatures,
            contractRows.Length, 1);
        var contractBytes = TrRmContractTableWriter.EncodeForeignInvocationDraft(
            contractRows, typeDefs, methods, signatures, default, typeSpecs,
            featureReader, modules, predicateGraphCount: 0);
        var contracts = new TrRmContractTableReader(contractBytes,
            contractRows.Length, typeDefs, methods, signatures, default,
            typeSpecs, 0, featureReader, modules,
            TrRmArtifactSchema.ForeignInvocationDraft);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layouts = new TrRmLayoutTableReader([], 0, typeDefs, targetProfiles, 0);
        var nativeImportBytes = TrRmNativeImportTableWriter.Encode([
            new TrRmNativeImportRow(1, (byte)import.ResolutionMode,
                (byte)import.ResolutionPolicy, (byte)import.CallingConvention,
                targetMethodRow, libraryNameRow, exportNameRow),
        ], strings, methods, default, typeDefs, default, modules,
            signatures, default, typeSpecs, contracts, layouts);
        var maps = TrRmRegisterBodyMapBuilder.Create(
            [0x1100_0001], [0x1600_0000u | (uint)targetMethodRow]);
        var entryState = new byte[8];
        entryState[0] = 0x01;
        byte[] code = [0xE9, 0, 1, 0, 0x18, 0];
        var nativeAddress = TrRmRegisterBodyRecordWriter.EncodeNativeAddress(
            1, 15, maps.GetMethodMapIndex(0x1600_0000u | (uint)targetMethodRow),
            maps.GetTypeMapIndex(0x1100_0001));
        var body = TrRmRegisterBodyWriter.Encode(default, maps.TypeMap,
            maps.MethodMap, default, default,
            [new TrRmRegisterBodyBlock(0, (uint)code.Length, 0)],
            [entryState, nativeAddress], code, contractRows.Length);
        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, (uint)stringRows.Count, stringsBytes),
            new(0x0004, 0, (uint)featureRows.Length, featureBytes),
            new(0x0011, 0, (uint)typeSpecRows.Length, typeSpecBytes),
            new(0x0016, 0, (uint)methodRows.Count, methodBytes),
            new(0x0017, 0, 1, signatureBytes),
            new(0x001B, 0, (uint)contractRows.Length, contractBytes),
        };
        if (exportRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001D, 0, (uint)exportRows.Count,
                TrRmExportTableWriter.Encode(exportRows, strings, typeDefs,
                    methods, default, default, default, default, default,
                    featureReader, modules,
                    TrRmArtifactSchema.ForeignInvocationDraft)));
        }
        sections.Add(new TrRmSection(0x0020, 0, 1,
            TrRmVariableTableWriter.EncodeBodies([body])));
        sections.Add(new TrRmSection(0x0021, 0, 1, nativeImportBytes));

        var artifact = TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.ForeignInvocationDraft);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        tables.ValidateRegisterBodyMapsForMethod(callerMethodRow);
        return artifact;

        int AddString(string text, int encoding)
        {
            var key = (text, encoding);
            if (stringIndexes.TryGetValue(key, out var row))
            {
                return row;
            }

            row = stringRows.Count + 1;
            stringIndexes.Add(key, row);
            stringRows.Add(key);
            return row;
        }
    }

    private static bool IsAddressOnlyVoidBody(BoundFunction function,
        BoundNativeFunctionAddress address)
    {
        var statements = new List<BoundStatement>();
        if (!AppendStatements(function.Body, statements)
            || statements.Count != 2
            || statements[1] is not BoundReturn { Value: null })
        {
            return false;
        }

        if (statements[0] is BoundExpressionStatement
            { Expression: BoundNativeFunctionAddress expressionAddress })
        {
            return expressionAddress == address;
        }

        if (statements[0] is not BoundLocal
            {
                Initializer: BoundNativeFunctionAddress localAddress,
            } discard
            || localAddress != address
            || (uint)discard.Variable >= (uint)function.Variables.Length)
        {
            return false;
        }

        var variable = function.Variables[discard.Variable];
        if (variable.Name != "<discard>" || variable.Type != address.Type
            || variable.IsMutable || variable.IsOut || variable.IsInit
            || variable.IsInitializationLocal || variable.IsImplicitRef
            || variable.IsImplicitReferenceTemporary
            || variable.NativeFixedArrayLayout is not null)
        {
            return false;
        }

        var isRead = false;
        return BoundTreeTraversal.VisitExpressions(function.Body, expression =>
        {
            if (expression is BoundRead read && read.Variable == discard.Variable)
            {
                isRead = true;
            }
        }) && !isRead;
    }

    private static bool AppendStatements(BoundBlock block,
        List<BoundStatement> statements)
    {
        if (!block.RetiredManagedLocals.IsEmpty)
        {
            return false;
        }

        foreach (var statement in block.Statements)
        {
            switch (statement)
            {
                case BoundBlock nested:
                    if (!AppendStatements(nested, statements))
                    {
                        return false;
                    }
                    break;
                case BoundUnsafe unsafeBlock:
                    if (!AppendStatements(unsafeBlock.Body, statements))
                    {
                        return false;
                    }
                    break;
                default:
                    statements.Add(statement);
                    break;
            }
        }

        return true;
    }

    private static string CreateAnchorName(ImmutableArray<BoundFunction> functions)
    {
        var names = functions.Select(static function => function.Name)
            .ToHashSet(StringComparer.Ordinal);
        for (var index = 1; ; index++)
        {
            var name = $"__trrm_fnptr_anchor_{index}";
            if (!names.Contains(name))
            {
                return name;
            }
        }
    }
}
