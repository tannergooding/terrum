using System;
using System.Collections.Immutable;
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

    private sealed record FniCoreEnumProvider(
        TrRmStandardArtifactInput Input, CompilationReference Reference);

    private static FniCoreEnumProvider CreateFniCoreEnumProvider(
        int cdeclValue = 2)
    {
        var tree = SyntaxTree.Parse($$"""
            namespace Terrum.Interop {
                public enum NativeCallingConvention : int32 {
                    Unmanaged = 1,
                    Cdecl = {{cdeclValue}},
                    Stdcall = 3,
                    Vectorcall = 4
                }
                public enum NativeImportResolutionMode : int32 {
                    Link = 1,
                    Dynamic = 2
                }
                public enum NativeImportResolutionPolicy : int32 {
                    Default = 1,
                    CompatibilitySearch = 2
                }
            }
            public enum LayoutKind : int32 {
                Auto = 0,
                Sequential = 1,
                Union = 2,
                Transparent = 3
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        if (compilation.HasErrors)
        {
            throw new InvalidOperationException(string.Join(
                Environment.NewLine, compilation.Diagnostics));
        }

        var program = (BoundProgram)compilation.Program!;
        var enumExports
            = ImmutableArray.CreateBuilder<
                TrRmSourceEnumDeclarationExport>(
                program.EnumDefinitions.Length);
        foreach (var definition in program.EnumDefinitions)
        {
            if (definition.Identity
                is not BoundEnumDeclarationIdentity.Source source)
            {
                throw new InvalidOperationException(
                    "The Core enum provider fixture contains a non-source enum.");
            }

            var primary = source.Value.Parts.Single(
                static part => part.IsPrimary);
            var exportKey = definition.Name switch {
                "Terrum.Interop.NativeCallingConvention"
                    => "NativeCallingConvention",
                "Terrum.Interop.NativeImportResolutionMode"
                    => "NativeImportResolutionMode",
                "Terrum.Interop.NativeImportResolutionPolicy"
                    => "NativeImportResolutionPolicy",
                "LayoutKind" => "LayoutKind",
                _ => throw new InvalidOperationException(
                    $"Unexpected Core enum '{definition.Name}'."),
            };
            var memberKeys = definition.Members.Select(member =>
                exportKey == "LayoutKind"
                    ? $"layout-kind/member/{member.Name}"
                    : $"{exportKey}/member/{member.Name}")
                .ToImmutableArray();
            enumExports.Add(new TrRmSourceEnumDeclarationExport(program,
                primary.SourceInputOrdinal, primary.Declaration.Span,
                [exportKey], memberKeys));
        }

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "core.fni", SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], enumExports.ToImmutable());
        var input = new TrRmStandardArtifactInput(SchemaIdentity, artifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(input,
            "core.fni", [], [
                "NativeCallingConvention",
                "NativeImportResolutionMode",
                "NativeImportResolutionPolicy",
                "LayoutKind",
            ]);
        return new FniCoreEnumProvider(input, reference);
    }

    private static (NativeCoreRegistrationSet Native,
        LayoutCoreRegistrationSet Layout) CreateFniCoreRegistrations(
        string usingModuleIdentity, string? enumSchemaIdentity = null)
    {
        const string enumProvider = "core.fni";
        const string nativeSchemaProvider = "core.native-schemas";
        const string layoutSchemaProvider = "core.layout-schemas";
        var original = NativeCoreRegistrationTests
            .CreateRegistration(includeForeignNativeInterface: true);

        CoreDeclarationIdentity RebindIdentity(
            CoreDeclarationIdentity identity, string provider)
            => identity with { ProviderIdentity = provider };

        CoreEnumDefinition RebindEnum(CoreEnumDefinition definition)
        {
            var identity = RebindIdentity(definition.Identity, enumProvider);
            return definition with {
                Identity = identity,
                Members = [
                    .. definition.Members.Select(member => member with {
                        Identity = RebindIdentity(member.Identity,
                            enumProvider),
                        DeclaringEnum = identity,
                    }),
                ],
            };
        }

        CoreNativeAttributeSchema RebindNativeSchema(
            CoreNativeAttributeSchema schema)
            => schema with {
                Identity = RebindIdentity(schema.Identity,
                    nativeSchemaProvider),
                Arguments = [
                    .. schema.Arguments.Select(argument => argument with {
                        EnumType = argument.EnumType is
                            CoreDeclarationIdentity enumType
                            ? RebindIdentity(enumType, enumProvider) : null,
                        Default = argument.Default is
                            CoreNativeAttributeDefault defaultValue
                            && defaultValue.Kind
                                == CoreNativeAttributeDefaultKind.EnumMember
                                ? defaultValue with {
                                    Member = RebindIdentity(
                                        defaultValue.Member!.Value,
                                        enumProvider),
                                } : argument.Default,
                    }),
                ],
                CallingConventionEnum = schema.CallingConventionEnum is
                    CoreDeclarationIdentity conventions
                    ? RebindIdentity(conventions, enumProvider) : null,
                ResolutionModeEnum = schema.ResolutionModeEnum is
                    CoreDeclarationIdentity modes
                    ? RebindIdentity(modes, enumProvider) : null,
                ResolutionPolicyEnum = schema.ResolutionPolicyEnum is
                    CoreDeclarationIdentity policies
                    ? RebindIdentity(policies, enumProvider) : null,
            };

        CoreLayoutAttributeSchema RebindLayoutSchema(
            CoreLayoutAttributeSchema schema)
            => schema with {
                Identity = RebindIdentity(schema.Identity,
                    layoutSchemaProvider),
                Arguments = [
                    .. schema.Arguments.Select(argument => argument with {
                        EnumType = argument.EnumType is
                            CoreDeclarationIdentity enumType
                            ? RebindIdentity(enumType, enumProvider) : null,
                    }),
                ],
            };

        var callingConventions = RebindEnum(original.CallingConventions);
        var resolutionModes = RebindEnum(original.ResolutionModes);
        var resolutionPolicies = RebindEnum(original.ResolutionPolicies);
        var nativeImport = RebindNativeSchema(original.NativeImport);
        var nativeCallable = RebindNativeSchema(original.NativeCallable);
        var nativeCallConv = RebindNativeSchema(original.NativeCallConv);
        var nativeExport = RebindNativeSchema(original.NativeExport!);
        var foreignNativeInterface = RebindNativeSchema(
            original.ForeignNativeInterface!);

        var originalLayout
            = LayoutCoreRegistrationTestData.CreateDeclarations("core.native");
        var layoutKinds = RebindEnum(originalLayout.LayoutKinds);
        var layout = RebindLayoutSchema(originalLayout.Layout);
        var blittable = RebindLayoutSchema(originalLayout.Blittable);
        var declarations
            = ImmutableArray.CreateBuilder<
                CoreProviderDeclarationSnapshot>();
        declarations.Add(CoreProviderDeclarationSnapshot.From(
            callingConventions));
        declarations.Add(CoreProviderDeclarationSnapshot.From(
            resolutionModes));
        declarations.Add(CoreProviderDeclarationSnapshot.From(
            resolutionPolicies));
        declarations.Add(CoreProviderDeclarationSnapshot.From(layoutKinds));
        declarations.Add(CoreProviderDeclarationSnapshot.From(nativeImport));
        declarations.Add(CoreProviderDeclarationSnapshot.From(
            nativeCallable));
        declarations.Add(CoreProviderDeclarationSnapshot.From(nativeCallConv));
        declarations.Add(CoreProviderDeclarationSnapshot.From(nativeExport));
        declarations.Add(CoreProviderDeclarationSnapshot.From(
            foreignNativeInterface));
        declarations.Add(CoreProviderDeclarationSnapshot.From(layout));
        declarations.Add(CoreProviderDeclarationSnapshot.From(blittable));

        var bindings = CoreProviderModuleBindings.Create(
            usingModuleIdentity, [
                new CoreProviderModuleOrigin(enumProvider, "fni-enum-revision",
                    new CoreProviderSchemaSelection(
                        enumSchemaIdentity ?? SchemaIdentity, 1),
                    1, TrRmModuleTableReader
                        .WindowsX64NativeTargetRequirement, 0),
                new CoreProviderModuleOrigin(nativeSchemaProvider,
                    "native-schema-revision",
                    new CoreProviderSchemaSelection(
                        "core.native-registration", 1), 1, 0, 0),
                new CoreProviderModuleOrigin(layoutSchemaProvider,
                    "layout-schema-revision",
                    new CoreProviderSchemaSelection(
                        "layout-registration", 1), 1, 0, 0),
            ], declarations.ToImmutable());
        var nativeRegistration = NativeCoreRegistrationSet.Create(bindings,
            callingConventions, resolutionModes, resolutionPolicies,
            nativeImport, nativeCallable, nativeCallConv, nativeExport,
            foreignNativeInterface);
        var layoutRegistration = LayoutCoreRegistrationSet.Create(bindings,
            layoutKinds, layout, blittable);
        return (nativeRegistration, layoutRegistration);
    }

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
        var coreProvider = CreateFniCoreEnumProvider();
        var (registration, layoutRegistration)
            = CreateFniCoreRegistrations("FniEnumProvider");
        var compilation = Compilation.CreateLibrary(tree,
            coreProvider.Reference,
            new CompilationOptions {
                NativeCoreRegistration = registration,
                LayoutCoreRegistration = layoutRegistration,
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
            "FniEnumProvider", [coreProvider.Input]);
        var shadeEnumIndex = Enumerable.Range(0,
                reference.EnumDefinitions.Length)
            .Single(index => reference.EnumDefinitions[index].Name
                == "Demo.Shade");
        Assert.That(reference.EnumDefinitions[shadeEnumIndex].UnderlyingType,
            Is.EqualTo(PrimitiveType.Int8));
        Assert.That(reference.EnumDefinitions[shadeEnumIndex]
            .BlittableTargetRequirement, Is.EqualTo(1));
        Assert.That(reference.Interfaces.Single().Members
            .Single(member => member.Name == "Get")
            .ResultType.PrimitiveType,
            Is.EqualTo(BoundEnumTypes.Type(shadeEnumIndex)));

        Assert.That(tables.EnumDefs.Count, Is.EqualTo(5));
        Assert.That(tables.EnumMembers.Count, Is.EqualTo(14));
        Assert.That(tables.ForeignNativeInterfaces.Count, Is.EqualTo(1));
        Assert.That(index.TryGetSection(0x0030, out _, out var sourceCount),
            Is.True);
        Assert.That(sourceCount, Is.EqualTo(14));
        Assert.That(index.TryGetSection(0x0021, out _, out var nativeImportCount),
            Is.True);
        Assert.That(nativeImportCount, Is.EqualTo(1));
        Assert.That(index.TryGetSection(0x0066, out _, out var surfaceCount),
            Is.True);
        Assert.That(surfaceCount, Is.EqualTo(1));

        var shadeEnumRow = Enumerable.Range(1, tables.EnumDefs.Count)
            .Single(row => tables.TypeDefs.GetName(checked((int)
                tables.EnumDefs.GetOwnerTypeDef(row))) == "Shade");
        var enumTypeDefRow = checked((int)
            tables.EnumDefs.GetOwnerTypeDef(shadeEnumRow));
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
        var coreProvider = CreateFniCoreEnumProvider();
        var (registration, layoutRegistration)
            = CreateFniCoreRegistrations("FniEnumProvider");
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
            coreProvider.Reference,
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
            .CreateStandardReference(providerInput, "FniEnumProvider",
                [coreProvider.Input]);

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
        var (consumerRegistration, consumerLayoutRegistration)
            = CreateFniCoreRegistrations("FniEnumConsumer");
        var consumerCompilation = Compilation.CreateLibrary(consumerTree,
            providerReference, new CompilationOptions {
                NativeCoreRegistration = consumerRegistration,
                LayoutCoreRegistration = consumerLayoutRegistration,
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
                "FniEnumConsumer", [providerInput, coreProvider.Input]);
        var consumerShadeEnumIndex = Enumerable.Range(0,
                consumerReference.EnumDefinitions.Length)
            .Single(index => consumerReference.EnumDefinitions[index].Name
                == "Demo.Shade");
        Assert.That(
            consumerReference.EnumDefinitions[consumerShadeEnumIndex]
                .UnderlyingType,
            Is.EqualTo(PrimitiveType.Int8));
        Assert.That(
            consumerReference.EnumDefinitions[consumerShadeEnumIndex].Identity,
            Is.TypeOf<BoundEnumDeclarationIdentity.Standard>());
        Assert.That(consumerReference.Interfaces.Single(
                definition => definition.QualifiedName
                    == "Consumer.IWrapper").Members.Single()
            .ResultType.PrimitiveType,
            Is.EqualTo(BoundEnumTypes.Type(consumerShadeEnumIndex)));
        var selectedEnumRoot = TrRmExecutableCompiler
            .CreateStandardReference(providerInput, "FniEnumProvider",
                [coreProvider.Input], ["type.shade"]);
        var composedReference = TrRmExecutableCompiler
            .CreateStandardReferenceSet([
                consumerReference, selectedEnumRoot,
            ]);
        Assert.That(composedReference.Interfaces.Any(
            static definition => definition.QualifiedName
                == "Consumer.IWrapper"), Is.True);
        var composedShade = composedReference.EnumDefinitions.Single(
            static definition => definition.Name == "Demo.Shade");
        Assert.That(((BoundEnumDeclarationIdentity.Standard)
            composedShade.Identity)
            .Value.SelectedExportKeys, Does.Contain("type.shade"));
        Assert.That(((BoundEnumDeclarationIdentity.Standard)
            composedShade.Identity)
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
            replacementTree, coreProvider.Reference,
            new CompilationOptions {
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
        var replacementInput = new TrRmStandardArtifactInput(SchemaIdentity,
            replacementArtifact);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    consumerArtifact),
                "FniEnumConsumer", [replacementInput, coreProvider.Input]));
    }

    [Test]
    public void RejectsCoreEnumProviderWithWrongSchemaIdentity()
    {
        var coreProvider = CreateFniCoreEnumProvider();
        var (registration, layoutRegistration)
            = CreateFniCoreRegistrations("FniEnumWrongSchema",
                "trrm/wrong-core-enum-schema");
        var tree = SyntaxTree.Parse("""
            using Terrum.Interop;
            namespace Demo {
                [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
                [ForeignNativeInterface(VTableByteOffset: uint32(0))]
                public interface IProbe {
                    public unsafe void Ping();
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree,
            coreProvider.Reference, new CompilationOptions {
                NativeCoreRegistration = registration,
                LayoutCoreRegistration = layoutRegistration,
            });

        Assert.That(compilation.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3005"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void RejectsCoreEnumProviderReplacementWithChangedMemberValue()
    {
        var replacementProvider = CreateFniCoreEnumProvider(cdeclValue: 3);
        var (registration, layoutRegistration)
            = CreateFniCoreRegistrations("FniEnumCoreReplacement");
        var tree = SyntaxTree.Parse("""
            using Terrum.Interop;
            namespace Demo {
                [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
                [ForeignNativeInterface(VTableByteOffset: uint32(0))]
                public interface IProbe {
                    public unsafe void Ping();
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree,
            replacementProvider.Reference, new CompilationOptions {
                NativeCoreRegistration = registration,
                LayoutCoreRegistration = layoutRegistration,
            });

        Assert.That(compilation.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3005"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void RejectsCoreEnumPublicationWithoutOwnedStandardProvider()
    {
        var (registration, layoutRegistration)
            = CreateFniCoreRegistrations("FniEnumMissingCoreProvider");
        var tree = SyntaxTree.Parse("""
            using Terrum.Interop;
            namespace Demo {
                [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
                [ForeignNativeInterface(VTableByteOffset: uint32(0))]
                public interface IProbe {
                    public unsafe void Ping();
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree,
            new CompilationOptions {
                NativeCoreRegistration = registration,
                LayoutCoreRegistration = layoutRegistration,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var interfaceDefinition = program.Interfaces.Single();
        var interfaceExport
            = new TrRmSourceTypeExport("Demo", "IProbe", "type.probe") {
                InterfaceMethodExports = [
                    .. interfaceDefinition.Members.Select(member =>
                        new TrRmSourceInterfaceMethodExport(
                            member.SourceInputOrdinal,
                            member.SourceDeclarationSpan,
                            $"method.probe.{member.Name}")),
                ],
            };
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program,
                "FniEnumMissingCoreProvider", SchemaIdentity,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
                [interfaceExport],
                Array.Empty<TrRmSourceEnumDeclarationExport>()));
    }

    [Test]
    public void ReemitsCoreEnumOnlyProviderImports()
    {
        var coreProvider = CreateFniCoreEnumProvider();
        var tree = SyntaxTree.Parse("namespace Wrapper {}");
        var (registration, layoutRegistration)
            = CreateFniCoreRegistrations("FniCoreEnumWrapper");
        var compilation = Compilation.CreateLibrary(tree,
            coreProvider.Reference, new CompilationOptions {
                NativeCoreRegistration = registration,
                LayoutCoreRegistration = layoutRegistration,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!, "FniCoreEnumWrapper",
            SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement, [],
            Array.Empty<TrRmSourceEnumDeclarationExport>());
        var index
            = TrRmArtifactIndex
                .ForForeignNativeInterfaceParameterSurfaceEnumClosureDraft(
                    artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(index);
        Assert.That(tables.EnumDefs.Count, Is.EqualTo(4));
        Assert.That(tables.EnumMembers.Count, Is.EqualTo(12));
        Assert.That(index.TryGetSection(0x001C, out _, out var importCount),
            Is.True);
        Assert.That(importCount, Is.EqualTo(4));

        var input = new TrRmStandardArtifactInput(SchemaIdentity, artifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(input,
            "FniCoreEnumWrapper", [coreProvider.Input]);
        var (reemitRegistration, reemitLayoutRegistration)
            = CreateFniCoreRegistrations("FniCoreEnumReemission");
        var reemitCompilation = Compilation.CreateLibrary(tree, reference,
            new CompilationOptions {
                NativeCoreRegistration = reemitRegistration,
                LayoutCoreRegistration = reemitLayoutRegistration,
            });
        Assert.That(reemitCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, reemitCompilation.Diagnostics));
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)reemitCompilation.Program!,
            "FniCoreEnumReemission", SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement, [],
            Array.Empty<TrRmSourceEnumDeclarationExport>());
        var reemittedIndex
            = TrRmArtifactIndex
                .ForForeignNativeInterfaceParameterSurfaceEnumClosureDraft(
                    reemittedArtifact);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(reemittedIndex);
        Assert.That(reemittedTables.EnumDefs.Count, Is.EqualTo(4));
        Assert.That(reemittedTables.EnumMembers.Count, Is.EqualTo(12));
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
        var coreProvider = CreateFniCoreEnumProvider();
        var (registration, layoutRegistration)
            = CreateFniCoreRegistrations("FniEnumPointerProvider");
        var compilation = Compilation.CreateLibrary(tree,
            coreProvider.Reference,
            new CompilationOptions {
                NativeCoreRegistration = registration,
                LayoutCoreRegistration = layoutRegistration,
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
            "FniEnumPointerProvider", [coreProvider.Input]);
        var shadeEnumIndex = Enumerable.Range(0,
                reference.EnumDefinitions.Length)
            .Single(index => reference.EnumDefinitions[index].Name
                == "Demo.Shade");
        Assert.That(reference.EnumDefinitions[shadeEnumIndex].UnderlyingType,
            Is.EqualTo(PrimitiveType.Int128));
        Assert.That(BoundEnumTypes.IsType(reference.Interfaces.Single()
            .Members.Single().Parameters.Single().Type.PrimitiveType),
            Is.False);
        Assert.That(reference.NativePointerTypes.Single().PointeeType,
            Is.EqualTo(BoundEnumTypes.Type(shadeEnumIndex)));

        Assert.That(tables.EnumDefs.Count, Is.EqualTo(5));
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
