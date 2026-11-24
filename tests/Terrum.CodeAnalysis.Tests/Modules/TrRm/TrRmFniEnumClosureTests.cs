using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
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
    public void RejectsFniEnumSectionsUnderThePredecessorParameterSurfaceSchema()
    {
        var artifact = CreateFniCoreEnumProvider().Input.Artifact.ToArray();

        Assert.Throws<InvalidDataException>(() =>
            TrRmArtifactIndex
                .ForForeignNativeInterfaceParameterSurfaceDraft(artifact));
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
                    public unsafe Shade Echo(Shade value);
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
            .Single(member => member.Name == "Echo")
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
        Assert.That(surfaceCount, Is.EqualTo(2));

        var shadeEnumRow = Enumerable.Range(1, tables.EnumDefs.Count)
            .Single(row => tables.TypeDefs.GetName(checked((int)
                tables.EnumDefs.GetOwnerTypeDef(row))) == "Shade");
        var enumTypeDefRow = checked((int)
            tables.EnumDefs.GetOwnerTypeDef(shadeEnumRow));
        var echoMethod = Enumerable.Range(1, tables.Methods.Count)
            .Single(row => tables.Methods.GetName(row) == "Echo");
        var echoSignature = tables.Methods.GetSignature(echoMethod);
        var resultType = tables.TypeSpecs.GetTypeSpecRef(
            tables.Signatures.GetResultTypeSpec(echoSignature));
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
    public void RoundTripsNestedEnumConstantThroughFniBodyAndNativeLowering()
    {
        var coreProvider = CreateFniCoreEnumProvider();
        var (registration, layoutRegistration)
            = CreateFniCoreRegistrations("FniEnumBodyProvider");
        var tree = SyntaxTree.Parse("""
            using Terrum.Interop;
            namespace Demo {
                [Blittable]
                public enum Shade : int8 {
                    Negative = -1,
                    Positive = 1
                }
                [Blittable]
                public enum Tint : uint16 {
                    Green = 5
                }
                [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
                [ForeignNativeInterface(VTableByteOffset: uint32(0))]
                public interface IProbe {
                    public unsafe Shade GetShade(Tint value);
                    public unsafe Shade GetCoreShade(LayoutKind value);
                    public unsafe Shade Echo(Shade value);
                    public unsafe NativeCallingConvention EchoConvention(
                        NativeCallingConvention value);
                }
                Shade Invoke(ptr<void> receiver) {
                    unsafe {
                        return IProbe.Echo(receiver,
                            IProbe.GetShade(receiver, Tint.Green));
                    }
                }
                Shade InvokeCoreNested(ptr<void> receiver) {
                    unsafe {
                        return IProbe.Echo(receiver,
                            IProbe.GetCoreShade(receiver,
                                LayoutKind.Sequential));
                    }
                }
                Shade InvokeDirect(ptr<void> receiver) {
                    unsafe { return IProbe.Echo(receiver, Shade.Positive); }
                }
                Shade InvokeStaged(ptr<void> receiver) {
                    Shade staged = Shade.Negative;
                    unsafe { return IProbe.Echo(receiver, staged); }
                }
                NativeCallingConvention InvokeCore(
                    ptr<void> receiver) {
                    unsafe {
                        return IProbe.EchoConvention(receiver,
                            NativeCallingConvention.Cdecl);
                    }
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree,
            coreProvider.Reference, new CompilationOptions {
                NativeCoreRegistration = registration,
                LayoutCoreRegistration = layoutRegistration,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        TrRmSourceEnumDeclarationExport SourceEnumExport(
            string qualifiedName, string exportKey)
        {
            var definition = program.EnumDefinitions.Single(item =>
                item.Name == qualifiedName
                && item.Identity is BoundEnumDeclarationIdentity.Source);
            var source = (BoundEnumDeclarationIdentity.Source)
                definition.Identity;
            var primary = source.Value.Parts.Single(
                static part => part.IsPrimary);
            return new TrRmSourceEnumDeclarationExport(program,
                primary.SourceInputOrdinal, primary.Declaration.Span,
                [exportKey], [.. definition.Members.Select(member =>
                    $"{exportKey}.{member.Name.ToLowerInvariant()}")]);
        }

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
            "FniEnumBodyProvider", SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [interfaceExport], [
                SourceEnumExport("Demo.Shade", "type.shade"),
                SourceEnumExport("Demo.Tint", "type.tint"),
            ]);
        var index
            = TrRmArtifactIndex
                .ForForeignNativeInterfaceParameterSurfaceEnumClosureDraft(
                    artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(index);
        Assert.That(tables.EnumDefs.Count, Is.EqualTo(6));
        Assert.That(tables.EnumMembers.Count, Is.EqualTo(15));
        var shadeTypeDef = Enumerable.Range(1, tables.TypeDefs.Count)
            .Single(row => tables.TypeDefs.GetName(row) == "Shade");
        var tintTypeDef = Enumerable.Range(1, tables.TypeDefs.Count)
            .Single(row => tables.TypeDefs.GetName(row) == "Tint");
        int FindTypeSpecRow(int typeDefRow)
            => Enumerable.Range(1, tables.TypeSpecs.Count).Single(row => {
                var type = tables.TypeSpecs.GetTypeSpecRef(
                    checked((uint)row));
                return type.Form == 0x02 && type.Row == typeDefRow;
            });

        var shadeTypeSpecRow = FindTypeSpecRow(shadeTypeDef);
        var tintTypeSpecRow = FindTypeSpecRow(tintTypeDef);
        bool HasSimpleName(string name, string simpleName)
            => name == simpleName
                || name.EndsWith("." + simpleName,
                    StringComparison.Ordinal);
        var invokeRow = Enumerable.Range(1, tables.Methods.Count)
            .Single(row => HasSimpleName(tables.Methods.GetName(row),
                "Invoke"));
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(invokeRow), tables.Contracts.Count);
        bool HasEnumTypeSpec(TrRmRegisterBodyReader candidateBody,
            int typeSpecRow)
        {
            var token = 0x1100_0000u | checked((uint)typeSpecRow);
            for (var index = 0; index < candidateBody.TypeCount; index++)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(
                        candidateBody.TypeMap.Slice(index * sizeof(uint)))
                    == token)
                {
                    return true;
                }
            }

            return false;
        }

        Assert.That(HasEnumTypeSpec(body, shadeTypeSpecRow), Is.True);
        Assert.That(HasEnumTypeSpec(body, tintTypeSpecRow), Is.True);
        Assert.That(body.ConstantCount, Is.EqualTo(1));
        var constantTypeSpec = BinaryPrimitives.ReadUInt32LittleEndian(
            body.ConstantMap);
        var constantType = tables.TypeSpecs.GetTypeSpecRef(
            constantTypeSpec);
        Assert.That(constantType.Form, Is.EqualTo(0x02));
        Assert.That(constantType.Row, Is.EqualTo(tintTypeDef));
        foreach (var functionName in new[] {
            "InvokeDirect", "InvokeStaged",
        })
        {
            var functionRow = Enumerable.Range(1, tables.Methods.Count)
                .Single(row => HasSimpleName(
                    tables.Methods.GetName(row), functionName));
            var functionBody = tables.Bodies.GetRegisterBody(
                tables.Methods.GetBodyRow(functionRow),
                tables.Contracts.Count);
            Assert.That(functionBody.ConstantCount, Is.EqualTo(1));
            Assert.That(HasEnumTypeSpec(functionBody, shadeTypeSpecRow),
                Is.True);
        }

        var input = new TrRmStandardArtifactInput(SchemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            "FniEnumBodyProvider", [coreProvider.Input]);
        var loadedShadeIndex = Enumerable.Range(0,
                loaded.EnumDefinitions.Length)
            .Single(enumIndex => loaded.EnumDefinitions[enumIndex].Name
                == "Demo.Shade");
        var loadedTintIndex = Enumerable.Range(0,
                loaded.EnumDefinitions.Length)
            .Single(enumIndex => loaded.EnumDefinitions[enumIndex].Name
                == "Demo.Tint");
        var loadedCoreEnumIndex = Enumerable.Range(0,
                loaded.EnumDefinitions.Length)
            .Single(enumIndex => loaded.EnumDefinitions[enumIndex].Name
                == "Terrum.Interop.NativeCallingConvention");
        var loadedLayoutEnumIndex = Enumerable.Range(0,
                loaded.EnumDefinitions.Length)
            .Single(enumIndex => loaded.EnumDefinitions[enumIndex].Name
                == "LayoutKind");
        BoundCall? FindCall(string functionName, string memberName)
        {
            BoundCall? result = null;
            var function = loaded.Functions.Single(item =>
                HasSimpleName(item.Name, functionName));
            _ = BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall {
                            ForeignInterfaceCall:
                                BoundForeignInterfaceCall foreignCall,
                        } call && foreignCall.Member.Name == memberName)
                    {
                        result = call;
                    }
                });
            return result;
        }

        var outerCall = FindCall("Invoke", "Echo");
        var nestedCall = FindCall("Invoke", "GetShade");
        Assert.That(outerCall, Is.Not.Null);
        Assert.That(nestedCall, Is.Not.Null);
        Assert.That(outerCall!.Type,
            Is.EqualTo(BoundEnumTypes.Type(loadedShadeIndex)));
        Assert.That(outerCall.Arguments.Single(),
            Is.TypeOf<BoundCall>());
        var nestedConstant = (BoundConstant)nestedCall!.Arguments.Single();
        Assert.That(nestedConstant.Type,
            Is.EqualTo(BoundEnumTypes.Type(loadedTintIndex)));
        Assert.That(nestedConstant.RepresentationType,
            Is.EqualTo(PrimitiveType.UInt16));
        var directCall = FindCall("InvokeDirect", "Echo");
        var directConstant = (BoundConstant)directCall!.Arguments.Single();
        Assert.That(directConstant.Type,
            Is.EqualTo(BoundEnumTypes.Type(loadedShadeIndex)));
        Assert.That(directConstant.RepresentationType,
            Is.EqualTo(PrimitiveType.Int8));
        var stagedCall = FindCall("InvokeStaged", "Echo");
        Assert.That(stagedCall!.Arguments.Single(), Is.TypeOf<BoundRead>());
        Assert.That(stagedCall.Arguments.Single().Type,
            Is.EqualTo(BoundEnumTypes.Type(loadedShadeIndex)));
        var coreCall = FindCall("InvokeCore", "EchoConvention");
        Assert.That(coreCall!.Type,
            Is.EqualTo(BoundEnumTypes.Type(loadedCoreEnumIndex)));
        var coreConstant = (BoundConstant)coreCall.Arguments.Single();
        Assert.That(coreConstant.Type,
            Is.EqualTo(BoundEnumTypes.Type(loadedCoreEnumIndex)));
        Assert.That(coreConstant.RepresentationType,
            Is.EqualTo(PrimitiveType.Int32));
        var nestedCoreCall = FindCall("InvokeCoreNested", "GetCoreShade");
        var nestedCoreConstant
            = (BoundConstant)nestedCoreCall!.Arguments.Single();
        Assert.That(nestedCoreConstant.Type,
            Is.EqualTo(BoundEnumTypes.Type(loadedLayoutEnumIndex)));
        Assert.That(nestedCoreConstant.RepresentationType,
            Is.EqualTo(PrimitiveType.Int32));
        var nativeFunctionNames = loaded.Functions.Where(function =>
            HasSimpleName(function.Name, "Invoke")
            || HasSimpleName(function.Name, "InvokeDirect")
            || HasSimpleName(function.Name, "InvokeStaged")
            || HasSimpleName(function.Name, "InvokeCore")
            || HasSimpleName(function.Name, "InvokeCoreNested"))
            .Select(static function => function.Name).ToArray();
        Assert.That(nativeFunctionNames.Length, Is.EqualTo(5));
        Assert.That(NativeEmitter.EmitLibrary(loaded,
            nativeFunctionNames,
            NativeRuntimePolicy.FailFast), Is.Not.Empty);

        var stagedFunctionRow = Enumerable.Range(1, tables.Methods.Count)
            .Single(row => HasSimpleName(tables.Methods.GetName(row),
                "InvokeStaged"));
        var stagedBody = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(stagedFunctionRow),
            tables.Contracts.Count);
        var malformedTypeMap = stagedBody.TypeMap.ToArray();
        var shadeTypeToken = 0x1100_0000u
            | checked((uint)shadeTypeSpecRow);
        var tintTypeToken = 0x1100_0000u
            | checked((uint)tintTypeSpecRow);
        var replacedTypeCount = 0;
        for (var typeIndex = 0; typeIndex < stagedBody.TypeCount;
            typeIndex++)
        {
            var offset = typeIndex * sizeof(uint);
            if (BinaryPrimitives.ReadUInt32LittleEndian(
                    malformedTypeMap.AsSpan(offset)) == shadeTypeToken)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    malformedTypeMap.AsSpan(offset), tintTypeToken);
                replacedTypeCount++;
            }
        }

        Assert.That(replacedTypeCount, Is.GreaterThan(0));
        var stagedBlocks = new TrRmRegisterBodyBlock[stagedBody.BlockCount];
        for (var blockIndex = 0; blockIndex < stagedBlocks.Length;
            blockIndex++)
        {
            var block = stagedBody.GetBlock(blockIndex);
            stagedBlocks[blockIndex] = new TrRmRegisterBodyBlock(
                BinaryPrimitives.ReadUInt32LittleEndian(block),
                BinaryPrimitives.ReadUInt32LittleEndian(block[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(block[8..]));
        }

        var stagedRecords
            = new ReadOnlyMemory<byte>[stagedBody.RecordCount];
        for (var recordIndex = 0; recordIndex < stagedRecords.Length;
            recordIndex++)
        {
            stagedRecords[recordIndex]
                = stagedBody.GetRecord(recordIndex).ToArray();
        }

        var malformedStagedBody = TrRmRegisterBodyWriter.Encode(
            stagedBody.HomeMap.ToArray(), malformedTypeMap,
            stagedBody.MethodMap.ToArray(), stagedBody.FieldMap.ToArray(),
            stagedBody.ConstantMap.ToArray(), stagedBlocks, stagedRecords,
            stagedBody.Code.ToArray(), tables.Contracts.Count,
            TrRmArtifactSchema
                .ForeignNativeInterfaceParameterSurfaceEnumClosureDraft);
        var malformedArtifact = ReplaceRegisterBody(artifact,
            TrRmArtifactSchema
                .ForeignNativeInterfaceParameterSurfaceEnumClosureDraft,
            SchemaIdentity,
            tables.Methods.GetBodyRow(stagedFunctionRow),
            malformedStagedBody);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(malformedArtifact,
                "FniEnumBodyProvider", [coreProvider.Input]));
    }

    [Test]
    public void RejectsEnumDefaultsOnFniParameters()
    {
        var coreProvider = CreateFniCoreEnumProvider();
        var (registration, layoutRegistration)
            = CreateFniCoreRegistrations("FniEnumDefault");
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
                    public unsafe Shade Echo(
                        Shade value = Shade.Light);
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree,
            coreProvider.Reference, new CompilationOptions {
                NativeCoreRegistration = registration,
                LayoutCoreRegistration = layoutRegistration,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var enumDeclaration = tree.Root.DescendantNodes().Single(node =>
            node.Kind == SyntaxKind.EnumDeclaration);
        var enumExport = new TrRmSourceEnumDeclarationExport(program, 0,
            enumDeclaration.Span, ["type.shade"],
            ["shade.dark", "shade.light"]);
        var member = program.Interfaces.Single().Members.Single();
        var interfaceExport = new TrRmSourceTypeExport("Demo", "IProbe",
            "type.probe") {
            InterfaceMethodExports = [
                new TrRmSourceInterfaceMethodExport(
                    member.SourceInputOrdinal, member.SourceDeclarationSpan,
                    "method.probe.Echo"),
            ],
        };
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program,
                "FniEnumDefault", SchemaIdentity,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
                [interfaceExport], [enumExport]));
    }

    [Test]
    public void RejectsMalformedFniEnumRowsBeforeBinding()
    {
        var artifact = CreateFniCoreEnumProvider().Input.Artifact.ToArray();

        byte[] CorruptSectionUInt32(ushort sectionKind, int fieldOffset,
            uint value)
        {
            var sectionCount = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(
                    artifact.AsSpan(12, sizeof(uint))));
            for (var sectionIndex = 0; sectionIndex < sectionCount;
                sectionIndex++)
            {
                var directoryOffset = 32 + sectionIndex * 16;
                var currentKind = BinaryPrimitives.ReadUInt16LittleEndian(
                    artifact.AsSpan(directoryOffset, sizeof(ushort)));
                if (currentKind != sectionKind)
                {
                    continue;
                }

                var sectionOffset = checked((int)
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        artifact.AsSpan(directoryOffset + 8,
                            sizeof(uint))));
                var corrupted = artifact.ToArray();
                BinaryPrimitives.WriteUInt32LittleEndian(
                    corrupted.AsSpan(sectionOffset + fieldOffset,
                        sizeof(uint)), value);
                return corrupted;
            }

            throw new InvalidOperationException(
                $"The FNI enum artifact has no section 0x{sectionKind:X4}.");
        }

        foreach (var malformedArtifact in new[] {
            CorruptSectionUInt32(0x0053, 0, uint.MaxValue),
            CorruptSectionUInt32(0x0054, 16, uint.MaxValue),
        })
        {
            Assert.Throws<InvalidDataException>(() =>
                TrRmStructuralMetadataValidator
                    .ValidateAndGetExecutionTables(
                        TrRmArtifactIndex
                            .ForForeignNativeInterfaceParameterSurfaceEnumClosureDraft(
                                malformedArtifact)));
        }
    }

    [TestCase("value + Shade.Light", "int8")]
    [TestCase("value == Shade.Light", "bool")]
    public void RejectsUnselectedEnumOperators(string expression,
        string returnType)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
            [Blittable]
            public enum Shade : int8 {
                Dark = -1,
                Light = 1
            }

            {{returnType}} Test(Shade value) {
                return {{expression}};
            }
            """));

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
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
                    public unsafe Shade Echo(Shade value);
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

        byte[] CreateShiftedProviderArtifact()
        {
            var shiftedTree = SyntaxTree.Parse("""
                using Terrum.Interop;
                namespace Demo {
                    [Blittable]
                    public enum Before : int8 {
                        Zero = 0
                    }
                    [Blittable]
                    public enum Shade : int8 {
                        Dark = -1,
                        Light = 1
                    }
                    [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
                    [ForeignNativeInterface(VTableByteOffset: uint32(0))]
                    public interface IProbe {
                        public unsafe Shade Echo(Shade value);
                    }
                }
                """);
            var shiftedCompilation = Compilation.CreateLibrary(shiftedTree,
                coreProvider.Reference,
                new CompilationOptions {
                    NativeCoreRegistration = registration,
                    LayoutCoreRegistration = layoutRegistration,
                });
            Assert.That(shiftedCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    shiftedCompilation.Diagnostics));
            var shiftedProgram
                = (BoundProgram)shiftedCompilation.Program!;
            TrRmSourceEnumDeclarationExport EnumExport(
                string qualifiedName, string exportKey,
                string[] memberKeys)
            {
                var definition = shiftedProgram.EnumDefinitions.Single(
                    item => item.Name == qualifiedName
                        && item.Identity
                            is BoundEnumDeclarationIdentity.Source);
                var source = (BoundEnumDeclarationIdentity.Source)
                    definition.Identity;
                var primary = source.Value.Parts.Single(
                    static part => part.IsPrimary);
                return new TrRmSourceEnumDeclarationExport(shiftedProgram,
                    primary.SourceInputOrdinal,
                    primary.Declaration.Span, [exportKey], memberKeys);
            }

            var shiftedInterface = shiftedProgram.Interfaces.Single();
            var shiftedTypeExport
                = new TrRmSourceTypeExport("Demo", "IProbe",
                    "type.probe") {
                    InterfaceMethodExports = [
                        .. shiftedInterface.Members.Select(member =>
                            new TrRmSourceInterfaceMethodExport(
                                member.SourceInputOrdinal,
                                member.SourceDeclarationSpan,
                                $"method.probe.{member.Name}")),
                    ],
                };
            return TrRmExecutableCompiler.EmitStandard(shiftedProgram,
                "FniEnumProvider", SchemaIdentity,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
                [shiftedTypeExport], [
                    EnumExport("Demo.Before", "type.before",
                        ["before.zero"]),
                    EnumExport("Demo.Shade", "type.shade",
                        ["shade.dark", "shade.light"]),
                ]);
        }

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
            Shade Invoke(ptr<void> receiver) {
                unsafe { return IProbe.Echo(receiver, Shade.Light); }
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
        var consumerInput = new TrRmStandardArtifactInput(SchemaIdentity,
            consumerArtifact);
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
        var loadedConsumer = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity,
                consumerArtifact),
            "FniEnumConsumer", [providerInput, coreProvider.Input]);
        var loadedShadeIndex = Enumerable.Range(0,
                loadedConsumer.EnumDefinitions.Length)
            .Single(index => loadedConsumer.EnumDefinitions[index].Name
                == "Demo.Shade");
        var loadedInvoke = loadedConsumer.Functions.Single(
            static function => function.Name == "Invoke");
        BoundCall? loadedEcho = null;
        _ = BoundTreeTraversal.VisitExpressions(loadedInvoke.Body,
            expression =>
            {
                if (expression is BoundCall {
                        ForeignInterfaceCall:
                            BoundForeignInterfaceCall foreignCall,
                    } call && foreignCall.Member.Name == "Echo")
                {
                    loadedEcho = call;
                }
            });
        Assert.That(loadedEcho, Is.Not.Null);
        Assert.That(loadedEcho!.Type,
            Is.EqualTo(BoundEnumTypes.Type(loadedShadeIndex)));
        var loadedValue = (BoundConstant)loadedEcho.Arguments.Single();
        Assert.That(loadedValue.Type,
            Is.EqualTo(BoundEnumTypes.Type(loadedShadeIndex)));
        Assert.That(loadedValue.RepresentationType,
            Is.EqualTo(PrimitiveType.Int8));
        Assert.That(NativeEmitter.EmitLibrary(loadedConsumer, ["Invoke"],
            NativeRuntimePolicy.FailFast), Is.Not.Empty);
        var shiftedProviderArtifact = CreateShiftedProviderArtifact();
        var shiftedProviderInput = new TrRmStandardArtifactInput(
            SchemaIdentity, shiftedProviderArtifact);
        var originalProviderTables
            = TrRmStructuralMetadataValidator
                .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                    providerArtifact,
                    TrRmArtifactSchema
                        .ForeignNativeInterfaceParameterSurfaceEnumClosureDraft));
        var shiftedProviderTables
            = TrRmStructuralMetadataValidator
                .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                    shiftedProviderArtifact,
                    TrRmArtifactSchema
                        .ForeignNativeInterfaceParameterSurfaceEnumClosureDraft));
        var originalShadeTypeDef = Enumerable.Range(1,
                originalProviderTables.TypeDefs.Count)
            .Single(row => originalProviderTables.TypeDefs.GetName(row)
                == "Shade");
        var shiftedShadeTypeDef = Enumerable.Range(1,
                shiftedProviderTables.TypeDefs.Count)
            .Single(row => shiftedProviderTables.TypeDefs.GetName(row)
                == "Shade");
        Assert.That(shiftedShadeTypeDef, Is.Not.EqualTo(originalShadeTypeDef));
        var shiftedLoadedConsumer = TrRmExecutableCompiler.LoadStandard(
            consumerInput, "FniEnumConsumer",
            [shiftedProviderInput, coreProvider.Input]);
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(
            shiftedLoadedConsumer, "FniEnumConsumerReemitted", SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reloadedConsumer = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity,
                reemittedArtifact),
            "FniEnumConsumerReemitted",
            [consumerInput, shiftedProviderInput, coreProvider.Input]);
        var reloadedInvoke = reloadedConsumer.Functions.Single(
            static function => function.Name == "Invoke");
        BoundCall? reloadedEcho = null;
        _ = BoundTreeTraversal.VisitExpressions(reloadedInvoke.Body,
            expression =>
            {
                if (expression is BoundCall {
                        ForeignInterfaceCall:
                            BoundForeignInterfaceCall foreignCall,
                    } call && foreignCall.Member.Name == "Echo")
                {
                    reloadedEcho = call;
                }
            });
        Assert.That(reloadedEcho, Is.Not.Null);
        var reloadedShadeIndex = Enumerable.Range(0,
                reloadedConsumer.EnumDefinitions.Length)
            .Single(index => reloadedConsumer.EnumDefinitions[index].Name
                == "Demo.Shade");
        var reloadedValue = (BoundConstant)reloadedEcho!.Arguments.Single();
        Assert.That(reloadedEcho.Type,
            Is.EqualTo(BoundEnumTypes.Type(reloadedShadeIndex)));
        Assert.That(reloadedValue.Type,
            Is.EqualTo(BoundEnumTypes.Type(reloadedShadeIndex)));
        Assert.That(reloadedValue.RepresentationType,
            Is.EqualTo(PrimitiveType.Int8));
        Assert.That(NativeEmitter.EmitLibrary(reloadedConsumer, ["Invoke"],
            NativeRuntimePolicy.FailFast), Is.Not.Empty);
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
                    public unsafe Shade Echo(Shade value);
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

    private static byte[] ReplaceRegisterBody(byte[] artifact,
        TrRmArtifactSchema schema, string standardSchemaIdentity, int bodyRow,
        byte[] replacementBody)
    {
        var index
            = TrRmArtifactIndex
                .ForForeignNativeInterfaceParameterSurfaceEnumClosureDraft(
                    artifact);
        if (!index.TryGetSection(0x0020, out var bodySection,
                out var bodyCount)
            || bodyRow <= 0 || bodyRow > bodyCount)
        {
            throw new InvalidOperationException(
                "The FNI enum test artifact has no selected Body row.");
        }

        var offsetBytes = checked((bodyCount + 1) * sizeof(uint));
        var bodyRows = new byte[bodyCount][];
        var bodyDataLength = 0;
        for (var row = 1; row <= bodyCount; row++)
        {
            var start = checked((int)BinaryPrimitives
                .ReadUInt32LittleEndian(bodySection[((row - 1)
                    * sizeof(uint))..]));
            var end = checked((int)BinaryPrimitives
                .ReadUInt32LittleEndian(bodySection[(row
                    * sizeof(uint))..]));
            var rowBytes = row == bodyRow
                ? (byte[])replacementBody.Clone()
                : bodySection.Slice(offsetBytes + start,
                    end - start).ToArray();
            bodyRows[row - 1] = rowBytes;
            bodyDataLength = checked(bodyDataLength + rowBytes.Length);
        }

        var newBodySection = new byte[checked(offsetBytes + bodyDataLength)];
        var nextOffset = 0;
        for (var rowIndex = 0; rowIndex < bodyRows.Length; rowIndex++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                newBodySection.AsSpan(rowIndex * sizeof(uint)),
                checked((uint)nextOffset));
            var rowBytes = bodyRows[rowIndex];
            rowBytes.CopyTo(newBodySection, offsetBytes + nextOffset);
            nextOffset = checked(nextOffset + rowBytes.Length);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(
            newBodySection.AsSpan(bodyCount * sizeof(uint)),
            checked((uint)nextOffset));
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < index.SectionCount; sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)recordCount),
                kind == 0x0020 ? newBodySection : section.ToArray()));
        }

        return TrRmArtifactWriter.Encode(sections, schema,
            standardSchemaIdentity: standardSchemaIdentity);
    }
}
