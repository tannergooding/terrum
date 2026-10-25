// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    internal static BoundProgram LoadStandardScalarReturn(ReadOnlySpan<byte> artifact)
    {
        var index = new TrRmArtifactIndex(artifact, TrRmArtifactSchema.Standard);
        var tables = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(index);
        _ = index.TryGetSection(0x0004, out _, out var featureCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out _, out _, out _);
            if (kind is not (0x0001 or 0x0002 or 0x0003 or 0x0004 or 0x0011
                or 0x0016 or 0x0017 or 0x0020 or 0x0030 or 0x003F))
            {
                throw Unsupported($"Section 0x{kind:X4} is outside the standard scalar-return profile.");
            }
        }

        if (tables.Modules.Count != 1 || tables.Methods.Count != 1
            || tables.Signatures.Count != 1 || tables.Bodies.Count != 1
            || tables.Contracts.Count != 0
            || tables.Imports.Count != 0 || tables.TypeDefs.Count != 0
            || tables.Modules.GetRequirements(1) != (1u, 0u)
            || tables.Modules.GetGoverningRuleEdition(1) != 1
            || tables.Modules.GetFeatureRange(1).Count > 1
            || featureCount > 1
            || (featureCount == 1
                && tables.Features.GetFeature(1) != TrRmFeatureRow.TypedFrozenValues))
        {
            throw Unsupported("Standard scalar-return execution requires one feature-free Windows x64 module and one local callable.");
        }

        if (!index.TryGetSection(0x0001, out var moduleBytes, out _)
            || BinaryPrimitives.ReadUInt32LittleEndian(moduleBytes.Slice(16, sizeof(uint))) != 1
            || !index.TryGetSection(0x0016, out var methodBytes, out _)
            || BinaryPrimitives.ReadUInt32LittleEndian(methodBytes) != 0
            || tables.Methods.GetBodyRow(1) != 1)
        {
            throw Unsupported("Standard scalar-return execution requires the local entry method to own the selected Body.");
        }

        var signature = tables.Methods.GetSignature(1);
        if (tables.Signatures.GetReceiverTypeSpec(signature) != 0
            || tables.Signatures.GetParamRange(signature).Count != 0
            || tables.Signatures.GetGenericCount(signature) != 0)
        {
            throw Unsupported("Standard scalar-return execution requires a top-level non-generic int32(void) entry.");
        }

        var resultTypeRef = tables.Signatures.GetResultTypeSpec(signature);
        var resultTypeInfo = tables.TypeSpecs.GetTypeSpecRef(resultTypeRef);
        if (resultTypeInfo.Form != 0x01 || resultTypeInfo.Qualifiers != 0
            || resultTypeInfo.TypeCode != 0x08)
        {
            throw Unsupported("Standard scalar-return execution currently supports only an exact int32 result.");
        }

        var bodyRow = tables.Methods.GetBodyRow(1);
        tables.Bodies.ValidateSelectedImplementation(bodyRow, tables.Contracts.Count);
        tables.ValidateRegisterBodyMapsForMethod(1);
        var body = tables.Bodies.GetRegisterBody(bodyRow, tables.Contracts.Count);
        if (body.MapMask is not (0 or 0x10)
            || body.BlockCount != 1 || body.RecordCount != 1)
        {
            throw Unsupported("Standard scalar-return execution requires one bounded Block, result record and optional ConstantMap.");
        }

        var constantBody = body.MapMask == 0x10;
        if (constantBody
            ? featureCount != 1 || tables.Modules.GetFeatureRange(1) != (1, 1)
            : featureCount != 0 || tables.Modules.GetFeatureRange(1) != (0, 0))
        {
            throw Unsupported("Standard scalar-return execution requires the exact Feature manifest for its selected Body.");
        }

        var entryState = body.GetRecord(0);
        if (entryState[0] != 0x01
            || BinaryPrimitives.ReadUInt32LittleEndian(entryState[4..]) != 0)
        {
            throw Unsupported("Standard scalar-return execution requires an empty BlockEntryState.");
        }

        var code = body.GetBlockCode(0);
        BoundConstant result;
        if (constantBody)
        {
            if (body.ConstantCount != 1 || code.Length != 2
                || code[0] != 0x09 || code[1] != 0)
            {
                throw Unsupported("Standard scalar-return execution supports one exact ConstantMap load and tiny Return R0.");
            }

            var typeSpecRef = BinaryPrimitives.ReadUInt32LittleEndian(body.ConstantMap);
            var constantSourceRow = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                body.ConstantMap[sizeof(uint)..]));
            result = TrRmConstantSourceValueValidator.MaterializeFrozenScalar(
                constantSourceRow, typeSpecRef, 1,
                tables.Modules.GetRequirements(1).TargetRequirement,
                tables.ConstantSources, tables.FrozenValueNodes, tables.Blobs,
                tables.TypeSpecs, tables.Features, tables.Modules);
            if (result.Type != PrimitiveType.Int32)
            {
                throw Unsupported("Standard scalar-return execution currently supports only an int32 ConstantMap result.");
            }
        }
        else
        {
            if (code.Length != 2 || code[0] != 0x17
                || code[1] is not (0x0E or 0x0F))
            {
                throw Unsupported("Standard scalar-return execution supports only Return R14 or Return R15.");
            }

            var value = code[1] == 0x0E ? 0 : -1;
            result = new BoundConstant(value, PrimitiveType.Int32, default);
        }

        var function = new BoundFunction(tables.Methods.GetName(1),
            PrimitiveType.Int32, [], 0,
            new BoundBlock([new BoundReturn(result, default)], default), default)
        {
            IsPrivate = tables.Methods.GetFlags(1) == 0,
            IsInternal = tables.Methods.GetFlags(1) == 1,
        };
        var program = new BoundProgram([function], 0)
        {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
        };
        SemanticVerifier.Verify(program);
        return program;
    }
}
