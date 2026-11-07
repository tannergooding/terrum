using System.Buffers.Binary;
using System;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Modules;

[TestFixture]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class PrivateNominalEnumTests
{
    [Test]
    public void PrivateEnumDirectoryRoundTripsAndBindsWithoutCoreTokens()
    {
        var colorIdentity = new CoreDeclarationIdentity("CoreNative",
            "Color", CoreDeclarationKind.EnumType);
        var colorMemberIdentity = new CoreDeclarationIdentity("CoreNative",
            "Color.Red", CoreDeclarationKind.EnumMember);
        var colorEnum = new BoundEnumDefinition(
            new BoundEnumDeclarationIdentity.Core(colorIdentity), "Color",
            PrimitiveType.Int32, [
                new BoundEnumMemberDefinition(
                    new BoundEnumMemberIdentity.Core(colorMemberIdentity),
                    new BoundEnumDeclarationIdentity.Core(colorIdentity),
                    "Red", 7, true, "member.red"),
            ], true, true, false);
        var sizeIdentity = new CoreDeclarationIdentity("CoreNative",
            "Size", CoreDeclarationKind.EnumType);
        var sizeMemberIdentity = new CoreDeclarationIdentity("CoreNative",
            "Size.Small", CoreDeclarationKind.EnumMember);
        var sizeEnum = new BoundEnumDefinition(
            new BoundEnumDeclarationIdentity.Core(sizeIdentity),
            "Size", PrimitiveType.Int32, [
                new BoundEnumMemberDefinition(
                    new BoundEnumMemberIdentity.Core(sizeMemberIdentity),
                    new BoundEnumDeclarationIdentity.Core(sizeIdentity),
                    "Small", 1, true, "member.small"),
            ], true, true, false);
        var unusedIdentity = new CoreDeclarationIdentity("CoreNative",
            "Unused", CoreDeclarationKind.EnumType);
        var unusedEnum = new BoundEnumDefinition(
            new BoundEnumDeclarationIdentity.Core(unusedIdentity), "Unused",
            PrimitiveType.Int32, [], true, true, false);
        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            enumDefinitions: [colorEnum, sizeEnum, unusedEnum]);
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public Color GetColor() { return Color.Red; }
                public Size GetSize() { return Size.Small; }
                public void Accept(ref<Color> value) {}
                public unsafe void AcceptPointer(ptr<Color> value) {}
                public int32 UsePointer() {
                    unsafe { AcceptPointer(null); }
                    return 0;
                }
                """),
            reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program!.NativePointerTypes,
            Has.Length.EqualTo(1));

        var colorType = BoundEnumTypes.Type(0);
        var sizeType = BoundEnumTypes.Type(1);
        var inputs = PrivateModuleIdentityInputs.Create(
            compilation.Program!, "PrivateModule", [],
            [
                new PrivateNominalImport(sizeType, "types.size.proxy",
                    "CoreNative", "Size"),
                new PrivateNominalImport(colorType, "types.color.proxy",
                    "CoreNative", "Color"),
            ],
            [
                new PrivateEnumMemberAssignment(sizeType, sizeIdentity, 0,
                    sizeMemberIdentity, "member.small"),
                new PrivateEnumMemberAssignment(colorType, colorIdentity, 0,
                    colorMemberIdentity, "member.red"),
            ]);
        var producer = ModuleCompiler.CreateProducer(compilation.Program!,
            ["GetColor", "GetSize", "AcceptPointer", "UsePointer"], inputs);
        var persistedReference = ModuleCompiler.CreateReference(producer);
        Assert.That(persistedReference.NativePointerTypes,
            Has.Length.EqualTo(1));
        var enumOffsets = GetPrivateEnumOffsets(producer);
        RejectMalformedEnum(producer, bytes =>
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(enumOffsets.EnumCount, sizeof(int)), 0));
        RejectMalformedEnum(producer, bytes =>
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(enumOffsets.OriginReference, sizeof(uint)), 0));
        RejectMalformedEnum(producer, bytes =>
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(enumOffsets.UnderlyingType, sizeof(int)),
                (int)PrimitiveType.Int64));
        RejectMalformedEnum(producer, bytes =>
            bytes[enumOffsets.Flags] = 0x80);
        RejectMalformedEnum(producer, bytes =>
            bytes[enumOffsets.ReservedByte] = 1);
        RejectMalformedEnum(producer, bytes =>
            BinaryPrimitives.WriteUInt16LittleEndian(
                bytes.AsSpan(enumOffsets.ReservedWord, sizeof(ushort)), 1));
        RejectMalformedEnum(producer, bytes =>
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(enumOffsets.BlittableTargetRequirement,
                    sizeof(int)), 2));
        RejectMalformedEnum(producer, bytes =>
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(enumOffsets.MemberCount, sizeof(int)),
                int.MaxValue));
        RejectMalformedEnum(producer, bytes =>
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(enumOffsets.MemberKeyLength, sizeof(int)),
                int.MaxValue));
        RejectMalformedEnum(producer, bytes =>
            bytes[enumOffsets.MemberKeyBytes] = 0);
        RejectMalformedEnum(producer, bytes =>
            bytes[enumOffsets.MemberKeyBytes] = 0xff);
        var nullPointerPayload = GetNullPointerPayloadOffset(producer,
            NativePointerTypes.Type(0));
        RejectMalformedEnum(producer, bytes =>
            BinaryPrimitives.WriteInt64LittleEndian(
                bytes.AsSpan(nullPointerPayload, sizeof(long)), 1));

        Assert.That(persistedReference.EnumDefinitions.Select(
                static definition => definition.Name),
            Is.EqualTo(["Size", "Color"]));
        Assert.That(persistedReference.CheckedReferenceTypes, Is.Empty);
        Assert.That(persistedReference.NativePointerTypes,
            Has.Length.EqualTo(1));
        Assert.That(persistedReference.NativePointerTypes[0].PointeeType,
            Is.EqualTo(BoundEnumTypes.Type(1)));
        var persistedColor = persistedReference.EnumDefinitions.Single(
            static definition => definition.Name == "Color");

        Assert.That(persistedColor.Identity,
            Is.TypeOf<BoundEnumDeclarationIdentity.Private>());
        Assert.That(persistedColor.Members.Single().Identity,
            Is.TypeOf<BoundEnumMemberIdentity.Private>());
        Assert.That(persistedColor.Members.Single().DeclarationKey,
            Is.EqualTo("member.red"));
        Assert.That(persistedReference.PrivateNominalTypeOrigins
                .Single(static origin =>
                    origin.LocalDeclarationKey == "types.color.proxy")
                .SelectedModuleIdentity,
            Is.EqualTo("CoreNative"));
        Assert.That(persistedReference.Functions.Single(
                static function => function.Name == "GetColor").ReturnType,
            Is.EqualTo(BoundEnumTypes.Type(1)));
        Assert.That(persistedReference.Functions.Single(
                static function => function.Name == "GetSize").ReturnType,
            Is.EqualTo(BoundEnumTypes.Type(0)));

        var consumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public Color GetColorAgain() { return Color.Red; }
                public Size GetSizeAgain() { return Size.Small; }
                """),
            persistedReference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        Assert.That(consumer.Program!.EnumDefinitions.Length, Is.EqualTo(2));

        var enumOnlyReference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            enumDefinitions: persistedReference.EnumDefinitions,
            privateNominalTypeOrigins:
                persistedReference.PrivateNominalTypeOrigins);
        var valueCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public Color GetColor() { return Color.Red; }
                public Size GetSize() { return Size.Small; }
                public unsafe void AcceptPointer(ptr<Color> value) {}
                public int32 UsePointer() {
                    unsafe { AcceptPointer(null); }
                    return 0;
                }
                """),
            enumOnlyReference);
        Assert.That(valueCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, valueCompilation.Diagnostics));
        var valueProducer = ModuleCompiler.CreateProducer(
            valueCompilation.Program!, ["GetColor", "GetSize"]);
        var valueReference = ModuleCompiler.CreateReference(valueProducer);
        Assert.That(valueReference.EnumDefinitions.Select(
                static definition => definition.Name),
            Is.EqualTo(["Size", "Color"]));
        Assert.That(valueReference.Functions.Single(
                static function => function.Name == "GetColor").ReturnType,
            Is.EqualTo(BoundEnumTypes.Type(1)));
        Assert.That(valueReference.Functions.Single(
                static function => function.Name == "GetSize").ReturnType,
            Is.EqualTo(BoundEnumTypes.Type(0)));

        var priorityIdentity = new CoreDeclarationIdentity("OtherNative",
            "Priority", CoreDeclarationKind.EnumType);
        var priorityReference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            enumDefinitions: [
                new BoundEnumDefinition(
                    new BoundEnumDeclarationIdentity.Core(priorityIdentity),
                    "Priority", PrimitiveType.Int32, [], true, true, false),
            ]);
        var referenceSet = ModuleCompiler.CreateReferenceSetFromReferences(
            [priorityReference, valueReference]);
        Assert.That(referenceSet.EnumDefinitions.Select(
                static definition => definition.Name),
            Is.EqualTo(["Priority", "Size", "Color"]));
        Assert.That(referenceSet.Functions.Single(
                static function => function.Name == "GetColor").ReturnType,
            Is.EqualTo(BoundEnumTypes.Type(2)));
        Assert.That(referenceSet.Functions.Single(
                static function => function.Name == "GetSize").ReturnType,
            Is.EqualTo(BoundEnumTypes.Type(1)));
        var graphReference = ModuleCompiler.MergeGraphReferences(
            [priorityReference, valueReference]);
        Assert.That(graphReference.EnumDefinitions.Select(
                static definition => definition.Name),
            Is.EqualTo(["Priority", "Size", "Color"]));
        Assert.That(graphReference.Functions.Single(
                static function => function.Name == "GetColor").ReturnType,
            Is.EqualTo(BoundEnumTypes.Type(2)));

        var nativePackage = NativeModuleCompiler.CreateProducer(
            compilation.Program!, ["GetColor", "GetSize", "AcceptPointer"],
            "PrivateModule", NativeRuntimePolicy.FailFast, inputs);
        var neutralCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public int32 Neutral() { return 0; }"));
        Assert.That(neutralCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                neutralCompilation.Diagnostics));
        var neutralPackage = NativeModuleCompiler.CreateProducer(
            neutralCompilation.Program!, ["Neutral"], "NeutralModule",
            NativeRuntimePolicy.FailFast);
        foreach (var nativeReference in new[] {
            NativeModuleCompiler.CreateReferenceSet(
                [neutralPackage, nativePackage]),
            NativeModuleCompiler.CreateGraphReference(
                [neutralPackage, nativePackage]),
        })
        {
            Assert.That(nativeReference.EnumDefinitions.Select(
                    static definition => definition.Name),
                Is.EqualTo(["Size", "Color"]));
            Assert.That(nativeReference.Functions.Single(
                    static function => function.Name == "GetColor")
                    .ReturnType,
                Is.EqualTo(BoundEnumTypes.Type(1)));
            Assert.That(nativeReference.NativePointerTypes,
                Has.Length.EqualTo(1));
            Assert.That(nativeReference.NativePointerTypes[0].PointeeType,
                Is.EqualTo(BoundEnumTypes.Type(1)));
        }

        var rereadReference = ModuleCompiler.CreateReference(valueProducer);
        var colorOnlyReference = new CompilationReference([], [
            valueReference.Functions.Single(
                static function => function.Name == "GetColor"),
        ], valueReference.RuntimePolicy,
            enumDefinitions: valueReference.EnumDefinitions,
            privateNominalTypeOrigins:
                valueReference.PrivateNominalTypeOrigins);
        var sizeOnlyReference = new CompilationReference([], [
            rereadReference.Functions.Single(
                static function => function.Name == "GetSize"),
        ], rereadReference.RuntimePolicy,
            enumDefinitions: rereadReference.EnumDefinitions,
            privateNominalTypeOrigins:
                rereadReference.PrivateNominalTypeOrigins);
        var rereadSet = ModuleCompiler.CreateReferenceSetFromReferences(
            [colorOnlyReference, sizeOnlyReference]);
        Assert.That(rereadSet.EnumDefinitions, Has.Length.EqualTo(2));
        Assert.That(rereadSet.PrivateNominalTypeOrigins,
            Has.Length.EqualTo(2));
        Assert.That(rereadSet.Functions.Single(
                static function => function.Name == "GetColor").ReturnType,
            Is.EqualTo(BoundEnumTypes.Type(1)));
        Assert.That(rereadSet.Functions.Single(
                static function => function.Name == "GetSize").ReturnType,
            Is.EqualTo(BoundEnumTypes.Type(0)));

        var ownerPackage = CreateOwnedEnumProvider(colorEnum, sizeEnum);
        var ownerReference = ModuleCompiler.CreateReference(ownerPackage);
        var ownedColor = ownerReference.EnumDefinitions.Single(
            static definition => definition.Name == "Color");
        Assert.That(ownedColor.Identity,
            Is.TypeOf<BoundEnumDeclarationIdentity.Private>());
        Assert.That(ownerReference.PrivateNominalTypeOrigins.Single(
                static origin => origin.LocalDeclarationKey == "Color")
                .OriginKind,
            Is.EqualTo(BoundPrivateNominalOriginKind.OwnedDefinition));
        var ownerSource = Compilation.CreateLibrary(
            SyntaxTree.Parse(
                "public Color GetColorAgain() { return Color.Red; }"),
            ownerReference);
        Assert.That(ownerSource.HasErrors, Is.False,
            string.Join(Environment.NewLine, ownerSource.Diagnostics));

        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                int32 Main() {
                    return UsePointer();
                }
                """),
            persistedReference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var pointerConsumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, ownerPackage);
        Assert.DoesNotThrow(() =>
            ModuleCompiler.Link(pointerConsumer, ownerPackage));

        var changedColor = colorEnum with {
            Members = [colorEnum.Members.Single() with { Value = 8 }],
        };
        var changedOwnerPackage = CreateOwnedEnumProvider(changedColor,
            sizeEnum);
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateConsumer(consumerCompilation.Program!,
                changedOwnerPackage));

        var sourceProgram = compilation.Program!;
        var sourceFunction = sourceProgram.Functions.Single(
            static function => function.Name == "AcceptPointer");
        var functionPointerSignature =
            new NativeFunctionPointerSignatureDefinition(
                NativeCallingConvention.Native, colorType, [colorType]);
        var acceptFunctionPointer = sourceFunction with {
            Name = "AcceptFunctionPointer",
            Variables = sourceFunction.Variables.SetItem(0,
                sourceFunction.Variables[0] with {
                    Type = NativeFunctionPointerTypes.Type(0),
                }),
        };
        var functionPointerProgram = sourceProgram with {
            EnumDefinitions = [.. sourceProgram.EnumDefinitions.Select(
                definition => definition.Name == "Color"
                    ? definition with {
                        BlittableTargetRequirement
                            = FixedLayoutStorage.WindowsX64TargetRequirement,
                    }
                    : definition)],
            Functions = [
                sourceProgram.Functions.Single(
                    static function => function.Name == "GetColor"),
                sourceProgram.Functions.Single(
                    static function => function.Name == "GetSize"),
                acceptFunctionPointer,
            ],
            NativePointerTypes = [],
            CheckedReferenceTypes = [],
            NativeFunctionPointerSignatures = [
                functionPointerSignature,
            ],
        };
        var functionPointerInputs
            = PrivateModuleIdentityInputs.Create(functionPointerProgram,
                inputs.UsingModuleIdentity, inputs.Definitions,
                inputs.Imports, inputs.EnumMembers);
        var functionPointerProducer = ModuleCompiler.CreateProducer(
            functionPointerProgram,
            ["GetColor", "GetSize", "AcceptFunctionPointer"],
            functionPointerInputs);
        var functionPointerReference
            = ModuleCompiler.CreateReference(functionPointerProducer);
        Assert.That(functionPointerReference
                .NativeFunctionPointerSignatures,
            Has.Length.EqualTo(1));
        Assert.That(functionPointerReference
                .NativeFunctionPointerSignatures[0].ReturnType,
            Is.EqualTo(BoundEnumTypes.Type(1)));
        Assert.That(functionPointerReference
                .NativeFunctionPointerSignatures[0].ParameterTypes,
            Is.EqualTo([BoundEnumTypes.Type(1)]));
        Assert.That(functionPointerReference.EnumDefinitions.Single(
                static definition => definition.Name == "Color")
                .Identity,
            Is.TypeOf<BoundEnumDeclarationIdentity.Private>());
        var functionPointerConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return 0; }"));
        Assert.That(functionPointerConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                functionPointerConsumerCompilation.Diagnostics));
        var functionPointerConsumer = ModuleCompiler.CreateConsumer(
            functionPointerConsumerCompilation.Program!,
            functionPointerProducer);
        Assert.That(functionPointerConsumer, Is.Not.Empty);
        foreach (var combinedReference in new[] {
            ModuleCompiler.CreateReferenceSetFromReferences(
                [priorityReference, functionPointerReference]),
            ModuleCompiler.MergeGraphReferences(
                [priorityReference, functionPointerReference]),
        })
        {
            Assert.That(combinedReference.EnumDefinitions.Select(
                    static definition => definition.Name),
                Is.EqualTo(["Priority", "Size", "Color"]));
            Assert.That(combinedReference
                    .NativeFunctionPointerSignatures.Single().ReturnType,
                Is.EqualTo(BoundEnumTypes.Type(2)));
            Assert.That(combinedReference
                    .NativeFunctionPointerSignatures.Single()
                    .ParameterTypes,
                Is.EqualTo([BoundEnumTypes.Type(2)]));
            Assert.That(combinedReference.Functions.Single(
                    static function =>
                        function.Name == "AcceptFunctionPointer")
                    .Variables[0].Type,
                Is.EqualTo(NativeFunctionPointerTypes.Type(0)));
        }

        var pointerReference = new CompilationReference([], [],
            persistedReference.RuntimePolicy,
            nativePointerTypes: persistedReference.NativePointerTypes,
            enumDefinitions: persistedReference.EnumDefinitions,
            privateNominalTypeOrigins:
                persistedReference.PrivateNominalTypeOrigins);
        foreach (var combinedReference in new[] {
            ModuleCompiler.CreateReferenceSetFromReferences(
                [priorityReference, pointerReference]),
            ModuleCompiler.MergeGraphReferences(
                [priorityReference, pointerReference]),
        })
        {
            Assert.That(combinedReference.EnumDefinitions.Select(
                    static definition => definition.Name),
                Is.EqualTo(["Priority", "Size", "Color"]));
            Assert.That(combinedReference.NativePointerTypes,
                Has.Length.EqualTo(1));
            Assert.That(combinedReference.NativePointerTypes[0].PointeeType,
                Is.EqualTo(BoundEnumTypes.Type(2)));
        }
    }

    [Test]
    public void UncalledNativeParameterEnumRootRetainsPrivateProviderExpectation()
    {
        var colorIdentity = new CoreDeclarationIdentity("CoreNative",
            "Color", CoreDeclarationKind.EnumType);
        var colorMemberIdentity = new CoreDeclarationIdentity("CoreNative",
            "Color.Red", CoreDeclarationKind.EnumMember);
        var colorEnum = new BoundEnumDefinition(
            new BoundEnumDeclarationIdentity.Core(colorIdentity), "Color",
            PrimitiveType.Int32, [
                new BoundEnumMemberDefinition(
                    new BoundEnumMemberIdentity.Core(colorMemberIdentity),
                    new BoundEnumDeclarationIdentity.Core(colorIdentity),
                    "Red", 7, true, "member.red"),
            ], true, true, false) {
            BlittableTargetRequirement
                = FixedLayoutStorage.WindowsX64TargetRequirement,
        };
        var sizeIdentity = new CoreDeclarationIdentity("CoreNative",
            "Size", CoreDeclarationKind.EnumType);
        var sizeMemberIdentity = new CoreDeclarationIdentity("CoreNative",
            "Size.Small", CoreDeclarationKind.EnumMember);
        var sizeEnum = new BoundEnumDefinition(
            new BoundEnumDeclarationIdentity.Core(sizeIdentity), "Size",
            PrimitiveType.Int32, [
                new BoundEnumMemberDefinition(
                    new BoundEnumMemberIdentity.Core(sizeMemberIdentity),
                    new BoundEnumDeclarationIdentity.Core(sizeIdentity),
                    "Small", 1, true, "member.small"),
            ], true, true, false);
        var enumProvider = CreateOwnedEnumProvider(colorEnum, sizeEnum);
        var enumReference = ModuleCompiler.CreateReference(enumProvider);
        var colorDefinition = BoundEnumDefinition.RemapPrivateOccurrence(
            enumReference.EnumDefinitions.Single(
                static definition => definition.Name == "Color"),
            BoundEnumTypes.Type(0));
        var colorOrigin = ((BoundEnumDeclarationIdentity.Private)
            colorDefinition.Identity).Value.Origin;
        var enumOnlyReference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            enumDefinitions: [colorDefinition],
            privateNominalTypeOrigins: [colorOrigin]);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("sensor.dll", "ReadColor")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            public extern int32 ReadColor(ptr<Color> color);
            """), enumOnlyReference,
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var colorType = BoundEnumTypes.Type(program.EnumDefinitions
            .Select((definition, index) => (definition, index))
            .Single(static item => item.definition.Name == "Color").index);
        var inputs = PrivateModuleIdentityInputs.Create(program,
            "EnumNativeConsumer", [], [
                new PrivateNominalImport(colorType, "types.color.proxy",
                    "CoreNative", "Color"),
            ]);
        var artifact = ModuleCompiler.CreateProducer(program,
            ["ReadColor"], inputs);
        var reference = ModuleCompiler.CreateReference(artifact);
        var imported = reference.Functions.Single(
            static function => function.Name == "ReadColor");

        Assert.That(imported.DirectNativeImport, Is.Not.Null);
        var importedColorType = BoundEnumTypes.Type(
            reference.EnumDefinitions.Select((definition, index)
                => (definition, index))
                .Single(static item => item.definition.Name == "Color")
                .index);
        Assert.That(imported.DirectNativeImport!.Parameters.Single()
                .PointeeType,
            Is.EqualTo(importedColorType));
        Assert.That(reference.PrivateNominalTypeOrigins.Single(
                static origin => origin.UsingModuleIdentity
                    == "EnumNativeConsumer").OriginKind,
            Is.EqualTo(BoundPrivateNominalOriginKind.ImportedExpectation));

        var reboundCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(
                "public Color GetColorAgain() { return Color.Red; }"),
            reference);
        Assert.That(reboundCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reboundCompilation.Diagnostics));
        var reboundProgram = reboundCompilation.Program
            ?? throw new AssertionException(
                "Expected a rebound enum program.");
        var reboundColorType = BoundEnumTypes.Type(
            reboundProgram.EnumDefinitions.Select((definition, index)
                => (definition, index))
                .Single(static item => item.definition.Name == "Color")
                .index);
        var reboundInputs = PrivateModuleIdentityInputs.Create(
            reboundProgram, "EnumNativeConsumerAgain", [], [
                new PrivateNominalImport(reboundColorType,
                    "types.color.rebound", "CoreNative", "Color"),
            ]);
        var reboundProducer = ModuleCompiler.CreateProducer(
            reboundProgram, ["GetColorAgain"], reboundInputs);
        var reboundReference = ModuleCompiler.CreateReference(
            reboundProducer);
        Assert.That(reboundReference.Functions.Single(
                static function => function.Name == "GetColorAgain")
                .ReturnType,
            Is.EqualTo(BoundEnumTypes.Type(0)));

        var priorityIdentity = new CoreDeclarationIdentity("OtherNative",
            "Priority", CoreDeclarationKind.EnumType);
        var priorityReference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            enumDefinitions: [
                new BoundEnumDefinition(
                    new BoundEnumDeclarationIdentity.Core(priorityIdentity),
                    "Priority", PrimitiveType.Int32, [], true, true, false),
            ]);
        foreach (var merged in new[] {
            ModuleCompiler.CreateReferenceSetFromReferences(
                [priorityReference, reference]),
            ModuleCompiler.MergeGraphReferences(
                [priorityReference, reference]),
        })
        {
            var mergedColorType = BoundEnumTypes.Type(
                merged.EnumDefinitions.Select((definition, index)
                    => (definition, index))
                    .Single(static item =>
                        item.definition.Name == "Color").index);
            Assert.That(merged.Functions.Single(
                    static function => function.Name == "ReadColor")
                    .DirectNativeImport!.Parameters.Single().PointeeType,
                Is.EqualTo(mergedColorType));
        }

        foreach (var providerBound in new[] {
            ModuleCompiler.CreateReferenceSetFromReferences(
                [enumReference, reference]),
            ModuleCompiler.MergeGraphReferences(
                [enumReference, reference]),
        })
        {
            var providerColorType = BoundEnumTypes.Type(
                providerBound.EnumDefinitions.Select((definition, index)
                    => (definition, index))
                    .Single(static item =>
                        item.definition.Name == "Color").index);
            Assert.That(providerBound.Functions.Single(
                    static function => function.Name == "ReadColor")
                    .DirectNativeImport!.Parameters.Single().PointeeType,
                Is.EqualTo(providerColorType));
        }

        var changedColor = colorEnum with {
            Members = [colorEnum.Members.Single() with { Value = 8 }],
        };
        var changedEnumReference = ModuleCompiler.CreateReference(
            CreateOwnedEnumProvider(changedColor, sizeEnum));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateReferenceSetFromReferences(
                [changedEnumReference, reference]));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.MergeGraphReferences(
                [changedEnumReference, reference]));
    }

    private static byte[] CreateOwnedEnumProvider(
        BoundEnumDefinition colorEnum, BoundEnumDefinition sizeEnum)
    {
        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            enumDefinitions: [colorEnum, sizeEnum]);
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public Color GetColor() { return Color.Red; }
                public Size GetSize() { return Size.Small; }
                public unsafe void AcceptPointer(ptr<Color> value) {}
                public int32 UsePointer() {
                    unsafe { AcceptPointer(null); }
                    return 0;
                }
                """),
            reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var colorType = BoundEnumTypes.Type(program.EnumDefinitions
            .Select((definition, index) => (definition, index))
            .Single(static item => item.definition.Name == "Color").index);
        var sizeType = BoundEnumTypes.Type(program.EnumDefinitions
            .Select((definition, index) => (definition, index))
            .Single(static item => item.definition.Name == "Size").index);
        var assignments = program.EnumDefinitions.SelectMany(
                (definition, index) => {
                    if (definition.Identity
                            is not BoundEnumDeclarationIdentity.Core
                                enumIdentity)
                    {
                        throw new InvalidOperationException(
                            "The provider source adapter requires Core enum identities.");
                    }
                    return definition.Members.Select((member, ordinal) => {
                        if (member.Identity
                                is not BoundEnumMemberIdentity.Core
                                    memberIdentity)
                        {
                            throw new InvalidOperationException(
                                "The provider source adapter requires Core member identities.");
                        }
                        return new PrivateEnumMemberAssignment(
                            BoundEnumTypes.Type(index),
                            enumIdentity.Value, ordinal,
                            memberIdentity.Value, member.DeclarationKey);
                    });
                }).ToArray();
        var inputs = PrivateModuleIdentityInputs.Create(program,
            "CoreNative", [
                new PrivateNominalDefinition(sizeType, "Size",
                    IsExported: true),
                new PrivateNominalDefinition(colorType, "Color",
                    IsExported: true),
            ], [], assignments);
        return ModuleCompiler.CreateProducer(program,
            ["GetColor", "GetSize", "AcceptPointer", "UsePointer"],
            inputs);
    }

    private static void RejectMalformedEnum(byte[] artifact,
        Action<byte[]> corrupt)
    {
        var malformed = artifact.ToArray();
        corrupt(malformed);
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(malformed));
    }

    private static int GetNullPointerPayloadOffset(byte[] artifact,
        PrimitiveType pointerType)
    {
        for (var offset = 0; offset <= artifact.Length - 21; offset++)
        {
            if (artifact[offset] == 1
                && BinaryPrimitives.ReadInt32LittleEndian(
                    artifact.AsSpan(offset + 1, sizeof(int)))
                    == (int)pointerType
                && BinaryPrimitives.ReadInt64LittleEndian(
                    artifact.AsSpan(offset + 13, sizeof(long))) == 0)
            {
                return offset + 13;
            }
        }

        throw new InvalidOperationException(
            "The typed null-pointer constant was not found.");
    }

    private static PrivateEnumOffsets GetPrivateEnumOffsets(byte[] artifact)
    {
        const int enumRowSize = 44;
        var expectedLookupName = "Size"u8;
        var expectedMemberKey = "member.small"u8;
        for (var offset = 4;
            offset <= artifact.Length - enumRowSize; offset++)
        {
            var candidate = artifact.AsSpan(offset, enumRowSize);
            if (BinaryPrimitives.ReadInt32LittleEndian(
                    artifact.AsSpan(offset - sizeof(int), sizeof(int))) != 2
                || BinaryPrimitives.ReadUInt32LittleEndian(
                    candidate[..sizeof(uint)]) != 1
                || BinaryPrimitives.ReadInt32LittleEndian(
                    candidate.Slice(sizeof(uint), sizeof(int)))
                    != expectedLookupName.Length
                || !candidate.Slice(2 * sizeof(int),
                    expectedLookupName.Length).SequenceEqual(
                        expectedLookupName)
                || BinaryPrimitives.ReadInt32LittleEndian(
                    candidate.Slice(3 * sizeof(int), sizeof(int)))
                    != (int)PrimitiveType.Int32
                || candidate[4 * sizeof(int)] != 0
                || candidate[(4 * sizeof(int)) + 1] != 0
                || BinaryPrimitives.ReadUInt16LittleEndian(
                    candidate.Slice((4 * sizeof(int)) + 2,
                        sizeof(ushort))) != 0
                || BinaryPrimitives.ReadInt32LittleEndian(
                    candidate.Slice(5 * sizeof(int), sizeof(int))) != 0
                || BinaryPrimitives.ReadInt32LittleEndian(
                    candidate.Slice(6 * sizeof(int), sizeof(int))) != 1
                || BinaryPrimitives.ReadInt32LittleEndian(
                    candidate.Slice(7 * sizeof(int), sizeof(int)))
                    != expectedMemberKey.Length
                || !candidate.Slice(8 * sizeof(int),
                    expectedMemberKey.Length).SequenceEqual(
                        expectedMemberKey))
            {
                continue;
            }

            return new PrivateEnumOffsets(offset - sizeof(int), offset,
                offset + (3 * sizeof(int)), offset + (4 * sizeof(int)),
                offset + (4 * sizeof(int)) + 1,
                offset + (4 * sizeof(int)) + 2,
                offset + (5 * sizeof(int)), offset + (6 * sizeof(int)),
                offset + (7 * sizeof(int)), offset + (8 * sizeof(int)));
        }

        throw new InvalidOperationException(
            "The private enum directory record was not found.");
    }

    private readonly record struct PrivateEnumOffsets(
        int EnumCount, int OriginReference, int UnderlyingType, int Flags,
        int ReservedByte, int ReservedWord,
        int BlittableTargetRequirement, int MemberCount,
        int MemberKeyLength, int MemberKeyBytes);
}
