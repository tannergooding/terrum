// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private const string CreateBufferVertexDeclaration = """
        [Layout(Sequential)]
        struct Vertex {
            float32 X;
            float32 Y;
            float32 Z;
            public Vertex(float32 x, float32 y, float32 z) {
                X = x;
                Y = y;
                Z = z;
            }
        }
        """;

    private static NativeImportCatalog CreateDx11CreateBufferFixtureCatalog() =>
        NativeImportCatalog.CreateForTesting(NativeDx11CreateBufferFixtureContract.CatalogIdentity, [
            CreateVertex3ImportDefinition(),
            .. NativeDx11CreateBufferFixtureContract.Imports,
        ], storageRecordLayouts: NativeDx11BufferContract.SampleArrayStorageRecordLayouts);

    private static Compilation CompileDx11CreateBufferFixture(string body,
        string? declarations = null, NativeImportCatalog? catalog = null,
        BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast) =>
        Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse((declarations ?? CreateBufferVertexDeclaration)
                + "\nint32 Main() {\n" + body + "\n}")],
            catalog ?? CreateDx11CreateBufferFixtureCatalog(), policy);

    private static string CreateBufferFixtureBody() =>
        Vertex3Initializer + BufferDescriptorInitializer
        + "return createbufferfixture(0, &description, &initialData);";

    [Test]
    public void Dx11CreateBufferFixtureBindsOnlyClosedDescriptorAndBorrowedReceiverContracts()
    {
        var compilation = CompileDx11CreateBufferFixture(CreateBufferFixtureBody());
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue0);
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue1 = main.Body.Statements[3];
        Assert.That(__nunitAssertValue1, Is.TypeOf<BoundReturn>());
        var __nunitAssertValue2 = (((BoundReturn)__nunitAssertValue1)).Value;
        Assert.That(__nunitAssertValue2, Is.TypeOf<BoundNativeDx11CreateBufferFixture>());
        var gate = ((BoundNativeDx11CreateBufferFixture)__nunitAssertValue2);
        Assert.That(NativeDx11CreateBufferFixtureContract.MatchesGate(gate, program), Is.True);
        Assert.That(gate.Mode, Is.EqualTo(0));
        Assert.That(gate.Description.Type, Is.EqualTo(PrimitiveType.NativeDx11BufferDescAddress));
        Assert.That(gate.InitialData.Type, Is.EqualTo(PrimitiveType.NativeDx11SubresourceDataAddress));
        Assert.That(gate.CreateBuffer.Slot, Is.EqualTo(3));
        Assert.That(gate.CreateBuffer.ReturnType, Is.EqualTo(NativeDx11BufferAbiType.SignedHResult));
        Assert.That(gate.Release.Slot, Is.EqualTo(2));
        Assert.That(gate.Release.ReturnType, Is.EqualTo(NativeDx11BufferAbiType.UnsignedUlong));
        Assert.That(gate.Release.Effect, Is.EqualTo(NativeDx11BufferMethodEffect.ConsumeOneOwnedOutputReference));
        Assert.That(main.Variables[gate.InitialData.Variable].NativeStorageRecordFacts?.Loan, Is.EqualTo(main.Variables[gate.Description.Variable].NativeStorageRecordFacts?.Loan));
        Assert.That(NativeImportCatalog.WindowsX64.SupportsDx11CreateBufferFixture, Is.False);

        var image = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
        foreach (var definition in NativeDx11CreateBufferFixtureContract.Imports)
        {
            Assert.That(image.AsSpan().IndexOf(Encoding.ASCII.GetBytes(definition.ExportName + "\0")) >= 0, Is.True);
        }
        Assert.That(CountCreateBufferSequence(image, [0xFF, 0x50, 0x18]), Is.EqualTo(1));
        Assert.That(CountCreateBufferSequence(image, [0xFF, 0x50, 0x10]), Is.EqualTo(1));
        Assert.That(image.AsSpan().IndexOf("D3D11CreateDevice\0"u8) < 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("D3DCompileFromFile\0"u8) < 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("QueryInterface\0"u8) < 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("AddRef\0"u8) < 0, Is.True);
    }

    private static int CountCreateBufferSequence(byte[] image, byte[] sequence)
    {
        var count = 0;
        var remaining = image.AsSpan();
        while (remaining.IndexOf(sequence) is var index && index >= 0)
        {
            count++;
            remaining = remaining[(index + sequence.Length)..];
        }
        return count;
    }

    [TestCase("return createbufferfixture();")]
    [TestCase("return createbufferfixture(3, &description, &initialData);")]
    [TestCase("return createbufferfixture(-1, &description, &initialData);")]
    [TestCase("return createbufferfixture(true, &description, &initialData);")]
    [TestCase("return createbufferfixture(mode, &description, &initialData);")]
    [TestCase("return createbufferfixture(0 + 0, &description, &initialData);")]
    [TestCase("return createbufferfixture((0), &description, &initialData);")]
    [TestCase("return createbufferfixture(mode: 0, &description, &initialData);")]
    [TestCase("return createbufferfixture(0, &initialData, &description);")]
    [TestCase("return createbufferfixture(0, description, &initialData);")]
    [TestCase("return createbufferfixture(0, &description, initialData);")]
    [TestCase("return createbufferfixture(0, &description, &description);")]
    [TestCase("return createbufferfixture(0, &vertices[0], &initialData);")]
    [TestCase("return createbufferfixture(0, &description, &initialData, 0);")]
    [TestCase("return createbufferfixture(0, &description, &initialData).Release();")]
    [TestCase("uint64 device = createbufferfixture(0, &description, &initialData); return 0;")]
    public void Dx11CreateBufferFixtureRejectsUnconfinedArgumentsAndResourceEscape(string body)
    {
        var compilation = CompileDx11CreateBufferFixture(Vertex3Initializer + BufferDescriptorInitializer + body);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [Test]
    public void Dx11CreateBufferFixtureRejectsMismatchedBackingAndExpiredLocals()
    {
        var otherVertices = Vertex3Initializer.Replace("vertices", "other", StringComparison.Ordinal);
        var mismatchedData = BufferDescriptorInitializer.Replace("subresourcedata(&vertices[0]",
            "subresourcedata(&other[0]", StringComparison.Ordinal);
        var mismatched = CompileDx11CreateBufferFixture(Vertex3Initializer + otherVertices + mismatchedData
            + "return createbufferfixture(0, &description, &initialData);");
        Assert.That(mismatched.Program, Is.Null);
        Assert.That(mismatched.Diagnostics, Is.Not.Empty);

        var ended = CompileDx11CreateBufferFixture(Vertex3Initializer + BufferDescriptorInitializer
            + "description = _; return createbufferfixture(0, &description, &initialData);");
        Assert.That(ended.Program, Is.Null);
        Assert.That(ended.Diagnostics, Is.Not.Empty);

        var scoped = CompileDx11CreateBufferFixture(Vertex3Initializer + "{ " + BufferDescriptorInitializer
            + "} return createbufferfixture(0, &description, &initialData);");
        Assert.That(scoped.Program, Is.Null);
        Assert.That(scoped.Diagnostics, Is.Not.Empty);
    }

    [TestCase("descriptor-loan")]
    [TestCase("subresource-loan")]
    [TestCase("descriptor-facts")]
    [TestCase("mode")]
    [TestCase("createbuffer-slot")]
    [TestCase("release-effect")]
    [TestCase("helper-key")]
    [TestCase("catalog-identity")]
    [TestCase("catalog-removed")]
    [TestCase("checked-policy")]
    public void Dx11CreateBufferFixtureMandatoryVerifierRejectsForgedContracts(string variant)
    {
        var compilation = CompileDx11CreateBufferFixture(CreateBufferFixtureBody());
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue3 = compilation.Program;
        Assert.That(__nunitAssertValue3, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue3);
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue4 = main.Body.Statements[3];
        Assert.That(__nunitAssertValue4, Is.TypeOf<BoundReturn>());
        var returned = ((BoundReturn)__nunitAssertValue4);
        var __nunitAssertValue5 = returned.Value;
        Assert.That(__nunitAssertValue5, Is.TypeOf<BoundNativeDx11CreateBufferFixture>());
        var gate = ((BoundNativeDx11CreateBufferFixture)__nunitAssertValue5);
        gate = variant switch {
            "descriptor-loan" => gate with {
                Description = gate.Description with { Variable = -1 },
            },
            "subresource-loan" => gate with {
                InitialData = gate.InitialData with { IsReadonly = false },
            },
            "mode" => gate with { Mode = 3 },
            "createbuffer-slot" => gate with {
                CreateBuffer = gate.CreateBuffer with { Slot = 2 },
            },
            "release-effect" => gate with {
                Release = gate.Release with { Effect = NativeDx11BufferMethodEffect.CreateOneOwnedOutputReference },
            },
            "helper-key" => gate with { ImportKeys = gate.ImportKeys.SetItem(0, "test.other") },
            _ => gate,
        };
        if (variant == "descriptor-facts")
        {
            var description = main.Variables[gate.Description.Variable];
            main = main with {
                Variables = main.Variables.SetItem(gate.Description.Variable, description with {
                    NativeStorageRecordFacts = description.NativeStorageRecordFacts!
                        with { BackingExtent = 35 },
                }),
            };
        }
        main = main with {
            Body = main.Body with { Statements = main.Body.Statements.SetItem(3, returned with { Value = gate }) },
        };
        program = program with {
            RuntimePolicy = variant == "checked-policy" ? BoundRuntimePolicy.CheckedExceptions : program.RuntimePolicy,
            NativeImportCatalog = variant == "catalog-removed" ? null : program.NativeImportCatalog,
            NativeImportCatalogId = variant == "catalog-identity" ? "test.other"
                : variant == "catalog-removed" ? null : program.NativeImportCatalogId,
            Functions = program.Functions.SetItem(program.EntryPoint, main),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(program));
        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast));
    }

    [Test]
    public void Dx11CreateBufferFixtureRejectsDescriptorLocalsMovedOutsideTheirLexicalScope()
    {
        var compilation = CompileDx11CreateBufferFixture(Vertex3Initializer + "{ " + BufferDescriptorInitializer
            + "return createbufferfixture(0, &description, &initialData); } return 0;");
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue6 = compilation.Program;
        Assert.That(__nunitAssertValue6, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue6);
        var main = program.Functions[program.EntryPoint];
        var root = main.Body;
        var __nunitAssertValue7 = root.Statements[1];
        Assert.That(__nunitAssertValue7, Is.TypeOf<BoundBlock>());
        var scoped = ((BoundBlock)__nunitAssertValue7);
        var __nunitAssertValue8 = scoped.Statements[2];
        Assert.That(__nunitAssertValue8, Is.TypeOf<BoundReturn>());
        var scopedReturn = ((BoundReturn)__nunitAssertValue8);
        var __nunitAssertValue9 = scopedReturn.Value;
        Assert.That(__nunitAssertValue9, Is.TypeOf<BoundNativeDx11CreateBufferFixture>());
        var gate = ((BoundNativeDx11CreateBufferFixture)__nunitAssertValue9);
        var movedScope = scoped with { Statements = scoped.Statements.RemoveAt(2) };
        var movedRoot = root with {
            Statements = root.Statements.SetItem(1, movedScope).SetItem(2,
                new BoundReturn(gate, root.Statements[2].Span)),
        };
        program = program with {
            Functions = program.Functions.SetItem(program.EntryPoint, main with { Body = movedRoot }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(program));
    }

    [Test]
    public void Dx11CreateBufferFixtureRejectsEndedSiblingDescriptorsInsideViewReturn()
    {
        var probe = $$"""
            int32 Probe(OwnedInt32Buffer owner) {
                {{Vertex3Initializer}}
                {{BufferDescriptorInitializer}}
                ReadonlyInt32BufferView view = owner.Slice(0, 1);
                return createbufferfixture(0, &description, &initialData);
            }
            """;
        var compilation = CompileDx11CreateBufferFixture("return 0;",
            CreateBufferVertexDeclaration + probe);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue10 = compilation.Program;
        Assert.That(__nunitAssertValue10, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue10);
        var __nunitAssertValue11 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static item => item.Name == "Probe"));
        Assert.That(__nunitAssertValue11, Has.Exactly(1).Items);
        var function = __nunitAssertValue11[0];
        var descriptorLocals = ImmutableArray.CreateBuilder<BoundLocal>();
        CollectDescriptorLocals(function.Body);
        Assert.That(descriptorLocals.Count, Is.EqualTo(2));
        var descriptorVariables = descriptorLocals.Select(static local => local.Variable).ToArray();
        var inserted = false;

        BoundStatement Rewrite(BoundStatement statement) => statement switch {
            BoundBlock block => RewriteBlock(block),
            BoundIf conditional => conditional with {
                Then = Rewrite(conditional.Then),
                Else = conditional.Else is null ? null : Rewrite(conditional.Else),
            },
            BoundWhile loop => loop with {
                Body = Rewrite(loop.Body),
                Iterator = loop.Iterator is null ? null : Rewrite(loop.Iterator),
            },
            BoundTryFinally cleanup => cleanup with {
                Body = RewriteBlock(cleanup.Body),
                Finally = RewriteBlock(cleanup.Finally),
            },
            BoundViewScope view => view with { Body = RewriteBlock(view.Body) },
            BoundTryCatch recovery => recovery with {
                Body = RewriteBlock(recovery.Body),
                Catch = RewriteBlock(recovery.Catch),
                Finally = recovery.Finally is null ? null : RewriteBlock(recovery.Finally),
            },
            BoundUnsafe unsafeBlock => unsafeBlock with { Body = RewriteBlock(unsafeBlock.Body) },
            _ => statement,
        };

        BoundBlock RewriteBlock(BoundBlock block)
        {
            var rewritten = ImmutableArray.CreateBuilder<BoundStatement>();
            foreach (var statement in block.Statements)
            {
                var child = Rewrite(statement);
                if (child is BoundLocal local && Array.IndexOf(descriptorVariables, local.Variable) >= 0)
                {
                    continue;
                }
                if (child is BoundViewScope view)
                {
                    Assert.That(inserted, Is.False);
                    Assert.That(view.Body.Statements, Has.Some.Matches(static child => child is BoundReturn { Value: BoundNativeDx11CreateBufferFixture }));
                    rewritten.Add(new BoundBlock(descriptorLocals.ToImmutable()
                        .Cast<BoundStatement>().ToImmutableArray(), descriptorLocals[0].Span));
                    inserted = true;
                }
                rewritten.Add(child);
            }
            return block with { Statements = rewritten.ToImmutable() };
        }

        void CollectDescriptorLocals(BoundStatement statement)
        {
            switch (statement)
            {
                case BoundBlock block:
                    foreach (var child in block.Statements)
                    {
                        CollectDescriptorLocals(child);
                    }
                    break;
                case BoundLocal local when NativeDx11BufferContract.IsCarrier(function.Variables[local.Variable].Type)
                    || function.Variables[local.Variable].Type == PrimitiveType.NativeStorageRecord:
                    descriptorLocals.Add(local);
                    break;
                case BoundIf conditional:
                    CollectDescriptorLocals(conditional.Then);
                    if (conditional.Else is not null)
                    {
                        CollectDescriptorLocals(conditional.Else);
                    }
                    break;
                case BoundWhile loop:
                    CollectDescriptorLocals(loop.Body);
                    if (loop.Iterator is not null)
                    {
                        CollectDescriptorLocals(loop.Iterator);
                    }
                    break;
                case BoundTryFinally cleanup:
                    CollectDescriptorLocals(cleanup.Body);
                    CollectDescriptorLocals(cleanup.Finally);
                    break;
                case BoundViewScope view:
                    CollectDescriptorLocals(view.Body);
                    break;
                case BoundTryCatch recovery:
                    CollectDescriptorLocals(recovery.Body);
                    CollectDescriptorLocals(recovery.Catch);
                    if (recovery.Finally is not null)
                    {
                        CollectDescriptorLocals(recovery.Finally);
                    }
                    break;
                case BoundUnsafe unsafeBlock:
                    CollectDescriptorLocals(unsafeBlock.Body);
                    break;
            }
        }

        var forged = program with {
            Functions = program.Functions.SetItem(program.Functions.IndexOf(function),
                function with { Body = (BoundBlock)Rewrite(function.Body) }),
        };
        Assert.That(inserted, Is.True);
        var verificationError = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(forged));
        Assert.That(verificationError.Data["Terrum.InvalidEffect"], Is.EqualTo("NativeImport"));
        var emissionError = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImportsForTesting(forged, NativeRuntimePolicy.FailFast));
        Assert.That(emissionError.Data["Terrum.InvalidEffect"], Is.EqualTo("NativeImport"));
    }

    [Test]
    public void Dx11CreateBufferFixtureReservesPrivateAcquisitionAndCheckerContracts()
    {
        var imports = ImmutableArray.Create(CreateVertex3ImportDefinition())
            .AddRange(NativeDx11CreateBufferFixtureContract.Imports);
        foreach (var definition in NativeDx11CreateBufferFixtureContract.Imports)
        {
            foreach (var invalid in new[] {
                definition with { Key = "test.other" },
                definition with { DllName = "other.dll" },
                definition with { DllName = NativeDx11CreateBufferFixtureContract.DllName.ToUpperInvariant() },
                definition with { ExportName = "OtherExport" },
                definition with { ReturnType = PrimitiveType.Int64 },
                definition with { ParameterTypes = default },
                definition with { ParameterTypes = [PrimitiveType.UInt64] },
                definition with { ParameterContracts = default },
                definition with { IsNoThrow = false },
                definition with { RequiresUnsafeCaller = true },
                definition with { DoesNotReturn = true },
                definition with { DeploymentPolicy = NativeImportDeploymentPolicy.System32 },
            })
            {
                _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
                    NativeDx11CreateBufferFixtureContract.CatalogIdentity,
                    imports.Replace(definition, invalid),
                    fixedArrayStorageLayouts: NativeDx11BufferContract.SampleArrayStorageLayouts,
                    storageRecordLayouts: NativeDx11BufferContract.SampleArrayStorageRecordLayouts));
            }
            var alias = new NativeImportDefinition("test.alias",
                NativeDx11CreateBufferFixtureContract.DllName.ToUpperInvariant(), definition.ExportName,
                [], PrimitiveType.Int32, true, true, false,
                NativeImportDeploymentPolicy.ApplicationDirectory);
            _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
                NativeDx11CreateBufferFixtureContract.CatalogIdentity, imports.Add(alias),
                fixedArrayStorageLayouts: NativeDx11BufferContract.SampleArrayStorageLayouts,
                storageRecordLayouts: NativeDx11BufferContract.SampleArrayStorageRecordLayouts));
        }

        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting("test.other", imports));
        _ = Assert.Throws<ArgumentException>(() => new NativeImportCatalog(
            NativeDx11CreateBufferFixtureContract.CatalogIdentity, imports));

        foreach (var definition in NativeDx11CreateBufferFixtureContract.Imports)
        {
            var result = definition.ReturnType == PrimitiveType.UInt64 ? "uint64" : "int32";
            var parameters = definition.ExportName == "NativeFfiAcquireCreateBufferDevice"
                ? "uint32 mode"
                : "uint32 mode, uint64 device, uint64 description, uint64 initialData, int32 result, uint64 output";
            var declaration = $$"""
                [Terrum.Interop.NativeImport("{{definition.Key}}")]
                [NoThrow] extern {{result}} PrivateCreateBufferHelper({{parameters}});
                """;
            var compilation = CompileDx11CreateBufferFixture("return 0;",
                CreateBufferVertexDeclaration + SampleArrayBufferDescriptorDeclaration + declaration);
            Assert.That(compilation.Program, Is.Null);
            Assert.That(compilation.Diagnostics, Has.Some.Matches(static diagnostic => diagnostic.Code == "TRM3030"));
        }
    }

    [Test]
    public void Dx11CreateBufferFixtureRemainsTestOnlyFailFastPeAot()
    {
        var compilation = CompileDx11CreateBufferFixture(CreateBufferFixtureBody());
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue12 = compilation.Program;
        Assert.That(__nunitAssertValue12, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue12);
        Assert.That(CompileDx11CreateBufferFixture(CreateBufferFixtureBody(),
            catalog: NativeImportCatalog.WindowsX64).Program, Is.Null);
        Assert.That(CompileDx11CreateBufferFixture(CreateBufferFixtureBody(),
            catalog: CreateShaderCompileCatalog()).Program, Is.Null);
        Assert.That(CompileDx11CreateBufferFixture(CreateBufferFixtureBody(),
            policy: BoundRuntimePolicy.CheckedExceptions).Program, Is.Null);
        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() =>
            NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() =>
            NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() =>
            ModuleCompiler.CreateProducer(program with { EntryPoint = -1 }, ["Main"]));
        _ = Assert.Throws<NotSupportedException>(() =>
            NativeModuleCompiler.CreateProducer(program with { EntryPoint = -1 }, ["Main"],
                "createbufferfixture", NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(program with { EntryPoint = -1 }, "createbufferfixture"));
        _ = Assert.Throws<NotSupportedException>(() =>
            NativeEmitter.EmitLibrary(program with { EntryPoint = -1 }, ["Main"],
                NativeRuntimePolicy.FailFast));
        Assert.That(NativeImportCatalog.WindowsX64.TryGetImport(
            NativeDx11CreateBufferFixtureContract.AcquireKey, out _), Is.False);
        Assert.That(NativeImportCatalog.WindowsX64.TryGetImport(
            NativeDx11CreateBufferFixtureContract.CheckKey, out _), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Dx11CreateBufferFixtureChecksSdkAbiAndCleansEveryOwnedOutputAsync(bool sampleArray)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var root = FindRepositoryRoot();
        var environment = await FindNativeFixtureEnvironmentAsync(root).ConfigureAwait(false);
        var directory = Directory.CreateTempSubdirectory("terrum-dx11-create-buffer-").FullName;
        var fixture = Path.Combine(root, "tests", "native", "native-dx11-create-buffer-probe.c");
        var sdk = Path.Combine(root, "tests", "native", "dx11-sdk-layout-probe.c");
        var assembly = Path.Combine(directory, "dx11-sdk.asm");
        var comspec = Environment.GetEnvironmentVariable("ComSpec")
            ?? throw new InvalidOperationException("ComSpec is unset.");
        try
        {
            _ = await RunAsync(new ProcessStartInfo(comspec) {
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /wd4201 /O2 /MT /LD /TC /Fo\"{directory}\\fixture.obj\" /Fe\"{directory}\\{NativeDx11CreateBufferFixtureContract.DllName}\" \"{fixture}\" /link /incremental:no && cl /nologo /W4 /WX /wd4201 /O2 /MT /TC /DNATIVE_DX11_CREATE_BUFFER_FIXTURE_SELF_TEST /Fo\"{directory}\\selftest.obj\" /Fe\"{directory}\\selftest.exe\" \"{fixture}\" /link /incremental:no && cl /nologo /W4 /WX /wd4201 /Od /TC /c /FAs /Fa\"{assembly}\" /Fo\"{directory}\\sdk.obj\" \"{sdk}\"\"",
                WorkingDirectory = directory,
            }, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            _ = await RunAsync(new ProcessStartInfo(Path.Combine(directory, "selftest.exe")) {
                WorkingDirectory = directory,
            }, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            var listing = await File.ReadAllTextAsync(assembly).ConfigureAwait(false);
            var start = listing.IndexOf("NativeFfiCreateBufferSdkSignatures PROC", StringComparison.Ordinal);
            Assert.That(start >= 0, Is.True, listing);
            var end = listing.IndexOf("NativeFfiCreateBufferSdkSignatures ENDP", start, StringComparison.Ordinal);
            Assert.That(end > start, Is.True, listing);

            var initializers = sampleArray
                ? SampleArrayBufferInitializers()
                : Vertex3Initializer + BufferDescriptorInitializer;
            var declarations = (sampleArray
                ? SampleArrayCreateBufferDeclarations
                : CreateBufferVertexDeclaration) + $$"""
                int32 Success() {
                    {{initializers}}
                    return createbufferfixture(0, &description, &initialData);
                }
                int32 FailureWithoutOutput() {
                    {{initializers}}
                    return createbufferfixture(1, &description, &initialData);
                }
                int32 FailureWithOwnedOutput() {
                    {{initializers}}
                    return createbufferfixture(2, &description, &initialData);
                }
                int32 Main() {
                    mut int32 repeat = 0;
                    while (repeat < 32) {
                        if (Success() != 0) { return 1; }
                        if (FailureWithoutOutput() != 0) { return 2; }
                        if (FailureWithOwnedOutput() != 0) { return 3; }
                        repeat++;
                    }
                    return 0;
                }
                """;
            var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(declarations)],
                sampleArray ? CreateSampleArrayCreateBufferFixtureCatalog() : CreateDx11CreateBufferFixtureCatalog(),
                BoundRuntimePolicy.FailFast);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var path = Path.Combine(directory, "dx11-create-buffer-fixture.exe");
            var __nunitAssertValue13 = compilation.Program;
            Assert.That(__nunitAssertValue13, Is.TypeOf<BoundProgram>());
            await File.WriteAllBytesAsync(path, NativeEmitter.EmitWithNativeImportsForTesting(
                            ((BoundProgram)__nunitAssertValue13), NativeRuntimePolicy.FailFast)).ConfigureAwait(false);
            _ = await RunAsync(new ProcessStartInfo(path) {
                WorkingDirectory = directory,
            }, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
