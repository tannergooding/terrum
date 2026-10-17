// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Text;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private static readonly string SampleArrayCreateBufferDeclarations =
        SampleArrayBufferDeclarations.Replace(SampleArrayBufferDescriptorDeclaration, "", StringComparison.Ordinal);

    private static NativeImportCatalog CreateSampleArrayCreateBufferFixtureCatalog() =>
        NativeImportCatalog.CreateForTesting(NativeDx11SampleArrayCreateBufferFixtureContract.CatalogIdentity,
            NativeDx11CreateBufferFixtureContract.Imports,
            fixedArrayStorageLayouts: NativeDx11BufferContract.SampleArrayStorageLayouts,
            storageRecordLayouts: NativeDx11BufferContract.SampleArrayStorageRecordLayouts);

    private static Compilation CompileSampleArrayCreateBufferFixture(string body, string? declarations = null,
        NativeImportCatalog? catalog = null, BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast) =>
        Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse((declarations ?? SampleArrayCreateBufferDeclarations)
                + "\nint32 Main() {\n" + body + "\n}")],
            catalog ?? CreateSampleArrayCreateBufferFixtureCatalog(), policy);

    [TestCase(0, 1)]
    [TestCase(0, 35)]
    [TestCase(0, 36)]
    [TestCase(1, 1)]
    [TestCase(1, 35)]
    [TestCase(1, 36)]
    [TestCase(2, 1)]
    [TestCase(2, 35)]
    [TestCase(2, 36)]
    public void SampleArrayCreateBufferFixturePreservesExactArrayLoansAndPrivateAbi(int mode, int byteWidth)
    {
        var compilation = CompileSampleArrayCreateBufferFixture(SampleArrayBufferInitializers(byteWidth)
            + $"return createbufferfixture({mode}, &description, &initialData);");
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue0);
        var __nunitAssertValue1 = program.NativeImportCatalog;
        Assert.That(__nunitAssertValue1, Is.TypeOf<NativeImportCatalog>());
        var catalog = ((NativeImportCatalog)__nunitAssertValue1);
        Assert.That(NativeDx11SampleArrayCreateBufferFixtureContract.MatchesCatalog(catalog), Is.True);
        Assert.That(NativeDx11CreateBufferFixtureContract.MatchesCatalog(catalog), Is.False);
        Assert.That(NativeDx11BufferContract.MatchesSampleArrayCatalog(catalog), Is.False);
        Assert.That(catalog.SupportsDx11SampleArrayCreateBufferFixture, Is.True);
        Assert.That(catalog.SupportsDx11BufferInitialization, Is.False);
        Assert.That(NativeFixedArrayContract.MatchesProgram(program), Is.True);
        Assert.That(catalog.SupportsFixedArrayStorage(PrimitiveType.NativeFixedArray), Is.False);
        Assert.That(catalog.SupportsDx11SampleArrayBufferInitialization, Is.False);
        Assert.That(catalog.SupportsDx11BufferOwnerFixture, Is.False);
        Assert.That(catalog.SupportsDx11InputLayoutOwnerFixture, Is.False);
        Assert.That(catalog.ImportCount, Is.EqualTo(2));
        Assert.That(catalog.TryGetImport(Vertex3ImportKey, out _), Is.False);
        Assert.That(catalog.TryGetImport(NativeDx11BufferContract.ImportKey, out _), Is.False);
        Assert.That(catalog.TryGetImport(NativeDx11BufferContract.SampleArrayImportKey, out _), Is.False);
        Assert.That(program.Functions, Has.None.Matches(static function => function.IsExternal));

        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue2 = main.Body.Statements[0];
        Assert.That(__nunitAssertValue2, Is.TypeOf<BoundLocal>());
        var vertices = ((BoundLocal)__nunitAssertValue2);
        var __nunitAssertValue3 = vertices.Initializer;
        Assert.That(__nunitAssertValue3, Is.TypeOf<BoundNativeFixedArrayInitialization>());
        var array = ((BoundNativeFixedArrayInitialization)__nunitAssertValue3);
        Assert.That(array.Type, Is.EqualTo(PrimitiveType.NativeFixedArray));
        Assert.That(NativeFixedArrayContract.MatchesLayout(array.Layout, program.Structures, catalog), Is.True);
        var __nunitAssertValue4 = main.Body.Statements[1];
        Assert.That(__nunitAssertValue4, Is.TypeOf<BoundLocal>());
        var description = ((BoundLocal)__nunitAssertValue4);
        var __nunitAssertValue5 = main.Body.Statements[2];
        Assert.That(__nunitAssertValue5, Is.TypeOf<BoundLocal>());
        var initialData = ((BoundLocal)__nunitAssertValue5);
        var __nunitAssertValue6 = description.Initializer;
        Assert.That(__nunitAssertValue6, Is.TypeOf<BoundNativeStorageRecordInitialization>());
        var buffer = ((BoundNativeStorageRecordInitialization)__nunitAssertValue6);
        var __nunitAssertValue7 = initialData.Initializer;
        Assert.That(__nunitAssertValue7, Is.TypeOf<BoundNativeStorageRecordInitialization>());
        var subresource = ((BoundNativeStorageRecordInitialization)__nunitAssertValue7);
        Assert.That(buffer.DataSource, Is.TypeOf<BoundNativeStorageAddress>());
        Assert.That(subresource.DataSource, Is.TypeOf<BoundNativeStorageAddress>());
        var loan = new NativeStorageLoan(vertices.Variable, PrimitiveType.NativeFixedArray,
            array.Layout.ElementType, true, 36);
        Assert.That(buffer.Loan, Is.EqualTo(loan));
        Assert.That(subresource.Loan, Is.EqualTo(loan));
        Assert.That(main.Variables[description.Variable].NativeStorageRecordFacts?.BackingExtent, Is.EqualTo((uint)byteWidth));
        var __nunitAssertValue8 = main.Body.Statements[3];
        Assert.That(__nunitAssertValue8, Is.TypeOf<BoundReturn>());
        var __nunitAssertValue9 = (((BoundReturn)__nunitAssertValue8)).Value;
        Assert.That(__nunitAssertValue9, Is.TypeOf<BoundNativeDx11CreateBufferFixture>());
        var gate = ((BoundNativeDx11CreateBufferFixture)__nunitAssertValue9);
        Assert.That(gate.CatalogIdentity, Is.EqualTo(catalog.Identity));
        Assert.That(gate.Description.CarrierType, Is.EqualTo(PrimitiveType.NativeStorageRecord));
        Assert.That(gate.Description.ElementType, Is.EqualTo(PrimitiveType.NativeStorageRecord));
        Assert.That(gate.Description.ByteExtent, Is.EqualTo(NativeDx11BufferContract.SampleArrayStorageRecordLayouts[0].ByteExtent));
        Assert.That(gate.InitialData.CarrierType, Is.EqualTo(PrimitiveType.NativeStorageRecord));
        Assert.That(gate.InitialData.ElementType, Is.EqualTo(PrimitiveType.NativeStorageRecord));
        Assert.That(gate.InitialData.ByteExtent, Is.EqualTo(NativeDx11BufferContract.SampleArrayStorageRecordLayouts[1].ByteExtent));
        Assert.That(main.Variables[gate.Description.Variable].NativeStorageRecordFacts?.Loan, Is.EqualTo(loan));
        Assert.That(main.Variables[gate.InitialData.Variable].NativeStorageRecordFacts?.Loan, Is.EqualTo(loan));
        Assert.That(NativeDx11CreateBufferFixtureContract.MatchesGate(gate, program), Is.True);
        Assert.That(gate.CreateBuffer, Is.EqualTo(NativeDx11CreateBufferFixtureContract.CreateBuffer));
        Assert.That(gate.Release, Is.EqualTo(NativeDx11CreateBufferFixtureContract.Release));

        var image = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
        foreach (var definition in NativeDx11CreateBufferFixtureContract.Imports)
        {
            Assert.That(image.AsSpan().IndexOf(Encoding.ASCII.GetBytes(definition.ExportName + "\0")) >= 0, Is.True);
        }
        foreach (var excluded in new[] { "NativeFfiBufferInitialization", "NativeFfiVertex3",
            "D3D11CreateDevice", "D3DCompileFromFile", "QueryInterface", "AddRef" })
        {
            Assert.That(image.AsSpan().IndexOf(Encoding.ASCII.GetBytes(excluded + "\0")) < 0, Is.True);
        }
        Assert.That(CountCreateBufferSequence(image, [0xFF, 0x50, 0x18]), Is.EqualTo(1));
        Assert.That(CountCreateBufferSequence(image, [0xFF, 0x50, 0x10]), Is.EqualTo(1));
    }

    [TestCase("&vertices[0]", "&vertices[-1]")]
    [TestCase("&vertices[0]", "&vertices[1]")]
    [TestCase("&vertices[0]", "&vertices[3]")]
    [TestCase("&vertices[0]", "&vertices[VertexIndex(0)]")]
    [TestCase("&vertices[0]", "(ptr<void>)&vertices[0]")]
    [TestCase("&vertices[0]", "&vertices[0] + 1")]
    [TestCase("&vertices[0]", "true ? &vertices[0] : &vertices[0]")]
    [TestCase(", 36, 1, 1, 0, 0, 0", ", 0, 1, 1, 0, 0, 0")]
    [TestCase(", 36, 1, 1, 0, 0, 0", ", 37, 1, 1, 0, 0, 0")]
    [TestCase(", 36, 1, 1, 0, 0, 0", ", 4294967295, 1, 1, 0, 0, 0")]
    [TestCase(", 36, 1, 1, 0, 0, 0", ", VertexIndex(36), 1, 1, 0, 0, 0")]
    [TestCase(", 36, 1, 1, 0, 0, 0", ", 36, 0, 1, 0, 0, 0")]
    [TestCase(", 36, 1, 1, 0, 0, 0", ", 36, 1, 2, 0, 0, 0")]
    [TestCase(", 36, 1, 1, 0, 0, 0", ", 36, 1, 1, 1, 0, 0")]
    [TestCase("subresourcedata(&vertices[0], 0, 0)", "subresourcedata(&vertices[0], 1, 0)")]
    [TestCase("subresourcedata(&vertices[0], 0, 0)", "subresourcedata(&vertices[0], 0, 1)")]
    [TestCase("InlineArray<Vertex, 3>", "mut InlineArray<Vertex, 3>")]
    [TestCase("InlineArray<Vertex, 3>", "InlineArray<Vertex, 4>")]
    public void SampleArrayCreateBufferFixtureRejectsInvalidDescriptorSources(string original, string replacement)
    {
        var initializers = SampleArrayBufferInitializers();
        var invalid = initializers.Replace(original, replacement, StringComparison.Ordinal);
        Assert.That(invalid, Is.Not.EqualTo(initializers));
        var compilation = CompileSampleArrayCreateBufferFixture(invalid
            + "return createbufferfixture(0, &description, &initialData);");
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("Terrum.Interop.Dx11.ReadonlyVertex3 address = &vertices[0]; return 0;")]
    [TestCase("Terrum.Interop.Dx11.ReadonlyBufferDesc address = &description; return 0;")]
    [TestCase("Terrum.Interop.Dx11.BufferDesc copy = description; return 0;")]
    [TestCase("Terrum.Interop.Dx11.SubresourceData copy = initialData; return 0;")]
    [TestCase("InlineArray<Vertex, 3> copy = vertices; return 0;")]
    [TestCase("vertices[0] = new Vertex(1, 1, 1); return 0;")]
    [TestCase("description.ByteWidth = 1; return 0;")]
    [TestCase("return &vertices[0];")]
    [TestCase("return createbufferfixture(0, &vertices[0], &initialData);")]
    [TestCase("return createbufferfixture(0, &description, &vertices[0]);")]
    [TestCase("return createbufferfixture(0, &initialData, &description);")]
    [TestCase("return createbufferfixture(0 + 0, &description, &initialData);")]
    [TestCase("return createbufferfixture(3, &description, &initialData);")]
    [TestCase("return NativeFfiBufferInitialization(&description, 0, 0, 0, 0, &initialData, 0);")]
    [TestCase("return trycreatebufferownerfixture(0, &description, &initialData, out var buffer) ? 0 : 1;")]
    [TestCase("Terrum.Interop.Dx11.Vertex3 other = vertex3(new Vertex(0, 0, 0), new Vertex(0, 0, 0), new Vertex(0, 0, 0)); return 0;")]
    [TestCase("InlineArray<float32, 4> clear = [0, 0, 0, 0]; return 0;")]
    [TestCase("InlineArray<D3D_FEATURE_LEVEL, 1> levels = [D3D_FEATURE_LEVEL.LEVEL_11_0]; return 0;")]
    [TestCase("vertices!; return createbufferfixture(0, &description, &initialData);")]
    [TestCase("description!; return createbufferfixture(0, &description, &initialData);")]
    [TestCase("initialData!; return createbufferfixture(0, &description, &initialData);")]
    public void SampleArrayCreateBufferFixtureRejectsEscapeOtherConsumersAndExpiredSource(string body)
    {
        var compilation = CompileSampleArrayCreateBufferFixture(SampleArrayBufferInitializers() + body);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("[Layout(Sequential)]", "")]
    [TestCase("namespace Triangle;", "namespace Other;")]
    [TestCase("public float32 Z;", "public float64 Z;")]
    [TestCase("Z = z;", "")]
    public void SampleArrayCreateBufferFixtureRejectsInvalidPublicTriangleLayout(string original, string replacement)
    {
        var declarations = SampleArrayCreateBufferDeclarations.Replace(original, replacement,
            StringComparison.Ordinal);
        Assert.That(declarations, Is.Not.EqualTo(SampleArrayCreateBufferDeclarations));
        var compilation = CompileSampleArrayCreateBufferFixture(SampleArrayBufferInitializers()
            + "return createbufferfixture(0, &description, &initialData);", declarations);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("public struct Vertex", "struct Vertex")]
    [TestCase("public float32 X;", "float32 X;")]
    [TestCase("public float32 X;", "public mut float32 X;")]
    public void SampleArrayCreateBufferFixtureAcceptsCataloguedVertexDeclarations(string original,
        string replacement)
    {
        var declarations = SampleArrayCreateBufferDeclarations.Replace(original, replacement,
            StringComparison.Ordinal);
        var compilation = CompileSampleArrayCreateBufferFixture(SampleArrayBufferInitializers()
            + "return createbufferfixture(0, &description, &initialData);", declarations);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        _ = Assert.That(compilation.Program, Is.TypeOf<BoundProgram>());
    }

    [Test]
    public void SampleArrayCreateBufferFixtureAcceptsCataloguedVertexFieldRename()
    {
        var declarations = SampleArrayCreateBufferDeclarations
            .Replace("public float32 Z;", "public float32 W;", StringComparison.Ordinal)
            .Replace("Z = z;", "W = z;", StringComparison.Ordinal);
        var compilation = CompileSampleArrayCreateBufferFixture(SampleArrayBufferInitializers()
            + "return createbufferfixture(0, &description, &initialData);", declarations);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        _ = Assert.That(compilation.Program, Is.TypeOf<BoundProgram>());
    }

    [Test]
    public void SampleArrayCreateBufferFixtureRejectsDistinctDescriptorBacking()
    {
        var initializers = SampleArrayBufferInitializers();
        var array = initializers[..initializers.IndexOf("Terrum.Interop.Dx11.BufferDesc", StringComparison.Ordinal)];
        var invalid = initializers.Replace("subresourcedata(&vertices[0]", "subresourcedata(&other[0]",
            StringComparison.Ordinal);
        var compilation = CompileSampleArrayCreateBufferFixture(array.Replace("vertices", "other",
            StringComparison.Ordinal) + invalid + "return createbufferfixture(0, &description, &initialData);");
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [Test]
    public void SampleArrayCreateBufferFixturePreservesByValueReadsAndFoldedZeroStarts()
    {
        var initializers = SampleArrayBufferInitializers().Replace("&vertices[0]", "&vertices[1 - 1]",
            StringComparison.Ordinal);
        var compilation = CompileSampleArrayCreateBufferFixture(initializers
            + "return vertices[VertexIndex(0)].X == 0 ? createbufferfixture(0, &description, &initialData) : 1;");
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue10 = compilation.Program;
        Assert.That(__nunitAssertValue10, Is.TypeOf<BoundProgram>());
        _ = NativeEmitter.EmitWithNativeImportsForTesting(
                    ((BoundProgram)__nunitAssertValue10), NativeRuntimePolicy.FailFast);
    }

    [Test]
    public void SampleArrayCreateBufferFixtureCatalogHasOnlyTwoExactPrivateImports()
    {
        var identity = NativeDx11SampleArrayCreateBufferFixtureContract.CatalogIdentity;
        var imports = NativeDx11CreateBufferFixtureContract.Imports;
        foreach (var invalid in new[] {
            ImmutableArray<NativeImportDefinition>.Empty,
            imports.RemoveAt(0),
            imports.RemoveAt(1),
            imports.Add(CreateVertex3ImportDefinition()),
            imports.Add(CreateSampleArrayBufferDescriptorImportDefinition()),
            imports.Add(CreateBufferDescriptorImportDefinition()),
            imports.Add(new NativeImportDefinition("test.extra", "extra.dll", "Extra",
                [], PrimitiveType.Int32, true, true, false, NativeImportDeploymentPolicy.ApplicationDirectory)),
        })
        {
            _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(identity, invalid));
        }
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting("test.other", imports));
        _ = Assert.Throws<ArgumentException>(() => new NativeImportCatalog(identity, imports));
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(identity, imports));
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(identity, imports,
            fixedArrayStorageLayouts: [
                NativeDx11BufferContract.SampleArrayStorageLayouts[0] with { ElementStride = 16, ByteExtent = 48 },
            ]));
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            NativeDx11CreateBufferFixtureContract.CatalogIdentity, imports));
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            NativeDx11BufferContract.SampleArrayCatalogIdentity, imports));

        foreach (var definition in imports)
        {
            foreach (var invalid in new[] {
                definition with { Key = "test.other" },
                definition with { DllName = "other.dll" },
                definition with { DllName = definition.DllName.ToUpperInvariant() },
                definition with { ExportName = "Other" },
                definition with { ReturnType = PrimitiveType.Int64 },
                definition with { ParameterTypes = default },
                definition with { ParameterTypes = [PrimitiveType.UInt64] },
                definition with { ParameterContracts = default },
                definition with { ParameterContracts = definition.ParameterContracts.SetItem(0,
                    NativeImportParameterContract.CompilerInternalDx11CreateBufferFixture) },
                definition with { ResultContract = NativeImportResultContract.Value },
                definition with { IsNoThrow = false },
                definition with { RequiresUnsafeCaller = true },
                definition with { DoesNotReturn = true },
                definition with { DeploymentPolicy = NativeImportDeploymentPolicy.System32 },
            })
            {
                _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(identity,
                    imports.Replace(definition, invalid)));
            }
            var parameters = definition.Key == NativeDx11CreateBufferFixtureContract.AcquireKey
                ? "uint32 mode"
                : "uint32 mode, uint64 device, uint64 description, uint64 initialData, int32 result, uint64 output";
            var result = definition.ReturnType == PrimitiveType.UInt64 ? "uint64" : "int32";
            var declaration = $$"""
                [Terrum.Interop.NativeImport("{{definition.Key}}")]
                [NoThrow] extern {{result}} PrivateHelper({{parameters}});
                """;
            Assert.That(CompileSampleArrayCreateBufferFixture("return 0;",
                SampleArrayCreateBufferDeclarations + declaration).Program, Is.Null);
        }
    }

    [TestCase("carrier")]
    [TestCase("element")]
    [TestCase("backing")]
    [TestCase("readonly")]
    [TestCase("extent")]
    [TestCase("negative-start")]
    [TestCase("nonzero-start")]
    [TestCase("dynamic-start")]
    [TestCase("start-type")]
    [TestCase("source-address-type")]
    [TestCase("array-elements")]
    [TestCase("array-metadata")]
    [TestCase("array-mutable")]
    [TestCase("array-out")]
    [TestCase("array-init")]
    [TestCase("array-initialization-local")]
    [TestCase("buffer-width")]
    [TestCase("subresource-pitch")]
    [TestCase("descriptor-facts")]
    [TestCase("gate-loan")]
    [TestCase("gate-mode")]
    [TestCase("gate-identity")]
    [TestCase("createbuffer-slot")]
    [TestCase("createbuffer-abi")]
    [TestCase("release-effect")]
    [TestCase("helper-key")]
    [TestCase("array-lifetime")]
    [TestCase("descriptor-lifetime")]
    [TestCase("array-scope")]
    [TestCase("descriptor-scope")]
    [TestCase("address-escape")]
    [TestCase("catalog-identity")]
    [TestCase("catalog-removed")]
    [TestCase("checked-policy")]
    public void SampleArrayCreateBufferFixtureMandatoryVerificationRejectsForgedFacts(string variant)
    {
        var compilation = CompileSampleArrayCreateBufferFixture(SampleArrayBufferInitializers()
            + "return createbufferfixture(0, &description, &initialData);");
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue11 = compilation.Program;
        Assert.That(__nunitAssertValue11, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue11);
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue12 = main.Body.Statements[0];
        Assert.That(__nunitAssertValue12, Is.TypeOf<BoundLocal>());
        var arrayLocal = ((BoundLocal)__nunitAssertValue12);
        var __nunitAssertValue13 = arrayLocal.Initializer;
        Assert.That(__nunitAssertValue13, Is.TypeOf<BoundNativeFixedArrayInitialization>());
        var array = ((BoundNativeFixedArrayInitialization)__nunitAssertValue13);
        var __nunitAssertValue14 = main.Body.Statements[1];
        Assert.That(__nunitAssertValue14, Is.TypeOf<BoundLocal>());
        var bufferLocal = ((BoundLocal)__nunitAssertValue14);
        var __nunitAssertValue15 = main.Body.Statements[2];
        Assert.That(__nunitAssertValue15, Is.TypeOf<BoundLocal>());
        var dataLocal = ((BoundLocal)__nunitAssertValue15);
        var __nunitAssertValue16 = bufferLocal.Initializer;
        Assert.That(__nunitAssertValue16, Is.TypeOf<BoundNativeStorageRecordInitialization>());
        var buffer = ((BoundNativeStorageRecordInitialization)__nunitAssertValue16);
        var __nunitAssertValue17 = dataLocal.Initializer;
        Assert.That(__nunitAssertValue17, Is.TypeOf<BoundNativeStorageRecordInitialization>());
        var data = ((BoundNativeStorageRecordInitialization)__nunitAssertValue17);
        var __nunitAssertValue18 = data.DataSource;
        Assert.That(__nunitAssertValue18, Is.TypeOf<BoundNativeStorageAddress>());
        var source = ((BoundNativeStorageAddress)__nunitAssertValue18);
        var __nunitAssertValue19 = data.Loan;
        Assert.That(__nunitAssertValue19, Is.TypeOf<NativeStorageLoan>());
        var loan = ((NativeStorageLoan)__nunitAssertValue19);
        var __nunitAssertValue20 = main.Body.Statements[3];
        Assert.That(__nunitAssertValue20, Is.TypeOf<BoundReturn>());
        var returned = ((BoundReturn)__nunitAssertValue20);
        var __nunitAssertValue21 = returned.Value;
        Assert.That(__nunitAssertValue21, Is.TypeOf<BoundNativeDx11CreateBufferFixture>());
        var gate = ((BoundNativeDx11CreateBufferFixture)__nunitAssertValue21);
        var arrayVariable = main.Variables[arrayLocal.Variable];

        loan = variant switch {
            "carrier" => loan with { CarrierType = PrimitiveType.NativeDx11BufferDesc },
            "element" => loan with { ElementType = PrimitiveType.Float32 },
            "backing" => loan with { BackingVariable = dataLocal.Variable },
            "readonly" => loan with { IsReadonly = false },
            "extent" => loan with { ByteExtent = 35 },
            _ => loan,
        };
        source = variant switch {
            "element" => source with { ElementType = loan.ElementType },
            "backing" => source with { Variable = loan.BackingVariable },
            "negative-start" => source with { Index = new BoundConstant(-1, PrimitiveType.Int32, source.Span) },
            "nonzero-start" => source with { Index = new BoundConstant(1, PrimitiveType.Int32, source.Span) },
            "dynamic-start" => source with { Index = new BoundRead(arrayLocal.Variable, PrimitiveType.Int32, source.Span) },
            "start-type" => source with { Index = new BoundConstant(0, PrimitiveType.UInt32, source.Span) },
            "source-address-type" => source with { Type = PrimitiveType.NativeDx11BufferDescAddress },
            _ => source,
        };
        buffer = buffer with { Loan = loan, DataSource = source };
        data = data with { Loan = loan, DataSource = source };
        var arrayLayout = arrayVariable.NativeFixedArrayLayout
            ?? throw new InvalidOperationException("The verified array local must carry its layout.");
        arrayVariable = variant switch {
            "array-metadata" => arrayVariable with {
                NativeFixedArrayLayout = arrayLayout with { ElementType = PrimitiveType.Float32 },
            },
            "array-mutable" => arrayVariable with { IsMutable = true },
            "array-out" => arrayVariable with { IsOut = true },
            "array-init" => arrayVariable with { IsInit = true },
            "array-initialization-local" => arrayVariable with { IsInitializationLocal = true },
            _ => arrayVariable,
        };
        if (variant == "array-elements")
        {
            arrayLocal = arrayLocal with { Initializer = array with { Elements = array.Elements.RemoveAt(2) } };
        }
        if (variant == "buffer-width")
        {
            buffer = buffer with {
                Fields = buffer.Fields.SetItem(0,
                new BoundConstant(37, PrimitiveType.UInt32, buffer.Span))
            };
        }
        if (variant == "subresource-pitch")
        {
            data = data with {
                Fields = data.Fields.SetItem(0,
                new BoundConstant(1, PrimitiveType.UInt32, data.Span))
            };
        }
        var bufferVariable = main.Variables[bufferLocal.Variable] with {
            NativeStorageRecordFacts = new NativeStorageRecordFacts(loan,
                variant == "descriptor-facts" ? 35u : 36u),
        };
        var dataVariable = main.Variables[dataLocal.Variable] with {
            NativeStorageRecordFacts = new NativeStorageRecordFacts(loan, null),
        };
        gate = variant switch {
            "gate-loan" => gate with { InitialData = gate.InitialData with { Variable = -1 } },
            "gate-mode" => gate with { Mode = 3 },
            "gate-identity" => gate with { CatalogIdentity = NativeDx11CreateBufferFixtureContract.CatalogIdentity },
            "createbuffer-slot" => gate with { CreateBuffer = gate.CreateBuffer with { Slot = 2 } },
            "createbuffer-abi" => gate with { CreateBuffer = gate.CreateBuffer with { Abi = "other" } },
            "release-effect" => gate with {
                Release = gate.Release with {
                    Effect = NativeDx11BufferMethodEffect.CreateOneOwnedOutputReference,
                }
            },
            "helper-key" => gate with { ImportKeys = gate.ImportKeys.SetItem(0, "test.other") },
            _ => gate,
        };
        var statements = main.Body.Statements.SetItem(0, arrayLocal)
            .SetItem(1, bufferLocal with { Initializer = buffer })
            .SetItem(2, dataLocal with { Initializer = data })
            .SetItem(3, returned with { Value = gate });
        statements = variant switch {
            "array-lifetime" => statements.Insert(3, new BoundLifetimeEnd(arrayLocal.Variable, -1, source.Span)),
            "descriptor-lifetime" => statements.Insert(3, new BoundLifetimeEnd(bufferLocal.Variable, -1, source.Span)),
            "array-scope" => statements.SetItem(0,
                new BoundBlock([arrayLocal], arrayLocal.Span) { IsManagedLexicalScope = true }),
            "descriptor-scope" => [statements[0],
                new BoundBlock([statements[1], statements[2]], bufferLocal.Span) { IsManagedLexicalScope = true },
                statements[3]],
            "address-escape" => statements.Insert(3, new BoundExpressionStatement(source, source.Span)),
            _ => statements,
        };
        main = main with {
            Variables = main.Variables.SetItem(arrayLocal.Variable, arrayVariable)
                .SetItem(bufferLocal.Variable, bufferVariable).SetItem(dataLocal.Variable, dataVariable),
            Body = main.Body with { Statements = statements },
        };
        program = program with {
            Functions = program.Functions.SetItem(program.EntryPoint, main),
            NativeImportCatalogId = variant == "catalog-identity" ? "test.other" : program.NativeImportCatalogId,
            NativeImportCatalog = variant == "catalog-removed" ? null : program.NativeImportCatalog,
            RuntimePolicy = variant == "checked-policy" ? BoundRuntimePolicy.CheckedExceptions : program.RuntimePolicy,
        };
        AssertSampleArrayCreateBufferVerifierRejects(program);
    }

    [TestCase("name")]
    [TestCase("sequential")]
    [TestCase("transparent")]
    [TestCase("unique")]
    [TestCase("size")]
    [TestCase("cleanup")]
    [TestCase("field-count")]
    [TestCase("field-offset")]
    [TestCase("field-type")]
    [TestCase("field-size")]
    [TestCase("field-unsafe")]
    public void SampleArrayCreateBufferFixtureMandatoryVerificationRejectsForgedVertexLayout(string variant)
    {
        var compilation = CompileSampleArrayCreateBufferFixture(SampleArrayBufferInitializers()
            + "return createbufferfixture(0, &description, &initialData);");
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue22 = compilation.Program;
        Assert.That(__nunitAssertValue22, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue22);
        var __nunitAssertValue23 = global::System.Linq.Enumerable.ToArray(program.Structures);
        Assert.That(__nunitAssertValue23, Has.Exactly(1).Items);
        var vertex = __nunitAssertValue23[0];
        var field = vertex.Fields[0];
        field = variant switch {
            "field-name" => field with { Name = "W" },
            "field-offset" => field with { Offset = 4 },
            "field-type" => field with { Type = PrimitiveType.Float64, Size = 8 },
            "field-size" => field with { Size = 8 },
            "field-mutable" => field with { IsMutable = true },
            "field-private" => field with { IsPrivate = true },
            "field-internal" => field with { IsInternal = true },
            "field-unsafe" => field with { RequiresUnsafeCaller = true },
            _ => field,
        };
        vertex = vertex with { Fields = vertex.Fields.SetItem(0, field) };
        vertex = variant switch {
            "name" => vertex with { Name = "Other.Vertex" },
            "sequential" => vertex with { IsSequential = false },
            "transparent" => vertex with { IsTransparent = true },
            "unique" => vertex with { IsUnique = true },
            "private" => vertex with { IsPrivate = true },
            "internal" => vertex with { IsInternal = true },
            "size" => vertex with { Size = 16 },
            "cleanup" => vertex with { CleanupMethod = "Triangle.Vertex.Close" },
            "field-count" => vertex with { Fields = vertex.Fields.RemoveAt(2) },
            _ => vertex,
        };
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue24 = main.Body.Statements[0];
        Assert.That(__nunitAssertValue24, Is.TypeOf<BoundLocal>());
        var __nunitAssertValue25 = (((BoundLocal)__nunitAssertValue24)).Initializer;
        Assert.That(__nunitAssertValue25, Is.TypeOf<BoundNativeFixedArrayInitialization>());
        var array = ((BoundNativeFixedArrayInitialization)__nunitAssertValue25);
        AssertSampleArrayCreateBufferVerifierRejects(program with { Structures = [vertex] });
    }

    [TestCase("private")]
    [TestCase("internal")]
    [TestCase("field-name")]
    [TestCase("field-mutable")]
    [TestCase("field-private")]
    [TestCase("field-internal")]
    public void SampleArrayCreateBufferFixtureAcceptsCataloguedVertexLayoutMetadata(string variant)
    {
        var compilation = CompileSampleArrayCreateBufferFixture(SampleArrayBufferInitializers()
            + "return createbufferfixture(0, &description, &initialData);");
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue26 = compilation.Program;
        Assert.That(__nunitAssertValue26, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue26);
        var __nunitAssertValue27 = global::System.Linq.Enumerable.ToArray(program.Structures);
        Assert.That(__nunitAssertValue27, Has.Exactly(1).Items);
        var vertex = __nunitAssertValue27[0];
        var field = vertex.Fields[0];
        field = variant switch {
            "field-name" => field with { Name = "W" },
            "field-mutable" => field with { IsMutable = true },
            "field-private" => field with { IsPrivate = true },
            "field-internal" => field with { IsInternal = true },
            _ => field,
        };
        vertex = vertex with { Fields = vertex.Fields.SetItem(0, field) };
        vertex = variant switch {
            "private" => vertex with { IsPrivate = true },
            "internal" => vertex with { IsInternal = true },
            _ => vertex,
        };
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue28 = main.Body.Statements[0];
        Assert.That(__nunitAssertValue28, Is.TypeOf<BoundLocal>());
        var __nunitAssertValue29 = (((BoundLocal)__nunitAssertValue28)).Initializer;
        Assert.That(__nunitAssertValue29, Is.TypeOf<BoundNativeFixedArrayInitialization>());
        var array = ((BoundNativeFixedArrayInitialization)__nunitAssertValue29);
        Assert.That(NativeFixedArrayContract.MatchesLayout(array.Layout, [vertex],
            program.NativeImportCatalog), Is.True);
        var accepted = program with { Structures = [vertex] };
        SemanticVerifier.VerifyReferences(accepted);
        _ = NativeEmitter.EmitWithNativeImportsForTesting(accepted, NativeRuntimePolicy.FailFast);
    }

    [Test]
    public void SampleArrayCreateBufferFixtureCannotRetagEitherOlderProfileOrBroadenConsumers()
    {
        var body = SampleArrayBufferInitializers() + "return createbufferfixture(0, &description, &initialData);";
        var compilation = CompileSampleArrayCreateBufferFixture(body);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue30 = compilation.Program;
        Assert.That(__nunitAssertValue30, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue30);
        foreach (var catalog in new[] { CreateDx11CreateBufferFixtureCatalog(), CreateSampleArrayBufferCatalog(),
            SampleArrayCatalog, NativeImportCatalog.WindowsX64 })
        {
            Assert.That(CompileSampleArrayCreateBufferFixture(body, catalog: catalog).Program, Is.Null);
            AssertSampleArrayCreateBufferVerifierRejects(program with {
                NativeImportCatalog = catalog,
                NativeImportCatalogId = catalog.Identity,
            });
        }
        var legacy = CompileDx11CreateBufferFixture(CreateBufferFixtureBody());
        Assert.That(legacy.HasErrors, Is.False, string.Join(Environment.NewLine, legacy.Diagnostics));
        var __nunitAssertValue31 = legacy.Program;
        Assert.That(__nunitAssertValue31, Is.TypeOf<BoundProgram>());
        var legacyProgram = ((BoundProgram)__nunitAssertValue31);
        var newCatalog = CreateSampleArrayCreateBufferFixtureCatalog();
        AssertSampleArrayCreateBufferVerifierRejects(legacyProgram with {
            NativeImportCatalog = newCatalog,
            NativeImportCatalogId = newCatalog.Identity,
        });
        var descriptorOnly = CompileSampleArrayBuffer(SampleArrayBufferInitializers()
            + "return " + BufferDescriptorCall + ";");
        Assert.That(descriptorOnly.HasErrors, Is.False, string.Join(Environment.NewLine, descriptorOnly.Diagnostics));
        var __nunitAssertValue32 = descriptorOnly.Program;
        Assert.That(__nunitAssertValue32, Is.TypeOf<BoundProgram>());
        AssertSampleArrayCreateBufferVerifierRejects(((BoundProgram)__nunitAssertValue32) with {
            NativeImportCatalog = newCatalog,
            NativeImportCatalogId = newCatalog.Identity,
        });

        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue33 = main.Body.Statements[3];
        Assert.That(__nunitAssertValue33, Is.TypeOf<BoundReturn>());
        var returned = ((BoundReturn)__nunitAssertValue33);
        var __nunitAssertValue34 = returned.Value;
        Assert.That(__nunitAssertValue34, Is.TypeOf<BoundNativeDx11CreateBufferFixture>());
        var gate = ((BoundNativeDx11CreateBufferFixture)__nunitAssertValue34);
        var legacyMain = legacyProgram.Functions[legacyProgram.EntryPoint];
        var __nunitAssertValue35 = legacyMain.Body.Statements[3];
        Assert.That(__nunitAssertValue35, Is.TypeOf<BoundReturn>());
        var __nunitAssertValue36 = (((BoundReturn)__nunitAssertValue35)).Value;
        Assert.That(__nunitAssertValue36, Is.TypeOf<BoundNativeDx11CreateBufferFixture>());
        var legacyGate = ((BoundNativeDx11CreateBufferFixture)__nunitAssertValue36);
        var retaggedGate = legacyGate with { Description = gate.Description, InitialData = gate.InitialData };
        Assert.That(NativeDx11CreateBufferFixtureContract.MatchesGate(retaggedGate, program), Is.False);
        AssertSampleArrayCreateBufferVerifierRejects(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, main with {
                Body = main.Body with {
                    Statements = main.Body.Statements.SetItem(3,
                    returned with { Value = retaggedGate })
                },
            }),
        });
        AssertSampleArrayCreateBufferVerifierRejects(program with {
            NativeImportCatalog = CreateBufferOwnerCatalog(),
            NativeImportCatalogId = NativeDx11BufferOwnerFixtureContract.CatalogIdentity,
            Functions = program.Functions.SetItem(program.EntryPoint, main with {
                Body = main.Body with {
                    Statements = main.Body.Statements.SetItem(3, returned with {
                        Value = gate with { CatalogIdentity = NativeDx11BufferOwnerFixtureContract.CatalogIdentity },
                    })
                },
            }),
        });
    }

    [Test]
    public void SampleArrayCreateBufferFixtureRemainsTestOnlyFailFastPeAot()
    {
        var body = SampleArrayBufferInitializers() + "return createbufferfixture(0, &description, &initialData);";
        var compilation = CompileSampleArrayCreateBufferFixture(body);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue37 = compilation.Program;
        Assert.That(__nunitAssertValue37, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue37);
        Assert.That(CompileSampleArrayCreateBufferFixture(body, policy: BoundRuntimePolicy.CheckedExceptions).Program, Is.Null);
        var source = SyntaxTree.Parse(SampleArrayCreateBufferDeclarations + "\nint32 Main() {" + body + "}");
        Assert.That(Compilation.Create(source).Program, Is.Null);
        Assert.That(Compilation.CreateWithNativeImports(source).Program, Is.Null);
        Assert.That(NativeImportCatalog.WindowsX64.SupportsDx11SampleArrayCreateBufferFixture, Is.False);
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() => NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() =>
            ModuleCompiler.CreateProducer(program with { EntryPoint = -1 }, ["Triangle.Main"]));
        _ = Assert.Throws<NotSupportedException>(() => NativeModuleCompiler.CreateProducer(
            program with { EntryPoint = -1 }, ["Triangle.Main"], "sample_createbuffer", NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(program with { EntryPoint = -1 }, "sample_createbuffer"));
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibrary(
            program with { EntryPoint = -1 }, ["Triangle.Main"], NativeRuntimePolicy.FailFast));
    }

    private static void AssertSampleArrayCreateBufferVerifierRejects(BoundProgram program)
    {
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(program));
        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast));
    }
}
