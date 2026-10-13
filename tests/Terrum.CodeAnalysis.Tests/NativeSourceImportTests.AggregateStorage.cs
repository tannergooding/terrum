// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private const string AggregateStorageImportKey = "test.native.fixture.FillRect";

    private const string AggregateStorageDeclarations = $$"""
        [Layout(Sequential)]
        struct FixtureRect {
            int32 Left;
            int32 Top;
            int32 Right;
            int32 Bottom;
            public FixtureRect(int32 left, int32 top, int32 right, int32 bottom) {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }
        }

        [Terrum.Interop.NativeImport("{{AggregateStorageImportKey}}")]
        [NoThrow]
        extern int32 FillRect(Terrum.Interop.NativeStorageAddress destination);
        """;

    private static NativeImportDefinition CreateAggregateStorageImport(bool isReadonly = false) =>
        new(AggregateStorageImportKey, "native-aggregate-probe.dll", "FillRect",
            [PrimitiveType.NativeStorageAddress], PrimitiveType.Int32,
            IsNoThrow: true, RequiresUnsafeCaller: false, DoesNotReturn: false,
            NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [
                isReadonly
                    ? NativeImportParameterContract.ReadonlyNativeStorageNoEscape
                    : NativeImportParameterContract.WritableNativeStorageNoEscape,
            ],
            AggregateStorageParameters = [
                new(0, PrimitiveType.NativeStorageAddress, 16, 4, IsReadonly: isReadonly),
            ],
        };

    private static Compilation CompileAggregateStorage(string body, bool isReadonly = false,
        int byteExtent = 16, BoundRuntimePolicy runtimePolicy = BoundRuntimePolicy.FailFast) =>
        Compilation.CreateWithNativeImports([
            SyntaxTree.Parse(AggregateStorageDeclarations + "\nint32 Main() {\n" + body + "\n}"),
        ], NativeImportCatalog.CreateForTesting("test-native-aggregate-storage-v1", [
            CreateAggregateStorageImport(isReadonly) with {
                AggregateStorageParameters = [
                    new(0, PrimitiveType.NativeStorageAddress, byteExtent, 4, IsReadonly: isReadonly),
                ],
            },
        ]), runtimePolicy);

    [Fact]
    public void NativeAggregateStorageSupportsUnrelatedWritableAndReadonlyLoans()
    {
        var writable = CompileAggregateStorage("""
            mut FixtureRect rectangle = new FixtureRect(1, 2, 3, 4);
            return FillRect(&rectangle);
            """);
        Assert.False(writable.HasErrors, string.Join(Environment.NewLine, writable.Diagnostics));
        var writableProgram = Assert.IsType<BoundProgram>(writable.Program);
        _ = NativeEmitter.EmitWithNativeImportsForTesting(writableProgram, NativeRuntimePolicy.FailFast);

        var readonlyCompilation = CompileAggregateStorage("""
            FixtureRect rectangle = new FixtureRect(1, 2, 3, 4);
            return FillRect(&rectangle);
            """, isReadonly: true);
        Assert.False(readonlyCompilation.HasErrors,
            string.Join(Environment.NewLine, readonlyCompilation.Diagnostics));
        var readonlyProgram = Assert.IsType<BoundProgram>(readonlyCompilation.Program);
        _ = NativeEmitter.EmitWithNativeImportsForTesting(readonlyProgram, NativeRuntimePolicy.FailFast);
    }

    [Fact]
    public void NativeAggregateStorageSupportsCheckedExceptionsForNoThrowImports()
    {
        var compilation = CompileAggregateStorage("""
            mut FixtureRect rectangle = new FixtureRect(1, 2, 3, 4);
            return FillRect(&rectangle);
            """, runtimePolicy: BoundRuntimePolicy.CheckedExceptions);

        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        _ = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.CheckedExceptions);
    }

    [Fact]
    public void NativeAggregateStorageRejectsReadonlyOrUninitializedWritableLoans()
    {
        var readonlyLocal = CompileAggregateStorage("""
            FixtureRect rectangle = new FixtureRect(1, 2, 3, 4);
            return FillRect(&rectangle);
            """);
        Assert.True(readonlyLocal.HasErrors);

        var uninitializedLocal = CompileAggregateStorage("""
            mut FixtureRect rectangle;
            return FillRect(&rectangle);
            """);
        Assert.True(uninitializedLocal.HasErrors);
    }

    [Fact]
    public void NativeAggregateStorageRequiresTheCataloguedExtent()
    {
        var compilation = CompileAggregateStorage("""
            mut FixtureRect rectangle = new FixtureRect(1, 2, 3, 4);
            return FillRect(&rectangle);
            """, byteExtent: 12);

        Assert.True(compilation.HasErrors);
    }

    [Fact]
    public void NativeAggregateAddressCannotBeStoredOrReturned()
    {
        var compilation = CompileAggregateStorage("""
            mut FixtureRect rectangle = new FixtureRect(1, 2, 3, 4);
            Terrum.Interop.NativeStorageAddress address = &rectangle;
            return 0;
            """);

        Assert.True(compilation.HasErrors);
    }
}
