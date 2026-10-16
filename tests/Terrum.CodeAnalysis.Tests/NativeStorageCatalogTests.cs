// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeStorageCatalogTests
{
    [Test]
    public void CheckedFixedArrayStorageAdmitsUnrelatedBlittableAggregates()
    {
        var catalog = NativeImportCatalog.CreateForTesting("test.generic-checked-fixed-array.v1", []);
        var source = """
            [Blittable] [Layout(Sequential)]
            struct Measurement {
                int64 First;
                int64 Second;
                int64 Third;
                public Measurement(int64 first, int64 second, int64 third) {
                    First = first;
                    Second = second;
                    Third = third;
                }
                public int64 GetThird() { return Third; }
            }

            int32 Main() {
                InlineArray<Measurement, 2> values = [
                    new Measurement(1, 2, 3), new Measurement(4, 5, 6)];
                int32 index = 1;
                return int32(values[index].GetThird());
            }
            """;
        var compilation = Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(source)], catalog, BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program ?? throw new AssertionException("Expected a bound program.");

        var layout = program.Functions[program.EntryPoint].Variables
            .Single(static variable => variable.Name == "values").NativeFixedArrayLayout;
        Assert.That(layout, Is.Not.Null);
        var arrayLayout = layout ?? throw new AssertionException("Expected the fixed array layout.");
        Assert.That(arrayLayout.ElementStride, Is.EqualTo(24));
        Assert.That(arrayLayout.ByteExtent, Is.EqualTo(48));
        Assert.That(NativeFixedArrayContract.MatchesProgram(program), Is.True);
        Assert.That(NativeEmitter.EmitWithNativeImportsForTesting(
            program, NativeRuntimePolicy.CheckedExceptions), Is.Not.Empty);
    }

    [Test]
    public void CheckedFixedArrayArithmeticUsesGenericSourceLayoutWithoutWindowsImports()
    {
        var catalog = NativeImportCatalog.CreateForTesting("test.generic-checked-array-arithmetic.v1", []);
        var source = """
            int32 Main() {
                mut InlineArray<uint32, 3> values = [4, 5, 6];
                values[1] = values[0] + 1;
                return int32(values[2]);
            }
            """;
        var compilation = Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(source)], catalog, BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program ?? throw new AssertionException("Expected a bound program.");

        Assert.That(NativeEmitter.EmitWithNativeImportsForTesting(program,
            NativeRuntimePolicy.CheckedExceptions), Is.Not.Empty);
    }

    [Test]
    public void TypedNativePointerCanAddressACheckedFixedArrayElement()
    {
        var catalog = NativeImportCatalog.CreateForTesting("test.fixed-array-pointer-address.v1", []);
        const string source = """
            int32 Main() {
                mut InlineArray<uint32, 3> values = [4, 5, 6];
                int32 index = 1;
                unsafe {
                    write ptr<uint32> selected = &values[index];
                    *selected = 42;
                    return int32(values[1]);
                }
            }
            """;
        var compilation = Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(source)], catalog, BoundRuntimePolicy.CheckedExceptions);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program ?? throw new AssertionException("Expected a bound program.");
        Assert.That(NativeEmitter.EmitWithNativeImportsForTesting(program,
            NativeRuntimePolicy.CheckedExceptions), Is.Not.Empty);
    }

    [TestCase("ptr<char16> selected = &values[0];")]
    [TestCase("write ptr<uint32> selected = &values[0];")]
    [TestCase("ptr<uint32> selected = &values[3];")]
    public void TypedNativePointerAddressRejectsMismatchedElementsAndAccess(string addressDeclaration)
    {
        var catalog = NativeImportCatalog.CreateForTesting("test.invalid-fixed-array-pointer-address.v1", []);
        var source = $$"""
            int32 Main() {
                InlineArray<uint32, 3> values = [4, 5, 6];
                unsafe {
                    {{addressDeclaration}}
                    return 0;
                }
            }
            """;
        var compilation = Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(source)], catalog, BoundRuntimePolicy.CheckedExceptions);

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void FixedArraysRejectNonBlittableAndOversizedElements()
    {
        var catalog = NativeImportCatalog.CreateForTesting("test.invalid-fixed-array.v1", []);
        const string nonBlittable = """
            [Layout(Sequential)]
            struct Measurement {
                int64 Value;
                public Measurement(int64 value) { Value = value; }
            }
            int32 Main() {
                InlineArray<Measurement, 1> values = [new Measurement(1)];
                return 0;
            }
            """;
        const string oversized = """
            int32 Main() {
                InlineArray<uint64, 513> values = [];
                return 0;
            }
            """;

        Assert.That(Compilation.CreateWithNativeImports([SyntaxTree.Parse(nonBlittable)],
            catalog, BoundRuntimePolicy.FailFast).Program, Is.Null);
        Assert.That(Compilation.CreateWithNativeImports([SyntaxTree.Parse(oversized)],
            catalog, BoundRuntimePolicy.FailFast).Program, Is.Null);
    }

    [Test]
    public void FixedArrayDynamicIndexEffectDependsOnSelectedRuntimePolicy()
    {
        var catalog = NativeImportCatalog.CreateForTesting("test.fixed-array-runtime-policy.v1", []);
        const string source = """
            [NoThrow]
            int32 Index() { return 0; }

            [NoThrow]
            int32 Main() {
                InlineArray<uint32, 2> values = [4, 5];
                return values[Index()] == 4 ? 0 : 1;
            }
            """;

        var failFast = Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)],
            catalog, BoundRuntimePolicy.FailFast);
        Assert.That(failFast.HasErrors, Is.False, string.Join(Environment.NewLine, failFast.Diagnostics));
        var checkedExceptions = Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)],
            catalog, BoundRuntimePolicy.CheckedExceptions);
        Assert.That(checkedExceptions.Program, Is.Null);
        Assert.That(checkedExceptions.Diagnostics, Is.Not.Empty);
    }

    [Test]
    public void CataloguedAggregateLayoutsAdmitNestedStructsBeyondTheValueProfile()
    {
        const string importKey = "test.fixture.read-descriptor";
        var import = new NativeImportDefinition(importKey, "fixture.dll", "ReadDescriptor",
            [PrimitiveType.NativeStorageAddress], PrimitiveType.Int32,
            IsNoThrow: true, RequiresUnsafeCaller: true, DoesNotReturn: false,
            NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [NativeImportParameterContract.ReadonlyNativeStorageNoEscape],
            AggregateStorageParameters = [
                new(0, PrimitiveType.NativeStorageAddress, 40, 8, IsReadonly: true,
                    SourceType: "FixtureDescriptor"),
            ],
        };
        var catalog = NativeImportCatalog.CreateForTesting("test.generic-aggregate-storage.v1", [import],
            aggregateStorageLayouts: [
                new("FixtureHeader", 3 * sizeof(ulong), sizeof(ulong)) {
                    Fields = [
                        new("First", nameof(PrimitiveType.UInt64), 0, sizeof(ulong)),
                        new("Second", nameof(PrimitiveType.UInt64), sizeof(ulong), sizeof(ulong)),
                        new("Third", nameof(PrimitiveType.UInt64), 2 * sizeof(ulong), sizeof(ulong)),
                    ],
                },
                new("FixtureDescriptor", 40, 8) {
                    Fields = [
                        new("Header", "FixtureHeader", 0, 3 * sizeof(ulong)),
                        new("OutputHandle", nameof(PrimitiveType.UInt64),
                            3 * sizeof(ulong), sizeof(ulong)),
                        new("Count", nameof(PrimitiveType.UInt32), 4 * sizeof(ulong), sizeof(uint)),
                        new("Flags", nameof(PrimitiveType.UInt32), 4 * sizeof(ulong) + sizeof(uint),
                            sizeof(uint)),
                    ],
                },
            ]);
        var source = $$"""
            [Terrum.Interop.NativeImport("{{importKey}}")]
            [NoThrow]
            extern int32 ReadDescriptor(Terrum.Interop.NativeStorageAddress descriptor);

            struct FixtureHeader {
                uint64 First;
                uint64 Second;
                uint64 Third;

                public FixtureHeader(uint64 first, uint64 second, uint64 third) {
                    First = first;
                    Second = second;
                    Third = third;
                }
            }

            struct FixtureDescriptor {
                FixtureHeader Header;
                uint64 OutputHandle;
                uint32 Count;
                uint32 Flags;

                public FixtureDescriptor(FixtureHeader header, uint64 outputHandle,
                    uint32 count, uint32 flags) {
                    Header = header;
                    OutputHandle = outputHandle;
                    Count = count;
                    Flags = flags;
                }
            }

            int32 Main() {
                FixtureDescriptor descriptor = new FixtureDescriptor(
                    new FixtureHeader(1, 2, 3), 4, 5, 6);
                return unsafe(ReadDescriptor(&descriptor));
            }
            """;

        var compilation = Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(source)], catalog, BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program ?? throw new AssertionException("Expected a bound program.");
        var main = program.Functions[program.EntryPoint];
        var returnStatement = main.Body.Statements.OfType<BoundReturn>().Single();
        var call = returnStatement.Value switch {
            BoundUnsafeExpression { Expression: BoundCall nativeCall } => nativeCall,
            BoundCall nativeCall => nativeCall,
            _ => throw new AssertionException("Expected the aggregate import call."),
        };
        var address = call.Arguments[0] as BoundNativeStorageAddress
            ?? throw new AssertionException("Expected a catalogued aggregate storage address.");
        Assert.That(address.ByteExtent, Is.EqualTo(40));
        Assert.That(address.IsReadonly, Is.True);
    }

    [Test]
    public void NativeImportCatalogRejectsImportsWithoutCallerUnsafety()
    {
        var safeImport = CreateImport() with { RequiresUnsafeCaller = false };

        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test.native-import.safe-caller.v1", [safeImport]))!;
    }

    private static NativeImportDefinition CreateImport() =>
        new("test.fixture.call", "fixture.dll", "Call", [PrimitiveType.UInt32], PrimitiveType.UInt32,
            IsNoThrow: true, RequiresUnsafeCaller: true, DoesNotReturn: false,
            NativeImportDeploymentPolicy.ApplicationDirectory);
}
