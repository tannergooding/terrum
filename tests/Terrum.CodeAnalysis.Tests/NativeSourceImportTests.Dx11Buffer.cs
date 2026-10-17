// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private const string BufferDescriptorInitializer = """
        Terrum.Interop.Dx11.BufferDesc description = bufferdesc(&vertices[0], 36, 1, 1, 0, 0, 0);
        Terrum.Interop.Dx11.SubresourceData initialData = subresourcedata(&vertices[0], 0, 0);
        """;
    private const string BufferDescriptorCall = """
        NativeFfiBufferInitialization(&description, 324478056, 81985529216486895,
            -1234567890123, 610800471, &initialData, 0)
        """;
    private static readonly string BufferDescriptorDeclaration = $$"""
        [Terrum.Interop.NativeImport("{{NativeDx11BufferContract.ImportKey}}")]
        [NoThrow]
        extern int32 NativeFfiBufferInitialization(Terrum.Interop.Dx11.ReadonlyBufferDesc first,
            uint32 second, uint64 third, int64 fourth, uint32 fifth,
            Terrum.Interop.Dx11.ReadonlySubresourceData sixth, uint32 fail);
        """;
    private static readonly string SampleArrayBufferDescriptorDeclaration = $$"""
        [Terrum.Interop.NativeImport("{{NativeDx11BufferContract.SampleArrayImportKey}}")]
        [NoThrow]
        extern int32 NativeFfiBufferInitialization(Terrum.Interop.NativeStorageAddress first,
            uint32 second, uint64 third, int64 fourth, uint32 fifth,
            Terrum.Interop.NativeStorageAddress sixth, uint32 fail);
        """;

    internal static string BufferDescriptorSource => Vertex3Declarations + BufferDescriptorDeclaration
        + "\nint32 Main() {\n" + Vertex3Initializer + BufferDescriptorInitializer
        + "return " + BufferDescriptorCall + ";\n}";

    private static NativeImportDefinition CreateBufferDescriptorImportDefinition() =>
        new(NativeDx11BufferContract.ImportKey, "native-import-abi-probe.dll", "NativeFfiBufferInitialization",
            [PrimitiveType.NativeDx11BufferDescAddress, PrimitiveType.UInt32, PrimitiveType.UInt64,
                PrimitiveType.Int64, PrimitiveType.UInt32, PrimitiveType.NativeDx11SubresourceDataAddress,
                PrimitiveType.UInt32],
            PrimitiveType.Int32, true, true, false, NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [
                NativeImportParameterContract.ReadonlyDx11BufferInitializationNoEscape,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.ReadonlyDx11BufferInitializationNoEscape,
                NativeImportParameterContract.Value,
            ],
        };

    private static NativeImportDefinition CreateSampleArrayBufferDescriptorImportDefinition() =>
        new(NativeDx11BufferContract.SampleArrayImportKey, "native-import-abi-probe.dll",
            "NativeFfiBufferInitialization",
            [PrimitiveType.NativeStorageAddress, PrimitiveType.UInt32, PrimitiveType.UInt64,
                PrimitiveType.Int64, PrimitiveType.UInt32, PrimitiveType.NativeStorageAddress,
                PrimitiveType.UInt32],
            PrimitiveType.Int32, true, true, false, NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [
                NativeImportParameterContract.ReadonlyNativeStorageNoEscape,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.ReadonlyNativeStorageNoEscape,
                NativeImportParameterContract.Value,
            ],
            StorageRecordParameters = [
                new(0, NativeDx11BufferContract.BufferTypeName, true,
                    NativeDx11BufferContract.SampleArrayLoanGroup),
                new(5, NativeDx11BufferContract.SubresourceTypeName, true,
                    NativeDx11BufferContract.SampleArrayLoanGroup),
            ],
        };

    private static Compilation CompileBufferDescriptors(string body, string? declarations = null,
        BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast, string? additionalSource = null)
    {
        var tree = SyntaxTree.Parse((declarations ?? Vertex3Declarations + BufferDescriptorDeclaration)
            + "\nint32 Main() {\n" + body + "\n}");

        return Compilation.CreateWithNativeImports(
            additionalSource is null ? [tree] : [tree, SyntaxTree.Parse(additionalSource)],
            NativeImportCatalog.CreateForTesting("test-native-dx11-buffer-v1",
                [CreateVertex3ImportDefinition(), CreateBufferDescriptorImportDefinition()]), policy);
    }

    [TestCase(1)]
    [TestCase(35)]
    [TestCase(36)]
    public void BufferDescriptorsVerifyExactLayoutAndBacking(int byteWidth)
    {
        var compilation = CompileBufferDescriptors(Vertex3Initializer + BufferDescriptorInitializer.Replace(
            ", 36,", $", {byteWidth},", StringComparison.Ordinal) + "return " + BufferDescriptorCall + ";");
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue0);
        Assert.That(ValueLayout.Size(PrimitiveType.NativeDx11BufferDesc, program.Structures), Is.EqualTo(24));
        Assert.That(ValueLayout.Alignment(PrimitiveType.NativeDx11BufferDesc, program.Structures), Is.EqualTo(4));
        Assert.That(ValueLayout.Size(PrimitiveType.NativeDx11SubresourceData, program.Structures), Is.EqualTo(16));
        Assert.That(ValueLayout.Alignment(PrimitiveType.NativeDx11SubresourceData, program.Structures), Is.EqualTo(8));
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue1 = main.Body.Statements[0];
        Assert.That(__nunitAssertValue1, Is.TypeOf<BoundLocal>());
        var vertices = ((BoundLocal)__nunitAssertValue1);
        var __nunitAssertValue2 = main.Body.Statements[1];
        Assert.That(__nunitAssertValue2, Is.TypeOf<BoundLocal>());
        var description = ((BoundLocal)__nunitAssertValue2);
        var __nunitAssertValue3 = main.Body.Statements[2];
        Assert.That(__nunitAssertValue3, Is.TypeOf<BoundLocal>());
        var data = ((BoundLocal)__nunitAssertValue3);
        var __nunitAssertValue4 = description.Initializer;
        Assert.That(__nunitAssertValue4, Is.TypeOf<BoundNativeDx11DescriptorInitialization>());
        var buffer = ((BoundNativeDx11DescriptorInitialization)__nunitAssertValue4);
        var __nunitAssertValue5 = data.Initializer;
        Assert.That(__nunitAssertValue5, Is.TypeOf<BoundNativeDx11DescriptorInitialization>());
        var subresource = ((BoundNativeDx11DescriptorInitialization)__nunitAssertValue5);
        Assert.That(buffer.Fields, Is.All.TypeOf<BoundConstant>());
        Assert.That(buffer.Fields.Select(static field => ((BoundConstant)field).Value),
            Is.EqualTo(new long[] { byteWidth, 1, 1, 0, 0, 0 }));
        Assert.That(subresource.Fields, Is.All.TypeOf<BoundConstant>());
        Assert.That(subresource.Fields.Select(static field => ((BoundConstant)field).Value),
            Is.EqualTo(new long[] { 0, 0 }));
        Assert.That(subresource.Loan, Is.EqualTo(buffer.Loan));
        Assert.That(buffer.Loan, Is.EqualTo(new NativeStorageLoan(vertices.Variable, PrimitiveType.NativeFixedArray,
            (PrimitiveType)100, true, 36)));
        Assert.That(main.Variables[description.Variable].NativeStorageRecordFacts?.BackingExtent, Is.EqualTo((uint?)byteWidth));
        _ = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
    }

    [TestCase("bufferdesc(&vertices[0], 0, 1, 1, 0, 0, 0)")]
    [TestCase("bufferdesc(&vertices[0], 37, 1, 1, 0, 0, 0)")]
    [TestCase("bufferdesc(&vertices[0], 4294967295, 1, 1, 0, 0, 0)")]
    [TestCase("bufferdesc(&vertices[0], uint32.MaxValue, 1, 1, 0, 0, 0)")]
    [TestCase("bufferdesc(&vertices[0], VertexIndex(36), 1, 1, 0, 0, 0)")]
    [TestCase("bufferdesc(&vertices[0], 36, 0, 1, 0, 0, 0)")]
    [TestCase("bufferdesc(&vertices[0], 36, 1, 2, 0, 0, 0)")]
    [TestCase("bufferdesc(&vertices[0], 36, 1, 1, 1, 0, 0)")]
    [TestCase("bufferdesc(&vertices[0], 36, 1, 1, 0, 1, 0)")]
    [TestCase("bufferdesc(&vertices[0], 36, 1, 1, 0, 0, 12)")]
    [TestCase("bufferdesc(&vertices[1], 36, 1, 1, 0, 0, 0)")]
    [TestCase("bufferdesc(&vertices[VertexIndex(0)], 36, 1, 1, 0, 0, 0)")]
    [TestCase("bufferdesc(null, 36, 1, 1, 0, 0, 0)")]
    [TestCase("bufferdesc(&vertices[0], 36, 1, 1, 0, 0)")]
    public void BufferDescriptorsRejectInvalidWidthSettingsAndDataSource(string initializer)
    {
        var body = Vertex3Initializer + $"\nTerrum.Interop.Dx11.BufferDesc description = {initializer}; return 0;";
        var compilation = CompileBufferDescriptors(body);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("subresourcedata(&vertices[0], 1, 0)")]
    [TestCase("subresourcedata(&vertices[0], 0, 1)")]
    [TestCase("subresourcedata(&vertices[0], 0)")]
    [TestCase("subresourcedata(null, 0, 0)")]
    [TestCase("subresourcedata(0, 0, 0)")]
    [TestCase("subresourcedata(&vertices[1], 0, 0)")]
    [TestCase("subresourcedata(&vertices[-1], 0, 0)")]
    [TestCase("subresourcedata(&vertices[3], 0, 0)")]
    [TestCase("subresourcedata(&vertices[VertexIndex(0)], 0, 0)")]
    [TestCase("subresourcedata((write ptr<void>)&vertices[0], 0, 0)")]
    [TestCase("subresourcedata((ptr<void>)&vertices[0], 0, 0)")]
    [TestCase("subresourcedata(&vertices[0] + 1, 0, 0)")]
    [TestCase("subresourcedata(true ? &vertices[0] : &vertices[0], 0, 0)")]
    public void BufferDescriptorsRejectInvalidEmbeddedPointerAndPitches(string initializer)
    {
        var compilation = CompileBufferDescriptors(Vertex3Initializer
            + $"\nTerrum.Interop.Dx11.SubresourceData initialData = {initializer}; return 0;");
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("Terrum.Interop.Dx11.BufferDesc copy = description; return 0;")]
    [TestCase("Terrum.Interop.Dx11.SubresourceData copy = initialData; return 0;")]
    [TestCase("mut Terrum.Interop.Dx11.BufferDesc copy = bufferdesc(&vertices[0], 36, 1, 1, 0, 0, 0); return 0;")]
    [TestCase("mut Terrum.Interop.Dx11.SubresourceData copy = subresourcedata(&vertices[0], 0, 0); return 0;")]
    [TestCase("init Terrum.Interop.Dx11.BufferDesc vacant; return 0;")]
    [TestCase("init Terrum.Interop.Dx11.SubresourceData vacant; return 0;")]
    [TestCase("description = bufferdesc(&vertices[0], 35, 1, 1, 0, 0, 0); return 0;")]
    [TestCase("initialData = subresourcedata(&vertices[0], 0, 0); return 0;")]
    [TestCase("description.ByteWidth = 1; return 0;")]
    [TestCase("initialData.pSysMem = &vertices[0]; return 0;")]
    [TestCase("return description.ByteWidth;")]
    [TestCase("Terrum.Interop.Dx11.ReadonlyBufferDesc pointer = &description; return 0;")]
    [TestCase("Terrum.Interop.Dx11.ReadonlySubresourceData pointer = &initialData; return 0;")]
    [TestCase("return NativeFfiBufferInitialization(&initialData, 0, 0, 0, 0, &description, 0);")]
    [TestCase("return NativeFfiBufferInitialization(null, 0, 0, 0, 0, &initialData, 0);")]
    [TestCase("return NativeFfiBufferInitialization(&description, 0, 0, 0, 0, null, 0);")]
    [TestCase("return NativeFfiBufferInitialization(true ? &description : &description, 0, 0, 0, 0, &initialData, 0);")]
    [TestCase("description!; return " + BufferDescriptorCall + ";")]
    [TestCase("initialData!; return " + BufferDescriptorCall + ";")]
    [TestCase("vertices!; return " + BufferDescriptorCall + ";")]
    public void BufferDescriptorsRejectCopiesMutationAddressEscapeAndMismatches(string body)
    {
        var compilation = CompileBufferDescriptors(Vertex3Initializer + BufferDescriptorInitializer + body);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("struct Holder { Terrum.Interop.Dx11.SubresourceData Value; }")]
    [TestCase("mut class Holder { Terrum.Interop.Dx11.BufferDesc Value; }")]
    [TestCase("int32 Forward(Terrum.Interop.Dx11.BufferDesc value) { return 0; }")]
    [TestCase("int32 Forward(Terrum.Interop.Dx11.SubresourceData value) { return 0; }")]
    [TestCase("int32 Forward(Terrum.Interop.Dx11.ReadonlyBufferDesc value) { return 0; }")]
    [TestCase("int32 Forward(Terrum.Interop.Dx11.ReadonlySubresourceData value) { return 0; }")]
    [TestCase("Terrum.Interop.Dx11.BufferDesc Escape() { " + Vertex3Initializer + BufferDescriptorInitializer + " return description; }")]
    [TestCase("Terrum.Interop.Dx11.ReadonlySubresourceData Escape() { " + Vertex3Initializer + BufferDescriptorInitializer + " return &initialData; }")]
    [TestCase("namespace Terrum.Interop.Dx11 { struct BufferDesc { int32 Value; } }")]
    [TestCase("namespace Terrum.Interop.Dx11 { mut class ReadonlySubresourceData { int32 Value; } }")]
    public void BufferDescriptorsRejectDeclarationsForwardingAndReservedTypeSpoofing(string declaration)
    {
        var compilation = CompileBufferDescriptors("return 0;",
            Vertex3Declarations + BufferDescriptorDeclaration + declaration);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [Test]
    public void BufferDescriptorsRejectWrongBackingAndNominalIdentity()
    {
        var other = Vertex3Initializer.Replace("vertices =", "other =", StringComparison.Ordinal);
        var descriptors = BufferDescriptorInitializer.Replace("subresourcedata(&vertices[0]",
            "subresourcedata(&other[0]", StringComparison.Ordinal);
        Assert.That(CompileBufferDescriptors(Vertex3Initializer + other + descriptors
            + "return " + BufferDescriptorCall + ";").Program, Is.Null);
        var declaration = Vertex3Declarations[..Vertex3Declarations.IndexOf(
            "[Terrum.Interop.NativeImport", StringComparison.Ordinal)];
        other = other.Replace("InlineArray<Vertex, 3>", "InlineArray<Other.Vertex, 3>", StringComparison.Ordinal)
            .Replace("new Vertex(", "new Other.Vertex(", StringComparison.Ordinal);
        var wrongNominal = CompileBufferDescriptors(Vertex3Initializer + other + descriptors
            + "return " + BufferDescriptorCall + ";", additionalSource: "namespace Other;\n" + declaration);
        Assert.That(wrongNominal.Program, Is.Null);
        Assert.That(wrongNominal.Diagnostics, Is.Not.Empty);
        var matchingCatalog = NativeImportCatalog.CreateForTesting("test-native-dx11-buffer-matching-other-vertex-v1",
            [CreateVertex3ImportDefinition(), CreateBufferDescriptorImportDefinition()],
            fixedArrayStorageLayouts: [Vertex3StorageLayout("Other.Vertex")]);
        var matchingSource = Vertex3Declarations + "\n" + BufferDescriptorDeclaration
            + "\nint32 Main() {\n" + Vertex3Initializer + other
            + descriptors.Replace("bufferdesc(&vertices[0]", "bufferdesc(&other[0]", StringComparison.Ordinal)
            + "return " + BufferDescriptorCall + ";\n}";
        var matchingNominal = Compilation.CreateWithNativeImports([
            SyntaxTree.Parse(matchingSource),
            SyntaxTree.Parse("namespace Other;\n" + declaration),
        ], matchingCatalog, BoundRuntimePolicy.FailFast);
        Assert.That(matchingNominal.HasErrors, Is.False, string.Join(Environment.NewLine, matchingNominal.Diagnostics));
        var __nunitAssertValue8 = matchingNominal.Program;
        Assert.That(__nunitAssertValue8, Is.TypeOf<BoundProgram>());
        Assert.That(((BoundProgram)__nunitAssertValue8).Structures.Length, Is.EqualTo(2));
    }

    [Test]
    public void BufferDescriptorsRemainTestOnlyFailFastPeAot()
    {
        var body = Vertex3Initializer + BufferDescriptorInitializer + "return " + BufferDescriptorCall + ";";
        var compilation = CompileBufferDescriptors(body);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue9 = compilation.Program;
        Assert.That(__nunitAssertValue9, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue9);
        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() => NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() =>
            ModuleCompiler.CreateProducer(program with { EntryPoint = -1 }, ["Main"]));
        _ = Assert.Throws<NotSupportedException>(() => NativeModuleCompiler.CreateProducer(
            program with { EntryPoint = -1 }, ["Main"], "bufferdesc", NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(program with { EntryPoint = -1 }, "bufferdesc"));
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibrary(
            program with { EntryPoint = -1 }, ["Main"], NativeRuntimePolicy.FailFast));
        Assert.That(CompileBufferDescriptors(body, policy: BoundRuntimePolicy.CheckedExceptions).Program, Is.Null);
        Assert.That(NativeImportCatalog.WindowsX64.SupportsDx11BufferInitialization, Is.False);
        var source = Vertex3Declarations + BufferDescriptorDeclaration + "\nint32 Main() {" + body + "}";
        Assert.That(Compilation.Create(SyntaxTree.Parse(source)).Program, Is.Null);
        Assert.That(Compilation.CreateWithNativeImports(SyntaxTree.Parse(source)).Program, Is.Null);

        var stripped = program with {
            NativeImportCatalog = null,
            NativeImportCatalogId = null,
            Functions = program.Functions.Select(static function =>
                function with { NativeImportKey = null }).ToImmutableArray(),
        };
        _ = Assert.Throws<NotSupportedException>(() => SemanticVerifier.RejectSourceNativeImports(stripped, "JIT"));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(stripped, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImportsForTesting(stripped, NativeRuntimePolicy.FailFast));

        var descriptorOnlyCatalog = NativeImportCatalog.CreateForTesting("test-descriptor-only",
            [CreateBufferDescriptorImportDefinition()],
            fixedArrayStorageLayouts: [Vertex3StorageLayout("Vertex")]);
        var vertexDeclaration = Vertex3Declarations[..Vertex3Declarations.IndexOf(
            "[Terrum.Interop.NativeImport", StringComparison.Ordinal)];
        var descriptorOnly = Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(vertexDeclaration + BufferDescriptorDeclaration + "\nint32 Main() {" + body + "}")],
            descriptorOnlyCatalog, BoundRuntimePolicy.FailFast);
        Assert.That(descriptorOnly.HasErrors, Is.False, string.Join(Environment.NewLine, descriptorOnly.Diagnostics));
        var __nunitAssertValue10 = descriptorOnly.Program;
        Assert.That(__nunitAssertValue10, Is.TypeOf<BoundProgram>());
        _ = NativeEmitter.EmitWithNativeImportsForTesting(
                    ((BoundProgram)__nunitAssertValue10), NativeRuntimePolicy.FailFast);
        Assert.That(Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)],
            descriptorOnlyCatalog, BoundRuntimePolicy.FailFast).Program, Is.Null);
    }

    [Test]
    public void TestOnlyCatalogCannotReachTheProductionNativeEmitterThroughSystemImports()
    {
        var catalog = NativeImportCatalog.CreateForTesting("test-mixed-system-and-dx11-fixtures-v1", [
            CreateVertex3ImportDefinition(),
            CreateBufferDescriptorImportDefinition(),
            new NativeImportDefinition(ImportKey, "kernel32.dll", "GetCurrentThreadId", [],
                PrimitiveType.UInt32, true, true, false, NativeImportDeploymentPolicy.System32),
        ]);
        var vertexDeclaration = Vertex3Declarations[..Vertex3Declarations.IndexOf(
            "[Terrum.Interop.NativeImport", StringComparison.Ordinal)];
        var source = vertexDeclaration + $$"""
            [Terrum.Interop.NativeImport("{{ImportKey}}")]
            [NoThrow]
            extern uint32 CurrentThreadId();
            int32 Main() {
                {{Vertex3Initializer}}
                {{BufferDescriptorInitializer}}
                return CurrentThreadId() == 0 ? 1 : 0;
            }
            """;
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)], catalog,
            BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue11 = compilation.Program;
        Assert.That(__nunitAssertValue11, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue11);

        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast));
        _ = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
    }

    [Test]
    public void BufferDescriptorCatalogRejectsUnreviewedContracts()
    {
        var definition = CreateBufferDescriptorImportDefinition();
        foreach (var invalid in new[] {
            definition with { Key = "unreviewed" },
            definition with { DllName = "other.dll" },
            definition with { ExportName = "OtherExport" },
            definition with { DeploymentPolicy = NativeImportDeploymentPolicy.System32 },
            definition with { IsNoThrow = false },
            definition with { RequiresUnsafeCaller = true },
            definition with { DoesNotReturn = true },
            definition with { ParameterTypes = [PrimitiveType.NativeDx11BufferDescAddress] },
            definition with { ParameterContracts = [] },
            definition with { ParameterContracts = definition.ParameterContracts.SetItem(0,
                NativeImportParameterContract.Value) },
            definition with { ParameterContracts = definition.ParameterContracts.SetItem(5,
                NativeImportParameterContract.Value) },
        })
        {
            _ = Assert.Throws<ArgumentException>(() =>
                NativeImportCatalog.CreateForTesting("test-invalid-bufferdesc", [invalid]));
        }
        _ = Assert.Throws<ArgumentException>(() => new NativeImportCatalog("not-test-only", [definition]));
        foreach (var qualifier in new[] { "mut ", "out ", "init ", "write " })
        {
            foreach (var parameter in new[] { "ReadonlyBufferDesc first", "ReadonlySubresourceData sixth" })
            {
                var type = "Terrum.Interop.Dx11." + parameter;
                var declarations = Vertex3Declarations + BufferDescriptorDeclaration.Replace(type,
                    qualifier + type, StringComparison.Ordinal);
                Assert.That(CompileBufferDescriptors("return 0;", declarations).Program, Is.Null);
            }
        }
    }

    [Test]
    public void DescriptorInitializerNamesRemainOrdinaryFunctionsOutsideTheTestGate()
    {
        var tree = SyntaxTree.Parse("""
            int32 bufferdesc() { return 42; }
            int32 subresourcedata() { return 1; }
            int32 Main() { return bufferdesc() + subresourcedata(); }
            """);
        foreach (var compilation in new[] { Compilation.Create(tree), Compilation.CreateWithNativeImports(tree) })
        {
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var __nunitAssertValue12 = compilation.Program;
            Assert.That(__nunitAssertValue12, Is.TypeOf<BoundProgram>());
            var program = ((BoundProgram)__nunitAssertValue12);
            Assert.That(program.NativeImportCatalog, Is.Null);
            _ = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast);
        }
    }

    [TestCase("bufferdesc")]
    [TestCase("subresourcedata")]
    public void DescriptorInitializerNamesDoNotHideInferredGenericCallsInTheTestProfile(string name)
    {
        var declarations = Vertex3Declarations + BufferDescriptorDeclaration + $$"""
            [NoThrow]
            T {{name}}<T>(T value) where T : [Copy] { return value; }
            """;
        var compilation = CompileBufferDescriptors($"return {name}(42);", declarations);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue13 = compilation.Program;
        Assert.That(__nunitAssertValue13, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue13);
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue14 = main.Body.Statements[0];
        Assert.That(__nunitAssertValue14, Is.TypeOf<BoundReturn>());
        var result = ((BoundReturn)__nunitAssertValue14);
        var __nunitAssertValue15 = result.Value;
        Assert.That(__nunitAssertValue15, Is.TypeOf<BoundCall>());
        var call = ((BoundCall)__nunitAssertValue15);
        Assert.That(program.Functions[call.Function].IsExternal, Is.False);
        Assert.That(program.Functions[call.Function].ReturnType, Is.EqualTo(PrimitiveType.Int32));
    }

    [Test]
    public void BufferDescriptorsPreserveNoThrowAndConstantFolding()
    {
        var declarations = Vertex3Declarations.Replace("public Vertex(", "[NoThrow] public Vertex(",
            StringComparison.Ordinal) + BufferDescriptorDeclaration;
        var body = Vertex3Initializer + BufferDescriptorInitializer.Replace(", 36,", ", 18 + 18,",
            StringComparison.Ordinal) + "return " + BufferDescriptorCall + ";";
        var compilation = CompileBufferDescriptors("return CheckDescriptors();",
            declarations + "\n[NoThrow] int32 CheckDescriptors() {\n" + body + "\n}");
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue16 = compilation.Program;
        Assert.That(__nunitAssertValue16, Is.TypeOf<BoundProgram>());
        _ = NativeEmitter.EmitWithNativeImportsForTesting(
                    ((BoundProgram)__nunitAssertValue16), NativeRuntimePolicy.FailFast);
        Assert.That(CompileBufferDescriptors("return 0;", declarations
            + "\n[NoThrow] int32 CheckDescriptors(int32 index) {\n"
            + body.Replace("&vertices[0]", "&vertices[index]", StringComparison.Ordinal) + "\n}").Program, Is.Null);
    }

    [TestCase("zero-width")]
    [TestCase("overrun-width")]
    [TestCase("max-width")]
    [TestCase("noncanonical-width")]
    [TestCase("width-metadata")]
    [TestCase("usage")]
    [TestCase("bind-flags")]
    [TestCase("cpu-access")]
    [TestCase("misc-flags")]
    [TestCase("stride")]
    [TestCase("pitch")]
    [TestCase("slice-pitch")]
    [TestCase("missing-fields")]
    [TestCase("undefined-fields")]
    [TestCase("null-pointer")]
    [TestCase("writable-pointer")]
    [TestCase("nonzero-start")]
    [TestCase("dynamic-start")]
    [TestCase("wrong-source")]
    [TestCase("wrong-backing")]
    [TestCase("wrong-nominal")]
    [TestCase("writable-loan")]
    [TestCase("short-extent")]
    [TestCase("long-extent")]
    [TestCase("missing-loan")]
    [TestCase("address-variable")]
    [TestCase("address-type")]
    [TestCase("address-backing")]
    [TestCase("address-nominal")]
    [TestCase("address-writable")]
    [TestCase("address-extent")]
    [TestCase("address-nonzero-start")]
    [TestCase("wrong-descriptor")]
    [TestCase("copy")]
    [TestCase("escape")]
    [TestCase("forward")]
    [TestCase("mutable-descriptor")]
    [TestCase("vacant-descriptor")]
    [TestCase("reassigned-descriptor")]
    [TestCase("retired-backing")]
    [TestCase("retired-descriptor")]
    [TestCase("scoped-backing")]
    [TestCase("scoped-descriptors")]
    [TestCase("no-throw-dynamic-start")]
    public void BufferDescriptorsMandatoryVerificationRejectsForgedFactsAndUses(string variant)
    {
        var body = Vertex3Initializer
            + Vertex3Initializer.Replace("vertices =", "other =", StringComparison.Ordinal)
            + "\nint32 index = 0;\n" + BufferDescriptorInitializer + """
            Terrum.Interop.Dx11.BufferDesc otherDescription = bufferdesc(&other[0], 35, 1, 1, 0, 0, 0);
            Terrum.Interop.Dx11.SubresourceData otherData = subresourcedata(&other[0], 0, 0);
            """ + "return " + BufferDescriptorCall + ";";
        var vertexDeclaration = Vertex3Declarations[..Vertex3Declarations.IndexOf(
            "[Terrum.Interop.NativeImport", StringComparison.Ordinal)];
        var compilation = CompileBufferDescriptors(body,
            Vertex3Declarations.Replace("public Vertex(", "[NoThrow] public Vertex(", StringComparison.Ordinal)
                + BufferDescriptorDeclaration,
            additionalSource: "namespace Other;\n" + vertexDeclaration);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue17 = compilation.Program;
        Assert.That(__nunitAssertValue17, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue17);
        var main = program.Functions[program.EntryPoint];
        var statements = main.Body.Statements;
        var __nunitAssertValue18 = statements[0];
        Assert.That(__nunitAssertValue18, Is.TypeOf<BoundLocal>());
        var vertices = ((BoundLocal)__nunitAssertValue18);
        var __nunitAssertValue19 = statements[1];
        Assert.That(__nunitAssertValue19, Is.TypeOf<BoundLocal>());
        var other = ((BoundLocal)__nunitAssertValue19);
        var __nunitAssertValue20 = statements[2];
        Assert.That(__nunitAssertValue20, Is.TypeOf<BoundLocal>());
        var index = ((BoundLocal)__nunitAssertValue20);
        var __nunitAssertValue21 = statements[3];
        Assert.That(__nunitAssertValue21, Is.TypeOf<BoundLocal>());
        var description = ((BoundLocal)__nunitAssertValue21);
        var __nunitAssertValue22 = statements[4];
        Assert.That(__nunitAssertValue22, Is.TypeOf<BoundLocal>());
        var data = ((BoundLocal)__nunitAssertValue22);
        var __nunitAssertValue23 = statements[5];
        Assert.That(__nunitAssertValue23, Is.TypeOf<BoundLocal>());
        var otherDescription = ((BoundLocal)__nunitAssertValue23);
        var __nunitAssertValue24 = statements[6];
        Assert.That(__nunitAssertValue24, Is.TypeOf<BoundLocal>());
        var otherData = ((BoundLocal)__nunitAssertValue24);
        var __nunitAssertValue25 = description.Initializer;
        Assert.That(__nunitAssertValue25, Is.TypeOf<BoundNativeDx11DescriptorInitialization>());
        var buffer = ((BoundNativeDx11DescriptorInitialization)__nunitAssertValue25);
        var __nunitAssertValue26 = data.Initializer;
        Assert.That(__nunitAssertValue26, Is.TypeOf<BoundNativeDx11DescriptorInitialization>());
        var subresource = ((BoundNativeDx11DescriptorInitialization)__nunitAssertValue26);
        var __nunitAssertValue27 = subresource.DataSource;
        Assert.That(__nunitAssertValue27, Is.TypeOf<BoundNativeStorageAddress>());
        var source = ((BoundNativeStorageAddress)__nunitAssertValue27);
        var __nunitAssertValue28 = subresource.Loan;
        Assert.That(__nunitAssertValue28, Is.TypeOf<NativeStorageLoan>());
        var loan = ((NativeStorageLoan)__nunitAssertValue28);
        var __nunitAssertValue29 = statements[7];
        Assert.That(__nunitAssertValue29, Is.TypeOf<BoundReturn>());
        var result = ((BoundReturn)__nunitAssertValue29);
        var __nunitAssertValue30 = result.Value;
        Assert.That(__nunitAssertValue30, Is.TypeOf<BoundCall>());
        var call = ((BoundCall)__nunitAssertValue30);
        var __nunitAssertValue31 = call.Arguments[5];
        Assert.That(__nunitAssertValue31, Is.TypeOf<BoundNativeStorageAddress>());
        var address = ((BoundNativeStorageAddress)__nunitAssertValue31);
        var forged = main;

        void Initializer(int statement, BoundNativeDx11DescriptorInitialization value, bool synchronizeFacts = false)
        {
            var __nunitAssertValue32 = statements[statement];
            Assert.That(__nunitAssertValue32, Is.TypeOf<BoundLocal>());
            var local = ((BoundLocal)__nunitAssertValue32);
            statements = statements.SetItem(statement, local with { Initializer = value });
            if (synchronizeFacts)
            {
                var __nunitAssertValue33 = main.Variables[local.Variable].NativeStorageRecordFacts;
                Assert.That(__nunitAssertValue33, Is.TypeOf<NativeStorageRecordFacts>());
                var facts = ((NativeStorageRecordFacts)__nunitAssertValue33);
                var backing = value.Loan ?? throw new InvalidOperationException("Synchronized facts require a loan.");
                forged = forged with {
                    Variables = forged.Variables.SetItem(local.Variable,
                    forged.Variables[local.Variable] with {
                        NativeStorageRecordFacts = facts with { Loan = backing },
                    })
                };
            }
        }

        void Argument(int argument, BoundExpression value)
        {
            statements = statements.SetItem(7, result with {
                Value = call with {
                    Arguments = call.Arguments.SetItem(argument, value),
                }
            });
        }

        var metadataVariant = variant switch {
            "wrong-backing" => loan with { BackingVariable = other.Variable },
            "wrong-nominal" => loan with { ElementType = (PrimitiveType)101 },
            "writable-loan" => loan with { IsReadonly = false },
            "short-extent" => loan with { ByteExtent = 35 },
            "long-extent" => loan with { ByteExtent = 37 },
            _ => loan,
        };
        switch (variant)
        {
            case "zero-width" or "overrun-width" or "max-width" or "noncanonical-width" or "width-metadata":
                var width = variant switch {
                    "zero-width" => 0L,
                    "overrun-width" => 37L,
                    "max-width" => -1L,
                    "noncanonical-width" => (long)uint.MaxValue,
                    _ => 35L,
                };
                Initializer(3, buffer with {
                    Fields = buffer.Fields.SetItem(0,
                    new BoundConstant(width, PrimitiveType.UInt32, buffer.Span))
                });
                break;
            case "usage" or "bind-flags" or "cpu-access" or "misc-flags" or "stride":
                var field = variant switch {
                    "usage" => 1,
                    "bind-flags" => 2,
                    "cpu-access" => 3,
                    "misc-flags" => 4,
                    _ => 5
                };
                Initializer(3, buffer with {
                    Fields = buffer.Fields.SetItem(field,
                    new BoundConstant(2, PrimitiveType.UInt32, buffer.Span))
                });
                break;
            case "pitch" or "slice-pitch":
                Initializer(4, subresource with {
                    Fields = subresource.Fields.SetItem(
                    variant == "pitch" ? 0 : 1, new BoundConstant(1, PrimitiveType.UInt32, subresource.Span))
                });
                break;
            case "missing-fields":
                Initializer(4, subresource with { Fields = [] });
                break;
            case "undefined-fields":
                Initializer(4, subresource with { Fields = default });
                break;
            case "null-pointer":
                Initializer(4, subresource with {
                    DataSource = new BoundNull(
                    PrimitiveType.NativeStorageAddress, source.Span)
                });
                break;
            case "writable-pointer":
                Initializer(4, subresource with {
                    DataSource = new BoundMutableReceiverLoan(
                    vertices.Variable, PrimitiveType.NativeStorageAddress, source.Span)
                });
                break;
            case "nonzero-start":
                Initializer(4, subresource with {
                    DataSource = source with {
                        Index = new BoundConstant(1, PrimitiveType.Int32, source.Span),
                    }
                });
                break;
            case "dynamic-start" or "no-throw-dynamic-start":
                Initializer(4, subresource with {
                    DataSource = source with {
                        Index = new BoundRead(index.Variable, PrimitiveType.Int32, index.Span),
                    }
                });
                if (variant == "no-throw-dynamic-start")
                {
                    forged = forged with { IsNoThrow = true };
                }
                break;
            case "wrong-source":
                Initializer(4, subresource with { DataSource = source with { Variable = other.Variable } });
                break;
            case "wrong-backing":
                Initializer(4, subresource with {
                    DataSource = source with { Variable = other.Variable },
                    Loan = metadataVariant
                }, synchronizeFacts: true);
                break;
            case "wrong-nominal" or "writable-loan" or "short-extent" or "long-extent":
                Initializer(4, subresource with { Loan = metadataVariant }, synchronizeFacts: true);
                break;
            case "missing-loan":
                Initializer(4, subresource with { Loan = null });
                break;
            case "address-variable":
                Argument(5, address with { Variable = otherData.Variable });
                break;
            case "address-type":
                Argument(5, address with { Type = PrimitiveType.NativeDx11BufferDescAddress });
                break;
            case "address-backing":
                Argument(5, address with { Variable = vertices.Variable });
                break;
            case "address-nominal":
                Argument(5, address with { ElementType = (PrimitiveType)101 });
                break;
            case "address-writable":
                Argument(5, address with { IsReadonly = false });
                break;
            case "address-extent":
                Argument(5, address with { ByteExtent = 37 });
                break;
            case "address-nonzero-start":
                Argument(5, address with {
                    Index = new BoundConstant(1, PrimitiveType.Int32, address.Span),
                });
                break;
            case "wrong-descriptor":
                Argument(5, address with { Variable = otherDescription.Variable });
                break;
            case "copy":
                statements = statements.SetItem(5, otherDescription with {
                    Initializer = new BoundRead(
                    description.Variable, PrimitiveType.NativeDx11BufferDesc, description.Span)
                });
                break;
            case "escape":
                statements = statements.Insert(7, new BoundExpressionStatement(address, address.Span));
                break;
            case "forward":
                Argument(1, address);
                break;
            case "mutable-descriptor" or "vacant-descriptor":
                forged = forged with {
                    Variables = forged.Variables.SetItem(data.Variable,
                    forged.Variables[data.Variable] with {
                        IsMutable = variant == "mutable-descriptor",
                        IsInitializationLocal = variant == "vacant-descriptor",
                    })
                };
                break;
            case "reassigned-descriptor":
                statements = statements.Insert(7, new BoundAssignment(data.Variable, subresource, data.Span));
                break;
            case "retired-backing" or "retired-descriptor":
                statements = statements.Insert(7, new BoundLifetimeEnd(
                    variant == "retired-backing" ? vertices.Variable : data.Variable, -1, data.Span));
                break;
            case "scoped-backing":
                statements = statements.SetItem(0, new BoundBlock([vertices], vertices.Span) {
                    IsManagedLexicalScope = true,
                });
                break;
            case "scoped-descriptors":
                statements = statements.SetItem(3, new BoundBlock([description, data], data.Span) {
                    IsManagedLexicalScope = true,
                }).RemoveAt(4);
                break;
            default:
                throw new InvalidOperationException("Unknown descriptor forgery variant.");
        }
        forged = forged with { Body = forged.Body with { Statements = statements } };
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithNativeImportsForTesting(
            program with { Functions = program.Functions.SetItem(program.EntryPoint, forged) },
            NativeRuntimePolicy.FailFast));
    }

    private static async Task ValidateBufferDescriptorRuntimeAsync(string directory)
    {
        foreach (var byteWidth in new[] { 1, 35, 36 })
        {
            foreach (var fail in new[] { 0, 1 })
            {
                var body = Vertex3Initializer + BufferDescriptorInitializer.Replace(", 36,",
                    $", {byteWidth},", StringComparison.Ordinal) + $$"""
                    return NativeFfiBufferInitialization(fail: {{fail}}, sixth: &initialData, fifth: 610800471,
                        fourth: -1234567890123, third: 81985529216486895, second: 324478056,
                        first: &description) == {{(fail == 0 ? 0 : 73)}} ? 0 : 1;
                    """;
                var compilation = CompileBufferDescriptors(body);
                Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
                var path = Path.Combine(directory, $"bufferdesc-{byteWidth}-{fail}.exe");
                var __nunitAssertValue34 = compilation.Program;
                Assert.That(__nunitAssertValue34, Is.TypeOf<BoundProgram>());
                await File.WriteAllBytesAsync(path, NativeEmitter.EmitWithNativeImportsForTesting(
                                    ((BoundProgram)__nunitAssertValue34), NativeRuntimePolicy.FailFast)).ConfigureAwait(false);
                using var process = Process.Start(new ProcessStartInfo(path) {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = directory,
                }) ?? throw new InvalidOperationException("Buffer descriptor executable did not start.");
                try
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync().ConfigureAwait(false);
                    }
                    throw;
                }
                Assert.That(process.ExitCode, Is.EqualTo(0));
            }
        }
    }
}
