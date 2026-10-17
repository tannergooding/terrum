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
    private static readonly string BlobAcquisitionDeclaration = $$"""
        [Terrum.Interop.NativeImport("{{NativeBlobFixtureContract.AcquisitionKey}}")]
        [NoThrow]
        extern Terrum.Interop.Dx11.BlobFixtureObject AcquireBlob();
        """;

    private static NativeImportDefinition CreateBlobAcquisitionDefinition() =>
        new(NativeBlobFixtureContract.AcquisitionKey, NativeBlobFixtureContract.DllName,
            NativeBlobFixtureContract.AcquisitionExport, [], PrimitiveType.NativeBlobFixtureObject,
            true, true, false, NativeImportDeploymentPolicy.ApplicationDirectory) {
            ResultContract = NativeImportResultContract.FixtureOwnedBlob,
        };

    private static NativeImportDefinition CreateBlobValidationDefinition() =>
        new(NativeBlobFixtureContract.ValidationKey, NativeBlobFixtureContract.DllName,
            NativeBlobFixtureContract.ValidationExport,
            [PrimitiveType.NativeBlobFixtureObject, PrimitiveType.NativeBlobBorrowedBytes, PrimitiveType.UInt64],
            PrimitiveType.Int32, true, true, false, NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [NativeImportParameterContract.FixtureOwnedBlobReceiver,
                NativeImportParameterContract.BorrowedBlobBytesNoEscape, NativeImportParameterContract.Value],
        };

    private static NativeImportCatalog CreateBlobCatalog() =>
        NativeImportCatalog.CreateForTesting("test-native-blob-getters-v1",
            [CreateBlobAcquisitionDefinition(), CreateBlobValidationDefinition()]);

    private static Compilation CompileBlob(string body = "return blobgetters(AcquireBlob());",
        string? declarations = null, BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast,
        NativeImportCatalog? catalog = null) => Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse((declarations ?? BlobAcquisitionDeclaration) + "\nint32 Main() {" + body + "}")],
            catalog ?? CreateBlobCatalog(), policy);

    [Test]
    public void BlobGettersBindAndEmitOnlyExactWindowsX64SlotsAndContracts()
    {
        var compilation = CompileBlob();
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue0);
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue1 = global::System.Linq.Enumerable.ToArray(main.Body.Statements);
        Assert.That(__nunitAssertValue1, Has.Exactly(1).Items);
        var __nunitAssertValue2 = __nunitAssertValue1[0];
        Assert.That(__nunitAssertValue2, Is.TypeOf<BoundReturn>());
        var result = ((BoundReturn)__nunitAssertValue2);
        var __nunitAssertValue3 = result.Value;
        Assert.That(__nunitAssertValue3, Is.TypeOf<BoundNativeBlobGetters>());
        var getters = ((BoundNativeBlobGetters)__nunitAssertValue3);
        Assert.That(main.Variables, Is.Empty);
        Assert.That(NativeBlobFixtureContract.MatchesGate(getters, program), Is.True);
        Assert.That(getters.PointerGetter.Slot, Is.EqualTo(3));
        Assert.That(getters.SizeGetter.Slot, Is.EqualTo(4));
        Assert.That(getters.PointerGetter.ReturnType, Is.EqualTo(PrimitiveType.NativeBlobBorrowedBytes));
        Assert.That(getters.PointerGetter.Result, Is.EqualTo(NativeBlobGetterResult.BorrowedBytesWhileFixtureLive));
        Assert.That(getters.SizeGetter.ReturnType, Is.EqualTo(PrimitiveType.UInt64));
        Assert.That(getters.SizeGetter.Result, Is.EqualTo(NativeBlobGetterResult.UnsignedNativeSize));
        Assert.That(getters.PointerGetter.Abi, Is.EqualTo(NativeBlobFixtureContract.Abi));
        Assert.That(getters.SizeGetter.Abi, Is.EqualTo(NativeBlobFixtureContract.Abi));
        var __nunitAssertValue4 = getters.Receiver;
        Assert.That(__nunitAssertValue4, Is.TypeOf<BoundCall>());
        var receiver = ((BoundCall)__nunitAssertValue4);
        Assert.That(receiver.Arguments, Is.Empty);
        Assert.That(program.Functions[receiver.Function].NativeImportKey, Is.EqualTo(NativeBlobFixtureContract.AcquisitionKey));
        var image = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
        Assert.That(image.AsSpan().IndexOf("NativeFfiAcquireBlob\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("NativeFfiCheckBlob\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf(new byte[] {
            0x48, 0x89, 0xC1, 0x48, 0x8B, 0x01, 0xFF, 0x50, 0x18,
        }) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf(new byte[] {
            0x48, 0x89, 0xC1, 0x48, 0x8B, 0x01, 0xFF, 0x50, 0x20, 0x49, 0x89, 0xC0,
        }) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("D3DCompiler"u8) < 0, Is.True);
    }

    [TestCase("return blobgetters();")]
    [TestCase("return blobgetters(AcquireBlob(), AcquireBlob());")]
    [TestCase("return blobgetters(receiver: AcquireBlob());")]
    [TestCase("return blobgetters(null);")]
    [TestCase("return blobgetters(0);")]
    [TestCase("return blobgetters(&AcquireBlob);")]
    [TestCase("return blobgetters(true ? AcquireBlob() : AcquireBlob());")]
    [TestCase("return blobgetters(AcquireBlob(), 3, 4);")]
    [TestCase("AcquireBlob(); return 0;")]
    [TestCase("Terrum.Interop.Dx11.BlobFixtureObject blob = AcquireBlob(); return blobgetters(blob);")]
    [TestCase("mut Terrum.Interop.Dx11.BlobFixtureObject blob = AcquireBlob(); return 0;")]
    [TestCase("init Terrum.Interop.Dx11.BlobFixtureObject blob; return 0;")]
    [TestCase("uint64 address = AcquireBlob(); return 0;")]
    [TestCase("return int32(AcquireBlob());")]
    [TestCase("return AcquireBlob().GetBufferSize();")]
    [TestCase("return AcquireBlob().QueryInterface();")]
    [TestCase("return AcquireBlob().AddRef();")]
    [TestCase("return AcquireBlob().Release();")]
    [TestCase("ptr<uint32> pointer = AcquireBlob(); return 0;")]
    public void BlobGettersRejectAlteredReceiversRawDispatchAndEscape(string body)
    {
        var compilation = CompileBlob(body);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("Terrum.Interop.Dx11.BlobFixtureObject Forward() { return AcquireBlob(); }")]
    [TestCase("int32 Forward(Terrum.Interop.Dx11.BlobFixtureObject receiver) { return 0; }")]
    [TestCase("struct Holder { Terrum.Interop.Dx11.BlobFixtureObject Value; }")]
    [TestCase("mut class Holder { Terrum.Interop.Dx11.BlobFixtureObject Value; }")]
    [TestCase("namespace Terrum.Interop.Dx11 { struct BlobFixtureObject { uint64 Address; } }")]
    [TestCase("namespace Terrum.Interop.Dx11 { mut class BlobFixtureObject { uint64 Address; } }")]
    public void BlobGettersRejectForwardingFieldsAndNominalSpoofing(string extraDeclaration)
    {
        var compilation = CompileBlob(declarations: BlobAcquisitionDeclaration + extraDeclaration);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("uint64", "")]
    [TestCase("Terrum.Interop.Dx11.BlobFixtureObject", "uint64 ignored")]
    [TestCase("Terrum.Interop.Dx11.BlobFixtureObject?", "")]
    [TestCase("write Terrum.Interop.Dx11.BlobFixtureObject", "")]
    public void BlobAcquisitionBindingRejectsAlteredSignatures(string returnType, string parameters)
    {
        var declaration = $$"""
            [Terrum.Interop.NativeImport("{{NativeBlobFixtureContract.AcquisitionKey}}")]
            [NoThrow]
            extern {{returnType}} AcquireBlob({{parameters}});
            """;
        var compilation = CompileBlob(declarations: declaration);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [Test]
    public void BlobGetterCodegenRechecksTheExactAcquisitionBinding()
    {
        var compilation = CompileBlob();
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue5 = compilation.Program;
        Assert.That(__nunitAssertValue5, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue5);
        var __nunitAssertValue6 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.IsExternal));
        Assert.That(__nunitAssertValue6, Has.Exactly(1).Items);
        var acquisition = __nunitAssertValue6[0];
        foreach (var binding in new[] {
            new NativeImportBinding(acquisition.Name, "other.dll", NativeBlobFixtureContract.AcquisitionExport),
            new NativeImportBinding(acquisition.Name, NativeBlobFixtureContract.DllName, "OtherBlob"),
            new NativeImportBinding(acquisition.Name, "NATIVE-IMPORT-ABI-PROBE.DLL",
                NativeBlobFixtureContract.AcquisitionExport),
        })
        {
            _ = Assert.Throws<InvalidOperationException>(() => NativeEmitter.EmitWithImportUsage(
                program, [binding], NativeRuntimePolicy.FailFast, allowNativeImports: true));
        }
    }

    [Test]
    public void BlobGetterCatalogRequiresExactImportsSignaturesResultsAndFixtureBoundary()
    {
        var acquisition = CreateBlobAcquisitionDefinition();
        var validation = CreateBlobValidationDefinition();
        foreach (var definition in new[] { acquisition, validation })
        {
            foreach (var invalid in new[] {
                definition with { Key = "test.other" },
                definition with { DllName = "other.dll" },
                definition with { DllName = "NATIVE-IMPORT-ABI-PROBE.DLL" },
                definition with { ExportName = "OtherBlob" },
                definition with { IsNoThrow = false },
                definition with { RequiresUnsafeCaller = true },
                definition with { DoesNotReturn = true },
                definition with { DeploymentPolicy = NativeImportDeploymentPolicy.System32 },
                definition with { ReturnType = PrimitiveType.UInt64 },
                definition with { ParameterTypes = default },
                definition with { ParameterTypes = [PrimitiveType.UInt64] },
                definition with { ParameterContracts = default },
                definition with { ParameterContracts = [NativeImportParameterContract.Value] },
                definition with { ResultContract = (NativeImportResultContract)99 },
            })
            {
                var imports = definition == acquisition ? ImmutableArray.Create(invalid, validation)
                    : ImmutableArray.Create(acquisition, invalid);
                _ = Assert.Throws<ArgumentException>(() =>
                    NativeImportCatalog.CreateForTesting("test-altered-blob", imports));
            }
        }
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting("test-blob-result", [
            acquisition with { ResultContract = NativeImportResultContract.Value }, validation,
        ]));
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting("test-blob-borrow", [
            acquisition, validation with { ParameterContracts = [
                NativeImportParameterContract.Value, NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
            ] },
        ]));
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting("test-blob-size", [
            acquisition, validation with { ParameterTypes = [
                PrimitiveType.NativeBlobFixtureObject, PrimitiveType.NativeBlobBorrowedBytes, PrimitiveType.Int64,
            ] },
        ]));
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting("test-blob-missing", [acquisition]));
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting("test-blob-missing", [validation]));
        _ = Assert.Throws<ArgumentException>(() => new NativeImportCatalog("production-blob", [acquisition, validation]));
        foreach (var export in new[] { NativeBlobFixtureContract.AcquisitionExport, NativeBlobFixtureContract.ValidationExport })
        {
            var alias = new NativeImportDefinition("test.alias", "NATIVE-IMPORT-ABI-PROBE.DLL", export,
                [], PrimitiveType.UInt64, true, true, false, NativeImportDeploymentPolicy.ApplicationDirectory);
            _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting("test-blob-alias", [alias]));
        }
    }

    [TestCase("receiver-constant")]
    [TestCase("receiver-conditional")]
    [TestCase("receiver-negative")]
    [TestCase("receiver-outside-program")]
    [TestCase("receiver-main")]
    [TestCase("receiver-type")]
    [TestCase("receiver-arguments")]
    [TestCase("receiver-arguments-default")]
    [TestCase("receiver-mapping")]
    [TestCase("receiver-unsafe")]
    [TestCase("import-key")]
    [TestCase("import-return")]
    [TestCase("import-throwing")]
    [TestCase("import-unsafe")]
    [TestCase("import-not-external")]
    [TestCase("validation-key")]
    [TestCase("pointer-slot-query")]
    [TestCase("pointer-slot-addref")]
    [TestCase("pointer-slot-release")]
    [TestCase("pointer-slot-size")]
    [TestCase("size-slot-pointer")]
    [TestCase("size-slot-outside")]
    [TestCase("pointer-signature")]
    [TestCase("pointer-signature-default")]
    [TestCase("pointer-signature-extra")]
    [TestCase("size-signature")]
    [TestCase("size-signature-default")]
    [TestCase("pointer-return")]
    [TestCase("size-return")]
    [TestCase("size-return-signed")]
    [TestCase("pointer-result")]
    [TestCase("size-result")]
    [TestCase("pointer-abi")]
    [TestCase("size-abi")]
    [TestCase("gate-return")]
    [TestCase("receiver-escape")]
    [TestCase("catalog-removed")]
    [TestCase("catalog-identity")]
    [TestCase("catalog-production")]
    [TestCase("checked-policy")]
    public void BlobGetterMandatoryVerificationRejectsForgedContracts(string variant)
    {
        var compilation = CompileBlob();
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue7 = compilation.Program;
        Assert.That(__nunitAssertValue7, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue7);
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue8 = main.Body.Statements[0];
        Assert.That(__nunitAssertValue8, Is.TypeOf<BoundReturn>());
        var result = ((BoundReturn)__nunitAssertValue8);
        var __nunitAssertValue9 = result.Value;
        Assert.That(__nunitAssertValue9, Is.TypeOf<BoundNativeBlobGetters>());
        var getters = ((BoundNativeBlobGetters)__nunitAssertValue9);
        var __nunitAssertValue10 = getters.Receiver;
        Assert.That(__nunitAssertValue10, Is.TypeOf<BoundCall>());
        var receiver = ((BoundCall)__nunitAssertValue10);
        var acquisition = program.Functions[receiver.Function];
        var pointer = getters.PointerGetter;
        var size = getters.SizeGetter;
        getters = variant switch {
            "receiver-constant" => getters with { Receiver = new BoundConstant(1, receiver.Type, receiver.Span) },
            "receiver-conditional" => getters with {
                Receiver = new BoundConditional(
                new BoundConstant(1, PrimitiveType.Bool, receiver.Span), receiver, receiver, receiver.Type, receiver.Span)
            },
            "receiver-negative" => getters with { Receiver = receiver with { Function = -1 } },
            "receiver-outside-program" => getters with { Receiver = receiver with { Function = program.Functions.Length } },
            "receiver-main" => getters with { Receiver = receiver with { Function = program.EntryPoint } },
            "receiver-type" => getters with { Receiver = receiver with { Type = PrimitiveType.UInt64 } },
            "receiver-arguments" => getters with {
                Receiver = receiver with {
                    Arguments = [
                new BoundConstant(1, PrimitiveType.UInt64, receiver.Span),
            ]
                }
            },
            "receiver-arguments-default" => getters with { Receiver = receiver with { Arguments = default } },
            "receiver-mapping" => getters with { Receiver = receiver with { ArgumentParameterIndices = [0] } },
            "receiver-unsafe" => getters with { Receiver = receiver with { IsUnsafeAuthorized = true } },
            "validation-key" => getters with { ValidationKey = "test.other" },
            "pointer-slot-query" => getters with { PointerGetter = pointer with { Slot = 0 } },
            "pointer-slot-addref" => getters with { PointerGetter = pointer with { Slot = 1 } },
            "pointer-slot-release" => getters with { PointerGetter = pointer with { Slot = 2 } },
            "pointer-slot-size" => getters with { PointerGetter = pointer with { Slot = 4 } },
            "size-slot-pointer" => getters with { SizeGetter = size with { Slot = 3 } },
            "size-slot-outside" => getters with { SizeGetter = size with { Slot = 5 } },
            "pointer-signature" => getters with { PointerGetter = pointer with { ParameterTypes = [] } },
            "pointer-signature-default" => getters with { PointerGetter = pointer with { ParameterTypes = default } },
            "pointer-signature-extra" => getters with {
                PointerGetter = pointer with {
                    ParameterTypes = [
                PrimitiveType.NativeBlobFixtureObject, PrimitiveType.UInt64,
            ]
                }
            },
            "size-signature" => getters with { SizeGetter = size with { ParameterTypes = [PrimitiveType.UInt64] } },
            "size-signature-default" => getters with { SizeGetter = size with { ParameterTypes = default } },
            "pointer-return" => getters with { PointerGetter = pointer with { ReturnType = PrimitiveType.UInt64 } },
            "size-return" => getters with { SizeGetter = size with { ReturnType = PrimitiveType.UInt32 } },
            "size-return-signed" => getters with { SizeGetter = size with { ReturnType = PrimitiveType.Int64 } },
            "pointer-result" => getters with { PointerGetter = pointer with { Result = NativeBlobGetterResult.UnsignedNativeSize } },
            "size-result" => getters with { SizeGetter = size with { Result = NativeBlobGetterResult.BorrowedBytesWhileFixtureLive } },
            "pointer-abi" => getters with { PointerGetter = pointer with { Abi = "x86-stdcall" } },
            "size-abi" => getters with { SizeGetter = size with { Abi = "x86-stdcall" } },
            "gate-return" => getters with { Type = PrimitiveType.UInt64 },
            _ => getters,
        };
        acquisition = variant switch {
            "import-key" => acquisition with { NativeImportKey = "test.other" },
            "import-return" => acquisition with { ReturnType = PrimitiveType.UInt64 },
            "import-throwing" => acquisition with { IsNoThrow = false },
            "import-unsafe" => acquisition with { RequiresUnsafeCaller = true },
            "import-not-external" => acquisition with { IsExternal = false },
            _ => acquisition,
        };
        program = variant switch {
            "catalog-removed" => program with { NativeImportCatalog = null, NativeImportCatalogId = null },
            "catalog-identity" => program with { NativeImportCatalogId = "wrong" },
            "catalog-production" => program with {
                NativeImportCatalog = NativeImportCatalog.WindowsX64,
                NativeImportCatalogId = NativeImportCatalog.WindowsX64.Identity
            },
            "checked-policy" => program with { RuntimePolicy = BoundRuntimePolicy.CheckedExceptions },
            _ => program,
        };
        main = main with {
            Body = main.Body with {
                Statements = [
            result with { Value = variant == "receiver-escape" ? receiver : getters },
        ]
            }
        };
        program = program with {
            Functions = program.Functions.SetItem(receiver.Function, acquisition)
            .SetItem(program.EntryPoint, main)
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(program));
        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast));
    }

    [Test]
    public void BlobGettersRemainTestOnlyFailFastPeAot()
    {
        var compilation = CompileBlob();
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue11 = compilation.Program;
        Assert.That(__nunitAssertValue11, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue11);
        Assert.That(NativeImportCatalog.WindowsX64.SupportsBlobGetterFixture, Is.False);
        Assert.That(CompileBlob(catalog: NativeImportCatalog.WindowsX64).Program, Is.Null);
        Assert.That(CompileBlob(policy: BoundRuntimePolicy.CheckedExceptions).Program, Is.Null);
        Assert.That(CompileBlob("return 0;", policy: BoundRuntimePolicy.CheckedExceptions).Program, Is.Null);
        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() =>
            NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() => NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() =>
            ModuleCompiler.CreateProducer(program with { EntryPoint = -1 }, ["Main"]));
        _ = Assert.Throws<NotSupportedException>(() => NativeModuleCompiler.CreateProducer(
            program with { EntryPoint = -1 }, ["Main"], "blob", NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(program with { EntryPoint = -1 }, "blob"));
        _ = Assert.Throws<NotSupportedException>(() =>
            NativeEmitter.EmitLibrary(program with { EntryPoint = -1 }, ["Main"], NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() => SemanticVerifier.RejectSourceNativeImports(program with {
            NativeImportCatalog = null,
            NativeImportCatalogId = null,
            Functions = program.Functions.Select(static function => function with { NativeImportKey = null })
                .ToImmutableArray(),
        }, "JIT"));
    }

    [Test]
    public void BlobGetterTestCatalogCannotReachProductionEmissionThroughASystemImport()
    {
        var catalog = NativeImportCatalog.CreateForTesting("test-mixed-system-and-blob", [
            CreateBlobAcquisitionDefinition(), CreateBlobValidationDefinition(),
            new NativeImportDefinition(ImportKey, "kernel32.dll", "GetCurrentThreadId", [],
                PrimitiveType.UInt32, true, true, false, NativeImportDeploymentPolicy.System32),
        ]);
        var compilation = CompileBlob("return CurrentThreadId() == 0 ? 1 : 0;", $$"""
            [Terrum.Interop.NativeImport("{{ImportKey}}")]
            [NoThrow]
            extern uint32 CurrentThreadId();
            """, catalog: catalog);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue12 = compilation.Program;
        Assert.That(__nunitAssertValue12, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue12);
        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast));
        _ = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
    }

    [Test]
    public void BlobGetterIntrinsicDoesNotHideOrdinaryOrGenericFunctions()
    {
        foreach (var declaration in new[] {
            "[NoThrow] int32 blobgetters(int32 value) { return value; }",
            "[NoThrow] T blobgetters<T>(T value) where T : [Copy] { return value; }",
        })
        {
            var compilation = CompileBlob("return blobgetters(42);", BlobAcquisitionDeclaration + declaration);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var __nunitAssertValue13 = compilation.Program;
            Assert.That(__nunitAssertValue13, Is.TypeOf<BoundProgram>());
            var program = ((BoundProgram)__nunitAssertValue13);
            var __nunitAssertValue14 = program.Functions[program.EntryPoint].Body.Statements[0];
            Assert.That(__nunitAssertValue14, Is.TypeOf<BoundReturn>());
            var result = ((BoundReturn)__nunitAssertValue14);
            var __nunitAssertValue15 = result.Value;
            Assert.That(__nunitAssertValue15, Is.TypeOf<BoundCall>());
            Assert.That(program.Functions[(((BoundCall)__nunitAssertValue15)).Function].IsExternal, Is.False);
            _ = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
        }
    }

    private static async Task ValidateBlobGetterRuntimeAsync(string directory)
    {
        const string selfTestKey = "test.application.native-import-abi.NativeFfiBlobSelfTest";
        var catalog = NativeImportCatalog.CreateForTesting("test-native-blob-runtime-v1", [
            CreateBlobAcquisitionDefinition(), CreateBlobValidationDefinition(),
            new NativeImportDefinition(selfTestKey, NativeBlobFixtureContract.DllName, "NativeFfiBlobSelfTest",
                [], PrimitiveType.Int32, true, true, false, NativeImportDeploymentPolicy.ApplicationDirectory),
        ]);
        var compilation = CompileBlob("""
            if (BlobSelfTest() != 0) { return 1; }
            mut int32 index = 0;
            while (index < 32) {
                if (Check() != 0) { return 2; }
                index++;
            }
            return Check();
            """, BlobAcquisitionDeclaration + $$"""
            [Terrum.Interop.NativeImport("{{selfTestKey}}")]
            [NoThrow]
            extern int32 BlobSelfTest();
            [NoThrow]
            int32 Check() { return blobgetters(AcquireBlob()); }
            """, catalog: catalog);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var path = Path.Combine(directory, "blob-getters.exe");
        var __nunitAssertValue16 = compilation.Program;
        Assert.That(__nunitAssertValue16, Is.TypeOf<BoundProgram>());
        await File.WriteAllBytesAsync(path, NativeEmitter.EmitWithNativeImportsForTesting(
                    ((BoundProgram)__nunitAssertValue16), NativeRuntimePolicy.FailFast)).ConfigureAwait(false);
        using var process = Process.Start(new ProcessStartInfo(path) {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = directory,
        }) ?? throw new InvalidOperationException("Blob getter executable did not start.");
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
