// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Tests.Semantics;

internal static class BoundProgramTestHelpers
{
    internal static BoundProgram MarkNoFailureProof(BoundProgram program, params string[] functionNames)
    {
        var functions = program.Functions;
        for (var index = 0; index < functions.Length; index++)
        {
            if (functionNames.Length == 0
                || functionNames.Contains(functions[index].Name, StringComparer.Ordinal))
            {
                functions = functions.SetItem(index, functions[index] with { NoFailureProof = true });
            }
        }
        return program with { Functions = functions };
    }
}
