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

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeOutputPointFunction(IntPtr receiver,
        IntPtr source, IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeAggregatePoint NativeAggregatePointFunction(
        IntPtr receiver, IntPtr source, NativeAggregatePoint fallback);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeAggregatePoint
    {
        public long X;
        public long Y;
    }

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
    public void StandardForeignNativeInterfaceCallBodiesRetainLocalAndImportedTargets()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-native-interface-parameter-surface-1";
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Terrum.Interop.NativeCallable]
                [Terrum.Interop.ForeignNativeInterface(
                    VTableByteOffset: uint32(8))]
                public interface IProbe {
                    public unsafe int32 Read(int32 value);
                }

                int32 Invoke(ptr<void> receiver, int32 value) {
                    unsafe { return IProbe.Read(receiver, value); }
                }

                int32 InvokeStaged(ptr<void> receiver, int32 value) {
                    int32 staged = value;
                    unsafe { return IProbe.Read(receiver, staged); }
                }
                """), new CompilationOptions {
                NativeCoreRegistration = CreateRegistration(
                    includeForeignNativeInterface: true),
            });
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program
            ?? throw new AssertionException("Expected a bound provider.");
        var providerInterface = providerProgram.Interfaces.Single();
        var providerTypeExport = new TrRmSourceTypeExport("", "IProbe",
            "provider/IProbe") {
            GenericArity = 0,
            InterfaceMethodExports = [.. providerInterface.Members.Select(
                member => new TrRmSourceInterfaceMethodExport(
                    member.SourceInputOrdinal,
                    member.SourceDeclarationSpan,
                    $"provider/IProbe.{member.Name}"))],
        };
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "provider.module", schemaIdentity, 1,
            [providerTypeExport]);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var providerTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                providerArtifact,
                TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft));
        var localInvokeRow = Enumerable.Range(1, providerTables.Methods.Count)
            .Single(row => providerTables.Methods.GetName(row) == "Invoke");
        var localBody = providerTables.Bodies.GetRegisterBody(
            providerTables.Methods.GetBodyRow(localInvokeRow),
            providerTables.Contracts.Count);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                localBody.MethodMap) >> 24,
            Is.EqualTo(0x16u));
        var localStagedRow = Enumerable.Range(1, providerTables.Methods.Count)
            .Single(row =>
                providerTables.Methods.GetName(row) == "InvokeStaged");
        var localStagedBody = providerTables.Bodies.GetRegisterBody(
            providerTables.Methods.GetBodyRow(localStagedRow),
            providerTables.Contracts.Count);
        Assert.That(localStagedBody.GetRecord(1)[24], Is.EqualTo((byte)2));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            localStagedBody.GetRecord(1)[26..]), Is.EqualTo((ushort)2));
        Assert.That(localStagedBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
            0x11, 0x11, 0xD6, 0x11, 2, 0,
                0x90, 1, 0x17, 0,
            }));
        var loadedProvider = TrRmExecutableCompiler.LoadStandard(
            providerInput, "provider.module", []);
        AssertForeignInterfaceTarget(loadedProvider, "Invoke", 1);
        AssertForeignInterfaceTarget(loadedProvider, "InvokeStaged", 2);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "provider.module", []);
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                int32 Invoke(ptr<void> receiver, int32 value) {
                    unsafe { return IProbe.Read(receiver, value); }
                }

                int32 InvokeStaged(ptr<void> receiver, int32 value) {
                    int32 staged = value;
                    unsafe { return IProbe.Read(receiver, staged); }
                }
                """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new AssertionException("Expected a bound consumer.");
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "consumer.module", schemaIdentity, 1, []);
        var schema
            = TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft;
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact, schema));
        var invokeRow = Enumerable.Range(1, tables.Methods.Count)
            .Single(row => tables.Methods.GetName(row) == "Invoke");
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(invokeRow), tables.Contracts.Count);
        var methodToken = BinaryPrimitives.ReadUInt32LittleEndian(
            body.MethodMap);
        Assert.That(methodToken >> 24, Is.EqualTo(0x1Cu));
        var importRow = checked((int)(methodToken & 0x00FF_FFFF));
        var importBinding = tables.Imports.GetBindingForImportRow(importRow);
        Assert.That(importBinding.ProducerIdentity,
            Is.EqualTo("provider.module"));
        Assert.That(importBinding.ExportKey,
            Is.EqualTo("provider/IProbe.Read"));
        var callSite = body.GetRecord(1);
        Assert.That(callSite[4], Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            callSite[12..]), Is.EqualTo(2u));
        Assert.That((callSite[20], callSite[21],
                BinaryPrimitives.ReadUInt16LittleEndian(callSite[22..])),
            Is.EqualTo(((byte)2, (byte)1, (ushort)0)));
        Assert.That((callSite[24], callSite[25],
                BinaryPrimitives.ReadUInt16LittleEndian(callSite[26..])),
            Is.EqualTo(((byte)2, (byte)1, (ushort)1)));
        var stagedInvokeRow
            = Enumerable.Range(1, tables.Methods.Count).Single(row =>
                tables.Methods.GetName(row) == "InvokeStaged");
        var stagedBody = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(stagedInvokeRow),
            tables.Contracts.Count);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            stagedBody.MethodMap) >> 24, Is.EqualTo(0x1Cu));
        Assert.That(stagedBody.GetRecord(1)[24], Is.EqualTo((byte)2));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            stagedBody.GetRecord(1)[26..]), Is.EqualTo((ushort)2));

        var providers = new[] { providerInput };
        var malformedIndex = new TrRmArtifactIndex(consumerArtifact,
            schema);
        _ = malformedIndex.TryGetSection(0x0020, out var bodySection,
            out var bodyCount);
        var malformedBodySection = bodySection.ToArray();
        var bodyRowStart = checked((bodyCount + 1) * sizeof(uint)
            + (int)BinaryPrimitives.ReadUInt32LittleEndian(
                malformedBodySection.AsSpan(
                    (tables.Methods.GetBodyRow(invokeRow) - 1)
                        * sizeof(uint), sizeof(uint))));
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedBodySection.AsSpan(bodyRowStart
                + body.MethodMapOffset), 0xFE00_0001);
        var malformedSections
            = new TrRmSection[malformedIndex.SectionCount];
        for (var sectionIndex = 0;
            sectionIndex < malformedIndex.SectionCount; sectionIndex++)
        {
            malformedIndex.GetSection(sectionIndex, out var kind,
                out var flags, out var bytes, out var count);
            malformedSections[sectionIndex] = new TrRmSection(kind, flags,
                checked((uint)count),
                kind == 0x0020 ? malformedBodySection : bytes.ToArray());
        }
        var malformedArtifact
            = TrRmArtifactWriter.Encode(malformedSections, schema);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    malformedArtifact), "consumer.module", providers));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                consumerArtifact), "consumer.module", providers);
        AssertForeignInterfaceTarget(loaded, "Invoke", 1);
        AssertForeignInterfaceTarget(loaded, "InvokeStaged", 2);
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            "consumer.module", schemaIdentity, 1, []);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reemittedArtifact), "consumer.module", providers);
        AssertForeignInterfaceTarget(reloaded, "Invoke", 1);
        AssertForeignInterfaceTarget(reloaded, "InvokeStaged", 2);

        static void AssertForeignInterfaceTarget(BoundProgram program,
            string functionName, int expectedArgumentVariable)
        {
            var invoke = program.Functions.Single(function =>
                function.Name == functionName);
            BoundCall? foreignCall = null;
            Assert.That(BoundTreeTraversal.VisitExpressions(invoke.Body,
                expression =>
                {
                    if (expression is BoundCall {
                            ForeignInterfaceCall: not null,
                        } call)
                    {
                        foreignCall = call;
                    }
                }), Is.True);
            Assert.That(foreignCall?.Function,
                Is.EqualTo(BoundCall.ForeignInterfaceFunction));
            Assert.That(foreignCall?.ForeignInterfaceCall?.Member
                .DeclaringInterface.ModuleImport?.ProducerIdentity,
                Is.EqualTo("provider.module"));
            Assert.That(foreignCall?.ForeignInterfaceCall?.Member
                .MemberOrdinal, Is.Zero);
            Assert.That(foreignCall?.ForeignInterfaceCall
                ?.Receiver.Type, Is.EqualTo(PrimitiveType.NativeRawPointer));
            Assert.That(foreignCall?.Arguments.Single().Type,
                Is.EqualTo(PrimitiveType.Int32));
            Assert.That(((BoundRead)foreignCall!.Arguments.Single()).Variable,
                Is.EqualTo(expectedArgumentVariable));
            Assert.That(foreignCall?.IsUnsafeAuthorized, Is.True);
        }
    }

    [Test]
    public void StandardForeignNativeInterfaceVoidCallBodiesRoundTrip()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-native-interface-contracts-1";
        const string providerSource = """
            [Terrum.Interop.NativeCallable]
            [Terrum.Interop.ForeignNativeInterface(
                VTableByteOffset: uint32(0))]
            public interface IProbe {
                public unsafe void Reset();
            }

            void Invoke(ptr<void> receiver) {
                unsafe {
                    IProbe.Reset(receiver);
                    return;
                }
            }
            """;
        const string consumerSource = """
            void Invoke(ptr<void> receiver) {
                unsafe {
                    IProbe.Reset(receiver);
                    return;
                }
            }
            """;
        var options = new CompilationOptions {
            NativeCoreRegistration = CreateRegistration(
                includeForeignNativeInterface: true),
        };
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource), options);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program
            ?? throw new AssertionException("Expected a bound provider.");
        var providerInterface = providerProgram.Interfaces.Single();
        var typeExport = new TrRmSourceTypeExport("", "IProbe",
            "provider/IProbe") {
            GenericArity = 0,
            InterfaceMethodExports = [.. providerInterface.Members.Select(
                member => new TrRmSourceInterfaceMethodExport(
                    member.SourceInputOrdinal,
                    member.SourceDeclarationSpan,
                    $"provider/IProbe.{member.Name}"))],
        };
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "provider.module", schemaIdentity, 1,
            [typeExport]);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var schema = TrRmArtifactSchema.ForeignNativeInterfaceDraft;
        var providerTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                providerArtifact, schema));
        var providerInvokeRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row) == "Invoke");
        var providerBody = providerTables.Bodies.GetRegisterBody(
            providerTables.Methods.GetBodyRow(providerInvokeRow),
            providerTables.Contracts.Count);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            providerBody.MethodMap) >> 24, Is.EqualTo(0x16u));
        Assert.That(providerBody.GetRecord(1)[5], Is.EqualTo((byte)0));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            providerBody.GetRecord(1)[12..]), Is.EqualTo(1u));
        Assert.That(providerBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] { 0x90, 1, 0x18, 0 }));

        var loadedProvider = TrRmExecutableCompiler.LoadStandard(
            providerInput, "provider.module", []);
        AssertVoidForeignInterfaceCall(loadedProvider, expectedProvider: null);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "provider.module", []);
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(consumerSource), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerCompilation.Program
                ?? throw new AssertionException("Expected a bound consumer."),
            "consumer.module", schemaIdentity, 1, []);
        var consumerInput = new TrRmStandardArtifactInput(schemaIdentity,
            consumerArtifact);
        var consumerTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact, schema));
        var consumerInvokeRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row) == "Invoke");
        var consumerBody = consumerTables.Bodies.GetRegisterBody(
            consumerTables.Methods.GetBodyRow(consumerInvokeRow),
            consumerTables.Contracts.Count);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            consumerBody.MethodMap) >> 24, Is.EqualTo(0x1Cu));
        Assert.That(consumerBody.GetRecord(1)[5], Is.EqualTo((byte)0));
        Assert.That(consumerBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] { 0x90, 1, 0x18, 0 }));

        var providers = new[] { providerInput };
        var loadedConsumer = TrRmExecutableCompiler.LoadStandard(
            consumerInput, "consumer.module", providers);
        AssertVoidForeignInterfaceCall(loadedConsumer, "provider.module");
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(
            loadedConsumer, "consumer.module", schemaIdentity, 1, []);
        var reloadedConsumer = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reemittedArtifact), "consumer.module", providers);
        AssertVoidForeignInterfaceCall(reloadedConsumer, "provider.module");

        static void AssertVoidForeignInterfaceCall(BoundProgram program,
            string? expectedProvider)
        {
            var invoke = program.Functions.Single(static function =>
                function.Name == "Invoke");
            BoundCall? foreignCall = null;
            Assert.That(BoundTreeTraversal.VisitExpressions(invoke.Body,
                expression =>
                {
                    if (expression is BoundCall {
                            ForeignInterfaceCall: not null,
                        } call)
                    {
                        foreignCall = call;
                    }
                }), Is.True);
            var selectedCall = foreignCall
                ?? throw new AssertionException("Expected an FNI call.");
            Assert.That(selectedCall.Type, Is.EqualTo(PrimitiveType.Void));
            Assert.That(selectedCall.Arguments, Is.Empty);
            Assert.That(selectedCall.ForeignInterfaceCall?.Member
                .DeclaringInterface.ModuleImport?.ProducerIdentity,
                Is.EqualTo(expectedProvider));
            Assert.That(selectedCall.IsUnsafeAuthorized, Is.True);
        }
    }

    [Test]
    public void StandardForeignNativeInterfaceCallBodiesRetainOutputReservations()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-native-interface-parameter-surface-1";
        var (native, output) = NativeOutputCoreRegistrationTests
            .CreateCombinedRegistrations(includeForeignNativeInterface: true);
        var options = new CompilationOptions {
            NativeCoreRegistration = native,
            LayoutCoreRegistration = CreateLayoutRegistration(native),
            NativeOutputCoreRegistration = output,
        };
        const string providerSource = """
            [Terrum.Interop.NativeCallable(
                CallConv: Terrum.Interop.NativeCallingConvention.Cdecl)]
            [Terrum.Interop.ForeignNativeInterface(
                VTableByteOffset: uint32(0))]
            public interface IProbe {
                [param(value): Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonNegative)]
                public unsafe int32 TryRead(ptr<int32> source,
                    [Terrum.CodeAnalysis.DependsOn(this, nameof(source))]
                    [Terrum.CodeAnalysis.ForwardsBacking(
                        this, nameof(source))]
                    write ptr<int32> value);

                [param(value): Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                public unsafe int32 TryReadNonZero(ptr<int32> source,
                    [Terrum.CodeAnalysis.DependsOn(this, nameof(source))]
                    [Terrum.CodeAnalysis.ForwardsBacking(
                        this, nameof(source))]
                    write ptr<int32> value);

                [param(value): Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                public unsafe uint32 TryReadUInt32(ptr<uint32> source,
                    [Terrum.CodeAnalysis.DependsOn(this, nameof(source))]
                    [Terrum.CodeAnalysis.ForwardsBacking(
                        this, nameof(source))]
                    write ptr<uint32> value);

                [param(value): Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonNegative)]
                public unsafe int32 TryReadFloat64(ptr<int32> source,
                    [Terrum.CodeAnalysis.DependsOn(this, nameof(source))]
                    [Terrum.CodeAnalysis.ForwardsBacking(
                        this, nameof(source))]
                    write ptr<float64> value);

                [param(value): Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                public unsafe int32 TryReadUInt8(ptr<uint8> source,
                    [Terrum.CodeAnalysis.DependsOn(this, nameof(source))]
                    [Terrum.CodeAnalysis.ForwardsBacking(
                        this, nameof(source))]
                    write ptr<uint8> value);

                [param(value): Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonNegative)]
                public unsafe int32 TryReadBool(ptr<int32> source,
                    [Terrum.CodeAnalysis.DependsOn(this, nameof(source))]
                    [Terrum.CodeAnalysis.ForwardsBacking(
                        this, nameof(source))]
                    write ptr<bool> value);

                [param(value): Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonNegative)]
                public unsafe int32 TryReadInt64(ptr<int32> source,
                    [Terrum.CodeAnalysis.DependsOn(this, nameof(source))]
                    [Terrum.CodeAnalysis.ForwardsBacking(
                        this, nameof(source))]
                    write ptr<int64> value);

                [param(value): Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonNegative)]
                public unsafe int32 TryReadFloat32(ptr<int32> source,
                    [Terrum.CodeAnalysis.DependsOn(this, nameof(source))]
                    [Terrum.CodeAnalysis.ForwardsBacking(
                        this, nameof(source))]
                    write ptr<float32> value);

                [param(value): Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonNegative)]
                public unsafe int32 TryReadRune(ptr<int32> source,
                    [Terrum.CodeAnalysis.DependsOn(this, nameof(source))]
                    [Terrum.CodeAnalysis.ForwardsBacking(
                        this, nameof(source))]
                    write ptr<rune> value);

                [param(value): Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonNegative)]
                public unsafe int32 TryReadNuint(ptr<int32> source,
                    [Terrum.CodeAnalysis.DependsOn(this, nameof(source))]
                    [Terrum.CodeAnalysis.ForwardsBacking(
                        this, nameof(source))]
                    write ptr<nuint> value);

                public unsafe int32 GetIndex(ptr<int32> source);
                public unsafe int32 UseIndex(int32 index);
            }

            int32 Invoke(ptr<void> receiver, ptr<int32> source) {
                init int32 value;
                unsafe { return IProbe.TryRead(
                    receiver, source, &init value); }
            }

            int32 InvokeCorrelated(ptr<void> receiver, ptr<int32> source) {
                init int32 value;
                unsafe {
                    int32 status = IProbe.TryRead(
                        receiver, source, &init value);
                    if (status >= 0) { return value; }
                    return status;
                }
            }

            int32 InvokeCorrelatedReversed(ptr<void> receiver,
                ptr<int32> source) {
                init int32 value;
                unsafe {
                    int32 status = IProbe.TryRead(
                        receiver, source, &init value);
                    if (0 <= status) { return value; }
                    return status;
                }
            }

            int32 InvokeCorrelatedComplement(ptr<void> receiver,
                ptr<int32> source) {
                init int32 value;
                unsafe {
                    int32 status = IProbe.TryRead(
                        receiver, source, &init value);
                    if (status < 0) { return status; }
                    return value;
                }
            }

            int32 InvokeNonZeroInt32(ptr<void> receiver,
                ptr<int32> source) {
                init int32 value;
                unsafe {
                    int32 status = IProbe.TryReadNonZero(
                        receiver, source, &init value);
                    if (status != 0) { return value; }
                    return status;
                }
            }

            int32 InvokeNonZeroInt32Negated(ptr<void> receiver,
                ptr<int32> source) {
                init int32 value;
                unsafe {
                    int32 status = IProbe.TryReadNonZero(
                        receiver, source, &init value);
                    if (!(status == 0)) { return value; }
                    return status;
                }
            }

            int32 InvokeNonZeroInt32Complement(ptr<void> receiver,
                ptr<int32> source) {
                init int32 value;
                unsafe {
                    int32 status = IProbe.TryReadNonZero(
                        receiver, source, &init value);
                    if (status == 0) { return status; }
                    return value;
                }
            }

            uint32 InvokeNonZero(ptr<void> receiver, ptr<uint32> source) {
                init uint32 value;
                unsafe {
                    uint32 status = IProbe.TryReadUInt32(
                        receiver, source, &init value);
                    if (status != 0) { return value; }
                    return status;
                }
            }

            float64 InvokeFloat64(ptr<void> receiver, ptr<int32> source,
                float64 fallback) {
                init float64 value;
                unsafe {
                    int32 status = IProbe.TryReadFloat64(
                        receiver, source, &init value);
                    if (status >= 0) { return value; }
                    return fallback;
                }
            }

            float64 InvokeFloat64Complement(ptr<void> receiver,
                ptr<int32> source, float64 fallback) {
                init float64 value;
                unsafe {
                    int32 status = IProbe.TryReadFloat64(
                        receiver, source, &init value);
                    if (status < 0) { return fallback; }
                    return value;
                }
            }

            uint8 InvokeUInt8(ptr<void> receiver, ptr<uint8> source,
                uint8 fallback) {
                init uint8 value;
                unsafe {
                    int32 status = IProbe.TryReadUInt8(
                        receiver, source, &init value);
                    if (status != 0) { return value; }
                    return fallback;
                }
            }

            bool InvokeBool(ptr<void> receiver, ptr<int32> source,
                bool fallback) {
                init bool value;
                unsafe {
                    int32 status = IProbe.TryReadBool(
                        receiver, source, &init value);
                    if (status >= 0) { return value; }
                    return fallback;
                }
            }

            int64 InvokeInt64(ptr<void> receiver, ptr<int32> source,
                int64 fallback) {
                init int64 value;
                unsafe {
                    int32 status = IProbe.TryReadInt64(
                        receiver, source, &init value);
                    if (status >= 0) { return value; }
                    return fallback;
                }
            }

            float32 InvokeFloat32(ptr<void> receiver, ptr<int32> source,
                float32 fallback) {
                init float32 value;
                unsafe {
                    int32 status = IProbe.TryReadFloat32(
                        receiver, source, &init value);
                    if (status >= 0) { return value; }
                    return fallback;
                }
            }

            rune InvokeRune(ptr<void> receiver, ptr<int32> source,
                rune fallback) {
                init rune value;
                unsafe {
                    int32 status = IProbe.TryReadRune(
                        receiver, source, &init value);
                    if (status >= 0) { return value; }
                    return fallback;
                }
            }

            nuint InvokeNuint(ptr<void> receiver, ptr<int32> source,
                nuint fallback) {
                init nuint value;
                unsafe {
                    int32 status = IProbe.TryReadNuint(
                        receiver, source, &init value);
                    if (status >= 0) { return value; }
                    return fallback;
                }
            }

            int32 InvokeNested(ptr<void> receiver, ptr<int32> source) {
                unsafe {
                    return IProbe.UseIndex(
                        receiver, IProbe.GetIndex(receiver, source));
                }
            }
            """;
        const string consumerSource = """
            int32 Invoke(ptr<void> receiver, ptr<int32> source) {
                init int32 value;
                unsafe { return IProbe.TryRead(
                    receiver, source, &init value); }
            }

            int32 InvokeCorrelated(ptr<void> receiver, ptr<int32> source) {
                init int32 value;
                unsafe {
                    int32 status = IProbe.TryRead(
                        receiver, source, &init value);
                    if (status >= 0) { return value; }
                    return status;
                }
            }

            int32 InvokeCorrelatedReversed(ptr<void> receiver,
                ptr<int32> source) {
                init int32 value;
                unsafe {
                    int32 status = IProbe.TryRead(
                        receiver, source, &init value);
                    if (0 <= status) { return value; }
                    return status;
                }
            }

            int32 InvokeCorrelatedComplement(ptr<void> receiver,
                ptr<int32> source) {
                init int32 value;
                unsafe {
                    int32 status = IProbe.TryRead(
                        receiver, source, &init value);
                    if (status < 0) { return status; }
                    return value;
                }
            }

            int32 InvokeNonZeroInt32(ptr<void> receiver,
                ptr<int32> source) {
                init int32 value;
                unsafe {
                    int32 status = IProbe.TryReadNonZero(
                        receiver, source, &init value);
                    if (status != 0) { return value; }
                    return status;
                }
            }

            int32 InvokeNonZeroInt32Negated(ptr<void> receiver,
                ptr<int32> source) {
                init int32 value;
                unsafe {
                    int32 status = IProbe.TryReadNonZero(
                        receiver, source, &init value);
                    if (!(status == 0)) { return value; }
                    return status;
                }
            }

            int32 InvokeNonZeroInt32Complement(ptr<void> receiver,
                ptr<int32> source) {
                init int32 value;
                unsafe {
                    int32 status = IProbe.TryReadNonZero(
                        receiver, source, &init value);
                    if (status == 0) { return status; }
                    return value;
                }
            }

            uint32 InvokeNonZero(ptr<void> receiver, ptr<uint32> source) {
                init uint32 value;
                unsafe {
                    uint32 status = IProbe.TryReadUInt32(
                        receiver, source, &init value);
                    if (status != 0) { return value; }
                    return status;
                }
            }

            float64 InvokeFloat64(ptr<void> receiver, ptr<int32> source,
                float64 fallback) {
                init float64 value;
                unsafe {
                    int32 status = IProbe.TryReadFloat64(
                        receiver, source, &init value);
                    if (status >= 0) { return value; }
                    return fallback;
                }
            }

            float64 InvokeFloat64Complement(ptr<void> receiver,
                ptr<int32> source, float64 fallback) {
                init float64 value;
                unsafe {
                    int32 status = IProbe.TryReadFloat64(
                        receiver, source, &init value);
                    if (status < 0) { return fallback; }
                    return value;
                }
            }

            uint8 InvokeUInt8(ptr<void> receiver, ptr<uint8> source,
                uint8 fallback) {
                init uint8 value;
                unsafe {
                    int32 status = IProbe.TryReadUInt8(
                        receiver, source, &init value);
                    if (status != 0) { return value; }
                    return fallback;
                }
            }

            bool InvokeBool(ptr<void> receiver, ptr<int32> source,
                bool fallback) {
                init bool value;
                unsafe {
                    int32 status = IProbe.TryReadBool(
                        receiver, source, &init value);
                    if (status >= 0) { return value; }
                    return fallback;
                }
            }

            int64 InvokeInt64(ptr<void> receiver, ptr<int32> source,
                int64 fallback) {
                init int64 value;
                unsafe {
                    int32 status = IProbe.TryReadInt64(
                        receiver, source, &init value);
                    if (status >= 0) { return value; }
                    return fallback;
                }
            }

            float32 InvokeFloat32(ptr<void> receiver, ptr<int32> source,
                float32 fallback) {
                init float32 value;
                unsafe {
                    int32 status = IProbe.TryReadFloat32(
                        receiver, source, &init value);
                    if (status >= 0) { return value; }
                    return fallback;
                }
            }

            rune InvokeRune(ptr<void> receiver, ptr<int32> source,
                rune fallback) {
                init rune value;
                unsafe {
                    int32 status = IProbe.TryReadRune(
                        receiver, source, &init value);
                    if (status >= 0) { return value; }
                    return fallback;
                }
            }

            nuint InvokeNuint(ptr<void> receiver, ptr<int32> source,
                nuint fallback) {
                init nuint value;
                unsafe {
                    int32 status = IProbe.TryReadNuint(
                        receiver, source, &init value);
                    if (status >= 0) { return value; }
                    return fallback;
                }
            }

            int32 InvokeNested(ptr<void> receiver, ptr<int32> source) {
                unsafe {
                    return IProbe.UseIndex(
                        receiver, IProbe.GetIndex(receiver, source));
                }
            }
            """;
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource), options);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program
            ?? throw new AssertionException("Expected a bound provider.");
        var providerInterface = providerProgram.Interfaces.Single();
        var typeExport = new TrRmSourceTypeExport("", "IProbe",
            "provider/IProbe") {
            GenericArity = 0,
            InterfaceMethodExports = [.. providerInterface.Members.Select(
                member => new TrRmSourceInterfaceMethodExport(
                    member.SourceInputOrdinal,
                    member.SourceDeclarationSpan,
                    $"provider/IProbe.{member.Name}"))],
        };
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "provider.module", schemaIdentity, 1,
            [typeExport]);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var schema
            = TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft;
        var providerTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                providerArtifact, schema));
        var providerInvokeRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row) == "Invoke");
        var providerBody = providerTables.Bodies.GetRegisterBody(
            providerTables.Methods.GetBodyRow(providerInvokeRow),
            providerTables.Contracts.Count);
        var localMethodToken = BinaryPrimitives.ReadUInt32LittleEndian(
            providerBody.MethodMap);
        Assert.That(localMethodToken >> 24, Is.EqualTo(0x16u));
        AssertOutputReservationCallSite(providerBody);
        var providerCorrelatedRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row) == "InvokeCorrelated");
        var providerCorrelatedBody = providerTables.Bodies.GetRegisterBody(
            providerTables.Methods.GetBodyRow(providerCorrelatedRow),
            providerTables.Contracts.Count);
        AssertCorrelatedOutputBody(providerCorrelatedBody, 6);
        var providerCorrelatedReversedRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row)
                    == "InvokeCorrelatedReversed");
        var providerCorrelatedReversedBody
            = providerTables.Bodies.GetRegisterBody(
                providerTables.Methods.GetBodyRow(
                    providerCorrelatedReversedRow),
                providerTables.Contracts.Count);
        AssertCorrelatedOutputBody(providerCorrelatedReversedBody, 6);
        var providerCorrelatedComplementRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row)
                    == "InvokeCorrelatedComplement");
        var providerCorrelatedComplementBody
            = providerTables.Bodies.GetRegisterBody(
                providerTables.Methods.GetBodyRow(
                    providerCorrelatedComplementRow),
                providerTables.Contracts.Count);
        AssertCorrelatedOutputBody(providerCorrelatedComplementBody, 1,
            successOnTrue: false);
        var providerNonZeroInt32Row
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row) == "InvokeNonZeroInt32");
        var providerNonZeroInt32Body = providerTables.Bodies.GetRegisterBody(
            providerTables.Methods.GetBodyRow(providerNonZeroInt32Row),
            providerTables.Contracts.Count);
        AssertCorrelatedOutputBody(providerNonZeroInt32Body, 13);
        var providerNonZeroInt32NegatedRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row)
                    == "InvokeNonZeroInt32Negated");
        var providerNonZeroInt32NegatedBody
            = providerTables.Bodies.GetRegisterBody(
                providerTables.Methods.GetBodyRow(
                    providerNonZeroInt32NegatedRow),
                providerTables.Contracts.Count);
        AssertCorrelatedOutputBody(providerNonZeroInt32NegatedBody, 13);
        var providerNonZeroInt32ComplementRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row)
                    == "InvokeNonZeroInt32Complement");
        var providerNonZeroInt32ComplementBody
            = providerTables.Bodies.GetRegisterBody(
                providerTables.Methods.GetBodyRow(
                    providerNonZeroInt32ComplementRow),
                providerTables.Contracts.Count);
        AssertCorrelatedOutputBody(providerNonZeroInt32ComplementBody, 2,
            successOnTrue: false);
        var providerNonZeroRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row) == "InvokeNonZero");
        var providerNonZeroBody = providerTables.Bodies.GetRegisterBody(
            providerTables.Methods.GetBodyRow(providerNonZeroRow),
            providerTables.Contracts.Count);
        AssertCorrelatedOutputBody(providerNonZeroBody, 13);
        var providerFloat64Row
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row) == "InvokeFloat64");
        var providerFloat64Body = providerTables.Bodies.GetRegisterBody(
            providerTables.Methods.GetBodyRow(providerFloat64Row),
            providerTables.Contracts.Count);
        AssertCorrelatedOutputBody(providerFloat64Body, 6, 3, 4, 2);
        var providerFloat64ComplementRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row)
                    == "InvokeFloat64Complement");
        var providerFloat64ComplementBody
            = providerTables.Bodies.GetRegisterBody(
                providerTables.Methods.GetBodyRow(
                    providerFloat64ComplementRow),
                providerTables.Contracts.Count);
        AssertCorrelatedOutputBody(providerFloat64ComplementBody, 1, 3, 4,
            2, successOnTrue: false);
        var providerUInt8Row
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row) == "InvokeUInt8");
        var providerUInt8Body = providerTables.Bodies.GetRegisterBody(
            providerTables.Methods.GetBodyRow(providerUInt8Row),
            providerTables.Contracts.Count);
        AssertCorrelatedOutputBody(providerUInt8Body, 13, 3, 4, 2);
        var providerBoolRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row) == "InvokeBool");
        var providerBoolBody = providerTables.Bodies.GetRegisterBody(
            providerTables.Methods.GetBodyRow(providerBoolRow),
            providerTables.Contracts.Count);
        AssertCorrelatedOutputBody(providerBoolBody, 6, 3, 4, 2);
        var providerInt64Row
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row) == "InvokeInt64");
        var providerInt64Body = providerTables.Bodies.GetRegisterBody(
            providerTables.Methods.GetBodyRow(providerInt64Row),
            providerTables.Contracts.Count);
        AssertCorrelatedOutputBody(providerInt64Body, 6, 3, 4, 2);
        var providerFloat32Row
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row) == "InvokeFloat32");
        var providerFloat32Body = providerTables.Bodies.GetRegisterBody(
            providerTables.Methods.GetBodyRow(providerFloat32Row),
            providerTables.Contracts.Count);
        AssertCorrelatedOutputBody(providerFloat32Body, 6, 3, 4, 2);
        var providerRuneRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row) == "InvokeRune");
        var providerRuneBody = providerTables.Bodies.GetRegisterBody(
            providerTables.Methods.GetBodyRow(providerRuneRow),
            providerTables.Contracts.Count);
        AssertCorrelatedOutputBody(providerRuneBody, 6, 3, 4, 2);
        var providerNuintRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row) == "InvokeNuint");
        var providerNuintBody = providerTables.Bodies.GetRegisterBody(
            providerTables.Methods.GetBodyRow(providerNuintRow),
            providerTables.Contracts.Count);
        AssertCorrelatedOutputBody(providerNuintBody, 6, 3, 4, 2);
        var providerNestedRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row) == "InvokeNested");
        var providerNestedBody = providerTables.Bodies.GetRegisterBody(
            providerTables.Methods.GetBodyRow(providerNestedRow),
            providerTables.Contracts.Count);
        AssertNestedFniInputBody(providerNestedBody, 0x16);

        var loadedProvider = TrRmExecutableCompiler.LoadStandard(
            providerInput, "provider.module", []);
        AssertForeignOutputCall(loadedProvider, expectedProvider: null);
        AssertCorrelatedOutputCall(loadedProvider, null,
            "InvokeCorrelated", TokenKind.GreaterEquals, PrimitiveType.Int32);
        AssertCorrelatedOutputCall(loadedProvider, null,
            "InvokeCorrelatedReversed", TokenKind.GreaterEquals,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(loadedProvider, null,
            "InvokeCorrelatedComplement", TokenKind.Less,
            PrimitiveType.Int32, successOnTrue: false);
        AssertCorrelatedOutputCall(loadedProvider, null,
            "InvokeNonZeroInt32", TokenKind.BangEquals,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(loadedProvider, null,
            "InvokeNonZeroInt32Negated", TokenKind.BangEquals,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(loadedProvider, null,
            "InvokeNonZeroInt32Complement", TokenKind.EqualsEquals,
            PrimitiveType.Int32, successOnTrue: false);
        AssertCorrelatedOutputCall(loadedProvider, null,
            "InvokeNonZero", TokenKind.BangEquals, PrimitiveType.UInt32);
        AssertCorrelatedOutputCall(loadedProvider, null,
            "InvokeFloat64", TokenKind.GreaterEquals, PrimitiveType.Float64,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(loadedProvider, null,
            "InvokeFloat64Complement", TokenKind.Less,
            PrimitiveType.Float64, PrimitiveType.Int32,
            successOnTrue: false);
        AssertCorrelatedOutputCall(loadedProvider, null,
            "InvokeUInt8", TokenKind.BangEquals, PrimitiveType.UInt8,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(loadedProvider, null,
            "InvokeBool", TokenKind.GreaterEquals, PrimitiveType.Bool,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(loadedProvider, null,
            "InvokeInt64", TokenKind.GreaterEquals, PrimitiveType.Int64,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(loadedProvider, null,
            "InvokeFloat32", TokenKind.GreaterEquals, PrimitiveType.Float32,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(loadedProvider, null,
            "InvokeRune", TokenKind.GreaterEquals, PrimitiveType.Rune,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(loadedProvider, null,
            "InvokeNuint", TokenKind.GreaterEquals, PrimitiveType.NativeUInt,
            PrimitiveType.Int32);
        AssertNestedFniInputCall(loadedProvider, null);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "provider.module", []);
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(consumerSource), reference, options);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new AssertionException("Expected a bound consumer.");
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "consumer.module", schemaIdentity, 1, []);
        var consumerInput = new TrRmStandardArtifactInput(schemaIdentity,
            consumerArtifact);
        var consumerTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact, schema));
        var consumerInvokeRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row) == "Invoke");
        var consumerBody = consumerTables.Bodies.GetRegisterBody(
            consumerTables.Methods.GetBodyRow(consumerInvokeRow),
            consumerTables.Contracts.Count);
        var importedMethodToken = BinaryPrimitives.ReadUInt32LittleEndian(
            consumerBody.MethodMap);
        Assert.That(importedMethodToken >> 24, Is.EqualTo(0x1Cu));
        AssertOutputReservationCallSite(consumerBody);
        var consumerCorrelatedRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row) == "InvokeCorrelated");
        var consumerCorrelatedBody = consumerTables.Bodies.GetRegisterBody(
            consumerTables.Methods.GetBodyRow(consumerCorrelatedRow),
            consumerTables.Contracts.Count);
        AssertCorrelatedOutputBody(consumerCorrelatedBody, 6);
        var consumerCorrelatedReversedRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row)
                    == "InvokeCorrelatedReversed");
        var consumerCorrelatedReversedBody
            = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(
                    consumerCorrelatedReversedRow),
                consumerTables.Contracts.Count);
        AssertCorrelatedOutputBody(consumerCorrelatedReversedBody, 6);
        var consumerCorrelatedComplementRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row)
                    == "InvokeCorrelatedComplement");
        var consumerCorrelatedComplementBody
            = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(
                    consumerCorrelatedComplementRow),
                consumerTables.Contracts.Count);
        AssertCorrelatedOutputBody(consumerCorrelatedComplementBody, 1,
            successOnTrue: false);
        var consumerNonZeroInt32Row
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row) == "InvokeNonZeroInt32");
        var consumerNonZeroInt32Body = consumerTables.Bodies.GetRegisterBody(
            consumerTables.Methods.GetBodyRow(consumerNonZeroInt32Row),
            consumerTables.Contracts.Count);
        AssertCorrelatedOutputBody(consumerNonZeroInt32Body, 13);
        var consumerNonZeroInt32NegatedRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row)
                    == "InvokeNonZeroInt32Negated");
        var consumerNonZeroInt32NegatedBody
            = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(
                    consumerNonZeroInt32NegatedRow),
                consumerTables.Contracts.Count);
        AssertCorrelatedOutputBody(consumerNonZeroInt32NegatedBody, 13);
        var consumerNonZeroInt32ComplementRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row)
                    == "InvokeNonZeroInt32Complement");
        var consumerNonZeroInt32ComplementBody
            = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(
                    consumerNonZeroInt32ComplementRow),
                consumerTables.Contracts.Count);
        AssertCorrelatedOutputBody(consumerNonZeroInt32ComplementBody, 2,
            successOnTrue: false);
        var consumerNonZeroRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row) == "InvokeNonZero");
        var consumerNonZeroBody = consumerTables.Bodies.GetRegisterBody(
            consumerTables.Methods.GetBodyRow(consumerNonZeroRow),
            consumerTables.Contracts.Count);
        AssertCorrelatedOutputBody(consumerNonZeroBody, 13);
        var consumerFloat64Row
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row) == "InvokeFloat64");
        var consumerFloat64Body = consumerTables.Bodies.GetRegisterBody(
            consumerTables.Methods.GetBodyRow(consumerFloat64Row),
            consumerTables.Contracts.Count);
        AssertCorrelatedOutputBody(consumerFloat64Body, 6, 3, 4, 2);
        var consumerFloat64ComplementRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row)
                    == "InvokeFloat64Complement");
        var consumerFloat64ComplementBody
            = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(
                    consumerFloat64ComplementRow),
                consumerTables.Contracts.Count);
        AssertCorrelatedOutputBody(consumerFloat64ComplementBody, 1, 3, 4,
            2, successOnTrue: false);
        var consumerUInt8Row
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row) == "InvokeUInt8");
        var consumerUInt8Body = consumerTables.Bodies.GetRegisterBody(
            consumerTables.Methods.GetBodyRow(consumerUInt8Row),
            consumerTables.Contracts.Count);
        AssertCorrelatedOutputBody(consumerUInt8Body, 13, 3, 4, 2);
        var consumerBoolRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row) == "InvokeBool");
        var consumerBoolBody = consumerTables.Bodies.GetRegisterBody(
            consumerTables.Methods.GetBodyRow(consumerBoolRow),
            consumerTables.Contracts.Count);
        AssertCorrelatedOutputBody(consumerBoolBody, 6, 3, 4, 2);
        var consumerInt64Row
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row) == "InvokeInt64");
        var consumerInt64Body = consumerTables.Bodies.GetRegisterBody(
            consumerTables.Methods.GetBodyRow(consumerInt64Row),
            consumerTables.Contracts.Count);
        AssertCorrelatedOutputBody(consumerInt64Body, 6, 3, 4, 2);
        var consumerFloat32Row
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row) == "InvokeFloat32");
        var consumerFloat32Body = consumerTables.Bodies.GetRegisterBody(
            consumerTables.Methods.GetBodyRow(consumerFloat32Row),
            consumerTables.Contracts.Count);
        AssertCorrelatedOutputBody(consumerFloat32Body, 6, 3, 4, 2);
        var consumerRuneRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row) == "InvokeRune");
        var consumerRuneBody = consumerTables.Bodies.GetRegisterBody(
            consumerTables.Methods.GetBodyRow(consumerRuneRow),
            consumerTables.Contracts.Count);
        AssertCorrelatedOutputBody(consumerRuneBody, 6, 3, 4, 2);
        var consumerNuintRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row) == "InvokeNuint");
        var consumerNuintBody = consumerTables.Bodies.GetRegisterBody(
            consumerTables.Methods.GetBodyRow(consumerNuintRow),
            consumerTables.Contracts.Count);
        AssertCorrelatedOutputBody(consumerNuintBody, 6, 3, 4, 2);
        var consumerNestedRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row) == "InvokeNested");
        var consumerNestedBody = consumerTables.Bodies.GetRegisterBody(
            consumerTables.Methods.GetBodyRow(consumerNestedRow),
            consumerTables.Contracts.Count);
        AssertNestedFniInputBody(consumerNestedBody, 0x1C);

        var providers = new[] { providerInput };
        var malformedIndex = new TrRmArtifactIndex(consumerArtifact,
            schema);
        _ = malformedIndex.TryGetSection(0x0020, out var bodySection,
            out var bodyCount);
        var bodySectionBytes = bodySection.ToArray();
        AssertMalformedCorrelatedBodyRejected(
            (section, codeStart, _, _) => section[codeStart + 19] = 1);
        AssertMalformedCorrelatedBodyRejected(
            (section, _, _, edgeRecordStart) =>
                BinaryPrimitives.WriteUInt16LittleEndian(
                    section.AsSpan(edgeRecordStart + 4), 0));
        AssertMalformedCorrelatedBodyRejected(
            (section, _, reservationRecordStart, _) =>
                BinaryPrimitives.WriteUInt16LittleEndian(
                    section.AsSpan(reservationRecordStart + 6), 3));

        var loadedConsumer = TrRmExecutableCompiler.LoadStandard(
            consumerInput, "consumer.module", providers);
        AssertForeignOutputCall(loadedConsumer, "provider.module");
        AssertCorrelatedOutputCall(loadedConsumer, "provider.module",
            "InvokeCorrelated", TokenKind.GreaterEquals, PrimitiveType.Int32);
        AssertCorrelatedOutputCall(loadedConsumer, "provider.module",
            "InvokeCorrelatedReversed", TokenKind.GreaterEquals,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(loadedConsumer, "provider.module",
            "InvokeCorrelatedComplement", TokenKind.Less,
            PrimitiveType.Int32, successOnTrue: false);
        AssertCorrelatedOutputCall(loadedConsumer, "provider.module",
            "InvokeNonZeroInt32", TokenKind.BangEquals,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(loadedConsumer, "provider.module",
            "InvokeNonZeroInt32Negated", TokenKind.BangEquals,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(loadedConsumer, "provider.module",
            "InvokeNonZeroInt32Complement", TokenKind.EqualsEquals,
            PrimitiveType.Int32, successOnTrue: false);
        AssertCorrelatedOutputCall(loadedConsumer, "provider.module",
            "InvokeNonZero", TokenKind.BangEquals, PrimitiveType.UInt32);
        AssertCorrelatedOutputCall(loadedConsumer, "provider.module",
            "InvokeFloat64", TokenKind.GreaterEquals, PrimitiveType.Float64,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(loadedConsumer, "provider.module",
            "InvokeFloat64Complement", TokenKind.Less,
            PrimitiveType.Float64, PrimitiveType.Int32,
            successOnTrue: false);
        AssertCorrelatedOutputCall(loadedConsumer, "provider.module",
            "InvokeUInt8", TokenKind.BangEquals, PrimitiveType.UInt8,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(loadedConsumer, "provider.module",
            "InvokeBool", TokenKind.GreaterEquals, PrimitiveType.Bool,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(loadedConsumer, "provider.module",
            "InvokeInt64", TokenKind.GreaterEquals, PrimitiveType.Int64,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(loadedConsumer, "provider.module",
            "InvokeFloat32", TokenKind.GreaterEquals, PrimitiveType.Float32,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(loadedConsumer, "provider.module",
            "InvokeRune", TokenKind.GreaterEquals, PrimitiveType.Rune,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(loadedConsumer, "provider.module",
            "InvokeNuint", TokenKind.GreaterEquals, PrimitiveType.NativeUInt,
            PrimitiveType.Int32);
        AssertNestedFniInputCall(loadedConsumer, "provider.module");
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(
            loadedConsumer, "consumer.module", schemaIdentity, 1, []);
        var reloadedConsumer = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reemittedArtifact), "consumer.module", providers);
        AssertForeignOutputCall(reloadedConsumer, "provider.module");
        AssertCorrelatedOutputCall(reloadedConsumer, "provider.module",
            "InvokeCorrelated", TokenKind.GreaterEquals, PrimitiveType.Int32);
        AssertCorrelatedOutputCall(reloadedConsumer, "provider.module",
            "InvokeCorrelatedReversed", TokenKind.GreaterEquals,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(reloadedConsumer, "provider.module",
            "InvokeCorrelatedComplement", TokenKind.Less,
            PrimitiveType.Int32, successOnTrue: false);
        AssertCorrelatedOutputCall(reloadedConsumer, "provider.module",
            "InvokeNonZeroInt32", TokenKind.BangEquals,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(reloadedConsumer, "provider.module",
            "InvokeNonZeroInt32Negated", TokenKind.BangEquals,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(reloadedConsumer, "provider.module",
            "InvokeNonZeroInt32Complement", TokenKind.EqualsEquals,
            PrimitiveType.Int32, successOnTrue: false);
        AssertCorrelatedOutputCall(reloadedConsumer, "provider.module",
            "InvokeNonZero", TokenKind.BangEquals, PrimitiveType.UInt32);
        AssertCorrelatedOutputCall(reloadedConsumer, "provider.module",
            "InvokeFloat64", TokenKind.GreaterEquals, PrimitiveType.Float64,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(reloadedConsumer, "provider.module",
            "InvokeFloat64Complement", TokenKind.Less,
            PrimitiveType.Float64, PrimitiveType.Int32,
            successOnTrue: false);
        AssertCorrelatedOutputCall(reloadedConsumer, "provider.module",
            "InvokeUInt8", TokenKind.BangEquals, PrimitiveType.UInt8,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(reloadedConsumer, "provider.module",
            "InvokeBool", TokenKind.GreaterEquals, PrimitiveType.Bool,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(reloadedConsumer, "provider.module",
            "InvokeInt64", TokenKind.GreaterEquals, PrimitiveType.Int64,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(reloadedConsumer, "provider.module",
            "InvokeFloat32", TokenKind.GreaterEquals, PrimitiveType.Float32,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(reloadedConsumer, "provider.module",
            "InvokeRune", TokenKind.GreaterEquals, PrimitiveType.Rune,
            PrimitiveType.Int32);
        AssertCorrelatedOutputCall(reloadedConsumer, "provider.module",
            "InvokeNuint", TokenKind.GreaterEquals, PrimitiveType.NativeUInt,
            PrimitiveType.Int32);
        AssertNestedFniInputCall(reloadedConsumer, "provider.module");

        static void AssertCorrelatedOutputBody(
            TrRmRegisterBodyReader body, byte comparison,
            ushort outputHome = 2, ushort statusHome = 3,
            ushort complementHome = 3, bool successOnTrue = true)
        {
            Assert.That(body.BlockCount, Is.EqualTo(3));
            var entryCode = body.GetBlockCode(0);
            Assert.That(entryCode[19], Is.EqualTo(comparison));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                entryCode[8..]), Is.EqualTo(statusHome));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                entryCode[12..]), Is.EqualTo(statusHome));
            Assert.That(body.GetRecord(1)[5], Is.EqualTo((byte)1));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                body.GetRecord(1)[30..]), Is.EqualTo(outputHome));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                body.GetRecord(2)[6..]), Is.EqualTo(outputHome));
            Assert.That(body.GetRecord(3)[0], Is.EqualTo((byte)0x06));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                body.GetRecord(3)[4..]), Is.EqualTo(
                    successOnTrue ? (ushort)1 : (ushort)2));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                body.GetRecord(3)[6..]), Is.EqualTo(
                    successOnTrue ? (ushort)2 : (ushort)1));
            Assert.That(entryCode[^4], Is.EqualTo((byte)0xE4));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                body.GetBlockCode(1)[2..]), Is.EqualTo(outputHome));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                body.GetBlockCode(2)[2..]), Is.EqualTo(complementHome));
            Assert.That(body.GetBlockCode(1).ToArray(), Is.EqualTo(
                new byte[] { 0xD5, 0, (byte)outputHome,
                    (byte)(outputHome >> 8), 0x17, 0 }));
            Assert.That(body.GetBlockCode(2).ToArray(), Is.EqualTo(
                new byte[] { 0xD5, 0, (byte)complementHome,
                    (byte)(complementHome >> 8), 0x17, 0 }));
        }

        static void AssertNestedFniInputBody(
            TrRmRegisterBodyReader body, byte methodTokenKind)
        {
            Assert.That(body.BlockCount, Is.EqualTo(1));
            Assert.That(body.RecordCount, Is.EqualTo(3));
            Assert.That(body.MethodCount, Is.EqualTo(2));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                body.MethodMap) >> 24, Is.EqualTo((uint)methodTokenKind));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                body.MethodMap[sizeof(uint)..]) >> 24,
                Is.EqualTo((uint)methodTokenKind));
            var innerCallSite = body.GetRecord(1);
            var outerCallSite = body.GetRecord(2);
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                innerCallSite[8..]), Is.Zero);
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                innerCallSite[10..]), Is.EqualTo((ushort)1));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                outerCallSite[8..]), Is.EqualTo((ushort)1));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                outerCallSite[10..]), Is.Zero);
            Assert.That((innerCallSite[20], innerCallSite[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        innerCallSite[22..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)0)));
            Assert.That((innerCallSite[24], innerCallSite[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        innerCallSite[26..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)1)));
            Assert.That((outerCallSite[24], outerCallSite[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        outerCallSite[26..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)1)));
            Assert.That(body.GetBlockCode(0).ToArray(),
                Is.EqualTo(new byte[] {
                    0x90, 1, 0x90, 2, 0x17, 0,
                }));
        }

        static void AssertOutputReservationCallSite(
            TrRmRegisterBodyReader body)
        {
            var callSite = body.GetRecord(1);
            Assert.That(callSite[4], Is.EqualTo((byte)1));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                callSite[12..]), Is.EqualTo(3u));
            Assert.That((callSite[20], callSite[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(callSite[22..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)0)));
            Assert.That((callSite[24], callSite[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(callSite[26..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)1)));
            Assert.That((callSite[28], callSite[29],
                    BinaryPrimitives.ReadUInt16LittleEndian(callSite[30..])),
                Is.EqualTo(((byte)2, (byte)6, (ushort)2)));
            var reservationRecordIndex = Enumerable.Range(0,
                    body.RecordCount)
                .Single(index => body.GetRecord(index)[0] == 0x0C);
            var reservation = body.GetRecord(reservationRecordIndex);
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                reservation[4..]), Is.EqualTo((ushort)1));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                reservation[6..]), Is.EqualTo((ushort)2));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                reservation[8..]), Is.EqualTo(2u));
            var code = body.GetBlockCode(0).ToArray();
            Assert.That(code, Is.EqualTo(new byte[] {
                0xDF, 0, (byte)reservationRecordIndex,
                (byte)(reservationRecordIndex >> 8),
                0x90, 1, 0x17, 0,
            }));
        }

        static void AssertForeignOutputCall(BoundProgram program,
            string? expectedProvider)
        {
            var invoke = program.Functions.Single(static function =>
                function.Name == "Invoke");
            BoundCall? foreignCall = null;
            Assert.That(BoundTreeTraversal.VisitExpressions(invoke.Body,
                expression =>
                {
                    if (expression is BoundCall {
                            ForeignInterfaceCall: not null,
                        } call)
                    {
                        foreignCall = call;
                    }
                }), Is.True);
            Assert.That(foreignCall, Is.Not.Null);
            var selectedCall = foreignCall
                ?? throw new AssertionException("Expected an FNI call.");
            var foreignInterfaceCall = selectedCall.ForeignInterfaceCall
                ?? throw new AssertionException(
                    "Expected the selected FNI call identity.");
            Assert.That(foreignInterfaceCall.NativeOutputContracts
                .Single().FormalOrdinal,
                Is.EqualTo(1));
            var output = foreignInterfaceCall.NativeOutputContracts.Single();
            Assert.That(output.Dependencies.Select(static dependency =>
                    (dependency.SourceKind, dependency.SourceOrdinal,
                        dependency.Relationship)),
                Is.EquivalentTo(new[] {
                    (NativeOutputDependencySourceKind.Formal, 0,
                        NativeOutputDependencyRelationship.Forward),
                    (NativeOutputDependencySourceKind.Receiver, 0,
                        NativeOutputDependencyRelationship.Forward),
                }));
            Assert.That(selectedCall.Arguments[1],
                Is.TypeOf<BoundNativeOutputReservation>());
            var reservation = (BoundNativeOutputReservation)
                selectedCall.Arguments[1];
            Assert.That(reservation.PointeeType,
                Is.EqualTo(PrimitiveType.Int32));
            Assert.That(invoke.Variables[reservation.Variable]
                .IsInitializationLocal, Is.True);
            Assert.That(selectedCall.IsUnsafeAuthorized, Is.True);
            Assert.That(foreignInterfaceCall.Member
                .DeclaringInterface.ModuleImport?.ProducerIdentity,
                Is.EqualTo(expectedProvider));
        }

        static void AssertCorrelatedOutputCall(BoundProgram program,
            string? expectedProvider, string functionName,
            TokenKind expectedOperator, PrimitiveType returnType,
            PrimitiveType? nativeResultType = null,
            bool successOnTrue = true)
        {
            var statusType = nativeResultType ?? returnType;
            var invoke = program.Functions.Single(function =>
                function.Name == functionName);
            Assert.That(invoke.ReturnType, Is.EqualTo(returnType));
            BoundCall? foreignCall = null;
            Assert.That(BoundTreeTraversal.VisitExpressions(invoke.Body,
                expression =>
                {
                    if (expression is BoundCall {
                            ForeignInterfaceCall: not null,
                        } call)
                    {
                        foreignCall = call;
                    }
                }), Is.True);
            var selectedCall = foreignCall
                ?? throw new AssertionException("Expected an FNI call.");
            Assert.That(selectedCall.Type, Is.EqualTo(statusType));
            var foreignInterfaceCall = selectedCall.ForeignInterfaceCall
                ?? throw new AssertionException(
                    "Expected the selected FNI call identity.");
            Assert.That(foreignInterfaceCall.NativeOutputContracts
                .Single().FormalOrdinal, Is.EqualTo(1));
            Assert.That(foreignInterfaceCall.NativeOutputContracts
                .Single().SuccessSelector,
                Is.EqualTo(expectedOperator is TokenKind.BangEquals
                    or TokenKind.EqualsEquals
                    ? NativeOutputSelector.NonZero
                    : NativeOutputSelector.NonNegative));
            Assert.That(selectedCall.Arguments[1],
                Is.TypeOf<BoundNativeOutputReservation>());
            var reservation = (BoundNativeOutputReservation)
                selectedCall.Arguments[1];
            Assert.That(invoke.Variables[reservation.Variable]
                .IsInitializationLocal, Is.True);
            Assert.That(reservation.PointeeType, Is.EqualTo(returnType));
            var statusLocal = invoke.Body.Statements
                .OfType<BoundLocal>().Single();
            Assert.That(statusLocal.Initializer,
                Is.TypeOf<BoundUnsafeExpression>());
            Assert.That(statusLocal.Initializer.Type,
                Is.EqualTo(statusType));
            var branch = invoke.Body.Statements.OfType<BoundIf>().Single();
            Assert.That(branch.Condition, Is.TypeOf<BoundBinary>());
            var condition = (BoundBinary)branch.Condition;
            Assert.That(condition.Operator, Is.EqualTo(expectedOperator));
            Assert.That(((BoundRead)condition.Left).Variable,
                Is.EqualTo(statusLocal.Variable));
            Assert.That(condition.Left.Type, Is.EqualTo(statusType));
            Assert.That(condition.Right.Type, Is.EqualTo(statusType));
            Assert.That(((BoundConstant)condition.Right).Value, Is.Zero);
            var branchReturn = (BoundReturn)((BoundBlock)branch.Then)
                .Statements.Single();
            var complementVariable = invoke.ParameterCount == 3
                ? 2
                : statusLocal.Variable;
            Assert.That(((BoundRead)branchReturn.Value!).Variable,
                Is.EqualTo(successOnTrue
                    ? reservation.Variable
                    : complementVariable));
            Assert.That(branchReturn.Value!.Type, Is.EqualTo(returnType));
            var fallbackReturn = invoke.Body.Statements
                .OfType<BoundReturn>().Last();
            Assert.That(((BoundRead)fallbackReturn.Value!).Variable,
                Is.EqualTo(successOnTrue
                    ? complementVariable
                    : reservation.Variable));
            Assert.That(fallbackReturn.Value!.Type, Is.EqualTo(returnType));
            Assert.That(foreignInterfaceCall.Member.DeclaringInterface
                .ModuleImport?.ProducerIdentity, Is.EqualTo(expectedProvider));
        }

        static void AssertNestedFniInputCall(BoundProgram program,
            string? expectedProvider)
        {
            var invoke = program.Functions.Single(function =>
                function.Name == "InvokeNested");
            var calls = new List<BoundCall>();
            Assert.That(BoundTreeTraversal.VisitExpressions(invoke.Body,
                expression =>
                {
                    if (expression is BoundCall {
                            ForeignInterfaceCall: not null,
                        } call)
                    {
                        calls.Add(call);
                    }
                }), Is.True);
            Assert.That(calls, Has.Count.EqualTo(2));
            static BoundCall? UnwrapCall(BoundExpression expression)
            {
                while (expression is BoundUnsafeExpression unsafeCall)
                {
                    expression = unsafeCall.Expression;
                }

                return expression as BoundCall;
            }

            var outerCall = calls.Single(call =>
                call.Arguments.Length == 1
                && UnwrapCall(call.Arguments[0]) is
                    { ForeignInterfaceCall: not null });
            var nestedCall = UnwrapCall(outerCall.Arguments[0])
                ?? throw new AssertionException(
                    "Expected the nested FNI input call.");
            Assert.That(outerCall.IsUnsafeAuthorized, Is.True);
            Assert.That(nestedCall.IsUnsafeAuthorized, Is.True);
            Assert.That(nestedCall.Type, Is.EqualTo(PrimitiveType.Int32));
            Assert.That(((BoundRead)nestedCall.Arguments.Single()).Variable,
                Is.EqualTo(1));
            Assert.That(outerCall.ForeignInterfaceCall!.Member.MemberOrdinal,
                Is.Not.EqualTo(
                    nestedCall.ForeignInterfaceCall!.Member.MemberOrdinal));
            Assert.That(outerCall.ForeignInterfaceCall.Member
                .DeclaringInterface.ModuleImport?.ProducerIdentity,
                Is.EqualTo(expectedProvider));
            Assert.That(nestedCall.ForeignInterfaceCall.Member
                .DeclaringInterface.ModuleImport?.ProducerIdentity,
                Is.EqualTo(expectedProvider));
        }

        void AssertMalformedCorrelatedBodyRejected(
            Action<byte[], int, int, int> mutate)
        {
            var malformedBodySection = bodySectionBytes.ToArray();
            var correlatedBodyRow
                = consumerTables.Methods.GetBodyRow(consumerCorrelatedRow);
            var bodyRowEnd = checked((bodyCount + 1) * sizeof(uint)
                + (int)BinaryPrimitives.ReadUInt32LittleEndian(
                    malformedBodySection.AsSpan(correlatedBodyRow
                        * sizeof(uint), sizeof(uint))));
            var codeStart = checked(bodyRowEnd
                - consumerCorrelatedBody.CodeByteLength);
            var recordDataLength = 0;
            for (var record = 0;
                record < consumerCorrelatedBody.RecordCount; record++)
            {
                recordDataLength = checked(recordDataLength
                    + consumerCorrelatedBody.GetRecord(record).Length);
            }

            var recordDataStart = checked(codeStart - recordDataLength);
            var reservationRecordStart = checked(recordDataStart
                + consumerCorrelatedBody.GetRecord(0).Length
                + consumerCorrelatedBody.GetRecord(1).Length);
            var edgeRecordStart = checked(reservationRecordStart
                + consumerCorrelatedBody.GetRecord(2).Length);
            var entryBlockStart = checked(codeStart + (int)
                BinaryPrimitives.ReadUInt32LittleEndian(
                    consumerCorrelatedBody.GetBlock(0)));
            mutate(malformedBodySection, entryBlockStart,
                reservationRecordStart, edgeRecordStart);

            var malformedSections
                = new TrRmSection[malformedIndex.SectionCount];
            for (var sectionIndex = 0;
                sectionIndex < malformedIndex.SectionCount; sectionIndex++)
            {
                malformedIndex.GetSection(sectionIndex, out var kind,
                    out var flags, out var bytes, out var count);
                malformedSections[sectionIndex] = new TrRmSection(kind,
                    flags, checked((uint)count),
                    kind == 0x0020 ? malformedBodySection : bytes.ToArray());
            }
            var malformedArtifact
                = TrRmArtifactWriter.Encode(malformedSections, schema);
            Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler.LoadStandard(
                    new TrRmStandardArtifactInput(schemaIdentity,
                        malformedArtifact), "consumer.module", providers));
        }
    }

    [TestCase("char8", PrimitiveType.Char8)]
    [TestCase("char16", PrimitiveType.Char16)]
    [TestCase("char32", PrimitiveType.Char32)]
    [TestCase("int8", PrimitiveType.Int8)]
    [TestCase("int16", PrimitiveType.Int16)]
    [TestCase("uint16", PrimitiveType.UInt16)]
    [TestCase("uint64", PrimitiveType.UInt64)]
    [TestCase("nint", PrimitiveType.NativeInt)]
    public void StandardForeignNativeInterfaceRoundTripsAdditionalScalarOutputs(
        string typeName, PrimitiveType expectedType)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-native-interface-parameter-surface-1";
        var (native, output) = NativeOutputCoreRegistrationTests
            .CreateCombinedRegistrations(includeForeignNativeInterface: true);
        var options = new CompilationOptions {
            NativeCoreRegistration = native,
            LayoutCoreRegistration = CreateLayoutRegistration(native),
            NativeOutputCoreRegistration = output,
        };
        var source = $$"""
            using Terrum.Interop;
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                [param(value): NativeOutput(
                    When: NativeOutputCondition.NonZero)]
                public unsafe int32 TryRead(ptr<{{typeName}}> source,
                    [Terrum.CodeAnalysis.DependsOn(this, nameof(source))]
                    [Terrum.CodeAnalysis.ForwardsBacking(
                        this, nameof(source))]
                    write ptr<{{typeName}}> value);
            }

            {{typeName}} Invoke(ptr<void> receiver,
                ptr<{{typeName}}> source, {{typeName}} fallback) {
                init {{typeName}} value;
                unsafe {
                    int32 status = IProbe.TryRead(
                        receiver, source, &init value);
                    if (status != 0) { return value; }
                    return fallback;
                }
            }
            """;
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a scalar FNI program.");
        var member = program.Interfaces.Single().Members.Single();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "scalar-output.module", schemaIdentity, 1, [
                new TrRmSourceTypeExport("", "IProbe",
                    "provider/IProbe") {
                    GenericArity = 0,
                    InterfaceMethodExports = [
                        new TrRmSourceInterfaceMethodExport(
                            member.SourceInputOrdinal,
                            member.SourceDeclarationSpan,
                            "provider/IProbe.TryRead"),
                    ],
                },
            ]);
        var input = new TrRmStandardArtifactInput(schemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            "scalar-output.module", []);
        var loadedFunction = loaded.Functions.Single();
        Assert.That(loadedFunction.ReturnType, Is.EqualTo(expectedType));
        Assert.That(loadedFunction.Variables[3].Type,
            Is.EqualTo(expectedType));
        var image = NativeEmitter.EmitLibrary(loaded, ["Invoke"],
            NativeRuntimePolicy.FailFast);
        Assert.That(image[..2], Is.EqualTo(new byte[] {
            (byte)'M', (byte)'Z',
        }));

        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            "scalar-output.module", schemaIdentity, 1, []);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemittedArtifact),
            "scalar-output.module", []);
        Assert.That(reloaded.Functions.Single().ReturnType,
            Is.EqualTo(expectedType));
        Assert.That(reloaded.Functions.Single().Variables[3].Type,
            Is.EqualTo(expectedType));
    }

    [Test]
    public async Task StandardForeignNativeInterfaceConsumesAggregateOutputUnderAc1()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string schemaIdentity
            = "trrm/1-draft-foreign-native-interface-aggregate-output-consumption-1";
        const string providerSource = """
            using Terrum.Interop;
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Point {
                public int64 X;
                public int64 Y;
            }
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                [param(value): NativeOutput(
                    When: NativeOutputCondition.NonZero)]
                public unsafe int32 TryReadPoint(ptr<int32> source,
                    [Terrum.CodeAnalysis.DependsOn(this, nameof(source))]
                    [Terrum.CodeAnalysis.ForwardsBacking(
                        this, nameof(source))]
                    write ptr<Point> value);
            }
            Point InvokePoint(ptr<void> receiver, ptr<int32> source,
                Point fallback) {
                init Point value;
                unsafe {
                    int32 status = IProbe.TryReadPoint(
                        receiver, source, &init value);
                    if (status != 0) { return value; }
                    return fallback;
                }
            }
            """;
        var (native, output) = NativeOutputCoreRegistrationTests
            .CreateCombinedRegistrations(includeForeignNativeInterface: true);
        var options = new CompilationOptions {
            NativeCoreRegistration = native,
            LayoutCoreRegistration = CreateLayoutRegistration(native),
            NativeOutputCoreRegistration = output,
        };
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource), options);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program
            ?? throw new AssertionException("Expected a bound provider.");
        var providerMember = providerProgram.Interfaces.Single().Members
            .Single();
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "provider.module", schemaIdentity, 1, [
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
                            providerMember.SourceInputOrdinal,
                            providerMember.SourceDeclarationSpan,
                            "provider/IProbe.TryReadPoint"),
                    ],
                },
            ]);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var schema
            = TrRmArtifactSchema
                .ForeignNativeInterfaceAggregateOutputConsumptionDraft;
        var providerTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                providerArtifact, schema));
        Assert.That(providerTables.Exports.TryGetDefinitionForKey(1,
                "provider/Point", out var pointDefinition), Is.True);
        var pointTypeDef = checked((int)(pointDefinition & 0x00FF_FFFF));
        Assert.That(providerTables.TypeDefs.HasDeclaredCopy(pointTypeDef),
            Is.True);
        Assert.That(TrRmFeatureManifestValidator.Requires(
            providerTables.Features, providerTables.Modules, 1,
            TrRmFeatureTableReader.RootlessNativeValuesFeatureId, schema),
            Is.True);
        Assert.That(TrRmFeatureManifestValidator.Requires(
            providerTables.Features, providerTables.Modules, 1,
            TrRmFeatureTableReader.LayoutQueriesFeatureId, schema), Is.True);
        var providerBody = GetBody(providerTables, "InvokePoint");
        AssertAggregateOutputBody(providerBody, 0x13);

        var loadedProvider = TrRmExecutableCompiler.LoadStandard(
            providerInput, "provider.module", []);
        AssertAggregateOutputFunction(loadedProvider, "InvokePoint");
        var providerImage = NativeEmitter.EmitLibrary(loadedProvider,
            ["InvokePoint"], NativeRuntimePolicy.FailFast);
        Assert.That(providerImage[..2], Is.EqualTo(new byte[] {
            (byte)'M', (byte)'Z',
        }));
        await AssertAggregatePointOutputAsync(providerImage, "InvokePoint")
            .ConfigureAwait(true);

        const string parameterSurfaceSchemaIdentity
            = "trrm/1-draft-foreign-native-interface-parameter-surface-1";
        var parameterSurfaceCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Terrum.Interop;
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Point {
                    public int64 X;
                    public int64 Y;
                }
                [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
                [ForeignNativeInterface(VTableByteOffset: uint32(0))]
                public interface IProbe {
                    [param(value): NativeOutput(
                        When: NativeOutputCondition.NonZero)]
                    public unsafe int32 TryReadPoint(ptr<int32> source,
                        [Terrum.CodeAnalysis.DependsOn(this, nameof(source))]
                        [Terrum.CodeAnalysis.ForwardsBacking(
                            this, nameof(source))]
                        write ptr<Point> value);
                }
                """), options);
        Assert.That(parameterSurfaceCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                parameterSurfaceCompilation.Diagnostics));
        var parameterSurfaceProgram = parameterSurfaceCompilation.Program
            ?? throw new AssertionException(
                "Expected a bodyless PS1 FNI provider.");
        var parameterSurfaceMember
            = parameterSurfaceProgram.Interfaces.Single().Members.Single();
        var parameterSurfaceArtifact = TrRmExecutableCompiler.EmitStandard(
            parameterSurfaceProgram, "parameter-surface.module",
            parameterSurfaceSchemaIdentity, 1, [
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
                            parameterSurfaceMember.SourceInputOrdinal,
                            parameterSurfaceMember.SourceDeclarationSpan,
                            "provider/IProbe.TryReadPoint"),
                    ],
                },
            ]);
        var parameterSurfaceSchema
            = TrRmArtifactSchema
                .ForeignNativeInterfaceParameterSurfaceDraft;
        var parameterSurfaceTables
            = TrRmStructuralMetadataValidator
                .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                    parameterSurfaceArtifact, parameterSurfaceSchema));
        var parameterSurfacePointTypeDef
            = Enumerable.Range(1, parameterSurfaceTables.TypeDefs.Count)
                .Single(row => parameterSurfaceTables.TypeDefs.GetName(row)
                    == "Point");
        Assert.That(parameterSurfaceTables.TypeDefs.HasDeclaredCopy(
            parameterSurfacePointTypeDef), Is.True);
        Assert.That(TrRmFeatureManifestValidator.Requires(
            parameterSurfaceTables.Features,
            parameterSurfaceTables.Modules, 1,
            TrRmFeatureTableReader.LayoutQueriesFeatureId,
            parameterSurfaceSchema), Is.True);
        Assert.That(parameterSurfaceTables.InterfaceMethods.Count,
            Is.EqualTo(1));
        var parameterSurfaceMethodDef
            = parameterSurfaceTables.InterfaceMethods.GetMethodDefRow(1);
        Assert.That(parameterSurfaceTables.Methods.GetBodyRow(
            parameterSurfaceMethodDef), Is.Zero);
        var parameterSurfaceInput = new TrRmStandardArtifactInput(
            parameterSurfaceSchemaIdentity, parameterSurfaceArtifact);
        var providerReference
            = TrRmExecutableCompiler.CreateStandardReference(
                parameterSurfaceInput, "parameter-surface.module", []);
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                Point InvokeImportedPoint(ptr<void> receiver,
                    ptr<int32> source, Point fallback) {
                    init Point value;
                    unsafe {
                        int32 status = IProbe.TryReadPoint(
                            receiver, source, &init value);
                        if (status != 0) { return value; }
                        return fallback;
                    }
                }
                """), providerReference, options);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new AssertionException("Expected a bound consumer.");
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "consumer.module", schemaIdentity, 1, []);
        var consumerInput = new TrRmStandardArtifactInput(schemaIdentity,
            consumerArtifact);
        var consumerTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact, schema));
        var pointImportRow = Enumerable.Range(1,
                consumerTables.Imports.Count).Single(row =>
                (consumerTables.Imports
                    .GetExpectedDefinitionForImportRow(row) >> 24) == 0x13
                && StringComparer.Ordinal.Equals(
                    consumerTables.Imports.GetBindingForImportRow(row)
                        .ExportKey, "provider/Point"));
        var expectedPointTypeDef = checked((int)(
            consumerTables.Imports
                .GetExpectedDefinitionForImportRow(pointImportRow)
            & 0x00FF_FFFF));
        Assert.That(consumerTables.TypeDefs.HasDeclaredCopy(
            expectedPointTypeDef), Is.True);
        var missingExpectedCopy = RewriteTypeDefFlags(
            consumerArtifact, schema, expectedPointTypeDef,
            clearMask: 0x20);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                new TrRmArtifactIndex(missingExpectedCopy, schema)));
        var consumerBody = GetBody(consumerTables,
            "InvokeImportedPoint");
        AssertAggregateOutputBody(consumerBody, 0x1C);

        var providers = new[] { parameterSurfaceInput };
        var malformedD5Artifact = RewriteD5Home(consumerArtifact, schema,
            "InvokeImportedPoint", 1, 2);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    malformedD5Artifact), "consumer.module", providers));

        var loadedConsumer = TrRmExecutableCompiler.LoadStandard(
            consumerInput, "consumer.module", providers);
        AssertAggregateOutputFunction(loadedConsumer,
            "InvokeImportedPoint");
        var consumerImage = NativeEmitter.EmitLibrary(loadedConsumer,
            ["InvokeImportedPoint"], NativeRuntimePolicy.FailFast);
        Assert.That(consumerImage[..2], Is.EqualTo(new byte[] {
            (byte)'M', (byte)'Z',
        }));
        await AssertAggregatePointOutputAsync(consumerImage,
            "InvokeImportedPoint").ConfigureAwait(true);
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(
            loadedConsumer, "consumer.module", schemaIdentity, 1, []);
        var reloadedConsumer = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reemittedArtifact), "consumer.module", providers);
        AssertAggregateOutputFunction(reloadedConsumer,
            "InvokeImportedPoint");
        var reloadedImage = NativeEmitter.EmitLibrary(reloadedConsumer,
            ["InvokeImportedPoint"], NativeRuntimePolicy.FailFast);
        await AssertAggregatePointOutputAsync(reloadedImage,
            "InvokeImportedPoint").ConfigureAwait(true);

        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                new TrRmArtifactIndex(providerArtifact,
                    TrRmArtifactSchema
                        .ForeignNativeInterfaceParameterSurfaceDraft)));

        static TrRmRegisterBodyReader GetBody(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            string methodName)
        {
            var method = Enumerable.Range(1, tables.Methods.Count).Single(row =>
                tables.Methods.GetName(row) == methodName);
            return tables.Bodies.GetRegisterBody(
                tables.Methods.GetBodyRow(method), tables.Contracts.Count);
        }

        static void AssertAggregateOutputBody(
            TrRmRegisterBodyReader body, byte expectedTypeTokenKind)
        {
            Assert.That(body.BlockCount, Is.EqualTo(3));
            Assert.That(body.HomeCount, Is.EqualTo(5));
            var outputHome = body.HomeMap.Slice(3 * 8, 8);
            var outputTypeMapIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(
                    outputHome[2..]);
            var outputTypeToken = BinaryPrimitives
                .ReadUInt32LittleEndian(body.TypeMap[
                    outputTypeMapIndex * sizeof(uint)..]);
            Assert.That(outputTypeToken >> 24,
                Is.EqualTo((uint)expectedTypeTokenKind));
            Assert.That(body.GetBlockCode(1).ToArray(),
                Is.EqualTo(new byte[] { 0xD5, 0, 3, 0, 0x17, 0 }));
            Assert.That(body.GetBlockCode(2).ToArray(),
                Is.EqualTo(new byte[] { 0xD5, 0, 2, 0, 0x17, 0 }));
        }

        static void AssertAggregateOutputFunction(BoundProgram program,
            string methodName)
        {
            var function = program.Functions.Single(function =>
                function.Name == methodName);
            var conditional = function.Body.Statements
                .OfType<BoundIf>().Single();
            var outputReturn = (BoundReturn)conditional.Then.Statements
                .Single();
            var fallbackReturn = (BoundReturn)function.Body.Statements[^1];
            Assert.That(outputReturn.Value, Is.TypeOf<BoundRead>());
            Assert.That(outputReturn.Value.Type,
                Is.EqualTo(function.ReturnType));
            Assert.That(((BoundRead)outputReturn.Value).Variable,
                Is.EqualTo(function.ParameterCount));
            Assert.That(fallbackReturn.Value, Is.TypeOf<BoundRead>());
            Assert.That(fallbackReturn.Value.Type,
                Is.EqualTo(function.ReturnType));
            Assert.That(((BoundRead)fallbackReturn.Value).Variable,
                Is.EqualTo(2));
        }

        static byte[] RewriteD5Home(byte[] artifact,
            TrRmArtifactSchema schema, string methodName, int blockIndex,
            ushort replacementHome)
        {
            var index = new TrRmArtifactIndex(artifact, schema);
            Assert.That(index.TryGetSection(0x0020, out var bodySection,
                out var bodyCount), Is.True);
            var bodySectionBytes = bodySection.ToArray();
            var tables = TrRmStructuralMetadataValidator
                .ValidateAndGetExecutionTables(index);
            var method = Enumerable.Range(1, tables.Methods.Count).Single(row =>
                tables.Methods.GetName(row) == methodName);
            var bodyRow = tables.Methods.GetBodyRow(method);
            var body = tables.Bodies.GetRegisterBody(bodyRow,
                tables.Contracts.Count);
            var bodyRowEnd = checked((bodyCount + 1) * sizeof(uint)
                + (int)BinaryPrimitives.ReadUInt32LittleEndian(
                    bodySectionBytes.AsSpan(bodyRow * sizeof(uint),
                        sizeof(uint))));
            var codeStart = bodyRowEnd - body.CodeByteLength;
            var block = body.GetBlock(blockIndex);
            var blockCodeStart = checked(codeStart
                + (int)BinaryPrimitives.ReadUInt32LittleEndian(block));
            BinaryPrimitives.WriteUInt16LittleEndian(
                bodySectionBytes.AsSpan(blockCodeStart + 2, sizeof(ushort)),
                replacementHome);
            var sections = new TrRmSection[index.SectionCount];
            for (var sectionIndex = 0;
                sectionIndex < index.SectionCount;
                sectionIndex++)
            {
                index.GetSection(sectionIndex, out var kind, out var flags,
                    out var bytes, out var count);
                sections[sectionIndex] = new TrRmSection(kind, flags,
                    checked((uint)count),
                    kind == 0x0020 ? bodySectionBytes : bytes.ToArray());
            }

            return TrRmArtifactWriter.Encode(sections, schema);
        }

        static byte[] RewriteTypeDefFlags(byte[] artifact,
            TrRmArtifactSchema schema, int typeDefRow, uint clearMask)
        {
            var index = new TrRmArtifactIndex(artifact, schema);
            Assert.That(index.TryGetSection(0x0013,
                out var typeDefSection, out _), Is.True);
            var typeDefBytes = typeDefSection.ToArray();
            var flagsOffset = checked((typeDefRow - 1) * 44 + 8);
            var typeDefFlags = BinaryPrimitives.ReadUInt32LittleEndian(
                typeDefBytes.AsSpan(flagsOffset));
            BinaryPrimitives.WriteUInt32LittleEndian(
                typeDefBytes.AsSpan(flagsOffset),
                typeDefFlags & ~clearMask);
            var sections = new TrRmSection[index.SectionCount];
            for (var sectionIndex = 0;
                sectionIndex < index.SectionCount;
                sectionIndex++)
            {
                index.GetSection(sectionIndex, out var kind,
                    out var sectionFlags, out var bytes, out var count);
                sections[sectionIndex] = new TrRmSection(kind, sectionFlags,
                    checked((uint)count),
                    kind == 0x0013 ? typeDefBytes : bytes.ToArray());
            }

            return TrRmArtifactWriter.Encode(sections, schema);
        }
    }

    [Test]
    public void StandardForeignNativeInterfaceAc1RejectsMissingProviderCopy()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-native-interface-aggregate-output-consumption-1";
        const string typeProviderSchemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        var (native, output) = NativeOutputCoreRegistrationTests
            .CreateCombinedRegistrations(includeForeignNativeInterface: true);
        var options = new CompilationOptions {
            NativeCoreRegistration = native,
            LayoutCoreRegistration = CreateLayoutRegistration(native),
            NativeOutputCoreRegistration = output,
        };
        var typeProviderOptions = new CompilationOptions {
            LayoutCoreRegistration = CreateLayoutRegistration(native),
        };
        var typeProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Coordinates {
                    public int32 X;
                    public int32 Y;
                }
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Point {
                    public Coordinates Position;
                }
                """), typeProviderOptions);
        Assert.That(typeProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                typeProviderCompilation.Diagnostics));
        var typeProviderProgram = typeProviderCompilation.Program
            ?? throw new AssertionException(
                "Expected a bound aggregate provider.");
        var typeProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            typeProviderProgram, "type.module",
            typeProviderSchemaIdentity, 1, [
                new TrRmSourceTypeExport("", "Coordinates",
                    "type/Coordinates") {
                },
                new TrRmSourceTypeExport("", "Point", "type/Point") {
                },
            ]);
        typeProviderArtifact = AddLayoutQueriesFeature(
            typeProviderArtifact);
        var typeProviderInput = new TrRmStandardArtifactInput(
            typeProviderSchemaIdentity, typeProviderArtifact);
        var typeProviderReference
            = TrRmExecutableCompiler.CreateStandardReference(
                typeProviderInput, "type.module", []);
        Assert.That(typeProviderReference.Structures.Single(
            static structure => structure.Name == "Point")
            .StandardTypeOrigin!.Provider.SchemaIdentity,
            Is.EqualTo(typeProviderSchemaIdentity));
        var methodProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Terrum.Interop;
                [NativeCallable(
                    CallConv: NativeCallingConvention.Cdecl)]
                [ForeignNativeInterface(
                    VTableByteOffset: uint32(0))]
                public interface IProbe {
                    [param(value): NativeOutput(
                        When: NativeOutputCondition.NonZero)]
                    public unsafe int32 TryReadPoint(ptr<int32> source,
                        [Terrum.CodeAnalysis.DependsOn(
                            this, nameof(source))]
                        [Terrum.CodeAnalysis.ForwardsBacking(
                            this, nameof(source))]
                        write ptr<Point> value);
                }
                Point InvokePoint(ptr<void> receiver, ptr<int32> source,
                    Point fallback) {
                    init Point value;
                    unsafe {
                        int32 status = IProbe.TryReadPoint(
                            receiver, source, &init value);
                        if (status != 0) { return value; }
                        return fallback;
                    }
                }
                """), typeProviderReference, options);
        Assert.That(methodProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                methodProviderCompilation.Diagnostics));
        var methodProviderProgram = methodProviderCompilation.Program
            ?? throw new AssertionException(
                "Expected a bound FNI method provider.");
        var methodProviderMember = methodProviderProgram.Interfaces
            .Single(definition => definition.QualifiedName == "IProbe")
            .Members.Single();
        var methodProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            methodProviderProgram, "method.module", schemaIdentity, 1, [
                new TrRmSourceTypeExport("", "IProbe",
                    "method/IProbe") {
                    GenericArity = 0,
                    InterfaceMethodExports = [
                        new TrRmSourceInterfaceMethodExport(
                            methodProviderMember.SourceInputOrdinal,
                            methodProviderMember.SourceDeclarationSpan,
                            "method/IProbe.TryReadPoint"),
                    ],
                },
            ]);
        var methodProviderTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                methodProviderArtifact,
                TrRmArtifactSchema
                    .ForeignNativeInterfaceAggregateOutputConsumptionDraft));
        var expectedPointTypeDef = Enumerable.Range(
                1, methodProviderTables.TypeDefs.Count).Single(row =>
                methodProviderTables.TypeDefs.GetName(row) == "Point"
                && methodProviderTables.TypeDefs.GetOwnerModule(row) == 1);
        var expectedCoordinatesTypeDef = Enumerable.Range(
                1, methodProviderTables.TypeDefs.Count).Single(row =>
                methodProviderTables.TypeDefs.GetName(row)
                    == "Coordinates"
                && methodProviderTables.TypeDefs.GetOwnerModule(row) == 1);
        Assert.That(methodProviderTables.TypeDefs.HasDeclaredCopy(
            expectedPointTypeDef), Is.True);
        Assert.That(methodProviderTables.TypeDefs.HasDeclaredCopy(
            expectedCoordinatesTypeDef), Is.True);
        var methodProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, methodProviderArtifact);
        var loadedMethodProvider = TrRmExecutableCompiler.LoadStandard(
            methodProviderInput, "method.module", [typeProviderInput]);
        var loadedMethod = loadedMethodProvider.Functions.Single(function =>
            function.Name == "InvokePoint");
        Assert.That(loadedMethod.Body.Statements.OfType<BoundIf>().Count(),
            Is.EqualTo(1));
        var methodProviderImage = NativeEmitter.EmitLibrary(
            loadedMethodProvider, ["InvokePoint"],
            NativeRuntimePolicy.FailFast);
        Assert.That(methodProviderImage[..2], Is.EqualTo(new byte[] {
            (byte)'M', (byte)'Z',
        }));
        var reemittedMethodProviderArtifact
            = TrRmExecutableCompiler.EmitStandard(loadedMethodProvider,
                "method.module", schemaIdentity, 1, []);
        var reloadedMethodProvider = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reemittedMethodProviderArtifact), "method.module",
            [typeProviderInput]);
        Assert.That(reloadedMethodProvider.Functions.Single(function =>
                function.Name == "InvokePoint").Body.Statements
                .OfType<BoundIf>().Count(), Is.EqualTo(1));

        var typeProviderIndex = new TrRmArtifactIndex(
            typeProviderArtifact,
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        Assert.That(typeProviderIndex.TryGetSection(0x0013,
                out var typeDefSection, out _), Is.True);
        var typeDefBytes = typeDefSection.ToArray();
        var typeProviderTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                typeProviderIndex);
        Assert.That(typeProviderTables.Exports.TryGetDefinitionForKey(1,
                "type/Point", out var pointDefinition), Is.True);
        var pointTypeDef = checked((int)(
            pointDefinition & 0x00FF_FFFF));
        Assert.That(typeProviderTables.Exports.TryGetDefinitionForKey(1,
                "type/Coordinates", out var coordinatesDefinition), Is.True);
        var coordinatesTypeDef = checked((int)(
            coordinatesDefinition & 0x00FF_FFFF));
        Assert.That(typeProviderTables.TypeDefs.HasDeclaredCopy(
            pointTypeDef), Is.True);
        Assert.That(typeProviderTables.TypeDefs.HasDeclaredCopy(
            coordinatesTypeDef), Is.True);
        Assert.That(TrRmFeatureManifestValidator.Requires(
            typeProviderTables.Features, typeProviderTables.Modules, 1,
            TrRmFeatureTableReader.RootlessNativeValuesFeatureId,
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft),
            Is.True);
        Assert.That(TrRmFeatureManifestValidator.Requires(
            typeProviderTables.Features, typeProviderTables.Modules, 1,
            TrRmFeatureTableReader.LayoutQueriesFeatureId,
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft),
            Is.True);
        var pointFlagsOffset = checked((coordinatesTypeDef - 1) * 44 + 8);
        var pointFlags = BinaryPrimitives.ReadUInt32LittleEndian(
            typeDefBytes.AsSpan(pointFlagsOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(
            typeDefBytes.AsSpan(pointFlagsOffset), pointFlags & ~0x20u);
        var malformedSections
            = new TrRmSection[typeProviderIndex.SectionCount];
        for (var sectionIndex = 0;
            sectionIndex < typeProviderIndex.SectionCount; sectionIndex++)
        {
            typeProviderIndex.GetSection(sectionIndex, out var kind,
                out var flags, out var bytes, out var count);
            malformedSections[sectionIndex] = new TrRmSection(kind, flags,
                checked((uint)count),
                kind == 0x0013 ? typeDefBytes : bytes.ToArray());
        }

        var missingCopyProviderArtifact
            = TrRmArtifactWriter.Encode(malformedSections,
                TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        var missingCopyProviderInput = new TrRmStandardArtifactInput(
            typeProviderSchemaIdentity, missingCopyProviderArtifact);
        _ = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
            new TrRmArtifactIndex(missingCopyProviderArtifact,
                TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft));

        var methodInput = methodProviderInput;
        var methodReference = TrRmExecutableCompiler.CreateStandardReference(
            methodInput, "method.module", [typeProviderInput]);
        Assert.That(methodReference.Structures.Any(structure =>
                structure.Name == "Point"
                && structure.DeclaredCopyEvidence == true
                && structure.StandardTypeOrigin is { IsLookupRoot: true }
                    origin
                && StringComparer.Ordinal.Equals(
                    origin.Provider.SchemaIdentity,
                    typeProviderSchemaIdentity)), Is.True);

        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                methodInput, "method.module",
                [missingCopyProviderInput]));
        var changedLayoutProvider = RewriteTypeDefLayoutKind(
            typeProviderArtifact, pointTypeDef, 2);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(methodInput,
                "method.module", [
                    new TrRmStandardArtifactInput(
                        typeProviderSchemaIdentity, changedLayoutProvider),
                ]));

        static byte[] AddLayoutQueriesFeature(byte[] artifact)
        {
            var index = TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyDraft(artifact);
            Assert.That(index.TryGetSection(0x0001, out var moduleSection,
                out var moduleCount), Is.True);
            Assert.That(moduleCount, Is.EqualTo(1));
            Assert.That(index.TryGetSection(0x0004, out var featureSection,
                out var featureCount), Is.True);
            Assert.That(featureCount, Is.EqualTo(1));
            var moduleBytes = moduleSection.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(
                moduleBytes.AsSpan(24), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                moduleBytes.AsSpan(28), 2);
            var featureBytes = TrRmFeatureTableWriter.Encode([
                TrRmFeatureRow.RootlessNativeValues,
                TrRmFeatureRow.LayoutQueries,
            ], TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
            var sections = new TrRmSection[index.SectionCount];
            for (var sectionIndex = 0;
                sectionIndex < index.SectionCount;
                sectionIndex++)
            {
                index.GetSection(sectionIndex, out var kind, out var flags,
                    out var bytes, out var count);
                sections[sectionIndex] = new TrRmSection(kind, flags,
                    kind == 0x0004 ? 2u : checked((uint)count),
                    kind switch {
                        0x0001 => moduleBytes,
                        0x0004 => featureBytes,
                        _ => bytes.ToArray(),
                    });
            }

            return TrRmArtifactWriter.Encode(sections,
                TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);
        }

        static byte[] RewriteTypeDefLayoutKind(byte[] artifact,
            int typeDefRow, byte layoutKind)
        {
            var schema
                = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
            var index = TrRmArtifactIndex.ForForeignInvocationStorageFixedArrayCopyDraft(
                artifact);
            Assert.That(index.TryGetSection(0x0015, out var layoutSection,
                out _), Is.True);
            var layoutBytes = layoutSection.ToArray();
            var tables = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                    index);
            var layoutRow = tables.Layouts.GetLayoutForProfile(typeDefRow,
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader
                    .WindowsX64NativeProfileRuleRevision);
            layoutBytes[(layoutRow - 1) * 28 + 8] = layoutKind;
            var sections = new TrRmSection[index.SectionCount];
            for (var sectionIndex = 0;
                sectionIndex < index.SectionCount;
                sectionIndex++)
            {
                index.GetSection(sectionIndex, out var kind, out var flags,
                    out var bytes, out var count);
                sections[sectionIndex] = new TrRmSection(kind, flags,
                    checked((uint)count),
                    kind == 0x0015 ? layoutBytes : bytes.ToArray());
            }

            return TrRmArtifactWriter.Encode(sections, schema);
        }
    }

    [Test]
    public void StandardForeignNativeInterfaceNonReturningCallsRetainOutputContracts()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-native-interface-parameter-surface-1";
        var (native, output) = NativeOutputCoreRegistrationTests
            .CreateCombinedRegistrations(includeForeignNativeInterface: true);
        var options = new CompilationOptions {
            NativeCoreRegistration = native,
            LayoutCoreRegistration = CreateLayoutRegistration(native),
            NativeOutputCoreRegistration = output,
        };
        const string providerSource = """
            [Terrum.Interop.NativeCallable(
                CallConv: Terrum.Interop.NativeCallingConvention.Cdecl)]
            [Terrum.Interop.ForeignNativeInterface(
                VTableByteOffset: uint32(0))]
            public interface IProbe {
                [DoesNotReturn]
                [param(value): Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonNegative)]
                public unsafe int32 Stop(ptr<int32> source,
                    [Terrum.CodeAnalysis.DependsOn(this, nameof(source))]
                    [Terrum.CodeAnalysis.ForwardsBacking(
                        this, nameof(source))]
                    write ptr<int32> value);
            }

            [DoesNotReturn]
            void Invoke(ptr<void> receiver, ptr<int32> source) {
                init int32 value;
                unsafe { IProbe.Stop(receiver, source, &init value); }
            }
            """;
        const string consumerSource = """
            [DoesNotReturn]
            void Invoke(ptr<void> receiver, ptr<int32> source) {
                init int32 value;
                unsafe { IProbe.Stop(receiver, source, &init value); }
            }
            """;
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource), options);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program
            ?? throw new AssertionException("Expected a bound provider.");
        var providerInterface = providerProgram.Interfaces.Single();
        var typeExport = new TrRmSourceTypeExport("", "IProbe",
            "provider/IProbe") {
            GenericArity = 0,
            InterfaceMethodExports = [.. providerInterface.Members.Select(
                member => new TrRmSourceInterfaceMethodExport(
                    member.SourceInputOrdinal,
                    member.SourceDeclarationSpan,
                    $"provider/IProbe.{member.Name}"))],
        };
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "provider.module", schemaIdentity, 1,
            [typeExport]);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var schema
            = TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft;
        var providerTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                providerArtifact, schema));
        var providerInvokeRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row) == "Invoke");
        var providerBody = providerTables.Bodies.GetRegisterBody(
            providerTables.Methods.GetBodyRow(providerInvokeRow),
            providerTables.Contracts.Count);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            providerBody.MethodMap) >> 24, Is.EqualTo(0x16u));
        Assert.That(providerBody.GetRecord(1)[5], Is.EqualTo((byte)0));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            providerBody.GetRecord(1)[12..]), Is.EqualTo(3u));
        Assert.That(providerBody.GetRecord(2)[0], Is.EqualTo((byte)0x0C));
        Assert.That(providerBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] { 0xDF, 0, 2, 0, 0x90, 1 }));

        var providerInputArray = new[] { providerInput };
        var loadedProvider = TrRmExecutableCompiler.LoadStandard(
            providerInput, "provider.module", []);
        AssertNonReturningOutputCall(loadedProvider);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "provider.module", []);
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(consumerSource), reference, options);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new AssertionException("Expected a bound consumer.");
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "consumer.module", schemaIdentity, 1, []);
        var consumerInput = new TrRmStandardArtifactInput(schemaIdentity,
            consumerArtifact);
        var consumerTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact, schema));
        var consumerInvokeRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row) == "Invoke");
        var consumerBody = consumerTables.Bodies.GetRegisterBody(
            consumerTables.Methods.GetBodyRow(consumerInvokeRow),
            consumerTables.Contracts.Count);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            consumerBody.MethodMap) >> 24, Is.EqualTo(0x1Cu));
        Assert.That(consumerBody.GetRecord(1)[5], Is.EqualTo((byte)0));
        Assert.That(consumerBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] { 0xDF, 0, 2, 0, 0x90, 1 }));

        var loadedConsumer = TrRmExecutableCompiler.LoadStandard(
            consumerInput, "consumer.module", providerInputArray);
        AssertNonReturningOutputCall(loadedConsumer);
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(
            loadedConsumer, "consumer.module", schemaIdentity, 1, []);
        var reloadedConsumer = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reemittedArtifact), "consumer.module", providerInputArray);
        AssertNonReturningOutputCall(reloadedConsumer);

        static void AssertNonReturningOutputCall(BoundProgram program)
        {
            var invoke = program.Functions.Single(static function =>
                function.Name == "Invoke");
            Assert.That(invoke.DoesNotReturn, Is.True);
            Assert.That(invoke.Body.Statements.Any(static statement =>
                statement is BoundReturn), Is.False);
            BoundCall? foreignCall = null;
            Assert.That(BoundTreeTraversal.VisitExpressions(invoke.Body,
                expression =>
                {
                    if (expression is BoundCall {
                            ForeignInterfaceCall: not null,
                        } call)
                    {
                        foreignCall = call;
                    }
                }), Is.True);
            var selectedCall = foreignCall
                ?? throw new AssertionException("Expected an FNI call.");
            Assert.That(selectedCall.Type, Is.EqualTo(PrimitiveType.Int32));
            Assert.That(selectedCall.ForeignInterfaceCall
                ?.DoesNotReturn, Is.True);
            Assert.That(selectedCall.ForeignInterfaceCall
                ?.NativeOutputContracts.Single().FormalOrdinal,
                Is.EqualTo(1));
            Assert.That(selectedCall.Arguments[1],
                Is.TypeOf<BoundNativeOutputReservation>());
            var reservation = (BoundNativeOutputReservation)
                selectedCall.Arguments[1];
            Assert.That(invoke.Variables[reservation.Variable]
                .IsInitializationLocal, Is.True);
        }
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
        var reboundImage = NativeEmitter.EmitLibrary(
            reboundConsumer.Program
                ?? throw new AssertionException(
                    "Expected a bound aggregate consumer."),
            ["Invoke"], NativeRuntimePolicy.FailFast);
        Assert.That(reboundImage[..2], Is.EqualTo(new byte[] {
            (byte)'M', (byte)'Z',
        }));
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
    public void StandardForeignNativeInterfaceBindsNestedAggregateProviderChain()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-native-interface-parameter-surface-1";
        const string leafSource = """
            using Terrum.Interop;
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Leaf {
                public int32 Value;
            }
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface ILeaf {
                public unsafe int32 Read(ptr<Leaf> leaf);
            }
            """;
        const string middleSource = """
            using Terrum.Interop;
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Middle {
                public Leaf Child;
            }
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(8))]
            public interface IMiddle {
                public unsafe int32 Read(ptr<Middle> middle);
            }
            """;
        var registration = CreateRegistration(
            includeForeignNativeInterface: true);
        var options = new CompilationOptions {
            NativeCoreRegistration = registration,
            LayoutCoreRegistration = CreateLayoutRegistration(registration),
        };
        var schema
            = TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft;

        byte[] EmitProvider(string moduleIdentity, string source,
            TrRmSourceTypeExport structureExport, string interfaceName,
            string interfaceExportKey)
        {
            var compilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(source), options);
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = compilation.Program
                ?? throw new AssertionException(
                    "Expected a bound FNI aggregate provider.");
            var members = program.Interfaces.Single(
                definition => StringComparer.Ordinal.Equals(
                    definition.QualifiedName, interfaceName)).Members;
            var interfaceExport = new TrRmSourceTypeExport("",
                interfaceName, interfaceExportKey) {
                GenericArity = 0,
                InterfaceMethodExports = [.. members.Select(member =>
                    new TrRmSourceInterfaceMethodExport(
                        member.SourceInputOrdinal,
                        member.SourceDeclarationSpan,
                        $"{interfaceExportKey}.{member.Name}"))],
            };
            return TrRmExecutableCompiler.EmitStandard(program,
                moduleIdentity, schemaIdentity, 1, [
                    structureExport,
                    interfaceExport,
                ]);
        }

        var leafArtifact = EmitProvider("leaf.module", leafSource,
            new TrRmSourceTypeExport("", "Leaf", "leaf/Leaf") {
                FieldExports = [
                    new TrRmSourceFieldExport("Value", "leaf/Leaf.Value"),
                ],
            }, "ILeaf", "leaf/ILeaf");
        var leafReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, leafArtifact),
            "leaf.module", []);
        var middleCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(middleSource), leafReference, options);
        Assert.That(middleCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                middleCompilation.Diagnostics));
        var middleProgram = middleCompilation.Program
            ?? throw new AssertionException(
                "Expected a bound middle FNI provider.");
        var middleMember = middleProgram.Interfaces.Single(
            static definition => definition.QualifiedName == "IMiddle")
            .Members.Single();
        var middleArtifact = TrRmExecutableCompiler.EmitStandard(
            middleProgram, "middle.module", schemaIdentity, 1, [
                new TrRmSourceTypeExport("", "Middle",
                    "middle/Middle") {
                    FieldExports = [
                        new TrRmSourceFieldExport("Child",
                            "middle/Middle.Child"),
                    ],
                },
                new TrRmSourceTypeExport("", "IMiddle",
                    "middle/IMiddle") {
                    GenericArity = 0,
                    InterfaceMethodExports = [
                        new TrRmSourceInterfaceMethodExport(
                            middleMember.SourceInputOrdinal,
                            middleMember.SourceDeclarationSpan,
                            "middle/IMiddle.Read"),
                    ],
                },
            ]);
        var middleTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                middleArtifact, schema));
        var nestedLeafImport = Enumerable.Range(1,
                middleTables.Imports.Count)
            .Single(importRow => {
                var definitionId = middleTables.Imports
                    .GetExpectedDefinitionForImportRow(importRow);
                return (definitionId >> 24) == 0x13
                    && middleTables.TypeDefs.GetKind(checked((int)(
                        definitionId & 0x00FF_FFFF))) == 1
                    && middleTables.Imports.GetBindingForImportRow(
                        importRow).ExportKey == "leaf/Leaf";
            });
        var leafModuleRef = middleTables.Imports
            .GetModuleRefForImportRow(nestedLeafImport);
        Assert.That(middleTables.ModuleRefs.GetProducerIdentity(
                leafModuleRef),
            Is.EqualTo("leaf.module"));

        var middleReference = TrRmExecutableCompiler
            .CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    middleArtifact), "middle.module", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        leafArtifact),
                ]);
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                int32 Invoke(ptr<void> receiver, ptr<Middle> middle) {
                    unsafe { return IMiddle.Read(receiver, middle); }
                }
                """), middleReference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new AssertionException(
                "Expected a bound nested aggregate consumer.");
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "consumer.module", schemaIdentity, 1, []);
        var consumerTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact, schema));
        var structureImportKeys = Enumerable.Range(1,
                consumerTables.Imports.Count)
            .Where(importRow => {
                var definitionId = consumerTables.Imports
                    .GetExpectedDefinitionForImportRow(importRow);
                return (definitionId >> 24) == 0x13
                    && consumerTables.TypeDefs.GetKind(checked((int)(
                        definitionId & 0x00FF_FFFF))) == 1;
            })
            .Select(importRow => consumerTables.Imports
                .GetBindingForImportRow(importRow).ExportKey)
            .OrderBy(static exportKey => exportKey, StringComparer.Ordinal);
        Assert.That(structureImportKeys,
            Is.EqualTo(new[] { "leaf/Leaf", "middle/Middle" }));

        int GetStructureImportRow(string exportKey)
            => Enumerable.Range(1, consumerTables.Imports.Count)
                .Single(importRow => {
                    var definitionId = consumerTables.Imports
                        .GetExpectedDefinitionForImportRow(importRow);
                    return (definitionId >> 24) == 0x13
                        && consumerTables.TypeDefs.GetKind(checked((int)(
                            definitionId & 0x00FF_FFFF))) == 1
                        && consumerTables.Imports.GetBindingForImportRow(
                            importRow).ExportKey == exportKey;
                });

        byte[] RewriteSection(byte[] source, ushort sectionKind,
            byte[] replacement, int replacementCount)
        {
            var sourceIndex = new TrRmArtifactIndex(source, schema);
            var sections = new List<TrRmSection>(
                sourceIndex.SectionCount);
            var replaced = false;
            for (var sectionIndex = 0;
                sectionIndex < sourceIndex.SectionCount;
                sectionIndex++)
            {
                sourceIndex.GetSection(sectionIndex, out var kind,
                    out var flags, out var bytes, out var recordCount);
                if (kind == sectionKind)
                {
                    sections.Add(new TrRmSection(kind, flags,
                        checked((uint)replacementCount), replacement));
                    replaced = true;
                }
                else
                {
                    sections.Add(new TrRmSection(kind, flags,
                        checked((uint)recordCount), bytes.ToArray()));
                }
            }

            if (!replaced)
            {
                throw new AssertionException(
                    $"Expected section 0x{sectionKind:X4}.");
            }

            return TrRmArtifactWriter.Encode(sections, schema);
        }

        var consumerIndex = new TrRmArtifactIndex(consumerArtifact,
            schema);
        Assert.That(consumerIndex.TryGetSection(0x001C,
                out var importBytes, out var importCount), Is.True);
        var leafImportRow = GetStructureImportRow("leaf/Leaf");
        var middleImportRow = GetStructureImportRow("middle/Middle");
        var leafImportOffset = checked((leafImportRow - 1) * 12);
        var middleImportOffset = checked((middleImportRow - 1) * 12);
        var importBytesArray = importBytes.ToArray();

        var missingImportBytes = new byte[importBytesArray.Length - 12];
        importBytesArray.AsSpan(0, leafImportOffset).CopyTo(
            missingImportBytes);
        importBytesArray.AsSpan(leafImportOffset + 12).CopyTo(
            missingImportBytes.AsSpan(leafImportOffset));
        var missingLeafImportArtifact = RewriteSection(consumerArtifact,
            0x001C, missingImportBytes, importCount - 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    missingLeafImportArtifact), "consumer.module", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        middleArtifact),
                    new TrRmStandardArtifactInput(schemaIdentity,
                        leafArtifact),
                ]));

        var duplicateImportBytes
            = new byte[importBytesArray.Length + 12];
        importBytesArray.CopyTo(duplicateImportBytes, 0);
        importBytesArray.AsSpan(leafImportOffset, 12).CopyTo(
            duplicateImportBytes.AsSpan(importBytesArray.Length));
        var duplicateLeafImportArtifact = RewriteSection(consumerArtifact,
            0x001C, duplicateImportBytes, importCount + 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    duplicateLeafImportArtifact), "consumer.module", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        middleArtifact),
                    new TrRmStandardArtifactInput(schemaIdentity,
                        leafArtifact),
                ]));

        var wrongLeafExportKeyBytes = importBytesArray.ToArray();
        var middleExportKeyStringRow = BinaryPrimitives
            .ReadInt32LittleEndian(wrongLeafExportKeyBytes.AsSpan(
                middleImportOffset + 4, 4));
        BinaryPrimitives.WriteInt32LittleEndian(
            wrongLeafExportKeyBytes.AsSpan(leafImportOffset + 4, 4),
            middleExportKeyStringRow);
        var wrongLeafExportKeyArtifact = RewriteSection(consumerArtifact,
            0x001C, wrongLeafExportKeyBytes, importCount);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    wrongLeafExportKeyArtifact), "consumer.module", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        middleArtifact),
                    new TrRmStandardArtifactInput(schemaIdentity,
                        leafArtifact),
                ]));

        Assert.That(consumerIndex.TryGetSection(0x0010,
                out var moduleRefBytes, out var moduleRefCount), Is.True);
        var leafModuleRef = consumerTables.Imports
            .GetModuleRefForImportRow(leafImportRow);
        var wrongLeafRequirements = moduleRefBytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            wrongLeafRequirements.AsSpan((leafModuleRef - 1) * 12 + 4, 4),
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement + 1);
        var wrongLeafRequirementsArtifact = RewriteSection(consumerArtifact,
            0x0010, wrongLeafRequirements, moduleRefCount);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    wrongLeafRequirementsArtifact), "consumer.module", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        middleArtifact),
                    new TrRmStandardArtifactInput(schemaIdentity,
                        leafArtifact),
                ]));

        var reboundReference = TrRmExecutableCompiler
            .CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    consumerArtifact), "consumer.module", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        middleArtifact),
                    new TrRmStandardArtifactInput(schemaIdentity,
                        leafArtifact),
                ]);
        var reboundCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                int32 Invoke(ptr<void> receiver, ptr<Middle> middle) {
                    unsafe { return IMiddle.Read(receiver, middle); }
                }
                """), reboundReference);
        Assert.That(reboundCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reboundCompilation.Diagnostics));
        var reboundImage = NativeEmitter.EmitLibrary(
            reboundCompilation.Program
                ?? throw new AssertionException(
                    "Expected a rebound nested aggregate consumer."),
            ["Invoke"], NativeRuntimePolicy.FailFast);
        Assert.That(reboundImage[..2], Is.EqualTo(new byte[] {
            (byte)'M', (byte)'Z',
        }));

        var reemissionCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(""), reboundReference);
        Assert.That(reemissionCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reemissionCompilation.Diagnostics));
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(
            reemissionCompilation.Program
                ?? throw new AssertionException(
                    "Expected a source-free nested aggregate forwarder."),
            "forwarder.module", schemaIdentity, 1, []);
        var reloadedReference = TrRmExecutableCompiler
            .CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    reemittedArtifact), "forwarder.module", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        middleArtifact),
                    new TrRmStandardArtifactInput(schemaIdentity,
                        leafArtifact),
                ]);
        Assert.That(reloadedReference.Interfaces.Any(
            static definition => definition.QualifiedName == "IMiddle"),
            Is.True);
        var reloadedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                int32 Invoke(ptr<void> receiver, ptr<Middle> middle) {
                    unsafe { return IMiddle.Read(receiver, middle); }
                }
                """), reloadedReference);
        Assert.That(reloadedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reloadedCompilation.Diagnostics));

        var incompatibleLeafArtifact = EmitProvider("leaf.module",
            leafSource.Replace("public int32 Value;",
                "public int64 Value;", StringComparison.Ordinal),
            new TrRmSourceTypeExport("", "Leaf", "leaf/Leaf") {
                FieldExports = [
                    new TrRmSourceFieldExport("Value", "leaf/Leaf.Value"),
                ],
            }, "ILeaf", "leaf/ILeaf");
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    consumerArtifact), "consumer.module", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        middleArtifact),
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleLeafArtifact),
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

    private static async Task AssertAggregatePointOutputAsync(byte[] image,
        string functionName)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"native-fni-aggregate-output-{Guid.NewGuid():N}.dll");
        var module = IntPtr.Zero;
        var receiver = IntPtr.Zero;
        var vtable = IntPtr.Zero;
        var source = IntPtr.Zero;
        NativeOutputPointFunction? callback = null;
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            module = NativeLibrary.Load(path);
            receiver = Marshal.AllocHGlobal(IntPtr.Size);
            vtable = Marshal.AllocHGlobal(IntPtr.Size);
            source = Marshal.AllocHGlobal(sizeof(int));
            callback = (actualReceiver, actualSource, value) => {
                if (actualReceiver != receiver
                    || Marshal.ReadInt32(actualSource) == 0)
                {
                    return 0;
                }

                Marshal.WriteInt64(value, 0x1122_3344_5566_7788);
                Marshal.WriteInt64(value, sizeof(long),
                    unchecked((long)0x8877_6655_4433_2211));
                return 1;
            };
            Marshal.WriteIntPtr(vtable,
                Marshal.GetFunctionPointerForDelegate(callback));
            Marshal.WriteIntPtr(receiver, vtable);
            Marshal.WriteInt32(source, 1);

            var invoke = Marshal.GetDelegateForFunctionPointer<
                NativeAggregatePointFunction>(
                    NativeLibrary.GetExport(module, functionName));
            var fallback = new NativeAggregatePoint {
                X = 3,
                Y = 4,
            };
            var result = invoke(receiver, source, fallback);
            Assert.That(result.X, Is.EqualTo(0x1122_3344_5566_7788));
            Assert.That(result.Y,
                Is.EqualTo(unchecked((long)0x8877_6655_4433_2211)));

            Marshal.WriteInt32(source, 0);
            result = invoke(receiver, source, fallback);
            Assert.That(result.X, Is.EqualTo(fallback.X));
            Assert.That(result.Y, Is.EqualTo(fallback.Y));
            GC.KeepAlive(callback);
        }
        finally
        {
            if (module != IntPtr.Zero)
            {
                NativeLibrary.Free(module);
            }
            if (source != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(source);
            }
            if (vtable != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(vtable);
            }
            if (receiver != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(receiver);
            }
            await TestFileCleanup.DeleteWithRetryAsync(path)
                .ConfigureAwait(true);
        }
    }

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
