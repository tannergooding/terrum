// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Reflection.PortableExecutable;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class NativeCoreRegistrationTests
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EnumRoundTrip(int value);

    [Test]
    public void RegistrationRetainsSelectedEnumAndSchemaIdentities()
    {
        var registration = CreateRegistration();

        Assert.That(registration.IsValid(), Is.True);
        Assert.That(registration.CallingConventions.Members.Select(static member =>
            (member.Name, member.Value)), Is.EqualTo(
            [
                ("Unmanaged", 1),
                ("Cdecl", 2),
                ("Stdcall", 3),
                ("Vectorcall", 4),
            ]));
        Assert.That(registration.NativeImport.CallingConventionEnum,
            Is.EqualTo(registration.CallingConventions.Identity));
        Assert.That(registration.NativeCallable.CallingConventionEnum,
            Is.EqualTo(registration.CallingConventions.Identity));
        Assert.That(registration.NativeCallConv.CallingConventionEnum,
            Is.EqualTo(registration.CallingConventions.Identity));
    }

    [Test]
    public void RegistrationRetainsOptionalForeignNativeInterfaceSchema()
    {
        var standardRegistration = CreateRegistration();
        Assert.That(standardRegistration.ForeignNativeInterface, Is.Null);

        var registration = CreateRegistration(
            includeForeignNativeInterface: true);
        var schema = registration.ForeignNativeInterface
            ?? throw new AssertionException(
                "Expected the selected ForeignNativeInterface schema.");
        var argument = schema.Arguments.Single();
        Assert.That(registration.IsValid(), Is.True);
        Assert.That(schema.Role,
            Is.EqualTo(CoreNativeAttributeRole.ForeignNativeInterface));
        Assert.That(schema.Placement, Is.EqualTo("primary-declaration"));
        Assert.That(schema.Targets, Is.EquivalentTo(Contexts("interface")));
        Assert.That(argument.Name, Is.EqualTo("VTableByteOffset"));
        Assert.That(argument.Kind,
            Is.EqualTo(CoreNativeAttributeArgumentKind.UInt32Constant));
        Assert.That(argument.IsRequired, Is.True);
        Assert.That(argument.AllowsPositional, Is.False);
        Assert.That(argument.AllowsNamed, Is.True);
        Assert.That(argument.Contexts,
            Is.EquivalentTo(Contexts("interface")));
        Assert.That(registration.ModuleBindings.Declarations,
            Does.Contain(CoreProviderDeclarationSnapshot.From(schema)));

        var wrongRoleSchema = schema with {
            Role = CoreNativeAttributeRole.NativeExport,
        };
        var declarations = registration.ModuleBindings.Declarations
            .Select(declaration => declaration.Identity == schema.Identity
                ? CoreProviderDeclarationSnapshot.From(wrongRoleSchema)
                : declaration)
            .ToImmutableArray();
        var wrongRoleBindings = CoreProviderModuleBindings.Create(
            registration.UsingModuleIdentity,
            registration.ModuleBindings.Providers, declarations);
        _ = Assert.Throws<ArgumentException>(() =>
            NativeCoreRegistrationSet.Create(wrongRoleBindings,
                registration.CallingConventions, registration.ResolutionModes,
                registration.ResolutionPolicies, registration.NativeImport,
                registration.NativeCallable, registration.NativeCallConv,
                registration.NativeExport, wrongRoleSchema));
    }

    [Test]
    public void ForeignNativeInterfaceBindsOnlyTheRegisteredAlignedOffset()
    {
        const string source = """
            [Terrum.Interop.NativeCallable]
            [Terrum.Interop.ForeignNativeInterface(VTableByteOffset: uint32(16))]
            public interface IProbe {
                public unsafe int32 Read();
                public mut unsafe int32 Update();
            }
            """;
        var registration = CreateRegistration(
            includeForeignNativeInterface: true);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                NativeCoreRegistration = registration,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program!.Interfaces.Single()
            .ForeignNativeInterface!.VTableByteOffset, Is.EqualTo(16u));
        Assert.That(compilation.Program.Interfaces.Single().Members.Single()
            .NativeCallableConvention,
            Is.EqualTo(NativeCallingConvention.Unmanaged));

        var foreignDefinition = compilation.Program.Interfaces.Single();
        var foreignMember = foreignDefinition.Members.Single();
        var missingConvention = compilation.Program with {
            Interfaces = compilation.Program.Interfaces.SetItem(0,
                foreignDefinition with {
                    Members = foreignDefinition.Members.SetItem(0,
                        foreignMember with {
                            NativeCallableConvention = null,
                        }),
                }),
        };
        _ = Assert.Throws<ArgumentException>(() =>
            SemanticVerifier.VerifyLibrary(missingConvention));

        var missingDeclaredUnsafe = compilation.Program with {
            Interfaces = compilation.Program.Interfaces.SetItem(0,
                foreignDefinition with {
                    Members = foreignDefinition.Members.SetItem(0,
                        foreignMember with {
                            DeclaredRequiresUnsafeCaller = false,
                        }),
                }),
        };
        _ = Assert.Throws<ArgumentException>(() =>
            SemanticVerifier.VerifyLibrary(missingDeclaredUnsafe));

        var aliased = Compilation.CreateLibrary(SyntaxTree.Parse("""
            alias ForeignInterface = Terrum.Interop.ForeignNativeInterface;
            [Terrum.Interop.NativeCallable]
            [ForeignInterface(VTableByteOffset: uint32(8))]
            public interface IAliasedProbe {
                public unsafe int32 Read();
            }
            """), new CompilationOptions {
                NativeCoreRegistration = registration,
            });
        Assert.That(aliased.HasErrors, Is.False,
            string.Join(Environment.NewLine, aliased.Diagnostics));
        Assert.That(aliased.Program!.Interfaces.Single()
            .ForeignNativeInterface!.VTableByteOffset, Is.EqualTo(8u));

        var missingCallable = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IMissingCallable {
                public unsafe int32 Read();
            }
            """), new CompilationOptions {
                NativeCoreRegistration = registration,
            });
        Assert.That(missingCallable.HasErrors, Is.True,
            string.Join(Environment.NewLine, missingCallable.Diagnostics));
        Assert.That(missingCallable.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, missingCallable.Diagnostics));

        var missingUnsafe = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable]
            [Terrum.Interop.ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IMissingUnsafe {
                public int32 Read();
            }
            """), new CompilationOptions {
                NativeCoreRegistration = registration,
            });
        Assert.That(missingUnsafe.HasErrors, Is.True,
            string.Join(Environment.NewLine, missingUnsafe.Diagnostics));
        Assert.That(missingUnsafe.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, missingUnsafe.Diagnostics));

        var methodTarget = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public interface IMethodTarget {
                [Terrum.Interop.ForeignNativeInterface(
                    VTableByteOffset: uint32(0))]
                public unsafe int32 Read();
            }
            """), new CompilationOptions {
                NativeCoreRegistration = registration,
            });
        Assert.That(methodTarget.HasErrors, Is.True,
            string.Join(Environment.NewLine, methodTarget.Diagnostics));
        Assert.That(methodTarget.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, methodTarget.Diagnostics));

        var unregistered = Compilation.CreateLibrary(
            SyntaxTree.Parse(source));
        Assert.That(unregistered.HasErrors, Is.True,
            string.Join(Environment.NewLine, unregistered.Diagnostics));
        Assert.That(unregistered.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, unregistered.Diagnostics));

        var unselected = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                NativeCoreRegistration = CreateRegistration(),
            });
        Assert.That(unselected.HasErrors, Is.True,
            string.Join(Environment.NewLine, unselected.Diagnostics));
        Assert.That(unselected.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, unselected.Diagnostics));

        var misaligned = Compilation.CreateLibrary(SyntaxTree.Parse(source
            .Replace("uint32(16)", "uint32(4)", StringComparison.Ordinal)),
            new CompilationOptions {
                NativeCoreRegistration = registration,
            });
        Assert.That(misaligned.HasErrors, Is.True,
            string.Join(Environment.NewLine, misaligned.Diagnostics));
        Assert.That(misaligned.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, misaligned.Diagnostics));

        var positional = Compilation.CreateLibrary(SyntaxTree.Parse(source
            .Replace("VTableByteOffset: ", "", StringComparison.Ordinal)),
            new CompilationOptions {
                NativeCoreRegistration = registration,
            });
        Assert.That(positional.HasErrors, Is.True,
            string.Join(Environment.NewLine, positional.Diagnostics));
        Assert.That(positional.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, positional.Diagnostics));

        var missingArgument = Compilation.CreateLibrary(SyntaxTree.Parse(source
            .Replace("(VTableByteOffset: uint32(16))", "",
                StringComparison.Ordinal)),
            new CompilationOptions {
                NativeCoreRegistration = registration,
            });
        Assert.That(missingArgument.HasErrors, Is.True,
            string.Join(Environment.NewLine, missingArgument.Diagnostics));
        Assert.That(missingArgument.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, missingArgument.Diagnostics));

        var wrongType = Compilation.CreateLibrary(SyntaxTree.Parse(source
            .Replace("uint32(16)", "\"16\"", StringComparison.Ordinal)),
            new CompilationOptions {
                NativeCoreRegistration = registration,
            });
        Assert.That(wrongType.HasErrors, Is.True,
            string.Join(Environment.NewLine, wrongType.Diagnostics));
        Assert.That(wrongType.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, wrongType.Diagnostics));

        var wrongTarget = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public struct NotAnInterface { private int32 Value; }
            """), new CompilationOptions {
                NativeCoreRegistration = registration,
            });
        Assert.That(wrongTarget.HasErrors, Is.True,
            string.Join(Environment.NewLine, wrongTarget.Diagnostics));
    }

    [Test]
    public void StandardForeignNativeInterfaceProducerEmitsBaseMethodIdentity()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable]
            [Terrum.Interop.ForeignNativeInterface(VTableByteOffset: uint32(16))]
            public interface IProbe {
                public unsafe int32 Read();
            }
            """), new CompilationOptions {
                NativeCoreRegistration = CreateRegistration(
                    includeForeignNativeInterface: true),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var definition = program.Interfaces.Single();
        var typeExport = new TrRmSourceTypeExport("", "IProbe",
            "provider/IProbe") {
            GenericArity = 0,
            InterfaceMethodExports = [.. definition.Members.Select(member =>
                new TrRmSourceInterfaceMethodExport(
                    member.SourceInputOrdinal, member.SourceDeclarationSpan,
                    $"provider/IProbe.{member.Name}"))],
        };

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "provider.module",
            "trrm/1-draft-foreign-native-interface-contracts-1", 1,
            [typeExport]);
        var schema = TrRmArtifactSchema.ForeignNativeInterfaceDraft;
        var index = TrRmArtifactIndex.ForForeignNativeInterfaceDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(index);

        Assert.That(tables.Interfaces.Count, Is.EqualTo(1));
        Assert.That(tables.InterfaceMethods.Count, Is.EqualTo(2));
        Assert.That(tables.ForeignNativeInterfaces.Count, Is.EqualTo(1));
        Assert.That(tables.ForeignNativeInterfaces.GetOwnerInterfaceDef(1),
            Is.EqualTo(1u));
        Assert.That(tables.ForeignNativeInterfaces.GetVTableByteOffset(1),
            Is.EqualTo(16u));
        Assert.That(tables.Methods.GetBodyRow(1), Is.Zero);
        Assert.That(tables.Methods.GetBodyRow(2), Is.Zero);
        Assert.That(tables.Methods.GetFlags(1) & 3, Is.EqualTo(2u));
        Assert.That(tables.Methods.GetFlags(2) & 3, Is.EqualTo(2u));
        Assert.That(tables.Contracts.GetEffectsMask(1), Is.EqualTo(4u));
        Assert.That(tables.Contracts.GetNativeCallableConvention(1),
            Is.EqualTo((byte)1));
        var readReceiver = tables.Signatures.GetReceiverTypeSpec(
            tables.Methods.GetSignature(1));
        var updateReceiver = tables.Signatures.GetReceiverTypeSpec(
            tables.Methods.GetSignature(2));
        Assert.That(tables.TypeSpecs.GetTypeSpecBytes(readReceiver)[1],
            Is.Zero);
        Assert.That(tables.TypeSpecs.GetTypeSpecBytes(updateReceiver)[1],
            Is.EqualTo((byte)1));
        Assert.That(tables.Modules.GetRequirements(1).TargetRequirement,
            Is.EqualTo(TrRmModuleTableReader
                .WindowsX64NativeTargetRequirement));
        Assert.That(TrRmFeatureManifestValidator.Requires(tables.Features,
            tables.Modules, 1,
            TrRmFeatureTableReader.NativeInterfaceCallsFeatureId, schema),
            Is.True);
    }

    [Test]
    public void StandardForeignNativeInterfaceProducerEmitsOutputContracts()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-native-interface-parameter-surface-1";
        var (native, output) = NativeOutputCoreRegistrationTests
            .CreateCombinedRegistrations(includeForeignNativeInterface: true);
        var options = new CompilationOptions {
            NativeCoreRegistration = native,
            LayoutCoreRegistration =
                CreateLayoutRegistration(native),
            NativeOutputCoreRegistration = output,
        };
        const string providerSource = """
            [Terrum.Interop.NativeCallable(
                CallConv: Terrum.Interop.NativeCallingConvention.Cdecl)]
            [Terrum.Interop.ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                [param(value): Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonNegative)]
                public unsafe int32 TryRead(ptr<int32> source,
                    [Terrum.CodeAnalysis.DependsOn(this, nameof(source))]
                    [Terrum.CodeAnalysis.ForwardsBacking(
                        this, nameof(source))]
                    write ptr<int32> value);

                [return: Terrum.CodeAnalysis.DependsOn(nameof(source))]
                [return: Terrum.CodeAnalysis.ForwardsBacking(nameof(source))]
                public unsafe ptr<int32> Get(ptr<int32> source);
                public unsafe int32 Count(int32 count = 1);
            }
            """;
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var members = program.Interfaces.Single().Members;
        var typeExport = new TrRmSourceTypeExport("", "IProbe",
            "provider/IProbe") {
            GenericArity = 0,
            InterfaceMethodExports = [.. members.Select(member =>
                new TrRmSourceInterfaceMethodExport(
                    member.SourceInputOrdinal, member.SourceDeclarationSpan,
                    $"provider/IProbe.{member.Name}"))],
        };

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "provider.module",
            "trrm/1-draft-foreign-native-interface-parameter-surface-1", 1,
            [typeExport]);
        var schema
            = TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft;
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(artifact,
                schema));

        Assert.That(tables.Contracts.GetNativeOutputs(1).Single(),
            Is.EqualTo(new TrRmNativeOutputContract(1,
                PrimitiveType.Int32, 3, 1)));
        Assert.That(tables.Contracts.GetOutputStateContracts(1)
            .Select(static state =>
                (state.ParameterOrdinal, state.State)),
            Is.EqualTo(new[] { (1, (byte)1), (1, (byte)0) }));
        Assert.That(tables.Contracts.GetDependencyContracts(1),
            Is.EquivalentTo([
                new TrRmDependencyContract(2, 1, 2, 1, 1, 0),
                new TrRmDependencyContract(2, 2, 2, 1, 1, 0),
            ]));
        Assert.That(tables.Contracts.GetDependencyContracts(2).Single(),
            Is.EqualTo(new TrRmDependencyContract(1, 1, 2, 1, 0, 0)));
        Assert.That(tables.Contracts.GetPredicateGraphContracts(1)
            .Select(static predicate =>
                (predicate.PhaseTag, predicate.PhaseLocalOrdinal)),
            Is.EqualTo(new[] { ((byte)3, 0u), ((byte)3, 1u) }));
        Assert.That(tables.Contracts.GetNativeOutputs(2), Is.Empty);
        Assert.That(tables.PredicateGraphs.Count, Is.EqualTo(2));
        Assert.That(tables.PredicateReadFootprints.Count, Is.EqualTo(2));
        Assert.That(tables.PredicateReadRoots.Count, Is.EqualTo(2));
        Assert.That(tables.PredicateNodes.Count, Is.EqualTo(2));
        Assert.That(tables.PredicateNodeOperands.Count, Is.EqualTo(2));
        Assert.That(TrRmFeatureManifestValidator.Requires(tables.Features,
            tables.Modules, 1,
            TrRmFeatureTableReader.NativeOutputCellsCandidateFeatureId,
            schema), Is.True);
        Assert.That(TrRmFeatureManifestValidator.Requires(tables.Features,
            tables.Modules, 1,
            TrRmFeatureTableReader.PublishedPredicatesFeatureId, schema),
            Is.True);

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(
                "trrm/1-draft-foreign-native-interface-parameter-surface-1",
                artifact), "provider.module", []);
        var importedInterface = reference.Interfaces.Single();
        Assert.That(importedInterface.ForeignNativeInterface
            ?.VTableByteOffset, Is.Zero);
        Assert.That(importedInterface.ModuleImport?.ExportKey,
            Is.EqualTo("provider/IProbe"));
        Assert.That(importedInterface.Members[0].NativeOutputContracts
            .Single().FormalOrdinal, Is.EqualTo(1));
        Assert.That(importedInterface.Members[1].Dependencies.Select(
                static dependency => dependency.Relation),
            Is.EquivalentTo([
                BoundDependencyRelation.DependsOn,
                BoundDependencyRelation.ForwardsBacking,
            ]));

        var caller = Compilation.CreateLibrary(SyntaxTree.Parse("""
            int32 Invoke(ptr<void> receiver) {
                unsafe {
                    init int32 value;
                    int32 status = IProbe.TryRead(receiver, &init value);
                    if (status >= 0) { return value; }
                }
                return 0;
            }
            """), reference);
        Assert.That(caller.HasErrors, Is.False,
            string.Join(Environment.NewLine, caller.Diagnostics));

        var changedProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource
                .Replace("source", "input", StringComparison.Ordinal)
                .Replace("int32 Count(int32 count = 1)",
                    "int32 Count(int32 limit = 2)",
                    StringComparison.Ordinal)), options);
        Assert.That(changedProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                changedProviderCompilation.Diagnostics));
        var changedProviderProgram = changedProviderCompilation.Program
            ?? throw new AssertionException(
                "Expected a bound replacement provider.");
        var changedProviderMembers
            = changedProviderProgram.Interfaces.Single().Members;
        var changedProviderExport = new TrRmSourceTypeExport("", "IProbe",
            "provider/IProbe") {
            GenericArity = 0,
            InterfaceMethodExports = [.. changedProviderMembers.Select(member =>
                new TrRmSourceInterfaceMethodExport(
                    member.SourceInputOrdinal, member.SourceDeclarationSpan,
                    $"provider/IProbe.{member.Name}"))],
        };
        var changedProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            changedProviderProgram, "provider.module",
            "trrm/1-draft-foreign-native-interface-parameter-surface-1", 1,
            [changedProviderExport]);
        var outputChangedProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource.Replace(
                "NativeOutputCondition.NonNegative",
                "NativeOutputCondition.NonZero",
                StringComparison.Ordinal)), options);
        Assert.That(outputChangedProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                outputChangedProviderCompilation.Diagnostics));
        var outputChangedProviderProgram
            = outputChangedProviderCompilation.Program
                ?? throw new AssertionException(
                    "Expected a provider with a changed NativeOutput condition.");
        var outputChangedProviderExport = new TrRmSourceTypeExport("", "IProbe",
            "provider/IProbe") {
            GenericArity = 0,
            InterfaceMethodExports = [.. outputChangedProviderProgram
                .Interfaces.Single().Members.Select(member =>
                    new TrRmSourceInterfaceMethodExport(
                        member.SourceInputOrdinal,
                        member.SourceDeclarationSpan,
                        $"provider/IProbe.{member.Name}"))],
        };
        var outputChangedProviderArtifact
            = TrRmExecutableCompiler.EmitStandard(
                outputChangedProviderProgram, "provider.module",
                "trrm/1-draft-foreign-native-interface-parameter-surface-1",
                1, [outputChangedProviderExport]);
        var incompatibleProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource.Replace(
                "int32 Count(int32 count = 1)",
                "int32 Count(int64 count = 1)",
                StringComparison.Ordinal)), options);
        Assert.That(incompatibleProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                incompatibleProviderCompilation.Diagnostics));
        var incompatibleProviderProgram
            = incompatibleProviderCompilation.Program
                ?? throw new AssertionException(
                    "Expected an incompatible replacement provider.");
        var incompatibleProviderMember
            = incompatibleProviderProgram.Interfaces.Single().Members
                .Single(static member => member.Name == "Count");
        var incompatibleProviderExport = new TrRmSourceTypeExport("", "IProbe",
            "provider/IProbe") {
            GenericArity = 0,
            InterfaceMethodExports = [.. incompatibleProviderProgram
                .Interfaces.Single().Members.Select(member =>
                    new TrRmSourceInterfaceMethodExport(
                        member.SourceInputOrdinal,
                        member.SourceDeclarationSpan,
                        $"provider/IProbe.{member.Name}"))],
        };
        var incompatibleProviderArtifact
            = TrRmExecutableCompiler.EmitStandard(
                incompatibleProviderProgram, "provider.module",
                "trrm/1-draft-foreign-native-interface-parameter-surface-1",
                1, [incompatibleProviderExport]);
        Assert.That(incompatibleProviderMember.Parameters.Single().Type
            .PrimitiveType, Is.EqualTo(PrimitiveType.Int64));

        var importOnlyCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(""), reference);
        Assert.That(importOnlyCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                importOnlyCompilation.Diagnostics));
        var importerArtifact = TrRmExecutableCompiler.EmitStandard(
            importOnlyCompilation.Program
                ?? throw new AssertionException(
                    "Expected an imported FNI program."),
            "consumer.module",
            "trrm/1-draft-foreign-native-interface-parameter-surface-1", 1,
            []);
        var importerTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                importerArtifact, schema));
        Assert.That(importerTables.ForeignNativeInterfaces.Count,
            Is.EqualTo(1));
        Assert.That(importerTables.Imports.Count, Is.EqualTo(4));

        var reboundReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(
                "trrm/1-draft-foreign-native-interface-parameter-surface-1",
                importerArtifact), "consumer.module", [
                new TrRmStandardArtifactInput(
                    "trrm/1-draft-foreign-native-interface-parameter-surface-1",
                    changedProviderArtifact),
            ]);
        var reboundInterface = reboundReference.Interfaces.Single();
        Assert.That(reboundInterface.ModuleImport?.ProducerIdentity,
            Is.EqualTo("provider.module"));
        Assert.That(reboundInterface.Members[0].NativeOutputContracts
            .Single().FormalOrdinal, Is.EqualTo(1));
        var countMember = reboundInterface.Members.Single(static member =>
            member.Name == "Count");
        Assert.That(countMember.Parameters.Single().Name,
            Is.EqualTo("limit"));
        Assert.That(countMember.Parameters.Single().DefaultValue,
            Is.Not.Null);
        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(""), reboundReference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var forwardedArtifact = TrRmExecutableCompiler.EmitStandard(
            sourceFreeCompilation.Program
                ?? throw new AssertionException(
                    "Expected a source-free FNI forwarder."),
            "forwarder.module", schemaIdentity, 1, []);
        var forwardedReference = TrRmExecutableCompiler
            .CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    forwardedArtifact), "forwarder.module", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        changedProviderArtifact),
                ]);
        var forwardedInterface = forwardedReference.Interfaces.Single();
        Assert.That(forwardedInterface.ModuleImport?.ProducerIdentity,
            Is.EqualTo("provider.module"));
        Assert.That(forwardedInterface.Members[0].NativeOutputContracts
            .Single().FormalOrdinal, Is.EqualTo(1));
        Assert.That(forwardedInterface.Members[1].Dependencies.Select(
                static dependency => dependency.Relation),
            Is.EquivalentTo([
                BoundDependencyRelation.DependsOn,
                BoundDependencyRelation.ForwardsBacking,
            ]));
        var forwardedCountMember = forwardedInterface.Members.Single(
            static member => member.Name == "Count");
        Assert.That(forwardedCountMember.Parameters.Single().Name,
            Is.EqualTo("limit"));
        Assert.That(forwardedCountMember.Parameters.Single().DefaultValue,
            Is.Not.Null);
        Assert.That(forwardedCountMember.Parameters.Single().DefaultValue
            ?.Value, Is.EqualTo(2L));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(
                    "trrm/1-draft-foreign-native-interface-parameter-surface-1",
                    importerArtifact), "consumer.module", [
                new TrRmStandardArtifactInput(
                    "trrm/1-draft-foreign-native-interface-parameter-surface-1",
                    incompatibleProviderArtifact),
            ]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(
                    "trrm/1-draft-foreign-native-interface-parameter-surface-1",
                    importerArtifact), "consumer.module", [
                new TrRmStandardArtifactInput(
                    "trrm/1-draft-foreign-native-interface-parameter-surface-1",
                    outputChangedProviderArtifact),
            ]));
    }

    [Test]
    public void StandardForeignNativeInterfaceFunctionPointerAnchorsRoundTrip()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-native-interface-parameter-surface-1";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                public unsafe int32 Apply(
                    [NativeCallConv(NativeCallingConvention.Cdecl)]
                    fnptr<int32, int32> callback);
            }
            """), new CompilationOptions {
            NativeCoreRegistration = CreateRegistration(
                includeForeignNativeInterface: true),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var definition = program.Interfaces.Single();
        var member = definition.Members.Single();
        var typeExport = new TrRmSourceTypeExport("", "IProbe",
            "provider/IProbe") {
            GenericArity = 0,
            InterfaceMethodExports = [
                new TrRmSourceInterfaceMethodExport(
                    member.SourceInputOrdinal, member.SourceDeclarationSpan,
                    "provider/IProbe.Apply"),
            ],
        };
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "provider.module", schemaIdentity, 1, [typeExport]);
        var schema
            = TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft;
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(artifact,
                schema));
        var anchorTypeSpecRows = new List<int>();
        for (var typeSpecRow = 1;
            typeSpecRow <= tables.TypeSpecs.Count; typeSpecRow++)
        {
            if (tables.NativeFunctionPointerAnchors
                .HasAnchorForTypeSpec(typeSpecRow))
            {
                anchorTypeSpecRows.Add(typeSpecRow);
            }
        }
        Assert.That(anchorTypeSpecRows, Has.Count.EqualTo(1));
        Assert.That(tables.NativeFunctionPointerAnchors.GetCallingConvention(
            anchorTypeSpecRows[0]), Is.EqualTo((byte)
                NativeCallingConvention.Cdecl));

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            "provider.module", []);
        var signature = reference.NativeFunctionPointerSignatures.Single();
        Assert.That(signature.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Cdecl));
        Assert.That(signature.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(signature.ParameterTypes,
            Is.EqualTo(new[] { PrimitiveType.Int32 }));
        var callbackType = reference.Interfaces.Single().Members.Single()
            .Parameters.Single().Type.PrimitiveType;
        Assert.That(NativeFunctionPointerTypes.IsType(callbackType), Is.True);

        var caller = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;
            int32 Invoke(ptr<void> receiver,
                [NativeCallConv(NativeCallingConvention.Cdecl)]
                fnptr<int32, int32> callback) {
                unsafe { return IProbe.Apply(receiver, callback); }
            }
            """), reference);
        Assert.That(caller.HasErrors, Is.False,
            string.Join(Environment.NewLine, caller.Diagnostics));
    }

    [Test]
    public void StandardForeignNativeInterfaceLoadsAggregatePointeeLayout()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-native-interface-parameter-surface-1";
        var nativeRegistration = CreateRegistration(
            includeForeignNativeInterface: true);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Point {
                public int32 X;
                public int32 Y;
            }
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                public unsafe int32 Read(ptr<Point> point);
            }
            """), new CompilationOptions {
            NativeCoreRegistration = nativeRegistration,
            LayoutCoreRegistration =
                CreateLayoutRegistration(nativeRegistration),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var member = program.Interfaces.Single().Members.Single();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "provider.module", schemaIdentity, 1, [
                new TrRmSourceTypeExport("", "Point", "provider/Point") {
                    FieldExports = [
                        new TrRmSourceFieldExport("X", "provider/Point.X"),
                        new TrRmSourceFieldExport("Y", "provider/Point.Y"),
                    ],
                },
                new TrRmSourceTypeExport("", "IProbe",
                    "provider/IProbe") {
                    GenericArity = 0,
                    InterfaceMethodExports = [
                        new TrRmSourceInterfaceMethodExport(
                            member.SourceInputOrdinal,
                            member.SourceDeclarationSpan,
                            "provider/IProbe.Read"),
                    ],
                },
            ]);
        var schema
            = TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft;
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(artifact,
                schema));
        Assert.That(tables.TypeDefs.Count, Is.EqualTo(2));
        Assert.That(tables.Layouts.HasBlittableVariant(1), Is.True);
        var signatureRow = tables.Methods.GetSignature(1);
        var (firstParameter, parameterCount)
            = tables.Signatures.GetParamRange(signatureRow);
        Assert.That(parameterCount, Is.EqualTo(1));
        var pointerTypeSpec = tables.Parameters.GetTypeSpec(firstParameter);
        Assert.That(tables.TypeSpecs.TryGetNativePointerPointeeTypeSpecRef(
            pointerTypeSpec, out var pointeeTypeSpec), Is.True);
        var pointee = tables.TypeSpecs.GetTypeSpecRef(pointeeTypeSpec);
        Assert.That(pointee.IsRow, Is.True);
        Assert.That(pointee.Form, Is.EqualTo((byte)0x02));
        Assert.That(tables.TypeSpecs.GetNominalTypeDefRow(pointeeTypeSpec,
            tables.TypeDefs, tables.Imports), Is.EqualTo(1));

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            "provider.module", []);
        Assert.That(reference.Structures, Has.Length.EqualTo(1));
        Assert.That(reference.Structures.Single().StandardTypeOrigin
            ?.ExportKeys.ToArray(), Is.EqualTo(new[] { "provider/Point" }));
        Assert.That(reference.Structures.Single().Fields.Select(
                static field => field.Name),
            Is.EqualTo(new[] { "X", "Y" }));
        Assert.That(reference.Structures.Single().Fields.Select(
                static field => field.StandardFieldOrigin?.ExportKeys.Single()),
            Is.EqualTo(new[] { "provider/Point.X", "provider/Point.Y" }));
        var caller = Compilation.CreateLibrary(SyntaxTree.Parse("""
            int32 Invoke(ptr<void> receiver, ptr<Point> point) {
                unsafe { return IProbe.Read(receiver, point); }
            }
            """), reference);
        Assert.That(caller.HasErrors, Is.False,
            string.Join(Environment.NewLine, caller.Diagnostics));
    }

    [Test]
    public void StandardForeignNativeInterfaceBindsImportedAggregatePointee()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-native-interface-parameter-surface-1";
        const string providerSource = """
            using Terrum.Interop;
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Coordinates {
                public int32 X;
                public int32 Y;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Point {
                public Coordinates Position;
            }
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                public unsafe int32 Read(ptr<Point> point);
            }
            """;
        var registration = CreateRegistration(
            includeForeignNativeInterface: true);
        var options = new CompilationOptions {
            NativeCoreRegistration = registration,
            LayoutCoreRegistration = CreateLayoutRegistration(registration),
        };
        byte[] EmitProvider(string source)
        {
            var providerCompilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(source), options);
            Assert.That(providerCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    providerCompilation.Diagnostics));
            var providerProgram = providerCompilation.Program
                ?? throw new AssertionException(
                    "Expected a bound aggregate provider.");
            var providerMember = providerProgram.Interfaces.Single()
                .Members.Single();
            return TrRmExecutableCompiler.EmitStandard(providerProgram,
                "provider.module", schemaIdentity, 1, [
                    new TrRmSourceTypeExport("", "Coordinates",
                        "provider/Coordinates") {
                        FieldExports = [
                            new TrRmSourceFieldExport("X",
                                "provider/Coordinates.X"),
                            new TrRmSourceFieldExport("Y",
                                "provider/Coordinates.Y"),
                        ],
                    },
                    new TrRmSourceTypeExport("", "Point",
                        "provider/Point") {
                        FieldExports = [
                            new TrRmSourceFieldExport("Position",
                                "provider/Point.Position"),
                        ],
                    },
                    new TrRmSourceTypeExport("", "IProbe",
                        "provider/IProbe") {
                        GenericArity = 0,
                        InterfaceMethodExports = [
                            new TrRmSourceInterfaceMethodExport(
                                providerMember.SourceInputOrdinal,
                                providerMember.SourceDeclarationSpan,
                                "provider/IProbe.Read"),
                        ],
                    },
                ]);
        }

        var providerArtifact = EmitProvider(providerSource);
        const string relocatedProviderSource = """
            using Terrum.Interop;
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Point {
                public Coordinates Position;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Coordinates {
                public int32 X;
                public int32 Y;
            }
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                public unsafe int32 Read(ptr<Point> point);
            }
            """;
        var relocatedProviderArtifact
            = EmitProvider(relocatedProviderSource);
        var providerTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                providerArtifact,
                TrRmArtifactSchema
                    .ForeignNativeInterfaceParameterSurfaceDraft));
        var relocatedProviderTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                relocatedProviderArtifact,
                TrRmArtifactSchema
                    .ForeignNativeInterfaceParameterSurfaceDraft));
        Assert.That(providerTables.Exports.TryGetDefinitionForKey(1,
                "provider/Point", out var originalPointDefinition),
            Is.True);
        Assert.That(relocatedProviderTables.Exports.TryGetDefinitionForKey(1,
                "provider/Point", out var relocatedPointDefinition),
            Is.True);
        Assert.That(relocatedPointDefinition, Is.Not.EqualTo(
            originalPointDefinition));
        var providerReference = TrRmExecutableCompiler
            .CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    providerArtifact), "provider.module", []);
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                int32 Invoke(ptr<void> receiver, ptr<Point> point) {
                    unsafe { return IProbe.Read(receiver, point); }
                }
                """), providerReference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerCompilation.Program
                ?? throw new AssertionException("Expected a bound consumer."),
            "consumer.module", schemaIdentity, 1, []);
        var consumerTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact,
                TrRmArtifactSchema
                    .ForeignNativeInterfaceParameterSurfaceDraft));
        var structureImportRow = Enumerable.Range(1,
                consumerTables.Imports.Count)
            .Single(importRow => {
                var definitionId = consumerTables.Imports
                    .GetExpectedDefinitionForImportRow(importRow);
                return (definitionId >> 24) == 0x13
                    && consumerTables.TypeDefs.GetKind(checked((int)(
                        definitionId & 0x00FF_FFFF))) == 1
                    && consumerTables.Imports.GetBindingForImportRow(
                        importRow).ExportKey == "provider/Point";
            });
        Assert.That(consumerTables.Imports.GetBindingForImportRow(
                structureImportRow).ExportKey,
            Is.EqualTo("provider/Point"));
        Assert.That(Enumerable.Range(1, consumerTables.Imports.Count)
            .Where(importRow => (consumerTables.Imports
                .GetExpectedDefinitionForImportRow(importRow) >> 24)
                    == 0x13
                && consumerTables.TypeDefs.GetKind(checked((int)(
                    consumerTables.Imports.GetExpectedDefinitionForImportRow(
                        importRow) & 0x00FF_FFFF))) == 1)
            .Select(importRow => consumerTables.Imports
                .GetBindingForImportRow(importRow).ExportKey)
            .OrderBy(static exportKey => exportKey, StringComparer.Ordinal),
            Is.EqualTo(new[] { "provider/Coordinates", "provider/Point" }));

        var reboundReference = TrRmExecutableCompiler
            .CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    consumerArtifact), "consumer.module", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        relocatedProviderArtifact),
                ]);
        var reboundPoint = reboundReference.Structures.Single(
            static structure => structure.Name == "Point"
                && structure.StandardTypeOrigin?.IsLookupRoot == true);
        Assert.That(reboundPoint.StandardTypeOrigin?.Provider.ModuleIdentity,
            Is.EqualTo("provider.module"));
        Assert.That(reboundReference.Structures.Count(
            static structure => structure.StandardLayoutOnly),
            Is.EqualTo(2));
        var reboundConsumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                int32 Invoke(ptr<void> receiver, ptr<Point> point) {
                    unsafe { return IProbe.Read(receiver, point); }
                }
                """), reboundReference);
        Assert.That(reboundConsumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, reboundConsumer.Diagnostics));
        var reemissionCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(""), reboundReference);
        Assert.That(reemissionCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reemissionCompilation.Diagnostics));
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(
            reemissionCompilation.Program
                ?? throw new AssertionException(
                    "Expected a source-free FNI consumer."),
            "forwarder.module", schemaIdentity, 1, []);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                reemittedArtifact,
                TrRmArtifactSchema
                    .ForeignNativeInterfaceParameterSurfaceDraft));
        var reemittedStructureImportRow = Enumerable.Range(1,
                reemittedTables.Imports.Count)
            .Single(importRow => {
                var definitionId = reemittedTables.Imports
                    .GetExpectedDefinitionForImportRow(importRow);
                return (definitionId >> 24) == 0x13
                    && reemittedTables.TypeDefs.GetKind(checked((int)(
                        definitionId & 0x00FF_FFFF))) == 1
                    && reemittedTables.Imports.GetBindingForImportRow(
                        importRow).ExportKey == "provider/Point";
            });
        Assert.That(reemittedTables.Imports.GetBindingForImportRow(
                reemittedStructureImportRow).ExportKey,
            Is.EqualTo("provider/Point"));
        Assert.That(Enumerable.Range(1, reemittedTables.Imports.Count)
            .Where(importRow => (reemittedTables.Imports
                .GetExpectedDefinitionForImportRow(importRow) >> 24)
                    == 0x13
                && reemittedTables.TypeDefs.GetKind(checked((int)(
                    reemittedTables.Imports.GetExpectedDefinitionForImportRow(
                        importRow) & 0x00FF_FFFF))) == 1)
            .Select(importRow => reemittedTables.Imports
                .GetBindingForImportRow(importRow).ExportKey)
            .OrderBy(static exportKey => exportKey, StringComparer.Ordinal),
            Is.EqualTo(new[] { "provider/Coordinates", "provider/Point" }));
        var reloadedReference = TrRmExecutableCompiler
            .CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    reemittedArtifact), "forwarder.module", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        relocatedProviderArtifact),
                ]);
        Assert.That(reloadedReference.Interfaces, Has.Length.EqualTo(1));
        Assert.That(reloadedReference.Structures.Any(
            static structure => structure.Name == "Point"
                && structure.StandardTypeOrigin?.IsLookupRoot == true),
            Is.True);
        var reloadedConsumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                int32 Invoke(ptr<void> receiver, ptr<Point> point) {
                    unsafe { return IProbe.Read(receiver, point); }
                }
                """), reloadedReference);
        Assert.That(reloadedConsumer.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reloadedConsumer.Diagnostics));

        var incompatibleArtifact = EmitProvider(
            relocatedProviderSource.Replace("public int32 X;",
                "public int64 X;", StringComparison.Ordinal));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    consumerArtifact), "consumer.module", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleArtifact),
                ]));
    }

    [Test]
    public void StandardForeignNativeInterfaceParameterProducerRetainsDefaultSurface()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable]
            [Terrum.Interop.ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                public unsafe int32 Read(int32 value, int32 extra = 1);
            }
            """), new CompilationOptions {
                NativeCoreRegistration = CreateRegistration(
                    includeForeignNativeInterface: true),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var definition = program.Interfaces.Single();
        var member = definition.Members.Single();
        var typeExport = new TrRmSourceTypeExport("", "IProbe",
            "provider/IProbe") {
            GenericArity = 0,
            InterfaceMethodExports = [
                new TrRmSourceInterfaceMethodExport(
                    member.SourceInputOrdinal, member.SourceDeclarationSpan,
                    "provider/IProbe.Read"),
            ],
        };
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, "provider.module",
                "trrm/1-draft-foreign-native-interface-contracts-1", 1,
                [typeExport]));

        var schemaIdentity
            = "trrm/1-draft-foreign-native-interface-parameter-surface-1";
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "provider.module", schemaIdentity, 1, [typeExport]);
        var schema
            = TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft;
        var index = TrRmArtifactIndex
            .ForForeignNativeInterfaceParameterSurfaceDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(index);

        Assert.That(index.TryGetSection(0x0066, out var surfaceBytes,
            out var surfaceCount), Is.True);
        Assert.That(surfaceCount, Is.EqualTo(2));
        Assert.That(surfaceBytes.Length, Is.EqualTo(32));
        Assert.That(tables.MethodParameterLabels.TryGetNameStringRow(1, 0,
            out var valueNameRow), Is.True);
        Assert.That(tables.Strings.DecodeName(valueNameRow),
            Is.EqualTo("value"));
        Assert.That(tables.MethodParameterLabels.TryGetNameStringRow(1, 1,
            out var extraNameRow), Is.True);
        Assert.That(tables.Strings.DecodeName(extraNameRow),
            Is.EqualTo("extra"));
        Assert.That(tables.MethodParameterLabels
            .TryGetDefaultConstantSourceRow(1, 0, out var requiredDefault),
            Is.True);
        Assert.That(requiredDefault, Is.Zero);
        Assert.That(tables.MethodParameterLabels
            .TryGetDefaultConstantSourceRow(1, 1, out var optionalDefault),
            Is.True);
        Assert.That(optionalDefault, Is.EqualTo(1u));
        var frozenValueNode = checked((int)tables.ConstantSources
            .GetPayloadReference(checked((int)optionalDefault)));
        Assert.That(tables.FrozenValueNodes.GetScalarPayload(frozenValueNode,
            tables.Blobs).ToArray(), Is.EqualTo(new byte[] { 1, 0, 0, 0 }));
        foreach (var feature in new uint[] { 1, 2, 5, 11, 26 })
        {
            Assert.That(TrRmFeatureManifestValidator.Requires(
                tables.Features, tables.Modules, 1, feature, schema), Is.True,
                $"Feature {feature}");
        }
        Assert.That(TrRmFeatureManifestValidator.Requires(tables.Features,
            tables.Modules, 1,
            TrRmFeatureTableReader.NominalUnionMetadataFeatureId, schema),
            Is.False);
    }

    [Test]
    public void ForeignNativeInterfaceProjectsMutableReceiverAsWritablePointer()
    {
        var registration = CreateRegistration(
            includeForeignNativeInterface: true);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable]
            [Terrum.Interop.ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                public mut unsafe int32 Update();
            }
            """), new CompilationOptions {
                NativeCoreRegistration = registration,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var member = program.Interfaces.Single().Members.Single();
        Assert.That(member.RequiresMutableReceiver, Is.True);
        Assert.That(program.NativePointerTypes.Any(static pointer =>
            pointer.PointeeType == PrimitiveType.Void
            && pointer.IsWritable), Is.True);
        SemanticVerifier.VerifyLibrary(program);

        var foreignDefinition = program.Interfaces.Single();
        var invalidSignature = foreignDefinition with {
            Members = foreignDefinition.Members.SetItem(0,
                member with {
                    NativeCallableConvention =
                        NativeCallingConvention.Vectorcall,
                }),
        };
        _ = Assert.Throws<ArgumentException>(() =>
            new CompilationReference(program.Structures, program.Functions,
                BoundRuntimePolicy.FailFast,
                nativeFunctionPointerSignatures:
                    program.NativeFunctionPointerSignatures,
                nativePointerTypes: program.NativePointerTypes,
                nativeCoreRegistration: registration,
                interfaces: program.Interfaces.SetItem(0, invalidSignature)));
    }

    [Test]
    public void ForeignNativeInterfaceCallsKeepExactOwnerAndMapLabelsAndDefaults()
    {
        var registration = CreateRegistration(
            includeForeignNativeInterface: true);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable]
            [Terrum.Interop.ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                public unsafe int32 Read(int32 value, int32 extra = 1);
                public unsafe int32 Read(int64 value);
            }

            [Terrum.Interop.NativeCallable]
            [Terrum.Interop.ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IOther {
                public unsafe int32 Read(int32 value);
            }

            struct Counter {
                public mut int32 Value;
                public Counter() { Value = 0; }
                public mut int32 Next() {
                    Value = Value + 1;
                    return Value;
                }
            }

            int32 Invoke(ptr<void> receiver) {
                mut Counter counter = new Counter();
                unsafe { return IProbe.Read(receiver, value: counter.Next()); }
            }
            """), new CompilationOptions {
                NativeCoreRegistration = registration,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var invoke = program.Functions.Single(static function =>
            function.Name == "Invoke");
        BoundCall? foreignCall = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(invoke.Body,
            expression =>
            {
                if (expression is BoundCall call
                    && call.ForeignInterfaceCall is not null)
                {
                    foreignCall = call;
                }
            }), Is.True);
        Assert.That(foreignCall, Is.Not.Null);
        Assert.That(foreignCall!.Function,
            Is.EqualTo(BoundCall.ForeignInterfaceFunction));
        Assert.That(foreignCall.ForeignInterfaceCall!.Member
            .DeclaringInterface.NominalName, Is.EqualTo("IProbe"));
        Assert.That(foreignCall.ForeignInterfaceCall.Member.MemberOrdinal,
            Is.EqualTo(0));
        Assert.That(foreignCall.ForeignInterfaceCall.Receiver.Type,
            Is.EqualTo(PrimitiveType.NativeRawPointer));
        Assert.That(foreignCall.Arguments.Select(static argument =>
            argument.Type), Is.EqualTo(
            [PrimitiveType.Int32, PrimitiveType.Int32]));
        Assert.That(foreignCall.ArgumentParameterIndices,
            Is.EqualTo([0, 1]));
        Assert.That(foreignCall.Arguments[1],
            Is.TypeOf<BoundConstant>());
        Assert.That(foreignCall.NestedExpressions[0],
            Is.SameAs(foreignCall.ForeignInterfaceCall.Receiver));
        Assert.That(foreignCall.Arguments[0],
            Is.TypeOf<BoundCall>());
        var laterArgumentCall = (BoundCall)foreignCall.Arguments[0];
        Assert.That(program.Functions[laterArgumentCall.Function]
            .DeclaringStruct, Is.EqualTo("Counter"));
        Assert.That(foreignCall.NestedExpressions[1],
            Is.SameAs(laterArgumentCall));
        Assert.That(foreignCall.NestedExpressions.Skip(1),
            Is.EqualTo(foreignCall.Arguments));
        SemanticVerifier.VerifyLibrary(program);

        var invalidFunction = BoundTreeTraversal.RewriteCalls(invoke,
            call => call.ForeignInterfaceCall is null
                ? call : call with { ArgumentParameterIndices = [1, 2] });
        var invokeIndex = program.Functions.IndexOf(invoke);
        var invalidProgram = program with {
            Functions = program.Functions.SetItem(invokeIndex,
                invalidFunction),
        };
        _ = Assert.Throws<ArgumentException>(() =>
            SemanticVerifier.VerifyLibrary(invalidProgram));
    }

    [Test]
    public void ForeignNativeInterfaceCallsDoNotRankContextualLiteralValues()
    {
        var registration = CreateRegistration(
            includeForeignNativeInterface: true);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable]
            [Terrum.Interop.ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                public unsafe int32 Read(int8 value);
                public unsafe int32 Read(int64 value);
            }

            int32 Invoke(ptr<void> receiver) {
                unsafe { return IProbe.Read(receiver, 300); }
            }
            """), new CompilationOptions {
                NativeCoreRegistration = registration,
            });

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3011"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void ForeignNativeInterfaceCallsRejectCrossedPointerAccessQuality()
    {
        var registration = CreateRegistration(
            includeForeignNativeInterface: true);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable]
            [Terrum.Interop.ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                public unsafe int32 Read(ptr<int32> first, write ptr<int32> second);
                public unsafe int32 Read(write ptr<int32> first, ptr<int32> second);
            }

            int32 Invoke(ptr<void> receiver, write ptr<int32> first,
                write ptr<int32> second) {
                unsafe { return IProbe.Read(receiver, first, second); }
            }
            """), new CompilationOptions {
                NativeCoreRegistration = registration,
            });

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3011"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void ForeignNativeInterfaceCallsPreferExactReceiverAccess()
    {
        var registration = CreateRegistration(
            includeForeignNativeInterface: true);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable]
            [Terrum.Interop.ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                public unsafe int32 Read();
                public mut unsafe int32 Read();
            }

            int32 Invoke(write ptr<void> receiver) {
                unsafe { return IProbe.Read(receiver); }
            }
            """), new CompilationOptions {
                NativeCoreRegistration = registration,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var invoke = compilation.Program!.Functions.Single(
            static function => function.Name == "Invoke");
        BoundCall? foreignCall = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(invoke.Body,
            expression =>
            {
                if (expression is BoundCall call
                    && call.ForeignInterfaceCall is not null)
                {
                    foreignCall = call;
                }
            }), Is.True);
        Assert.That(foreignCall?.ForeignInterfaceCall?.Member.MemberOrdinal,
            Is.EqualTo(1));
        Assert.That(foreignCall?.ForeignInterfaceCall
            ?.RequiresMutableReceiver, Is.True);
    }

    [Test]
    public void ForeignNativeInterfaceRejectsUnsupportedInterfaceShapes()
    {
        var registration = CreateRegistration(
            includeForeignNativeInterface: true);
        var sources = new[] {
            """
            [Terrum.Interop.ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public partial interface IProbe {
                public unsafe int32 Read();
            }
            """,
            """
            [Terrum.Interop.ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe<T> {
                public unsafe int32 Read();
            }
            """,
            """
            public interface IBase { }
            [Terrum.Interop.ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe : IBase {
                public unsafe int32 Read();
            }
            """,
            """
            [Terrum.Interop.ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                public unsafe int32 Read() { return 42; }
            }
            """,
            """
            [Terrum.Interop.ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                public static unsafe int32 Read();
            }
            """,
            """
            [Terrum.Interop.ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                public extern unsafe int32 Read();
            }
            """,
            """
            [Terrum.Interop.NativeCallable]
            [Terrum.Interop.ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                public unsafe int32 Read(ref<int32> value);
            }
            """,
            """
            [Terrum.Interop.NativeCallable(
                CallConv: Terrum.Interop.NativeCallingConvention.Vectorcall)]
            [Terrum.Interop.ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                public unsafe int32 Read();
            }
            """,
        };

        foreach (var source in sources)
        {
            var compilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(source), new CompilationOptions {
                    NativeCoreRegistration = registration,
                });
            Assert.That(compilation.HasErrors, Is.True,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            Assert.That(compilation.Diagnostics.Any(static diagnostic =>
                diagnostic.Code == "TRM3030"), Is.True,
                string.Join(Environment.NewLine, compilation.Diagnostics));
        }
    }

    [Test]
    public void NativeCallableInterfaceDefaultsApplyAcrossPartialPartsAndOverrides()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;

            [NativeCallable(CallConv: SelectConvention())]
            public partial interface IProbe {
                public int32 InheritedFirst();
                [NativeCallable(CallConv: NativeCallingConvention.Stdcall)]
                public int32 MethodOverride();
            }

            public partial interface IProbe {
                [NativeCallable]
                public int32 ExplicitUnmanaged();
                public int32 InheritedSecond();
            }

            [ConstEval]
            internal NativeCallingConvention SelectConvention() {
                return NativeCallingConvention.Cdecl;
            }
            """), CoreEvaluationFormTestHelpers.WithConstEval(
                new CompilationOptions {
                    NativeCoreRegistration = CreateRegistration(),
                }));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var members = compilation.Program!.Interfaces.Single().Members;
        Assert.That(members.Select(static member =>
            member.NativeCallableConvention), Is.EqualTo(
            [
                NativeCallingConvention.Cdecl,
                NativeCallingConvention.Stdcall,
                NativeCallingConvention.Unmanaged,
                NativeCallingConvention.Cdecl,
            ]));
        Assert.That(members.All(static member =>
            member.RequiresUnsafeCaller), Is.True);
        Assert.That(members[0].DeclaredRequiresUnsafeCaller, Is.False);
        var invalidConvention = compilation.Program with {
            Interfaces = compilation.Program.Interfaces.SetItem(0,
                compilation.Program.Interfaces[0] with {
                    Members = members.SetItem(0, members[0] with {
                        NativeCallableConvention = NativeCallingConvention.Native,
                    }),
                }),
        };
        _ = Assert.Throws<ArgumentException>(() =>
            SemanticVerifier.VerifyLibrary(invalidConvention));

        var aliased = Compilation.CreateLibrary(SyntaxTree.Parse("""
            alias Callable = Terrum.Interop.NativeCallable;
            [Callable(CallConv: Terrum.Interop.NativeCallingConvention.Cdecl)]
            public interface IAliasedProbe {
                public int32 Read();
            }
            """), new CompilationOptions {
                NativeCoreRegistration = CreateRegistration(),
            });
        Assert.That(aliased.HasErrors, Is.False,
            string.Join(Environment.NewLine, aliased.Diagnostics));
        Assert.That(aliased.Program!.Interfaces.Single().Members.Single()
            .NativeCallableConvention,
            Is.EqualTo(NativeCallingConvention.Cdecl));
    }

    [Test]
    public void NativeCallableInterfaceDefaultRequiresPrimaryRegistrationAndMethodTargets()
    {
        var registration = CreateRegistration();
        var secondaryDefault = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public partial interface IProbe {
                    public int32 First();
                }

                [Terrum.Interop.NativeCallable]
                public partial interface IProbe {
                    public int32 Second();
                }
                """), new CompilationOptions {
                    NativeCoreRegistration = registration,
                });
        Assert.That(secondaryDefault.HasErrors, Is.True,
            string.Join(Environment.NewLine, secondaryDefault.Diagnostics));
        Assert.That(secondaryDefault.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, secondaryDefault.Diagnostics));

        var unregistered = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable]
            public interface IProbe {
                public int32 Read();
            }
            """));
        Assert.That(unregistered.HasErrors, Is.True,
            string.Join(Environment.NewLine, unregistered.Diagnostics));
        Assert.That(unregistered.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, unregistered.Diagnostics));

        var misplaced = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;
            public interface IProbe {
                [NativeCallable]
                public int32 Value { get; }
                public int32 Read([NativeCallable] int32 value);
            }
            [NativeCallable]
            public struct NativeCallableTypeTarget {
                private int32 Value;
            }
            """), new CompilationOptions {
                NativeCoreRegistration = registration,
            });
        Assert.That(misplaced.HasErrors, Is.True,
            string.Join(Environment.NewLine, misplaced.Diagnostics));
        Assert.That(misplaced.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, misplaced.Diagnostics));
    }

    [Test]
    public void RegistrationCanOmitNativeExportWhenThatRoleIsNotSelected()
    {
        var fullRegistration = CreateRegistration();
        var nativeExport = fullRegistration.NativeExport!;
        var nativeExportIdentity = nativeExport.Identity;
        var declarations = fullRegistration.ModuleBindings.Declarations
            .Where(declaration => declaration.Identity != nativeExportIdentity)
            .ToImmutableArray();
        var bindings = CoreProviderModuleBindings.Create(
            fullRegistration.UsingModuleIdentity,
            fullRegistration.ModuleBindings.Providers, declarations);
        var registration = NativeCoreRegistrationSet.Create(bindings,
            fullRegistration.CallingConventions,
            fullRegistration.ResolutionModes,
            fullRegistration.ResolutionPolicies,
            fullRegistration.NativeImport,
            fullRegistration.NativeCallable,
            fullRegistration.NativeCallConv,
            nativeExport: null);

        Assert.That(registration.IsValid(), Is.True);
        Assert.That(registration.NativeExport, Is.Null);
        Assert.That(registration.ModuleBindings.Declarations,
            Does.Not.Contain(CoreProviderDeclarationSnapshot.From(nativeExport)));
    }

    [Test]
    public void RegistrationRejectsWrongEnumValuesAndSchemaOwners()
    {
        var registration = CreateRegistration();
        var invalidMembers = registration.CallingConventions.Members.SetItem(1,
            registration.CallingConventions.Members[1] with { Value = 1 });
        var invalidEnum = registration.CallingConventions with { Members = invalidMembers };

        _ = Assert.Throws<ArgumentException>(() => NativeCoreRegistrationSet.Create(
            registration.ModuleBindings, invalidEnum, registration.ResolutionModes,
            registration.ResolutionPolicies, registration.NativeImport,
            registration.NativeCallable, registration.NativeCallConv, registration.NativeExport!));

        var invalidBlittableRequirement = registration.CallingConventions with {
            BlittableTargetRequirement = 2,
        };
        _ = Assert.Throws<ArgumentException>(() => NativeCoreRegistrationSet.Create(
            registration.ModuleBindings, invalidBlittableRequirement,
            registration.ResolutionModes, registration.ResolutionPolicies,
            registration.NativeImport, registration.NativeCallable,
            registration.NativeCallConv, registration.NativeExport!));

        var invalidMemberKey = registration.CallingConventions with {
            Members = registration.CallingConventions.Members.SetItem(1,
                registration.CallingConventions.Members[1] with { DeclarationKey = "" }),
        };
        _ = Assert.Throws<ArgumentException>(() => NativeCoreRegistrationSet.Create(
            registration.ModuleBindings, invalidMemberKey, registration.ResolutionModes,
            registration.ResolutionPolicies, registration.NativeImport,
            registration.NativeCallable, registration.NativeCallConv,
            registration.NativeExport!));

        var duplicateDeclarationKey = registration.CallingConventions with {
            Members = registration.CallingConventions.Members.SetItem(1,
                registration.CallingConventions.Members[1] with {
                    DeclarationKey = registration.CallingConventions.Members[0].DeclarationKey,
                }),
        };
        _ = Assert.Throws<ArgumentException>(() => NativeCoreRegistrationSet.Create(
            registration.ModuleBindings, duplicateDeclarationKey,
            registration.ResolutionModes, registration.ResolutionPolicies,
            registration.NativeImport, registration.NativeCallable,
            registration.NativeCallConv, registration.NativeExport!));

        var invalidImport = registration.NativeImport with {
            CallingConventionEnum = registration.ResolutionModes.Identity,
        };
        _ = Assert.Throws<ArgumentException>(() => NativeCoreRegistrationSet.Create(
            registration.ModuleBindings, registration.CallingConventions,
            registration.ResolutionModes, registration.ResolutionPolicies, invalidImport,
            registration.NativeCallable, registration.NativeCallConv, registration.NativeExport!));
    }

    [Test]
    public void RegistrationRetainsCompleteNativeExportSchemaExpectations()
    {
        var registration = CreateRegistration();
        var nativeExport = registration.NativeExport!;
        var argument = nativeExport.Arguments.Single();
        Assert.That(nativeExport.Placement, Is.EqualTo("primary-declaration"));
        Assert.That(nativeExport.Targets, Is.EquivalentTo(Contexts(
            "static-defined-callable", "static-defined-data")));
        Assert.That(argument.Name, Is.EqualTo("EntryName"));
        Assert.That(argument.Kind,
            Is.EqualTo(CoreNativeAttributeArgumentKind.EntryNameLiteral));
        Assert.That(argument.IsRequired, Is.False);
        Assert.That(argument.AllowsPositional, Is.True);
        Assert.That(argument.AllowsNamed, Is.True);
        Assert.That(argument.Contexts,
            Is.EquivalentTo(Contexts("method", "data")));
        Assert.That(argument.Default!.Value.Kind,
            Is.EqualTo(CoreNativeAttributeDefaultKind.DeclarationName));

        var incompleteExport = nativeExport with {
            Arguments = default,
        };
        var incompleteDeclarations = registration.ModuleBindings.Declarations
            .Select(declaration => declaration.Identity == incompleteExport.Identity
                ? CoreProviderDeclarationSnapshot.From(incompleteExport)
                : declaration)
            .ToImmutableArray();
        _ = Assert.Throws<ArgumentException>(() =>
            CoreProviderModuleBindings.Create(registration.UsingModuleIdentity,
                registration.ModuleBindings.Providers, incompleteDeclarations));

        var changedExport = nativeExport with {
            Arguments = nativeExport.Arguments.SetItem(0,
                argument with { Name = "ExportName" }),
        };
        _ = Assert.Throws<ArgumentException>(() =>
            NativeCoreRegistrationSet.Create(registration.ModuleBindings,
                registration.CallingConventions, registration.ResolutionModes,
                registration.ResolutionPolicies, registration.NativeImport,
                registration.NativeCallable, registration.NativeCallConv,
                changedExport));

        var changedDeclarations = registration.ModuleBindings.Declarations
            .Select(declaration => declaration.Identity == changedExport.Identity
                ? CoreProviderDeclarationSnapshot.From(changedExport)
                : declaration)
            .ToImmutableArray();
        var changedBindings = CoreProviderModuleBindings.Create(
            registration.UsingModuleIdentity,
            registration.ModuleBindings.Providers, changedDeclarations);
        _ = Assert.Throws<ArgumentException>(() =>
            NativeCoreRegistrationSet.Create(changedBindings,
                registration.CallingConventions, registration.ResolutionModes,
                registration.ResolutionPolicies, registration.NativeImport,
                registration.NativeCallable, registration.NativeCallConv,
                changedExport));
    }

    [Test]
    public void RegistrationRequiresEverySelectedProviderOrigin()
    {
        var registration = CreateRegistration();
        var missingProvider = CoreProviderModuleBindings.Create("consumer.module", [
            new CoreProviderModuleOrigin("other.provider", "test-revision",
                new CoreProviderSchemaSelection("core.native-registration", 1),
                1, 0, 0),
        ]);

        _ = Assert.Throws<ArgumentException>(() => NativeCoreRegistrationSet.Create(
            missingProvider, registration.CallingConventions,
            registration.ResolutionModes, registration.ResolutionPolicies,
            registration.NativeImport, registration.NativeCallable,
            registration.NativeCallConv, registration.NativeExport!));
    }

    [Test]
    public void ProviderModuleBindingsRejectDuplicateAndUnsupportedSelections()
    {
        var origin = new CoreProviderModuleOrigin("core.native", "test-revision",
            new CoreProviderSchemaSelection("core.native-registration", 1), 1, 0, 0);

        _ = Assert.Throws<ArgumentException>(() =>
            CoreProviderModuleBindings.Create("consumer.module", [origin, origin]));
        _ = Assert.Throws<ArgumentException>(() =>
            CoreProviderModuleBindings.Create("consumer.module", [origin with {
                TargetRequirement = 2,
            }]));
        _ = Assert.Throws<ArgumentException>(() =>
            CoreProviderModuleBindings.Create("consumer.module", [origin with {
                Schema = new CoreProviderSchemaSelection("core.native-registration", 0),
            }]));
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
    public void BoundEnumPreservesItsBlittableTargetRequirement()
    {
        var registration = CreateRegistration();
        var conventions = registration.CallingConventions with {
            BlittableTargetRequirement = FixedLayoutStorage.WindowsX64TargetRequirement,
        };
        var declarations = registration.ModuleBindings.Declarations
            .Select(declaration => declaration.Identity == conventions.Identity
                ? CoreProviderDeclarationSnapshot.From(conventions)
                : declaration)
            .ToImmutableArray();
        var bindings = CoreProviderModuleBindings.Create(
            registration.UsingModuleIdentity,
            registration.ModuleBindings.Providers, declarations);
        var configuredRegistration = NativeCoreRegistrationSet.Create(
            bindings, conventions, registration.ResolutionModes,
            registration.ResolutionPolicies, registration.NativeImport,
            registration.NativeCallable, registration.NativeCallConv,
            registration.NativeExport!);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Identity(int32 value) { return value; }"),
            new CompilationOptions { NativeCoreRegistration = configuredRegistration });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program!.NativeCoreRegistration,
            Is.SameAs(configuredRegistration));
        var definition = compilation.Program!.EnumDefinitions.Single(
            enumDefinition => enumDefinition.Identity
                == new BoundEnumDeclarationIdentity.Core(
                    conventions.Identity));
        Assert.That(definition.BlittableTargetRequirement,
            Is.EqualTo(FixedLayoutStorage.WindowsX64TargetRequirement));
        Assert.That(definition.Members[0].DeclarationKey,
            Is.EqualTo(conventions.Members[0].DeclarationKey));
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
    public void NativeExportMethodBindsItsSelectedNameAndCallableContract()
    {
        var registration = CreateRegistration();
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            alias ExportAlias = Terrum.Interop.NativeExport;

            [Terrum.Interop.NativeExport]
            public int32 Probe(int32 value) { return value; }

            [Terrum.Interop.NativeCallable(
                CallConv: Terrum.Interop.NativeCallingConvention.Cdecl)]
            [Terrum.Interop.NativeExport(EntryName: "Probe_C")]
            public int32 ProbeC(int32 value) { return value; }

            [ExportAlias]
            internal int32 ProbeAlias(int32 value) { return value; }

            struct StaticApi {
                int32 Value;
                [Terrum.Interop.NativeExport(EntryName: "Static")]
                static int32 Export(int32 value) { return value; }
            }
            """), new CompilationOptions {
            NativeCoreRegistration = registration,
        });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var probe = program.Functions.Single(static function =>
            function.Name == "Probe");
        Assert.That(probe.HasNativeExport, Is.True);
        Assert.That(probe.NativeExportEntryName, Is.EqualTo("Probe"));
        Assert.That(probe.NativeExportUsesDeclarationName, Is.True);
        Assert.That(probe.NativeCallableConvention,
            Is.EqualTo(NativeCallingConvention.Unmanaged));
        Assert.That(probe.NativeFunctionPointerType, Is.Not.Null);
        Assert.That(probe.RequiresUnsafeCaller, Is.True);
        var probeIndex = program.Functions.IndexOf(probe);
        var invalidProgram = program with {
            Functions = program.Functions.SetItem(probeIndex,
                probe with { NativeExportEntryName = "" }),
        };
        _ = Assert.Throws<ArgumentException>(() =>
            SemanticVerifier.VerifyLibrary(invalidProgram));

        var probeC = program.Functions.Single(static function =>
            function.Name == "ProbeC");
        Assert.That(probeC.HasNativeExport, Is.True);
        Assert.That(probeC.NativeExportEntryName, Is.EqualTo("Probe_C"));
        Assert.That(probeC.NativeExportUsesDeclarationName, Is.False);
        Assert.That(probeC.NativeCallableConvention,
            Is.EqualTo(NativeCallingConvention.Cdecl));
        Assert.That(probeC.NativeFunctionPointerType, Is.Not.Null);

        var probeAlias = program.Functions.Single(static function =>
            function.Name == "ProbeAlias");
        Assert.That(probeAlias.HasNativeExport, Is.True);
        Assert.That(probeAlias.NativeExportEntryName, Is.EqualTo("ProbeAlias"));
        Assert.That(probeAlias.NativeCallableConvention,
            Is.EqualTo(NativeCallingConvention.Unmanaged));

        ImmutableArray<string> expectedImageExports = [
            "Probe", "ProbeAlias", "Probe_C", "Static",
        ];
        var image = NativeEmitter.EmitLibrary(program, [],
            NativeRuntimePolicy.FailFast);
        Assert.That(ReadPeExportNames(image).ToArray(),
            Is.EqualTo(expectedImageExports.ToArray()));
        var hostAndSourceImage = NativeEmitter.EmitLibrary(program,
            ["Probe"], NativeRuntimePolicy.FailFast);
        Assert.That(ReadPeExportNames(hostAndSourceImage).ToArray(),
            Is.EqualTo(expectedImageExports.ToArray()));
    }

    [Test]
    public void NativeExportMethodAppearsInExecutablePeExportDirectory()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeExport(EntryName: "Probe")]
            int32 Probe(int32 value) { return value; }

            int32 Main() { unsafe { return Probe(42); } }
            """), new CompilationOptions {
            NativeCoreRegistration = CreateRegistration(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var image = NativeEmitter.Emit(compilation.Program!,
            NativeRuntimePolicy.FailFast);
        Assert.That(ReadPeExportNames(image), Is.EqualTo(["Probe"]));
        using var reader = new PEReader(new MemoryStream(image));
        var headers = reader.PEHeaders;
        Assert.That(headers.PEHeader!.ExportTableDirectory.RelativeVirtualAddress,
            Is.GreaterThan(0));
        Assert.That(headers.CoffHeader.Characteristics
            .HasFlag(Characteristics.Dll), Is.False);
    }

    [Test]
    public void NativeExportRejectsAbsentRegistrationAndMalformedDeclarations()
    {
        var noRegistration = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeExport]
            int32 Probe(int32 value) { return value; }
            """));
        Assert.That(noRegistration.HasErrors, Is.True);
        Assert.That(noRegistration.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, noRegistration.Diagnostics));
        var unregisteredShort = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;
            [NativeExport]
            int32 Probe(int32 value) { return value; }
            """));
        Assert.That(unregisteredShort.HasErrors, Is.True);
        Assert.That(unregisteredShort.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, unregisteredShort.Diagnostics));

        var registration = CreateRegistration();
        foreach (var source in new[] {
            """
            [Terrum.Interop.NativeExport(ExportName: "Probe")]
            int32 Probe(int32 value) { return value; }
            """,
            """
            [Terrum.Interop.NativeExport("First", "Second")]
            int32 Probe(int32 value) { return value; }
            """,
            """
            [Terrum.Interop.NativeExport(EntryName: "")]
            int32 Probe(int32 value) { return value; }
            """,
            """
            [Terrum.Interop.NativeExport]
            [Terrum.Interop.NativeExport]
            int32 Probe(int32 value) { return value; }
            """,
            """
            [Terrum.Interop.NativeImport("example.dll", "Probe")]
            [Terrum.Interop.NativeExport]
            extern int32 Probe(int32 value);
            """,
        })
        {
            var compilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(source), new CompilationOptions {
                    NativeCoreRegistration = registration,
                });
            Assert.That(compilation.HasErrors, Is.True, source);
            Assert.That(compilation.Diagnostics.Any(static diagnostic =>
                diagnostic.Code == "TRM3030"), Is.True,
                string.Join(Environment.NewLine, compilation.Diagnostics));
        }
    }

    [Test]
    public void NativeExportRejectsCollidingSourceSymbols()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeExport(EntryName: "Shared")]
            internal int32 First(int32 value) { return value; }

            [Terrum.Interop.NativeExport(EntryName: "Shared")]
            internal int32 Second(int32 value) { return value; }
            """), new CompilationOptions {
            NativeCoreRegistration = CreateRegistration(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitLibrary(compilation.Program!, [],
                NativeRuntimePolicy.FailFast));
    }

    [Test]
    public void StandardNativeExportRequiresItsSelectedCallableContract()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeExport]
            internal int32 Probe(int32 value) { return value; }
            """), new CompilationOptions {
            NativeCoreRegistration = CreateRegistration(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, "consumer.module",
            "trrm/1-draft-layout-queries-1", 1);
        var index = new TrRmArtifactIndex(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(index);
        Assert.That(tables.NativeExports.GetEntryName(1),
            Is.EqualTo("Probe"));
        Assert.That(index.TryGetSection(0x0022, out var nativeExportRows,
            out var nativeExportCount), Is.True);
        Assert.That(nativeExportCount, Is.EqualTo(1));
        Assert.That(nativeExportRows[0], Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            nativeExportRows.Slice(4, sizeof(uint))), Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            nativeExportRows.Slice(8, sizeof(uint))), Is.Zero);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            nativeExportRows.Slice(12, sizeof(uint))), Is.GreaterThan(0));

        var explicitCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Terrum.Interop.NativeExport(EntryName: "Probe_C")]
                internal int32 Probe(int32 value) { return value; }
                """), new CompilationOptions {
                NativeCoreRegistration = CreateRegistration(),
            });
        Assert.That(explicitCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                explicitCompilation.Diagnostics));
        var explicitArtifact = TrRmExecutableCompiler.EmitStandard(
            explicitCompilation.Program!, "consumer.module",
            "trrm/1-draft-layout-queries-1", 1);
        var explicitIndex = new TrRmArtifactIndex(explicitArtifact);
        var explicitTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(explicitIndex);
        Assert.That(explicitTables.NativeExports.GetEntryName(1),
            Is.EqualTo("Probe_C"));
        Assert.That(explicitIndex.TryGetSection(0x0022,
            out var explicitExportRows, out var explicitExportCount), Is.True);
        Assert.That(explicitExportCount, Is.EqualTo(1));
        var entryNameRow = BinaryPrimitives.ReadInt32LittleEndian(
            explicitExportRows.Slice(8, sizeof(int)));
        Assert.That(entryNameRow, Is.GreaterThan(0));
        Assert.That(explicitIndex.TryGetSection(0x0002,
            out var stringBytes, out var stringCount), Is.True);
        var strings = new TrRmStringTableReader(stringBytes, stringCount);
        Assert.That(strings.DecodeName(entryNameRow), Is.EqualTo("Probe_C"));
    }

    [Test]
    public void StandardNativeExportPreservesStaticTypeMethodOwner()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct StaticApi {
                int32 Value;

                [Terrum.Interop.NativeExport(EntryName: "Static")]
                static int32 Export(int32 value) { return value; }
            }
            """), new CompilationOptions {
            NativeCoreRegistration = CreateRegistration(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, "static-export.module",
            "trrm/1-draft-layout-queries-1", 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(artifact));
        Assert.That(tables.NativeExports.Count, Is.EqualTo(1));
        Assert.That(tables.NativeExports.GetEntryName(1),
            Is.EqualTo("Static"));
        var exportedMethod = tables.NativeExports.GetExportForMethod(1);
        Assert.That(exportedMethod.OwnerKind, Is.EqualTo(1));
        var ownerType = tables.Methods.GetOwnerType(exportedMethod.OwnerRef);
        Assert.That(ownerType, Is.GreaterThan(0));
        Assert.That(tables.TypeDefs.GetName(ownerType),
            Is.EqualTo("StaticApi"));
    }

    [Test]
    public void SplitProviderNativeImportSurvivesPrivateSourceFreeReplacementChecks()
    {
        var options = new CompilationOptions {
            NativeCoreRegistration = CreateSplitProviderRegistration(),
        };
        var sourceCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll", "Observe",
                CallConv: Terrum.Interop.NativeCallingConvention.Cdecl)]
            public extern unsafe int32 Observe();
            """), options);
        Assert.That(sourceCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, sourceCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            sourceCompilation.Program!, ["Observe"]);

        var reference = ModuleCompiler.CreateReference(producer);
        Assert.That(reference.NativeCoreRegistration, Is.Null);
        Assert.That(reference.Functions.Single(static function =>
            function.Name == "Observe").DirectNativeImport!.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Cdecl));
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            unsafe int32 Forward() {
                unsafe { return Observe(); }
            }
            int32 Main() { return 0; }
            """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, producer);

        Assert.DoesNotThrow(() => ModuleCompiler.Link(consumer, producer));

        var replacementCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Terrum.Interop.NativeImport(
                    "example.dll", "Observe",
                    CallConv: Terrum.Interop.NativeCallingConvention.Stdcall)]
                public extern unsafe int32 Observe();
                """), options);
        Assert.That(replacementCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                replacementCompilation.Diagnostics));
        var replacement = ModuleCompiler.CreateProducer(
            replacementCompilation.Program!, ["Observe"]);
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.Link(consumer, replacement));
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

        _ = Assert.Throws<ArgumentException>(() => Compilation.CreateLibrary(
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
    public void NativeCallConvOptionalFunctionPointerSuffixMatchesConstructor()
    {
        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;
            unsafe void AcceptSuffix(
                [NativeCallConv(NativeCallingConvention.Cdecl)] fnptr<int32>? callback) {}
            unsafe void AcceptConstructor(
                option<[NativeCallConv(NativeCallingConvention.Cdecl)] fnptr<int32>> callback) {}
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var functions = compilation.Program!.Functions;
        var suffixType = functions.Single(static function =>
            function.Name == "AcceptSuffix").Variables[0].Type;
        var constructorType = functions.Single(static function =>
            function.Name == "AcceptConstructor").Variables[0].Type;
        Assert.That(CheckedReferenceTypes.IsType(suffixType), Is.True);
        Assert.That(suffixType, Is.EqualTo(constructorType));
        var optional = compilation.Program.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(suffixType)];
        Assert.That(optional.IsOptionalValue, Is.True);
        Assert.That(NativeFunctionPointerTypes.IsType(
                optional.ReferentType),
            Is.True);
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
        var registration = CreateRegistration();
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: registration,
            layoutCoreRegistration: CreateLayoutRegistration(registration));
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
            """), reference,
            CoreEvaluationFormTestHelpers.Options());

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
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(program, ["Read"]));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(program, "nominal-enum"));
    }

    [Test]
    public async Task NativeEmitterPreservesUnrelatedEnumArgumentAndResultBits()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var enumDefinition = CreateUnrelatedEnum() with
        {
            BlittableTargetRequirement = FixedLayoutStorage.WindowsX64TargetRequirement,
        };
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            enumDefinitions: [enumDefinition]);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            alias Palette = Sample.Palette;

            public Palette Echo(Palette value) {
                return value;
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(program.EnumDefinitions.Single().BlittableTargetRequirement,
            Is.EqualTo(FixedLayoutStorage.WindowsX64TargetRequirement));
        var image = NativeEmitter.EmitLibrary(program, ["Echo"],
            NativeRuntimePolicy.FailFast);
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"native-enum-abi-{Guid.NewGuid():N}.dll");
        var handle = IntPtr.Zero;
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            var repositoryRoot = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
            var testSourcePath = Path.Combine(repositoryRoot, "tests",
                "Terrum.CodeAnalysis.Tests", "Semantics",
                "NativeCoreRegistrationTests.cs");
            var compilerAssemblyPath = typeof(NativeEmitter).Assembly.Location;
            var testAssemblyPath = typeof(NativeCoreRegistrationTests).Assembly.Location;
            TestContext.WriteLine("target=Windows-x64; policy=FailFast; abi=Winapi(int32)->int32");
            TestContext.WriteLine($"enum={program.EnumDefinitions.Single().Name}; blittable-target={program.EnumDefinitions.Single().BlittableTargetRequirement}");
            TestContext.WriteLine($"compiler-assembly-path={compilerAssemblyPath}");
            TestContext.WriteLine($"compiler-assembly-sha256={GetSha256(compilerAssemblyPath)}");
            TestContext.WriteLine($"test-assembly-path={testAssemblyPath}");
            TestContext.WriteLine($"test-assembly-sha256={GetSha256(testAssemblyPath)}");
            TestContext.WriteLine($"test-source-path={testSourcePath}");
            TestContext.WriteLine($"test-source-sha256={GetSha256(testSourcePath)}");
            TestContext.WriteLine($"output-dll-path={path}");
            TestContext.WriteLine($"output-dll-sha256={GetSha256(path)}");
            handle = NativeLibrary.Load(path);
            var echo = Marshal.GetDelegateForFunctionPointer<EnumRoundTrip>(
                NativeLibrary.GetExport(handle, "Echo"));
            Assert.That(echo(0x13572468), Is.EqualTo(0x13572468));
            Assert.That(echo(int.MinValue), Is.EqualTo(int.MinValue));
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeLibrary.Free(handle);
            }
            await TestFileCleanup.DeleteWithRetryAsync(path)
                .ConfigureAwait(true);
        }
    }

    private static string GetSha256(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static ImmutableArray<string> ReadPeExportNames(byte[] image)
    {
        using var reader = new PEReader(new MemoryStream(image));
        var exportDirectory = reader.PEHeaders.PEHeader!.ExportTableDirectory;
        if (exportDirectory.RelativeVirtualAddress == 0)
        {
            return [];
        }

        var directory = reader.GetSectionData(
            exportDirectory.RelativeVirtualAddress).GetContent().AsSpan();
        var nameCount = BinaryPrimitives.ReadInt32LittleEndian(
            directory.Slice(24, sizeof(int)));
        var namesRva = BinaryPrimitives.ReadInt32LittleEndian(
            directory.Slice(32, sizeof(int)));
        if (nameCount < 0 || namesRva <= 0)
        {
            throw new InvalidDataException("Invalid PE export directory.");
        }

        var names = reader.GetSectionData(namesRva).GetContent().AsSpan();
        var exports = ImmutableArray.CreateBuilder<string>(nameCount);
        for (var index = 0; index < nameCount; index++)
        {
            var nameRva = BinaryPrimitives.ReadInt32LittleEndian(
                names.Slice(index * sizeof(int), sizeof(int)));
            var nameBytes = reader.GetSectionData(nameRva).GetContent().AsSpan();
            var end = nameBytes.IndexOf((byte)0);
            if (end < 0)
            {
                throw new InvalidDataException("An exported PE name is unterminated.");
            }
            exports.Add(Encoding.ASCII.GetString(nameBytes[..end]));
        }

        return exports.MoveToImmutable();
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
            """), reference,
            CoreEvaluationFormTestHelpers.Options());

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
            """), reference,
            CoreEvaluationFormTestHelpers.Options());

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
        var registration = CreateRegistration();
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: registration,
            layoutCoreRegistration: CreateLayoutRegistration(registration));
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

            int32 Invoke(fnptr<
                ptr<[NativeCallConv(Forward(ABI.Cdecl, true))] fnptr<int32>>,
                int32> callback) {
                return 0;
            }
            """), reference,
            CoreEvaluationFormTestHelpers.Options());

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
        var moduleReference = ModuleCompiler.CreateReference(
            ModuleCompiler.CreateProducer(compilation.Program, ["Identity"]));
        var persistedFunction = moduleReference.Functions.Single();
        Assert.That(persistedFunction.NativeCallableConvention?.ToString(),
            Is.EqualTo(expectedConvention));
        Assert.That(persistedFunction.NativeFunctionPointerType, Is.Not.Null);
        var persistedSignature = moduleReference.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(
                persistedFunction.NativeFunctionPointerType!.Value)];
        Assert.That(persistedSignature.CallingConvention.ToString(),
            Is.EqualTo(expectedConvention));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(compilation.Program, "native-callable"));
    }

    [Test]
    [TestCase("Unmanaged", NativeCallingConvention.Unmanaged, 0)]
    [TestCase("Cdecl", NativeCallingConvention.Cdecl, 1)]
    [TestCase("Stdcall", NativeCallingConvention.Stdcall, 2)]
    [TestCase("Vectorcall", NativeCallingConvention.Vectorcall, 3)]
    [TestCase(null, NativeCallingConvention.Native, 4)]
    public void Private106RoundTripsFunctionPointerConventionWithReferenceTypes(
        string? conventionName, NativeCallingConvention expectedConvention,
        byte expectedPrivateConvention)
    {
        var image = CreatePrivate106FunctionPointerImage(conventionName);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(4, 4)),
            Is.EqualTo(106));
        var conventionOffset =
            GetPrivate106SignatureConventionOffset(image);
        Assert.That(image[conventionOffset],
            Is.EqualTo(expectedPrivateConvention));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            image.AsSpan(conventionOffset + 13, sizeof(uint))), Is.Zero);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(conventionOffset + 17, sizeof(int))), Is.Zero);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(conventionOffset + 21, sizeof(int))), Is.Zero);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(conventionOffset + 25, sizeof(int))), Is.Zero);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(conventionOffset + 29, sizeof(int))), Is.Zero);

        var moduleReference = ModuleCompiler.CreateReference(image);
        var signature = moduleReference.NativeFunctionPointerSignatures
            .Single();
        Assert.That(signature.CallingConvention,
            Is.EqualTo(expectedConvention));
        Assert.That(signature.EffectsMask, Is.Zero);
        Assert.That(signature.Selectors, Is.Empty);
        Assert.That(signature.OutputStates, Is.Empty);
        Assert.That(signature.Dependencies, Is.Empty);
        Assert.That(signature.NativeParameters, Is.Empty);
    }

    [Test]
    public void Private106RejectsInvalidSignatureConventionAndRowCounts()
    {
        var image = CreatePrivate106FunctionPointerImage("Cdecl");
        var conventionOffset =
            GetPrivate106SignatureConventionOffset(image);
        image[conventionOffset] = 5;
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(image));

        image = CreatePrivate106FunctionPointerImage("Cdecl");
        conventionOffset = GetPrivate106SignatureConventionOffset(image);
        BinaryPrimitives.WriteUInt32LittleEndian(
            image.AsSpan(conventionOffset + 13, sizeof(uint)), 7);
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(image));

        image = CreatePrivate106FunctionPointerImage("Cdecl");
        conventionOffset = GetPrivate106SignatureConventionOffset(image);
        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(conventionOffset + 17, sizeof(int)), int.MaxValue);
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(image));
    }

    [Test]
    public void Private106RoundTripsNativeCallableConventionAndAddressRoot()
    {
        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable(
                CallConv: Terrum.Interop.NativeCallingConvention.Cdecl)]
            public int32 Identity(int32 value) { return value; }

            public int32 Read(implicit ref<int32> value) { return value; }
            int32 Main() { return 0; }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var image = ModuleCompiler.CreateProducer(
            compilation.Program!, ["Identity", "Read"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(4, 4)),
            Is.EqualTo(106));

        var moduleReference = ModuleCompiler.CreateReference(image);
        var identity = moduleReference.Functions.Single(static function =>
            function.Name == "Identity");
        Assert.That(identity.NativeCallableConvention,
            Is.EqualTo(NativeCallingConvention.Cdecl));
        Assert.That(identity.NativeFunctionPointerType, Is.Not.Null);
    }

    private static byte[] CreatePrivate106FunctionPointerImage(
        string? conventionName)
    {
        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: CreateRegistration());
        var callConvAttribute = conventionName is null ? string.Empty
            : $"[Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.{conventionName})]";
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            public int32 Invoke(
                {{callConvAttribute}}
                fnptr<int32, int32> callback,
                implicit ref<int32> value) {
                unsafe { return callback(value); }
            }

            int32 Main() { return 0; }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return ModuleCompiler.CreateProducer(
            compilation.Program!, ["Invoke"]);
    }

    private static int GetPrivate106SignatureConventionOffset(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var reader = new BinaryReader(stream);
        Assert.That(Encoding.ASCII.GetString(reader.ReadBytes(4)),
            Is.EqualTo("TRRM"));
        Assert.That(reader.ReadInt32(), Is.EqualTo(106));
        SkipPrivateString(reader);
        _ = reader.ReadBoolean();
        _ = reader.ReadByte();
        _ = reader.ReadBoolean();
        SkipPrivateString(reader);
        var bufferCount = reader.ReadInt32();
        for (var index = 0; index < bufferCount; index++)
        {
            SkipPrivateString(reader);
            _ = reader.ReadInt32();
            _ = reader.ReadBoolean();
        }

        _ = reader.ReadInt32();
        Assert.That(reader.ReadInt32(), Is.Zero);
        Assert.That(reader.ReadInt32(), Is.Zero);
        Assert.That(reader.ReadInt32(), Is.Zero);
        Assert.That(reader.ReadInt32(), Is.EqualTo(1));
        return checked((int)stream.Position);
    }

    private static void SkipPrivateString(BinaryReader reader)
    {
        var byteCount = reader.ReadInt32();
        Assert.That(byteCount, Is.GreaterThanOrEqualTo(0));
        Assert.That(reader.ReadBytes(byteCount), Has.Length.EqualTo(byteCount));
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
            """), reference,
            CoreEvaluationFormTestHelpers.Options());

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
        var moduleReference = ModuleCompiler.CreateReference(
            ModuleCompiler.CreateProducer(compilation.Program, ["Read"]));
        var persistedImport = moduleReference.Functions.Single();
        Assert.That(persistedImport.DirectNativeImport!.ExportName,
            Is.EqualTo("Read"));
        Assert.That(persistedImport.DirectNativeImport.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Cdecl));
        Assert.That(persistedImport.DirectNativeImport.ResolutionMode,
            Is.EqualTo(NativeImportResolutionMode.Link));
        Assert.That(persistedImport.DirectNativeImport.ResolutionPolicy,
            Is.EqualTo(NativeImportResolutionPolicy.NotApplicable));
        _ = Assert.Throws<NotSupportedException>(() =>
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
            """), reference,
            CoreEvaluationFormTestHelpers.Options());

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
            """), CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        _ = Assert.Throws<ModuleContractException>(() =>
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
        var moduleReference = ModuleCompiler.CreateReference(
            ModuleCompiler.CreateProducer(compilation.Program, ["Read"]));
        var persistedImport = moduleReference.Functions.Single(
            static function => function.Name == "Read");
        Assert.That(persistedImport.DirectNativeImport!.ResolutionMode,
            Is.EqualTo(NativeImportResolutionMode.Dynamic));
        Assert.That(persistedImport.DirectNativeImport.ResolutionPolicy,
            Is.EqualTo(NativeImportResolutionPolicy.CompatibilitySearch));
        Assert.That(persistedImport.DirectNativeImport.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Stdcall));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(compilation.Program, "native-import"));
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibraryWithNativeImports(
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
        var moduleReference = ModuleCompiler.CreateReference(
            ModuleCompiler.CreateProducer(compilation.Program, ["Invoke"]));
        var persistedFunction = moduleReference.Functions.Single();
        var persistedPointer = persistedFunction.Variables[0].Type;
        Assert.That(NativeFunctionPointerTypes.IsType(persistedPointer), Is.True);
        Assert.That(moduleReference.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(persistedPointer)]
            .CallingConvention.ToString(), Is.EqualTo(member));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(compilation.Program, "native-callconv"));
    }

    [TestCase("Terrum.Interop.NativeCallingConvention.Unknown")]
    [TestCase("Other.NativeCallingConvention.Cdecl")]
    public void NativeCallConvRejectsUnselectedEnumMembers(string value)
    {
        var registration = CreateRegistration();
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: registration,
            layoutCoreRegistration: CreateLayoutRegistration(registration));
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
            """), new CompilationOptions {
                LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
            });

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3005"),
            Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [TestCase("[Terrum.Interop.NativeImport(\"example.dll\")] public extern int32 Read();")]
    [TestCase("[Terrum.Interop.NativeImport(\"example.dll\", \"Read\")] public extern int32 Read();")]
    [TestCase("[Terrum.Interop.NativeCallable] public int32 Identity(int32 value) { return value; }")]
    public void NativeDeclarationRolesRequireAnExplicitCoreRegistration(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void NativeAbiIsNotAFunctionPointerTypeArgumentAlias()
    {
        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast, nativeCoreRegistration: CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            unsafe int32 Invoke(fnptr<NativeAbi, int32, int32> callback,
                int32 value) {
                return callback(value);
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void NativeCallConvDoesNotFallBackToSameNamedLocalTypes()
    {
        var registration = CreateRegistration();
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: registration,
            layoutCoreRegistration: CreateLayoutRegistration(registration));
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
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibrary(
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

    internal static NativeCoreRegistrationSet CreateRegistration(
        bool includeForeignNativeInterface = false)
    {
        var conventions = CreateEnum("NativeCallingConvention",
            "Terrum.Interop.NativeCallingConvention",
            ("Unmanaged", 1), ("Cdecl", 2), ("Stdcall", 3), ("Vectorcall", 4));
        var resolutionModes = CreateEnum("NativeImportResolutionMode",
            "Terrum.Interop.NativeImportResolutionMode", ("Link", 1), ("Dynamic", 2));
        var resolutionPolicies = CreateEnum("NativeImportResolutionPolicy",
            "Terrum.Interop.NativeImportResolutionPolicy",
            ("Default", 1), ("CompatibilitySearch", 2));

        var nativeImport = CreateSchema("NativeImport", "Terrum.Interop.NativeImport",
            CoreNativeAttributeRole.NativeImport, conventions.Identity,
            resolutionModes.Identity, resolutionPolicies.Identity);
        var nativeCallable = CreateSchema("NativeCallable", "Terrum.Interop.NativeCallable",
            CoreNativeAttributeRole.NativeCallable, conventions.Identity);
        var nativeCallConv = CreateSchema("NativeCallConv", "Terrum.Interop.NativeCallConv",
            CoreNativeAttributeRole.NativeCallConv, conventions.Identity);
        var nativeExport = CreateSchema("NativeExport", "Terrum.Interop.NativeExport",
            CoreNativeAttributeRole.NativeExport);
        var foreignNativeInterface = includeForeignNativeInterface
            ? CreateSchema("ForeignNativeInterface",
                "Terrum.Interop.ForeignNativeInterface",
                CoreNativeAttributeRole.ForeignNativeInterface)
            : null;
        var layoutParts
            = LayoutCoreRegistrationTestData.CreateDeclarations("core.native");

        var declarations
            = ImmutableArray.CreateBuilder<CoreProviderDeclarationSnapshot>();
        declarations.Add(CoreProviderDeclarationSnapshot.From(conventions));
        declarations.Add(CoreProviderDeclarationSnapshot.From(resolutionModes));
        declarations.Add(CoreProviderDeclarationSnapshot.From(resolutionPolicies));
        declarations.Add(CoreProviderDeclarationSnapshot.From(nativeImport));
        declarations.Add(CoreProviderDeclarationSnapshot.From(nativeCallable));
        declarations.Add(CoreProviderDeclarationSnapshot.From(nativeCallConv));
        declarations.Add(CoreProviderDeclarationSnapshot.From(nativeExport));
        if (foreignNativeInterface is CoreNativeAttributeSchema schema)
        {
            declarations.Add(CoreProviderDeclarationSnapshot.From(schema));
        }
        declarations.AddRange(layoutParts.Declarations);
        var providerBindings = CoreProviderModuleBindings.Create("consumer.module", [
            new CoreProviderModuleOrigin("core.native", "test-revision",
                new CoreProviderSchemaSelection("core.native-registration", 1),
                1, 0, 0),
        ], declarations.ToImmutable());
        return NativeCoreRegistrationSet.Create(providerBindings, conventions,
            resolutionModes, resolutionPolicies, nativeImport, nativeCallable,
            nativeCallConv, nativeExport, foreignNativeInterface);
    }

    private static NativeCoreRegistrationSet CreateSplitProviderRegistration()
    {
        var original = CreateRegistration();
        const string enumProvider = "core.native-enums";
        const string schemaProvider = "core.native-schemas";

        static CoreDeclarationIdentity RebindIdentity(
            CoreDeclarationIdentity identity, string provider)
        {
            return identity with { ProviderIdentity = provider };
        }

        CoreEnumDefinition RebindEnum(CoreEnumDefinition definition)
        {
            var identity = RebindIdentity(definition.Identity, enumProvider);
            return definition with {
                Identity = identity,
                Members = [
                    .. definition.Members.Select(member => member with {
                        Identity = RebindIdentity(member.Identity, enumProvider),
                        DeclaringEnum = identity,
                    }),
                ],
            };
        }

        CoreNativeAttributeSchema RebindSchema(
            CoreNativeAttributeSchema schema)
        {
            return schema with {
                Identity = RebindIdentity(schema.Identity, schemaProvider),
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
        }

        var callingConventions = RebindEnum(original.CallingConventions);
        var resolutionModes = RebindEnum(original.ResolutionModes);
        var resolutionPolicies = RebindEnum(original.ResolutionPolicies);
        var nativeImport = RebindSchema(original.NativeImport);
        var nativeCallable = RebindSchema(original.NativeCallable);
        var nativeCallConv = RebindSchema(original.NativeCallConv);
        var nativeExport = RebindSchema(original.NativeExport!);
        var originalNativeIdentities = original.EnumDefinitions
            .Select(static definition => definition.Identity)
            .Append(original.NativeImport.Identity)
            .Append(original.NativeCallable.Identity)
            .Append(original.NativeCallConv.Identity)
            .Append(original.NativeExport!.Identity)
            .ToImmutableHashSet();
        var otherDeclarations = original.ModuleBindings.Declarations
            .Where(declaration =>
                !originalNativeIdentities.Contains(declaration.Identity));
        ImmutableArray<CoreProviderDeclarationSnapshot> declarations = [
            CoreProviderDeclarationSnapshot.From(callingConventions),
            CoreProviderDeclarationSnapshot.From(resolutionModes),
            CoreProviderDeclarationSnapshot.From(resolutionPolicies),
            CoreProviderDeclarationSnapshot.From(nativeImport),
            CoreProviderDeclarationSnapshot.From(nativeCallable),
            CoreProviderDeclarationSnapshot.From(nativeCallConv),
            CoreProviderDeclarationSnapshot.From(nativeExport),
            .. otherDeclarations,
        ];

        var providers = ImmutableArray.Create(
            new CoreProviderModuleOrigin(enumProvider, "enum-revision",
                new CoreProviderSchemaSelection("native-enum-contract", 1),
                1, 0, 0),
            new CoreProviderModuleOrigin(schemaProvider, "schema-revision",
                new CoreProviderSchemaSelection("native-schema-contract", 1),
                1, 0, 0),
            original.ModuleBindings.Providers.Single(static provider =>
                provider.ModuleIdentity == "core.native"));
        var bindings = CoreProviderModuleBindings.Create(
            original.UsingModuleIdentity, providers, declarations);
        return NativeCoreRegistrationSet.Create(bindings, callingConventions,
            resolutionModes, resolutionPolicies, nativeImport, nativeCallable,
            nativeCallConv, nativeExport);
    }

    internal static LayoutCoreRegistrationSet CreateLayoutRegistration(
        NativeCoreRegistrationSet registration)
        => LayoutCoreRegistrationTestData.CreateForBindings(
            registration.ModuleBindings,
            LayoutCoreRegistrationTestData.CreateDeclarations("core.native"));

    internal static CompilationOptions CreateRegisteredCompilationOptions(
        BoundRuntimePolicy runtimePolicy = BoundRuntimePolicy.FailFast,
        bool includeNativeOutput = false)
    {
        if (includeNativeOutput)
        {
            var (nativeRegistration, outputRegistration)
                = NativeOutputCoreRegistrationTests
                    .CreateCombinedRegistrations();
            return new CompilationOptions {
                RuntimePolicy = runtimePolicy,
                NativeCoreRegistration = nativeRegistration,
                LayoutCoreRegistration =
                    CreateLayoutRegistration(nativeRegistration),
                NativeOutputCoreRegistration = outputRegistration,
            };
        }

        var registration = CreateRegistration();
        return new CompilationOptions {
            RuntimePolicy = runtimePolicy,
            NativeCoreRegistration = registration,
            LayoutCoreRegistration = CreateLayoutRegistration(registration),
        };
    }

    private static CoreEnumDefinition CreateEnum(string exportKey,
        string qualifiedName, params (string Name, int Value)[] members)
    {
        var enumIdentity = Identity("core.native", exportKey, CoreDeclarationKind.EnumType);
        var memberDefinitions = ImmutableArray.CreateBuilder<CoreEnumMemberDefinition>(members.Length);
        foreach (var (Name, Value) in members)
        {
            memberDefinitions.Add(new CoreEnumMemberDefinition(
                Identity("core.native", $"{exportKey}.{Name}", CoreDeclarationKind.EnumMember),
                enumIdentity, Name, Value, IsPublic: true)
            {
                DeclarationKey = $"{exportKey}/member/{Name}",
            });
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
                new BoundEnumMemberIdentity.Core(Identity("sample.enums",
                    "Sample.Palette.Minimum",
                    CoreDeclarationKind.EnumMember)),
                new BoundEnumDeclarationIdentity.Core(identity), "Minimum",
                int.MinValue, IsPublic: true,
                DeclarationKey: "member.minimum"),
            new BoundEnumMemberDefinition(
                new BoundEnumMemberIdentity.Core(Identity("sample.enums",
                    "Sample.Palette.Maximum",
                    CoreDeclarationKind.EnumMember)),
                new BoundEnumDeclarationIdentity.Core(identity), "Maximum",
                int.MaxValue, IsPublic: true,
                DeclarationKey: "member.maximum"),
            new BoundEnumMemberDefinition(
                new BoundEnumMemberIdentity.Core(Identity("sample.enums",
                    "Sample.Palette.MinusOne",
                    CoreDeclarationKind.EnumMember)),
                new BoundEnumDeclarationIdentity.Core(identity), "MinusOne",
                -1, IsPublic: true,
                DeclarationKey: "member.minus-one"));
        return new BoundEnumDefinition(
            new BoundEnumDeclarationIdentity.Core(identity),
            "Sample.Palette", PrimitiveType.Int32,
            members, IsPublic: true, IsPrimary: true, IsFlags: false);
    }

    private static CoreNativeAttributeSchema CreateSchema(string exportKey,
        string qualifiedName, CoreNativeAttributeRole role,
        CoreDeclarationIdentity? callingConventionEnum = null,
        CoreDeclarationIdentity? resolutionModeEnum = null,
        CoreDeclarationIdentity? resolutionPolicyEnum = null)
    {
        ImmutableArray<CoreNativeAttributeArgument> arguments;
        string placement;
        ImmutableHashSet<string> targets;
        switch (role)
        {
            case CoreNativeAttributeRole.NativeImport:
                var allImportContexts = Contexts("callable-link",
                    "callable-dynamic", "data-link", "data-dynamic");
                arguments = [
                    new("LibraryName",
                        CoreNativeAttributeArgumentKind.LibraryNameLiteral,
                        null, true, true, true, allImportContexts, null),
                    new("EntryName",
                        CoreNativeAttributeArgumentKind.EntryNameLiteral,
                        null, false, true, true, allImportContexts,
                        DeclarationNameDefault()),
                    new("ResolutionMode",
                        CoreNativeAttributeArgumentKind.Enum,
                        resolutionModeEnum, false, false, true,
                        allImportContexts,
                        EnumMemberDefault(resolutionModeEnum, "Link")),
                    new("ResolutionPolicy",
                        CoreNativeAttributeArgumentKind.Enum,
                        resolutionPolicyEnum, false, false, true,
                        Contexts("callable-dynamic", "data-dynamic"),
                        EnumMemberDefault(resolutionPolicyEnum, "Default")),
                    new("CallConv",
                        CoreNativeAttributeArgumentKind.Enum,
                        callingConventionEnum, false, false, true,
                        Contexts("callable-link", "callable-dynamic"),
                        EnumMemberDefault(callingConventionEnum, "Unmanaged")),
                ];
                placement = "primary-declaration";
                targets = Contexts("static-extern-callable",
                    "static-extern-data");
                break;

            case CoreNativeAttributeRole.NativeCallable:
                arguments = [
                    new("CallConv",
                        CoreNativeAttributeArgumentKind.Enum,
                        callingConventionEnum, false, false, true,
                        Contexts("method", "interface"),
                        EnumMemberDefault(callingConventionEnum, "Unmanaged")),
                ];
                placement = "primary-declaration";
                targets = Contexts("ordinary-method", "interface");
                break;

            case CoreNativeAttributeRole.NativeCallConv:
                arguments = [
                    new(null, CoreNativeAttributeArgumentKind.Enum,
                        callingConventionEnum, true, true, false,
                        Contexts("fnptr-type-use"), null),
                ];
                placement = "immediate-fnptr-type-use";
                targets = Contexts("fnptr-type-use");
                break;

            case CoreNativeAttributeRole.NativeExport:
                arguments = [
                    new("EntryName",
                        CoreNativeAttributeArgumentKind.EntryNameLiteral,
                        null, false, true, true, Contexts("method", "data"),
                        DeclarationNameDefault()),
                ];
                placement = "primary-declaration";
                targets = Contexts("static-defined-callable",
                    "static-defined-data");
                break;

            case CoreNativeAttributeRole.ForeignNativeInterface:
                arguments = [
                    new("VTableByteOffset",
                        CoreNativeAttributeArgumentKind.UInt32Constant,
                        null, true, false, true, Contexts("interface"), null),
                ];
                placement = "primary-declaration";
                targets = Contexts("interface");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(role));
        }

        return new CoreNativeAttributeSchema(
            Identity("core.native", exportKey,
                CoreDeclarationKind.AttributeSchema),
            qualifiedName, role, arguments, placement, targets,
            callingConventionEnum, resolutionModeEnum,
            resolutionPolicyEnum, GenericArity: 0, IsPublic: true,
            IsPrimary: true, IsRepeatable: false);
    }

    private static CoreNativeAttributeDefault DeclarationNameDefault()
        => new(CoreNativeAttributeDefaultKind.DeclarationName, null);

    private static CoreNativeAttributeDefault EnumMemberDefault(
        CoreDeclarationIdentity? enumType, string name)
    {
        if (enumType is not CoreDeclarationIdentity identity)
        {
            throw new InvalidOperationException(
                "The enum member default requires a selected enum identity.");
        }

        return new(CoreNativeAttributeDefaultKind.EnumMember,
            Identity(identity.ProviderIdentity, $"{identity.ExportKey}.{name}",
                CoreDeclarationKind.EnumMember));
    }

    private static ImmutableHashSet<string> Contexts(params string[] values)
        => values.ToImmutableHashSet(StringComparer.Ordinal);

    private static CoreDeclarationIdentity Identity(string provider, string exportKey,
        CoreDeclarationKind kind) => new(provider, exportKey, kind);
}
