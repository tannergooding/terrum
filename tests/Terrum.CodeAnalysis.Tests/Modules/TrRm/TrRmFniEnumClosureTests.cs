using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmFniEnumClosureTests
{
    private const string SchemaIdentity
        = "trrm/1-draft-foreign-native-interface-parameter-surface-1-enum-closure-1";

    [Test]
    public void EmitsEnumOnlyProviderWithoutForeignInterfaceRows()
    {
        var tree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Shade : int8 {
                    Dark = -1,
                    Light = 1
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var declaration = tree.Root.DescendantNodes().Single(node =>
            node.Kind == SyntaxKind.EnumDeclaration);
        var definition = program.EnumDefinitions.Single(item =>
            item.Identity is BoundEnumDeclarationIdentity.Source source
            && source.Value.Parts.Any(part =>
                part.IsPrimary && ReferenceEquals(part.Tree, tree)
                && ReferenceEquals(part.Declaration, declaration)));
        Assert.That(definition.Members.Length, Is.EqualTo(2));
        var export = new TrRmSourceEnumDeclarationExport(program, 0,
            declaration.Span, ["enum.shade"],
            ["enum.shade.dark", "enum.shade.light"]);

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "EnumProvider", SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [export]);
        var index
            = TrRmArtifactIndex
                .ForForeignNativeInterfaceParameterSurfaceEnumClosureDraft(
                    artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(index);

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(SchemaIdentity, artifact),
            "EnumProvider", [], ["enum.shade"]);
        Assert.That(reference.EnumDefinitions.Length, Is.EqualTo(1));
        Assert.That(reference.EnumDefinitions[0].Identity,
            Is.TypeOf<BoundEnumDeclarationIdentity.Standard>());
        Assert.That(((BoundEnumDeclarationIdentity.Standard)
            reference.EnumDefinitions[0].Identity).Value.SelectedExportKeys,
            Does.Contain("enum.shade"));

        Assert.That(tables.TypeDefs.Count, Is.EqualTo(1));
        Assert.That(tables.EnumDefs.Count, Is.EqualTo(1));
        Assert.That(tables.EnumMembers.Count, Is.EqualTo(2));
        Assert.That(index.TryGetSection(0x004C, out _, out _), Is.False);
        Assert.That(index.TryGetSection(0x0066, out _, out _), Is.False);
        Assert.That(Enumerable.Range(1, tables.Features.Count)
            .Select(row => tables.Features.GetFeature(row).FeatureId),
            Does.Contain(18u));
        Assert.That(Enumerable.Range(1, tables.Features.Count)
            .Select(row => tables.Features.GetFeature(row).FeatureId),
            Does.Contain(TrRmFeatureTableReader.TypedFrozenValuesFeatureId));
        Assert.That(Enumerable.Range(1, tables.Features.Count)
            .Select(row => tables.Features.GetFeature(row).FeatureId),
            Does.Not.Contain(
                TrRmFeatureTableReader.NativeInterfaceCallsFeatureId));
        Assert.That(Enumerable.Range(1, tables.Features.Count)
            .Select(row => tables.Features.GetFeature(row).FeatureId),
            Does.Not.Contain(
                TrRmFeatureTableReader.MethodParameterLabelsFeatureId));
    }

    [Test]
    public void EmitsSourceOwnedEnumFniSignaturesBesideStandardNativeImports()
    {
        var tree = SyntaxTree.Parse("""
            using Terrum.Interop;
            namespace Demo {
                [Blittable]
                public enum Shade : int8 {
                    Dark = -1,
                    Light = 1
                }
                [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
                [ForeignNativeInterface(VTableByteOffset: uint32(0))]
                public interface IProbe {
                    public unsafe Shade Get();
                    public unsafe void Set(Shade value);
                }
                [NativeImport("fixture.dll", "ReadValue")]
                extern int32 ReadValue();
            }
            """);
        var registration = NativeCoreRegistrationTests
            .CreateRegistration(includeForeignNativeInterface: true);
        var compilation = Compilation.CreateLibrary(tree,
            new CompilationOptions {
                NativeCoreRegistration = registration,
                LayoutCoreRegistration =
                    NativeCoreRegistrationTests
                        .CreateLayoutRegistration(registration),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var enumDeclaration = tree.Root.DescendantNodes().Single(node =>
            node.Kind == SyntaxKind.EnumDeclaration);
        var enumDefinition = program.EnumDefinitions.Single(item =>
            item.Identity is BoundEnumDeclarationIdentity.Source source
            && source.Value.Parts.Any(part =>
                part.IsPrimary && ReferenceEquals(part.Tree, tree)
                && ReferenceEquals(part.Declaration, enumDeclaration)));
        var enumExport = new TrRmSourceEnumDeclarationExport(program, 0,
            enumDeclaration.Span, ["type.shade"],
            ["shade.dark", "shade.light"]);
        var interfaceDefinition = program.Interfaces.Single();
        var interfaceExport = new TrRmSourceTypeExport("Demo", "IProbe",
            "type.probe") {
            GenericArity = 0,
            InterfaceMethodExports = [
                .. interfaceDefinition.Members.Select(member =>
                    new TrRmSourceInterfaceMethodExport(
                        member.SourceInputOrdinal,
                        member.SourceDeclarationSpan,
                        $"method.probe.{member.Name}")),
            ],
        };

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "FniEnumProvider", SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [interfaceExport], [enumExport]);
        var index
            = TrRmArtifactIndex
                .ForForeignNativeInterfaceParameterSurfaceEnumClosureDraft(
                    artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(index);

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(SchemaIdentity, artifact),
            "FniEnumProvider", []);
        Assert.That(reference.EnumDefinitions.Length, Is.EqualTo(1));
        Assert.That(reference.EnumDefinitions[0].UnderlyingType,
            Is.EqualTo(PrimitiveType.Int8));
        Assert.That(reference.EnumDefinitions[0]
            .BlittableTargetRequirement, Is.EqualTo(1));
        Assert.That(reference.Interfaces.Single().Members
            .Single(member => member.Name == "Get")
            .ResultType.PrimitiveType, Is.EqualTo(BoundEnumTypes.Type(0)));

        Assert.That(tables.EnumDefs.Count, Is.EqualTo(1));
        Assert.That(tables.EnumMembers.Count, Is.EqualTo(2));
        Assert.That(tables.ForeignNativeInterfaces.Count, Is.EqualTo(1));
        Assert.That(index.TryGetSection(0x0030, out _, out var sourceCount),
            Is.True);
        Assert.That(sourceCount, Is.EqualTo(2));
        Assert.That(index.TryGetSection(0x0021, out _, out var nativeImportCount),
            Is.True);
        Assert.That(nativeImportCount, Is.EqualTo(1));
        Assert.That(index.TryGetSection(0x0066, out _, out var surfaceCount),
            Is.True);
        Assert.That(surfaceCount, Is.EqualTo(1));

        var enumTypeDefRow = checked((int)
            tables.EnumDefs.GetOwnerTypeDef(1));
        var getMethod = Enumerable.Range(1, tables.Methods.Count)
            .Single(row => tables.Methods.GetName(row) == "Get");
        var getSignature = tables.Methods.GetSignature(getMethod);
        var resultType = tables.TypeSpecs.GetTypeSpecRef(
            tables.Signatures.GetResultTypeSpec(getSignature));
        Assert.That(resultType.Form, Is.EqualTo(0x02));
        Assert.That(resultType.Row, Is.EqualTo(enumTypeDefRow));

        var setMethod = Enumerable.Range(1, tables.Methods.Count)
            .Single(row => tables.Methods.GetName(row) == "Set");
        var setSignature = tables.Methods.GetSignature(setMethod);
        var (parameterFirst, parameterCount)
            = tables.Signatures.GetParamRange(setSignature);
        Assert.That(parameterCount, Is.EqualTo(1));
        var parameterType = tables.TypeSpecs.GetTypeSpecRef(
            tables.Parameters.GetTypeSpec(parameterFirst));
        Assert.That(parameterType.Form, Is.EqualTo(0x02));
        Assert.That(parameterType.Row, Is.EqualTo(enumTypeDefRow));

        var featureIds = Enumerable.Range(1, tables.Features.Count)
            .Select(row => tables.Features.GetFeature(row).FeatureId)
            .ToArray();
        Assert.That(featureIds, Does.Contain(
            TrRmFeatureTableReader.NativeInterfaceCallsFeatureId));
        Assert.That(featureIds, Does.Contain(
            TrRmFeatureTableReader.TypedFrozenValuesFeatureId));
        Assert.That(featureIds, Does.Contain(18u));
        Assert.That(featureIds, Does.Contain(
            TrRmFeatureTableReader.MethodParameterLabelsFeatureId));
        Assert.That(enumDefinition.Members.Length, Is.EqualTo(2));
    }

    [Test]
    public void ReemitsAndRebindsImportedStandardFniEnumTypes()
    {
        var registration = NativeCoreRegistrationTests
            .CreateRegistration(includeForeignNativeInterface: true);
        var layoutRegistration
            = NativeCoreRegistrationTests.CreateLayoutRegistration(
                registration);
        var providerTree = SyntaxTree.Parse("""
            using Terrum.Interop;
            namespace Demo {
                [Blittable]
                public enum Shade : int8 {
                    Dark = -1,
                    Light = 1
                }
                [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
                [ForeignNativeInterface(VTableByteOffset: uint32(0))]
                public interface IProbe {
                    public unsafe Shade Get();
                }
            }
            """);
        var providerCompilation = Compilation.CreateLibrary(providerTree,
            new CompilationOptions {
                NativeCoreRegistration = registration,
                LayoutCoreRegistration = layoutRegistration,
            });
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var providerEnumDeclaration
            = providerTree.Root.DescendantNodes().Single(node =>
                node.Kind == SyntaxKind.EnumDeclaration);
        var providerEnumExport
            = new TrRmSourceEnumDeclarationExport(providerProgram, 0,
                providerEnumDeclaration.Span, ["type.shade"],
                ["shade.dark", "shade.light"]);
        var providerInterface = providerProgram.Interfaces.Single();
        var providerTypeExport
            = new TrRmSourceTypeExport("Demo", "IProbe", "type.probe") {
                InterfaceMethodExports = [
                    .. providerInterface.Members.Select(member =>
                        new TrRmSourceInterfaceMethodExport(
                            member.SourceInputOrdinal,
                            member.SourceDeclarationSpan,
                            $"method.probe.{member.Name}")),
                ],
            };
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "FniEnumProvider", SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [providerTypeExport], [providerEnumExport]);
        var providerInput = new TrRmStandardArtifactInput(SchemaIdentity,
            providerArtifact);
        var providerReference = TrRmExecutableCompiler
            .CreateStandardReference(providerInput, "FniEnumProvider", []);

        var consumerTree = SyntaxTree.Parse("""
            using Demo;
            using Terrum.Interop;
            namespace Consumer {
                [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
                [ForeignNativeInterface(VTableByteOffset: uint32(0))]
                public interface IWrapper {
                    public unsafe Shade Echo(Shade value);
                }
            }
            """);
        var consumerCompilation = Compilation.CreateLibrary(consumerTree,
            providerReference, new CompilationOptions {
                NativeCoreRegistration = registration,
                LayoutCoreRegistration = layoutRegistration,
            });
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = (BoundProgram)consumerCompilation.Program!;
        var consumerInterface = consumerProgram.Interfaces.Single(
            definition => definition.ModuleImport is null);
        var consumerTypeExport
            = new TrRmSourceTypeExport("Consumer", "IWrapper",
                "type.wrapper") {
                InterfaceMethodExports = [
                    .. consumerInterface.Members.Select(member =>
                        new TrRmSourceInterfaceMethodExport(
                            member.SourceInputOrdinal,
                            member.SourceDeclarationSpan,
                            $"method.wrapper.{member.Name}")),
                ],
            };
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "FniEnumConsumer", SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [consumerTypeExport]);
        var consumerIndex
            = TrRmArtifactIndex
                .ForForeignNativeInterfaceParameterSurfaceEnumClosureDraft(
                    consumerArtifact);
        _ = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(consumerIndex);

        var consumerReference = TrRmExecutableCompiler
            .CreateStandardReference(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    consumerArtifact),
                "FniEnumConsumer", [providerInput]);
        Assert.That(consumerReference.EnumDefinitions.Length,
            Is.EqualTo(1));
        Assert.That(consumerReference.EnumDefinitions[0].UnderlyingType,
            Is.EqualTo(PrimitiveType.Int8));
        Assert.That(consumerReference.EnumDefinitions[0].Identity,
            Is.TypeOf<BoundEnumDeclarationIdentity.Standard>());
        Assert.That(consumerReference.Interfaces.Single(
                definition => definition.QualifiedName
                    == "Consumer.IWrapper").Members.Single()
            .ResultType.PrimitiveType, Is.EqualTo(BoundEnumTypes.Type(0)));
        var selectedEnumRoot = TrRmExecutableCompiler
            .CreateStandardReference(providerInput, "FniEnumProvider", [],
                ["type.shade"]);
        var composedReference = TrRmExecutableCompiler
            .CreateStandardReferenceSet([
                consumerReference, selectedEnumRoot,
            ]);
        Assert.That(composedReference.Interfaces.Any(
            static definition => definition.QualifiedName
                == "Consumer.IWrapper"), Is.True);
        Assert.That(((BoundEnumDeclarationIdentity.Standard)
            composedReference.EnumDefinitions.Single().Identity)
            .Value.SelectedExportKeys, Does.Contain("type.shade"));
        Assert.That(((BoundEnumDeclarationIdentity.Standard)
            composedReference.EnumDefinitions.Single().Identity)
            .Value.Expectations.Length, Is.EqualTo(1));

        var replacementTree = SyntaxTree.Parse("""
            using Terrum.Interop;
            namespace Demo {
                [Blittable]
                public enum Shade : int8 {
                    Dark = -1,
                    Light = 2
                }
                [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
                [ForeignNativeInterface(VTableByteOffset: uint32(0))]
                public interface IProbe {
                    public unsafe Shade Get();
                }
            }
            """);
        var replacementCompilation = Compilation.CreateLibrary(
            replacementTree, new CompilationOptions {
                NativeCoreRegistration = registration,
                LayoutCoreRegistration = layoutRegistration,
            });
        Assert.That(replacementCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                replacementCompilation.Diagnostics));
        var replacementProgram
            = (BoundProgram)replacementCompilation.Program!;
        var replacementEnumDeclaration
            = replacementTree.Root.DescendantNodes().Single(node =>
                node.Kind == SyntaxKind.EnumDeclaration);
        var replacementEnumExport
            = new TrRmSourceEnumDeclarationExport(replacementProgram, 0,
                replacementEnumDeclaration.Span, ["type.shade"],
                ["shade.dark", "shade.light"]);
        var replacementInterface = replacementProgram.Interfaces.Single();
        var replacementTypeExport
            = new TrRmSourceTypeExport("Demo", "IProbe", "type.probe") {
                InterfaceMethodExports = [
                    .. replacementInterface.Members.Select(member =>
                        new TrRmSourceInterfaceMethodExport(
                            member.SourceInputOrdinal,
                            member.SourceDeclarationSpan,
                            $"method.probe.{member.Name}")),
                ],
            };
        var replacementArtifact = TrRmExecutableCompiler.EmitStandard(
            replacementProgram, "FniEnumProvider", SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [replacementTypeExport], [replacementEnumExport]);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    consumerArtifact),
                "FniEnumConsumer", [
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        replacementArtifact),
                ]));
    }

    [Test]
    public void AcceptsBlittable128BitEnumInsidePointerClosure()
    {
        var tree = SyntaxTree.Parse("""
            using Terrum.Interop;
            namespace Demo {
                [Blittable]
                public enum Shade : int128 {
                    Dark = -1,
                    Light = 1
                }
                [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
                [ForeignNativeInterface(VTableByteOffset: uint32(0))]
                public interface IProbe {
                    public unsafe void Set(ptr<Shade> value);
                }
            }
            """);
        var registration = NativeCoreRegistrationTests
            .CreateRegistration(includeForeignNativeInterface: true);
        var compilation = Compilation.CreateLibrary(tree,
            new CompilationOptions {
                NativeCoreRegistration = registration,
                LayoutCoreRegistration =
                    NativeCoreRegistrationTests
                        .CreateLayoutRegistration(registration),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var enumDeclaration = tree.Root.DescendantNodes().Single(node =>
            node.Kind == SyntaxKind.EnumDeclaration);
        var enumExport = new TrRmSourceEnumDeclarationExport(program, 0,
            enumDeclaration.Span, ["enum.shade"],
            ["enum.shade.dark", "enum.shade.light"]);
        var interfaceDefinition = program.Interfaces.Single();
        var interfaceExport = new TrRmSourceTypeExport("Demo", "IProbe",
            "type.probe") {
            InterfaceMethodExports = [
                .. interfaceDefinition.Members.Select(member =>
                    new TrRmSourceInterfaceMethodExport(
                        member.SourceInputOrdinal,
                        member.SourceDeclarationSpan,
                        $"method.probe.{member.Name}")),
            ],
        };
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "FniEnumPointerProvider", SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [interfaceExport], [enumExport]);
        var index
            = TrRmArtifactIndex
                .ForForeignNativeInterfaceParameterSurfaceEnumClosureDraft(
                    artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(index);

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(SchemaIdentity, artifact),
            "FniEnumPointerProvider", []);
        Assert.That(reference.EnumDefinitions.Length, Is.EqualTo(1));
        Assert.That(reference.EnumDefinitions[0].UnderlyingType,
            Is.EqualTo(PrimitiveType.Int128));
        Assert.That(BoundEnumTypes.IsType(reference.Interfaces.Single()
            .Members.Single().Parameters.Single().Type.PrimitiveType),
            Is.False);
        Assert.That(reference.NativePointerTypes.Single().PointeeType,
            Is.EqualTo(BoundEnumTypes.Type(0)));

        Assert.That(tables.EnumDefs.Count, Is.EqualTo(1));
        Assert.That(tables.ForeignNativeInterfaces.Count, Is.EqualTo(1));
        Assert.That(tables.Signatures.Count, Is.EqualTo(1));
    }

    [TestCase("int8", false)]
    [TestCase("int128", true)]
    public void RejectsDirectEnumSignaturesWithoutSupportedNativeLayout(
        string underlyingType, bool isBlittable)
    {
        var blittableAttribute = isBlittable ? "[Blittable]" : string.Empty;
        var tree = SyntaxTree.Parse($"""
            using Terrum.Interop;
            namespace Demo {{
                {blittableAttribute}
                public enum Shade : {underlyingType} {{
                    Dark = -1,
                    Light = 1
                }}
                [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
                [ForeignNativeInterface(VTableByteOffset: uint32(0))]
                public interface IProbe {{
                    public unsafe Shade Get();
                }}
            }}
            """);
        var registration = NativeCoreRegistrationTests
            .CreateRegistration(includeForeignNativeInterface: true);
        var compilation = Compilation.CreateLibrary(tree,
            new CompilationOptions {
                NativeCoreRegistration = registration,
                LayoutCoreRegistration =
                    NativeCoreRegistrationTests
                        .CreateLayoutRegistration(registration),
            });

        Assert.That(compilation.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }
}
