// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    [Fact]
    public void WindowsModulePathImportsUseGenericCompilerContracts()
    {
        const string source = """
            [Terrum.Interop.NativeImport("windows-x64.system32.kernel32.GetModuleFileNameW")]
            [NoThrow]
            extern uint32 GetModuleFileNameW(
                Terrum.Interop.Windows.NativeNullPointer moduleName,
                Terrum.Interop.NativeStorageAddress fileName,
                uint32 size);
            [Terrum.Interop.NativeImport("windows-x64.system32.kernel32.GetLastError")]
            [NoThrow]
            extern uint32 GetLastError();
            int32 Main() {
                mut InlineArray<char16, 260> path = [];
                uint32 length = GetModuleFileNameW(null, &path[0], 260);
                if (length == 0) {
                    return int32(GetLastError());
                }
                return int32(length);
            }
            """;

        var compilation = Compilation.CreateWithNativeImports(SyntaxTree.Parse(source),
            BoundRuntimePolicy.CheckedExceptions);

        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.True(NativeWindowsModulePathContract.MatchesCatalog(program.NativeImportCatalog));
    }
}
