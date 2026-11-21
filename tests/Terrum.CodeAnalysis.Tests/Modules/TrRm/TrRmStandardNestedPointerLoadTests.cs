// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmStandardNestedPointerLoadTests
{
    private const string PointerLoadsSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-pointer-loads-1";
    private const string GeneralPredicateGraphsPointerLoadsSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-pointer-loads-1";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReadNestedPointerFunction(nint pointer);

    [TestCase(PointerLoadsSchemaIdentity)]
    [TestCase(GeneralPredicateGraphsPointerLoadsSchemaIdentity)]
    public async Task NestedPointerCellsPreserveTypedLoadOrderAsync(
        string schemaIdentity)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string moduleIdentity = "standard-nested-pointer-load";
        const string source = """
            public unsafe int32 ReadNested(
                ptr<ptr<ptr<int32>>> outerCell)
            {
                unsafe { return *(*(*outerCell)); }
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var reemittedModuleIdentity = moduleIdentity + "-reemitted";
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            reemittedModuleIdentity, schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            reemittedModuleIdentity, []);
        var image = NativeEmitter.EmitLibrary(loaded, ["ReadNested"],
            NativeRuntimePolicy.FailFast);
        var libraryPath = Path.Combine(Path.GetTempPath(),
            $"terrum-standard-nested-pointer-load-{Guid.NewGuid():N}.dll");
        nint library = 0;
        nint firstBacking = 0;
        nint firstPointerCell = 0;
        nint secondBacking = 0;
        nint secondPointerCell = 0;
        nint outerPointerCell = 0;
        try
        {
            firstBacking = Marshal.AllocHGlobal(sizeof(int));
            firstPointerCell = Marshal.AllocHGlobal(IntPtr.Size);
            secondBacking = Marshal.AllocHGlobal(sizeof(int));
            secondPointerCell = Marshal.AllocHGlobal(IntPtr.Size);
            outerPointerCell = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteInt32(firstBacking, 42);
            Marshal.WriteIntPtr(firstPointerCell, firstBacking);
            Marshal.WriteInt32(secondBacking, 99);
            Marshal.WriteIntPtr(secondPointerCell, secondBacking);
            Marshal.WriteIntPtr(outerPointerCell, firstPointerCell);

            await File.WriteAllBytesAsync(libraryPath, image)
                .ConfigureAwait(false);
            library = NativeLibrary.Load(libraryPath);
            var readNested = Marshal.GetDelegateForFunctionPointer<
                ReadNestedPointerFunction>(NativeLibrary.GetExport(library,
                "ReadNested"));
            Assert.That(readNested(outerPointerCell), Is.EqualTo(42));

            Marshal.WriteIntPtr(outerPointerCell, secondPointerCell);
            Assert.That(readNested(outerPointerCell), Is.EqualTo(99));
        }
        finally
        {
            if (library != 0)
            {
                NativeLibrary.Free(library);
            }
            if (outerPointerCell != 0)
            {
                Marshal.FreeHGlobal(outerPointerCell);
            }
            if (secondPointerCell != 0)
            {
                Marshal.FreeHGlobal(secondPointerCell);
            }
            if (secondBacking != 0)
            {
                Marshal.FreeHGlobal(secondBacking);
            }
            if (firstPointerCell != 0)
            {
                Marshal.FreeHGlobal(firstPointerCell);
            }
            if (firstBacking != 0)
            {
                Marshal.FreeHGlobal(firstBacking);
            }
            await TestFileCleanup.DeleteWithRetryAsync(libraryPath)
                .ConfigureAwait(false);
        }
    }
}
