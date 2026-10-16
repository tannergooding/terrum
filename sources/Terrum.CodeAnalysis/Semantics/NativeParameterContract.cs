// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

namespace Terrum.CodeAnalysis.Semantics;

internal enum NativeParameterAccess : byte
{
    Read,
    Write,
    ReadWrite,
}

internal enum NativeParameterRetention : byte
{
    NoEscape,
    MayEscape,
}

internal sealed record NativeParameterDefinition(int ParameterIndex,
    NativeParameterAccess Access, NativeParameterRetention Retention, int Extent);
