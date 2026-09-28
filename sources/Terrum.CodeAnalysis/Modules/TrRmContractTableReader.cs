// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates Contract row framing, owner ranges and assigned clause shapes.</summary>
/// <remarks>Structural decoding does not prove the promises or make unassigned clause modes executable.</remarks>
public readonly ref struct TrRmContractTableReader
{
    private readonly TrRmVariableTableIndex _rows;

    public TrRmContractTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || recordCount != types.ContractCount || recordCount != methods.ContractCount
            || (recordCount == 0 && !section.IsEmpty))
        {
            throw InvalidContract();
        }

        _rows = recordCount == 0 ? default : new TrRmVariableTableIndex(section, recordCount, 8, 1);
        var owners = new uint[recordCount + 1];
        for (var type = 1; type <= types.Count; type++)
        {
            var (start, count) = types.GetContractRange(type);
            Fill(owners, start, count, 0x1300_0000u | (uint)type);
        }

        for (var method = 1; method <= methods.Count; method++)
        {
            var (start, count) = methods.GetContractRange(method);
            Fill(owners, start, count, 0x1600_0000u | (uint)method);
        }

        var singletons = new HashSet<(uint Owner, ushort Kind)>();
        for (var row = 1; row <= recordCount; row++)
        {
            var data = _rows.GetRow(row);
            var owner = Read(data, 0);
            var kind = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
            if (owners[row] == 0 || owner != owners[row]
                || BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) != 0
                || (kind is 1 or 5 && !singletons.Add((owner, kind))))
            {
                throw InvalidContract();
            }

            switch (kind)
            {
                case 1:
                    if (data.Length != 12 || (Read(data, 8) & ~7u) != 0)
                    {
                        throw InvalidContract();
                    }
                    break;
                case 2:
                    if (data.Length != 16 || data[12] > 1 || data[13] > 1
                        || data[14] > 1 || data[15] != 0)
                    {
                        throw InvalidContract();
                    }
                    break;
                case 3:
                    if (data.Length != 16 || data[14] != 0 || data[15] != 0)
                    {
                        throw InvalidContract();
                    }
                    break;
                case 4:
                    if (data.Length != 20)
                    {
                        throw InvalidContract();
                    }
                    break;
                case 5:
                    if (data.Length != 12 || owner >> 24 != 0x13
                        || Read(data, 8) is 0 or > 0xFF_FFFF
                        || Read(data, 8) > methods.Count
                        || methods.GetOwnerType((int)Read(data, 8)) != (owner & 0xFF_FFFF))
                    {
                        throw InvalidContract();
                    }
                    break;
                default:
                    throw InvalidContract();
            }
        }
    }

    private static void Fill(uint[] owners, int start, int count, uint owner)
    {
        if (count == 0 ? start != 0
            : start <= 0 || count < 0 || start + count > owners.Length)
        {
            throw InvalidContract();
        }

        for (var row = start; row < start + count; row++)
        {
            if (owners[row] != 0)
            {
                throw InvalidContract();
            }

            owners[row] = owner;
        }
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidContract()
        => new("Invalid TRRM Contract table.");
}
