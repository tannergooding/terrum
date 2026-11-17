// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmBodyTargetLayoutDependencyValidator
{
    private static void ConsumeWork(TrRmRegisterBodyReader body,
        ulong units = 1)
        => body.ResourceLedger?.ConsumeWork(units);

    internal static void Validate(string? schemaIdentity,
        int targetValueBindingCount, TrRmModuleTableReader modules,
        TrRmMethodDefTableReader methods, TrRmBodyTableReader bodies,
        TrRmContractTableReader contracts, TrRmTypeDefTableReader typeDefs,
        TrRmTypeSpecTableReader typeSpecs, TrRmFieldDefTableReader fields,
        TrRmFieldPathTableReader fieldPaths, TrRmImportTableReader imports,
        TrRmTargetProfileTableReader targetProfiles,
        TrRmConstantExecutionTableReader constantExecutions,
        TrRmTargetLayoutDependencyTableReader targetLayoutDependencies,
        TrRmTargetLayoutDependencyFieldTableReader targetLayoutFields)
    {
        if (schemaIdentity is null
            || !TrRmExecutableCompiler
                .IsSelectedC1ClassInstanceLayoutSchemaIdentity(
                    schemaIdentity))
        {
            return;
        }

        if (targetValueBindingCount != 0)
        {
            throw new NotSupportedException(
                "The selected EN1 class-instance-layout schema does not yet consume TargetValueBinding rows.");
        }

        var nextDependencyRow = 1;
        for (var moduleRow = 1; moduleRow <= modules.Count; moduleRow++)
        {
            var (methodFirst, methodEnd)
                = modules.GetMethodDefRange(moduleRow);
            for (var methodRow = methodFirst; methodRow < methodEnd;
                methodRow++)
            {
                if (methods.GetOwnerModule(methodRow) != moduleRow)
                {
                    throw InvalidDependencies();
                }

                var bodyRow = methods.GetBodyRow(methodRow);
                if (bodyRow == 0)
                {
                    continue;
                }

                var bodyBytes = bodies.GetRow(bodyRow);
                if (bodyBytes.IsEmpty)
                {
                    throw InvalidDependencies();
                }

                if ((bodyBytes[0] & 0x0C) != 0)
                {
                    bodies.ValidateSelectedImplementation(bodyRow,
                        contracts.Count);
                    continue;
                }

                var body = bodies.GetRegisterBody(bodyRow,
                    contracts.Count);
                for (var block = 0; block < body.BlockCount; block++)
                {
                    var code = body.GetBlockCode(block);
                    for (var position = 0; position < code.Length;)
                    {
                        ConsumeWork(body);
                        var opcode = code[position];
                        var instructionLength
                            = TrRmRegisterBodyReader.InstructionLength(
                                opcode, body.Schema);
                        if (instructionLength <= 0
                            || position > code.Length - instructionLength)
                        {
                            throw InvalidDependencies();
                        }

                        if (opcode == 0xDE)
                        {
                            var control = code[position + 1];
                            var queryKind = (byte)(control >> 4);
                            if (queryKind is >= 1 and <= 4)
                            {
                                var mapIndex
                                    = BinaryPrimitives
                                        .ReadUInt16LittleEndian(
                                            code[(position + 2)..]);
                                ValidateOccurrence(queryKind, mapIndex,
                                    body, modules, moduleRow, typeDefs, typeSpecs,
                                    fields, fieldPaths, imports,
                                    targetProfiles, constantExecutions,
                                    targetLayoutDependencies,
                                    targetLayoutFields,
                                    ref nextDependencyRow);
                            }
                        }

                        position += instructionLength;
                    }
                }
            }
        }

        while (nextDependencyRow <= targetLayoutDependencies.Count
            && constantExecutions.OwnsTargetLayoutDependency(
                nextDependencyRow))
        {
            nextDependencyRow++;
        }

        if (nextDependencyRow <= targetLayoutDependencies.Count)
        {
            throw InvalidDependencies();
        }
    }

    private static void ValidateOccurrence(byte queryKind, ushort mapIndex,
        TrRmRegisterBodyReader body, TrRmModuleTableReader modules,
        int ownerModuleRow,
        TrRmTypeDefTableReader typeDefs, TrRmTypeSpecTableReader typeSpecs,
        TrRmFieldDefTableReader fields, TrRmFieldPathTableReader fieldPaths,
        TrRmImportTableReader imports,
        TrRmTargetProfileTableReader targetProfiles,
        TrRmConstantExecutionTableReader constantExecutions,
        TrRmTargetLayoutDependencyTableReader targetLayoutDependencies,
        TrRmTargetLayoutDependencyFieldTableReader targetLayoutFields,
        ref int nextDependencyRow)
    {
        while (nextDependencyRow <= targetLayoutDependencies.Count
            && constantExecutions.OwnsTargetLayoutDependency(
                nextDependencyRow))
        {
            nextDependencyRow++;
        }

        if (nextDependencyRow > targetLayoutDependencies.Count
            || modules.GetRequirements(ownerModuleRow) != (1u, 0u))
        {
            throw InvalidDependencies();
        }

        var dependencyRow = nextDependencyRow++;
        if (targetLayoutDependencies.GetQueryKind(dependencyRow) != queryKind
            || targetProfiles.GetProfile(
                targetLayoutDependencies.GetTargetProfileRow(dependencyRow))
                != new TrRmTargetProfileRow(1, 1))
        {
            throw InvalidDependencies();
        }

        uint subjectTypeSpecRef;
        var fieldMapToken = 0u;
        if (queryKind is 1 or 2)
        {
            if (mapIndex >= body.TypeCount)
            {
                throw InvalidDependencies();
            }

            var typeMapToken = BinaryPrimitives.ReadUInt32LittleEndian(
                body.TypeMap[(mapIndex * sizeof(uint))..]);
            subjectTypeSpecRef
                = NormalizeTypeSpecReference(typeMapToken, typeSpecs);
        }
        else
        {
            if (mapIndex >= body.FieldCount)
            {
                throw InvalidDependencies();
            }

            fieldMapToken = BinaryPrimitives.ReadUInt32LittleEndian(
                body.FieldMap[(mapIndex * sizeof(uint))..]);
            var tokenKind = (byte)(fieldMapToken >> 24);
            var tokenRow = checked((int)(fieldMapToken & 0x00FF_FFFF));
            if (tokenKind == 0x1D)
            {
                if (tokenRow <= 0 || tokenRow > fieldPaths.Count)
                {
                    throw InvalidDependencies();
                }

                subjectTypeSpecRef
                    = fieldPaths.GetRootTypeSpecRef(tokenRow);
            }
            else if (tokenKind is 0x14 or 0x1C)
            {
                var fieldRow = ResolveFieldRow(tokenKind, tokenRow,
                    fields, imports);
                var subject = targetLayoutDependencies
                    .GetSubjectTypeSpecRef(dependencyRow);
                var subjectType = typeSpecs.GetTypeSpecRef(subject);
                var ownerTypeDef = fields.GetOwnerTypeDef(fieldRow);
                if (subjectType.Qualifiers != 0
                    || subjectType.Form is not (0x02 or 0x03)
                    || typeDefs.GetGenericRange(
                        typeSpecs.GetNominalTypeDefRow(subject,
                            typeDefs, imports)).Count != 0
                    || typeSpecs.GetNominalTypeDefRow(subject,
                        typeDefs, imports) != ownerTypeDef)
                {
                    throw InvalidDependencies();
                }

                subjectTypeSpecRef = subject;
            }
            else
            {
                throw InvalidDependencies();
            }
        }

        var dependencySubject
            = targetLayoutDependencies.GetSubjectTypeSpecRef(dependencyRow);
        if (!typeSpecs.AreEquivalentTypeSpecRefs(dependencySubject,
                subjectTypeSpecRef))
        {
            throw InvalidDependencies();
        }

        if (queryKind is 1 or 2)
        {
            if (targetLayoutDependencies.GetFieldRange(dependencyRow).Count
                != 0)
            {
                throw InvalidDependencies();
            }

            return;
        }

        var (fieldFirst, fieldCount)
            = targetLayoutDependencies.GetFieldRange(dependencyRow);
        if (fieldCount == 0 || (queryKind == 4 && fieldCount != 1))
        {
            throw InvalidDependencies();
        }

        var fieldMapKind = (byte)(fieldMapToken >> 24);
        if (fieldMapKind == 0x1D)
        {
            var pathRow = checked((int)(fieldMapToken
                & 0x00FF_FFFF));
            if (fieldPaths.GetFieldCount(pathRow) != fieldCount)
            {
                throw InvalidDependencies();
            }

            for (var ordinal = 0; ordinal < fieldCount; ordinal++)
            {
                ConsumeWork(body);
                if (fieldPaths.GetFieldReference(pathRow, ordinal)
                    != targetLayoutFields.GetFieldReference(
                        fieldFirst + ordinal))
                {
                    throw InvalidDependencies();
                }
            }

            return;
        }

        if (fieldCount != 1
            || fieldMapToken != targetLayoutFields.GetFieldReference(
                fieldFirst))
        {
            throw InvalidDependencies();
        }
    }

    private static uint NormalizeTypeSpecReference(uint token,
        TrRmTypeSpecTableReader typeSpecs)
    {
        var kind = (byte)(token >> 24);
        uint typeSpecRef;
        if (kind is 0xF2 or 0xF3)
        {
            typeSpecRef = token;
        }
        else if (token is > 0 and <= 0x00FF_FFFF)
        {
            typeSpecRef = token;
        }
        else if (kind == 0x11 && (token & 0x00FF_FFFF) != 0)
        {
            typeSpecRef = token & 0x00FF_FFFF;
        }
        else
        {
            throw InvalidDependencies();
        }

        _ = typeSpecs.GetTypeSpecRef(typeSpecRef);
        return typeSpecRef;
    }

    private static int ResolveFieldRow(byte tokenKind, int tokenRow,
        TrRmFieldDefTableReader fields, TrRmImportTableReader imports)
    {
        if (tokenKind == 0x14)
        {
            if (tokenRow <= 0 || tokenRow > fields.Count)
            {
                throw InvalidDependencies();
            }

            return tokenRow;
        }

        if (tokenRow <= 0 || tokenRow > imports.Count)
        {
            throw InvalidDependencies();
        }

        var expectedDefinition
            = imports.GetExpectedDefinitionForImportRow(tokenRow);
        if (expectedDefinition >> 24 != 0x14)
        {
            throw InvalidDependencies();
        }

        var fieldRow = checked((int)(expectedDefinition
            & 0x00FF_FFFF));
        if (fieldRow <= 0 || fieldRow > fields.Count)
        {
            throw InvalidDependencies();
        }

        return fieldRow;
    }

    private static InvalidDataException InvalidDependencies()
        => new("Invalid TRRM Body TargetLayoutDependency ownership.");
}
