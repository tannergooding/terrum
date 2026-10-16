// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Terrum.CodeAnalysis.Semantics;

internal static class ValueLayout
{
    internal const int MaximumSize = 16;
    // Address-based storage can exceed the value profile while remaining bounded to sixteen 64-bit fields.
    internal const int MaximumStructureSize = 16 * sizeof(ulong);

    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "Unsupported values have no executable layout.")]
    internal static int Size(PrimitiveType type, IReadOnlyList<BoundStruct> structures) => type switch {
        PrimitiveType.Bool => 1,
        PrimitiveType.Char16 => 2,
        PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Float32 => 4,
        PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Float64 or PrimitiveType.OwnedInt32Buffer => 8,
        _ when ManagedClassTypes.IsManaged(type) => 8,
        _ when NativePointerTypes.IsType(type) => NativeTargetProfile.PointerSize,
        PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView => 16,
        PrimitiveType.TokenSpan => TokenSpanContract.DescriptorSize,
        PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer
                or PrimitiveType.NativeCallbackAddress
                or PrimitiveType.NativeStorageAddress or PrimitiveType.NativeRawPointer
                    => NativeTargetProfile.PointerSize,
                _ when NativeFunctionPointerTypes.IsType(type) => NativeTargetProfile.PointerSize,
            PrimitiveType.NativeStorageRecord => MaximumStructureSize,
        PrimitiveType.Void => 0,
        _ when (int)type >= 100 && (int)type < ManagedClassTypes.First
            && (int)type - 100 < structures.Count => structures[(int)type - 100].Size,
        _ => 0,
    };

    // Callers have established that all nested layouts are acyclic and complete.
    internal static int Alignment(PrimitiveType type, IReadOnlyList<BoundStruct> structures)
        => Alignment(type, structures, null);

    internal static int Alignment(PrimitiveType type, IReadOnlyList<BoundStruct> structures,
        NativeImportCatalog? catalog)
    {
        if (NativePointerTypes.IsType(type) || NativeFunctionPointerTypes.IsType(type))
        {
            return NativeTargetProfile.PointerSize;
        }
        if (ManagedClassTypes.IsManaged(type))
        {
            return 8;
        }
        if ((int)type < 100)
        {
            if (type is PrimitiveType.TokenSpan or PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer
                or PrimitiveType.NativeCallbackAddress
                or PrimitiveType.NativeStorageAddress or PrimitiveType.NativeRawPointer)
            {
                return NativeTargetProfile.PointerSize;
            }
            if (type == PrimitiveType.NativeStorageRecord)
            {
                return NativeTargetProfile.PointerSize;
            }
            return type is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView
                ? 8 : Size(type, structures);
        }
        var structure = structures[(int)type - 100];
        var alignment = 1;
        foreach (var field in structure.Fields)
        {
            alignment = Math.Max(alignment, Alignment(field.Type, structures, catalog));
        }
        return alignment;
    }
}
