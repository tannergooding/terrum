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
    public void EmitsNativeParameterOnNativeImportAndSignatureAnchor()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 4294967295)]
            internal extern void Observe(ptr<uint32> value);

            internal void Form() {
                unsafe { _ = &Observe; }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                compilation.Program!, "native-address-parameter");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var target = 0;
        var caller = 0;
        var anchor = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            target = name == "Observe" ? row : target;
            caller = name == "Form" ? row : caller;
            anchor = name.StartsWith("__trrm_fnptr_anchor_",
                StringComparison.Ordinal) ? row : anchor;
        }

        Assert.That(target, Is.GreaterThan(0));
        Assert.That(caller, Is.GreaterThan(0));
        Assert.That(anchor, Is.GreaterThan(0));
        var signature = tables.Methods.GetSignature(target);
        var (parameterStart, parameterCount)
            = tables.Signatures.GetParamRange(signature);
        Assert.That(parameterCount, Is.EqualTo(1));
        var parameterTypeSpec = tables.Parameters.GetTypeSpec(parameterStart);
        Assert.That(TrRmNativeImportTypeContract.TryGetNativeParameterPointee(
            tables.TypeSpecs, parameterTypeSpec, 1, tables.Layouts,
            out var pointeeType), Is.True);
        Assert.That(pointeeType, Is.EqualTo(PrimitiveType.UInt32));

        var targetContracts = tables.Contracts.GetNativeParameters(target);
        var anchorContracts = tables.Contracts.GetNativeParameters(anchor);
        Assert.That(targetContracts.Length, Is.EqualTo(1));
        Assert.That(anchorContracts.Length, Is.EqualTo(1));
        Assert.That(targetContracts[0], Is.EqualTo(anchorContracts[0]));
        Assert.That(targetContracts[0].ParameterOrdinal, Is.Zero);
        Assert.That(targetContracts[0].Access, Is.EqualTo(1));
        Assert.That(targetContracts[0].Retention, Is.EqualTo(1));
        Assert.That(targetContracts[0].ExtentKind, Is.EqualTo(1));
        Assert.That(targetContracts[0].Extent, Is.EqualTo(uint.MaxValue));
        Assert.That(TrRmFeatureManifestValidator.Requires(tables.Features,
            tables.Modules, 1,
            TrRmFeatureTableReader.ForeignInvocationContractsCandidateFeatureId,
            TrRmArtifactSchema.ForeignInvocationDraft), Is.True);
        Assert.That(tables.StandardNativeImports.HasImportForMethod(target),
            Is.True);
        tables.ValidateRegisterBodyMapsForMethod(caller);
    }

    [Test]
    public void EmitsWritableNativeParameterPointerQualifier()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 0)]
            internal extern void Observe(write ptr<uint32> value);

            internal void Form() {
                unsafe { _ = &Observe; }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                compilation.Program!, "native-address-writable-parameter");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var target = 0;
        var anchor = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            target = name == "Observe" ? row : target;
            anchor = name.StartsWith("__trrm_fnptr_anchor_",
                StringComparison.Ordinal) ? row : anchor;
        }

        Assert.That(target, Is.GreaterThan(0));
        Assert.That(anchor, Is.GreaterThan(0));
        var signature = tables.Methods.GetSignature(target);
        var (parameterStart, parameterCount)
            = tables.Signatures.GetParamRange(signature);
        Assert.That(parameterCount, Is.EqualTo(1));
        var typeSpecRef = tables.Parameters.GetTypeSpec(parameterStart);
        var typeSpec = tables.TypeSpecs.GetTypeSpecBytes(
            typeSpecRef);
        Assert.That(typeSpec.Length, Is.EqualTo(4));
        Assert.That(typeSpec[1], Is.EqualTo(1));
        Assert.That(typeSpec[2], Is.EqualTo(0x8C));
        Assert.That(TrRmNativeImportTypeContract.TryGetNativeParameterPointee(
            tables.TypeSpecs, typeSpecRef, 2, tables.Layouts,
            out var pointeeType), Is.True);
        Assert.That(pointeeType, Is.EqualTo(PrimitiveType.UInt32));
        var targetContracts = tables.Contracts.GetNativeParameters(target);
        var anchorContracts = tables.Contracts.GetNativeParameters(anchor);
        Assert.That(targetContracts.Length, Is.EqualTo(1));
        Assert.That(targetContracts[0], Is.EqualTo(anchorContracts[0]));
        Assert.That(targetContracts[0].Access, Is.EqualTo(2));
        Assert.That(targetContracts[0].ExtentKind, Is.Zero);
        Assert.That(targetContracts[0].Extent, Is.Zero);
    }

    [Test]
    public void EmitsIndirectUnitCallFromNativeAddress()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void Observe();

            internal void Form() {
                unsafe {
                    var callback = &Observe;
                    callback();
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var form = program.Functions.Single(static function =>
            function.Name == "Form");
        var calls = new List<BoundIndirectCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(form.Body, expression =>
        {
            if (expression is BoundIndirectCall indirectCall)
            {
                calls.Add(indirectCall);
            }
        }), Is.True);
        var call = calls.Single();
        Assert.That(call.Target, Is.TypeOf<BoundRead>());

        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                program, "native-address-indirect-call");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var caller = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Form")
            {
                caller = row;
                break;
            }
        }

        Assert.That(caller, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(caller);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(caller), tables.Contracts.Count);
        Assert.That(body.RecordCount, Is.EqualTo(3));
        Assert.That(body.GetRecord(1)[0], Is.EqualTo(0x0D));
        Assert.That(body.GetRecord(1)[4], Is.EqualTo(2));
        Assert.That(body.GetRecord(1)[6], Is.Zero);
        Assert.That(body.HomeMap.Length, Is.EqualTo(8));
        Assert.That(body.HomeMap[0], Is.EqualTo(3));
        Assert.That(body.GetRecord(2)[0], Is.EqualTo(0x02));
        Assert.That(body.GetRecord(2)[4], Is.EqualTo(3));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xE9, 0, 1, 0, 0xE0, 0, 2, 0, 0x18, 0,
            }));
    }

    [Test]
    public void EmitsNativeParameterCallSiteFromInferredAddress()
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
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                program, "native-address-parameter-call");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var target = 0;
        var caller = 0;
        var anchor = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            target = name == "Observe" ? row : target;
            caller = name == "Form" ? row : caller;
            anchor = name.StartsWith("__trrm_fnptr_anchor_",
                StringComparison.Ordinal) ? row : anchor;
        }

        Assert.That(target, Is.GreaterThan(0));
        Assert.That(caller, Is.GreaterThan(0));
        Assert.That(anchor, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(caller);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(caller), tables.Contracts.Count);
        var callSite = body.GetRecord(2);
        Assert.That(body.HomeMap.Length, Is.EqualTo(16));
        Assert.That(body.HomeMap[0], Is.EqualTo(3));
        Assert.That(body.HomeMap[8], Is.EqualTo(2));
        Assert.That(callSite[0], Is.EqualTo(0x02));
        Assert.That(callSite[4], Is.EqualTo(3));
        Assert.That(callSite[12], Is.EqualTo(1));
        Assert.That(callSite[20], Is.EqualTo(2));
        Assert.That(callSite[21], Is.EqualTo(1));
        Assert.That(callSite[22], Is.EqualTo(1));
        Assert.That(callSite[23], Is.Zero);
        var targetParameters = tables.Contracts.GetNativeParameters(target);
        var anchorParameters = tables.Contracts.GetNativeParameters(anchor);
        Assert.That(targetParameters.Length, Is.EqualTo(1));
        Assert.That(targetParameters[0], Is.EqualTo(anchorParameters[0]));
        Assert.That(targetParameters[0].Extent, Is.EqualTo(1u));
    }

    [Test]
    public void EmitsWritableNativeParameterCallSiteFromInferredAddress()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 0)]
            internal extern void Observe(write ptr<uint32> value);

            internal void Form(write ptr<uint32> value) {
                unsafe {
                    var callback = &Observe;
                    callback(value);
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                compilation.Program!, "native-address-writable-parameter-call");
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
        tables.ValidateRegisterBodyMapsForMethod(caller);
        var targetParameters = tables.Contracts.GetNativeParameters(target);
        Assert.That(targetParameters.Length, Is.EqualTo(1));
        Assert.That(targetParameters[0].Access, Is.EqualTo(2));
        Assert.That(targetParameters[0].ExtentKind, Is.Zero);
        Assert.That(targetParameters[0].Extent, Is.Zero);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(caller), tables.Contracts.Count);
        Assert.That(body.HomeMap.Length, Is.EqualTo(16));
    }

    [Test]
    public void PreservesReorderedNativeParameterCallSiteInputs()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            internal extern void Observe(ptr<uint32> value, uint32 count);

            internal void Form(uint32 count, ptr<uint32> value) {
                unsafe {
                    var callback = &Observe;
                    callback(value, count);
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                compilation.Program!, "native-address-reordered-arguments");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var caller = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Form")
            {
                caller = row;
                break;
            }
        }

        Assert.That(caller, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(caller);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(caller), tables.Contracts.Count);
        var callSite = body.GetRecord(2);
        Assert.That(body.HomeMap.Length, Is.EqualTo(24));
        Assert.That(body.HomeMap[8], Is.EqualTo(2));
        Assert.That(body.HomeMap[16], Is.EqualTo(2));
        Assert.That(callSite[12], Is.EqualTo(2));
        Assert.That(callSite[20], Is.EqualTo(2));
        Assert.That(callSite[21], Is.EqualTo(1));
        Assert.That(callSite[22], Is.EqualTo(2));
        Assert.That(callSite[24], Is.EqualTo(2));
        Assert.That(callSite[25], Is.EqualTo(1));
        Assert.That(callSite[26], Is.EqualTo(1));
    }

    [Test]
    public void EmitsIndirectScalarResultFromNativeAddress()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern uint32 Transform(uint32 value);

            internal uint32 Form(uint32 value) {
                unsafe {
                    var callback = &Transform;
                    return callback(value);
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                compilation.Program!, "native-address-result");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var caller = 0;
        var target = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            caller = name == "Form" ? row : caller;
            target = name == "Transform" ? row : target;
        }

        Assert.That(caller, Is.GreaterThan(0));
        Assert.That(target, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(caller);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(caller), tables.Contracts.Count);
        var callSite = body.GetRecord(2);
        Assert.That(callSite[4], Is.EqualTo(3));
        Assert.That(callSite[5], Is.EqualTo(1));
        Assert.That(callSite[10], Is.Zero);
        Assert.That(callSite[11], Is.Zero);
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xE9, 0, 1, 0, 0xE0, 0, 2, 0, 0x17, 0,
            }));
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
            tables.Signatures.GetResultTypeSpec(
                tables.Methods.GetSignature(caller)),
            tables.Signatures.GetResultTypeSpec(
                tables.Methods.GetSignature(target))), Is.True);
    }

    [Test]
    public void EmitsIndirectScalarResultThroughHomeFromNativeAddress()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern uint32 Transform(uint32 value);

            internal uint32 Form(uint32 value) {
                unsafe {
                    var callback = &Transform;
                    var result = callback(value);
                    return result;
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                compilation.Program!, "native-address-result-home");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var caller = 0;
        var target = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            caller = name == "Form" ? row : caller;
            target = name == "Transform" ? row : target;
        }

        Assert.That(caller, Is.GreaterThan(0));
        Assert.That(target, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(caller);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(caller), tables.Contracts.Count);
        var callSite = body.GetRecord(2);
        Assert.That(body.HomeMap.Length, Is.EqualTo(24));
        Assert.That(body.HomeMap[0], Is.EqualTo(3));
        Assert.That(body.HomeMap[8], Is.EqualTo(3));
        Assert.That(body.HomeMap[16], Is.EqualTo(2));
        Assert.That(callSite[4], Is.EqualTo(3));
        Assert.That(callSite[5], Is.EqualTo(2));
        Assert.That(callSite[10], Is.EqualTo(1));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xE9, 0, 1, 0, 0xE0, 0, 2, 0, 0xB8, 1,
            }));
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
            tables.Signatures.GetResultTypeSpec(
                tables.Methods.GetSignature(caller)),
            tables.Signatures.GetResultTypeSpec(
                tables.Methods.GetSignature(target))), Is.True);
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
