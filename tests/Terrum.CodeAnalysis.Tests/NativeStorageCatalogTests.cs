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
        var compilation = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program ?? throw new AssertionException("Expected a bound program.");
        Assert.That(program.NativeImportCatalog, Is.Null);

        var layout = program.Functions[program.EntryPoint].Variables
            .Single(static variable => variable.Name == "values").NativeFixedArrayLayout;
        Assert.That(layout, Is.Not.Null);
        var arrayLayout = layout ?? throw new AssertionException("Expected the fixed array layout.");
        Assert.That(arrayLayout.ElementStride, Is.EqualTo(24));
        Assert.That(arrayLayout.ByteExtent, Is.EqualTo(48));
        Assert.That(NativeFixedArrayContract.MatchesProgram(program), Is.True);
        Assert.That(NativeEmitter.EmitWithNativeImports(
            program, NativeRuntimePolicy.CheckedExceptions), Is.Not.Empty);
    }

    [Test]
    public void CheckedFixedArrayArithmeticUsesGenericSourceLayoutWithoutWindowsImports()
    {
        var source = """
            int32 Main() {
                mut InlineArray<uint32, 3> values = [4, 5, 6];
                values[1] = values[0] + 1;
                return int32(values[2]);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program ?? throw new AssertionException("Expected a bound program.");

        Assert.That(NativeEmitter.EmitWithNativeImports(program,
            NativeRuntimePolicy.CheckedExceptions), Is.Not.Empty);
    }

    [Test]
    public void TypedNativePointerCanAddressACheckedFixedArrayElement()
    {
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
        var compilation = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.CheckedExceptions);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program ?? throw new AssertionException("Expected a bound program.");
        Assert.That(NativeEmitter.EmitWithNativeImports(program,
            NativeRuntimePolicy.CheckedExceptions), Is.Not.Empty);
    }

    [TestCase("ptr<char16> selected = &values[0];")]
    [TestCase("write ptr<uint32> selected = &values[0];")]
    [TestCase("ptr<uint32> selected = &values[3];")]
    public void TypedNativePointerAddressRejectsMismatchedElementsAndAccess(string addressDeclaration)
    {
        var source = $$"""
            int32 Main() {
                InlineArray<uint32, 3> values = [4, 5, 6];
                unsafe {
                    {{addressDeclaration}}
                    return 0;
                }
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.CheckedExceptions);

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void FixedArraysRejectNonBlittableAndOversizedElements()
    {
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

        Assert.That(Compilation.Create(SyntaxTree.Parse(nonBlittable),
            BoundRuntimePolicy.FailFast).Program, Is.Null);
        Assert.That(Compilation.Create(SyntaxTree.Parse(oversized),
            BoundRuntimePolicy.FailFast).Program, Is.Null);
    }

    [Test]
    public void FixedArrayDynamicIndexEffectDependsOnSelectedRuntimePolicy()
    {
        const string source = """
            [NoThrow]
            int32 Index() { return 0; }

            [NoThrow]
            int32 Main() {
                InlineArray<uint32, 2> values = [4, 5];
                return values[Index()] == 4 ? 0 : 1;
            }
            """;

        var failFast = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.FailFast);
        Assert.That(failFast.HasErrors, Is.False, string.Join(Environment.NewLine, failFast.Diagnostics));
        var checkedExceptions = Compilation.Create(SyntaxTree.Parse(source),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(checkedExceptions.Program, Is.Null);
        Assert.That(checkedExceptions.Diagnostics, Is.Not.Empty);
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
