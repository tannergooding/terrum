// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmForeignInvocationAddressEmitterTests
{
    [Test]
    public void ExplicitNativeImportConventionProducesAnInferredAddress()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void Observe();

            internal void Form() {
                unsafe { _ = &Observe; }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var target = program.Functions.Single(static function =>
            function.Name == "Observe");
        var form = program.Functions.Single(static function =>
            function.Name == "Form");
        var addresses = new List<BoundNativeFunctionAddress>();
        Assert.That(BoundTreeTraversal.VisitExpressions(form.Body, expression =>
        {
            if (expression is BoundNativeFunctionAddress address)
            {
                addresses.Add(address);
            }
        }), Is.True);

        var address = addresses.Single();
        Assert.That(address.Function, Is.EqualTo(
            Enumerable.Range(0, program.Functions.Length)
                .Single(index => program.Functions[index].Name == "Observe")));
        Assert.That(target.DirectNativeImport!.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Unmanaged));
        Assert.That(program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(address.Type)].CallingConvention,
            Is.EqualTo(NativeCallingConvention.Unmanaged));

        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(program, "native-address");
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        var targetRow = 0;
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            targetRow = name == "Observe" ? row : targetRow;
            callerRow = name == "Form" ? row : callerRow;
        }
        Assert.That(targetRow, Is.GreaterThan(0));
        Assert.That(callerRow, Is.GreaterThan(0));
        Assert.That(tables.StandardNativeImports.HasImportForMethod(targetRow), Is.True);
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        Assert.That(body.MapMask, Is.EqualTo((byte)0x06));
        Assert.That(body.MethodMap.Length, Is.EqualTo(sizeof(uint)));
        Assert.That(body.TypeMap.Length, Is.EqualTo(sizeof(uint)));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] { 0xE9, 0, 1, 0, 0x18, 0 }));
    }

    [Test]
    public void EmitsParameterizedScalarNativeAddressSignature()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern uint32 Observe(uint32 value);

            internal void Form() {
                unsafe { _ = &Observe; }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                compilation.Program!, "native-address-parameterized");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var target = 0;
        var caller = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            target = name == "Observe" ? row : target;
            caller = name == "Form" ? row : caller;
        }

        Assert.That(target, Is.GreaterThan(0));
        Assert.That(caller, Is.GreaterThan(0));
        var signature = tables.Methods.GetSignature(target);
        var (parameterStart, parameterCount)
            = tables.Signatures.GetParamRange(signature);
        var resultType = tables.Signatures.GetResultTypeSpec(signature);

        Assert.That(tables.Signatures.ParamCount, Is.EqualTo(1));
        Assert.That(parameterCount, Is.EqualTo(1));
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
            resultType, tables.Parameters.GetTypeSpec(parameterStart)), Is.True);
        Assert.That(tables.TypeSpecs.GetTypeSpecRef(resultType).Form,
            Is.EqualTo(0x01));
        Assert.That(tables.Methods.GetSignature(caller),
            Is.Not.EqualTo(signature));
        tables.ValidateRegisterBodyMapsForMethod(caller);
    }

    [Test]
    public void NativeParameterIndirectCallBindsThroughAnInferredAddress()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            internal extern void Observe(ptr<uint32> value);

            internal void Form(ptr<uint32> value) {
                unsafe {
                    var callback = &Observe;
                    callback(value);
                }
            }
            int32 Main() { return 0; }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var import = program.Functions.Single(static function =>
            function.Name == "Observe").DirectNativeImport!;
        Assert.That(import.Parameters.Single().Extent, Is.EqualTo(1));
        var signature = program.NativeFunctionPointerSignatures.Single();
        Assert.That(signature.NativeParameters.Single().Extent, Is.EqualTo(1));
        var parameterType = signature.ParameterTypes.Single();
        Assert.That(NativePointerTypes.IsType(parameterType), Is.True);
        Assert.That(program.NativePointerTypes[NativePointerTypes.Index(parameterType)],
            Is.EqualTo(new NativePointerTypeDefinition(PrimitiveType.UInt32, false)));
    }

    [Test]
    public void NativeParameterRejectsWriteAccessForReadonlyFormal()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            internal extern void Observe(ptr<uint32> value);
            """), core);

        Assert.That(compilation.HasErrors, Is.True,
            "Write access must require a writable pointer formal.");
    }

    [Test]
    public void NativeParameterReadPreservesWritableFormalQualifier()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            internal extern void Observe(write ptr<uint32> value);

            internal void Form(ptr<uint32> value) {
                unsafe {
                    var callback = &Observe;
                    callback(value);
                }
            }
            int32 Main() { return 0; }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var formalType = program.NativeFunctionPointerSignatures.Single()
            .ParameterTypes.Single();
        Assert.That(NativePointerTypes.IsType(formalType), Is.True);
        Assert.That(program.NativePointerTypes[NativePointerTypes.Index(formalType)],
            Is.EqualTo(new NativePointerTypeDefinition(PrimitiveType.UInt32, true)));
        Assert.That(program.NativeFunctionPointerSignatures.Single()
            .NativeParameters.Single().Access, Is.EqualTo(NativeParameterAccess.Read));
        Assert.That(NativeEmitter.EmitWithNativeImports(
            program, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void ReadAccessDoesNotUpgradeWritableFormalDuringDevelopmentRoundTrip()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "Observe")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            extern void Observe(write ptr<uint32> value);

            int32 Main() {
                uint32 value = 42;
                unsafe { Observe(&value); return 0; }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(compilation.Program!, "native-read-access");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        var main = loaded.Functions.Single(static function => function.Name == "Main");
        var addresses = new List<BoundNativeStorageAddress>();
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body, expression =>
        {
            if (expression is BoundNativeStorageAddress address)
            {
                addresses.Add(address);
            }
        }), Is.True);
        Assert.That(addresses, Has.Count.EqualTo(1));
        Assert.That(addresses[0].IsReadonly, Is.True);
        Assert.That(main.Variables[addresses[0].Variable].IsMutable, Is.False);
        var target = loaded.Functions.Single(static function => function.Name == "Observe");
        var formalType = target.Variables[0].Type;
        Assert.That(NativePointerTypes.IsType(formalType), Is.True);
        Assert.That(loaded.NativePointerTypes[NativePointerTypes.Index(formalType)],
            Is.EqualTo(new NativePointerTypeDefinition(PrimitiveType.UInt32, true)));
        Assert.That(target.DirectNativeImport!.Parameters.Single().Access,
            Is.EqualTo(NativeParameterAccess.Read));
    }

    [Test]
    public void NativeParameterIndirectCallRejectsDifferentPointeeType()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            internal extern void Observe(ptr<uint32> value);

            internal void Form(ptr<uint64> value) {
                unsafe {
                    var callback = &Observe;
                    callback(value);
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.True,
            "The inferred pointer signature must retain the target's pointee type.");
    }
}
