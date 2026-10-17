// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private static readonly string SampleArrayBufferDeclarations = $$"""
        namespace Triangle;

        [Blittable] [Layout(Sequential)]
        public struct Vertex {
            public float32 X;
            public float32 Y;
            public float32 Z;
            public Vertex(float32 x, float32 y, float32 z) {
                X = x;
                Y = y;
                Z = z;
            }
        }

        {{SampleArrayBufferDescriptorDeclaration}}

        int32 VertexIndex(int32 value) { return value; }
        """;

    private static string SampleArrayBufferInitializers(int byteWidth = 36) =>
        SampleArrayBufferInitializers(byteWidth.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static string SampleArrayBufferInitializers(string byteWidth) => $$"""
        InlineArray<Vertex, 3> vertices = [
            new Vertex(0, 0.6, 0.5),
            new Vertex(0.6, -0.5, 0.5),
            new Vertex(-0.6, -0.5, 0.5)];
        Terrum.Interop.Dx11.BufferDesc description =
            bufferdesc(&vertices[0], {{byteWidth}}, 1, 1, 0, 0, 0);
        Terrum.Interop.Dx11.SubresourceData initialData =
            subresourcedata(&vertices[0], 0, 0);
        """;

    private static NativeImportCatalog CreateSampleArrayBufferCatalog() =>
        NativeImportCatalog.CreateForTesting(NativeDx11BufferContract.SampleArrayCatalogIdentity,
            [CreateSampleArrayBufferDescriptorImportDefinition()],
            fixedArrayStorageLayouts: NativeDx11BufferContract.SampleArrayStorageLayouts,
            storageRecordLayouts: NativeDx11BufferContract.SampleArrayStorageRecordLayouts);

    private static Compilation CompileSampleArrayBuffer(string body,
        BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast, string? declarations = null,
        NativeImportCatalog? catalog = null) =>
        Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse((declarations ?? SampleArrayBufferDeclarations)
                + "\nint32 Main() {\n" + body + "\n}")],
            catalog ?? CreateSampleArrayBufferCatalog(), policy);

    [TestCase(1)]
    [TestCase(35)]
    [TestCase(36)]
    public void SampleArrayBufferDescriptorsPreserveTriangleVertexLoan(int byteWidth)
    {
        var compilation = CompileSampleArrayBuffer(
            SampleArrayBufferInitializers(byteWidth) + "return " + BufferDescriptorCall + ";");
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue0);
        var __nunitAssertValue1 = program.NativeImportCatalog;
        Assert.That(__nunitAssertValue1, Is.TypeOf<NativeImportCatalog>());
        var catalog = ((NativeImportCatalog)__nunitAssertValue1);
        Assert.That(NativeDx11BufferContract.MatchesSampleArrayCatalog(catalog), Is.True);
        Assert.That(NativeFixedArrayContract.MatchesProgram(program), Is.True);
        Assert.That(catalog.ImportCount, Is.EqualTo(1));
        Assert.That(catalog.Identity, Is.EqualTo(NativeDx11BufferContract.SampleArrayCatalogIdentity));

        var vertexType = (PrimitiveType)100;
        var __nunitAssertValue2 = global::System.Linq.Enumerable.ToArray(program.Structures);
        Assert.That(__nunitAssertValue2, Has.Exactly(1).Items);
        var vertex = __nunitAssertValue2[0];
        Assert.That(vertex.Name, Is.EqualTo("Triangle.Vertex"));
        Assert.That(vertex.IsSequential, Is.True);
        Assert.That(vertex.IsUnique, Is.False);
        Assert.That(vertex.IsTransparent, Is.False);
        Assert.That(vertex.IsPrivate, Is.False);
        Assert.That(vertex.IsInternal, Is.False);
        Assert.That(vertex.CleanupMethod, Is.Null);
        Assert.That(ValueLayout.Size(vertexType, program.Structures), Is.EqualTo(12));
        Assert.That(ValueLayout.Alignment(vertexType, program.Structures), Is.EqualTo(4));
        Assert.That(vertex.Fields.Select(static field => field.Name), Is.EqualTo(new[] { "X", "Y", "Z" }));
        Assert.That(vertex.Fields.Select(static field => field.Offset), Is.EqualTo(new[] { 0, 4, 8 }));
        Assert.Multiple(() => {
            foreach (var field in vertex.Fields)
            {
                Assert.That(field.Type, Is.EqualTo(PrimitiveType.Float32));
                Assert.That(field.Size, Is.EqualTo(4));
                Assert.That(field.IsPrivate, Is.False);
                Assert.That(field.IsInternal, Is.False);
                Assert.That(field.IsMutable, Is.False);
                Assert.That(field.RequiresUnsafeCaller, Is.False);
            }
        });

        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue3 = main.Body.Statements[0];
        Assert.That(__nunitAssertValue3, Is.TypeOf<BoundLocal>());
        var vertices = ((BoundLocal)__nunitAssertValue3);
        Assert.That(main.Variables[vertices.Variable].Type, Is.EqualTo(PrimitiveType.NativeFixedArray));
        var __nunitAssertValue4 = vertices.Initializer;
        Assert.That(__nunitAssertValue4, Is.TypeOf<BoundNativeFixedArrayInitialization>());
        var array = ((BoundNativeFixedArrayInitialization)__nunitAssertValue4);
        Assert.That(array.Layout.ByteExtent, Is.EqualTo(36));
        Assert.That(array.Layout.ElementType, Is.EqualTo(vertexType));
        Assert.That(array.Elements.Length, Is.EqualTo(3));
        var __nunitAssertValue5 = main.Body.Statements[1];
        Assert.That(__nunitAssertValue5, Is.TypeOf<BoundLocal>());
        var description = ((BoundLocal)__nunitAssertValue5);
        var __nunitAssertValue6 = main.Body.Statements[2];
        Assert.That(__nunitAssertValue6, Is.TypeOf<BoundLocal>());
        var initialData = ((BoundLocal)__nunitAssertValue6);
        var __nunitAssertValue7 = description.Initializer;
        Assert.That(__nunitAssertValue7, Is.TypeOf<BoundNativeStorageRecordInitialization>());
        var buffer = ((BoundNativeStorageRecordInitialization)__nunitAssertValue7);
        var __nunitAssertValue8 = initialData.Initializer;
        Assert.That(__nunitAssertValue8, Is.TypeOf<BoundNativeStorageRecordInitialization>());
        var subresource = ((BoundNativeStorageRecordInitialization)__nunitAssertValue8);
        Assert.That(buffer.Layout.ByteExtent, Is.EqualTo(NativeDx11BufferContract.BufferSize));
        Assert.That(buffer.Layout.Alignment, Is.EqualTo(NativeDx11BufferContract.BufferAlignment));
        Assert.That(subresource.Layout.ByteExtent, Is.EqualTo(NativeDx11BufferContract.SubresourceSize));
        Assert.That(subresource.Layout.Alignment, Is.EqualTo(NativeDx11BufferContract.SubresourceAlignment));
        var __nunitAssertValue9 = buffer.DataSource;
        Assert.That(__nunitAssertValue9, Is.TypeOf<BoundNativeStorageAddress>());
        var bufferSource = ((BoundNativeStorageAddress)__nunitAssertValue9);
        var __nunitAssertValue10 = subresource.DataSource;
        Assert.That(__nunitAssertValue10, Is.TypeOf<BoundNativeStorageAddress>());
        var subresourceSource = ((BoundNativeStorageAddress)__nunitAssertValue10);
        Assert.That(bufferSource.CarrierType, Is.EqualTo(PrimitiveType.NativeFixedArray));
        Assert.That(bufferSource.ElementCount, Is.EqualTo(NativeSampleArrayContract.VertexArrayCount));
        Assert.That(bufferSource.ElementStride, Is.EqualTo(NativeSampleArrayContract.VertexStride));
        Assert.That(bufferSource.ByteExtent, Is.EqualTo(NativeSampleArrayContract.VertexArraySize));
        Assert.That(bufferSource.IsReadonly, Is.True);
        Assert.That(bufferSource.Type, Is.EqualTo(PrimitiveType.NativeStorageAddress));
        Assert.That(bufferSource.Variable, Is.EqualTo(vertices.Variable));
        Assert.That(subresourceSource.Variable, Is.EqualTo(vertices.Variable));
        Assert.That(bufferSource.ElementType, Is.EqualTo(vertexType));
        Assert.That(subresourceSource.ElementType, Is.EqualTo(vertexType));
        var __nunitAssertValue11 = bufferSource.Index;
        Assert.That(__nunitAssertValue11, Is.TypeOf<BoundConstant>());
        Assert.That((((BoundConstant)__nunitAssertValue11)).Value, Is.EqualTo(0));
        Assert.That(bufferSource.Index.Type, Is.EqualTo(PrimitiveType.Int32));
        var __nunitAssertValue12 = subresourceSource.Index;
        Assert.That(__nunitAssertValue12, Is.TypeOf<BoundConstant>());
        Assert.That((((BoundConstant)__nunitAssertValue12)).Value, Is.EqualTo(0));
        Assert.That(subresourceSource.Index.Type, Is.EqualTo(PrimitiveType.Int32));
        var expectedLoan = new NativeStorageLoan(vertices.Variable,
            PrimitiveType.NativeFixedArray, vertexType, true, NativeSampleArrayContract.VertexArraySize);
        Assert.That(buffer.Loan, Is.EqualTo(expectedLoan));
        Assert.That(subresource.Loan, Is.EqualTo(expectedLoan));
        Assert.That(buffer.Fields, Is.All.TypeOf<BoundConstant>());
        Assert.That(buffer.Fields.Select(static field => ((BoundConstant)field).Value),
            Is.EqualTo(new long[] { byteWidth, 1, 1, 0, 0, 0 }));
        Assert.That(subresource.Fields, Is.All.TypeOf<BoundConstant>());
        Assert.That(subresource.Fields.Select(static field => ((BoundConstant)field).Value),
            Is.EqualTo(new long[] { 0, 0 }));
        Assert.That(main.Variables[description.Variable]
            .NativeStorageRecordFacts?.BackingExtent, Is.EqualTo((uint)byteWidth));
        _ = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
    }

    [TestCase("0")]
    [TestCase("37")]
    [TestCase("4294967295")]
    public void SampleArrayBufferDescriptorsRejectWidthsOutsideTheBackingExtent(string byteWidth)
    {
        var compilation = CompileSampleArrayBuffer(
            SampleArrayBufferInitializers(byteWidth) + "return " + BufferDescriptorCall + ";");
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("public float32 X;", "float32 X;")]
    [TestCase("public float32 X;", "public mut float32 X;")]
    public void SampleArrayBufferAcceptsCataloguedVertexFieldModifiers(string oldField, string newField)
    {
        var declarations = SampleArrayBufferDeclarations.Replace(oldField, newField, StringComparison.Ordinal);
        var compilation = CompileSampleArrayBuffer(
            SampleArrayBufferInitializers() + "return " + BufferDescriptorCall + ";", declarations: declarations);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        _ = Assert.That(compilation.Program, Is.TypeOf<BoundProgram>());
    }

    [Test]
    public void SampleArrayBufferAcceptsCataloguedVertexFieldRename()
    {
        var declarations = SampleArrayBufferDeclarations
            .Replace("public float32 Z;", "public float32 W;", StringComparison.Ordinal)
            .Replace("Z = z;", "W = z;", StringComparison.Ordinal);
        var compilation = CompileSampleArrayBuffer(
            SampleArrayBufferInitializers() + "return " + BufferDescriptorCall + ";", declarations: declarations);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        _ = Assert.That(compilation.Program, Is.TypeOf<BoundProgram>());
    }

    [Test]
    public void SampleArrayBufferRejectsElementLayoutsOutsideTheCatalog()
    {
        var declarations = SampleArrayBufferDeclarations.Replace("public float32 Z;", "public float64 Z;",
            StringComparison.Ordinal);
        var compilation = CompileSampleArrayBuffer(
            SampleArrayBufferInitializers() + "return " + BufferDescriptorCall + ";", declarations: declarations);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [Test]
    public void SampleArrayBufferRejectsOtherPublicVertexNamespacesAndTheOlderArrayGate()
    {
        var otherNamespace = SampleArrayBufferDeclarations.Replace("namespace Triangle;", "namespace Other;",
            StringComparison.Ordinal);
        var wrongNamespace = CompileSampleArrayBuffer(
            SampleArrayBufferInitializers() + "return " + BufferDescriptorCall + ";",
            declarations: otherNamespace);
        Assert.That(wrongNamespace.Program, Is.Null);
        Assert.That(wrongNamespace.Diagnostics, Is.Not.Empty);

        var oldGateSource = """
            namespace Triangle;
            [Layout(Sequential)]
            public struct Vertex {
                public float32 X;
                public float32 Y;
                public float32 Z;
                public Vertex(float32 x, float32 y, float32 z) { X = x; Y = y; Z = z; }
            }
            int32 Main() {
                InlineArray<Vertex, 3> vertices = [
                    new Vertex(0, 0, 0), new Vertex(1, 1, 1), new Vertex(2, 2, 2)];
                return 0;
            }
            """;
        var oldGate = CompileSampleArraySource(oldGateSource);
        Assert.That(oldGate.Program, Is.Null);
        Assert.That(oldGate.Diagnostics, Is.Not.Empty);
    }

    [Test]
    public void SampleArrayBufferCatalogIsClosedAndDoesNotFollowGenericDescriptorImports()
    {
        var catalog = CreateSampleArrayBufferCatalog();
        Assert.That(catalog.SupportsDx11SampleArrayBufferInitialization, Is.True);
        Assert.That(NativeImportCatalog.WindowsX64.SupportsDx11SampleArrayBufferInitialization, Is.False);
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            NativeDx11BufferContract.SampleArrayCatalogIdentity, []));
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            NativeDx11BufferContract.SampleArrayCatalogIdentity, [CreateBufferDescriptorImportDefinition()]));
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            NativeDx11BufferContract.SampleArrayCatalogIdentity, [CreateBufferDescriptorImportDefinition()],
            fixedArrayStorageLayouts: [
                NativeDx11BufferContract.SampleArrayStorageLayouts[0] with { ByteExtent = 24 },
            ], storageRecordLayouts: NativeDx11BufferContract.SampleArrayStorageRecordLayouts));
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            NativeDx11BufferContract.SampleArrayCatalogIdentity,
            [CreateSampleArrayBufferDescriptorImportDefinition(), CreateVertex3ImportDefinition()],
            fixedArrayStorageLayouts: NativeDx11BufferContract.SampleArrayStorageLayouts,
            storageRecordLayouts: NativeDx11BufferContract.SampleArrayStorageRecordLayouts));

        var descriptorOnly = NativeImportCatalog.CreateForTesting("test-descriptor-only",
            [CreateSampleArrayBufferDescriptorImportDefinition()],
            storageRecordLayouts: NativeDx11BufferContract.SampleArrayStorageRecordLayouts);
        var compilation = CompileSampleArrayBuffer(
            SampleArrayBufferInitializers() + "return " + BufferDescriptorCall + ";", catalog: descriptorOnly);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [Test]
    public void SampleArrayBufferCatalogRejectsRetaggedLegacyVertex3Backing()
    {
        var vertexDeclaration = Vertex3Declarations[..Vertex3Declarations.IndexOf(
            "[Terrum.Interop.NativeImport", StringComparison.Ordinal)];
        var declarations = vertexDeclaration + BufferDescriptorDeclaration;
        var compilation = CompileBufferDescriptors(
            Vertex3Initializer + BufferDescriptorInitializer + "return " + BufferDescriptorCall + ";",
            declarations: declarations);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue15 = compilation.Program;
        Assert.That(__nunitAssertValue15, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue15);
        var combinedCatalog = CreateSampleArrayBufferCatalog();
        var retagged = program with {
            NativeImportCatalog = combinedCatalog,
            NativeImportCatalogId = combinedCatalog.Identity,
        };

        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(retagged));
        AssertVerifierRejects(retagged);
    }

    [Test]
    public void SampleArrayBufferRejectsSourceAddressEscapeAndNonCombinedTargets()
    {
        foreach (var source in new[] {
            "Terrum.Interop.NativeStorageAddress pointer = &vertices[0]; return 0;",
            "return NativeFfiBufferInitialization(&vertices[0], 324478056, 81985529216486895, "
                + "-1234567890123, 610800471, &initialData, 0);",
            "Terrum.Interop.Dx11.SubresourceData copy = subresourcedata(&vertices[0], 1, 0); return 0;",
            "Terrum.Interop.Dx11.SubresourceData copy = subresourcedata(&vertices[1], 0, 0); return 0;",
            "Terrum.Interop.Dx11.SubresourceData copy = subresourcedata(&vertices[VertexIndex(0)], 0, 0); return 0;",
            "Terrum.Interop.Dx11.SubresourceData copy = subresourcedata((ptr<void>)&vertices[0], 0, 0); return 0;",
            "Terrum.Interop.Dx11.SubresourceData copy = subresourcedata(&vertices[0] + 1, 0, 0); return 0;",
        })
        {
            var compilation = CompileSampleArrayBuffer(SampleArrayBufferInitializers() + source);
            Assert.That(compilation.Program, Is.Null);
            Assert.That(compilation.Diagnostics, Is.Not.Empty);
        }

        var checkedPolicy = CompileSampleArrayBuffer(
            SampleArrayBufferInitializers() + "return " + BufferDescriptorCall + ";",
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(checkedPolicy.Program, Is.Null);
    }

    [Test]
    public void SampleArrayBufferMandatoryVerifierRechecksLoanFactsScopeAndCatalog()
    {
        var compilation = CompileSampleArrayBuffer(
            SampleArrayBufferInitializers() + "return " + BufferDescriptorCall + ";");
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue16 = compilation.Program;
        Assert.That(__nunitAssertValue16, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue16);
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue17 = main.Body.Statements[0];
        Assert.That(__nunitAssertValue17, Is.TypeOf<BoundLocal>());
        var arrayLocal = ((BoundLocal)__nunitAssertValue17);
        var __nunitAssertValue18 = main.Body.Statements[1];
        Assert.That(__nunitAssertValue18, Is.TypeOf<BoundLocal>());
        var description = ((BoundLocal)__nunitAssertValue18);
        var __nunitAssertValue19 = main.Body.Statements[2];
        Assert.That(__nunitAssertValue19, Is.TypeOf<BoundLocal>());
        var initialData = ((BoundLocal)__nunitAssertValue19);
        var __nunitAssertValue20 = description.Initializer;
        Assert.That(__nunitAssertValue20, Is.TypeOf<BoundNativeStorageRecordInitialization>());
        var buffer = ((BoundNativeStorageRecordInitialization)__nunitAssertValue20);
        var __nunitAssertValue21 = initialData.Initializer;
        Assert.That(__nunitAssertValue21, Is.TypeOf<BoundNativeStorageRecordInitialization>());
        var subresource = ((BoundNativeStorageRecordInitialization)__nunitAssertValue21);
        var __nunitAssertValue22 = subresource.DataSource;
        Assert.That(__nunitAssertValue22, Is.TypeOf<BoundNativeStorageAddress>());
        var source = ((BoundNativeStorageAddress)__nunitAssertValue22);

        AssertVerifierRejects(ReplaceStatement(program,
            initialData with {
                Initializer = subresource with {
                    Loan = subresource.Loan! with { CarrierType = PrimitiveType.NativeDx11BufferDesc },
                }
            }));
        AssertVerifierRejects(ReplaceStatement(program,
            initialData with {
                Initializer = subresource with {
                    DataSource = source with { ElementType = (PrimitiveType)101 },
                    Loan = subresource.Loan! with { ElementType = (PrimitiveType)101 },
                }
            }));
        AssertVerifierRejects(ReplaceStatement(program,
            initialData with {
                Initializer = subresource with {
                    Loan = subresource.Loan! with { BackingVariable = initialData.Variable },
                }
            }));

        var leaked = new BoundExpressionStatement(source, source.Span);
        var leakedMain = main with {
            Body = main.Body with {
                Statements = main.Body.Statements.Insert(3, leaked),
            }
        };
        AssertVerifierRejects(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, leakedMain),
        });

        var retiredMain = main with {
            Body = main.Body with {
                Statements = main.Body.Statements.Insert(3,
                new BoundLifetimeEnd(arrayLocal.Variable, -1, source.Span)),
            }
        };
        AssertVerifierRejects(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, retiredMain),
        });

        var scopedMain = main with {
            Body = main.Body with {
                Statements = main.Body.Statements.SetItem(0,
                new BoundBlock([arrayLocal], arrayLocal.Span) { IsManagedLexicalScope = true }),
            }
        };
        AssertVerifierRejects(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, scopedMain),
        });

        var stripped = program with {
            NativeImportCatalog = null,
            NativeImportCatalogId = null,
            Functions = program.Functions.Select(static function =>
                function with { NativeImportKey = null }).ToImmutableArray(),
        };
        AssertVerifierRejects(stripped);
        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() => NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
    }

    private static async Task ValidateSampleArrayBufferDescriptorRuntimeAsync(string directory)
    {
        foreach (var byteWidth in new[] { 1, 35, 36 })
        {
            foreach (var fail in new[] { 0, 1 })
            {
                var expectedStatus = fail == 0 ? 0 : 73;
                var call = BufferDescriptorCall.Replace("&initialData, 0", $"&initialData, {fail}",
                    StringComparison.Ordinal);
                var body = SampleArrayBufferInitializers(byteWidth)
                    + $"return {call} == {expectedStatus} ? 0 : 1;";
                var compilation = CompileSampleArrayBuffer(body);
                Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
                var path = Path.Combine(directory, $"sample-array-buffer-{byteWidth}-{fail}.exe");
                var __nunitAssertValue23 = compilation.Program;
                Assert.That(__nunitAssertValue23, Is.TypeOf<BoundProgram>());
                await File.WriteAllBytesAsync(path, NativeEmitter.EmitWithNativeImportsForTesting(
                                    ((BoundProgram)__nunitAssertValue23), NativeRuntimePolicy.FailFast))
                                    .ConfigureAwait(false);
                using var process = Process.Start(new ProcessStartInfo(path) {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = directory,
                }) ?? throw new InvalidOperationException("Sample-array buffer executable did not start.");
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
