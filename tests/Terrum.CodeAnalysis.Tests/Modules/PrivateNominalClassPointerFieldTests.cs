// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
internal sealed class PrivateNominalClassPointerFieldTests
{
    [Test]
    public void ClassReferenceRetainsAndValidatesEnumArrayFieldIdentity()
    {
        var enumType = BoundEnumTypes.Type(0);
        var enumOrigin = new BoundPrivateNominalTypeOrigin(enumType,
            BoundPrivateNominalOriginKind.OwnedDefinition,
            IsExported: true, UsingModuleIdentity: "provider",
            LocalDeclarationKey: "types.color");
        var color = CreateEnum(enumOrigin, 1);
        var enumReference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast, enumDefinitions: [color],
            privateNominalTypeOrigins: [enumOrigin]);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public class Widget {
                public ptr<int32> Data;
                public Widget(ptr<int32> data) { Data = data; }
            }
            public int32 GetValue() { return 1; }
            """), enumReference,
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var fixedArrayTypes = new List<BoundNativeFixedArrayType>();
        Assert.That(NativeFixedArrayContract.TryCreateType(enumType, 2,
                program.Structures, fixedArrayTypes, out var arrayType,
                out _),
            Is.True);
        var pointerType = program.Classes.Single().Fields.Single().Type;
        var pointerIndex = NativePointerTypes.Index(pointerType);
        var pointerTypes = program.NativePointerTypes.SetItem(pointerIndex,
            program.NativePointerTypes[pointerIndex] with {
                PointeeType = arrayType,
            });
        var @class = program.Classes.Single();
        var classType = (PrimitiveType)ManagedClassTypes.First;
        var classOrigin = new BoundPrivateNominalTypeOrigin(classType,
            BoundPrivateNominalOriginKind.OwnedDefinition,
            IsExported: true, UsingModuleIdentity: "provider",
            LocalDeclarationKey: "types.widget");
        var origins = ImmutableArray.Create(enumOrigin, classOrigin);
        var snapshot = new BoundPrivateClassSnapshot(program.Structures,
            [@class], program.CheckedReferenceTypes,
            program.NativeFunctionPointerSignatures, pointerTypes,
            [.. fixedArrayTypes], origins, [color]);
        var classReference = new BoundPrivateClassReference(classType,
            classOrigin, snapshot, classType, IsSourceLookupRoot: true,
            ExpectedStructureMapping: [.. Enumerable.Range(
                0, program.Structures.Length)],
            ExpectedClassMapping: [0],
            ExpectedNativePointerMapping: [.. Enumerable.Range(
                0, pointerTypes.Length)],
            ExpectedNativeFunctionPointerMapping: [.. Enumerable.Range(
                0, program.NativeFunctionPointerSignatures.Length)],
            ExpectedNativeFixedArrayMapping: [0],
            ExpectedCheckedReferenceMapping: [.. Enumerable.Range(
                0, program.CheckedReferenceTypes.Length)],
            ExpectedEnumMapping: [0]);
        var reference = new CompilationReference(program.Structures, [],
            BoundRuntimePolicy.FailFast,
            nativeFunctionPointerSignatures:
                program.NativeFunctionPointerSignatures,
            nativePointerTypes: pointerTypes,
            nativeFixedArrayTypes: [.. fixedArrayTypes],
            enumDefinitions: [color],
            privateNominalTypeOrigins: origins,
            classes: [@class],
            privateClassReferences: [classReference]);

        var consumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("public int32 Read() { return 1; }"),
            reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        Assert.That(consumer.Program!.PrivateClassReferences.Single()
                .ExpectedEnumMapping,
            Is.EqualTo([0]));

        var changedReference = new CompilationReference(
            program.Structures, [], BoundRuntimePolicy.FailFast,
            nativeFunctionPointerSignatures:
                program.NativeFunctionPointerSignatures,
            nativePointerTypes: pointerTypes,
            nativeFixedArrayTypes: [.. fixedArrayTypes],
            enumDefinitions: [CreateEnum(enumOrigin, 2)],
            privateNominalTypeOrigins: origins,
            classes: [@class],
            privateClassReferences: [classReference]);
        var changedConsumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("public int32 Read() { return 1; }"),
            changedReference);
        Assert.That(changedConsumer.HasErrors, Is.True,
            "The original class snapshot must reject a replacement enum definition.");

        var priorityIdentity = new CoreDeclarationIdentity("priority",
            "Priority", CoreDeclarationKind.EnumType);
        var priorityReference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            enumDefinitions: [
                new BoundEnumDefinition(
                    new BoundEnumDeclarationIdentity.Core(priorityIdentity),
                    "Priority", PrimitiveType.Int32, [], true, true, false),
            ]);
        foreach (var combined in new[] {
            ModuleCompiler.CreateReferenceSetFromReferences(
                [priorityReference, reference]),
            ModuleCompiler.MergeGraphReferences(
                [priorityReference, reference]),
        })
        {
            var combinedClassReference
                = combined.PrivateClassReferences.Single();
            Assert.That(combinedClassReference.ExpectedEnumMapping,
                Is.EqualTo([1]));
            Assert.That(combined.NativeFixedArrayTypes.Single().ElementType,
                Is.EqualTo(BoundEnumTypes.Type(1)));
            var combinedCompilation = Compilation.Create(
                SyntaxTree.Parse("int32 Main() { return 0; }"), combined);
            Assert.That(combinedCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    combinedCompilation.Diagnostics));
        }

        static BoundEnumDefinition CreateEnum(
            BoundPrivateNominalTypeOrigin origin, int memberValue)
        {
            return BoundEnumDefinition.CreatePrivate(origin, "Color",
                PrimitiveType.Int32, [
                    new BoundPrivateEnumMemberSnapshot("Red", memberValue,
                        true, "member.red"),
                ], isPublic: true, isPrimary: true, isFlags: false,
                blittableTargetRequirement: 0);
        }
    }

    [TestCase(1)]
    [TestCase(2)]
    public void ClassReferenceRemapsPointerFieldClosureAcrossProviderReplacement(
        int pointerDepth)
    {
        var original = CreateProvider(reorderStructures: false,
            changeHiddenFieldType: false, pointerDepth);
        var reordered = CreateProvider(reorderStructures: true,
            changeHiddenFieldType: false, pointerDepth);
        var changed = CreateProvider(reorderStructures: false,
            changeHiddenFieldType: true, pointerDepth);
        var reference = ModuleCompiler.CreateReference(original);
        var widget = reference.PrivateClassReferences.Single();
        Assert.That(reference.Functions.Select(static function => function.Name),
            Is.EqualTo(["GetValue"]));
        Assert.That(reference.NativePointerTypes,
            Has.Length.EqualTo(pointerDepth));
        var expectedClass = widget.ExpectedSnapshot.Classes[
            ManagedClassTypes.ClassIndex(widget.ExpectedClassType)];
        var expectedOuterPointerIndex = NativePointerTypes.Index(
            expectedClass.Fields.Single().Type);
        Assert.That(reference.Classes.Single().Fields.Single().Type,
            Is.EqualTo(NativePointerTypes.Type(
                widget.ExpectedNativePointerMapping[
                    expectedOuterPointerIndex])));
        Assert.That(widget.ExpectedSnapshot.Structures.Select(
            static structure => structure.Name).ToArray(),
            Does.Contain("Hidden"));

        var extra = CreateExtraPointerFieldProvider();
        var extraReference = ModuleCompiler.CreateReference(extra);
        foreach (var combinedReference in new[] {
            ModuleCompiler.CreateReferenceSetFromReferences(
                [extraReference, reference]),
            ModuleCompiler.MergeGraphReferences(
                [extraReference, reference]),
        })
        {
            var combinedWidget = combinedReference.Classes.Single(
                static @class => @class.Name == "Widget");
            var combinedPointerType = combinedWidget.Fields.Single().Type;
            Assert.That(combinedPointerType,
                Is.EqualTo(NativePointerTypes.Type(
                extraReference.NativePointerTypes.Length
                + NativePointerTypes.Index(
                    reference.Classes.Single().Fields.Single().Type))));
            var combinedOrigins = combinedReference.PrivateClassReferences
                .Select(static classReference =>
                    classReference.Origin.LocalDeclarationKey).ToArray();
            Assert.That(combinedOrigins, Does.Contain("types.widget"),
                string.Join(", ", combinedOrigins));
            Assert.That(combinedReference.PrivateClassReferences.Single(
                static classReference =>
                    classReference.Origin.LocalDeclarationKey
                    == "types.widget")
                .ExpectedNativePointerMapping[expectedOuterPointerIndex],
                Is.EqualTo(NativePointerTypes.Index(
                    combinedPointerType)));
            var combinedCompilation = Compilation.Create(
                SyntaxTree.Parse("int32 Main() { return 0; }"),
                combinedReference);
            Assert.That(combinedCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    combinedCompilation.Diagnostics));
            var hiddenTypeCompilation = Compilation.Create(
                SyntaxTree.Parse("int32 Main(Hidden value) { return 0; }"),
                combinedReference);
            Assert.That(hiddenTypeCompilation.HasErrors, Is.True,
                "A class's private field dependency must not become a source lookup root.");
        }

        var compilation = Compilation.Create(SyntaxTree.Parse(
            "public int32 Main() { return GetValue(); }"), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var inputs = PrivateModuleIdentityInputs.Create(
            compilation.Program!, "consumer", [],
            [
                new PrivateNominalImport(widget.ClassType,
                    "types.widget.proxy", "provider", "types.widget"),
            ]);
        var consumer = ModuleCompiler.CreateConsumer(compilation.Program!,
            original, inputs);

        Assert.DoesNotThrow(() => ModuleCompiler.Link(consumer, reordered));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.Link(consumer, changed));
    }

    [Test]
    public void ClassReferenceRemapsCheckedReferenceFieldAcrossProviderSetsAndGraphs()
    {
        var reference = CreateCheckedReferenceFieldReference();
        var widget = reference.PrivateClassReferences.Single();
        var expectedClass = widget.ExpectedSnapshot.Classes[
            ManagedClassTypes.ClassIndex(widget.ExpectedClassType)];
        var expectedReferenceIndex = CheckedReferenceTypes.Index(
            expectedClass.Fields.Single().Type);
        Assert.That(reference.CheckedReferenceTypes, Has.Length.EqualTo(1));
        Assert.That(reference.Classes.Single().Fields.Single().Type,
            Is.EqualTo(CheckedReferenceTypes.Type(
                widget.ExpectedCheckedReferenceMapping[
                    expectedReferenceIndex])));

        var adapterCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public int32 Adapted() { return 1; }"),
            reference,
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(adapterCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                adapterCompilation.Diagnostics));
        var adapterInputs = PrivateModuleIdentityInputs.Create(
            adapterCompilation.Program!, "adapter", [],
            [
                new PrivateNominalImport(widget.ClassType,
                    "types.widget.proxy", "provider", "types.widget"),
            ]);
        var adapter = ModuleCompiler.CreateProducer(
            adapterCompilation.Program!, ["Adapted"], adapterInputs);
        var persistedReference = ModuleCompiler.CreateReference(adapter);
        Assert.That(persistedReference.CheckedReferenceTypes, Has.Length.EqualTo(1));
        var persistedClassReference
            = persistedReference.PrivateClassReferences.Single(
                static classReference =>
                    classReference.Origin.LocalDeclarationKey
                        == "types.widget.proxy");
        Assert.That(persistedReference.Classes.Single().Fields.Single().Type,
            Is.EqualTo(CheckedReferenceTypes.Type(
                persistedClassReference.ExpectedCheckedReferenceMapping[
                    expectedReferenceIndex])));

        var extraReference = CreateExtraCheckedReferenceReference();
        foreach (var combinedReference in new[] {
            ModuleCompiler.CreateReferenceSetFromReferences(
                [extraReference, persistedReference]),
            ModuleCompiler.MergeGraphReferences(
                [extraReference, persistedReference]),
        })
        {
            var combinedWidget = combinedReference.Classes.Single(
                static @class => @class.Name == "Widget");
            var combinedReferenceIndex
                = combinedReference.PrivateClassReferences.Single(
                    static classReference =>
                        classReference.Origin.LocalDeclarationKey
                            == "types.widget")
                    .ExpectedCheckedReferenceMapping[
                        expectedReferenceIndex];
            Assert.That(combinedReference.CheckedReferenceTypes,
                Has.Length.EqualTo(2));
            Assert.That(combinedWidget.Fields.Single().Type,
                Is.EqualTo(CheckedReferenceTypes.Type(
                    combinedReferenceIndex)));
            var combinedCompilation = Compilation.Create(
                SyntaxTree.Parse("int32 Main() { return 0; }"),
                combinedReference);
            Assert.That(combinedCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    combinedCompilation.Diagnostics));
        }
    }

    [Test]
    public void ClassReferenceRemapsFixedArrayPointerClosureAcrossSetsAndGraphs()
    {
        var reference = CreateFixedArrayClassReference();
        var classReference = reference.PrivateClassReferences.Single();
        Assert.That(classReference.ExpectedNativeFixedArrayMapping,
            Is.EqualTo([0]));

        var extraReference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeFixedArrayTypes: [
                new BoundNativeFixedArrayType(PrimitiveType.UInt32, 1, 4,
                    4, 4, 1),
            ]);
        foreach (var combinedReference in new[] {
            ModuleCompiler.CreateReferenceSetFromReferences(
                [extraReference, reference]),
            ModuleCompiler.MergeGraphReferences(
                [extraReference, reference]),
        })
        {
            var combinedClassReference
                = combinedReference.PrivateClassReferences.Single();
            Assert.That(combinedClassReference
                .ExpectedNativeFixedArrayMapping, Is.EqualTo([1]));
            Assert.That(combinedReference.NativeFixedArrayTypes,
                Has.Length.EqualTo(2));
            var pointerType = combinedReference.Classes.Single()
                .Fields.Single().Type;
            var pointer = combinedReference.NativePointerTypes[
                NativePointerTypes.Index(pointerType)];
            Assert.That(pointer.PointeeType,
                Is.EqualTo(NativeFixedArrayTypes.Type(1)));
            var compilation = Compilation.Create(
                SyntaxTree.Parse("int32 Main() { return 0; }"),
                combinedReference,
                NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));

            var invalidClassReference = combinedClassReference with {
                ExpectedNativeFixedArrayMapping = [0],
            };
            var invalidReference = new CompilationReference(
                combinedReference.Structures, combinedReference.Functions,
                combinedReference.RuntimePolicy,
                nativeFunctionPointerSignatures:
                    combinedReference.NativeFunctionPointerSignatures,
                nativePointerTypes: combinedReference.NativePointerTypes,
                checkedReferenceTypes: combinedReference.CheckedReferenceTypes,
                nativeFixedArrayTypes:
                    combinedReference.NativeFixedArrayTypes,
                enumDefinitions: combinedReference.EnumDefinitions,
                privateNominalTypeOrigins:
                    combinedReference.PrivateNominalTypeOrigins,
                classes: combinedReference.Classes,
                privateClassReferences: [invalidClassReference]);
            var invalidCompilation = Compilation.Create(
                SyntaxTree.Parse("int32 Main() { return GetValue(); }"),
                invalidReference,
                NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
            Assert.That(invalidCompilation.HasErrors, Is.True,
                "A class snapshot must reject a row map to a different array contract.");
        }
    }

    [Test]
    public void ClassReferencePersistsFixedArrayPointerClosure()
    {
        var reference = CreateFixedArrayClassReference();
        var widget = reference.PrivateClassReferences.Single();
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public int32 Adapted() { return 1; }"),
            reference,
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var inputs = PrivateModuleIdentityInputs.Create(
            compilation.Program!, "adapter", [],
            [
                new PrivateNominalImport(widget.ClassType,
                    "types.widget.proxy", "provider", "types.widget"),
            ]);
        var producer = ModuleCompiler.CreateProducer(compilation.Program!,
            ["Adapted"], inputs);
        var profile = ModuleCompiler.GetNativeProfileVersion(producer,
            isConsumer: false);
        Assert.That(profile, Is.Not.EqualTo("native-19"));
        Assert.That(profile, Is.Not.EqualTo("native-20"));
        var parsedModule
            = ModuleCompiler.ReadProviderContractSnapshot(producer)
                .ParsedModule;
        Assert.That(parsedModule.NominalTypeOrigins.Any(origin =>
                origin.OriginKind
                    == BoundPrivateNominalOriginKind.ImportedExpectation
                && origin.LocalDeclarationKey == "types.widget.proxy"),
            Is.True, string.Join(Environment.NewLine,
                parsedModule.NominalTypeOrigins));
        var persistedReference = ModuleCompiler.CreateReference(producer);
        var persistedClassReference
            = persistedReference.PrivateClassReferences.Single(
                static reference => reference.Origin.LocalDeclarationKey
                    == "types.widget.proxy");
        Assert.That(persistedReference.NativeFixedArrayTypes,
            Has.Length.EqualTo(1));
        Assert.That(persistedReference.NativePointerTypes[
                NativePointerTypes.Index(
                    persistedReference.Classes.Single().Fields.Single().Type)]
                .PointeeType,
            Is.EqualTo(NativeFixedArrayTypes.Type(
                persistedClassReference.ExpectedNativeFixedArrayMapping[0])));
        var nativePackage = NativeModuleCompiler.CreateProducer(
            compilation.Program!, ["Adapted"], "adapter",
            NativeRuntimePolicy.FailFast, inputs);
        var nativeInfo = NativeModuleCompiler.Inspect(nativePackage);
        Assert.That(nativeInfo.NativeProfileVersion,
            Is.Not.EqualTo("native-19"));
        Assert.That(nativeInfo.NativeProfileVersion,
            Is.Not.EqualTo("native-20"));
        var nativeReference = NativeModuleCompiler.CreateReference(
            nativePackage);
        Assert.That(nativeReference.Functions.Select(
                static function => function.Name),
            Is.EqualTo(["Adapted"]));
        var neutralCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public int32 Neutral() { return 0; }"));
        Assert.That(neutralCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                neutralCompilation.Diagnostics));
        var neutralPackage = NativeModuleCompiler.CreateProducer(
            neutralCompilation.Program!, ["Neutral"], "neutral",
            NativeRuntimePolicy.FailFast);
        var nativeReferenceSet = NativeModuleCompiler.CreateReferenceSet(
            [nativePackage, neutralPackage]);
        Assert.That(nativeReferenceSet.Classes.Single().Name,
            Is.EqualTo("Widget"));
        Assert.That(nativeReferenceSet.NativeFixedArrayTypes,
            Has.Length.EqualTo(1));
        var nativeGraphReference = NativeModuleCompiler.CreateGraphReference(
            [nativePackage, neutralPackage]);
        Assert.That(nativeGraphReference.Classes.Single().Name,
            Is.EqualTo("Widget"));
        Assert.That(nativeGraphReference.NativeFixedArrayTypes,
            Has.Length.EqualTo(1));

        var divergent = CreateFixedArrayClassReference(3);
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateReferenceSetFromReferences(
                [reference, divergent]));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.MergeGraphReferences([reference, divergent]));
        var otherOwner = CreateFixedArrayClassReference(
            ownerModule: "other-provider");
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateReferenceSetFromReferences(
                [reference, otherOwner]));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.MergeGraphReferences([reference, otherOwner]));

        var extraReference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeFixedArrayTypes: [
                new BoundNativeFixedArrayType(PrimitiveType.UInt32, 1, 4,
                    4, 4, 1),
            ]);
        foreach (var combined in new[] {
            ModuleCompiler.CreateReferenceSetFromReferences(
                [extraReference, reference, persistedReference]),
            ModuleCompiler.MergeGraphReferences(
                [extraReference, reference, persistedReference]),
        })
        {
            Assert.That(combined.Classes, Has.Length.EqualTo(1));
            var combinedClassReference
                = combined.PrivateClassReferences.Single(
                    static classReference =>
                        classReference.Origin.LocalDeclarationKey
                            == "types.widget.proxy");
            Assert.That(
                combinedClassReference.ExpectedNativeFixedArrayMapping,
                Is.EqualTo([1]));
            Assert.That(combined.PrivateNominalTypeOrigins,
                Has.Length.EqualTo(2));
            var consumer = Compilation.CreateLibrary(
                SyntaxTree.Parse(
                    "public int32 Use() { return Adapted(); }"),
                combined,
                NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
            Assert.That(consumer.HasErrors, Is.False,
                string.Join(Environment.NewLine, consumer.Diagnostics));
        }
    }

    [Test]
    public void ClassReferenceRemapsFunctionPointerFieldAcrossSetsAndGraphs()
    {
        var reference = CreateFunctionPointerClassReference();
        var classReference = reference.PrivateClassReferences.Single();
        Assert.That(classReference.ExpectedNativeFunctionPointerMapping,
            Is.EqualTo([0]));

        var extraReference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeFunctionPointerSignatures: [
                new NativeFunctionPointerSignatureDefinition(
                    NativeCallingConvention.Native, PrimitiveType.Int32, []),
            ]);
        foreach (var combinedReference in new[] {
            ModuleCompiler.CreateReferenceSetFromReferences(
                [extraReference, reference]),
            ModuleCompiler.MergeGraphReferences(
                [extraReference, reference]),
        })
        {
            var combinedClassReference
                = combinedReference.PrivateClassReferences.Single();
            Assert.That(combinedClassReference
                .ExpectedNativeFunctionPointerMapping, Is.EqualTo([1]));
            Assert.That(combinedReference.NativeFunctionPointerSignatures,
                Has.Length.EqualTo(2));
            Assert.That(combinedReference.Classes.Single()
                .Fields.Single().Type,
                Is.EqualTo(NativeFunctionPointerTypes.Type(1)));
            var compilation = Compilation.Create(
                SyntaxTree.Parse("int32 Main() { return 0; }"),
                combinedReference);
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
        }
    }

    [Test]
    public void ClassReferenceDiamondComparesNestedClassGraphs()
    {
        var left = ModuleCompiler.CreateReference(
            CreateNestedClassProvider("GetLeft",
                changeChildFieldType: false));
        var same = ModuleCompiler.CreateReference(
            CreateNestedClassProvider("GetRight",
                changeChildFieldType: false));
        var divergent = ModuleCompiler.CreateReference(
            CreateNestedClassProvider("GetOther",
                changeChildFieldType: true));

        var combinedSet = ModuleCompiler.CreateReferenceSetFromReferences(
            [left, same]);
        Assert.That(combinedSet.Classes, Has.Length.EqualTo(2));
        var combinedGraph = ModuleCompiler.MergeGraphReferences(
            [left, same]);
        Assert.That(combinedGraph.Classes, Has.Length.EqualTo(2));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateReferenceSetFromReferences(
                [left, divergent]));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.MergeGraphReferences([left, divergent]));

        var leftProducer = CreateNestedClassProvider("GetValue",
            changeChildFieldType: false);
        var sameProducer = CreateNestedClassProvider("GetValue",
            changeChildFieldType: false);
        var divergentProducer = CreateNestedClassProvider("GetValue",
            changeChildFieldType: true);
        var reference = ModuleCompiler.CreateReference(leftProducer);
        var childReference = reference.PrivateClassReferences.Single(
            static classReference =>
                classReference.Origin.LocalDeclarationKey == "types.Child");
        var widgetReference = reference.PrivateClassReferences.Single(
            static classReference =>
                classReference.Origin.LocalDeclarationKey == "types.Widget");
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "public int32 Main() { return GetValue(); }"), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var inputs = PrivateModuleIdentityInputs.Create(
            compilation.Program!, "consumer", [],
            [
                new PrivateNominalImport(childReference.ClassType,
                    "types.child.proxy", "provider", "types.Child"),
                new PrivateNominalImport(widgetReference.ClassType,
                    "types.widget.proxy", "provider", "types.Widget"),
            ]);
        var consumer = ModuleCompiler.CreateConsumer(compilation.Program!,
            leftProducer, inputs);
        Assert.DoesNotThrow(() => ModuleCompiler.Link(consumer, sameProducer));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.Link(consumer, divergentProducer));
    }

    private static byte[] CreateExtraPointerFieldProvider()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public ptr<uint32> Value;
            }
            public int32 GetExtra() { return 0; }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var holderType = (PrimitiveType)(100
            + program.Structures.Select((structure, index) =>
                    (structure, index))
                .Single(static item => item.structure.Name == "Holder")
                .index);
        return ModuleCompiler.CreateProducer(program, ["GetExtra"],
            PrivateModuleIdentityInputs.Create(program, "extra",
                [new PrivateNominalDefinition(holderType, "types.holder",
                    IsExported: true)], []));
    }

    private static CompilationReference CreateExtraCheckedReferenceReference()
        => new([], [], BoundRuntimePolicy.FailFast,
            checkedReferenceTypes: [
                new CheckedReferenceTypeDefinition(PrimitiveType.UInt32,
                    IsWritable: false, IsGarbageCollected: false,
                    IsInitialization: false, IsOptional: false),
            ]);

    private static CompilationReference
        CreateCheckedReferenceFieldReference()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Hidden {
                public int32 Value;
                public Hidden(int32 value) { Value = value; }
            }
            public class Widget {
                public ptr<Hidden> Data;
                public Widget(ptr<Hidden> data) { Data = data; }
            }
            public int32 GetValue() { return 1; }
            """),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var classType = (PrimitiveType)ManagedClassTypes.First;
        var definitions = program.Structures
            .Select((structure, index) => new PrivateNominalDefinition(
                (PrimitiveType)(100 + index), "types." + structure.Name,
                IsExported: false))
            .ToList();
        definitions.Add(new PrivateNominalDefinition(classType,
            "types.widget", IsExported: true));
        var producer = ModuleCompiler.CreateProducer(program, ["GetValue"],
            PrivateModuleIdentityInputs.Create(program, "provider",
                definitions, []));
        var baseReference = ModuleCompiler.CreateReference(producer);
        var baseClassReference
            = baseReference.PrivateClassReferences.Single();
        var snapshot = baseClassReference.ExpectedSnapshot;
        var hiddenIndex = snapshot.Structures.Select(
                (structure, index) => (structure, index))
            .Single(static item => item.structure.Name == "Hidden").index;
        var checkedReferenceTypes = ImmutableArray.Create(
            new CheckedReferenceTypeDefinition(
                (PrimitiveType)(100 + hiddenIndex), IsWritable: false,
                IsGarbageCollected: false, IsInitialization: false,
                IsOptional: false));
        var checkedReferenceType = CheckedReferenceTypes.Type(0);
        var expectedClassIndex = ManagedClassTypes.ClassIndex(
            baseClassReference.ExpectedClassType);
        var expectedClass = snapshot.Classes[expectedClassIndex];
        var expectedField = expectedClass.Fields.Single();
        expectedClass = expectedClass with {
            Fields = [expectedField with {
                Type = checkedReferenceType,
                Size = 8,
            }],
        };
        var expectedClasses = snapshot.Classes.SetItem(expectedClassIndex,
            expectedClass);
        var updatedSnapshot = snapshot with {
            Classes = expectedClasses,
            CheckedReferenceTypes = checkedReferenceTypes,
        };
        var classReference = baseClassReference with {
            ExpectedSnapshot = updatedSnapshot,
            ExpectedStructureMapping = [.. Enumerable.Range(
                0, snapshot.Structures.Length)],
            ExpectedClassMapping = [.. Enumerable.Range(
                0, snapshot.Classes.Length)],
            ExpectedCheckedReferenceMapping = [0],
        };
        return new CompilationReference(snapshot.Structures,
            [], baseReference.RuntimePolicy,
            nativeFunctionPointerSignatures:
                snapshot.NativeFunctionPointerSignatures,
            nativePointerTypes: snapshot.NativePointerTypes,
            checkedReferenceTypes: checkedReferenceTypes,
            nativeFixedArrayTypes: snapshot.NativeFixedArrayTypes,
            privateNominalTypeOrigins:
                baseReference.PrivateNominalTypeOrigins,
            classes: expectedClasses,
            privateClassReferences: [classReference]);
    }

    private static CompilationReference CreateFixedArrayClassReference(
        ulong elementCount = 2, string ownerModule = "provider")
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public class Widget {
                public ptr<uint32> Data;
                public Widget(ptr<uint32> data) { Data = data; }
            }
            public int32 GetValue() { return 1; }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var sourceClass = program.Classes.Single();
        var fixedArray = new BoundNativeFixedArrayType(
            PrimitiveType.UInt32, elementCount, 4,
            checked(elementCount * 4), 4, 1);
        var pointerType = sourceClass.Fields.Single().Type;
        var pointerIndex = NativePointerTypes.Index(pointerType);
        var pointerTypes = program.NativePointerTypes.SetItem(pointerIndex,
            program.NativePointerTypes[pointerIndex] with {
                PointeeType = NativeFixedArrayTypes.Type(0),
            });
        var @class = sourceClass;
        var classType = (PrimitiveType)ManagedClassTypes.First;
        var origin = new BoundPrivateNominalTypeOrigin(classType,
            BoundPrivateNominalOriginKind.OwnedDefinition,
            IsExported: true, UsingModuleIdentity: ownerModule,
            LocalDeclarationKey: "types.widget");
        var origins = ImmutableArray.Create(origin);
        var snapshot = new BoundPrivateClassSnapshot(program.Structures,
            [@class], program.CheckedReferenceTypes,
            program.NativeFunctionPointerSignatures, pointerTypes,
            [fixedArray], origins,
            program.EnumDefinitions.IsDefault
                ? []
                : program.EnumDefinitions);
        var classReference = new BoundPrivateClassReference(classType,
            origin, snapshot, classType, IsSourceLookupRoot: true,
            ExpectedStructureMapping: [.. Enumerable.Range(
                0, program.Structures.Length)],
            ExpectedClassMapping: [0],
            ExpectedNativePointerMapping: [.. Enumerable.Range(
                0, pointerTypes.Length)],
            ExpectedNativeFunctionPointerMapping: [.. Enumerable.Range(
                0, program.NativeFunctionPointerSignatures.Length)],
            ExpectedNativeFixedArrayMapping: [0],
            ExpectedCheckedReferenceMapping: [.. Enumerable.Range(
                0, program.CheckedReferenceTypes.Length)],
            ExpectedEnumMapping: program.EnumDefinitions.IsDefaultOrEmpty
                ? []
                : [.. Enumerable.Range(0,
                    program.EnumDefinitions.Length)]);
        return new CompilationReference(program.Structures, [],
            BoundRuntimePolicy.FailFast,
            nativeFunctionPointerSignatures:
                program.NativeFunctionPointerSignatures,
            nativePointerTypes: pointerTypes,
            checkedReferenceTypes: program.CheckedReferenceTypes,
            nativeFixedArrayTypes: [fixedArray],
            enumDefinitions: program.EnumDefinitions,
            privateNominalTypeOrigins: origins,
            classes: [@class],
            privateClassReferences: [classReference]);
    }

    private static CompilationReference
        CreateFunctionPointerClassReference()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public class Widget {
                public int32 Data;
                public Widget(int32 data) { Data = data; }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var sourceClass = compilation.Program!.Classes.Single();
        var signature = new NativeFunctionPointerSignatureDefinition(
            NativeCallingConvention.Native, PrimitiveType.UInt32, []);
        var functionPointerType = NativeFunctionPointerTypes.Type(0);
        var @class = sourceClass with {
            Fields = [sourceClass.Fields.Single() with {
                Type = functionPointerType,
                Size = 8,
            }],
        };
        var classType = (PrimitiveType)ManagedClassTypes.First;
        var origin = new BoundPrivateNominalTypeOrigin(classType,
            BoundPrivateNominalOriginKind.OwnedDefinition,
            IsExported: true, UsingModuleIdentity: "provider",
            LocalDeclarationKey: "types.widget");
        var origins = ImmutableArray.Create(origin);
        var snapshot = new BoundPrivateClassSnapshot([], [@class], [],
            [signature], [], [], origins,
            []);
        var classReference = new BoundPrivateClassReference(classType,
            origin, snapshot, classType, IsSourceLookupRoot: true,
            ExpectedStructureMapping: [], ExpectedClassMapping: [0],
            ExpectedNativePointerMapping: [],
            ExpectedNativeFunctionPointerMapping: [0],
            ExpectedNativeFixedArrayMapping: [],
            ExpectedCheckedReferenceMapping: [],
            ExpectedEnumMapping: []);
        return new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeFunctionPointerSignatures: [signature],
            nativePointerTypes: [],
            checkedReferenceTypes: [],
            nativeFixedArrayTypes: [],
            privateNominalTypeOrigins: origins,
            classes: [@class],
            privateClassReferences: [classReference]);
    }

    private static byte[] CreateNestedClassProvider(string functionName,
        bool changeChildFieldType)
    {
        var childType = changeChildFieldType ? "uint32" : "int32";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
            public class Child {
                public {{childType}} Value;
                public Child({{childType}} value) { Value = value; }
            }
            public class Widget {
                public Child ChildValue;
                public Widget(Child child) { ChildValue = child; }
            }
            public int32 {{functionName}}() { return 1; }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var definitions = program.Classes
            .Select((@class, index) => new PrivateNominalDefinition(
                (PrimitiveType)(ManagedClassTypes.First + index),
                "types." + @class.Name, IsExported: true))
            .ToArray();
        return ModuleCompiler.CreateProducer(program, [functionName],
            PrivateModuleIdentityInputs.Create(program, "provider",
                definitions, []));
    }

    private static byte[] CreateProvider(bool reorderStructures,
        bool changeHiddenFieldType, int pointerDepth = 1)
    {
        var dataType = pointerDepth == 1
            ? "ptr<Hidden>" : "ptr<ptr<Hidden>>";
        var noise = """
            struct Noise {
                public int32 Value;
                public Noise(int32 value) { Value = value; }
            }
            """;
        var hidden = $$"""
            struct Hidden {
                public {{(changeHiddenFieldType ? "uint32" : "int32")}} Value;
                public Hidden({{(changeHiddenFieldType ? "uint32" : "int32")}} value) {
                    Value = value;
                }
            }
            """;
        var source = (reorderStructures ? noise + hidden : hidden + noise) + $$"""
            public class Widget {
                private {{dataType}} Data;
                public Widget({{dataType}} data) { Data = data; }
            }
            public int32 GetValue() { return 1; }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var widgetType = (PrimitiveType)ManagedClassTypes.First;
        var definitions = program.Structures
            .Select((structure, index) => new PrivateNominalDefinition(
                (PrimitiveType)(100 + index),
                structure.Name == "Hidden" ? "types.hidden" : "types.noise",
                IsExported: false))
            .ToList();
        definitions.Add(new PrivateNominalDefinition(widgetType,
            "types.widget", IsExported: true));

        return ModuleCompiler.CreateProducer(program, ["GetValue"],
            PrivateModuleIdentityInputs.Create(program, "provider",
                definitions, []));
    }
}
