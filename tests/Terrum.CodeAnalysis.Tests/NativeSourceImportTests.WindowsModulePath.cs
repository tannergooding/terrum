// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    [Test]
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
                unsafe {
                    uint32 length = GetModuleFileNameW(null, &path[0], 260);
                    if (length == 0) {
                        return int32(GetLastError());
                    }
                    return int32(length);
                }
            }
            """;

        var compilation = Compilation.CreateWithNativeImports(SyntaxTree.Parse(source),
            BoundRuntimePolicy.CheckedExceptions);

        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        Assert.That(NativeWindowsModulePathContract.MatchesCatalog(program.NativeImportCatalog), Is.True);
    }
}
