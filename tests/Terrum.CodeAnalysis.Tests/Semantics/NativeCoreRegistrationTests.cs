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

        var nativeImport = CreateSchema("NativeImport", "Terrum.Interop.NativeImport",
            CoreNativeAttributeRole.NativeImport, conventions.Identity,
            resolutionModes.Identity, resolutionPolicies.Identity);
        var nativeCallable = CreateSchema("NativeCallable", "Terrum.Interop.NativeCallable",
            CoreNativeAttributeRole.NativeCallable, conventions.Identity);
        var nativeCallConv = CreateSchema("NativeCallConv", "Terrum.Interop.NativeCallConv",
            CoreNativeAttributeRole.NativeCallConv, conventions.Identity);
        var nativeExport = CreateSchema("NativeExport", "Terrum.Interop.NativeExport",
            CoreNativeAttributeRole.NativeExport);
        var layoutParts
            = LayoutCoreRegistrationTestData.CreateDeclarations("core.native");

        var providerBindings = CoreProviderModuleBindings.Create("consumer.module", [
            new CoreProviderModuleOrigin("core.native", "test-revision",
                new CoreProviderSchemaSelection("core.native-registration", 1),
                1, 0, 0),
        ],
        [
            CoreProviderDeclarationSnapshot.From(conventions),
            CoreProviderDeclarationSnapshot.From(resolutionModes),
            CoreProviderDeclarationSnapshot.From(resolutionPolicies),
            CoreProviderDeclarationSnapshot.From(nativeImport),
            CoreProviderDeclarationSnapshot.From(nativeCallable),
            CoreProviderDeclarationSnapshot.From(nativeCallConv),
            CoreProviderDeclarationSnapshot.From(nativeExport),
            .. layoutParts.Declarations,
        ]);
        return NativeCoreRegistrationSet.Create(providerBindings, conventions,
            resolutionModes, resolutionPolicies, nativeImport, nativeCallable,
            nativeCallConv, nativeExport);
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
        BoundRuntimePolicy runtimePolicy = BoundRuntimePolicy.FailFast)
    {
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
