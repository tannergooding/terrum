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
    private const string InputElementInitializer =
        "inputelementdesc(utf8z(\"POSITION\"), 0, 6, 0, 0, 0, 0)";
    private const string InputElementLocal =
        "Terrum.Interop.Dx11.InputElementDesc description = " + InputElementInitializer + ";";
    private static readonly string InputElementDeclaration = $$"""
        [Terrum.Interop.NativeImport("{{NativeDx11InputElementContract.ImportKey}}")]
        [NoThrow]
        extern int32 NativeFfiInputElement(Terrum.Interop.Dx11.ReadonlyInputElementDesc input);
        """;

    internal static string InputElementSource => InputElementDeclaration
        + "\nint32 Main() {" + InputElementLocal + "return NativeFfiInputElement(&description);}";

    private static NativeImportDefinition CreateInputElementImportDefinition() =>
        new(NativeDx11InputElementContract.ImportKey, "native-import-abi-probe.dll", "NativeFfiInputElement",
            [PrimitiveType.NativeDx11InputElementDescAddress], PrimitiveType.Int32,
            true, true, false, NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [NativeImportParameterContract.ReadonlyInputElementNoEscape],
        };

    private static Compilation CompileInputElement(string body, string? declarations = null,
        BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast, NativeImportCatalog? catalog = null) =>
        Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse((declarations ?? InputElementDeclaration) + "\nint32 Main() {" + body + "}")],
            catalog ?? NativeImportCatalog.CreateForTesting("test-native-input-element-v1",
                [CreateInputElementImportDefinition()]), policy);

    [Test]
    public void InputElementVerifiesExactLayoutAndUtf8LiteralDependency()
    {
        var compilation = CompileInputElement(InputElementLocal + "return NativeFfiInputElement(input: &description);");
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue0);
        Assert.That(ValueLayout.Size(PrimitiveType.NativeDx11InputElementDesc, program.Structures), Is.EqualTo(32));
        Assert.That(ValueLayout.Alignment(PrimitiveType.NativeDx11InputElementDesc, program.Structures), Is.EqualTo(8));
        Assert.That(ValueLayout.Size(PrimitiveType.NativeDx11InputElementDescAddress, program.Structures), Is.EqualTo(8));
        Assert.That(ValueLayout.Alignment(PrimitiveType.NativeDx11InputElementDescAddress, program.Structures), Is.EqualTo(8));
        Assert.That(program.NativeUtf8ZLiterals, Is.EqualTo(new[] { "POSITION" }));
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue1 = main.Body.Statements[0];
        Assert.That(__nunitAssertValue1, Is.TypeOf<BoundLocal>());
        var local = ((BoundLocal)__nunitAssertValue1);
        var __nunitAssertValue2 = local.Initializer;
        Assert.That(__nunitAssertValue2, Is.TypeOf<BoundNativeDx11InputElementInitialization>());
        var initializer = ((BoundNativeDx11InputElementInitialization)__nunitAssertValue2);
        var __nunitAssertValue3 = initializer.SemanticName;
        Assert.That(__nunitAssertValue3, Is.TypeOf<BoundConstant>());
        Assert.That((((BoundConstant)__nunitAssertValue3)).Value, Is.EqualTo(0));
        Assert.That(main.Variables[local.Variable].NativeInputElementLiteralIndex, Is.EqualTo(0));
        Assert.That(initializer.Fields, Is.All.TypeOf<BoundConstant>());
        Assert.That(initializer.Fields.Select(static field => ((BoundConstant)field).Value),
            Is.EqualTo(new long[] { 0, 6, 0, 0, 0, 0 }));
        var __nunitAssertValue5 = main.Body.Statements[1];
        Assert.That(__nunitAssertValue5, Is.TypeOf<BoundReturn>());
        var result = ((BoundReturn)__nunitAssertValue5);
        var __nunitAssertValue6 = result.Value;
        Assert.That(__nunitAssertValue6, Is.TypeOf<BoundCall>());
        var call = ((BoundCall)__nunitAssertValue6);
        var __nunitAssertValue7 = call.Arguments[0];
        Assert.That(__nunitAssertValue7, Is.TypeOf<BoundNativeDx11InputElementAddress>());
        Assert.That((((BoundNativeDx11InputElementAddress)__nunitAssertValue7)).Variable, Is.EqualTo(local.Variable));
        var image = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
        Assert.That(image.AsSpan().IndexOf("POSITION\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("NativeFfiInputElement\0"u8) >= 0, Is.True);
        var __nunitAssertValue8 = program.NativeImportCatalog;
        Assert.That(__nunitAssertValue8, Is.TypeOf<NativeImportCatalog>());
        Assert.That(((NativeImportCatalog)__nunitAssertValue8).SupportsParameterType(PrimitiveType.NativeUtf8ZPointer), Is.False);
    }

    [TestCase("inputelementdesc(utf8z(\"OTHER\"), 0, 6, 0, 0, 0, 0)")]
    [TestCase("inputelementdesc(utf8z(\"position\"), 0, 6, 0, 0, 0, 0)")]
    [TestCase("inputelementdesc(utf8z(\"POSITION\\0\"), 0, 6, 0, 0, 0, 0)")]
    [TestCase("inputelementdesc(utf8z(\"POSITI\\u00D6N\"), 0, 6, 0, 0, 0, 0)")]
    [TestCase("inputelementdesc(utf8z(\"POS\" + \"ITION\"), 0, 6, 0, 0, 0, 0)")]
    [TestCase("inputelementdesc(utf16z(\"POSITION\"), 0, 6, 0, 0, 0, 0)")]
    [TestCase("inputelementdesc(\"POSITION\", 0, 6, 0, 0, 0, 0)")]
    [TestCase("inputelementdesc(null, 0, 6, 0, 0, 0, 0)")]
    [TestCase("inputelementdesc(0, 0, 6, 0, 0, 0, 0)")]
    [TestCase("inputelementdesc(true ? utf8z(\"POSITION\") : utf8z(\"POSITION\"), 0, 6, 0, 0, 0, 0)")]
    [TestCase("inputelementdesc(utf8z(\"POSITION\"), 1, 6, 0, 0, 0, 0)")]
    [TestCase("inputelementdesc(utf8z(\"POSITION\"), 0, 7, 0, 0, 0, 0)")]
    [TestCase("inputelementdesc(utf8z(\"POSITION\"), 0, 6, 1, 0, 0, 0)")]
    [TestCase("inputelementdesc(utf8z(\"POSITION\"), 0, 6, 0, 1, 0, 0)")]
    [TestCase("inputelementdesc(utf8z(\"POSITION\"), 0, 6, 0, 0, 1, 0)")]
    [TestCase("inputelementdesc(utf8z(\"POSITION\"), 0, 6, 0, 0, 0, 1)")]
    [TestCase("inputelementdesc(utf8z(\"POSITION\"), 0, 6, 0, 0, 0, uint32.MaxValue)")]
    [TestCase("inputelementdesc(utf8z(\"POSITION\"), index, 6, 0, 0, 0, 0)")]
    [TestCase("inputelementdesc(utf8z(\"POSITION\"), 0, 6, 0, 0, 0)")]
    [TestCase("inputelementdesc(utf8z(\"POSITION\"), 0, 6, 0, 0, 0, 0, 0)")]
    [TestCase("inputelementdesc(name: utf8z(\"POSITION\"), 0, 6, 0, 0, 0, 0)")]
    public void InputElementRejectsNonSampleOrPartialInitializers(string initializer)
    {
        var compilation = CompileInputElement("uint32 index = 0;"
            + $"Terrum.Interop.Dx11.InputElementDesc description = {initializer}; return 0;");
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("Terrum.Interop.Dx11.InputElementDesc copy = description; return 0;")]
    [TestCase("mut Terrum.Interop.Dx11.InputElementDesc copy = " + InputElementInitializer + "; return 0;")]
    [TestCase("init Terrum.Interop.Dx11.InputElementDesc vacant; return 0;")]
    [TestCase("Terrum.Interop.Dx11.InputElementDesc vacant; return 0;")]
    [TestCase("Terrum.Interop.Dx11.InputElementDesc[1] array; return 0;")]
    [TestCase("InlineArray<Terrum.Interop.Dx11.InputElementDesc, 1> array; return 0;")]
    [TestCase("description = " + InputElementInitializer + "; return 0;")]
    [TestCase("description.SemanticName = utf8z(\"POSITION\"); return 0;")]
    [TestCase("description.Format = 6; return 0;")]
    [TestCase("return description.Format;")]
    [TestCase("Terrum.Interop.Dx11.ReadonlyInputElementDesc pointer = &description; return 0;")]
    [TestCase("ptr<char8> pointer = utf8z(\"POSITION\"); return 0;")]
    [TestCase("return NativeFfiInputElement(null);")]
    [TestCase("return NativeFfiInputElement(description);")]
    [TestCase("return NativeFfiInputElement(&description + 1);")]
    [TestCase("return NativeFfiInputElement(true ? &description : &description);")]
    [TestCase("description!; return NativeFfiInputElement(&description);")]
    public void InputElementRejectsCopiesMutationArraysAndAddressEscape(string body)
    {
        var compilation = CompileInputElement(InputElementLocal + body);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("struct Holder { Terrum.Interop.Dx11.InputElementDesc Value; }")]
    [TestCase("mut class Holder { Terrum.Interop.Dx11.ReadonlyInputElementDesc Value; }")]
    [TestCase("int32 Forward(Terrum.Interop.Dx11.InputElementDesc value) { return 0; }")]
    [TestCase("int32 Forward(Terrum.Interop.Dx11.ReadonlyInputElementDesc value) { return 0; }")]
    [TestCase("Terrum.Interop.Dx11.InputElementDesc Escape() {" + InputElementLocal + "return description;}")]
    [TestCase("Terrum.Interop.Dx11.ReadonlyInputElementDesc Escape() {" + InputElementLocal + "return &description;}")]
    [TestCase("namespace Terrum.Interop.Dx11 { struct InputElementDesc { int32 Value; } }")]
    [TestCase("namespace Terrum.Interop.Dx11 { mut class ReadonlyInputElementDesc { int32 Value; } }")]
    public void InputElementRejectsDeclarationsForwardingAndReservedTypeSpoofing(string declaration)
    {
        var compilation = CompileInputElement("return 0;", InputElementDeclaration + declaration);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [Test]
    public void InputElementCatalogRejectsUnreviewedReaderContracts()
    {
        var definition = CreateInputElementImportDefinition();
        foreach (var invalid in new[] {
            definition with { Key = "unreviewed" },
            definition with { DllName = "other.dll" },
            definition with { ExportName = "OtherReader" },
            definition with { ReturnType = PrimitiveType.UInt32 },
            definition with { DeploymentPolicy = NativeImportDeploymentPolicy.System32 },
            definition with { IsNoThrow = false },
            definition with { RequiresUnsafeCaller = true },
            definition with { DoesNotReturn = true },
            definition with { ParameterTypes = [PrimitiveType.NativeDx11InputElementDescAddress, PrimitiveType.UInt32] },
            definition with { ParameterContracts = [] },
            definition with { ParameterContracts = [NativeImportParameterContract.Value] },
        })
        {
            _ = Assert.Throws<ArgumentException>(() =>
                NativeImportCatalog.CreateForTesting("test-invalid-input-element", [invalid]));
        }
        _ = Assert.Throws<ArgumentException>(() => new NativeImportCatalog("not-test-only", [definition]));
        foreach (var qualifier in new[] { "mut ", "out ", "init ", "write " })
        {
            var declarations = InputElementDeclaration.Replace(NativeDx11InputElementContract.AddressTypeName,
                qualifier + NativeDx11InputElementContract.AddressTypeName, StringComparison.Ordinal);
            Assert.That(CompileInputElement("return 0;", declarations).Program, Is.Null);
        }
    }

    [Test]
    public void InputElementReaderRemainsTestOnlyFailFastPeAot()
    {
        var body = InputElementLocal + "return NativeFfiInputElement(&description);";
        var compilation = CompileInputElement(body);
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
            program with { EntryPoint = -1 }, ["Main"], "inputelementdesc", NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(program with { EntryPoint = -1 }, "inputelementdesc"));
        _ = Assert.Throws<NotSupportedException>(() =>
            NativeEmitter.EmitLibrary(program with { EntryPoint = -1 }, ["Main"], NativeRuntimePolicy.FailFast));
        Assert.That(CompileInputElement(body, policy: BoundRuntimePolicy.CheckedExceptions).Program, Is.Null);
        Assert.That(CompileInputElement("return 0;", policy: BoundRuntimePolicy.CheckedExceptions).Program, Is.Null);
        var windowsCatalog = NativeImportCatalog.WindowsX64;
        Assert.That(windowsCatalog.SupportsDx11DirectShader, Is.True);
        Assert.That(windowsCatalog.SupportsInputElementStorage, Is.True);
        Assert.That(windowsCatalog.SupportsParameterType(PrimitiveType.NativeDx11InputElementDescAddress), Is.False);
        Assert.That(Compilation.Create(SyntaxTree.Parse(InputElementSource)).Program, Is.Null);
        Assert.That(Compilation.CreateWithNativeImports(SyntaxTree.Parse(InputElementSource)).Program, Is.Null);
        var stripped = program with {
            NativeImportCatalog = null,
            NativeImportCatalogId = null,
            NativeUtf8ZLiterals = [],
            Functions = program.Functions.Select(static function =>
                function with { NativeImportKey = null }).ToImmutableArray(),
        };
        _ = Assert.Throws<NotSupportedException>(() => SemanticVerifier.RejectSourceNativeImports(stripped, "JIT"));
        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImportsForTesting(stripped, NativeRuntimePolicy.FailFast));
    }

    [Test]
    public void InputElementTestCatalogCannotReachProductionEmissionThroughASystemImport()
    {
        var catalog = NativeImportCatalog.CreateForTesting("test-mixed-system-and-input-element-v1", [
            CreateInputElementImportDefinition(),
            new NativeImportDefinition(ImportKey, "kernel32.dll", "GetCurrentThreadId", [],
                PrimitiveType.UInt32, true, true, false, NativeImportDeploymentPolicy.System32),
        ]);
        var declaration = $$"""
            [Terrum.Interop.NativeImport("{{ImportKey}}")]
            [NoThrow]
            extern uint32 CurrentThreadId();
            """;
        var compilation = CompileInputElement(InputElementLocal + "return CurrentThreadId() == 0 ? 1 : 0;",
            declaration, catalog: catalog);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue10 = compilation.Program;
        Assert.That(__nunitAssertValue10, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue10);
        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast));
        _ = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
    }

    [Test]
    public void InputElementInitializerDoesNotHideOrdinaryOrInferredGenericFunctions()
    {
        const string ordinary = "[NoThrow] int32 inputelementdesc(int32 value) { return value; }"
            + "int32 Main() { return inputelementdesc(42); }";
        var ordinaryCompilation = Compilation.Create(SyntaxTree.Parse(ordinary));
        Assert.That(ordinaryCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, ordinaryCompilation.Diagnostics));
        var __nunitAssertValue11 = ordinaryCompilation.Program;
        Assert.That(__nunitAssertValue11, Is.TypeOf<BoundProgram>());
        _ = NativeEmitter.EmitCode(((BoundProgram)__nunitAssertValue11), NativeRuntimePolicy.FailFast);
        var compilation = CompileInputElement("return inputelementdesc(42);", InputElementDeclaration
            + "[NoThrow] T inputelementdesc<T>(T value) where T : [Copy] { return value; }");
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue12 = compilation.Program;
        Assert.That(__nunitAssertValue12, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue12);
        var __nunitAssertValue13 = program.Functions[program.EntryPoint].Body.Statements[0];
        Assert.That(__nunitAssertValue13, Is.TypeOf<BoundReturn>());
        var result = ((BoundReturn)__nunitAssertValue13);
        var __nunitAssertValue14 = result.Value;
        Assert.That(__nunitAssertValue14, Is.TypeOf<BoundCall>());
        Assert.That(program.Functions[(((BoundCall)__nunitAssertValue14)).Function].IsExternal, Is.False);
    }

    [TestCase("index-negative")]
    [TestCase("index-outside-pool")]
    [TestCase("index-type")]
    [TestCase("index-dynamic")]
    [TestCase("pointer-null")]
    [TestCase("metadata-missing")]
    [TestCase("metadata-mismatch")]
    [TestCase("metadata-on-scalar")]
    [TestCase("fields-missing")]
    [TestCase("fields-default")]
    [TestCase("fields-extra")]
    [TestCase("field-0")]
    [TestCase("field-1")]
    [TestCase("field-2")]
    [TestCase("field-3")]
    [TestCase("field-4")]
    [TestCase("field-5")]
    [TestCase("field-type")]
    [TestCase("field-dynamic")]
    [TestCase("initializer-type")]
    [TestCase("pool-empty")]
    [TestCase("pool-default")]
    [TestCase("pool-wrong-literal")]
    [TestCase("pool-embedded-nul")]
    [TestCase("pool-nonascii")]
    [TestCase("pool-null")]
    [TestCase("pool-invalid-unreferenced")]
    [TestCase("pool-over-budget")]
    [TestCase("pool-shared-over-budget")]
    [TestCase("pool-synchronized-wrong-literal")]
    [TestCase("address-variable")]
    [TestCase("address-negative")]
    [TestCase("address-outside-locals")]
    [TestCase("address-type")]
    [TestCase("address-read")]
    [TestCase("address-conditional")]
    [TestCase("copy")]
    [TestCase("escape")]
    [TestCase("mutable")]
    [TestCase("out-local")]
    [TestCase("init-local")]
    [TestCase("vacant")]
    [TestCase("reassignment")]
    [TestCase("field-read")]
    [TestCase("field-write")]
    [TestCase("retired")]
    [TestCase("scoped")]
    [TestCase("checked-policy")]
    public void InputElementMandatoryVerificationRecomputesInitializerAndRejectsForgedUses(string variant)
    {
        var compilation = CompileInputElement("uint32 index = 0;" + InputElementLocal
            + "return NativeFfiInputElement(&description);");
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue15 = compilation.Program;
        Assert.That(__nunitAssertValue15, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue15);
        var main = program.Functions[program.EntryPoint];
        var statements = main.Body.Statements;
        var __nunitAssertValue16 = statements[0];
        Assert.That(__nunitAssertValue16, Is.TypeOf<BoundLocal>());
        var scalar = ((BoundLocal)__nunitAssertValue16);
        var __nunitAssertValue17 = statements[1];
        Assert.That(__nunitAssertValue17, Is.TypeOf<BoundLocal>());
        var local = ((BoundLocal)__nunitAssertValue17);
        var __nunitAssertValue18 = local.Initializer;
        Assert.That(__nunitAssertValue18, Is.TypeOf<BoundNativeDx11InputElementInitialization>());
        var descriptor = ((BoundNativeDx11InputElementInitialization)__nunitAssertValue18);
        var __nunitAssertValue19 = descriptor.SemanticName;
        Assert.That(__nunitAssertValue19, Is.TypeOf<BoundConstant>());
        var literal = ((BoundConstant)__nunitAssertValue19);
        var __nunitAssertValue20 = statements[2];
        Assert.That(__nunitAssertValue20, Is.TypeOf<BoundReturn>());
        var result = ((BoundReturn)__nunitAssertValue20);
        var __nunitAssertValue21 = result.Value;
        Assert.That(__nunitAssertValue21, Is.TypeOf<BoundCall>());
        var call = ((BoundCall)__nunitAssertValue21);
        var __nunitAssertValue22 = call.Arguments[0];
        Assert.That(__nunitAssertValue22, Is.TypeOf<BoundNativeDx11InputElementAddress>());
        var address = ((BoundNativeDx11InputElementAddress)__nunitAssertValue22);
        var variables = main.Variables;
        BoundExpression initializer = descriptor;
        BoundExpression argument = address;
        switch (variant)
        {
            case "index-negative" or "index-outside-pool":
                initializer = descriptor with {
                    SemanticName = literal with {
                        Value = variant == "index-negative" ? -1 : 1,
                    }
                };
                break;
            case "index-type":
                initializer = descriptor with { SemanticName = literal with { Type = PrimitiveType.NativeUtf16ZPointer } };
                break;
            case "index-dynamic":
                initializer = descriptor with {
                    SemanticName = new BoundRead(scalar.Variable,
                    PrimitiveType.NativeUtf8ZPointer, scalar.Span)
                };
                break;
            case "pointer-null":
                initializer = descriptor with { SemanticName = new BoundNull(PrimitiveType.NativeUtf8ZPointer, local.Span) };
                break;
            case "metadata-missing" or "metadata-mismatch":
                variables = variables.SetItem(local.Variable, variables[local.Variable] with {
                    NativeInputElementLiteralIndex = variant == "metadata-missing" ? null : 1,
                });
                if (variant == "metadata-mismatch")
                {
                    program = program with { NativeUtf8ZLiterals = ["POSITION", "POSITION"] };
                }
                break;
            case "metadata-on-scalar":
                variables = variables.SetItem(scalar.Variable, variables[scalar.Variable] with {
                    NativeInputElementLiteralIndex = 0,
                });
                break;
            case "fields-missing" or "fields-default" or "fields-extra":
                initializer = descriptor with {
                    Fields = variant switch {
                        "fields-missing" => descriptor.Fields.RemoveAt(5),
                        "fields-default" => default,
                        _ => descriptor.Fields.Add(new BoundConstant(0, PrimitiveType.UInt32, local.Span)),
                    }
                };
                break;
            case "field-0" or "field-1" or "field-2" or "field-3" or "field-4" or "field-5":
                var fieldIndex = variant[^1] - '0';
                initializer = descriptor with {
                    Fields = descriptor.Fields.SetItem(fieldIndex,
                    new BoundConstant(1, PrimitiveType.UInt32, local.Span))
                };
                break;
            case "field-type":
                initializer = descriptor with {
                    Fields = descriptor.Fields.SetItem(0,
                    new BoundConstant(0, PrimitiveType.Int32, local.Span))
                };
                break;
            case "field-dynamic":
                initializer = descriptor with {
                    Fields = descriptor.Fields.SetItem(0,
                    new BoundRead(scalar.Variable, PrimitiveType.UInt32, scalar.Span))
                };
                break;
            case "initializer-type":
                initializer = descriptor with { Type = PrimitiveType.NativeDx11BufferDesc };
                break;
            case "pool-empty" or "pool-default" or "pool-wrong-literal" or "pool-embedded-nul"
                or "pool-nonascii" or "pool-null":
                program = program with {
                    NativeUtf8ZLiterals = variant switch {
                        "pool-empty" => [],
                        "pool-default" => default,
                        "pool-wrong-literal" => ["OTHER"],
                        "pool-embedded-nul" => ["POSITION\0"],
                        "pool-nonascii" => ["POSITI\u00D6N"],
                        _ => [null!],
                    }
                };
                break;
            case "pool-synchronized-wrong-literal":
                program = program with { NativeUtf8ZLiterals = ["POSITION", "OTHER"] };
                initializer = descriptor with { SemanticName = literal with { Value = 1 } };
                variables = variables.SetItem(local.Variable, variables[local.Variable] with {
                    NativeInputElementLiteralIndex = 1,
                });
                break;
            case "pool-invalid-unreferenced":
                program = program with { NativeUtf8ZLiterals = ["POSITION", "bad\0text"] };
                break;
            case "pool-over-budget":
                program = program with {
                    NativeUtf8ZLiterals =
                    ["POSITION", new string('x', NativeStringZPoolContract.MaximumStorageBytes - 1)]
                };
                break;
            case "pool-shared-over-budget":
                program = program with {
                    NativeUtf16ZLiterals =
                    [new string('u', NativeStringZPoolContract.MaximumStorageBytes / sizeof(char) - 1)]
                };
                break;
            case "address-variable" or "address-negative" or "address-outside-locals":
                argument = address with {
                    Variable = variant switch {
                        "address-variable" => scalar.Variable,
                        "address-negative" => -1,
                        _ => variables.Length,
                    }
                };
                break;
            case "address-type":
                argument = address with { Type = PrimitiveType.NativeDx11BufferDescAddress };
                break;
            case "address-read":
                argument = new BoundRead(local.Variable, PrimitiveType.NativeDx11InputElementDescAddress, local.Span);
                break;
            case "address-conditional":
                argument = new BoundConditional(new BoundConstant(1, PrimitiveType.Bool, local.Span),
                    address, address, PrimitiveType.NativeDx11InputElementDescAddress, local.Span);
                break;
            case "copy":
                initializer = new BoundRead(local.Variable, PrimitiveType.NativeDx11InputElementDesc, local.Span);
                break;
            case "escape":
                statements = statements.Insert(2, new BoundExpressionStatement(address, address.Span));
                break;
            case "mutable" or "out-local" or "init-local" or "vacant":
                variables = variables.SetItem(local.Variable, variables[local.Variable] with {
                    IsMutable = variant == "mutable",
                    IsOut = variant == "out-local",
                    IsInit = variant == "init-local",
                    IsInitializationLocal = variant == "vacant",
                });
                break;
            case "reassignment":
                statements = statements.Insert(2, new BoundAssignment(local.Variable, descriptor, local.Span));
                break;
            case "field-read":
                statements = statements.Insert(2, new BoundExpressionStatement(new BoundFieldRead(
                    new BoundRead(local.Variable, PrimitiveType.NativeDx11InputElementDesc, local.Span),
                    0, PrimitiveType.NativeUtf8ZPointer, local.Span), local.Span));
                break;
            case "field-write":
                statements = statements.Insert(2, new BoundFieldAssignment(local.Variable, 0, literal, local.Span));
                break;
            case "retired":
                statements = statements.Insert(2, new BoundLifetimeEnd(local.Variable, -1, local.Span));
                break;
            case "scoped":
                statements = statements.SetItem(1, new BoundBlock([local], local.Span) { IsManagedLexicalScope = true });
                break;
            case "checked-policy":
                program = program with { RuntimePolicy = BoundRuntimePolicy.CheckedExceptions };
                break;
            default:
                throw new InvalidOperationException("Unknown input-element forgery variant.");
        }
        if (variant != "scoped")
        {
            statements = statements.SetItem(1, local with { Initializer = initializer });
        }
        var returnIndex = statements.Length - 1;
        statements = statements.SetItem(returnIndex, result with { Value = call with { Arguments = [argument] } });
        main = main with { Variables = variables, Body = main.Body with { Statements = statements } };
        program = program with { Functions = program.Functions.SetItem(program.EntryPoint, main) };
        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast));
    }

    [Test]
    public void InputElementVerificationBindsToTheValidatedPoolIndexNotAnAssumedFirstEntry()
    {
        var compilation = CompileInputElement(InputElementLocal + "return NativeFfiInputElement(&description);");
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue23 = compilation.Program;
        Assert.That(__nunitAssertValue23, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue23);
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue24 = main.Body.Statements[0];
        Assert.That(__nunitAssertValue24, Is.TypeOf<BoundLocal>());
        var local = ((BoundLocal)__nunitAssertValue24);
        var __nunitAssertValue25 = local.Initializer;
        Assert.That(__nunitAssertValue25, Is.TypeOf<BoundNativeDx11InputElementInitialization>());
        var initializer = ((BoundNativeDx11InputElementInitialization)__nunitAssertValue25);
        var __nunitAssertValue26 = initializer.SemanticName;
        Assert.That(__nunitAssertValue26, Is.TypeOf<BoundConstant>());
        var literal = ((BoundConstant)__nunitAssertValue26);
        main = main with {
            Variables = main.Variables.SetItem(local.Variable, main.Variables[local.Variable] with {
                NativeInputElementLiteralIndex = 1,
            }),
            Body = main.Body with {
                Statements = main.Body.Statements.SetItem(0, local with {
                    Initializer = initializer with { SemanticName = literal with { Value = 1 } },
                })
            },
        };
        _ = NativeEmitter.EmitWithNativeImportsForTesting(program with {
            NativeUtf8ZLiterals = ["other", "POSITION"],
            Functions = program.Functions.SetItem(program.EntryPoint, main),
        }, NativeRuntimePolicy.FailFast);
    }

    private static async Task ValidateInputElementRuntimeAsync(string directory)
    {
        var compilation = CompileInputElement("return CheckInputElement();", InputElementDeclaration + $$"""
            [Terrum.Interop.NativeImport("{{Utf8ZFixtureKey}}")]
            [NoThrow]
            extern int32 NativeFfiUtf8Z(ptr<char8> first, uint32 second, uint64 third,
                ptr<char8> fourth, uint32 fifth, ptr<char8> sixth);
            [NoThrow]
            int32 CheckInputElement() {
                if (NativeFfiUtf8Z(utf8z("A\nmain"), 324478056, 81985529216486895,
                    utf8z("ps_4_0"), 610800471, utf8z("")) != 0) {
                    return 7;
                }
                {{InputElementLocal}}
                return NativeFfiInputElement(input: &description);
            }
            """, catalog: NativeImportCatalog.CreateForTesting("test-native-input-element-and-utf8-v1",
                [CreateInputElementImportDefinition(), CreateUtf8ZImportDefinition()]));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue27 = compilation.Program;
        Assert.That(__nunitAssertValue27, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue27);
        Assert.That(program.NativeUtf8ZLiterals, Is.EqualTo(new[] { "A\nmain", "ps_4_0", "", "POSITION" }));
        var path = Path.Combine(directory, "input-element.exe");
        await File.WriteAllBytesAsync(path, NativeEmitter.EmitWithNativeImportsForTesting(
            program, NativeRuntimePolicy.FailFast)).ConfigureAwait(false);
        using var process = Process.Start(new ProcessStartInfo(path) {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = directory,
        }) ?? throw new InvalidOperationException("Input-element executable did not start.");
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
