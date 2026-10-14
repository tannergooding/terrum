// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmNativeComInterfaceContract(Guid Iid, int BaseTypeDefRow);

internal readonly record struct TrRmNativeComMethodContract(uint VTableSlot, uint Effects);

internal readonly record struct TrRmNativeParameterContract(
    int ParameterOrdinal, byte Access, byte Retention, uint Extent);

internal readonly record struct TrRmNativeComAcquisitionContract(
    int MethodDefRow, int OutputParameterOrdinal, int OwnerTypeDefRow, int SourceMethodDefRow,
    int SourceOutputParameterOrdinal, byte FailureDisposition);
