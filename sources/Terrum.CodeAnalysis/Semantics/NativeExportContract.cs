// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeExportContract
{
    internal static bool IsValidWindowsX64SymbolName(string name)
        => !string.IsNullOrEmpty(name)
            && name.All(static character => character is >= '!' and <= '~');
}
