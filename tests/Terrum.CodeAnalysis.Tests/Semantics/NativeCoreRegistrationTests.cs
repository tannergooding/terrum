// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Semantics;

public sealed class NativeCoreRegistrationTests
{
    [Test]
    public void RegistrationRetainsSelectedEnumAndSchemaIdentities()
    {
        var registration = CreateRegistration();

        Assert.That(registration.IsValid(), Is.True);
        Assert.That(registration.CallingConventions.Members.Select(static member =>
            (member.Name, member.Value)), Is.EqualTo(new[]
            {
                ("Unmanaged", 1),
                ("Cdecl", 2),
                ("Stdcall", 3),
                ("Vectorcall", 4),
            }));
        Assert.That(registration.NativeImport.CallingConventionEnum,
            Is.EqualTo(registration.CallingConventions.Identity));
        Assert.That(registration.NativeCallable.CallingConventionEnum,
            Is.EqualTo(registration.CallingConventions.Identity));
        Assert.That(registration.NativeCallConv.CallingConventionEnum,
            Is.EqualTo(registration.CallingConventions.Identity));
    }

    [Test]
    public void RegistrationRejectsWrongEnumValuesAndSchemaOwners()
    {
        var registration = CreateRegistration();
        var invalidMembers = registration.CallingConventions.Members.SetItem(1,
            registration.CallingConventions.Members[1] with { Value = 1 });
        var invalidEnum = registration.CallingConventions with { Members = invalidMembers };

        Assert.Throws<ArgumentException>(() => NativeCoreRegistrationSet.Create(
            registration.UsingModuleIdentity, invalidEnum, registration.ResolutionModes,
            registration.ResolutionPolicies, registration.NativeImport,
            registration.NativeCallable, registration.NativeCallConv, registration.NativeExport));

        var invalidImport = registration.NativeImport with {
            CallingConventionEnum = registration.ResolutionModes.Identity,
        };
        Assert.Throws<ArgumentException>(() => NativeCoreRegistrationSet.Create(
            registration.UsingModuleIdentity, registration.CallingConventions,
            registration.ResolutionModes, registration.ResolutionPolicies, invalidImport,
            registration.NativeCallable, registration.NativeCallConv, registration.NativeExport));
    }

    [Test]
    public void CompilationReferenceCarriesTheExplicitNativeCoreRegistration()
    {
        var registration = CreateRegistration();
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: registration);

        Assert.That(reference.NativeCoreRegistration, Is.SameAs(registration));
        var compilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return 0; }"), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void CompilationOptionsSupplyTheExplicitNativeCoreRegistration()
    {
        var options = new CompilationOptions {
            NativeCoreRegistration = CreateRegistration(),
        };
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;
            [NativeImport("example.dll", "Read", CallConv: NativeCallingConvention.Cdecl)]
            extern int32 Read();
            """), options);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program!.Functions.Single(static function =>
            function.Name == "Read").DirectNativeImport!.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Cdecl));
    }

    [Test]
    public void CompilationOptionsRejectDuplicateNativeCoreRegistration()
    {
        var registration = CreateRegistration();
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: registration);
        var options = new CompilationOptions {
            NativeCoreRegistration = registration,
        };

        Assert.Throws<ArgumentException>(() => Compilation.CreateLibrary(
            SyntaxTree.Parse("int32 Read() { return 0; }"), reference, options));
    }

    [Test]
    public void NativeCallConvBindsAgainstTheSelectedCoreEnum()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;
            int32 Invoke(
                [NativeCallConv(NativeCallingConvention.Cdecl)] fnptr<int32> callback) {
                return 0;
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var functionPointerType = program.Functions.Single().Variables[0].Type;
        Assert.That(NativeFunctionPointerTypes.IsType(functionPointerType), Is.True);
        var signature = program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(functionPointerType)];
        Assert.That(signature.CallingConvention, Is.EqualTo(NativeCallingConvention.Cdecl));
        Assert.That(signature.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(signature.ParameterTypes, Is.Empty);
    }

    [Test]
    public void BareFunctionPointerUsesTheTerrumDefaultConvention()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            unsafe int32 Invoke(fnptr<int32, int32> callback, int32 value) {
                unsafe { return callback(value); }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var functionPointerType = program.Functions.Single().Variables[0].Type;
        var signature = program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(functionPointerType)];
        Assert.That(signature.CallingConvention, Is.EqualTo(NativeCallingConvention.Native));
        Assert.That(signature.ParameterTypes.Length, Is.EqualTo(1));
        Assert.That(signature.ParameterTypes[0], Is.EqualTo(PrimitiveType.Int32));
        Assert.That(signature.ReturnType, Is.EqualTo(PrimitiveType.Int32));
    }

    [Test]
    public void RegisteredEnumValuesForwardThroughOrdinaryConstEval()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;
            alias ABI = NativeCallingConvention;

            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct CallbackHolder {
                public [NativeCallConv(Forward(ABI.Cdecl, true))]
                    fnptr<int32, int32> Callback;
                public [NativeCallConv(ABI.Cdecl)] fnptr<int32, int32> DirectCallback;
            }

            [ConstEval]
            internal ABI Forward(ABI value, bool choose) {
                ABI copy = value;
                return choose ? copy : ABI.Stdcall;
            }

            internal ABI Evaluate() {
                ABI chosen = const Forward(ABI.Cdecl, true);
                return chosen;
            }

            struct Holder { public ABI Value; }

            internal ABI Read(Holder holder) {
                return holder.Value;
            }

            public int32 Invoke() {
                ABI selected = Forward(ABI.Cdecl, true);
                return 0;
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var enumType = program.Functions.Single(static function =>
            function.Name == "Forward").ReturnType;
        Assert.That(enumType, Is.EqualTo(BoundEnumTypes.Type(0)));
        Assert.That(program.EnumDefinitions[0].Name,
            Is.EqualTo("Terrum.Interop.NativeCallingConvention"));
        var callbackFields = program.Structures.Single(static structure =>
            structure.Name == "CallbackHolder").Fields;
        Assert.That(callbackFields, Has.Length.EqualTo(2));
        Assert.That(callbackFields[0].Type, Is.EqualTo(callbackFields[1].Type));
        Assert.That(program.Structures.Single(static structure =>
            structure.Name == "Holder").Fields.Single().Type, Is.EqualTo(enumType));
        var evaluate = program.Functions.Single(static function =>
            function.Name == "Evaluate");
        var initializer = evaluate.Body!.Statements.OfType<BoundLocal>().Single().Initializer;
        Assert.That(initializer, Is.TypeOf<BoundConstant>());
        Assert.That(initializer.Type, Is.EqualTo(enumType));
        Assert.That(((BoundConstant)initializer).Value, Is.EqualTo(2));
        Assert.That(NativeEmitter.EmitLibrary(program, ["Invoke"], NativeRuntimePolicy.FailFast),
            Is.Not.Empty);
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(program, ["Read"]));
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(program, "nominal-enum"));
    }

    [Test]
    public void NativeDeclarationConventionsAcceptForwardedEnumConstants()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;
            alias ABI = NativeCallingConvention;

            [ConstEval]
            internal ABI Forward(ABI value, bool choose) {
                ABI copy = value;
                return choose ? copy : ABI.Stdcall;
            }

            [ConstEval]
            internal NativeImportResolutionMode ForwardMode(
                NativeImportResolutionMode value) {
                return value;
            }

            [ConstEval]
            internal NativeImportResolutionPolicy ForwardPolicy(
                NativeImportResolutionPolicy value) {
                return value;
            }

            [NativeImport(
                "example.dll", "Read",
                CallConv: Forward(ABI.Cdecl, true))]
            extern int32 Read();

            [NativeImport(
                "example.dll", "ReadDynamic",
                ResolutionMode: ForwardMode(NativeImportResolutionMode.Dynamic),
                ResolutionPolicy: ForwardPolicy(
                    NativeImportResolutionPolicy.CompatibilitySearch),
                CallConv: Forward(ABI.Stdcall, true))]
            extern int32 ReadDynamic();

            [NativeCallable(CallConv: Forward(ABI.Stdcall, true))]
            int32 Callback(int32 value) { return value; }

            [NativeCallable]
            int32 DefaultCallback(int32 value) { return value; }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var import = program.Functions.Single(static function =>
            function.Name == "Read");
        Assert.That(import.DirectNativeImport!.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Cdecl));
        var dynamicImport = program.Functions.Single(static function =>
            function.Name == "ReadDynamic");
        Assert.That(dynamicImport.DirectNativeImport!.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Stdcall));
        Assert.That(dynamicImport.DirectNativeImport.ResolutionMode,
            Is.EqualTo(NativeImportResolutionMode.Dynamic));
        Assert.That(dynamicImport.DirectNativeImport.ResolutionPolicy,
            Is.EqualTo(NativeImportResolutionPolicy.CompatibilitySearch));
        var callable = program.Functions.Single(static function =>
            function.Name == "Callback");
        Assert.That(callable.NativeCallableConvention,
            Is.EqualTo(NativeCallingConvention.Stdcall));
        var signature = program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(callable.NativeFunctionPointerType!.Value)];
        Assert.That(signature.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Stdcall));
        var defaultCallable = program.Functions.Single(static function =>
            function.Name == "DefaultCallback");
        Assert.That(defaultCallable.NativeCallableConvention,
            Is.EqualTo(NativeCallingConvention.Unmanaged));
    }

    [Test]
    public void UnrelatedRegisteredEnumUsesTheSameOrdinaryValueModel()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            enumDefinitions: [CreateUnrelatedEnum()]);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            alias Palette = Sample.Palette;

            [ConstEval]
            internal Palette Forward(Palette value, bool choose) {
                Palette copy = value;
                return choose ? copy : Palette.Maximum;
            }

            internal Palette Evaluate() {
                Palette minimum = const Forward(Palette.Minimum, true);
                Palette maximum = const Forward(Palette.Minimum, false);
                return minimum;
            }

            public int32 Invoke() {
                Palette runtimeValue = Forward(Palette.MinusOne, true);
                return 0;
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var enumType = BoundEnumTypes.Type(0);
        Assert.That(program.Functions.Single(static function =>
            function.Name == "Forward").ReturnType, Is.EqualTo(enumType));
        var evaluate = program.Functions.Single(static function =>
            function.Name == "Evaluate");
        var constants = evaluate.Body!.Statements.OfType<BoundLocal>()
            .Select(static local => (BoundConstant)local.Initializer).ToArray();
        Assert.That(constants.Select(static constant => constant.Value),
            Is.EqualTo(new long[] { int.MinValue, int.MaxValue }));
        Assert.That(constants.All(constant => constant.Type == enumType), Is.True);
        Assert.That(NativeEmitter.EmitLibrary(program, ["Invoke"], NativeRuntimePolicy.FailFast),
            Is.Not.Empty);
    }

    [Test]
    public void UnrelatedEnumCannotSupplyNativeCallConv()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration(),
            enumDefinitions: [CreateUnrelatedEnum()]);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;
            alias Palette = Sample.Palette;
            int32 Invoke(
                [NativeCallConv(Palette.Maximum)] fnptr<int32> callback) {
                return 0;
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void RegisteredEnumValuesDoNotConvertToTheirUnderlyingInteger()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;
            int32 Read() {
                int32 value = NativeCallingConvention.Cdecl;
                return value;
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void NativeCallConvPreservesConventionOnBlittableFunctionPointerFields()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct CallbackHolder {
                public [Terrum.Interop.NativeCallConv(
                    Terrum.Interop.NativeCallingConvention.Cdecl)] fnptr<int32> Callback;
            }
            public int32 Invoke() { return 0; }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var fieldType = program.Structures.Single().Fields.Single().Type;
        var signature = program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(fieldType)];
        Assert.That(signature.CallingConvention, Is.EqualTo(NativeCallingConvention.Cdecl));
    }

    [TestCase("SelectMode()")]
    [TestCase("RuntimeConvention()")]
    public void NativeCallConvTypeUseForwardingRejectsWrongTypesAndRuntimeCalls(
        string conventionExpression)
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var source = $$"""
            using Terrum.Interop;
            alias ABI = NativeCallingConvention;
            alias Mode = NativeImportResolutionMode;

            [ConstEval]
            internal Mode SelectMode() { return Mode.Dynamic; }
            internal ABI RuntimeConvention() { return ABI.Cdecl; }

            int32 Invoke(
                [NativeCallConv({{conventionExpression}})] fnptr<int32> callback) {
                return 0;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), reference);

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void NativeCallConvTypeUseForwardingRemapsNestedFunctionPointers()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;
            alias ABI = NativeCallingConvention;

            [ConstEval]
            internal ABI Forward(ABI value, bool choose) {
                ABI copy = value;
                return choose ? copy : ABI.Stdcall;
            }

            int32 Invoke(fnptr<NativeAbi,
                ptr<[NativeCallConv(Forward(ABI.Cdecl, true))] fnptr<int32>>,
                int32> callback) {
                return 0;
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var outerType = program.Functions.Single(static function =>
            function.Name == "Invoke").Variables[0].Type;
        var outerSignature = program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(outerType)];
        var pointerType = outerSignature.ParameterTypes.Single();
        Assert.That(NativePointerTypes.IsType(pointerType), Is.True);
        var innerType = program.NativePointerTypes[NativePointerTypes.Index(pointerType)].PointeeType;
        var innerSignature = program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(innerType)];
        Assert.That(innerSignature.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Cdecl));
    }

    [Test]
    public void NativeCallConvKeepsEqualCarrierConventionsAsDistinctTypes()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            int32 Invoke(
                [Terrum.Interop.NativeCallConv(
                    Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32> unmanagedCallback,
                [Terrum.Interop.NativeCallConv(
                    Terrum.Interop.NativeCallingConvention.Cdecl)] fnptr<int32> cdeclCallback) {
                return 0;
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = compilation.Program!.Functions.Single();
        Assert.That(function.Variables[0].Type, Is.Not.EqualTo(function.Variables[1].Type));
        Assert.That(compilation.Program.NativeFunctionPointerSignatures, Has.Length.EqualTo(2));
    }

    [TestCase("[Terrum.Interop.NativeCallable]", "Unmanaged")]
    [TestCase("[Terrum.Interop.NativeCallable()]", "Unmanaged")]
    [TestCase("[Terrum.Interop.NativeCallable(CallConv: Terrum.Interop.NativeCallingConvention.Cdecl)]",
        "Cdecl")]
    public void NativeCallableNormalizesItsRegisteredEffectiveConvention(
        string attribute, string expectedConvention)
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var source = $$"""
            {{attribute}}
            public int32 Identity(int32 value) { return value; }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = compilation.Program!.Functions.Single();
        Assert.That(function.NativeCallableConvention?.ToString(), Is.EqualTo(expectedConvention));
        Assert.That(function.NativeFunctionPointerType, Is.Not.Null);
        var signature = compilation.Program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(function.NativeFunctionPointerType!.Value)];
        Assert.That(signature.CallingConvention.ToString(), Is.EqualTo(expectedConvention));
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(compilation.Program, ["Identity"]));
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(compilation.Program, "native-callable"));
    }

    [Test]
    public void NativeCallableAddressMatchesItsRegisteredFunctionPointerConvention()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable(
                CallConv: Terrum.Interop.NativeCallingConvention.Cdecl)]
            public int32 Identity(int32 value) { return value; }

            public int32 Invoke() {
                unsafe {
                    [Terrum.Interop.NativeCallConv(
                        Terrum.Interop.NativeCallingConvention.Cdecl)]
                    fnptr<int32, int32> callback = &Identity;
                    return callback(42);
                }
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var invoke = compilation.Program!.Functions.Single(static function =>
            function.Name == "Invoke");
        var unsafeBody = (BoundBlock)invoke.Body!.Statements.OfType<BoundUnsafe>().Single().Body;
        var callback = unsafeBody.Statements.OfType<BoundLocal>().Single();
        Assert.That(callback.Initializer, Is.TypeOf<BoundNativeFunctionAddress>());
        var indirectCall = unsafeBody.Statements.OfType<BoundReturn>().Single().Value;
        Assert.That(indirectCall, Is.TypeOf<BoundIndirectCall>());
    }

    [Test]
    public void NativeCallableConventionUsesOrdinaryConstEvalForwarding()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;
            alias ABI = NativeCallingConvention;

            [ConstEval]
            internal ABI Forward(ABI value, bool choose) {
                ABI copy = value;
                return choose ? copy : ABI.Stdcall;
            }

            [NativeCallable(CallConv: Forward(ABI.Cdecl, true))]
            public int32 Identity(int32 value) { return value; }

            public int32 Invoke() {
                unsafe {
                    [NativeCallConv(Forward(ABI.Cdecl, true))]
                    fnptr<int32, int32> callback = &Identity;
                    return callback(42);
                }
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var identity = program.Functions.Single(static function =>
            function.Name == "Identity");
        Assert.That(identity.NativeCallableConvention,
            Is.EqualTo(NativeCallingConvention.Cdecl));
        var functionPointer = identity.NativeFunctionPointerType!.Value;
        Assert.That(program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(functionPointer)].CallingConvention,
            Is.EqualTo(NativeCallingConvention.Cdecl));
        var invoke = program.Functions.Single(static function => function.Name == "Invoke");
        var unsafeBody = (BoundBlock)invoke.Body!.Statements.OfType<BoundUnsafe>().Single().Body;
        var callback = unsafeBody.Statements.OfType<BoundLocal>().Single();
        Assert.That(invoke.Variables[callback.Variable].Type, Is.EqualTo(functionPointer));
        Assert.That(NativeEmitter.EmitLibrary(program, ["Invoke"], NativeRuntimePolicy.FailFast),
            Is.Not.Empty);
    }

    [Test]
    public void NativeImportCallConvDefaultsAndNormalizesFromTheSelectedCoreProfile()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Cdecl)]
            public extern int32 Read(int32 value);
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var import = compilation.Program!.Functions.Single();
        Assert.That(import.DirectNativeImport!.ExportName, Is.EqualTo("Read"));
        Assert.That(import.DirectNativeImport.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Cdecl));
        Assert.That(import.DirectNativeImport.ResolutionMode,
            Is.EqualTo(NativeImportResolutionMode.Link));
        Assert.That(import.DirectNativeImport.ResolutionPolicy,
            Is.EqualTo(NativeImportResolutionPolicy.NotApplicable));
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(compilation.Program, ["Read"]));
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(compilation.Program, "native-import"));
    }

    [Test]
    public void NativeImportEnumsUseOrdinaryConstEvalForwarding()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;
            alias ABI = NativeCallingConvention;
            alias Mode = NativeImportResolutionMode;
            alias Policy = NativeImportResolutionPolicy;

            [ConstEval]
            internal ABI Forward(ABI value, bool choose) {
                ABI copy = value;
                return choose ? copy : ABI.Stdcall;
            }
            [ConstEval]
            internal Mode SelectMode(bool dynamic) {
                return dynamic ? Mode.Dynamic : Mode.Link;
            }
            [ConstEval]
            internal Policy SelectPolicy() {
                return Policy.CompatibilitySearch;
            }

            [NativeImport(
                "example.dll",
                EntryName: "Read",
                CallConv: Forward(ABI.Cdecl, true),
                ResolutionMode: SelectMode(true),
                ResolutionPolicy: SelectPolicy())]
            public extern int32 Read(int32 value);
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var import = compilation.Program!.Functions.Single(static function =>
            function.Name == "Read").DirectNativeImport!;
        Assert.That(import.CallingConvention, Is.EqualTo(NativeCallingConvention.Cdecl));
        Assert.That(import.ResolutionMode, Is.EqualTo(NativeImportResolutionMode.Dynamic));
        Assert.That(import.ResolutionPolicy,
            Is.EqualTo(NativeImportResolutionPolicy.CompatibilitySearch));
    }

    [TestCase("ABI.Cdecl")]
    [TestCase("RuntimeMode()")]
    public void NativeImportEnumForwardingRejectsWrongTypeAndRuntimeCalls(string modeExpression)
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var source = $$"""
            using Terrum.Interop;
            alias ABI = NativeCallingConvention;
            alias Mode = NativeImportResolutionMode;

            internal Mode RuntimeMode() { return Mode.Dynamic; }

            [NativeImport("example.dll", ResolutionMode: {{modeExpression}})]
            extern int32 Read();
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), reference);

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void NativeImportEnumForwardingRejectsPolicyForLinkMode()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;
            alias Mode = NativeImportResolutionMode;
            alias Policy = NativeImportResolutionPolicy;

            [ConstEval]
            internal Mode SelectMode() { return Mode.Link; }
            [ConstEval]
            internal Policy SelectPolicy() { return Policy.Default; }

            [NativeImport(
                "example.dll",
                ResolutionMode: SelectMode(),
                ResolutionPolicy: SelectPolicy())]
            extern int32 Read();
            """), reference);

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void NativeImportDefaultsCallConvAndEntryNameFromTheCoreProfile()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll")]
            extern int32 Read(int32 value);
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var import = compilation.Program!.Functions.Single();
        Assert.That(import.DirectNativeImport!.ExportName, Is.EqualTo("Read"));
        Assert.That(import.DirectNativeImport.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Unmanaged));
    }

    [Test]
    public void NativeImportAcceptsNamedLibraryAndEntryNames()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                LibraryName: "example.dll",
                EntryName: "Read",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            public extern int32 Read(int32 value);
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var import = compilation.Program!.Functions.Single();
        Assert.That(import.DirectNativeImport!.LibraryName, Is.EqualTo("example.dll"));
        Assert.That(import.DirectNativeImport.ExportName, Is.EqualTo("Read"));
        Assert.That(import.DirectNativeImport.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Unmanaged));
    }

    [TestCase("probe")]
    [TestCase("probe name")]
    [TestCase("libstdc++.so.6")]
    public void NativeImportPreservesSourceValidLibraryNames(string libraryName)
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
            [Terrum.Interop.NativeImport("{{libraryName}}", "Read")]
            extern int32 Read();
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program!.Functions.Single().DirectNativeImport!.LibraryName,
            Is.EqualTo(libraryName));
    }

    [Test]
    public void NativeImportRejectsInvalidSourceLibraryNames()
    {
        var invalidNames = new[] {
            "", ".", "..", "dir/probe", "dir\\probe", "C:probe",
            "probe\u0000", "probe\u001F", "probe\u007F", "probe\u0080", "проба",
        };
        foreach (var libraryName in invalidNames)
        {
            Assert.That(DirectNativeImportContract.IsValidSourceLibraryName(libraryName), Is.False,
                $"Unexpectedly accepted a library name of length {libraryName.Length}.");
        }
    }

    [Test]
    public void SourceLibraryNameValidationDoesNotWidenPrivateWindowsAdmission()
    {
        Assert.That(DirectNativeImportContract.IsValidSourceLibraryName("libstdc++.so.6"), Is.True);
        Assert.That(DirectNativeImportContract.IsValidPrivateWindowsLibraryName("libstdc++.so.6"), Is.False);
        Assert.That(DirectNativeImportContract.IsValidPrivateWindowsLibraryName("kernel32.dll"), Is.True);
    }

    [Test]
    public void PrivateModuleWriterRetainsItsWindowsLibraryNameLimit()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("libstdc++.so.6", "Read")]
            extern int32 Read();
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(compilation.Program!, ["Read"]));
    }

    [Test]
    public void SourceLibraryNamesPreserveSpacesAndAreNotCappedByPrivateLimits()
    {
        var overPrivateLimit = new string('a', 257);

        Assert.That(DirectNativeImportContract.IsValidSourceLibraryName(" "), Is.True);
        Assert.That(DirectNativeImportContract.IsValidSourceLibraryName(overPrivateLimit), Is.True);
        Assert.That(DirectNativeImportContract.IsValidPrivateWindowsLibraryName(
            $"{overPrivateLimit}.dll"), Is.False);
    }

    [Test]
    public void NativeImportRejectsPathsUsingTheSourceLibraryNameRule()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("dir/probe", "Read")]
            extern int32 Read();
            """), reference);

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"),
            Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void DynamicNativeImportDefaultsItsResolutionPolicy()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll", "Read",
                ResolutionMode: Terrum.Interop.NativeImportResolutionMode.Dynamic)]
            extern int32 Read(int32 value);
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var import = compilation.Program!.Functions.Single();
        Assert.That(import.DirectNativeImport!.ResolutionMode,
            Is.EqualTo(NativeImportResolutionMode.Dynamic));
        Assert.That(import.DirectNativeImport.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Unmanaged));
        Assert.That(import.DirectNativeImport.ResolutionPolicy,
            Is.EqualTo(NativeImportResolutionPolicy.Default));
    }

    [Test]
    public void NativeImportRejectsResolutionPolicyOnLinkMode()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll", "Read",
                ResolutionPolicy: Terrum.Interop.NativeImportResolutionPolicy.Default)]
            extern int32 Read(int32 value);
            """), reference);

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"),
            Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [TestCase("[Terrum.Interop.NativeImport(\"example.dll\", LibraryName: \"example.dll\")]")]
    [TestCase("[Terrum.Interop.NativeImport(\"example.dll\", CallConv: Terrum.Interop.NativeCallingConvention.Cdecl, CallConv: Terrum.Interop.NativeCallingConvention.Stdcall)]")]
    [TestCase("[Terrum.Interop.NativeImport(\"example.dll\", CallConv: Terrum.Interop.NativeCallingConvention.Cdecl, \"Read\")]")]
    [TestCase("[Terrum.Interop.NativeImport(\"example.dll\", Unsupported: 1)]")]
    public void NativeImportRejectsDuplicateUnknownOrOutOfOrderArguments(string attribute)
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse($"{attribute}\nextern int32 Read(int32 value);"), reference);

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"),
            Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void DynamicNativeImportResolutionIsRetainedAndNotDowngradedByTheEmitter()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll", "Read",
                ResolutionMode: Terrum.Interop.NativeImportResolutionMode.Dynamic,
                ResolutionPolicy: Terrum.Interop.NativeImportResolutionPolicy.CompatibilitySearch,
                CallConv: Terrum.Interop.NativeCallingConvention.Stdcall)]
            public extern int32 Read(int32 value);

            public int32 Invoke() {
                unsafe { return Read(1); }
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var import = compilation.Program!.Functions.Single(static function => function.Name == "Read");
        Assert.That(import.DirectNativeImport!.ResolutionMode,
            Is.EqualTo(NativeImportResolutionMode.Dynamic));
        Assert.That(import.DirectNativeImport.ResolutionPolicy,
            Is.EqualTo(NativeImportResolutionPolicy.CompatibilitySearch));
        Assert.That(import.DirectNativeImport.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Stdcall));
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(compilation.Program, ["Read"]));
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(compilation.Program, "native-import"));
        Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibraryWithNativeImports(
            compilation.Program, ["Invoke"], NativeRuntimePolicy.FailFast));
    }

    [Test]
    public void CdeclNativeImportEmitsThroughTheSelectedWindowsX64CallMapping()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "kernel32.dll", "GetCurrentProcessId",
                CallConv: Terrum.Interop.NativeCallingConvention.Cdecl)]
            extern uint32 GetCurrentProcessId();

            public int32 Invoke() {
                unsafe { return int32(GetCurrentProcessId()); }
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var image = NativeEmitter.EmitLibraryWithNativeImports(
            compilation.Program!, ["Invoke"], NativeRuntimePolicy.FailFast);
        Assert.That(image, Is.Not.Empty);
    }

    [TestCase("Unmanaged")]
    [TestCase("Cdecl")]
    [TestCase("Stdcall")]
    [TestCase("Vectorcall")]
    public void NativeCallConvPreservesEachClosedMember(string member)
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var source = $$"""
            public int32 Invoke(
                [Terrum.Interop.NativeCallConv(
                    Terrum.Interop.NativeCallingConvention.{{member}})] fnptr<int32> callback) {
                return 0;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var signature = compilation.Program!.NativeFunctionPointerSignatures.Single();
        Assert.That(signature.CallingConvention.ToString(), Is.EqualTo(member));
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(compilation.Program, ["Invoke"]));
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(compilation.Program, "native-callconv"));
    }

    [TestCase("Terrum.Interop.NativeCallingConvention.Unknown")]
    [TestCase("Other.NativeCallingConvention.Cdecl")]
    public void NativeCallConvRejectsUnselectedEnumMembers(string value)
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var source = $$"""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct CallbackHolder {
                public [Terrum.Interop.NativeCallConv({{value}})] fnptr<int32> Callback;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), reference);

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3005"),
            Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void NativeCallConvRequiresAnExplicitCoreRegistration()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct CallbackHolder {
                public [Terrum.Interop.NativeCallConv(
                    Terrum.Interop.NativeCallingConvention.Cdecl)] fnptr<int32> Callback;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3005"),
            Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void NativeCallConvDoesNotFallBackToSameNamedLocalTypes()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            namespace Terrum.Interop {
                public struct NativeCallingConvention { public int32 Value; }
            }
            namespace Consumer {
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct CallbackHolder {
                    public [Terrum.Interop.NativeCallConv(
                        Terrum.Interop.NativeCallingConvention.Cdecl)] fnptr<int32> Callback;
                }
                public int32 Invoke() { return 0; }
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void VectorcallLoweringRejectsTheUnsupportedWindowsX64Mapping()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Invoke(
                [Terrum.Interop.NativeCallConv(
                    Terrum.Interop.NativeCallingConvention.Vectorcall)] fnptr<int32> callback) {
                unsafe { return callback(); }
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibrary(
            compilation.Program!, ["Invoke"], NativeRuntimePolicy.FailFast));
    }

    [TestCase("Unmanaged")]
    [TestCase("Cdecl")]
    [TestCase("Stdcall")]
    public void RegisteredNativeCallableConventionExecutesIndirectlyOnWindowsX64(string convention)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var source = $$"""
            using Terrum.Interop;
            [NativeCallable(CallConv: NativeCallingConvention.{{convention}})]
            public int32 Identity(int32 value) { return value; }

            int32 Main() {
                unsafe {
                    [NativeCallConv(NativeCallingConvention.{{convention}})]
                    fnptr<int32, int32> callback = &Identity;
                    return callback(42);
                }
            }
            """;
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration = CreateRegistration(),
        };
        var compilation = Compilation.Create(SyntaxTree.Parse(source), options);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!, NativeRuntimePolicy.FailFast),
            Is.EqualTo(42));
    }

    [TestCase("Cdecl")]
    [TestCase("Stdcall")]
    public void RegisteredNativeImportConventionExecutesOnWindowsX64(string convention)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var source = $$"""
            using Terrum.Interop;
            [NativeImport("kernel32.dll", "GetCurrentThreadId",
                CallConv: NativeCallingConvention.{{convention}})]
            extern unsafe uint32 GetCurrentThreadId();

            int32 Main() {
                unsafe {
                    if (GetCurrentThreadId() == 0) { return 1; }
                    return 0;
                }
            }
            """;
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.Create(SyntaxTree.Parse(source), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var import = compilation.Program!.Functions.Single(static function =>
            function.Name == "GetCurrentThreadId");
        var expectedConvention = convention switch {
            "Cdecl" => NativeCallingConvention.Cdecl,
            "Stdcall" => NativeCallingConvention.Stdcall,
            _ => throw new ArgumentOutOfRangeException(nameof(convention)),
        };
        Assert.That(import.DirectNativeImport!.CallingConvention,
            Is.EqualTo(expectedConvention));
        Assert.That(NativeJit.Execute(compilation.Program, NativeRuntimePolicy.FailFast),
            Is.Zero);
    }

    internal static NativeCoreRegistrationSet CreateRegistration()
    {
        var conventions = CreateEnum("NativeCallingConvention",
            "Terrum.Interop.NativeCallingConvention",
            ("Unmanaged", 1), ("Cdecl", 2), ("Stdcall", 3), ("Vectorcall", 4));
        var resolutionModes = CreateEnum("NativeImportResolutionMode",
            "Terrum.Interop.NativeImportResolutionMode", ("Link", 1), ("Dynamic", 2));
        var resolutionPolicies = CreateEnum("NativeImportResolutionPolicy",
            "Terrum.Interop.NativeImportResolutionPolicy",
            ("Default", 1), ("CompatibilitySearch", 2));

        return NativeCoreRegistrationSet.Create("consumer.module", conventions, resolutionModes,
            resolutionPolicies,
            CreateSchema("NativeImport", "Terrum.Interop.NativeImport",
                CoreNativeAttributeRole.NativeImport, conventions.Identity,
                resolutionModes.Identity, resolutionPolicies.Identity),
            CreateSchema("NativeCallable", "Terrum.Interop.NativeCallable",
                CoreNativeAttributeRole.NativeCallable, conventions.Identity),
            CreateSchema("NativeCallConv", "Terrum.Interop.NativeCallConv",
                CoreNativeAttributeRole.NativeCallConv, conventions.Identity),
            CreateSchema("NativeExport", "Terrum.Interop.NativeExport",
                CoreNativeAttributeRole.NativeExport));
    }

    private static CoreEnumDefinition CreateEnum(string exportKey,
        string qualifiedName, params (string Name, int Value)[] members)
    {
        var enumIdentity = Identity("core.native", exportKey, CoreDeclarationKind.EnumType);
        var memberDefinitions = ImmutableArray.CreateBuilder<CoreEnumMemberDefinition>(members.Length);
        foreach (var member in members)
        {
            memberDefinitions.Add(new CoreEnumMemberDefinition(
                Identity("core.native", $"{exportKey}.{member.Name}", CoreDeclarationKind.EnumMember),
                enumIdentity, member.Name, member.Value, IsPublic: true));
        }

        return new CoreEnumDefinition(enumIdentity, qualifiedName, PrimitiveType.Int32,
            memberDefinitions.MoveToImmutable(), GenericArity: 0, IsPublic: true,
            IsPrimary: true, IsFlags: false);
    }

    private static BoundEnumDefinition CreateUnrelatedEnum()
    {
        var identity = Identity("sample.enums", "Sample.Palette", CoreDeclarationKind.EnumType);
        var members = ImmutableArray.Create(
            new BoundEnumMemberDefinition(
                Identity("sample.enums", "Sample.Palette.Minimum", CoreDeclarationKind.EnumMember),
                identity, "Minimum", int.MinValue, IsPublic: true),
            new BoundEnumMemberDefinition(
                Identity("sample.enums", "Sample.Palette.Maximum", CoreDeclarationKind.EnumMember),
                identity, "Maximum", int.MaxValue, IsPublic: true),
            new BoundEnumMemberDefinition(
                Identity("sample.enums", "Sample.Palette.MinusOne", CoreDeclarationKind.EnumMember),
                identity, "MinusOne", -1, IsPublic: true));
        return new BoundEnumDefinition(identity, "Sample.Palette", PrimitiveType.Int32,
            members, IsPublic: true, IsPrimary: true, IsFlags: false);
    }

    private static CoreNativeAttributeSchema CreateSchema(string exportKey,
        string qualifiedName, CoreNativeAttributeRole role,
        CoreDeclarationIdentity? callingConventionEnum = null,
        CoreDeclarationIdentity? resolutionModeEnum = null,
        CoreDeclarationIdentity? resolutionPolicyEnum = null) =>
        new(Identity("core.native", exportKey, CoreDeclarationKind.AttributeSchema),
            qualifiedName, role, callingConventionEnum, resolutionModeEnum,
            resolutionPolicyEnum, GenericArity: 0, IsPublic: true,
            IsPrimary: true, IsRepeatable: false);

    private static CoreDeclarationIdentity Identity(string provider, string exportKey,
        CoreDeclarationKind kind) => new(provider, exportKey, kind);
}
