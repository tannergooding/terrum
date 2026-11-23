// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
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

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeAggregateNestedPayload
        NativeAggregateNestedPayloadFunction(IntPtr receiver, IntPtr source,
            IntPtr tagSource, int mode,
            NativeAggregateNestedPayload fallback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeAggregateNestedPayload
        NativeAggregateLiteralPayloadFunction(IntPtr receiver,
            IntPtr source, NativeAggregateNestedPayload fallback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeTagFunction(IntPtr receiver, IntPtr source);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeOutputTaggedPayloadFunction(IntPtr receiver,
        IntPtr source, int tag, int mode, IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativePointerOutputCallback(IntPtr receiver,
        IntPtr source, int tag, out IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeFunctionPointerMultiOutputCallback(
        IntPtr receiver, IntPtr source, int tag, out int scalarValue,
        out IntPtr functionPointerValue);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr NativePointerOutputFunction(IntPtr receiver,
        IntPtr source, int tag, IntPtr fallback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeFniReadPairCallback(IntPtr receiver,
        int first, int second);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeFniUpdateFunction(IntPtr receiver, int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeFniUpdateReceiverFunction(IntPtr receiver);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeFunctionPointerTarget(int value);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeAggregatePoint
    {
        public long X;
        public long Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeAggregateNestedInner
    {
        public long X;
        public long Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeAggregateNestedPayload
    {
        public NativeAggregateNestedInner Inner;
        public long Z;
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
                    if (expression is BoundCall call
                        && call.ForeignInterfaceCall?.Member.Name == "Read")
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

    [TestCase("int32", "value + 1", (byte)0x21, TokenKind.Plus, false,
        SourceArithmeticProfile.Release, BoundIntegerOverflowBehavior.Wrapping,
        (ushort)2, false)]
    [TestCase("int32", "value - 1", (byte)0x22, TokenKind.Minus, false,
        SourceArithmeticProfile.Release, BoundIntegerOverflowBehavior.Wrapping,
        (ushort)2, false)]
    [TestCase("int32", "value * 2", (byte)0x23, TokenKind.Star, false,
        SourceArithmeticProfile.Release, BoundIntegerOverflowBehavior.Wrapping,
        (ushort)2, false)]
    [TestCase("int32", "-value", (byte)0xC0, TokenKind.Minus, true,
        SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, (ushort)1, false)]
    [TestCase("int32", "value + 1", (byte)0xC0, TokenKind.Plus, false,
        SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, (ushort)2, false)]
    [TestCase("int32", "value / 2", (byte)0xC0, TokenKind.Slash, false,
        SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, (ushort)2, false)]
    [TestCase("int32", "value % 2", (byte)0xC0, TokenKind.Percent, false,
        SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, (ushort)2, false)]
    [TestCase("int32", "~value", (byte)0x33, TokenKind.Tilde, true,
        SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, (ushort)1, false)]
    [TestCase("int32", "value & 2", (byte)0x30,
        TokenKind.Ampersand, false, SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, (ushort)2, false)]
    [TestCase("int32", "value | 2", (byte)0x31,
        TokenKind.Pipe, false, SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, (ushort)2, false)]
    [TestCase("int32", "value ^ 2", (byte)0x32,
        TokenKind.Caret, false, SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, (ushort)2, false)]
    [TestCase("int32", "+value", (byte)0x11, TokenKind.Plus, true,
        SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, (ushort)1, false)]
    [TestCase("float64", "+value", (byte)0x11, TokenKind.Plus, true,
        SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, (ushort)1, false)]
    [TestCase("bool", "!value", (byte)0x33, TokenKind.Bang, true,
        SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, (ushort)1, false)]
    [TestCase("bool", "value & true", (byte)0x30,
        TokenKind.Ampersand, false, SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, (ushort)2, false)]
    [TestCase("bool", "value && true", (byte)0x30,
        TokenKind.Ampersand, false, SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, (ushort)2, false)]
    [TestCase("bool", "value || false", (byte)0x31,
        TokenKind.Pipe, false, SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, (ushort)2, false)]
    [TestCase("bool", "value == true", (byte)0xC2,
        TokenKind.EqualsEquals, false, SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, (ushort)2, false)]
    [TestCase("bool", "value != false", (byte)0xC2,
        TokenKind.BangEquals, false, SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, (ushort)2, false)]
    [TestCase("uint32", "value << value", (byte)0x34,
        TokenKind.ShiftLeft, false, SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, (ushort)1, false)]
    [TestCase("uint32", "value >> value", (byte)0x35,
        TokenKind.ShiftRight, false, SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, (ushort)1, false)]
    [TestCase("uint32", "value >>> value", (byte)0x36,
        TokenKind.ShiftRightUnsigned, false,
        SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, (ushort)1, false)]
    [TestCase("int32", "value << count", (byte)0x34,
        TokenKind.ShiftLeft, false, SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, (ushort)1, false)]
    [TestCase("int32", "value >> count", (byte)0x35,
        TokenKind.ShiftRight, false, SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, (ushort)1, false)]
    [TestCase("int32", "value >>> count", (byte)0x36,
        TokenKind.ShiftRightUnsigned, false,
        SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, (ushort)1, false)]
    [TestCase("int32", "value + 1", (byte)0x21, TokenKind.Plus, false,
        SourceArithmeticProfile.Release, BoundIntegerOverflowBehavior.Wrapping,
        (ushort)2, true)]
    [TestCase("float64", "-value", (byte)0x26, TokenKind.Minus, true,
        SourceArithmeticProfile.Development, BoundIntegerOverflowBehavior.Checked,
        (ushort)1, false)]
    [TestCase("float64", "value + 1", (byte)0x27, TokenKind.Plus, false,
        SourceArithmeticProfile.Development, BoundIntegerOverflowBehavior.Checked,
        (ushort)2, false)]
    [TestCase("float64", "value - 1", (byte)0x28, TokenKind.Minus, false,
        SourceArithmeticProfile.Development, BoundIntegerOverflowBehavior.Checked,
        (ushort)2, false)]
    [TestCase("float64", "value * 2", (byte)0x29, TokenKind.Star, false,
        SourceArithmeticProfile.Development, BoundIntegerOverflowBehavior.Checked,
        (ushort)2, false)]
    [TestCase("float64", "value / 2", (byte)0x2A, TokenKind.Slash, false,
        SourceArithmeticProfile.Development, BoundIntegerOverflowBehavior.Checked,
        (ushort)2, false)]
    [TestCase("float64", "value % 2", (byte)0x2B, TokenKind.Percent, false,
        SourceArithmeticProfile.Development, BoundIntegerOverflowBehavior.Checked,
        (ushort)2, false)]
    public void StandardForeignNativeInterfaceCallsAcceptScalarExpressionInputs(
        string type, string expression, byte expectedOpcode,
        TokenKind expectedOperator, bool unary,
        SourceArithmeticProfile arithmeticProfile,
        BoundIntegerOverflowBehavior expectedOverflowBehavior,
        ushort expectedInputRegister, bool withVoidCallPrefix)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-native-interface-parameter-surface-1";
        var registration = CreateRegistration(
            includeForeignNativeInterface: true);
        var options = new CompilationOptions {
            NativeCoreRegistration = registration,
            LayoutCoreRegistration = CreateLayoutRegistration(registration),
            SourceArithmeticProfile = arithmeticProfile,
        };
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse($$"""
                using Terrum.Interop;
                [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
                [ForeignNativeInterface(VTableByteOffset: uint32(0))]
                public interface IProbe {
                    public unsafe {{type}} Read({{type}} value);
                    public unsafe void Touch();
                }
                """), options);
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
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "provider.module", []);
        var prefixCall = withVoidCallPrefix
            ? "IProbe.Touch(receiver);"
            : "";
        var shiftCountParameter = expression.Contains("count",
                StringComparison.Ordinal)
            ? ", uint32 count" : "";
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse($$"""
                {{type}} InvokeComputed(ptr<void> receiver, {{type}} value{{shiftCountParameter}}) {
                    unsafe {
                        {{prefixCall}}
                        return IProbe.Read(receiver, {{expression}});
                    }
                }
                """), reference, options);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new AssertionException("Expected a bound consumer.");
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "consumer.module", schemaIdentity, 1, []);
        var consumerTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact,
                TrRmArtifactSchema
                    .ForeignNativeInterfaceParameterSurfaceDraft));
        var invokeRow = Enumerable.Range(1, consumerTables.Methods.Count)
            .Single(row =>
                consumerTables.Methods.GetName(row) == "InvokeComputed");
        var body = consumerTables.Bodies.GetRegisterBody(
            consumerTables.Methods.GetBodyRow(invokeRow),
            consumerTables.Contracts.Count);
        Assert.That(body.GetBlockCode(0).ToArray(),
            Does.Contain(expectedOpcode));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(body.MethodMap)
                >> 24,
            Is.EqualTo(0x1Cu));
        var callSite = body.GetRecord(withVoidCallPrefix ? 2 : 1);
        Assert.That((callSite[24], callSite[25],
                BinaryPrimitives.ReadUInt16LittleEndian(callSite[26..])),
            Is.EqualTo(((byte)1, (byte)1, expectedInputRegister)));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "consumer.module", [providerInput]);
        AssertComputedInput(loaded, type, expectedOperator, unary,
            expectedOverflowBehavior);
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            "consumer.module", schemaIdentity, 1, []);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reemittedArtifact), "consumer.module", [providerInput]);
        AssertComputedInput(reloaded, type, expectedOperator, unary,
            expectedOverflowBehavior);

        static void AssertComputedInput(BoundProgram program, string typeName,
            TokenKind operatorKind, bool unary,
            BoundIntegerOverflowBehavior overflowBehavior)
        {
            var expectedType = typeName switch {
                "int32" => PrimitiveType.Int32,
                "uint32" => PrimitiveType.UInt32,
                "float64" => PrimitiveType.Float64,
                "bool" => PrimitiveType.Bool,
                _ => throw new AssertionException(
                    "The test selected an unsupported scalar type."),
            };
            var function = program.Functions.Single(
                static candidate => candidate.Name == "InvokeComputed");
            BoundCall? foreignCall = null;
            Assert.That(BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall call
                        && call.ForeignInterfaceCall?.Member.Name == "Read")
                    {
                        foreignCall = call;
                    }
                }), Is.True);
            Assert.That(foreignCall?.Function,
                Is.EqualTo(BoundCall.ForeignInterfaceFunction));
            var argument = foreignCall!.Arguments.Single();
            if (unary)
            {
                if (operatorKind == TokenKind.Plus)
                {
                    Assert.That(argument, Is.TypeOf<BoundRead>());
                    Assert.That(((BoundRead)argument).Variable,
                        Is.EqualTo(1));
                    Assert.That(argument.Type, Is.EqualTo(expectedType));
                    return;
                }

                Assert.That(argument, Is.TypeOf<BoundUnary>());
                var unaryExpression = (BoundUnary)argument;
                Assert.That(unaryExpression.Operator,
                    Is.EqualTo(operatorKind));
                Assert.That(unaryExpression.OverflowBehavior,
                    Is.EqualTo(overflowBehavior));
                Assert.That(unaryExpression.Operand, Is.TypeOf<BoundRead>());
                Assert.That(((BoundRead)unaryExpression.Operand).Variable,
                    Is.EqualTo(1));
                Assert.That(unaryExpression.Type, Is.EqualTo(expectedType));
                return;
            }

            Assert.That(argument, Is.TypeOf<BoundBinary>());
            var binary = (BoundBinary)argument;
            Assert.That(binary.Operator, Is.EqualTo(operatorKind));
            Assert.That(binary.OverflowBehavior,
                Is.EqualTo(overflowBehavior));
            Assert.That(binary.Left, Is.TypeOf<BoundRead>());
            Assert.That(((BoundRead)binary.Left).Variable, Is.EqualTo(1));
            if (operatorKind is TokenKind.ShiftLeft or TokenKind.ShiftRight
                or TokenKind.ShiftRightUnsigned)
            {
                Assert.That(binary.Right.Type,
                    Is.EqualTo(PrimitiveType.UInt32));
                if (binary.Right is BoundRead countRead)
                {
                    Assert.That(countRead.Variable,
                        Is.EqualTo(expectedType == PrimitiveType.UInt32
                            ? 1 : 2));
                }
                else
                {
                    Assert.That(binary.Right, Is.TypeOf<BoundConstant>());
                }
            }
            else
            {
                Assert.That(binary.Right, Is.TypeOf<BoundConstant>());
                Assert.That(binary.Right.Type, Is.EqualTo(expectedType));
            }
        }
    }

    [Test]
    public void StandardForeignNativeInterfaceRejectsFallibleShortCircuitRightOperands()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-native-interface-parameter-surface-1";
        var registration = CreateRegistration(
            includeForeignNativeInterface: true);
        var options = new CompilationOptions {
            NativeCoreRegistration = registration,
            LayoutCoreRegistration = CreateLayoutRegistration(registration),
            SourceArithmeticProfile = SourceArithmeticProfile.Development,
        };
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Terrum.Interop;
                [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
                [ForeignNativeInterface(VTableByteOffset: uint32(0))]
                public interface IProbe {
                    public unsafe bool Read(bool value);
                }
                """), options);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program
            ?? throw new AssertionException("Expected a bound provider.");
        var providerInterface = providerProgram.Interfaces.Single();
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "provider.module", schemaIdentity, 1, [
                new TrRmSourceTypeExport("", "IProbe", "provider/IProbe") {
                    GenericArity = 0,
                    InterfaceMethodExports = [.. providerInterface.Members
                        .Select(member =>
                            new TrRmSourceInterfaceMethodExport(
                                member.SourceInputOrdinal,
                                member.SourceDeclarationSpan,
                                $"provider/IProbe.{member.Name}"))],
                },
            ]);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, providerArtifact),
            "provider.module", []);
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                bool RejectShortCircuit(ptr<void> receiver, bool value,
                    int32 divisor) {
                    unsafe {
                        return IProbe.Read(receiver,
                            value && (int32(1) / divisor != 0));
                    }
                }
                """), reference, options);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(
                consumerCompilation.Program!, "consumer.module",
                schemaIdentity, 1, []));
    }

    [TestCase("&&", (byte)0x30, TokenKind.Ampersand)]
    [TestCase("||", (byte)0x31, TokenKind.Pipe)]
    public void StandardForeignNativeInterfacePreservesLogicalCheckedAndNestedOrder(
        string logicalOperator, byte expectedOpcode,
        TokenKind decodedOperator)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-native-interface-parameter-surface-1";
        var registration = CreateRegistration(
            includeForeignNativeInterface: true);
        var options = new CompilationOptions {
            NativeCoreRegistration = registration,
            LayoutCoreRegistration = CreateLayoutRegistration(registration),
            SourceArithmeticProfile = SourceArithmeticProfile.Development,
        };
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Terrum.Interop;
                [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
                [ForeignNativeInterface(VTableByteOffset: uint32(0))]
                public interface IProbe {
                    public unsafe bool ReadBoolean(bool first, bool second);
                    public unsafe bool NestedBoolean(bool value);
                }
                """), options);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program
            ?? throw new AssertionException("Expected a bound provider.");
        var providerInterface = providerProgram.Interfaces.Single();
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "provider.module", schemaIdentity, 1, [
                new TrRmSourceTypeExport("", "IProbe", "provider/IProbe") {
                    GenericArity = 0,
                    InterfaceMethodExports = [.. providerInterface.Members
                        .Select(member =>
                            new TrRmSourceInterfaceMethodExport(
                                member.SourceInputOrdinal,
                                member.SourceDeclarationSpan,
                                $"provider/IProbe.{member.Name}"))],
                },
            ]);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "provider.module", []);
        var functionName = logicalOperator == "&&"
            ? "LogicalAnd" : "LogicalOr";
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse($$"""
                bool {{functionName}}(ptr<void> receiver, int32 numerator,
                    int32 divisor, bool flag, bool value) {
                    unsafe {
                        return IProbe.ReadBoolean(receiver,
                            first: (numerator / divisor != 0)
                                {{logicalOperator}} flag,
                            second: IProbe.NestedBoolean(receiver, value));
                    }
                }
                """), reference, options);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new AssertionException("Expected a bound consumer.");
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "consumer.module", schemaIdentity, 1, []);
        AssertInstructionOrder(consumerArtifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "consumer.module", [providerInput]);
        AssertArgumentOrder(loaded);
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            "consumer.module", schemaIdentity, 1, []);
        AssertInstructionOrder(reemittedArtifact);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemittedArtifact),
            "consumer.module", [providerInput]);
        AssertArgumentOrder(reloaded);

        void AssertInstructionOrder(byte[] artifact)
        {
            var tables = TrRmStructuralMetadataValidator
                .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                    artifact,
                    TrRmArtifactSchema
                        .ForeignNativeInterfaceParameterSurfaceDraft));
            var row = Enumerable.Range(1, tables.Methods.Count)
                .Single(candidate => tables.Methods.GetName(candidate)
                    == functionName);
            var body = tables.Bodies.GetRegisterBody(
                tables.Methods.GetBodyRow(row), tables.Contracts.Count);
            var code = body.GetBlockCode(0).ToArray();
            var checkedArithmeticOffset = Array.IndexOf(code, (byte)0xC0);
            var logicalOffset = Array.IndexOf(code, expectedOpcode);
            var nestedCallOffset = Array.IndexOf(code, (byte)0x90);
            Assert.That(checkedArithmeticOffset,
                Is.GreaterThanOrEqualTo(0), functionName);
            Assert.That(logicalOffset, Is.GreaterThan(checkedArithmeticOffset),
                functionName);
            Assert.That(nestedCallOffset, Is.GreaterThan(logicalOffset),
                functionName);
        }

        void AssertArgumentOrder(BoundProgram program)
        {
            var function = program.Functions.Single(
                candidate => candidate.Name == functionName);
            BoundCall? foreignCall = null;
            _ = BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall call
                        && call.ForeignInterfaceCall?.Member.Name
                            == "ReadBoolean")
                    {
                        foreignCall = call;
                    }
                });
            var selectedCall = foreignCall
                ?? throw new AssertionException(
                    "Expected the FNI ReadBoolean call.");
            Assert.That(selectedCall.ParameterIndex(0), Is.EqualTo(0));
            Assert.That(selectedCall.ParameterIndex(1), Is.EqualTo(1));
            var logicalInput = selectedCall.Arguments[0];
            Assert.That(logicalInput, Is.TypeOf<BoundBinary>());
            var logical = (BoundBinary)logicalInput;
            Assert.That(logical.Operator, Is.EqualTo(decodedOperator));
            Assert.That(logical.OverflowBehavior,
                Is.EqualTo(BoundIntegerOverflowBehavior.Checked));
            Assert.That(logical.Left, Is.TypeOf<BoundBinary>());
            var checkedDivision = (BoundBinary)logical.Left;
            Assert.That(checkedDivision.Operator,
                Is.EqualTo(TokenKind.BangEquals));
            Assert.That(checkedDivision.Left, Is.TypeOf<BoundBinary>());
            Assert.That(((BoundBinary)checkedDivision.Left).Operator,
                Is.EqualTo(TokenKind.Slash));
            Assert.That(selectedCall.Arguments[1],
                Is.TypeOf<BoundCall>());
            Assert.That(((BoundCall)selectedCall.Arguments[1])
                    .ForeignInterfaceCall?.Member.Name,
                Is.EqualTo("NestedBoolean"));
        }
    }

    [Test]
    public void StandardForeignNativeInterfaceCallsRoundTripTotalScalarConversions()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-native-interface-parameter-surface-1";
        var registration = CreateRegistration(
            includeForeignNativeInterface: true);
        var options = new CompilationOptions {
            NativeCoreRegistration = registration,
            LayoutCoreRegistration = CreateLayoutRegistration(registration),
        };
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Terrum.Interop;
                [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
                [ForeignNativeInterface(VTableByteOffset: uint32(0))]
                public interface IProbe {
                    public unsafe float64 Read(float64 value);
                }
                """), options);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program
            ?? throw new AssertionException("Expected a bound provider.");
        var providerInterface = providerProgram.Interfaces.Single();
        var providerRead = providerInterface.Members.Single();
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "provider.module", schemaIdentity, 1, [
                new TrRmSourceTypeExport("", "IProbe", "provider/IProbe") {
                    GenericArity = 0,
                    InterfaceMethodExports = [
                        new TrRmSourceInterfaceMethodExport(
                            providerRead.SourceInputOrdinal,
                            providerRead.SourceDeclarationSpan,
                            "provider/IProbe.Read"),
                    ],
                },
            ]);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "provider.module", []);
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                float64 Invoke(ptr<void> receiver, float32 value) {
                    unsafe {
                        return IProbe.Read(receiver, float64(value));
                    }
                }
                """), reference, options);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new AssertionException("Expected a bound consumer.");
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "consumer.module", schemaIdentity, 1, []);
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact,
                TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft));
        var invokeRow = Enumerable.Range(1, tables.Methods.Count)
            .Single(row => tables.Methods.GetName(row) == "Invoke");
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(invokeRow), tables.Contracts.Count);
        Assert.That(body.GetBlockCode(0).ToArray(),
            Does.Contain((byte)0x50));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "consumer.module", [providerInput]);
        AssertConversion(loaded);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            "consumer.module", schemaIdentity, 1, []);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            "consumer.module", [providerInput]);
        AssertConversion(reloaded);

        static void AssertConversion(BoundProgram program)
        {
            var function = program.Functions.Single(
                static candidate => candidate.Name == "Invoke");
            BoundCall? foreignCall = null;
            _ = BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall call
                        && call.ForeignInterfaceCall?.Member.Name == "Read")
                    {
                        foreignCall = call;
                    }
                });
            var selectedCall = foreignCall
                ?? throw new AssertionException("Expected the FNI Read call.");
            Assert.That(selectedCall.Arguments.Single(),
                Is.TypeOf<BoundCast>());
            var conversion = (BoundCast)selectedCall.Arguments.Single();
            Assert.That(conversion.Type,
                Is.EqualTo(PrimitiveType.Float64));
            Assert.That(conversion.Operand, Is.TypeOf<BoundRead>());
            Assert.That(conversion.Operand.Type,
                Is.EqualTo(PrimitiveType.Float32));
            Assert.That(((BoundRead)conversion.Operand).Variable,
                Is.EqualTo(1));
        }
    }

    [Test]
    public void StandardForeignNativeInterfaceCallsPreserveCheckedAndNestedInputOrder()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-native-interface-parameter-surface-1";
        var registration = CreateRegistration(
            includeForeignNativeInterface: true);
        var options = new CompilationOptions {
            NativeCoreRegistration = registration,
            LayoutCoreRegistration = CreateLayoutRegistration(registration),
            SourceArithmeticProfile = SourceArithmeticProfile.Development,
        };
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Terrum.Interop;
                [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
                [ForeignNativeInterface(VTableByteOffset: uint32(0))]
                public interface IProbe {
                    public unsafe void Ping(int32 value);
                    public unsafe int32 Read(int32 first, int32 second);
                    public unsafe int32 ReadNarrow(int8 first, int32 second);
                    public unsafe int32 Nested(int32 value);
                    public unsafe int32 ReadPointer(int32 first,
                        ptr<int32> target, int32 second);
                }
                """), options);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program
            ?? throw new AssertionException("Expected a bound provider.");
        var providerInterface = providerProgram.Interfaces.Single();
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "provider.module", schemaIdentity, 1, [
                new TrRmSourceTypeExport("", "IProbe", "provider/IProbe") {
                    GenericArity = 0,
                    InterfaceMethodExports = [.. providerInterface.Members
                        .Select(member =>
                            new TrRmSourceInterfaceMethodExport(
                                member.SourceInputOrdinal,
                                member.SourceDeclarationSpan,
                                $"provider/IProbe.{member.Name}"))],
                },
            ]);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "provider.module", []);
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                int32 NestedThenChecked(ptr<void> receiver, int32 first,
                    int32 divisor, int32 value) {
                    unsafe {
                        return IProbe.Read(receiver,
                            second: IProbe.Nested(receiver, value),
                            first: first / divisor);
                    }
                }
                int32 CheckedThenNested(ptr<void> receiver, int32 first,
                    int32 divisor, int32 value) {
                    unsafe {
                        return IProbe.Read(receiver,
                            first: first / divisor,
                            second: IProbe.Nested(receiver, value));
                    }
                }
                int32 ConversionThenNested(ptr<void> receiver,
                    int32 first, int32 value) {
                    unsafe {
                        return IProbe.ReadNarrow(receiver,
                            first: int8(first),
                            second: IProbe.Nested(receiver, value));
                    }
                }
                int32 NestedThenConversion(ptr<void> receiver,
                    int32 first, int32 value) {
                    unsafe {
                        return IProbe.ReadNarrow(receiver,
                            second: IProbe.Nested(receiver, value),
                            first: int8(first));
                    }
                }
                int32 SequenceNestedThenChecked(ptr<void> receiver,
                    int32 first, int32 divisor, int32 value) {
                    unsafe {
                        IProbe.Ping(receiver, value);
                        return IProbe.Read(receiver,
                            second: Identity(
                                IProbe.Nested(receiver, value)),
                            first: first / divisor);
                    }
                }
                int32 SequenceCheckedThenNested(ptr<void> receiver,
                    int32 first, int32 divisor, int32 value) {
                    unsafe {
                        IProbe.Ping(receiver, value);
                        return IProbe.Read(receiver,
                            first: first / divisor,
                            second: IProbe.Nested(receiver, value));
                    }
                }
                int32 SequenceConversionThenNested(ptr<void> receiver,
                    int32 first, int32 value) {
                    unsafe {
                        IProbe.Ping(receiver, value);
                        return IProbe.ReadNarrow(receiver,
                            first: int8(first),
                            second: IProbe.Nested(receiver, value));
                    }
                }
                int32 SequenceNestedThenConversion(ptr<void> receiver,
                    int32 first, int32 value) {
                    unsafe {
                        IProbe.Ping(receiver, value);
                        return IProbe.ReadNarrow(receiver,
                            second: IProbe.Nested(receiver, value),
                            first: int8(first));
                    }
                }
                int32 FloatConversionThenNested(ptr<void> receiver,
                    float64 first, int32 value) {
                    unsafe {
                        return IProbe.Read(receiver,
                            first: int32(first),
                            second: IProbe.Nested(receiver, value));
                    }
                }
                int32 NestedThenFloatConversion(ptr<void> receiver,
                    float64 first, int32 value) {
                    unsafe {
                        return IProbe.Read(receiver,
                            second: IProbe.Nested(receiver, value),
                            first: int32(first));
                    }
                }
                int32 SequenceFloatConversionThenNested(
                    ptr<void> receiver, float64 first, int32 value) {
                    unsafe {
                        IProbe.Ping(receiver, value);
                        return IProbe.Read(receiver,
                            first: int32(first),
                            second: IProbe.Nested(receiver, value));
                    }
                }
                int32 SequenceNestedThenFloatConversion(
                    ptr<void> receiver, float64 first, int32 value) {
                    unsafe {
                        IProbe.Ping(receiver, value);
                        return IProbe.Read(receiver,
                            second: IProbe.Nested(receiver, value),
                            first: int32(first));
                    }
                }
                int32 Identity(int32 value) { return value; }
                """), reference, options);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new AssertionException("Expected a bound consumer.");
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "consumer.module", schemaIdentity, 1, []);
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact,
                TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft));
        AssertInstructionOrder("NestedThenChecked", nestedFirst: true);
        AssertInstructionOrder("CheckedThenNested", nestedFirst: false);
        AssertSequenceInstructionOrder(consumerArtifact,
            "SequenceNestedThenChecked", nestedFirst: true,
            nestedCallCount: 2);
        AssertSequenceInstructionOrder(consumerArtifact,
            "SequenceCheckedThenNested", nestedFirst: false,
            nestedCallCount: 1);
        AssertConversionInstructionOrder(consumerArtifact,
            "ConversionThenNested", conversionFirst: true,
            expectedCallCount: 2);
        AssertConversionInstructionOrder(consumerArtifact,
            "NestedThenConversion", conversionFirst: false,
            expectedCallCount: 2);
        AssertConversionInstructionOrder(consumerArtifact,
            "SequenceConversionThenNested", conversionFirst: true,
            expectedCallCount: 3);
        AssertConversionInstructionOrder(consumerArtifact,
            "SequenceNestedThenConversion", conversionFirst: false,
            expectedCallCount: 3);
        AssertConversionInstructionOrder(consumerArtifact,
            "FloatConversionThenNested", conversionFirst: true,
            expectedCallCount: 2);
        AssertConversionInstructionOrder(consumerArtifact,
            "NestedThenFloatConversion", conversionFirst: false,
            expectedCallCount: 2);
        AssertConversionInstructionOrder(consumerArtifact,
            "SequenceFloatConversionThenNested", conversionFirst: true,
            expectedCallCount: 3);
        AssertConversionInstructionOrder(consumerArtifact,
            "SequenceNestedThenFloatConversion", conversionFirst: false,
            expectedCallCount: 3);

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "consumer.module", [providerInput]);
        AssertArgumentOrder(loaded, "NestedThenChecked", 1, 0);
        AssertArgumentOrder(loaded, "CheckedThenNested", 0, 1);
        AssertArgumentOrder(loaded, "SequenceNestedThenChecked", 1, 0);
        AssertArgumentOrder(loaded, "SequenceCheckedThenNested", 0, 1);
        AssertScalarConversionArgumentOrder(loaded, "ConversionThenNested",
            "ReadNarrow", true, PrimitiveType.Int32,
            PrimitiveType.Int8);
        AssertScalarConversionArgumentOrder(loaded, "NestedThenConversion",
            "ReadNarrow", false, PrimitiveType.Int32,
            PrimitiveType.Int8);
        AssertScalarConversionArgumentOrder(loaded,
            "SequenceConversionThenNested", "ReadNarrow",
            true, PrimitiveType.Int32, PrimitiveType.Int8);
        AssertScalarConversionArgumentOrder(loaded,
            "SequenceNestedThenConversion", "ReadNarrow",
            false, PrimitiveType.Int32, PrimitiveType.Int8);
        AssertScalarConversionArgumentOrder(loaded,
            "FloatConversionThenNested", "Read",
            true, PrimitiveType.Float64,
            PrimitiveType.Int32);
        AssertScalarConversionArgumentOrder(loaded,
            "NestedThenFloatConversion", "Read",
            false, PrimitiveType.Float64,
            PrimitiveType.Int32);
        AssertScalarConversionArgumentOrder(loaded,
            "SequenceFloatConversionThenNested", "Read",
            true, PrimitiveType.Float64,
            PrimitiveType.Int32);
        AssertScalarConversionArgumentOrder(loaded,
            "SequenceNestedThenFloatConversion", "Read",
            false, PrimitiveType.Float64,
            PrimitiveType.Int32);
        var image = NativeEmitter.EmitLibrary(loaded,
            ["FloatConversionThenNested"], NativeRuntimePolicy.FailFast);
        Assert.That(image[..2], Is.EqualTo(new byte[] {
            (byte)'M', (byte)'Z',
        }));
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            "consumer.module", schemaIdentity, 1, []);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            "consumer.module", [providerInput]);
        AssertArgumentOrder(reloaded, "NestedThenChecked", 1, 0);
        AssertArgumentOrder(reloaded, "CheckedThenNested", 0, 1);
        AssertArgumentOrder(reloaded, "SequenceNestedThenChecked", 1, 0);
        AssertArgumentOrder(reloaded, "SequenceCheckedThenNested", 0, 1);
        AssertScalarConversionArgumentOrder(reloaded, "ConversionThenNested",
            "ReadNarrow", true, PrimitiveType.Int32,
            PrimitiveType.Int8);
        AssertScalarConversionArgumentOrder(reloaded, "NestedThenConversion",
            "ReadNarrow", false, PrimitiveType.Int32,
            PrimitiveType.Int8);
        AssertScalarConversionArgumentOrder(reloaded,
            "SequenceConversionThenNested", "ReadNarrow",
            true, PrimitiveType.Int32, PrimitiveType.Int8);
        AssertScalarConversionArgumentOrder(reloaded,
            "SequenceNestedThenConversion", "ReadNarrow",
            false, PrimitiveType.Int32, PrimitiveType.Int8);
        AssertScalarConversionArgumentOrder(reloaded,
            "FloatConversionThenNested", "Read",
            true, PrimitiveType.Float64,
            PrimitiveType.Int32);
        AssertScalarConversionArgumentOrder(reloaded,
            "NestedThenFloatConversion", "Read",
            false, PrimitiveType.Float64,
            PrimitiveType.Int32);
        AssertScalarConversionArgumentOrder(reloaded,
            "SequenceFloatConversionThenNested", "Read",
            true, PrimitiveType.Float64,
            PrimitiveType.Int32);
        AssertScalarConversionArgumentOrder(reloaded,
            "SequenceNestedThenFloatConversion", "Read",
            false, PrimitiveType.Float64,
            PrimitiveType.Int32);
        AssertSequenceInstructionOrder(reemitted,
            "SequenceNestedThenChecked", nestedFirst: true,
            nestedCallCount: 2);
        AssertSequenceInstructionOrder(reemitted,
            "SequenceCheckedThenNested", nestedFirst: false,
            nestedCallCount: 1);
        AssertConversionInstructionOrder(reemitted,
            "ConversionThenNested", conversionFirst: true,
            expectedCallCount: 2);
        AssertConversionInstructionOrder(reemitted,
            "NestedThenConversion", conversionFirst: false,
            expectedCallCount: 2);
        AssertConversionInstructionOrder(reemitted,
            "SequenceConversionThenNested", conversionFirst: true,
            expectedCallCount: 3);
        AssertConversionInstructionOrder(reemitted,
            "SequenceNestedThenConversion", conversionFirst: false,
            expectedCallCount: 3);
        AssertConversionInstructionOrder(reemitted,
            "FloatConversionThenNested", conversionFirst: true,
            expectedCallCount: 2);
        AssertConversionInstructionOrder(reemitted,
            "NestedThenFloatConversion", conversionFirst: false,
            expectedCallCount: 2);
        AssertConversionInstructionOrder(reemitted,
            "SequenceFloatConversionThenNested", conversionFirst: true,
            expectedCallCount: 3);
        AssertConversionInstructionOrder(reemitted,
            "SequenceNestedThenFloatConversion", conversionFirst: false,
            expectedCallCount: 3);
        var pointerInputCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                int32 RejectPointerMix(ptr<void> receiver, int32 first,
                    int32 divisor, int32 nestedValue, ptr<int32> target) {
                    unsafe {
                        return IProbe.ReadPointer(receiver, first / divisor,
                            target, IProbe.Nested(receiver, nestedValue));
                    }
                }
                """), reference, options);
        Assert.That(pointerInputCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                pointerInputCompilation.Diagnostics));
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(
                pointerInputCompilation.Program!, "pointer-consumer.module",
                schemaIdentity, 1, []));
        var pointerSequenceCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                int32 RejectPointerSequenceMix(ptr<void> receiver,
                    int32 first, int32 divisor, int32 nestedValue,
                    ptr<int32> target) {
                    unsafe {
                        IProbe.Ping(receiver, nestedValue);
                        return IProbe.ReadPointer(receiver,
                            first / divisor, target,
                            IProbe.Nested(receiver, nestedValue));
                    }
                }
                """), reference, options);
        Assert.That(pointerSequenceCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                pointerSequenceCompilation.Diagnostics));
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(
                pointerSequenceCompilation.Program!,
                "pointer-sequence.module",
                schemaIdentity, 1, []));

        void AssertInstructionOrder(string name, bool nestedFirst)
        {
            var row = Enumerable.Range(1, tables.Methods.Count)
                .Single(candidate => tables.Methods.GetName(candidate)
                    == name);
            var body = tables.Bodies.GetRegisterBody(
                tables.Methods.GetBodyRow(row), tables.Contracts.Count);
            var code = body.GetBlockCode(0).ToArray();
            var nestedCallOffset = Array.IndexOf(code, (byte)0x90);
            var checkedArithmeticOffset = Array.IndexOf(code, (byte)0xC0);
            Assert.That(nestedCallOffset, Is.GreaterThanOrEqualTo(0), name);
            Assert.That(checkedArithmeticOffset,
                Is.GreaterThanOrEqualTo(0), name);
            Assert.That(code[checkedArithmeticOffset + 1],
                Is.EqualTo((byte)0x0C), name);
            Assert.That(nestedFirst
                    ? nestedCallOffset < checkedArithmeticOffset
                    : checkedArithmeticOffset < nestedCallOffset,
                Is.True, name);
        }

        static void AssertSequenceInstructionOrder(byte[] artifact,
            string name, bool nestedFirst, int nestedCallCount)
        {
            var tables = TrRmStructuralMetadataValidator
                .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                    artifact,
                    TrRmArtifactSchema
                        .ForeignNativeInterfaceParameterSurfaceDraft));
            var row = Enumerable.Range(1, tables.Methods.Count)
                .Single(candidate => tables.Methods.GetName(candidate)
                    == name);
            var body = tables.Bodies.GetRegisterBody(
                tables.Methods.GetBodyRow(row), tables.Contracts.Count);
            var code = body.GetBlockCode(0).ToArray();
            var callOffsets = new List<int>();
            for (var index = 0; index < code.Length - 1; index++)
            {
                if (code[index] == 0x90)
                {
                    callOffsets.Add(index);
                    index++;
                }
            }

            var checkedArithmeticOffset = Array.IndexOf(code, (byte)0xC0);
            Assert.That(callOffsets,
                Has.Count.EqualTo(nestedCallCount + 2), name);
            Assert.That(checkedArithmeticOffset,
                Is.GreaterThanOrEqualTo(0), name);
            var nestedCallOffsets = callOffsets.Skip(1)
                .Take(nestedCallCount).ToArray();
            Assert.That(callOffsets[0],
                Is.LessThan(Math.Min(nestedCallOffsets[0],
                    checkedArithmeticOffset)), name);
            Assert.That(nestedFirst
                    ? nestedCallOffsets[^1] < checkedArithmeticOffset
                    : checkedArithmeticOffset < nestedCallOffsets[0],
                Is.True, name);
            for (var index = 1; index < nestedCallOffsets.Length; index++)
            {
                Assert.That(nestedCallOffsets[index - 1],
                    Is.LessThan(nestedCallOffsets[index]), name);
            }

            Assert.That(callOffsets[^1],
                Is.GreaterThan(Math.Max(nestedCallOffsets[^1],
                    checkedArithmeticOffset)), name);
        }

        static void AssertConversionInstructionOrder(byte[] artifact,
            string name, bool conversionFirst, int expectedCallCount)
        {
            var tables = TrRmStructuralMetadataValidator
                .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                    artifact,
                    TrRmArtifactSchema
                        .ForeignNativeInterfaceParameterSurfaceDraft));
            var row = Enumerable.Range(1, tables.Methods.Count)
                .Single(candidate => tables.Methods.GetName(candidate)
                    == name);
            var body = tables.Bodies.GetRegisterBody(
                tables.Methods.GetBodyRow(row), tables.Contracts.Count);
            var code = body.GetBlockCode(0).ToArray();
            var callOffsets = new List<int>();
            var conversionOffsets = new List<int>();
            for (var index = 0; index < code.Length;)
            {
                switch (code[index])
                {
                    case 0x90:
                        callOffsets.Add(index);
                        index += 2;
                        break;
                    case 0xD0:
                        conversionOffsets.Add(index);
                        Assert.That(code[index + 1] & 0x0F,
                            Is.EqualTo(code[index + 1] >> 4), name);
                        index += 4;
                        break;
                    case 0xC0 or 0xC2 or 0xC3 or 0xD4 or 0xD5:
                        index += 4;
                        break;
                    default:
                        index += 2;
                        break;
                }
            }

            Assert.That(callOffsets, Has.Count.EqualTo(expectedCallCount),
                name);
            Assert.That(conversionOffsets, Has.Count.EqualTo(1), name);
            var nestedCallOffset = callOffsets[^2];
            var conversionOffset = conversionOffsets[0];
            if (expectedCallCount == 3)
            {
                Assert.That(callOffsets[0],
                    Is.LessThan(Math.Min(nestedCallOffset,
                        conversionOffset)), name);
            }

            Assert.That(conversionFirst
                    ? conversionOffset < nestedCallOffset
                    : nestedCallOffset < conversionOffset,
                Is.True, name);
            Assert.That(callOffsets[^1],
                Is.GreaterThan(Math.Max(nestedCallOffset,
                    conversionOffset)), name);
        }

        static void AssertArgumentOrder(BoundProgram program, string name,
            int firstFormal, int secondFormal)
        {
            var function = program.Functions.Single(
                candidate => candidate.Name == name);
            BoundCall? outerCall = null;
            _ = BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall call
                        && call.ForeignInterfaceCall?.Member.Name == "Read")
                    {
                        outerCall = call;
                    }
                });
            var selectedCall = outerCall
                ?? throw new AssertionException("Expected the FNI Read call.");
            Assert.That(selectedCall.Arguments.Length, Is.EqualTo(2));
            Assert.That(selectedCall.ParameterIndex(0),
                Is.EqualTo(firstFormal));
            Assert.That(selectedCall.ParameterIndex(1),
                Is.EqualTo(secondFormal));
            var nestedArgumentIndex = firstFormal == 1 ? 0 : 1;
            var nestedCallFound = false;
            _ = BoundTreeTraversal.VisitExpressions(
                selectedCall.Arguments[nestedArgumentIndex], expression =>
                {
                    if (expression is BoundCall call
                        && call.ForeignInterfaceCall?.Member.Name == "Nested")
                    {
                        Assert.That(call.Arguments.Single(),
                            Is.TypeOf<BoundRead>());
                        nestedCallFound = true;
                    }
                });
            Assert.That(nestedCallFound, Is.True);
            var checkedArgumentIndex = firstFormal == 0 ? 0 : 1;
            var checkedExpression
                = selectedCall.Arguments[checkedArgumentIndex];
            Assert.That(checkedExpression, Is.TypeOf<BoundBinary>());
            Assert.That(((BoundBinary)checkedExpression).Operator,
                Is.EqualTo(TokenKind.Slash));
            Assert.That(((BoundBinary)checkedExpression).OverflowBehavior,
                Is.EqualTo(BoundIntegerOverflowBehavior.Checked));
        }

        static void AssertScalarConversionArgumentOrder(
            BoundProgram program, string functionName, string memberName,
            bool conversionFirst, PrimitiveType sourceType,
            PrimitiveType destinationType)
        {
            var function = program.Functions.Single(
                candidate => candidate.Name == functionName);
            BoundCall? outerCall = null;
            _ = BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall call
                        && call.ForeignInterfaceCall?.Member.Name
                            == memberName)
                    {
                        outerCall = call;
                    }
                });
            var selectedCall = outerCall
                ?? throw new AssertionException(
                    $"Expected the FNI {memberName} call.");
            Assert.That(selectedCall.Arguments.Length, Is.EqualTo(2));
            Assert.That(selectedCall.ParameterIndex(0),
                Is.EqualTo(conversionFirst ? 0 : 1));
            Assert.That(selectedCall.ParameterIndex(1),
                Is.EqualTo(conversionFirst ? 1 : 0));
            var conversionIndex = conversionFirst ? 0 : 1;
            var conversion = selectedCall.Arguments[conversionIndex];
            Assert.That(conversion, Is.TypeOf<BoundCast>());
            var cast = (BoundCast)conversion;
            Assert.That(cast.Type, Is.EqualTo(destinationType));
            Assert.That(cast.Operand, Is.TypeOf<BoundRead>());
            Assert.That(cast.Operand.Type, Is.EqualTo(sourceType));
            var nestedIndex = conversionFirst ? 1 : 0;
            var nestedCallFound = false;
            _ = BoundTreeTraversal.VisitExpressions(
                selectedCall.Arguments[nestedIndex], expression =>
                {
                    if (expression is BoundCall call
                        && call.ForeignInterfaceCall?.Member.Name
                            == "Nested")
                    {
                        nestedCallFound = true;
                    }
                });
            Assert.That(nestedCallFound, Is.True);
        }
    }

    [TestCase(SourceArithmeticProfile.Development)]
    [TestCase(SourceArithmeticProfile.Release)]
    public void StandardForeignNativeInterfaceMutableUpdatesRoundTripArgumentOrder(
        SourceArithmeticProfile arithmeticProfile)
    {
        var (program, artifact, reemittedArtifact)
            = CreateMutableUpdateConsumerProgram(arithmeticProfile);
        var isChecked = arithmeticProfile
            == SourceArithmeticProfile.Development;
        var cases = new[] {
            (Name: "PrefixIncrementThenRead", UpdateArgument: 0,
                IsPrefix: true, Operator: TokenKind.PlusPlus,
                UpdateFirst: true, FirstFormal: 0, SecondFormal: 1),
            (Name: "ConstantInitializedPrefixIncrementThenRead",
                UpdateArgument: 0, IsPrefix: true,
                Operator: TokenKind.PlusPlus, UpdateFirst: true,
                FirstFormal: 0, SecondFormal: 1),
            (Name: "ReadThenPrefixIncrement", UpdateArgument: 1,
                IsPrefix: true, Operator: TokenKind.PlusPlus,
                UpdateFirst: false, FirstFormal: 0, SecondFormal: 1),
            (Name: "PostfixIncrementThenRead", UpdateArgument: 0,
                IsPrefix: false, Operator: TokenKind.PlusPlus,
                UpdateFirst: true, FirstFormal: 0, SecondFormal: 1),
            (Name: "ReadThenPostfixIncrement", UpdateArgument: 1,
                IsPrefix: false, Operator: TokenKind.PlusPlus,
                UpdateFirst: false, FirstFormal: 0, SecondFormal: 1),
            (Name: "ReversedFormalPrefixIncrementThenRead",
                UpdateArgument: 0, IsPrefix: true,
                Operator: TokenKind.PlusPlus, UpdateFirst: true,
                FirstFormal: 1, SecondFormal: 0),
            (Name: "ReversedFormalReadThenPostfixIncrement",
                UpdateArgument: 1, IsPrefix: false,
                Operator: TokenKind.PlusPlus, UpdateFirst: false,
                FirstFormal: 1, SecondFormal: 0),
            (Name: "PrefixDecrementThenRead", UpdateArgument: 0,
                IsPrefix: true, Operator: TokenKind.MinusMinus,
                UpdateFirst: true, FirstFormal: 0, SecondFormal: 1),
            (Name: "PostfixDecrementThenRead", UpdateArgument: 0,
                IsPrefix: false, Operator: TokenKind.MinusMinus,
                UpdateFirst: true, FirstFormal: 0, SecondFormal: 1),
        };

        foreach (var testCase in cases)
        {
            AssertUpdateCall(program, testCase, isChecked);
            AssertUpdateInstructionOrder(artifact, testCase, isChecked);
            AssertUpdateInstructionOrder(reemittedArtifact, testCase,
                isChecked);
        }

        var constantInitializedFunction = program.Functions.Single(
            static function => function.Name
                == "ConstantInitializedPrefixIncrementThenRead");
        var constantInitializedLocal
            = constantInitializedFunction.Body.Statements
                .OfType<BoundLocal>().Single();
        Assert.That(constantInitializedLocal.Initializer,
            Is.TypeOf<BoundConstant>());
        Assert.That(((BoundConstant)constantInitializedLocal.Initializer)
            .Bits.Low, Is.EqualTo(10UL));

        var image = NativeEmitter.EmitLibrary(program,
            [.. cases.Select(static testCase => testCase.Name)],
            NativeRuntimePolicy.FailFast);
        Assert.That(image[..2], Is.EqualTo(new byte[] {
            (byte)'M', (byte)'Z',
        }));

        static void AssertUpdateCall(BoundProgram program,
            (string Name, int UpdateArgument, bool IsPrefix,
                TokenKind Operator, bool UpdateFirst, int FirstFormal,
                int SecondFormal) testCase, bool checkedArithmetic)
        {
            var function = program.Functions.Single(
                candidate => candidate.Name == testCase.Name);
            BoundCall? outerCall = null;
            _ = BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall call
                        && call.ForeignInterfaceCall?.Member.Name == "Read")
                    {
                        outerCall = call;
                    }
                });
            var selectedCall = outerCall
                ?? throw new AssertionException(
                    "Expected the FNI Read call.");
            Assert.That(selectedCall.Arguments.Length, Is.EqualTo(2));
            Assert.That(selectedCall.ParameterIndex(0),
                Is.EqualTo(testCase.FirstFormal));
            Assert.That(selectedCall.ParameterIndex(1),
                Is.EqualTo(testCase.SecondFormal));

            var update = selectedCall.Arguments[testCase.UpdateArgument];
            Assert.That(update, Is.TypeOf<BoundUpdate>());
            var selectedUpdate = (BoundUpdate)update;
            Assert.That(selectedUpdate.Operator,
                Is.EqualTo(testCase.Operator));
            Assert.That(selectedUpdate.IsPrefix,
                Is.EqualTo(testCase.IsPrefix));
            Assert.That(selectedUpdate.OverflowBehavior,
                Is.EqualTo(checkedArithmetic
                    ? BoundIntegerOverflowBehavior.Checked
                    : BoundIntegerOverflowBehavior.Wrapping));
            Assert.That(selectedUpdate.Place, Is.TypeOf<BoundRead>());
            var variable = ((BoundRead)selectedUpdate.Place).Variable;
            var otherArgument
                = selectedCall.Arguments[1 - testCase.UpdateArgument];
            Assert.That(otherArgument, Is.TypeOf<BoundRead>());
            Assert.That(((BoundRead)otherArgument).Variable,
                Is.EqualTo(variable));
            Assert.That(function.Variables[variable].IsMutable, Is.True);
            Assert.That(function.Variables[variable].Type,
                Is.EqualTo(PrimitiveType.Int32));
        }

        static void AssertUpdateInstructionOrder(byte[] artifact,
            (string Name, int UpdateArgument, bool IsPrefix,
                TokenKind Operator, bool UpdateFirst, int FirstFormal,
                int SecondFormal) testCase, bool checkedArithmetic)
        {
            var tables = TrRmStructuralMetadataValidator
                .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                    artifact, TrRmArtifactSchema
                        .ForeignNativeInterfaceParameterSurfaceDraft));
            var row = Enumerable.Range(1, tables.Methods.Count)
                .Single(candidate => tables.Methods.GetName(candidate)
                    == testCase.Name);
            var body = tables.Bodies.GetRegisterBody(
                tables.Methods.GetBodyRow(row), tables.Contracts.Count);
            var code = body.GetBlockCode(0).ToArray();
            var homeLoads = new List<int>();
            var homeStores = new List<int>();
            var updateMoves = new List<int>();
            var arithmetic = new List<int>();
            var callSites = new List<int>();
            for (var index = 0; index < code.Length;)
            {
                var opcode = code[index];
                if (opcode == 0xD5)
                {
                    homeLoads.Add(index);
                }
                else if (opcode == 0xD6)
                {
                    homeStores.Add(index);
                }
                else if (opcode == 0x10)
                {
                    updateMoves.Add(index);
                }
                else if (opcode is 0xC0 or 0x21 or 0x22)
                {
                    arithmetic.Add(index);
                }
                else if (opcode == 0x90)
                {
                    callSites.Add(index);
                }

                index += opcode is 0xC0 or 0xD5 or 0xD6 ? 4 : 2;
            }

            Assert.That(homeLoads, Has.Count.EqualTo(2), testCase.Name);
            Assert.That(homeStores, Has.Count.EqualTo(2), testCase.Name);
            Assert.That(arithmetic, Has.Count.EqualTo(1), testCase.Name);
            Assert.That(updateMoves,
                Has.Count.EqualTo(testCase.IsPrefix ? 0 : 1),
                testCase.Name);
            Assert.That(callSites, Has.Count.EqualTo(1), testCase.Name);
            var expectedOpcode = checkedArithmetic
                ? (byte)0xC0
                : testCase.Operator == TokenKind.PlusPlus
                    ? (byte)0x21 : (byte)0x22;
            Assert.That(code[arithmetic[0]], Is.EqualTo(expectedOpcode),
                testCase.Name);
            if (checkedArithmetic)
            {
                Assert.That(code[arithmetic[0] + 1],
                    Is.EqualTo(testCase.Operator == TokenKind.PlusPlus
                        ? (byte)0x01 : (byte)0x02), testCase.Name);
            }

            Assert.That(homeStores[0], Is.LessThan(homeLoads[0]),
                testCase.Name);
            var updateLoad = homeLoads[testCase.UpdateFirst ? 0 : 1];
            Assert.That(updateLoad, Is.LessThan(arithmetic[0]),
                testCase.Name);
            if (updateMoves.Count != 0)
            {
                Assert.That(updateLoad, Is.LessThan(updateMoves[0]),
                    testCase.Name);
                Assert.That(updateMoves[0],
                    Is.LessThan(arithmetic[0]), testCase.Name);
            }

            Assert.That(arithmetic[0], Is.LessThan(homeStores[1]),
                testCase.Name);
            if (testCase.UpdateFirst)
            {
                Assert.That(homeStores[1], Is.LessThan(homeLoads[1]),
                    testCase.Name);
                Assert.That(homeLoads[1], Is.LessThan(callSites[0]),
                    testCase.Name);
            }
            else
            {
                Assert.That(homeLoads[0], Is.LessThan(homeLoads[1]),
                    testCase.Name);
                Assert.That(homeLoads[1], Is.LessThan(arithmetic[0]),
                    testCase.Name);
                Assert.That(homeStores[1], Is.LessThan(callSites[0]),
                    testCase.Name);
            }
        }
    }

    [Test]
    public async Task StandardForeignNativeInterfaceMutableUpdatesExecuteOnWindowsX64()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var (program, _, _) = CreateMutableUpdateConsumerProgram(
            SourceArithmeticProfile.Development);
        var functionNames = new[] {
            "PrefixIncrementThenRead",
            "ConstantInitializedPrefixIncrementThenRead",
            "ReadThenPrefixIncrement",
            "PostfixIncrementThenRead",
            "ReadThenPostfixIncrement",
            "ReversedFormalPrefixIncrementThenRead",
            "ReversedFormalReadThenPostfixIncrement",
            "PrefixDecrementThenRead",
            "PostfixDecrementThenRead",
        };
        var image = NativeEmitter.EmitLibrary(program, functionNames,
            NativeRuntimePolicy.FailFast);
        Assert.That(image[..2], Is.EqualTo(new byte[] {
            (byte)'M', (byte)'Z',
        }));
        await AssertMutableUpdateCallsAsync(image).ConfigureAwait(true);
    }

    private static (
        BoundProgram Program, byte[] Artifact, byte[] ReemittedArtifact)
        CreateMutableUpdateConsumerProgram(
            SourceArithmeticProfile arithmeticProfile)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-native-interface-parameter-surface-1";
        var registration = CreateRegistration(
            includeForeignNativeInterface: true);
        var options = new CompilationOptions {
            NativeCoreRegistration = registration,
            LayoutCoreRegistration = CreateLayoutRegistration(registration),
            SourceArithmeticProfile = arithmeticProfile,
        };
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Terrum.Interop;
                [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
                [ForeignNativeInterface(VTableByteOffset: uint32(0))]
                public interface IUpdateProbe {
                    public unsafe int32 Read(int32 first, int32 second);
                }
                """), options);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program
            ?? throw new AssertionException("Expected an FNI provider.");
        var providerInterface = providerProgram.Interfaces.Single();
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "update-provider.module", schemaIdentity, 1, [
                new TrRmSourceTypeExport("", "IUpdateProbe",
                    "update-provider/IUpdateProbe") {
                    GenericArity = 0,
                    InterfaceMethodExports = [.. providerInterface.Members
                        .Select(member =>
                            new TrRmSourceInterfaceMethodExport(
                                member.SourceInputOrdinal,
                                member.SourceDeclarationSpan,
                                $"update-provider/IUpdateProbe.{member.Name}"))],
                },
            ]);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "update-provider.module", []);
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                int32 PrefixIncrementThenRead(ptr<void> receiver,
                    int32 value) {
                    mut int32 current = value;
                    unsafe {
                        return IUpdateProbe.Read(receiver, ++current,
                            current);
                    }
                }
                int32 ConstantInitializedPrefixIncrementThenRead(
                    ptr<void> receiver) {
                    mut int32 current = 10;
                    unsafe {
                        return IUpdateProbe.Read(receiver, ++current,
                            current);
                    }
                }
                int32 ReadThenPrefixIncrement(ptr<void> receiver,
                    int32 value) {
                    mut int32 current = value;
                    unsafe {
                        return IUpdateProbe.Read(receiver, current,
                            ++current);
                    }
                }
                int32 PostfixIncrementThenRead(ptr<void> receiver,
                    int32 value) {
                    mut int32 current = value;
                    unsafe {
                        return IUpdateProbe.Read(receiver, current++,
                            current);
                    }
                }
                int32 ReadThenPostfixIncrement(ptr<void> receiver,
                    int32 value) {
                    mut int32 current = value;
                    unsafe {
                        return IUpdateProbe.Read(receiver, current,
                            current++);
                    }
                }
                int32 ReversedFormalPrefixIncrementThenRead(
                    ptr<void> receiver, int32 value) {
                    mut int32 current = value;
                    unsafe {
                        return IUpdateProbe.Read(receiver,
                            second: ++current, first: current);
                    }
                }
                int32 ReversedFormalReadThenPostfixIncrement(
                    ptr<void> receiver, int32 value) {
                    mut int32 current = value;
                    unsafe {
                        return IUpdateProbe.Read(receiver,
                            second: current, first: current++);
                    }
                }
                int32 PrefixDecrementThenRead(ptr<void> receiver,
                    int32 value) {
                    mut int32 current = value;
                    unsafe {
                        return IUpdateProbe.Read(receiver, --current,
                            current);
                    }
                }
                int32 PostfixDecrementThenRead(ptr<void> receiver,
                    int32 value) {
                    mut int32 current = value;
                    unsafe {
                        return IUpdateProbe.Read(receiver, current--,
                            current);
                    }
                }
                """), reference, options);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new AssertionException("Expected an FNI consumer.");
        var artifact = TrRmExecutableCompiler.EmitStandard(consumerProgram,
            "update-consumer.module", schemaIdentity, 1, []);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            "update-consumer.module", [providerInput]);
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            "update-consumer.module", schemaIdentity, 1, []);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemittedArtifact),
            "update-consumer.module", [providerInput]);
        return (reloaded, artifact, reemittedArtifact);
    }

    private static async Task AssertMutableUpdateCallsAsync(byte[] image)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"native-fni-mutable-update-{Guid.NewGuid():N}.dll");
        var module = IntPtr.Zero;
        var receiver = IntPtr.Zero;
        var vtable = IntPtr.Zero;
        NativeFniReadPairCallback? callback = null;
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            module = NativeLibrary.Load(path);
            receiver = Marshal.AllocHGlobal(IntPtr.Size);
            vtable = Marshal.AllocHGlobal(IntPtr.Size);
            callback = (actualReceiver, first, second) =>
                actualReceiver == receiver ? first * 100 + second : -1;
            Marshal.WriteIntPtr(vtable,
                Marshal.GetFunctionPointerForDelegate(callback));
            Marshal.WriteIntPtr(receiver, vtable);

            var expectedResults = new[] {
                (Name: "PrefixIncrementThenRead", Value: 1111),
                (Name: "ConstantInitializedPrefixIncrementThenRead",
                    Value: 1111),
                (Name: "ReadThenPrefixIncrement", Value: 1011),
                (Name: "PostfixIncrementThenRead", Value: 1011),
                (Name: "ReadThenPostfixIncrement", Value: 1010),
                (Name: "ReversedFormalPrefixIncrementThenRead",
                    Value: 1111),
                (Name: "ReversedFormalReadThenPostfixIncrement",
                    Value: 1010),
                (Name: "PrefixDecrementThenRead", Value: 909),
                (Name: "PostfixDecrementThenRead", Value: 1009),
            };
            foreach (var (name, value) in expectedResults)
            {
                var address = NativeLibrary.GetExport(module, name);
                if (name == "ConstantInitializedPrefixIncrementThenRead")
                {
                    var function = Marshal.GetDelegateForFunctionPointer<
                        NativeFniUpdateReceiverFunction>(address);
                    Assert.That(function(receiver), Is.EqualTo(value), name);
                }
                else
                {
                    var function = Marshal.GetDelegateForFunctionPointer<
                        NativeFniUpdateFunction>(address);
                    Assert.That(function(receiver, 10), Is.EqualTo(value),
                        name);
                }
            }
        }
        finally
        {
            if (module != IntPtr.Zero)
            {
                NativeLibrary.Free(module);
            }
            if (vtable != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(vtable);
            }
            if (receiver != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(receiver);
            }
            GC.KeepAlive(callback);
            await TestFileCleanup.DeleteWithRetryAsync(path)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public void StandardForeignNativeInterfaceShiftsPreserveTypedCountHomes()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-native-interface-parameter-surface-1";
        var registration = CreateRegistration(
            includeForeignNativeInterface: true);
        var options = new CompilationOptions {
            NativeCoreRegistration = registration,
            LayoutCoreRegistration = CreateLayoutRegistration(registration),
        };
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Terrum.Interop;
                [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
                [ForeignNativeInterface(VTableByteOffset: uint32(0))]
                public interface IProbe {
                    public unsafe int32 Read(int32 value);
                }
                """), options);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program
            ?? throw new AssertionException("Expected a bound provider.");
        var providerInterface = providerProgram.Interfaces.Single();
        var providerRead = providerInterface.Members.Single();
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "provider.module", schemaIdentity, 1, [
                new TrRmSourceTypeExport("", "IProbe", "provider/IProbe") {
                    GenericArity = 0,
                    InterfaceMethodExports = [
                        new TrRmSourceInterfaceMethodExport(
                            providerRead.SourceInputOrdinal,
                            providerRead.SourceDeclarationSpan,
                            "provider/IProbe.Read"),
                    ],
                },
            ]);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "provider.module", []);
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                int32 InvokeInt8(ptr<void> receiver, int32 value, int8 count) {
                    unsafe { return IProbe.Read(receiver, value << count); }
                }
                int32 InvokeUInt64(ptr<void> receiver, int32 value, uint64 count) {
                    unsafe { return IProbe.Read(receiver, value << count); }
                }
                int32 InvokeInt128(ptr<void> receiver, int32 value, int128 count) {
                    unsafe { return IProbe.Read(receiver, value << count); }
                }
                int32 InvokeUInt128(ptr<void> receiver, int32 value, uint128 count) {
                    unsafe { return IProbe.Read(receiver, value << count); }
                }
                """), reference, options);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new AssertionException("Expected a bound consumer.");
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "consumer.module", schemaIdentity, 1, []);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "consumer.module", [providerInput]);
        AssertShiftCountTypes(loaded);
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            "consumer.module", schemaIdentity, 1, []);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reemittedArtifact), "consumer.module", [providerInput]);
        AssertShiftCountTypes(reloaded);

        static void AssertShiftCountTypes(BoundProgram program)
        {
            var expectedCountTypes = new[] {
                ("InvokeInt8", PrimitiveType.Int8),
                ("InvokeUInt64", PrimitiveType.UInt64),
                ("InvokeInt128", PrimitiveType.Int128),
                ("InvokeUInt128", PrimitiveType.UInt128),
            };
            foreach (var (functionName, expectedCountType)
                in expectedCountTypes)
            {
                var function = program.Functions.Single(candidate =>
                    candidate.Name == functionName);
                BoundCall? foreignCall = null;
                _ = BoundTreeTraversal.VisitExpressions(function.Body,
                    expression =>
                    {
                        if (expression is BoundCall call
                            && call.ForeignInterfaceCall?.Member.Name
                                == "Read")
                        {
                            foreignCall = call;
                        }
                    });
                var selectedCall = foreignCall
                    ?? throw new AssertionException(
                        $"Expected {functionName} to contain IProbe.Read.");
                Assert.That(selectedCall.Arguments.Single(),
                    Is.TypeOf<BoundBinary>());
                var shift = (BoundBinary)selectedCall.Arguments.Single();
                Assert.That(shift.Operator, Is.EqualTo(TokenKind.ShiftLeft));
                Assert.That(shift.OverflowBehavior,
                    Is.EqualTo(BoundIntegerOverflowBehavior.Checked));
                Assert.That(shift.Right, Is.TypeOf<BoundRead>());
                Assert.That(shift.Right.Type, Is.EqualTo(expectedCountType));
                Assert.That(((BoundRead)shift.Right).Variable, Is.EqualTo(2));
            }
        }
    }

    [Test]
    public void StandardForeignNativeInterfaceConditionalOutputsAcceptScalarExpressions()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-native-interface-parameter-surface-1";
        var (native, output) = NativeOutputCoreRegistrationTests
            .CreateCombinedRegistrations(includeForeignNativeInterface: true);
        var options = new CompilationOptions {
            NativeCoreRegistration = native,
            LayoutCoreRegistration = CreateLayoutRegistration(native),
            NativeOutputCoreRegistration = output,
            SourceArithmeticProfile = SourceArithmeticProfile.Release,
        };
        const string providerSource = """
            [Terrum.Interop.NativeCallable(
                CallConv: Terrum.Interop.NativeCallingConvention.Cdecl)]
            [Terrum.Interop.ForeignNativeInterface(
                VTableByteOffset: uint32(0))]
            public interface IProbe {
                [param(value): Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonNegative)]
                public unsafe int32 TryReadPair(int32 first, int32 second,
                    write ptr<int32> value);
                [param(firstOutput): Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonNegative)]
                [param(secondOutput): Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonNegative)]
                public unsafe int32 TryReadTwo(ptr<void> backing,
                    int32 value,
                    write ptr<int32> firstOutput,
                    [Terrum.CodeAnalysis.DependsOn(nameof(backing))]
                    [Terrum.CodeAnalysis.ForwardsBacking(nameof(backing))]
                    write ptr<ptr<int32>> secondOutput);
                [param(firstOutput): Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonNegative)]
                [param(secondOutput): Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                public unsafe int32 TryReadTwoMismatched(int32 value,
                    write ptr<int32> firstOutput,
                    write ptr<ptr<int32>> secondOutput);
                public unsafe int32 GetIndex(int32 index);
                public unsafe int32 Pair(int32 first, int32 second);
                public unsafe void Touch();
                public unsafe void TouchPair(int32 first, int32 second);
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
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "provider.module", []);
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                int32 InvokeComputed(ptr<void> receiver, int32 first,
                    int32 second, int32 fallback) {
                    init int32 value;
                    unsafe {
                        int32 status = IProbe.TryReadPair(receiver,
                            first + second, second | 2, &init value);
                        if (status >= 0) { return value; }
                        return fallback;
                    }
                }

                int32 InvokeMixed(ptr<void> receiver, int32 first,
                    int32 second, int32 fallback) {
                    init int32 value;
                    unsafe {
                        int32 status = IProbe.TryReadPair(receiver,
                            IProbe.GetIndex(receiver, first),
                            second | first, &init value);
                        if (status >= 0) { return value; }
                        return fallback;
                    }
                }

                int32 InvokeConditionalSequence(ptr<void> receiver,
                    int32 first, int32 second, int32 fallback) {
                    unsafe {
                        IProbe.Touch(receiver);
                        IProbe.TouchPair(receiver,
                            second: second, first: first);
                        init int32 value;
                        int32 status = IProbe.TryReadPair(receiver,
                            first, second, &init value);
                        if (status >= 0) { return value; }
                        return fallback;
                    }
                }

                int32 InvokeTwoOutputs(ptr<void> receiver,
                    ptr<void> backing, int32 value, int32 fallback) {
                    unsafe {
                        init int32 firstOutput;
                        init ptr<int32> secondOutput;
                        int32 status = IProbe.TryReadTwo(receiver, backing,
                            value,
                            secondOutput: &init secondOutput,
                            firstOutput: &init firstOutput);
                        if (status >= 0) { return firstOutput; }
                        return fallback;
                    }
                }

                int32 InvokeMixedDirect(ptr<void> receiver, int32 first,
                    int32 second) {
                    unsafe {
                        return IProbe.Pair(receiver,
                            IProbe.GetIndex(receiver, first), second * 2);
                    }
                }

                int32 InvokeMixedSequence(ptr<void> receiver, int32 first,
                    int32 second) {
                    unsafe {
                        IProbe.Touch(receiver);
                        return IProbe.Pair(receiver,
                            IProbe.GetIndex(receiver, first), second * 2);
                    }
                }

                """), reference, options);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new AssertionException("Expected a bound consumer.");
        AssertTwoOutputs(consumerProgram, sourceArgumentOrder: true);
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "consumer.module", schemaIdentity, 1, []);
        var schema
            = TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft;
        var consumerTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact, schema));
        var invokeRow = Enumerable.Range(1, consumerTables.Methods.Count)
            .Single(row =>
                consumerTables.Methods.GetName(row) == "InvokeComputed");
        var body = consumerTables.Bodies.GetRegisterBody(
            consumerTables.Methods.GetBodyRow(invokeRow),
            consumerTables.Contracts.Count);
        var entryCode = body.GetBlockCode(0).ToArray();
        Assert.That(entryCode, Does.Contain((byte)0x11));
        Assert.That(entryCode, Does.Contain((byte)0x21));
        Assert.That(entryCode, Does.Contain((byte)0x31));
        var callSite = body.GetRecord(1);
        Assert.That((callSite[24], callSite[25],
                BinaryPrimitives.ReadUInt16LittleEndian(callSite[26..])),
            Is.EqualTo(((byte)1, (byte)1, (ushort)2)));
        Assert.That((callSite[28], callSite[29],
                BinaryPrimitives.ReadUInt16LittleEndian(callSite[30..])),
            Is.EqualTo(((byte)1, (byte)1, (ushort)4)));
        var mixedInvokeRow = Enumerable.Range(1, consumerTables.Methods.Count)
            .Single(row =>
                consumerTables.Methods.GetName(row) == "InvokeMixed");
        var mixedBody = consumerTables.Bodies.GetRegisterBody(
            consumerTables.Methods.GetBodyRow(mixedInvokeRow),
            consumerTables.Contracts.Count);
        Assert.That(mixedBody.GetBlockCode(0).ToArray().Take(8),
            Is.EqualTo(new byte[] {
                0x90, 1, 0x11, 0x22, 0x11, 0x13,
                0x31, 0x32,
            }));
        var mixedCallSite = mixedBody.GetRecord(2);
        Assert.That((mixedCallSite[24], mixedCallSite[25],
                BinaryPrimitives.ReadUInt16LittleEndian(
                    mixedCallSite[26..])),
            Is.EqualTo(((byte)1, (byte)1, (ushort)1)));
        Assert.That((mixedCallSite[28], mixedCallSite[29],
                BinaryPrimitives.ReadUInt16LittleEndian(
                    mixedCallSite[30..])),
            Is.EqualTo(((byte)1, (byte)1, (ushort)2)));
        var mixedDirectRow = Enumerable.Range(1, consumerTables.Methods.Count)
            .Single(row =>
                consumerTables.Methods.GetName(row) == "InvokeMixedDirect");
        var mixedDirectBody = consumerTables.Bodies.GetRegisterBody(
            consumerTables.Methods.GetBodyRow(mixedDirectRow),
            consumerTables.Contracts.Count);
        AssertMixedReturnCallSite(mixedDirectBody.GetRecord(2));
        var mixedSequenceRow = Enumerable.Range(1, consumerTables.Methods.Count)
            .Single(row =>
                consumerTables.Methods.GetName(row)
                    == "InvokeMixedSequence");
        var mixedSequenceBody = consumerTables.Bodies.GetRegisterBody(
            consumerTables.Methods.GetBodyRow(mixedSequenceRow),
            consumerTables.Contracts.Count);
        AssertMixedReturnCallSite(mixedSequenceBody.GetRecord(3));
        AssertConditionalSequence(consumerProgram,
            sourceArgumentOrder: true);
        var conditionalSequenceRow = Enumerable.Range(
                1, consumerTables.Methods.Count)
            .Single(row =>
                consumerTables.Methods.GetName(row)
                    == "InvokeConditionalSequence");
        var conditionalSequenceBody
            = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(conditionalSequenceRow),
                consumerTables.Contracts.Count);
        var conditionalSequenceCode
            = conditionalSequenceBody.GetBlockCode(0).ToArray();
        Assert.That(conditionalSequenceBody.RecordCount, Is.EqualTo(6));
        Assert.That(conditionalSequenceBody.MethodCount, Is.EqualTo(3));
        Assert.That(conditionalSequenceCode.Take(4),
            Is.EqualTo(new byte[] { 0x90, 1, 0x90, 2 }));
        Assert.That(conditionalSequenceCode[4], Is.EqualTo((byte)0xDF));
        Assert.That((conditionalSequenceCode[8],
                conditionalSequenceCode[9]),
            Is.EqualTo(((byte)0x90, (byte)3)));
        Assert.That(conditionalSequenceBody.GetRecord(1)[5],
            Is.EqualTo((byte)0));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                conditionalSequenceBody.GetRecord(1)[12..]),
            Is.EqualTo(1u));
        Assert.That((conditionalSequenceBody.GetRecord(1)[20],
                conditionalSequenceBody.GetRecord(1)[21],
                BinaryPrimitives.ReadUInt16LittleEndian(
                    conditionalSequenceBody.GetRecord(1)[22..])),
            Is.EqualTo(((byte)2, (byte)1, (ushort)0)));
        Assert.That(conditionalSequenceBody.GetRecord(2)[5],
            Is.EqualTo((byte)0));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                conditionalSequenceBody.GetRecord(2)[12..]),
            Is.EqualTo(3u));
        Assert.That((conditionalSequenceBody.GetRecord(2)[20],
                conditionalSequenceBody.GetRecord(2)[21],
                BinaryPrimitives.ReadUInt16LittleEndian(
                    conditionalSequenceBody.GetRecord(2)[22..])),
            Is.EqualTo(((byte)2, (byte)1, (ushort)0)));
        Assert.That((conditionalSequenceBody.GetRecord(2)[24],
                conditionalSequenceBody.GetRecord(2)[25],
                BinaryPrimitives.ReadUInt16LittleEndian(
                    conditionalSequenceBody.GetRecord(2)[26..])),
            Is.EqualTo(((byte)2, (byte)1, (ushort)1)));
        Assert.That((conditionalSequenceBody.GetRecord(2)[28],
                conditionalSequenceBody.GetRecord(2)[29],
                BinaryPrimitives.ReadUInt16LittleEndian(
                    conditionalSequenceBody.GetRecord(2)[30..])),
            Is.EqualTo(((byte)2, (byte)1, (ushort)2)));
        Assert.That(conditionalSequenceBody.GetRecord(3)[5],
            Is.EqualTo((byte)1));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                conditionalSequenceBody.GetRecord(4)[4..]),
            Is.EqualTo((ushort)3));
        var twoOutputsRow = Enumerable.Range(1, consumerTables.Methods.Count)
            .Single(row =>
                consumerTables.Methods.GetName(row) == "InvokeTwoOutputs");
        var twoOutputsBody = consumerTables.Bodies.GetRegisterBody(
            consumerTables.Methods.GetBodyRow(twoOutputsRow),
            consumerTables.Contracts.Count);
        var twoOutputsCode = twoOutputsBody.GetBlockCode(0).ToArray();
        Assert.That(twoOutputsBody.RecordCount, Is.EqualTo(5));
        Assert.That(twoOutputsCode.Take(10), Is.EqualTo(new byte[] {
            0xDF, 0, 2, 0, 0xDF, 0, 3, 0, 0x90, 1,
        }));
        var twoOutputsCallSite = twoOutputsBody.GetRecord(1);
        Assert.That((twoOutputsCallSite[32], twoOutputsCallSite[33],
                BinaryPrimitives.ReadUInt16LittleEndian(
                    twoOutputsCallSite[34..])),
            Is.EqualTo(((byte)2, (byte)6, (ushort)4)));
        Assert.That((twoOutputsCallSite[36], twoOutputsCallSite[37],
                BinaryPrimitives.ReadUInt16LittleEndian(
                    twoOutputsCallSite[38..])),
            Is.EqualTo(((byte)2, (byte)6, (ushort)5)));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                twoOutputsBody.GetRecord(2)[6..]),
            Is.EqualTo((ushort)4));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                twoOutputsBody.GetRecord(2)[8..]),
            Is.EqualTo(3u));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                twoOutputsBody.GetRecord(3)[6..]),
            Is.EqualTo((ushort)5));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                twoOutputsBody.GetRecord(3)[8..]),
            Is.EqualTo(4u));

        var mismatchedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                int32 InvokeMismatchedOutputs(ptr<void> receiver,
                    int32 value, int32 fallback) {
                    unsafe {
                        init int32 firstOutput;
                        init ptr<int32> secondOutput;
                        int32 status = IProbe.TryReadTwoMismatched(
                            receiver, value, &init firstOutput,
                            &init secondOutput);
                        if (status >= 0) { return firstOutput; }
                        return fallback;
                    }
                }
                """), reference, options);
        Assert.That(mismatchedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                mismatchedCompilation.Diagnostics));
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(
                mismatchedCompilation.Program!, "mismatched-consumer.module",
                schemaIdentity, 1, []));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "consumer.module", [providerInput]);
        AssertTwoOutputs(loaded, sourceArgumentOrder: false);
        AssertComputedInputs(loaded);
        AssertMixedInputs(loaded);
        AssertConditionalSequence(loaded, sourceArgumentOrder: false);
        AssertMixedReturnInputs(loaded, "InvokeMixedDirect");
        AssertMixedReturnInputs(loaded, "InvokeMixedSequence");
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            "consumer.module", schemaIdentity, 1, []);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reemittedArtifact), "consumer.module", [providerInput]);
        AssertTwoOutputs(reloaded, sourceArgumentOrder: false);
        AssertComputedInputs(reloaded);
        AssertMixedInputs(reloaded);
        AssertConditionalSequence(reloaded, sourceArgumentOrder: false);
        AssertMixedReturnInputs(reloaded, "InvokeMixedDirect");
        AssertMixedReturnInputs(reloaded, "InvokeMixedSequence");

        var outputBodyRow = consumerTables.Methods.GetBodyRow(twoOutputsRow);
        AssertRejectsMalformedOutputReservation(
            RewriteOutputReservation(
                consumerArtifact, outputBodyRow, 3,
                static record => BinaryPrimitives.WriteUInt32LittleEndian(
                    record.AsSpan(8), 0)),
            "missing-output-formal");
        AssertRejectsMalformedOutputReservation(
            RewriteOutputReservation(
                consumerArtifact, outputBodyRow, 3,
                static record => BinaryPrimitives.WriteUInt32LittleEndian(
                    record.AsSpan(8), 2)),
            "duplicate-output-formal");
        AssertRejectsMalformedOutputReservation(
            RewriteOutputReservation(
                consumerArtifact, outputBodyRow, 3,
                static record => BinaryPrimitives.WriteUInt16LittleEndian(
                    record.AsSpan(6), 3)),
            "mismatched-output-home");

        var checkedOptions = new CompilationOptions {
            NativeCoreRegistration = native,
            LayoutCoreRegistration = CreateLayoutRegistration(native),
            NativeOutputCoreRegistration = output,
            SourceArithmeticProfile = SourceArithmeticProfile.Development,
        };
        var checkedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                int32 InvokeChecked(ptr<void> receiver, int32 first,
                    int32 second, int32 fallback) {
                    init int32 value;
                    unsafe {
                        int32 status = IProbe.TryReadPair(receiver,
                            first + second, second * 2, &init value);
                        if (status >= 0) { return value; }
                        return fallback;
                    }
                }
                int32 InvokeNestedThenChecked(ptr<void> receiver,
                    int32 first, int32 divisor, int32 nestedValue,
                    int32 fallback) {
                    init int32 value;
                    unsafe {
                        int32 status = IProbe.TryReadPair(receiver,
                            second: IProbe.GetIndex(receiver, nestedValue),
                            first: first / divisor,
                            value: &init value);
                        if (status >= 0) { return value; }
                        return fallback;
                    }
                }
                int32 InvokeCheckedThenNested(ptr<void> receiver,
                    int32 first, int32 divisor, int32 nestedValue,
                    int32 fallback) {
                    init int32 value;
                    unsafe {
                        int32 status = IProbe.TryReadPair(receiver,
                            first: first / divisor,
                            second: IProbe.GetIndex(receiver, nestedValue),
                            value: &init value);
                        if (status >= 0) { return value; }
                        return fallback;
                    }
                }
                """), reference, checkedOptions);
        Assert.That(checkedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                checkedCompilation.Diagnostics));
        var checkedProgram = checkedCompilation.Program
            ?? throw new AssertionException("Expected a checked consumer.");
        var checkedArtifact = TrRmExecutableCompiler.EmitStandard(
            checkedProgram, "checked-consumer.module", schemaIdentity, 1, []);
        var checkedTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                checkedArtifact, schema));
        var checkedInvokeRow = Enumerable.Range(1, checkedTables.Methods.Count)
            .Single(row =>
                checkedTables.Methods.GetName(row) == "InvokeChecked");
        var checkedBody = checkedTables.Bodies.GetRegisterBody(
            checkedTables.Methods.GetBodyRow(checkedInvokeRow),
            checkedTables.Contracts.Count);
        Assert.That(checkedBody.GetBlockCode(0).ToArray(),
            Does.Contain((byte)0xC0));
        AssertCheckedArgumentInstructionOrder(
            GetCheckedBodyCode("InvokeNestedThenChecked"),
            nestedFirst: true);
        AssertCheckedArgumentInstructionOrder(
            GetCheckedBodyCode("InvokeCheckedThenNested"),
            nestedFirst: false);
        var loadedChecked = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, checkedArtifact),
            "checked-consumer.module", [providerInput]);
        AssertCheckedInputs(loadedChecked);
        AssertMixedCheckedInputOrder(loadedChecked,
            "InvokeNestedThenChecked", 1, 0);
        AssertMixedCheckedInputOrder(loadedChecked,
            "InvokeCheckedThenNested", 0, 1);
        var reemittedChecked = TrRmExecutableCompiler.EmitStandard(
            loadedChecked, "checked-consumer.module", schemaIdentity, 1, []);
        var reloadedChecked = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemittedChecked),
            "checked-consumer.module", [providerInput]);
        AssertCheckedInputs(reloadedChecked);
        AssertMixedCheckedInputOrder(reloadedChecked,
            "InvokeNestedThenChecked", 1, 0);
        AssertMixedCheckedInputOrder(reloadedChecked,
            "InvokeCheckedThenNested", 0, 1);

        static void AssertComputedInputs(BoundProgram program)
        {
            var function = program.Functions.Single(
                static candidate => candidate.Name == "InvokeComputed");
            BoundCall? foreignCall = null;
            Assert.That(BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall call
                        && call.ForeignInterfaceCall?.Member.Name
                            == "TryReadPair")
                    {
                        foreignCall = call;
                    }
                }), Is.True);
            var selectedCall = foreignCall
                ?? throw new AssertionException("Expected an FNI call.");
            Assert.That(selectedCall.Arguments.Length, Is.EqualTo(3));
            Assert.That(selectedCall.Arguments[0], Is.TypeOf<BoundBinary>());
            Assert.That(selectedCall.Arguments[1], Is.TypeOf<BoundBinary>());
            var first = (BoundBinary)selectedCall.Arguments[0];
            var second = (BoundBinary)selectedCall.Arguments[1];
            Assert.That(first.Operator, Is.EqualTo(TokenKind.Plus));
            Assert.That(first.OverflowBehavior,
                Is.EqualTo(BoundIntegerOverflowBehavior.Wrapping));
            Assert.That(first.Left, Is.TypeOf<BoundRead>());
            Assert.That(((BoundRead)first.Left).Variable, Is.EqualTo(1));
            Assert.That(first.Right, Is.TypeOf<BoundRead>());
            Assert.That(((BoundRead)first.Right).Variable, Is.EqualTo(2));
            Assert.That(second.Operator, Is.EqualTo(TokenKind.Pipe));
            Assert.That(second.OverflowBehavior,
                Is.EqualTo(BoundIntegerOverflowBehavior.Checked));
            Assert.That(second.Left, Is.TypeOf<BoundRead>());
            Assert.That(((BoundRead)second.Left).Variable, Is.EqualTo(2));
            Assert.That(second.Right, Is.TypeOf<BoundConstant>());
            Assert.That(selectedCall.Arguments[2],
                Is.TypeOf<BoundNativeOutputReservation>());
        }

        static void AssertTwoOutputs(BoundProgram program,
            bool sourceArgumentOrder)
        {
            var function = program.Functions.Single(
                static candidate => candidate.Name == "InvokeTwoOutputs");
            BoundCall? foreignCall = null;
            Assert.That(BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall call
                        && call.ForeignInterfaceCall?.Member.Name
                            == "TryReadTwo")
                    {
                        foreignCall = call;
                    }
                }), Is.True);
            var selectedCall = foreignCall
                ?? throw new AssertionException("Expected an FNI call.");
            Assert.That(selectedCall.Arguments.Length, Is.EqualTo(4));
            Assert.That(selectedCall.ParameterIndex(0), Is.EqualTo(0));
            Assert.That(selectedCall.Arguments[0], Is.TypeOf<BoundRead>());
            Assert.That(selectedCall.ParameterIndex(1), Is.EqualTo(1));
            Assert.That(selectedCall.Arguments[1], Is.TypeOf<BoundRead>());
            Assert.That(selectedCall.ParameterIndex(2),
                Is.EqualTo(sourceArgumentOrder ? 3 : 2));
            Assert.That(selectedCall.ParameterIndex(3),
                Is.EqualTo(sourceArgumentOrder ? 2 : 3));
            var firstReservation = (BoundNativeOutputReservation)
                selectedCall.Arguments[sourceArgumentOrder ? 3 : 2];
            var secondReservation = (BoundNativeOutputReservation)
                selectedCall.Arguments[sourceArgumentOrder ? 2 : 3];
            Assert.That(firstReservation.Variable, Is.EqualTo(4));
            Assert.That(firstReservation.PointeeType,
                Is.EqualTo(PrimitiveType.Int32));
            Assert.That(secondReservation.Variable, Is.EqualTo(5));
            Assert.That(NativePointerTypes.IsType(
                    secondReservation.PointeeType),
                Is.True);
            Assert.That(secondReservation.PointeeType,
                Is.Not.EqualTo(firstReservation.PointeeType));
            Assert.That(function.Variables[4].IsInitializationLocal,
                Is.True);
            Assert.That(function.Variables[5].IsInitializationLocal,
                Is.True);
            var outputReads = new HashSet<int>();
            Assert.That(BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundRead read
                        && read.Variable is 4 or 5)
                    {
                        _ = outputReads.Add(read.Variable);
                    }
                }), Is.True);
            Assert.That(outputReads, Is.EquivalentTo(new[] { 4 }));
            var outputContracts = selectedCall.ForeignInterfaceCall!
                .NativeOutputContracts.OrderBy(
                    static contract => contract.FormalOrdinal).ToArray();
            Assert.That(outputContracts.Length, Is.EqualTo(2));
            Assert.That(outputContracts[1].Dependencies.Length,
                Is.EqualTo(1));
            Assert.That(outputContracts[1].Dependencies[0],
                Is.EqualTo(new NativeOutputDependency(2,
                    NativeOutputDependencySourceKind.Formal,
                    NativeOutputDependencyRelationship.Forward,
                    NativeOutputPublicationPhase.NormalCompletion, 3, 0)));
        }

        void AssertRejectsMalformedOutputReservation(byte[] malformed,
            string identity)
        {
            Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler.LoadStandard(
                    new TrRmStandardArtifactInput(schemaIdentity, malformed),
                    identity, [providerInput]));
        }

        byte[] RewriteOutputReservation(byte[] artifact, int bodyRow,
            int recordIndex, Action<byte[]> updateRecord)
        {
            var index = new TrRmArtifactIndex(artifact, schema);
            Assert.That(index.TryGetSection(0x20, out var bodyBytes,
                out var bodyCount), Is.True);
            var bodyTable = new TrRmBodyTableReader(bodyBytes, bodyCount);
            var rows = new ReadOnlyMemory<byte>[bodyCount];
            for (var row = 1; row <= bodyCount; row++)
            {
                rows[row - 1] = bodyTable.GetRow(row).ToArray();
            }

            var bodyBytesForRow = rows[bodyRow - 1].ToArray();
            var bodyReader = new TrRmRegisterBodyReader(bodyBytesForRow,
                schema: schema);
            var record = bodyReader.GetRecord(recordIndex).ToArray();
            var recordOffset = bodyBytesForRow.AsSpan().IndexOf(record);
            Assert.That(recordOffset, Is.GreaterThanOrEqualTo(0));
            updateRecord(record);
            record.AsSpan().CopyTo(bodyBytesForRow.AsSpan(recordOffset));
            rows[bodyRow - 1] = bodyBytesForRow;

            var rewrittenBodies = TrRmVariableTableWriter.EncodeBodies(rows);
            var sections = new List<TrRmSection>(index.SectionCount);
            for (var sectionIndex = 0;
                sectionIndex < index.SectionCount;
                sectionIndex++)
            {
                index.GetSection(sectionIndex, out var kind, out var flags,
                    out var bytes, out var count);
                sections.Add(new TrRmSection(kind, flags, (uint)count,
                    kind == 0x20 ? rewrittenBodies : bytes.ToArray()));
            }

            return TrRmArtifactWriter.EncodeDevelopment(sections);
        }

        static void AssertConditionalSequence(BoundProgram program,
            bool sourceArgumentOrder)
        {
            var function = program.Functions.Single(
                static candidate => candidate.Name
                    == "InvokeConditionalSequence");
            var calls = new List<BoundCall>();
            Assert.That(BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall {
                        ForeignInterfaceCall: not null,
                    } call)
                    {
                        calls.Add(call);
                    }
                }), Is.True);
            Assert.That(calls.Select(static call =>
                    call.ForeignInterfaceCall!.Member.Name),
                Is.EqualTo(new[] {
                    "Touch", "TouchPair", "TryReadPair",
                }));
            Assert.That(calls[1].Arguments.Length, Is.EqualTo(2));
            Assert.That(calls[1].ParameterIndex(0),
                Is.EqualTo(sourceArgumentOrder ? 1 : 0));
            Assert.That(calls[1].Arguments[0], Is.TypeOf<BoundRead>());
            Assert.That(((BoundRead)calls[1].Arguments[0]).Variable,
                Is.EqualTo(sourceArgumentOrder ? 2 : 1));
            Assert.That(calls[1].ParameterIndex(1),
                Is.EqualTo(sourceArgumentOrder ? 0 : 1));
            Assert.That(calls[1].Arguments[1], Is.TypeOf<BoundRead>());
            Assert.That(((BoundRead)calls[1].Arguments[1]).Variable,
                Is.EqualTo(sourceArgumentOrder ? 1 : 2));
        }

        static void AssertCheckedInputs(BoundProgram program)
        {
            var function = program.Functions.Single(
                static candidate => candidate.Name == "InvokeChecked");
            BoundCall? foreignCall = null;
            Assert.That(BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall call
                        && call.ForeignInterfaceCall?.Member.Name
                            == "TryReadPair")
                    {
                        foreignCall = call;
                    }
                }), Is.True);
            var selectedCall = foreignCall
                ?? throw new AssertionException("Expected an FNI call.");
            var first = (BoundBinary)selectedCall.Arguments[0];
            var second = (BoundBinary)selectedCall.Arguments[1];
            Assert.That(first.Operator, Is.EqualTo(TokenKind.Plus));
            Assert.That(first.OverflowBehavior,
                Is.EqualTo(BoundIntegerOverflowBehavior.Checked));
            Assert.That(second.Operator, Is.EqualTo(TokenKind.Star));
            Assert.That(second.OverflowBehavior,
                Is.EqualTo(BoundIntegerOverflowBehavior.Checked));
        }

        byte[] GetCheckedBodyCode(string name)
        {
            var row = Enumerable.Range(1, checkedTables.Methods.Count)
                .Single(candidate => checkedTables.Methods.GetName(candidate)
                    == name);
            var body = checkedTables.Bodies.GetRegisterBody(
                checkedTables.Methods.GetBodyRow(row),
                checkedTables.Contracts.Count);
            return body.GetBlockCode(0).ToArray();
        }

        static void AssertCheckedArgumentInstructionOrder(byte[] code,
            bool nestedFirst)
        {
            var nestedCallOffset = Array.IndexOf(code, (byte)0x90);
            var checkedArithmeticOffset = Array.IndexOf(code, (byte)0xC0);
            Assert.That(nestedCallOffset, Is.GreaterThanOrEqualTo(0));
            Assert.That(checkedArithmeticOffset,
                Is.GreaterThanOrEqualTo(0));
            Assert.That(code[checkedArithmeticOffset + 1],
                Is.EqualTo((byte)0x0C));
            Assert.That(nestedFirst
                    ? nestedCallOffset < checkedArithmeticOffset
                    : checkedArithmeticOffset < nestedCallOffset,
                Is.True);
        }

        static void AssertMixedCheckedInputOrder(BoundProgram program,
            string functionName, int firstFormal, int secondFormal)
        {
            var function = program.Functions.Single(
                candidate => candidate.Name == functionName);
            BoundCall? foreignCall = null;
            _ = BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall call
                        && call.ForeignInterfaceCall?.Member.Name
                            == "TryReadPair")
                    {
                        foreignCall = call;
                    }
                });
            var selectedCall = foreignCall
                ?? throw new AssertionException(
                    "Expected the FNI TryReadPair call.");
            Assert.That(selectedCall.ParameterIndex(0),
                Is.EqualTo(firstFormal));
            Assert.That(selectedCall.ParameterIndex(1),
                Is.EqualTo(secondFormal));
            Assert.That(selectedCall.ParameterIndex(2), Is.EqualTo(2));
            var nestedArgumentIndex = secondFormal == 1 ? 1 : 0;
            var checkedArgumentIndex = firstFormal == 0 ? 0 : 1;
            var checkedExpression
                = selectedCall.Arguments[checkedArgumentIndex];
            Assert.That(checkedExpression, Is.TypeOf<BoundBinary>());
            Assert.That(((BoundBinary)checkedExpression).Operator,
                Is.EqualTo(TokenKind.Slash));
            Assert.That(((BoundBinary)checkedExpression).OverflowBehavior,
                Is.EqualTo(BoundIntegerOverflowBehavior.Checked));
            var nestedFound = false;
            _ = BoundTreeTraversal.VisitExpressions(
                selectedCall.Arguments[nestedArgumentIndex], expression =>
                {
                    if (expression is BoundCall call
                        && call.ForeignInterfaceCall?.Member.Name
                            == "GetIndex")
                    {
                        nestedFound = true;
                    }
                });
            Assert.That(nestedFound, Is.True);
            Assert.That(selectedCall.Arguments[2],
                Is.TypeOf<BoundNativeOutputReservation>());
        }

        static void AssertMixedReturnCallSite(ReadOnlySpan<byte> callSite)
        {
            Assert.That((callSite[24], callSite[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(callSite[26..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)2)));
            Assert.That((callSite[28], callSite[29],
                    BinaryPrimitives.ReadUInt16LittleEndian(callSite[30..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)3)));
        }

        static void AssertMixedReturnInputs(
            BoundProgram program, string functionName)
        {
            var function = program.Functions.Single(
                candidate => candidate.Name == functionName);
            BoundCall? foreignCall = null;
            Assert.That(BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall call
                        && call.ForeignInterfaceCall?.Member.Name == "Pair")
                    {
                        foreignCall = call;
                    }
                }), Is.True);
            var selectedCall = foreignCall
                ?? throw new AssertionException("Expected an FNI call.");
            Assert.That(selectedCall.Arguments.Length, Is.EqualTo(2));
            Assert.That(selectedCall.ParameterIndex(0), Is.EqualTo(0));
            Assert.That(selectedCall.Arguments[0],
                Is.TypeOf<BoundUnsafeExpression>());
            var nestedCall = (BoundCall)((BoundUnsafeExpression)
                selectedCall.Arguments[0]).Expression;
            Assert.That(nestedCall.ForeignInterfaceCall?.Member.Name,
                Is.EqualTo("GetIndex"));
            Assert.That(((BoundRead)nestedCall.Arguments.Single()).Variable,
                Is.EqualTo(1));
            Assert.That(selectedCall.ParameterIndex(1), Is.EqualTo(1));
            Assert.That(selectedCall.Arguments[1], Is.TypeOf<BoundBinary>());
            var expression = (BoundBinary)selectedCall.Arguments[1];
            Assert.That(expression.Operator, Is.EqualTo(TokenKind.Star));
            Assert.That(expression.OverflowBehavior,
                Is.EqualTo(BoundIntegerOverflowBehavior.Wrapping));
            Assert.That(((BoundRead)expression.Left).Variable, Is.EqualTo(2));
            Assert.That(expression.Right, Is.TypeOf<BoundConstant>());
        }

        static void AssertMixedInputs(BoundProgram program)
        {
            var function = program.Functions.Single(
                static candidate => candidate.Name == "InvokeMixed");
            BoundCall? foreignCall = null;
            Assert.That(BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall call
                        && call.ForeignInterfaceCall?.Member.Name
                            == "TryReadPair")
                    {
                        foreignCall = call;
                    }
                }), Is.True);
            var selectedCall = foreignCall
                ?? throw new AssertionException("Expected an FNI call.");
            Assert.That(selectedCall.Arguments.Length, Is.EqualTo(3));
            Assert.That(selectedCall.ParameterIndex(0), Is.EqualTo(0));
            Assert.That(selectedCall.Arguments[0],
                Is.TypeOf<BoundUnsafeExpression>());
            var nestedCall = (BoundCall)((BoundUnsafeExpression)
                selectedCall.Arguments[0]).Expression;
            Assert.That(nestedCall.ForeignInterfaceCall?.Member.Name,
                Is.EqualTo("GetIndex"));
            Assert.That(nestedCall.Arguments.Single(),
                Is.TypeOf<BoundRead>());
            Assert.That(((BoundRead)nestedCall.Arguments.Single()).Variable,
                Is.EqualTo(1));
            Assert.That(selectedCall.ParameterIndex(1), Is.EqualTo(1));
            Assert.That(selectedCall.Arguments[1], Is.TypeOf<BoundBinary>());
            var expression = (BoundBinary)selectedCall.Arguments[1];
            Assert.That(expression.Operator, Is.EqualTo(TokenKind.Pipe));
            Assert.That(expression.OverflowBehavior,
                Is.EqualTo(BoundIntegerOverflowBehavior.Checked));
            Assert.That(expression.Left, Is.TypeOf<BoundRead>());
            Assert.That(((BoundRead)expression.Left).Variable,
                Is.EqualTo(2));
            Assert.That(expression.Right, Is.TypeOf<BoundRead>());
            Assert.That(((BoundRead)expression.Right).Variable,
                Is.EqualTo(1));
            Assert.That(selectedCall.ParameterIndex(2), Is.EqualTo(2));
            Assert.That(selectedCall.Arguments[2],
                Is.TypeOf<BoundNativeOutputReservation>());
        }
    }

    [Test]
    public void StandardForeignNativeInterfaceConditionalOutputsDecodeComparisons()
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
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Terrum.Interop.NativeCallable(
                    CallConv: Terrum.Interop.NativeCallingConvention.Cdecl)]
                [Terrum.Interop.ForeignNativeInterface(
                    VTableByteOffset: uint32(0))]
                public interface IProbe {
                    [param(output): Terrum.Interop.NativeOutput(
                        When: Terrum.Interop.NativeOutputCondition.NonNegative)]
                    public unsafe int32 TryRead(bool flag,
                        write ptr<int32> output);
                    [param(output): Terrum.Interop.NativeOutput(
                        When: Terrum.Interop.NativeOutputCondition.NonNegative)]
                    public unsafe int32 TryReadConverted(float64 value,
                        write ptr<int32> output);
                }
                """), options);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program
            ?? throw new AssertionException("Expected a bound provider.");
        var providerInterface = providerProgram.Interfaces.Single();
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "provider.module", schemaIdentity, 1, [
                new TrRmSourceTypeExport("", "IProbe", "provider/IProbe") {
                    GenericArity = 0,
                    InterfaceMethodExports = [.. providerInterface.Members
                        .Select(member =>
                            new TrRmSourceInterfaceMethodExport(
                                member.SourceInputOrdinal,
                                member.SourceDeclarationSpan,
                                $"provider/IProbe.{member.Name}"))],
                },
            ]);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "provider.module", []);
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                int32 InvokeBoolean(ptr<void> receiver, bool first,
                    bool second, int32 fallback) {
                    init int32 value;
                    unsafe {
                        int32 status = IProbe.TryRead(receiver,
                            first == second, &init value);
                        if (status >= 0) { return value; }
                        return fallback;
                    }
                }
                int32 InvokeOrdered(ptr<void> receiver, int32 first,
                    int32 second, int32 fallback) {
                    init int32 value;
                    unsafe {
                        int32 status = IProbe.TryRead(receiver,
                            first < second, &init value);
                        if (status >= 0) { return value; }
                        return fallback;
                    }
                }
                int32 InvokeConverted(ptr<void> receiver, float32 input,
                    int32 fallback) {
                    init int32 value;
                    unsafe {
                        int32 status = IProbe.TryReadConverted(receiver,
                            float64(input), &init value);
                        if (status >= 0) { return value; }
                        return fallback;
                    }
                }
                """), reference, options);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new AssertionException("Expected a bound consumer.");
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "consumer.module", schemaIdentity, 1, []);
        var schema
            = TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft;
        var consumerTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact, schema));
        var booleanCode = GetBodyCode("InvokeBoolean");
        var booleanComparisonOffset
            = Array.IndexOf(booleanCode, (byte)0xC2);
        Assert.That(booleanComparisonOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(booleanCode[booleanComparisonOffset + 1],
            Is.EqualTo((byte)0x01));
        var orderedCode = GetBodyCode("InvokeOrdered");
        var orderedComparisonOffset
            = Array.IndexOf(orderedCode, (byte)0xC2);
        Assert.That(orderedComparisonOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(orderedCode[orderedComparisonOffset + 1],
            Is.EqualTo((byte)0));
        Assert.That(orderedCode[orderedComparisonOffset + 4],
            Is.EqualTo((byte)0xC3));
        Assert.That(orderedCode[orderedComparisonOffset + 5],
            Is.EqualTo((byte)0x01));
        var convertedCode = GetBodyCode("InvokeConverted");
        Assert.That(convertedCode, Does.Contain((byte)0x50));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "consumer.module", [providerInput]);
        AssertComparison(loaded, "InvokeBoolean", TokenKind.EqualsEquals,
            PrimitiveType.Bool);
        AssertComparison(loaded, "InvokeOrdered", TokenKind.Less,
            PrimitiveType.Int32);
        AssertConversion(loaded);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            "consumer.module", schemaIdentity, 1, []);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            "consumer.module", [providerInput]);
        AssertComparison(reloaded, "InvokeBoolean", TokenKind.EqualsEquals,
            PrimitiveType.Bool);
        AssertComparison(reloaded, "InvokeOrdered", TokenKind.Less,
            PrimitiveType.Int32);
        AssertConversion(reloaded);

        byte[] GetBodyCode(string name)
        {
            var row = Enumerable.Range(1, consumerTables.Methods.Count)
                .Single(candidate =>
                    consumerTables.Methods.GetName(candidate) == name);
            var body = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(row),
                consumerTables.Contracts.Count);
            return body.GetBlockCode(0).ToArray();
        }

        static void AssertComparison(BoundProgram program, string name,
            TokenKind operation, PrimitiveType operandType)
        {
            var function = program.Functions.Single(
                candidate => candidate.Name == name);
            BoundCall? foreignCall = null;
            _ = BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall call
                        && call.ForeignInterfaceCall?.Member.Name
                            == "TryRead")
                    {
                        foreignCall = call;
                    }
                });
            var selectedCall = foreignCall
                ?? throw new AssertionException("Expected an FNI call.");
            Assert.That(selectedCall.Arguments.Length, Is.EqualTo(2));
            Assert.That(selectedCall.Arguments[0],
                Is.TypeOf<BoundBinary>());
            var comparison = (BoundBinary)selectedCall.Arguments[0];
            Assert.That(comparison.Operator,
                Is.EqualTo(operation));
            Assert.That(comparison.Type, Is.EqualTo(PrimitiveType.Bool));
            Assert.That(comparison.Left, Is.TypeOf<BoundRead>());
            Assert.That(comparison.Left.Type, Is.EqualTo(operandType));
            Assert.That(((BoundRead)comparison.Left).Variable,
                Is.EqualTo(1));
            Assert.That(comparison.Right, Is.TypeOf<BoundRead>());
            Assert.That(comparison.Right.Type, Is.EqualTo(operandType));
            Assert.That(((BoundRead)comparison.Right).Variable,
                Is.EqualTo(2));
            Assert.That(selectedCall.Arguments[1],
                Is.TypeOf<BoundNativeOutputReservation>());
        }

        static void AssertConversion(BoundProgram program)
        {
            var function = program.Functions.Single(
                static candidate => candidate.Name == "InvokeConverted");
            BoundCall? foreignCall = null;
            _ = BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall call
                        && call.ForeignInterfaceCall?.Member.Name
                            == "TryReadConverted")
                    {
                        foreignCall = call;
                    }
                });
            var selectedCall = foreignCall
                ?? throw new AssertionException(
                    "Expected the FNI TryReadConverted call.");
            Assert.That(selectedCall.Arguments[0],
                Is.TypeOf<BoundCast>());
            var conversion = (BoundCast)selectedCall.Arguments[0];
            Assert.That(conversion.Type,
                Is.EqualTo(PrimitiveType.Float64));
            Assert.That(conversion.Operand, Is.TypeOf<BoundRead>());
            Assert.That(conversion.Operand.Type,
                Is.EqualTo(PrimitiveType.Float32));
            Assert.That(((BoundRead)conversion.Operand).Variable,
                Is.EqualTo(1));
            Assert.That(selectedCall.Arguments[1],
                Is.TypeOf<BoundNativeOutputReservation>());
        }
    }

    [Test]
    public void StandardForeignNativeInterfaceCallsRoundTripScalarRelationPredicates()
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
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Terrum.Interop.NativeCallable(
                    CallConv: Terrum.Interop.NativeCallingConvention.Cdecl)]
                [Terrum.Interop.ForeignNativeInterface(
                    VTableByteOffset: uint32(0))]
                public interface IProbe {
                    public unsafe bool Read(bool value);
                }
                """), options);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program
            ?? throw new AssertionException("Expected a bound provider.");
        var providerInterface = providerProgram.Interfaces.Single();
        var providerRead = providerInterface.Members.Single();
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "provider.module", schemaIdentity, 1, [
                new TrRmSourceTypeExport("", "IProbe", "provider/IProbe") {
                    GenericArity = 0,
                    InterfaceMethodExports = [
                        new TrRmSourceInterfaceMethodExport(
                            providerRead.SourceInputOrdinal,
                            providerRead.SourceDeclarationSpan,
                            "provider/IProbe.Read"),
                    ],
                },
            ]);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "provider.module", []);
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                bool LessI32(ptr<void> receiver, int32 left, int32 right) {
                    unsafe { return IProbe.Read(receiver, left < right); }
                }
                bool EqualI32(ptr<void> receiver, int32 left, int32 right) {
                    unsafe { return IProbe.Read(receiver, left == right); }
                }
                bool LessEqualU64(ptr<void> receiver, uint64 left,
                    uint64 right) {
                    unsafe { return IProbe.Read(receiver, left <= right); }
                }
                bool GreaterI128(ptr<void> receiver, int128 left,
                    int128 right) {
                    unsafe { return IProbe.Read(receiver, left > right); }
                }
                bool NotEqualI32(ptr<void> receiver, int32 left,
                    int32 right) {
                    unsafe { return IProbe.Read(receiver, left != right); }
                }
                bool GreaterEqualF64(ptr<void> receiver, float64 left,
                    float64 right) {
                    unsafe { return IProbe.Read(receiver, left >= right); }
                }
                bool NotEqualF64(ptr<void> receiver, float64 left,
                    float64 right) {
                    unsafe { return IProbe.Read(receiver, left != right); }
                }
                """), reference, options);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new AssertionException("Expected a bound consumer.");
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "consumer.module", schemaIdentity, 1, []);
        var schema
            = TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft;
        var consumerTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact, schema));
        var cases = new[] {
            ("LessI32", TokenKind.Less, PrimitiveType.Int32, (byte)0x01),
            ("EqualI32", TokenKind.EqualsEquals, PrimitiveType.Int32,
                (byte)0x02),
            ("LessEqualU64", TokenKind.LessEquals, PrimitiveType.UInt64,
                (byte)0x03),
            ("GreaterI128", TokenKind.Greater, PrimitiveType.Int128,
                (byte)0x04),
            ("NotEqualI32", TokenKind.BangEquals, PrimitiveType.Int32,
                (byte)0x0D),
            ("GreaterEqualF64", TokenKind.GreaterEquals,
                PrimitiveType.Float64, (byte)0x06),
            ("NotEqualF64", TokenKind.BangEquals, PrimitiveType.Float64,
                (byte)0x0D),
        };
        foreach (var (name, operation, operandType, predicateMask) in cases)
        {
            var row = Enumerable.Range(1, consumerTables.Methods.Count)
                .Single(candidate =>
                    consumerTables.Methods.GetName(candidate) == name);
            var body = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(row),
                consumerTables.Contracts.Count);
            var code = body.GetBlockCode(0).ToArray();
            var comparisonOffset = Array.IndexOf(code, (byte)0xC2);
            Assert.That(comparisonOffset, Is.GreaterThanOrEqualTo(0), name);
            Assert.That(code[comparisonOffset + 1], Is.EqualTo((byte)0),
                name);
            Assert.That(code[comparisonOffset + 4], Is.EqualTo((byte)0xC3),
                name);
            Assert.That(code[comparisonOffset + 5],
                Is.EqualTo(predicateMask), name);
        }

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "consumer.module", [providerInput]);
        AssertComparisons(loaded);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            "consumer.module", schemaIdentity, 1, []);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            "consumer.module", [providerInput]);
        AssertComparisons(reloaded);
        var lessMethodRow = Enumerable.Range(1, consumerTables.Methods.Count)
            .Single(candidate =>
                consumerTables.Methods.GetName(candidate) == "LessI32");
        var lessBodyRow
            = consumerTables.Methods.GetBodyRow(lessMethodRow);
        var malformedPredicate = RewriteBodyCode(consumerArtifact,
            lessBodyRow, static code => {
                var comparisonOffset = Array.IndexOf(code, (byte)0xC2);
                Assert.That(comparisonOffset,
                    Is.GreaterThanOrEqualTo(0));
                code[comparisonOffset + 5] = 0;
                return code;
            });
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    malformedPredicate),
                "consumer.module", [providerInput]));

        void AssertComparisons(BoundProgram program)
        {
            foreach (var (name, operation, operandType, _) in cases)
            {
                var function = program.Functions.Single(
                    candidate => candidate.Name == name);
                BoundCall? foreignCall = null;
                _ = BoundTreeTraversal.VisitExpressions(function.Body,
                    expression =>
                    {
                        if (expression is BoundCall call
                            && call.ForeignInterfaceCall?.Member.Name
                                == "Read")
                        {
                            foreignCall = call;
                        }
                    });
                var selectedCall = foreignCall
                    ?? throw new AssertionException(
                        $"Expected the FNI Read call in {name}.");
                Assert.That(selectedCall.Arguments[0],
                    Is.TypeOf<BoundBinary>(), name);
                var comparison = (BoundBinary)selectedCall.Arguments[0];
                Assert.That(comparison.Operator, Is.EqualTo(operation), name);
                Assert.That(comparison.Type,
                    Is.EqualTo(PrimitiveType.Bool), name);
                Assert.That(comparison.Left.Type,
                    Is.EqualTo(operandType), name);
                Assert.That(comparison.Right.Type,
                    Is.EqualTo(operandType), name);
            }
        }

        static byte[] RewriteBodyCode(byte[] artifact, int bodyRow,
            Func<byte[], byte[]> rewrite)
        {
            var index = new TrRmArtifactIndex(artifact,
                TrRmArtifactSchema
                    .ForeignNativeInterfaceParameterSurfaceDraft);
            Assert.That(index.TryGetSection(0x0020, out var bodySection,
                out var bodyCount), Is.True);
            var bodies = new TrRmBodyTableReader(bodySection.ToArray(),
                bodyCount);
            var bodyRows = new ReadOnlyMemory<byte>[bodies.Count];
            for (var row = 1; row <= bodies.Count; row++)
            {
                var rawBody = bodies.GetRow(row).ToArray();
                if (row == bodyRow)
                {
                    var body = new TrRmRegisterBodyReader(rawBody);
                    var code = rewrite(body.Code.ToArray());
                    Assert.That(code.Length,
                        Is.EqualTo(body.CodeByteLength));
                    code.CopyTo(rawBody,
                        rawBody.Length - body.CodeByteLength);
                }

                bodyRows[row - 1] = rawBody;
            }

            var rewrittenBodySection
                = TrRmVariableTableWriter.EncodeBodies(bodyRows);
            var sections = new TrRmSection[index.SectionCount];
            for (var sectionIndex = 0;
                sectionIndex < index.SectionCount;
                sectionIndex++)
            {
                index.GetSection(sectionIndex, out var kind, out var flags,
                    out var bytes, out var recordCount);
                sections[sectionIndex] = new TrRmSection(kind, flags,
                    checked((uint)recordCount),
                    kind == 0x0020
                        ? rewrittenBodySection : bytes.ToArray());
            }

            return TrRmArtifactWriter.Encode(sections,
                TrRmArtifactSchema
                    .ForeignNativeInterfaceParameterSurfaceDraft);
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
                public unsafe int32 GetIndexWithBias(ptr<int32> source,
                    int32 bias);
                public unsafe int32 GetOtherIndex(ptr<int32> source);
                public unsafe int32 UseIndex(int32 index);
                public unsafe int32 UseIndexWithDefault(int32 index,
                    int32 bias = 7);
                public unsafe int32 UsePair(int32 first, int32 second);
                public unsafe int32 UseTriple(int32 first, int32 second,
                    int32 third);
                public unsafe void UsePairVoid(int32 first, int32 second);
                public unsafe void Touch(ptr<int32> source);
                public unsafe void TouchValue(int32 value);

                [DoesNotReturn]
                public unsafe int32 StopWithIndex(int32 index);

                [param(value): Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonNegative)]
                public unsafe int32 TryReadPair(int32 first, int32 second,
                    write ptr<int32> value);

                [param(value): Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonNegative)]
                public unsafe int32 TryReadTriple(int32 first, int32 second,
                    int32 third, write ptr<int32> value);
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

            int32 InvokeNestedCorrelated(ptr<void> receiver,
                ptr<int32> sourceA, ptr<int32> sourceB, int32 fallback) {
                init int32 value;
                unsafe {
                    int32 status = IProbe.TryReadPair(receiver,
                        second: IProbe.GetOtherIndex(receiver, sourceB),
                        first: IProbe.GetIndex(receiver, sourceA),
                        value: &init value);
                    if (status >= 0) { return value; }
                    return fallback;
                }
            }

            int32 InvokeNestedConstantCorrelated(ptr<void> receiver,
                ptr<int32> sourceA, ptr<int32> sourceB, int32 fallback) {
                init int32 value;
                unsafe {
                    int32 status = IProbe.TryReadTriple(receiver,
                        second: IProbe.GetIndexWithBias(receiver, sourceB,
                            bias: 42),
                        first: IProbe.GetIndex(receiver, sourceA),
                        third: 42, value: &init value);
                    if (status >= 0) { return value; }
                    return fallback;
                }
            }

            int32 InvokeNestedValuePair(ptr<void> receiver,
                ptr<int32> sourceA, ptr<int32> sourceB) {
                unsafe {
                    return IProbe.UsePair(receiver,
                        second: IProbe.GetOtherIndex(receiver, sourceB),
                        first: IProbe.GetIndex(receiver, sourceA));
                }
            }

            int32 InvokeNestedValuePairWithStagedInput(
                ptr<void> receiver, ptr<int32> sourceA,
                ptr<int32> sourceB, int32 value) {
                int32 staged = value;
                unsafe {
                    return IProbe.UseTriple(receiver,
                        second: IProbe.GetOtherIndex(receiver, sourceB),
                        first: IProbe.GetIndex(receiver, sourceA),
                        third: staged);
                }
            }

            int32 InvokeNestedValuePairWithConstant(ptr<void> receiver,
                ptr<int32> sourceA, ptr<int32> sourceB) {
                unsafe {
                    return IProbe.UseTriple(receiver,
                        second: IProbe.GetOtherIndex(receiver, sourceB),
                        first: IProbe.GetIndex(receiver, sourceA),
                        third: 42);
                }
            }

            int32 InvokeNestedConstantArgument(ptr<void> receiver,
                ptr<int32> source) {
                unsafe {
                    return IProbe.UseIndex(receiver,
                        IProbe.GetIndexWithBias(receiver, source, bias: 42));
                }
            }

            [DoesNotReturn]
            void InvokeNestedTerminal(ptr<void> receiver,
                ptr<int32> source) {
                unsafe {
                    IProbe.StopWithIndex(receiver,
                        IProbe.GetIndexWithBias(receiver, source, bias: 42));
                }
            }

            [DoesNotReturn]
            void InvokeNestedTerminalSequence(ptr<void> receiver,
                ptr<int32> source) {
                unsafe {
                    IProbe.TouchValue(receiver, 42);
                    IProbe.StopWithIndex(receiver,
                        IProbe.GetIndex(receiver, source));
                }
            }

            [DoesNotReturn]
            void InvokeTerminalSequence(ptr<void> receiver) {
                unsafe {
                    IProbe.TouchValue(receiver, 42);
                    IProbe.StopWithIndex(receiver, 42);
                }
            }

            void InvokeNestedVoidPair(ptr<void> receiver,
                ptr<int32> sourceA, ptr<int32> sourceB) {
                unsafe {
                    IProbe.UsePairVoid(receiver,
                        second: IProbe.GetOtherIndex(receiver, sourceB),
                        first: IProbe.GetIndex(receiver, sourceA));
                    return;
                }
            }

            void InvokeSequence(ptr<void> receiver, ptr<int32> source) {
                unsafe {
                    IProbe.Touch(receiver, source);
                    IProbe.TouchValue(receiver, 42);
                    return;
                }
            }

            int32 InvokeValueSequence(ptr<void> receiver, int32 value) {
                unsafe {
                    IProbe.TouchValue(receiver, 42);
                    return IProbe.UseIndexWithDefault(receiver, value);
                }
            }

            int32 InvokeNestedValueSequence(ptr<void> receiver,
                ptr<int32> source) {
                unsafe {
                    IProbe.TouchValue(receiver, 42);
                    return IProbe.UseIndex(receiver,
                        IProbe.GetIndex(receiver, source));
                }
            }

            int32 InvokeNestedPairSequence(ptr<void> receiver,
                ptr<int32> sourceA, ptr<int32> sourceB) {
                unsafe {
                    IProbe.TouchValue(receiver, 42);
                    return IProbe.UsePair(receiver,
                        second: IProbe.GetOtherIndex(receiver, sourceB),
                        first: IProbe.GetIndex(receiver, sourceA));
                }
            }

            void TouchViaHelper(ptr<void> receiver) {
                unsafe {
                    IProbe.TouchValue(receiver, 42);
                    return;
                }
            }

            int32 InvokeHelperSequence(ptr<void> receiver, int32 value) {
                unsafe {
                    TouchViaHelper(receiver);
                    return IProbe.UseIndex(receiver, value);
                }
            }

            int32 GetIndexViaFni(ptr<void> receiver, ptr<int32> source,
                int32 bias) {
                unsafe {
                    return IProbe.GetIndexWithBias(receiver, source, bias);
                }
            }

            int32 InvokeNestedLocalValueSequence(ptr<void> receiver,
                ptr<int32> source) {
                unsafe {
                    IProbe.TouchValue(receiver, 42);
                    return IProbe.UseIndex(receiver,
                        GetIndexViaFni(receiver, source, 7));
                }
            }

            int32 UseIndexViaFni(ptr<void> receiver, int32 index) {
                unsafe { return IProbe.UseIndex(receiver, index); }
            }

            int32 InvokeNestedFniValueViaLocalHelper(ptr<void> receiver,
                ptr<int32> source) {
                unsafe {
                    IProbe.TouchValue(receiver, 42);
                    return IProbe.UseIndex(receiver,
                        UseIndexViaFni(receiver,
                            IProbe.GetIndex(receiver, source)));
                }
            }

            int32 InvokeConstant(ptr<void> receiver) {
                unsafe { return IProbe.UseIndex(receiver, 42); }
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

            int32 InvokeNestedCorrelated(ptr<void> receiver,
                ptr<int32> sourceA, ptr<int32> sourceB, int32 fallback) {
                init int32 value;
                unsafe {
                    int32 status = IProbe.TryReadPair(receiver,
                        second: IProbe.GetOtherIndex(receiver, sourceB),
                        first: IProbe.GetIndex(receiver, sourceA),
                        value: &init value);
                    if (status >= 0) { return value; }
                    return fallback;
                }
            }

            int32 InvokeNestedConstantCorrelated(ptr<void> receiver,
                ptr<int32> sourceA, ptr<int32> sourceB, int32 fallback) {
                init int32 value;
                unsafe {
                    int32 status = IProbe.TryReadTriple(receiver,
                        second: IProbe.GetIndexWithBias(receiver, sourceB,
                            bias: 42),
                        first: IProbe.GetIndex(receiver, sourceA),
                        third: 42, value: &init value);
                    if (status >= 0) { return value; }
                    return fallback;
                }
            }

            int32 InvokeNestedValuePair(ptr<void> receiver,
                ptr<int32> sourceA, ptr<int32> sourceB) {
                unsafe {
                    return IProbe.UsePair(receiver,
                        second: IProbe.GetOtherIndex(receiver, sourceB),
                        first: IProbe.GetIndex(receiver, sourceA));
                }
            }

            int32 InvokeNestedValuePairWithStagedInput(
                ptr<void> receiver, ptr<int32> sourceA,
                ptr<int32> sourceB, int32 value) {
                int32 staged = value;
                unsafe {
                    return IProbe.UseTriple(receiver,
                        second: IProbe.GetOtherIndex(receiver, sourceB),
                        first: IProbe.GetIndex(receiver, sourceA),
                        third: staged);
                }
            }

            int32 InvokeNestedValuePairWithConstant(ptr<void> receiver,
                ptr<int32> sourceA, ptr<int32> sourceB) {
                unsafe {
                    return IProbe.UseTriple(receiver,
                        second: IProbe.GetOtherIndex(receiver, sourceB),
                        first: IProbe.GetIndex(receiver, sourceA),
                        third: 42);
                }
            }

            int32 InvokeNestedConstantArgument(ptr<void> receiver,
                ptr<int32> source) {
                unsafe {
                    return IProbe.UseIndex(receiver,
                        IProbe.GetIndexWithBias(receiver, source, bias: 42));
                }
            }

            [DoesNotReturn]
            void InvokeNestedTerminal(ptr<void> receiver,
                ptr<int32> source) {
                unsafe {
                    IProbe.StopWithIndex(receiver,
                        IProbe.GetIndexWithBias(receiver, source, bias: 42));
                }
            }

            [DoesNotReturn]
            void InvokeNestedTerminalSequence(ptr<void> receiver,
                ptr<int32> source) {
                unsafe {
                    IProbe.TouchValue(receiver, 42);
                    IProbe.StopWithIndex(receiver,
                        IProbe.GetIndex(receiver, source));
                }
            }

            [DoesNotReturn]
            void InvokeTerminalSequence(ptr<void> receiver) {
                unsafe {
                    IProbe.TouchValue(receiver, 42);
                    IProbe.StopWithIndex(receiver, 42);
                }
            }

            void InvokeNestedVoidPair(ptr<void> receiver,
                ptr<int32> sourceA, ptr<int32> sourceB) {
                unsafe {
                    IProbe.UsePairVoid(receiver,
                        second: IProbe.GetOtherIndex(receiver, sourceB),
                        first: IProbe.GetIndex(receiver, sourceA));
                    return;
                }
            }

            void InvokeSequence(ptr<void> receiver, ptr<int32> source) {
                unsafe {
                    IProbe.Touch(receiver, source);
                    IProbe.TouchValue(receiver, 42);
                    return;
                }
            }

            int32 InvokeValueSequence(ptr<void> receiver, int32 value) {
                unsafe {
                    IProbe.TouchValue(receiver, 42);
                    return IProbe.UseIndexWithDefault(receiver, value);
                }
            }

            int32 InvokeNestedValueSequence(ptr<void> receiver,
                ptr<int32> source) {
                unsafe {
                    IProbe.TouchValue(receiver, 42);
                    return IProbe.UseIndex(receiver,
                        IProbe.GetIndex(receiver, source));
                }
            }

            int32 InvokeNestedPairSequence(ptr<void> receiver,
                ptr<int32> sourceA, ptr<int32> sourceB) {
                unsafe {
                    IProbe.TouchValue(receiver, 42);
                    return IProbe.UsePair(receiver,
                        second: IProbe.GetOtherIndex(receiver, sourceB),
                        first: IProbe.GetIndex(receiver, sourceA));
                }
            }

            void TouchViaHelper(ptr<void> receiver) {
                unsafe {
                    IProbe.TouchValue(receiver, 42);
                    return;
                }
            }

            int32 InvokeHelperSequence(ptr<void> receiver, int32 value) {
                unsafe {
                    TouchViaHelper(receiver);
                    return IProbe.UseIndex(receiver, value);
                }
            }

            int32 GetIndexViaFni(ptr<void> receiver, ptr<int32> source,
                int32 bias) {
                unsafe {
                    return IProbe.GetIndexWithBias(receiver, source, bias);
                }
            }

            int32 InvokeNestedLocalValueSequence(ptr<void> receiver,
                ptr<int32> source) {
                unsafe {
                    IProbe.TouchValue(receiver, 42);
                    return IProbe.UseIndex(receiver,
                        GetIndexViaFni(receiver, source, 7));
                }
            }

            int32 UseIndexViaFni(ptr<void> receiver, int32 index) {
                unsafe { return IProbe.UseIndex(receiver, index); }
            }

            int32 InvokeNestedFniValueViaLocalHelper(ptr<void> receiver,
                ptr<int32> source) {
                unsafe {
                    IProbe.TouchValue(receiver, 42);
                    return IProbe.UseIndex(receiver,
                        UseIndexViaFni(receiver,
                            IProbe.GetIndex(receiver, source)));
                }
            }

            int32 InvokeConstant(ptr<void> receiver) {
                unsafe { return IProbe.UseIndex(receiver, 42); }
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
        var providerNestedCorrelatedRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row)
                    == "InvokeNestedCorrelated");
        var providerNestedCorrelatedBody
            = providerTables.Bodies.GetRegisterBody(
                providerTables.Methods.GetBodyRow(
                    providerNestedCorrelatedRow),
                providerTables.Contracts.Count);
        AssertNestedFniConditionalOutputBody(
            providerNestedCorrelatedBody, 0x16);
        var providerNestedConstantCorrelatedRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row)
                    == "InvokeNestedConstantCorrelated");
        AssertNestedFniMixedConditionalOutputBody(
            providerTables.Bodies.GetRegisterBody(
                providerTables.Methods.GetBodyRow(
                    providerNestedConstantCorrelatedRow),
                providerTables.Contracts.Count), 0x16);
        foreach (var functionName in new[] {
                    "InvokeNestedValuePair", "InvokeNestedVoidPair",
                })
        {
            var row = Enumerable.Range(1, providerTables.Methods.Count)
                .Single(candidate => providerTables.Methods.GetName(candidate)
                    == functionName);
            AssertNestedFniDirectPairBody(
                providerTables.Bodies.GetRegisterBody(
                    providerTables.Methods.GetBodyRow(row),
                    providerTables.Contracts.Count), 0x16,
                hasValueResult: functionName == "InvokeNestedValuePair");
        }
        var providerNestedStagedPairRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row)
                    == "InvokeNestedValuePairWithStagedInput");
        AssertNestedFniStagedPairBody(
            providerTables.Bodies.GetRegisterBody(
                providerTables.Methods.GetBodyRow(
                    providerNestedStagedPairRow),
                providerTables.Contracts.Count), 0x16);
        var providerConstantRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row) == "InvokeConstant");
        AssertDirectFniConstantBody(
            providerTables.Bodies.GetRegisterBody(
                providerTables.Methods.GetBodyRow(providerConstantRow),
                providerTables.Contracts.Count), 0x16);
        var providerMixedConstantRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row)
                    == "InvokeNestedValuePairWithConstant");
        AssertNestedFniMixedConstantBody(
            providerTables.Bodies.GetRegisterBody(
                providerTables.Methods.GetBodyRow(providerMixedConstantRow),
                providerTables.Contracts.Count), 0x16);
        var providerNestedConstantArgumentRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row)
                    == "InvokeNestedConstantArgument");
        AssertNestedFniDirectConstantNestedBody(
            providerTables.Bodies.GetRegisterBody(
                providerTables.Methods.GetBodyRow(
                    providerNestedConstantArgumentRow),
                providerTables.Contracts.Count), 0x16);
        var providerNestedTerminalRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row)
                    == "InvokeNestedTerminal");
        AssertNestedFniTerminalBody(
            providerTables.Bodies.GetRegisterBody(
                providerTables.Methods.GetBodyRow(
                    providerNestedTerminalRow),
                providerTables.Contracts.Count), 0x16);
        var providerNestedTerminalSequenceRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row)
                    == "InvokeNestedTerminalSequence");
        AssertNestedFniTerminalSequenceBody(
            providerTables.Bodies.GetRegisterBody(
                providerTables.Methods.GetBodyRow(
                    providerNestedTerminalSequenceRow),
                providerTables.Contracts.Count), 0x16);
        var providerTerminalSequenceRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row)
                    == "InvokeTerminalSequence");
        AssertNestedFniTerminalCallSequenceBody(
            providerTables.Bodies.GetRegisterBody(
                providerTables.Methods.GetBodyRow(providerTerminalSequenceRow),
                providerTables.Contracts.Count), 0x16);
        var providerVoidCallSequenceRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row) == "InvokeSequence");
        AssertNestedFniVoidCallSequenceBody(
            providerTables.Bodies.GetRegisterBody(
                providerTables.Methods.GetBodyRow(
                    providerVoidCallSequenceRow),
                providerTables.Contracts.Count), 0x16);
        var providerValueCallSequenceRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row) == "InvokeValueSequence");
        AssertNestedFniValueCallSequenceBody(
            providerTables.Bodies.GetRegisterBody(
                providerTables.Methods.GetBodyRow(
                    providerValueCallSequenceRow),
                providerTables.Contracts.Count), 0x16);
        var providerNestedValueSequenceRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row)
                    == "InvokeNestedValueSequence");
        AssertNestedFniNestedValueSequenceBody(
            providerTables.Bodies.GetRegisterBody(
                providerTables.Methods.GetBodyRow(
                    providerNestedValueSequenceRow),
                providerTables.Contracts.Count), 0x16);
        var providerNestedPairSequenceRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row)
                    == "InvokeNestedPairSequence");
        AssertNestedFniNestedPairSequenceBody(
            providerTables.Bodies.GetRegisterBody(
                providerTables.Methods.GetBodyRow(
                    providerNestedPairSequenceRow),
                providerTables.Contracts.Count), 0x16);
        var providerHelperSequenceRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row)
                    == "InvokeHelperSequence");
        AssertLocalHelperFniSequenceBody(
            providerTables.Bodies.GetRegisterBody(
                providerTables.Methods.GetBodyRow(providerHelperSequenceRow),
                providerTables.Contracts.Count), 0x16);
        var providerLocalValueSequenceRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row)
                    == "InvokeNestedLocalValueSequence");
        AssertLocalValueHelperFniSequenceBody(
            providerTables.Bodies.GetRegisterBody(
                providerTables.Methods.GetBodyRow(
                    providerLocalValueSequenceRow),
                providerTables.Contracts.Count), 0x16);
        var providerNestedLocalFniValueSequenceRow
            = Enumerable.Range(1, providerTables.Methods.Count).Single(row =>
                providerTables.Methods.GetName(row)
                    == "InvokeNestedFniValueViaLocalHelper");
        AssertNestedFniLocalValueHelperBody(
            providerTables.Bodies.GetRegisterBody(
                providerTables.Methods.GetBodyRow(
                    providerNestedLocalFniValueSequenceRow),
                providerTables.Contracts.Count), 0x16);

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
        AssertNestedFniConditionalOutputCall(loadedProvider, null);
        AssertNestedFniMixedConditionalOutputCall(loadedProvider, null);
        AssertNestedFniDirectPairCall(loadedProvider,
            "InvokeNestedValuePair", "UsePair", PrimitiveType.Int32, null);
        AssertNestedFniDirectPairCall(loadedProvider,
            "InvokeNestedVoidPair", "UsePairVoid", PrimitiveType.Void, null);
        AssertNestedFniStagedPairCall(loadedProvider, null);
        AssertDirectFniConstantCall(loadedProvider, null);
        AssertNestedFniDirectConstantNestedCall(loadedProvider, null);
        AssertNestedFniTerminalCall(loadedProvider, null);
        AssertNestedFniTerminalSequence(loadedProvider, null);
        AssertNestedFniTerminalCallSequence(loadedProvider, null);
        AssertNestedFniVoidCallSequence(loadedProvider, null);
        AssertNestedFniValueCallSequence(loadedProvider, null);
        AssertNestedFniNestedValueSequence(loadedProvider, null);
        AssertNestedFniNestedPairSequence(loadedProvider, null);
        AssertLocalHelperFniSequence(loadedProvider, null);
        AssertLocalValueHelperFniSequence(loadedProvider, null);
        AssertNestedFniLocalValueHelper(
            loadedProvider, expectedProvider: null);
        AssertNestedFniMixedConstantCall(loadedProvider, null);
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
        var consumerNestedCorrelatedRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row)
                    == "InvokeNestedCorrelated");
        var consumerNestedCorrelatedBody
            = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(
                    consumerNestedCorrelatedRow),
                consumerTables.Contracts.Count);
        AssertNestedFniConditionalOutputBody(
            consumerNestedCorrelatedBody, 0x1C);
        var consumerNestedConstantCorrelatedRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row)
                    == "InvokeNestedConstantCorrelated");
        var consumerNestedConstantCorrelatedBody
            = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(
                    consumerNestedConstantCorrelatedRow),
                consumerTables.Contracts.Count);
        AssertNestedFniMixedConditionalOutputBody(
            consumerNestedConstantCorrelatedBody, 0x1C);
        var consumerNestedValuePairRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row)
                    == "InvokeNestedValuePair");
        var consumerNestedValuePairBody
            = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(
                    consumerNestedValuePairRow),
                consumerTables.Contracts.Count);
        AssertNestedFniDirectPairBody(
            consumerNestedValuePairBody, 0x1C, hasValueResult: true);
        var consumerNestedVoidPairRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row)
                    == "InvokeNestedVoidPair");
        AssertNestedFniDirectPairBody(
            consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(
                    consumerNestedVoidPairRow),
                consumerTables.Contracts.Count), 0x1C,
            hasValueResult: false);
        var consumerNestedStagedPairRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row)
                    == "InvokeNestedValuePairWithStagedInput");
        var consumerNestedStagedPairBody
            = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(
                    consumerNestedStagedPairRow),
                consumerTables.Contracts.Count);
        AssertNestedFniStagedPairBody(consumerNestedStagedPairBody, 0x1C);
        var consumerConstantRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row) == "InvokeConstant");
        AssertDirectFniConstantBody(
            consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(consumerConstantRow),
                consumerTables.Contracts.Count), 0x1C);
        var consumerMixedConstantRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row)
                    == "InvokeNestedValuePairWithConstant");
        var consumerMixedConstantBody
            = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(
                    consumerMixedConstantRow),
                consumerTables.Contracts.Count);
        AssertNestedFniMixedConstantBody(consumerMixedConstantBody, 0x1C);
        var consumerNestedConstantArgumentRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row)
                    == "InvokeNestedConstantArgument");
        var consumerNestedConstantArgumentBody
            = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(
                    consumerNestedConstantArgumentRow),
                consumerTables.Contracts.Count);
        AssertNestedFniDirectConstantNestedBody(
            consumerNestedConstantArgumentBody, 0x1C);
        var consumerNestedTerminalRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row)
                    == "InvokeNestedTerminal");
        var consumerNestedTerminalBody
            = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(
                    consumerNestedTerminalRow),
                consumerTables.Contracts.Count);
        AssertNestedFniTerminalBody(consumerNestedTerminalBody, 0x1C);
        var consumerNestedTerminalSequenceRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row)
                    == "InvokeNestedTerminalSequence");
        var consumerNestedTerminalSequenceBody
            = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(
                    consumerNestedTerminalSequenceRow),
                consumerTables.Contracts.Count);
        AssertNestedFniTerminalSequenceBody(
            consumerNestedTerminalSequenceBody, 0x1C);
        var consumerTerminalSequenceRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row)
                    == "InvokeTerminalSequence");
        var consumerTerminalSequenceBody
            = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(
                    consumerTerminalSequenceRow),
                consumerTables.Contracts.Count);
        AssertNestedFniTerminalCallSequenceBody(
            consumerTerminalSequenceBody, 0x1C);
        var consumerVoidCallSequenceRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row) == "InvokeSequence");
        var consumerVoidCallSequenceBody
            = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(
                    consumerVoidCallSequenceRow),
                consumerTables.Contracts.Count);
        AssertNestedFniVoidCallSequenceBody(
            consumerVoidCallSequenceBody, 0x1C);
        var consumerValueCallSequenceRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row) == "InvokeValueSequence");
        var consumerValueCallSequenceBody
            = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(
                    consumerValueCallSequenceRow),
                consumerTables.Contracts.Count);
        AssertNestedFniValueCallSequenceBody(
            consumerValueCallSequenceBody, 0x1C);
        var consumerNestedValueSequenceRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row)
                    == "InvokeNestedValueSequence");
        var consumerNestedValueSequenceBody
            = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(
                    consumerNestedValueSequenceRow),
                consumerTables.Contracts.Count);
        AssertNestedFniNestedValueSequenceBody(
            consumerNestedValueSequenceBody, 0x1C);
        var consumerNestedPairSequenceRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row)
                    == "InvokeNestedPairSequence");
        var consumerNestedPairSequenceBody
            = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(
                    consumerNestedPairSequenceRow),
                consumerTables.Contracts.Count);
        AssertNestedFniNestedPairSequenceBody(
            consumerNestedPairSequenceBody, 0x1C);
        var consumerHelperSequenceRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row)
                    == "InvokeHelperSequence");
        var consumerHelperSequenceBody
            = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(consumerHelperSequenceRow),
                consumerTables.Contracts.Count);
        AssertLocalHelperFniSequenceBody(
            consumerHelperSequenceBody, 0x1C);
        var consumerLocalValueSequenceRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row)
                    == "InvokeNestedLocalValueSequence");
        var consumerLocalValueSequenceBody
            = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(
                    consumerLocalValueSequenceRow),
                consumerTables.Contracts.Count);
        AssertLocalValueHelperFniSequenceBody(
            consumerLocalValueSequenceBody, 0x1C);
        var consumerNestedLocalFniValueSequenceRow
            = Enumerable.Range(1, consumerTables.Methods.Count).Single(row =>
                consumerTables.Methods.GetName(row)
                    == "InvokeNestedFniValueViaLocalHelper");
        var consumerNestedLocalFniValueSequenceBody
            = consumerTables.Bodies.GetRegisterBody(
                consumerTables.Methods.GetBodyRow(
                    consumerNestedLocalFniValueSequenceRow),
                consumerTables.Contracts.Count);
        AssertNestedFniLocalValueHelperBody(
            consumerNestedLocalFniValueSequenceBody, 0x1C);

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
        AssertMalformedNestedFniBodyRejected(
            consumerNestedCorrelatedRow, consumerNestedCorrelatedBody,
            (section, codeStart, _, _) => section[codeStart + 3] = 1);
        AssertMalformedNestedFniBodyRejected(
            consumerNestedCorrelatedRow, consumerNestedCorrelatedBody,
            (section, _, secondNestedCallRecordStart, _) =>
                BinaryPrimitives.WriteUInt16LittleEndian(
                    section.AsSpan(secondNestedCallRecordStart + 10), 1));
        AssertMalformedNestedFniBodyRejected(
            consumerNestedCorrelatedRow, consumerNestedCorrelatedBody,
            (section, _, _, outerCallRecordStart) =>
                BinaryPrimitives.WriteUInt16LittleEndian(
                    section.AsSpan(outerCallRecordStart + 30), 2));
        AssertMalformedNestedFniBodyRejected(
            consumerNestedValuePairRow, consumerNestedValuePairBody,
            (section, codeStart, _, _) => section[codeStart + 3] = 1);
        AssertMalformedNestedFniBodyRejected(
            consumerNestedValuePairRow, consumerNestedValuePairBody,
            (section, _, secondNestedCallRecordStart, _) =>
                BinaryPrimitives.WriteUInt16LittleEndian(
                    section.AsSpan(secondNestedCallRecordStart + 10), 1));
        AssertMalformedNestedFniBodyRejected(
            consumerNestedValuePairRow, consumerNestedValuePairBody,
            (section, _, _, outerCallRecordStart) =>
                BinaryPrimitives.WriteUInt16LittleEndian(
                    section.AsSpan(outerCallRecordStart + 26), 1));
        AssertMalformedNestedFniBodyRejected(
            consumerNestedConstantCorrelatedRow,
            consumerNestedConstantCorrelatedBody,
            (section, codeStart, _, _) => section[codeStart + 1] = 1);
        AssertMalformedNestedFniBodyRejected(
            consumerLocalValueSequenceRow,
            consumerLocalValueSequenceBody,
            (section, _, nestedCallRecordStart, _) =>
                section[nestedCallRecordStart + 5] = 0);
        AssertMalformedNestedFniBodyRejected(
            consumerLocalValueSequenceRow,
            consumerLocalValueSequenceBody,
            (section, _, nestedCallRecordStart, _) =>
                BinaryPrimitives.WriteUInt16LittleEndian(
                    section.AsSpan(nestedCallRecordStart + 10), 0));
        AssertMalformedNestedFniBodyRejected(
            consumerNestedLocalFniValueSequenceRow,
            consumerNestedLocalFniValueSequenceBody,
            (section, _, nestedCallRecordStart, _) =>
                BinaryPrimitives.WriteUInt16LittleEndian(
                    section.AsSpan(nestedCallRecordStart + 10), 1));

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
        AssertNestedFniConditionalOutputCall(
            loadedConsumer, "provider.module");
        AssertNestedFniMixedConditionalOutputCall(
            loadedConsumer, "provider.module");
        AssertNestedFniDirectPairCall(loadedConsumer,
            "InvokeNestedValuePair", "UsePair", PrimitiveType.Int32,
            "provider.module");
        AssertNestedFniDirectPairCall(loadedConsumer,
            "InvokeNestedVoidPair", "UsePairVoid", PrimitiveType.Void,
            "provider.module");
        AssertNestedFniStagedPairCall(
            loadedConsumer, "provider.module");
        AssertDirectFniConstantCall(loadedConsumer, "provider.module");
        AssertNestedFniDirectConstantNestedCall(
            loadedConsumer, "provider.module");
        AssertNestedFniTerminalCall(loadedConsumer, "provider.module");
        AssertNestedFniTerminalSequence(
            loadedConsumer, "provider.module");
        AssertNestedFniTerminalCallSequence(
            loadedConsumer, "provider.module");
        AssertNestedFniVoidCallSequence(
            loadedConsumer, "provider.module");
        AssertNestedFniValueCallSequence(
            loadedConsumer, "provider.module");
        AssertNestedFniNestedValueSequence(
            loadedConsumer, "provider.module");
        AssertNestedFniNestedPairSequence(
            loadedConsumer, "provider.module");
        AssertLocalHelperFniSequence(
            loadedConsumer, "provider.module");
        AssertLocalValueHelperFniSequence(
            loadedConsumer, "provider.module");
        AssertNestedFniLocalValueHelper(
            loadedConsumer, "provider.module");
        AssertNestedFniMixedConstantCall(
            loadedConsumer, "provider.module");
        AssertMalformedNestedFniBodyRejected(
            consumerValueCallSequenceRow, consumerValueCallSequenceBody,
            (section, _, valueCallRecordStart, _) =>
                section[valueCallRecordStart + 5] = 0);
        AssertMalformedNestedFniBodyRejected(
            consumerNestedValueSequenceRow,
            consumerNestedValueSequenceBody,
            (section, _, _, outerCallRecordStart) =>
                BinaryPrimitives.WriteUInt16LittleEndian(
                    section.AsSpan(outerCallRecordStart + 26), 1));
        AssertMalformedNestedFniBodyRejected(
            consumerNestedPairSequenceRow, consumerNestedPairSequenceBody,
            (section, _, _, outerCallRecordStart) =>
                BinaryPrimitives.WriteUInt16LittleEndian(
                    section.AsSpan(outerCallRecordStart + 26), 1),
            outerCallRecordIndex: 4);
        AssertMalformedNestedFniBodyRejected(
            consumerHelperSequenceRow, consumerHelperSequenceBody,
            (section, _, _, helperCallRecordStart) =>
                section[helperCallRecordStart + 5] = 1,
            outerCallRecordIndex: 1);
        AssertMalformedNestedFniBodyRejected(
            consumerNestedTerminalSequenceRow,
            consumerNestedTerminalSequenceBody,
            (section, _, _, outerCallRecordStart) =>
                section[outerCallRecordStart + 5] = 1);
        AssertMalformedNestedFniBodyRejected(
            consumerTerminalSequenceRow, consumerTerminalSequenceBody,
            (section, _, _, outerCallRecordStart) =>
                section[outerCallRecordStart + 5] = 1);
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(
            loadedConsumer, "consumer.module", schemaIdentity, 1, []);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                reemittedArtifact, schema));
        var reemittedNestedCorrelatedRow
            = Enumerable.Range(1, reemittedTables.Methods.Count).Single(row =>
                reemittedTables.Methods.GetName(row)
                    == "InvokeNestedCorrelated");
        var reemittedNestedCorrelatedBody
            = reemittedTables.Bodies.GetRegisterBody(
                reemittedTables.Methods.GetBodyRow(
                    reemittedNestedCorrelatedRow),
                reemittedTables.Contracts.Count);
        AssertNestedFniConditionalOutputBody(
            reemittedNestedCorrelatedBody, 0x1C);
        var reemittedNestedConstantCorrelatedRow
            = Enumerable.Range(1, reemittedTables.Methods.Count).Single(row =>
                reemittedTables.Methods.GetName(row)
                    == "InvokeNestedConstantCorrelated");
        AssertNestedFniMixedConditionalOutputBody(
            reemittedTables.Bodies.GetRegisterBody(
                reemittedTables.Methods.GetBodyRow(
                    reemittedNestedConstantCorrelatedRow),
                reemittedTables.Contracts.Count), 0x1C);
        foreach (var functionName in new[] {
                    "InvokeNestedValuePair", "InvokeNestedVoidPair",
                })
        {
            var row = Enumerable.Range(1, reemittedTables.Methods.Count)
                .Single(candidate => reemittedTables.Methods.GetName(candidate)
                    == functionName);
            AssertNestedFniDirectPairBody(
                reemittedTables.Bodies.GetRegisterBody(
                    reemittedTables.Methods.GetBodyRow(row),
                    reemittedTables.Contracts.Count), 0x1C,
                hasValueResult: functionName == "InvokeNestedValuePair");
        }
        var reemittedNestedStagedPairRow
            = Enumerable.Range(1, reemittedTables.Methods.Count).Single(row =>
                reemittedTables.Methods.GetName(row)
                    == "InvokeNestedValuePairWithStagedInput");
        AssertNestedFniStagedPairBody(
            reemittedTables.Bodies.GetRegisterBody(
                reemittedTables.Methods.GetBodyRow(
                    reemittedNestedStagedPairRow),
                reemittedTables.Contracts.Count), 0x1C);
        var reemittedConstantRow
            = Enumerable.Range(1, reemittedTables.Methods.Count).Single(row =>
                reemittedTables.Methods.GetName(row) == "InvokeConstant");
        AssertDirectFniConstantBody(
            reemittedTables.Bodies.GetRegisterBody(
                reemittedTables.Methods.GetBodyRow(reemittedConstantRow),
                reemittedTables.Contracts.Count), 0x1C);
        var reemittedMixedConstantRow
            = Enumerable.Range(1, reemittedTables.Methods.Count).Single(row =>
                reemittedTables.Methods.GetName(row)
                    == "InvokeNestedValuePairWithConstant");
        AssertNestedFniMixedConstantBody(
            reemittedTables.Bodies.GetRegisterBody(
                reemittedTables.Methods.GetBodyRow(
                    reemittedMixedConstantRow),
                reemittedTables.Contracts.Count), 0x1C);
        var reemittedNestedConstantArgumentRow
            = Enumerable.Range(1, reemittedTables.Methods.Count).Single(row =>
                reemittedTables.Methods.GetName(row)
                    == "InvokeNestedConstantArgument");
        AssertNestedFniDirectConstantNestedBody(
            reemittedTables.Bodies.GetRegisterBody(
                reemittedTables.Methods.GetBodyRow(
                    reemittedNestedConstantArgumentRow),
                reemittedTables.Contracts.Count), 0x1C);
        var reemittedNestedTerminalRow
            = Enumerable.Range(1, reemittedTables.Methods.Count).Single(row =>
                reemittedTables.Methods.GetName(row)
                    == "InvokeNestedTerminal");
        AssertNestedFniTerminalBody(
            reemittedTables.Bodies.GetRegisterBody(
                reemittedTables.Methods.GetBodyRow(
                    reemittedNestedTerminalRow),
                reemittedTables.Contracts.Count), 0x1C);
        var reemittedNestedTerminalSequenceRow
            = Enumerable.Range(1, reemittedTables.Methods.Count).Single(row =>
                reemittedTables.Methods.GetName(row)
                    == "InvokeNestedTerminalSequence");
        AssertNestedFniTerminalSequenceBody(
            reemittedTables.Bodies.GetRegisterBody(
                reemittedTables.Methods.GetBodyRow(
                    reemittedNestedTerminalSequenceRow),
                reemittedTables.Contracts.Count), 0x1C);
        var reemittedTerminalSequenceRow
            = Enumerable.Range(1, reemittedTables.Methods.Count).Single(row =>
                reemittedTables.Methods.GetName(row)
                    == "InvokeTerminalSequence");
        AssertNestedFniTerminalCallSequenceBody(
            reemittedTables.Bodies.GetRegisterBody(
                reemittedTables.Methods.GetBodyRow(
                    reemittedTerminalSequenceRow),
                reemittedTables.Contracts.Count), 0x1C);
        var reemittedVoidCallSequenceRow
            = Enumerable.Range(1, reemittedTables.Methods.Count).Single(row =>
                reemittedTables.Methods.GetName(row) == "InvokeSequence");
        AssertNestedFniVoidCallSequenceBody(
            reemittedTables.Bodies.GetRegisterBody(
                reemittedTables.Methods.GetBodyRow(
                    reemittedVoidCallSequenceRow),
                reemittedTables.Contracts.Count), 0x1C);
        var reemittedValueCallSequenceRow
            = Enumerable.Range(1, reemittedTables.Methods.Count).Single(row =>
                reemittedTables.Methods.GetName(row)
                    == "InvokeValueSequence");
        AssertNestedFniValueCallSequenceBody(
            reemittedTables.Bodies.GetRegisterBody(
                reemittedTables.Methods.GetBodyRow(
                    reemittedValueCallSequenceRow),
                reemittedTables.Contracts.Count), 0x1C);
        var reemittedNestedValueSequenceRow
            = Enumerable.Range(1, reemittedTables.Methods.Count).Single(row =>
                reemittedTables.Methods.GetName(row)
                    == "InvokeNestedValueSequence");
        AssertNestedFniNestedValueSequenceBody(
            reemittedTables.Bodies.GetRegisterBody(
                reemittedTables.Methods.GetBodyRow(
                    reemittedNestedValueSequenceRow),
                reemittedTables.Contracts.Count), 0x1C);
        var reemittedNestedPairSequenceRow
            = Enumerable.Range(1, reemittedTables.Methods.Count).Single(row =>
                reemittedTables.Methods.GetName(row)
                    == "InvokeNestedPairSequence");
        AssertNestedFniNestedPairSequenceBody(
            reemittedTables.Bodies.GetRegisterBody(
                reemittedTables.Methods.GetBodyRow(
                    reemittedNestedPairSequenceRow),
                reemittedTables.Contracts.Count), 0x1C);
        var reemittedHelperSequenceRow
            = Enumerable.Range(1, reemittedTables.Methods.Count).Single(row =>
                reemittedTables.Methods.GetName(row)
                    == "InvokeHelperSequence");
        AssertLocalHelperFniSequenceBody(
            reemittedTables.Bodies.GetRegisterBody(
                reemittedTables.Methods.GetBodyRow(
                    reemittedHelperSequenceRow),
                reemittedTables.Contracts.Count), 0x1C);
        var reemittedLocalValueSequenceRow
            = Enumerable.Range(1, reemittedTables.Methods.Count).Single(row =>
                reemittedTables.Methods.GetName(row)
                    == "InvokeNestedLocalValueSequence");
        AssertLocalValueHelperFniSequenceBody(
            reemittedTables.Bodies.GetRegisterBody(
                reemittedTables.Methods.GetBodyRow(
                    reemittedLocalValueSequenceRow),
                reemittedTables.Contracts.Count), 0x1C);
        var reemittedNestedLocalFniValueSequenceRow
            = Enumerable.Range(1, reemittedTables.Methods.Count).Single(row =>
                reemittedTables.Methods.GetName(row)
                    == "InvokeNestedFniValueViaLocalHelper");
        AssertNestedFniLocalValueHelperBody(
            reemittedTables.Bodies.GetRegisterBody(
                reemittedTables.Methods.GetBodyRow(
                    reemittedNestedLocalFniValueSequenceRow),
                reemittedTables.Contracts.Count), 0x1C);
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
        AssertNestedFniConditionalOutputCall(
            reloadedConsumer, "provider.module");
        AssertNestedFniMixedConditionalOutputCall(
            reloadedConsumer, "provider.module");
        AssertNestedFniDirectPairCall(reloadedConsumer,
            "InvokeNestedValuePair", "UsePair", PrimitiveType.Int32,
            "provider.module");
        AssertNestedFniDirectPairCall(reloadedConsumer,
            "InvokeNestedVoidPair", "UsePairVoid", PrimitiveType.Void,
            "provider.module");
        AssertNestedFniStagedPairCall(
            reloadedConsumer, "provider.module");
        AssertDirectFniConstantCall(reloadedConsumer, "provider.module");
        AssertNestedFniDirectConstantNestedCall(
            reloadedConsumer, "provider.module");
        AssertNestedFniTerminalCall(reloadedConsumer, "provider.module");
        AssertNestedFniTerminalSequence(
            reloadedConsumer, "provider.module");
        AssertNestedFniTerminalCallSequence(
            reloadedConsumer, "provider.module");
        AssertNestedFniVoidCallSequence(
            reloadedConsumer, "provider.module");
        AssertNestedFniValueCallSequence(
            reloadedConsumer, "provider.module");
        AssertNestedFniNestedValueSequence(
            reloadedConsumer, "provider.module");
        AssertNestedFniNestedPairSequence(
            reloadedConsumer, "provider.module");
        AssertLocalHelperFniSequence(
            reloadedConsumer, "provider.module");
        AssertLocalValueHelperFniSequence(
            reloadedConsumer, "provider.module");
        AssertNestedFniLocalValueHelper(
            reloadedConsumer, "provider.module");
        AssertNestedFniMixedConstantCall(
            reloadedConsumer, "provider.module");

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

        static void AssertNestedFniDirectPairBody(
            TrRmRegisterBodyReader body, byte methodTokenKind,
            bool hasValueResult)
        {
            Assert.That(body.BlockCount, Is.EqualTo(1));
            Assert.That(body.RecordCount, Is.EqualTo(4));
            Assert.That(body.MethodCount, Is.EqualTo(3));
            for (var methodIndex = 0; methodIndex < body.MethodCount;
                methodIndex++)
            {
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                    body.MethodMap[(methodIndex * sizeof(uint))..]) >> 24,
                    Is.EqualTo((uint)methodTokenKind));
            }

            var firstNestedCall = body.GetRecord(1);
            var secondNestedCall = body.GetRecord(2);
            var outerCall = body.GetRecord(3);
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                firstNestedCall[10..]), Is.EqualTo((ushort)1));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                secondNestedCall[10..]), Is.EqualTo((ushort)2));
            Assert.That(outerCall[5],
                Is.EqualTo(hasValueResult ? (byte)1 : (byte)0));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                outerCall[10..]), Is.Zero);
            Assert.That(new[] {
                BinaryPrimitives.ReadUInt16LittleEndian(
                    firstNestedCall[8..]),
                BinaryPrimitives.ReadUInt16LittleEndian(
                    secondNestedCall[8..]),
                BinaryPrimitives.ReadUInt16LittleEndian(outerCall[8..]),
            }.Distinct().Count(), Is.EqualTo(3));
            Assert.That((outerCall[24], outerCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(outerCall[26..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)2)));
            Assert.That((outerCall[28], outerCall[29],
                    BinaryPrimitives.ReadUInt16LittleEndian(outerCall[30..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)1)));
            Assert.That(body.GetBlockCode(0).ToArray(), Is.EqualTo(
                new byte[] {
                    0x90, 1, 0x90, 2, 0x90, 3,
                    hasValueResult ? (byte)0x17 : (byte)0x18, 0,
                }));
        }

        static void AssertNestedFniStagedPairBody(
            TrRmRegisterBodyReader body, byte methodTokenKind)
        {
            Assert.That(body.BlockCount, Is.EqualTo(1));
            Assert.That(body.RecordCount, Is.EqualTo(4));
            Assert.That(body.MethodCount, Is.EqualTo(3));
            for (var methodIndex = 0; methodIndex < body.MethodCount;
                methodIndex++)
            {
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                    body.MethodMap[(methodIndex * sizeof(uint))..]) >> 24,
                    Is.EqualTo((uint)methodTokenKind));
            }

            var firstNestedCall = body.GetRecord(1);
            var secondNestedCall = body.GetRecord(2);
            var outerCall = body.GetRecord(3);
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                firstNestedCall[10..]), Is.EqualTo((ushort)2));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                secondNestedCall[10..]), Is.EqualTo((ushort)3));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                outerCall[10..]), Is.Zero);
            Assert.That((outerCall[24], outerCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(outerCall[26..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)3)));
            Assert.That((outerCall[28], outerCall[29],
                    BinaryPrimitives.ReadUInt16LittleEndian(outerCall[30..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)2)));
            Assert.That((outerCall[32], outerCall[33],
                    BinaryPrimitives.ReadUInt16LittleEndian(outerCall[34..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)4)));
            Assert.That(body.GetBlockCode(0).ToArray(), Is.EqualTo(
                new byte[] {
                    0x11, 0x31, 0xD6, 0x11, 4, 0,
                    0x90, 1, 0x90, 2, 0x90, 3, 0x17, 0,
                }));
        }

        static void AssertDirectFniConstantBody(
            TrRmRegisterBodyReader body, byte methodTokenKind)
        {
            Assert.That(body.BlockCount, Is.EqualTo(1));
            Assert.That(body.RecordCount, Is.EqualTo(2));
            Assert.That(body.MethodCount, Is.EqualTo(1));
            Assert.That(body.ConstantCount, Is.EqualTo(1));
            Assert.That(body.ConstantMap.Length, Is.EqualTo(8));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                body.MethodMap) >> 24, Is.EqualTo((uint)methodTokenKind));
            var callSite = body.GetRecord(1);
            Assert.That((callSite[24], callSite[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(callSite[26..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)1)));
            Assert.That(body.GetBlockCode(0).ToArray(),
                Is.EqualTo(new byte[] {
                    0x16, 1, 0x90, 1, 0x17, 0,
                }));
        }

        static void AssertNestedFniMixedConstantBody(
            TrRmRegisterBodyReader body, byte methodTokenKind)
        {
            Assert.That(body.BlockCount, Is.EqualTo(1));
            Assert.That(body.RecordCount, Is.EqualTo(4));
            Assert.That(body.MethodCount, Is.EqualTo(3));
            Assert.That(body.ConstantCount, Is.EqualTo(2));
            Assert.That(body.ConstantMap.Length, Is.EqualTo(16));
            for (var methodIndex = 0; methodIndex < body.MethodCount;
                methodIndex++)
            {
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                    body.MethodMap[(methodIndex * sizeof(uint))..]) >> 24,
                    Is.EqualTo((uint)methodTokenKind));
            }

            var firstNestedCall = body.GetRecord(1);
            var secondNestedCall = body.GetRecord(2);
            var outerCall = body.GetRecord(3);
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                firstNestedCall[10..]), Is.EqualTo((ushort)2));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                secondNestedCall[10..]), Is.EqualTo((ushort)3));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                outerCall[10..]), Is.Zero);
            Assert.That((outerCall[24], outerCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(outerCall[26..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)3)));
            Assert.That((outerCall[28], outerCall[29],
                    BinaryPrimitives.ReadUInt16LittleEndian(outerCall[30..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)2)));
            Assert.That((outerCall[32], outerCall[33],
                    BinaryPrimitives.ReadUInt16LittleEndian(outerCall[34..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)1)));
            Assert.That(body.GetBlockCode(0).ToArray(), Is.EqualTo(
                new byte[] {
                    0x16, 1, 0x90, 1, 0x90, 2, 0x90, 3, 0x17, 0,
                }));
        }

        static void AssertNestedFniDirectConstantNestedBody(
            TrRmRegisterBodyReader body, byte methodTokenKind)
        {
            Assert.That(body.BlockCount, Is.EqualTo(1));
            Assert.That(body.RecordCount, Is.EqualTo(3));
            Assert.That(body.MethodCount, Is.EqualTo(2));
            Assert.That(body.ConstantCount, Is.EqualTo(1));
            Assert.That(body.ConstantMap.Length, Is.EqualTo(8));
            for (var methodIndex = 0; methodIndex < body.MethodCount;
                methodIndex++)
            {
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                    body.MethodMap[(methodIndex * sizeof(uint))..]) >> 24,
                    Is.EqualTo((uint)methodTokenKind));
            }

            var nestedCall = body.GetRecord(1);
            var outerCall = body.GetRecord(2);
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                nestedCall[10..]), Is.EqualTo((ushort)2));
            Assert.That((nestedCall[24], nestedCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(nestedCall[26..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)1)));
            Assert.That((nestedCall[28], nestedCall[29],
                    BinaryPrimitives.ReadUInt16LittleEndian(nestedCall[30..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)1)));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                outerCall[10..]), Is.Zero);
            Assert.That((outerCall[24], outerCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(outerCall[26..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)2)));
            Assert.That(body.GetBlockCode(0).ToArray(),
                Is.EqualTo(new byte[] {
                    0x16, 1, 0x90, 1, 0x90, 2, 0x17, 0,
                }));
        }

        static void AssertNestedFniTerminalBody(
            TrRmRegisterBodyReader body, byte methodTokenKind)
        {
            Assert.That(body.BlockCount, Is.EqualTo(1));
            Assert.That(body.RecordCount, Is.EqualTo(3));
            Assert.That(body.MethodCount, Is.EqualTo(2));
            Assert.That(body.ConstantCount, Is.EqualTo(1));
            Assert.That(body.ConstantMap.Length, Is.EqualTo(8));
            for (var methodIndex = 0; methodIndex < body.MethodCount;
                methodIndex++)
            {
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                    body.MethodMap[(methodIndex * sizeof(uint))..]) >> 24,
                    Is.EqualTo((uint)methodTokenKind));
            }

            var nestedCall = body.GetRecord(1);
            var outerCall = body.GetRecord(2);
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                nestedCall[10..]), Is.EqualTo((ushort)2));
            Assert.That(outerCall[5], Is.Zero);
            Assert.That((nestedCall[28], nestedCall[29],
                    BinaryPrimitives.ReadUInt16LittleEndian(nestedCall[30..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)1)));
            Assert.That((outerCall[24], outerCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(outerCall[26..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)2)));
            Assert.That(body.GetBlockCode(0).ToArray(),
                Is.EqualTo(new byte[] {
                    0x16, 1, 0x90, 1, 0x90, 2,
                }));
        }

        static void AssertNestedFniVoidCallSequenceBody(
            TrRmRegisterBodyReader body, byte methodTokenKind)
        {
            Assert.That(body.BlockCount, Is.EqualTo(1));
            Assert.That(body.RecordCount, Is.EqualTo(3));
            Assert.That(body.MethodCount, Is.EqualTo(2));
            Assert.That(body.ConstantCount, Is.EqualTo(1));
            Assert.That(body.ConstantMap.Length, Is.EqualTo(8));
            for (var methodIndex = 0; methodIndex < body.MethodCount;
                methodIndex++)
            {
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                    body.MethodMap[(methodIndex * sizeof(uint))..]) >> 24,
                    Is.EqualTo((uint)methodTokenKind));
            }

            var firstCall = body.GetRecord(1);
            var secondCall = body.GetRecord(2);
            Assert.That(firstCall[5], Is.Zero);
            Assert.That(secondCall[5], Is.Zero);
            Assert.That((firstCall[24], firstCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(firstCall[26..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)1)));
            Assert.That((secondCall[24], secondCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(secondCall[26..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)1)));
            Assert.That(body.GetBlockCode(0).ToArray(),
                Is.EqualTo(new byte[] {
                    0x16, 1, 0x90, 1, 0x90, 2, 0x18, 0,
                }));
        }

        static void AssertNestedFniValueCallSequenceBody(
            TrRmRegisterBodyReader body, byte methodTokenKind)
        {
            Assert.That(body.BlockCount, Is.EqualTo(1));
            Assert.That(body.RecordCount, Is.EqualTo(3));
            Assert.That(body.MethodCount, Is.EqualTo(2));
            Assert.That(body.ConstantCount, Is.EqualTo(2));
            Assert.That(body.ConstantMap.Length, Is.EqualTo(16));
            for (var methodIndex = 0; methodIndex < body.MethodCount;
                methodIndex++)
            {
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                    body.MethodMap[(methodIndex * sizeof(uint))..]) >> 24,
                    Is.EqualTo((uint)methodTokenKind));
            }

            var firstCall = body.GetRecord(1);
            var valueCall = body.GetRecord(2);
            Assert.That(firstCall[5], Is.Zero);
            Assert.That(valueCall[5], Is.EqualTo((byte)1));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                valueCall[10..]), Is.Zero);
            Assert.That((firstCall[20], firstCall[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(firstCall[22..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)0)));
            Assert.That((valueCall[20], valueCall[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(valueCall[22..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)0)));
            Assert.That((firstCall[24], firstCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(firstCall[26..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)1)));
            Assert.That((valueCall[24], valueCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(valueCall[26..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)1)));
            Assert.That((valueCall[28], valueCall[29],
                    BinaryPrimitives.ReadUInt16LittleEndian(valueCall[30..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)2)));
            Assert.That(body.GetBlockCode(0).ToArray(),
                Is.EqualTo(new byte[] {
                    0x16, 1, 0x16, 0x12, 0x90, 1, 0x90, 2, 0x17, 0,
                }));
        }

        static void AssertNestedFniNestedValueSequenceBody(
            TrRmRegisterBodyReader body, byte methodTokenKind)
        {
            Assert.That(body.BlockCount, Is.EqualTo(1));
            Assert.That(body.RecordCount, Is.EqualTo(4));
            Assert.That(body.MethodCount, Is.EqualTo(3));
            Assert.That(body.ConstantCount, Is.EqualTo(1));
            Assert.That(body.ConstantMap.Length, Is.EqualTo(8));
            for (var methodIndex = 0; methodIndex < body.MethodCount;
                methodIndex++)
            {
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                    body.MethodMap[(methodIndex * sizeof(uint))..]) >> 24,
                    Is.EqualTo((uint)methodTokenKind));
            }

            var prefixCall = body.GetRecord(1);
            var nestedCall = body.GetRecord(2);
            var valueCall = body.GetRecord(3);
            Assert.That(prefixCall[5], Is.Zero);
            Assert.That(nestedCall[5], Is.EqualTo((byte)1));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                nestedCall[10..]), Is.EqualTo((ushort)2));
            Assert.That(valueCall[5], Is.EqualTo((byte)1));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                valueCall[10..]), Is.Zero);
            Assert.That((prefixCall[20], prefixCall[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(prefixCall[22..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)0)));
            Assert.That((nestedCall[20], nestedCall[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(nestedCall[22..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)0)));
            Assert.That((valueCall[20], valueCall[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(valueCall[22..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)0)));
            Assert.That((prefixCall[24], prefixCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(prefixCall[26..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)1)));
            Assert.That((nestedCall[24], nestedCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(nestedCall[26..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)1)));
            Assert.That((valueCall[24], valueCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(valueCall[26..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)2)));
            Assert.That(body.GetBlockCode(0).ToArray(),
                Is.EqualTo(new byte[] {
                    0x16, 1, 0x90, 1, 0x90, 2, 0x90, 3, 0x17, 0,
                }));
        }

        static void AssertNestedFniNestedPairSequenceBody(
            TrRmRegisterBodyReader body, byte methodTokenKind)
        {
            Assert.That(body.BlockCount, Is.EqualTo(1));
            Assert.That(body.RecordCount, Is.EqualTo(5));
            Assert.That(body.MethodCount, Is.EqualTo(4));
            Assert.That(body.ConstantCount, Is.EqualTo(1));
            Assert.That(body.ConstantMap.Length, Is.EqualTo(8));
            for (var methodIndex = 0; methodIndex < body.MethodCount;
                methodIndex++)
            {
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                    body.MethodMap[(methodIndex * sizeof(uint))..]) >> 24,
                    Is.EqualTo((uint)methodTokenKind));
            }

            var prefixCall = body.GetRecord(1);
            var secondSourceCall = body.GetRecord(2);
            var firstSourceCall = body.GetRecord(3);
            var valueCall = body.GetRecord(4);
            Assert.That(prefixCall[5], Is.Zero);
            Assert.That(secondSourceCall[5], Is.EqualTo((byte)1));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                secondSourceCall[10..]), Is.EqualTo((ushort)2));
            Assert.That(firstSourceCall[5], Is.EqualTo((byte)1));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                firstSourceCall[10..]), Is.EqualTo((ushort)3));
            Assert.That(valueCall[5], Is.EqualTo((byte)1));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                valueCall[10..]), Is.Zero);
            Assert.That((prefixCall[20], prefixCall[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(prefixCall[22..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)0)));
            Assert.That((secondSourceCall[20], secondSourceCall[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        secondSourceCall[22..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)0)));
            Assert.That((firstSourceCall[20], firstSourceCall[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        firstSourceCall[22..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)0)));
            Assert.That((valueCall[20], valueCall[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(valueCall[22..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)0)));
            Assert.That((prefixCall[24], prefixCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(prefixCall[26..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)1)));
            Assert.That((secondSourceCall[24], secondSourceCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        secondSourceCall[26..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)2)));
            Assert.That((firstSourceCall[24], firstSourceCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        firstSourceCall[26..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)1)));
            Assert.That((valueCall[24], valueCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(valueCall[26..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)3)));
            Assert.That((valueCall[28], valueCall[29],
                    BinaryPrimitives.ReadUInt16LittleEndian(valueCall[30..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)2)));
            Assert.That(body.GetBlockCode(0).ToArray(),
                Is.EqualTo(new byte[] {
                    0x16, 1, 0x90, 1, 0x90, 2, 0x90, 3,
                    0x90, 4, 0x17, 0,
                }));
        }

        static void AssertNestedFniTerminalSequenceBody(
            TrRmRegisterBodyReader body, byte methodTokenKind)
        {
            Assert.That(body.BlockCount, Is.EqualTo(1));
            Assert.That(body.RecordCount, Is.EqualTo(4));
            Assert.That(body.MethodCount, Is.EqualTo(3));
            Assert.That(body.ConstantCount, Is.EqualTo(1));
            Assert.That(body.ConstantMap.Length, Is.EqualTo(8));
            for (var methodIndex = 0; methodIndex < body.MethodCount;
                methodIndex++)
            {
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                    body.MethodMap[(methodIndex * sizeof(uint))..]) >> 24,
                    Is.EqualTo((uint)methodTokenKind));
            }

            var prefixCall = body.GetRecord(1);
            var nestedCall = body.GetRecord(2);
            var terminalCall = body.GetRecord(3);
            Assert.That(prefixCall[5], Is.Zero);
            Assert.That(nestedCall[5], Is.EqualTo((byte)1));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                nestedCall[10..]), Is.EqualTo((ushort)2));
            Assert.That(terminalCall[5], Is.Zero);
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                terminalCall[10..]), Is.Zero);
            Assert.That((prefixCall[20], prefixCall[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(prefixCall[22..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)0)));
            Assert.That((nestedCall[20], nestedCall[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(nestedCall[22..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)0)));
            Assert.That((terminalCall[20], terminalCall[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        terminalCall[22..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)0)));
            Assert.That((prefixCall[24], prefixCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(prefixCall[26..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)1)));
            Assert.That((nestedCall[24], nestedCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(nestedCall[26..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)1)));
            Assert.That((terminalCall[24], terminalCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        terminalCall[26..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)2)));
            Assert.That(body.GetBlockCode(0).ToArray(),
                Is.EqualTo(new byte[] {
                    0x16, 1, 0x90, 1, 0x90, 2, 0x90, 3,
                }));
        }

        static void AssertNestedFniTerminalCallSequenceBody(
            TrRmRegisterBodyReader body, byte methodTokenKind)
        {
            Assert.That(body.BlockCount, Is.EqualTo(1));
            Assert.That(body.RecordCount, Is.EqualTo(3));
            Assert.That(body.MethodCount, Is.EqualTo(2));
            Assert.That(body.ConstantCount, Is.EqualTo(1));
            Assert.That(body.ConstantMap.Length, Is.EqualTo(8));
            for (var methodIndex = 0; methodIndex < body.MethodCount;
                methodIndex++)
            {
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                    body.MethodMap[(methodIndex * sizeof(uint))..]) >> 24,
                    Is.EqualTo((uint)methodTokenKind));
            }

            var prefixCall = body.GetRecord(1);
            var terminalCall = body.GetRecord(2);
            Assert.That(prefixCall[5], Is.Zero);
            Assert.That(terminalCall[5], Is.Zero);
            Assert.That((prefixCall[20], prefixCall[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(prefixCall[22..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)0)));
            Assert.That((terminalCall[20], terminalCall[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(terminalCall[22..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)0)));
            Assert.That((prefixCall[24], prefixCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(prefixCall[26..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)1)));
            Assert.That((terminalCall[24], terminalCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(terminalCall[26..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)1)));
            Assert.That(body.GetBlockCode(0).ToArray(),
                Is.EqualTo(new byte[] {
                    0x16, 1, 0x90, 1, 0x90, 2,
                }));
        }

        static void AssertLocalHelperFniSequenceBody(
            TrRmRegisterBodyReader body, byte foreignMethodTokenKind)
        {
            Assert.That(body.BlockCount, Is.EqualTo(1));
            Assert.That(body.RecordCount, Is.EqualTo(3));
            Assert.That(body.MethodCount, Is.EqualTo(2));
            Assert.That(body.ConstantCount, Is.Zero);
            Assert.That(body.ConstantMap.Length, Is.Zero);

            var helperCall = body.GetRecord(1);
            var foreignCall = body.GetRecord(2);
            var helperMethodMapIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(helperCall[8..]);
            var foreignMethodMapIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(foreignCall[8..]);
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                body.MethodMap[(helperMethodMapIndex * sizeof(uint))..])
                    >> 24,
                Is.EqualTo(0x16u));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                body.MethodMap[(foreignMethodMapIndex * sizeof(uint))..])
                    >> 24,
                Is.EqualTo((uint)foreignMethodTokenKind));
            Assert.That((helperCall[4], helperCall[5],
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        helperCall[12..])),
                Is.EqualTo(((byte)1, (byte)0, 1u)));
            Assert.That((helperCall[20], helperCall[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(helperCall[22..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)0)));
            Assert.That((foreignCall[5],
                    BinaryPrimitives.ReadUInt16LittleEndian(foreignCall[10..])),
                Is.EqualTo(((byte)1, (ushort)0)));
            Assert.That((foreignCall[20], foreignCall[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(foreignCall[22..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)0)));
            Assert.That((foreignCall[24], foreignCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(foreignCall[26..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)1)));
            Assert.That(body.GetBlockCode(0).ToArray(),
                Is.EqualTo(new byte[] {
                    0x90, 1, 0x90, 2, 0x17, 0,
                }));
        }

        static void AssertLocalValueHelperFniSequenceBody(
            TrRmRegisterBodyReader body, byte foreignMethodTokenKind)
        {
            Assert.That(body.BlockCount, Is.EqualTo(1));
            Assert.That(body.RecordCount, Is.EqualTo(4));
            Assert.That(body.MethodCount, Is.EqualTo(3));
            Assert.That(body.ConstantCount, Is.EqualTo(1));
            Assert.That(body.ConstantMap.Length, Is.EqualTo(8));

            var prefixCall = body.GetRecord(1);
            var helperCall = body.GetRecord(2);
            var outerCall = body.GetRecord(3);
            var prefixMethodMapIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(prefixCall[8..]);
            var helperMethodMapIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(helperCall[8..]);
            var outerMethodMapIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(outerCall[8..]);
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                body.MethodMap[(prefixMethodMapIndex * sizeof(uint))..])
                    >> 24,
                Is.EqualTo((uint)foreignMethodTokenKind));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                body.MethodMap[(helperMethodMapIndex * sizeof(uint))..])
                    >> 24,
                Is.EqualTo(0x16u));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                body.MethodMap[(outerMethodMapIndex * sizeof(uint))..])
                    >> 24,
                Is.EqualTo((uint)foreignMethodTokenKind));
            Assert.That((prefixCall[5], helperCall[5],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        helperCall[10..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        helperCall[12..])),
                Is.EqualTo(((byte)0, (byte)1, (ushort)3, 3u)));
            Assert.That((helperCall[20], helperCall[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        helperCall[22..]),
                    helperCall[24], helperCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        helperCall[26..]),
                    helperCall[28], helperCall[29],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        helperCall[30..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)0,
                    (byte)2, (byte)1, (ushort)1,
                    (byte)1, (byte)1, (ushort)2)));
            Assert.That((outerCall[5],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        outerCall[10..]),
                    outerCall[20], outerCall[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        outerCall[22..]),
                    outerCall[24], outerCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        outerCall[26..])),
                Is.EqualTo(((byte)1, (ushort)0,
                    (byte)2, (byte)1, (ushort)0,
                    (byte)1, (byte)1, (ushort)3)));
            Assert.That(body.GetBlockCode(0).ToArray(),
                Is.EqualTo(new byte[] {
                    0x16, 1, 0x16, 0x12,
                    0x90, 1, 0x90, 2, 0x90, 3, 0x17, 0,
                }));
        }

        static void AssertNestedFniLocalValueHelperBody(
            TrRmRegisterBodyReader body, byte foreignMethodTokenKind)
        {
            Assert.That(body.BlockCount, Is.EqualTo(1));
            Assert.That(body.RecordCount, Is.EqualTo(5));
            Assert.That(body.MethodCount, Is.EqualTo(4));
            Assert.That(body.ConstantCount, Is.EqualTo(1));
            Assert.That(body.ConstantMap.Length, Is.EqualTo(8));

            var prefixCall = body.GetRecord(1);
            var nestedCall = body.GetRecord(2);
            var helperCall = body.GetRecord(3);
            var outerCall = body.GetRecord(4);
            foreach (var call in new[] {
                        prefixCall, nestedCall, outerCall,
                    })
            {
                var methodMapIndex
                    = BinaryPrimitives.ReadUInt16LittleEndian(call[8..]);
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                    body.MethodMap[(methodMapIndex * sizeof(uint))..])
                        >> 24,
                    Is.EqualTo((uint)foreignMethodTokenKind));
            }

            var helperMethodMapIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(helperCall[8..]);
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                body.MethodMap[(helperMethodMapIndex * sizeof(uint))..])
                    >> 24,
                Is.EqualTo(0x16u));
            Assert.That((nestedCall[5],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        nestedCall[10..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        nestedCall[12..])),
                Is.EqualTo(((byte)1, (ushort)2, 2u)));
            Assert.That((helperCall[5],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        helperCall[10..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        helperCall[12..])),
                Is.EqualTo(((byte)1, (ushort)3, 2u)));
            Assert.That((outerCall[5],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        outerCall[10..])),
                Is.EqualTo(((byte)1, (ushort)0)));
            Assert.That((prefixCall[20], prefixCall[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        prefixCall[22..]),
                    prefixCall[24], prefixCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        prefixCall[26..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)0,
                    (byte)1, (byte)1, (ushort)1)));
            Assert.That((nestedCall[20], nestedCall[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        nestedCall[22..]),
                    nestedCall[24], nestedCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        nestedCall[26..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)0,
                    (byte)2, (byte)1, (ushort)1)));
            Assert.That((helperCall[20], helperCall[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        helperCall[22..]),
                    helperCall[24], helperCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        helperCall[26..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)0,
                    (byte)1, (byte)1, (ushort)2)));
            Assert.That((outerCall[20], outerCall[21],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        outerCall[22..]),
                    outerCall[24], outerCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        outerCall[26..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)0,
                    (byte)1, (byte)1, (ushort)3)));
            Assert.That(body.GetBlockCode(0).ToArray(),
                Is.EqualTo(new byte[] {
                    0x16, 1, 0x90, 1, 0x90, 2, 0x90, 3,
                    0x90, 4, 0x17, 0,
                }));
        }

        static void AssertNestedFniMixedConditionalOutputBody(
            TrRmRegisterBodyReader body, byte methodTokenKind)
        {
            Assert.That(body.BlockCount, Is.EqualTo(3));
            Assert.That(body.RecordCount, Is.EqualTo(6));
            Assert.That(body.MethodCount, Is.EqualTo(3));
            Assert.That(body.ConstantCount, Is.EqualTo(1));
            Assert.That(body.ConstantMap.Length, Is.EqualTo(8));
            for (var methodIndex = 0; methodIndex < body.MethodCount;
                methodIndex++)
            {
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                    body.MethodMap[(methodIndex * sizeof(uint))..]) >> 24,
                    Is.EqualTo((uint)methodTokenKind));
            }

            var firstNestedCall = body.GetRecord(1);
            var secondNestedCall = body.GetRecord(2);
            var outerCall = body.GetRecord(3);
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                firstNestedCall[10..]), Is.EqualTo((ushort)1));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                secondNestedCall[10..]), Is.EqualTo((ushort)2));
            Assert.That((firstNestedCall[24], firstNestedCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        firstNestedCall[26..])),
                Is.EqualTo(((byte)2, (byte)1, (ushort)2)));
            Assert.That((firstNestedCall[28], firstNestedCall[29],
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        firstNestedCall[30..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)3)));
            Assert.That(outerCall[5], Is.EqualTo((byte)1));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                outerCall[10..]), Is.Zero);
            Assert.That((outerCall[24], outerCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(outerCall[26..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)2)));
            Assert.That((outerCall[28], outerCall[29],
                    BinaryPrimitives.ReadUInt16LittleEndian(outerCall[30..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)1)));
            Assert.That((outerCall[32], outerCall[33],
                    BinaryPrimitives.ReadUInt16LittleEndian(outerCall[34..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)3)));
            Assert.That((outerCall[36], outerCall[37],
                    BinaryPrimitives.ReadUInt16LittleEndian(outerCall[38..])),
                Is.EqualTo(((byte)2, (byte)6, (ushort)4)));
            var reservation = body.GetRecord(4);
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                reservation[4..]), Is.EqualTo((ushort)3));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                reservation[6..]), Is.EqualTo((ushort)4));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                reservation[8..]), Is.EqualTo(4u));
            Assert.That(body.GetBlockCode(0)[..12].ToArray(),
                Is.EqualTo(new byte[] {
                    0x16, 3, 0x90, 1, 0x90, 2,
                    0xDF, 0, 4, 0, 0x90, 3,
                }));
        }

        static void AssertNestedFniConditionalOutputBody(
            TrRmRegisterBodyReader body, byte methodTokenKind)
        {
            Assert.That(body.BlockCount, Is.EqualTo(3));
            Assert.That(body.RecordCount, Is.EqualTo(6));
            Assert.That(body.MethodCount, Is.EqualTo(3));
            for (var methodIndex = 0; methodIndex < body.MethodCount;
                methodIndex++)
            {
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                    body.MethodMap[(methodIndex * sizeof(uint))..]) >> 24,
                    Is.EqualTo((uint)methodTokenKind));
            }

            var firstNestedCall = body.GetRecord(1);
            var secondNestedCall = body.GetRecord(2);
            var outerCall = body.GetRecord(3);
            Assert.That(firstNestedCall[0], Is.EqualTo((byte)0x02));
            Assert.That(secondNestedCall[0], Is.EqualTo((byte)0x02));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                firstNestedCall[10..]), Is.EqualTo((ushort)1));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                secondNestedCall[10..]), Is.EqualTo((ushort)2));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                outerCall[10..]), Is.Zero);
            var nestedMethodIndexes = new[] {
                BinaryPrimitives.ReadUInt16LittleEndian(
                    firstNestedCall[8..]),
                BinaryPrimitives.ReadUInt16LittleEndian(
                    secondNestedCall[8..]),
                BinaryPrimitives.ReadUInt16LittleEndian(outerCall[8..]),
            };
            Assert.That(nestedMethodIndexes.Distinct().Count(),
                Is.EqualTo(3));
            Assert.That((outerCall[24], outerCall[25],
                    BinaryPrimitives.ReadUInt16LittleEndian(outerCall[26..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)2)));
            Assert.That((outerCall[28], outerCall[29],
                    BinaryPrimitives.ReadUInt16LittleEndian(outerCall[30..])),
                Is.EqualTo(((byte)1, (byte)1, (ushort)1)));
            Assert.That((outerCall[32], outerCall[33],
                    BinaryPrimitives.ReadUInt16LittleEndian(outerCall[34..])),
                Is.EqualTo(((byte)2, (byte)6, (ushort)4)));
            var reservation = body.GetRecord(4);
            Assert.That(reservation[0], Is.EqualTo((byte)0x0C));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                reservation[4..]), Is.EqualTo((ushort)3));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                reservation[6..]), Is.EqualTo((ushort)4));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                reservation[8..]), Is.EqualTo(3u));
            Assert.That(body.GetRecord(5)[0], Is.EqualTo((byte)0x06));
            Assert.That(body.GetBlockCode(0)[..10].ToArray(),
                Is.EqualTo(new byte[] {
                    0x90, 1, 0x90, 2, 0xDF, 0, 4, 0, 0x90, 3,
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

        static void AssertNestedFniDirectPairCall(BoundProgram program,
            string functionName, string outerMemberName,
            PrimitiveType expectedResultType, string? expectedProvider)
        {
            var invoke = program.Functions.Single(function =>
                function.Name == functionName);
            var members = program.Interfaces.Single().Members;
            string GetMemberName(BoundCall call)
                => members.Single(member => member.Ordinal
                    == call.ForeignInterfaceCall!.Member.MemberOrdinal).Name;

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
            Assert.That(calls, Has.Count.EqualTo(3));
            var outerCall = calls.Single(call =>
                GetMemberName(call) == outerMemberName);
            BoundCall? UnwrapCall(BoundExpression expression)
            {
                while (expression is BoundUnsafeExpression unsafeCall)
                {
                    expression = unsafeCall.Expression;
                }

                return expression as BoundCall;
            }

            var nestedCalls = new[] {
                UnwrapCall(outerCall.Arguments[0]),
                UnwrapCall(outerCall.Arguments[1]),
            };
            Assert.That(nestedCalls.All(static call => call is not null),
                Is.True);
            Assert.That(nestedCalls.Select(call => GetMemberName(call!)),
                Is.EqualTo(new[] { "GetOtherIndex", "GetIndex" }));
            Assert.That(new[] { 0, 1 }.Select(outerCall.ParameterIndex),
                Is.EqualTo(new[] { 1, 0 }));
            Assert.That(outerCall.Type, Is.EqualTo(expectedResultType));
            Assert.That(outerCall.IsUnsafeAuthorized, Is.True);

            var expectedSourceVariables = new[] { 2, 1 };
            for (var index = 0; index < nestedCalls.Length; index++)
            {
                var nestedCall = nestedCalls[index]!;
                Assert.That(nestedCall.IsUnsafeAuthorized, Is.True);
                Assert.That(nestedCall.Type,
                    Is.EqualTo(PrimitiveType.Int32));
                Assert.That(((BoundRead)nestedCall.Arguments.Single())
                    .Variable, Is.EqualTo(expectedSourceVariables[index]));
                Assert.That(nestedCall.ForeignInterfaceCall!
                    .Member.DeclaringInterface.ModuleImport?.ProducerIdentity,
                    Is.EqualTo(expectedProvider));
            }

            Assert.That(outerCall.ForeignInterfaceCall!.Member
                .DeclaringInterface.ModuleImport?.ProducerIdentity,
                Is.EqualTo(expectedProvider));
        }

        static void AssertNestedFniStagedPairCall(
            BoundProgram program, string? expectedProvider)
        {
            var invoke = program.Functions.Single(function =>
                function.Name == "InvokeNestedValuePairWithStagedInput");
            var members = program.Interfaces.Single().Members;
            string GetMemberName(BoundCall call)
                => members.Single(member => member.Ordinal
                    == call.ForeignInterfaceCall!.Member.MemberOrdinal).Name;

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
            Assert.That(calls, Has.Count.EqualTo(3));
            var outerCall = calls.Single(call =>
                GetMemberName(call) == "UseTriple");
            BoundCall? UnwrapCall(BoundExpression expression)
            {
                while (expression is BoundUnsafeExpression unsafeCall)
                {
                    expression = unsafeCall.Expression;
                }

                return expression as BoundCall;
            }

            var nestedCalls = new[] {
                UnwrapCall(outerCall.Arguments[0]),
                UnwrapCall(outerCall.Arguments[1]),
            };
            Assert.That(nestedCalls.All(static call => call is not null),
                Is.True);
            Assert.That(nestedCalls.Select(call => GetMemberName(call!)),
                Is.EqualTo(new[] { "GetOtherIndex", "GetIndex" }));
            Assert.That(new[] { 0, 1, 2 }.Select(outerCall.ParameterIndex),
                Is.EqualTo(new[] { 1, 0, 2 }));
            Assert.That(outerCall.Arguments[2], Is.TypeOf<BoundRead>());
            Assert.That(((BoundRead)outerCall.Arguments[2]).Variable,
                Is.EqualTo(4));
            Assert.That(invoke.Variables[4].Type,
                Is.EqualTo(PrimitiveType.Int32));
            var stagedLocal = invoke.Body.Statements.OfType<BoundLocal>()
                .Single(local => local.Variable == 4);
            Assert.That(((BoundRead)stagedLocal.Initializer).Variable,
                Is.EqualTo(3));

            var expectedSourceVariables = new[] { 2, 1 };
            for (var index = 0; index < nestedCalls.Length; index++)
            {
                var nestedCall = nestedCalls[index]!;
                Assert.That(nestedCall.IsUnsafeAuthorized, Is.True);
                Assert.That(nestedCall.Type,
                    Is.EqualTo(PrimitiveType.Int32));
                Assert.That(((BoundRead)nestedCall.Arguments.Single())
                    .Variable, Is.EqualTo(expectedSourceVariables[index]));
                Assert.That(nestedCall.ForeignInterfaceCall!
                    .Member.DeclaringInterface.ModuleImport?.ProducerIdentity,
                    Is.EqualTo(expectedProvider));
            }

            Assert.That(outerCall.ForeignInterfaceCall!.Member
                .DeclaringInterface.ModuleImport?.ProducerIdentity,
                Is.EqualTo(expectedProvider));
        }

        static void AssertDirectFniConstantCall(BoundProgram program,
            string? expectedProvider)
        {
            var invoke = program.Functions.Single(function =>
                function.Name == "InvokeConstant");
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
            Assert.That(selectedCall.IsUnsafeAuthorized, Is.True);
            Assert.That(selectedCall.Arguments, Has.Length.EqualTo(1));
            Assert.That(selectedCall.Arguments[0], Is.TypeOf<BoundConstant>());
            var constant = (BoundConstant)selectedCall.Arguments[0];
            Assert.That(constant.Type, Is.EqualTo(PrimitiveType.Int32));
            Assert.That(constant.Value, Is.EqualTo(42L));
            Assert.That(selectedCall.ForeignInterfaceCall!.Member
                .DeclaringInterface.ModuleImport?.ProducerIdentity,
                Is.EqualTo(expectedProvider));
        }

        static void AssertNestedFniDirectConstantNestedCall(
            BoundProgram program, string? expectedProvider)
        {
            var invoke = program.Functions.Single(function =>
                function.Name == "InvokeNestedConstantArgument");
            var members = program.Interfaces.Single().Members;
            string GetMemberName(BoundCall call)
                => members.Single(member => member.Ordinal
                    == call.ForeignInterfaceCall!.Member.MemberOrdinal).Name;

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
            var outerCall = calls.Single(call =>
                GetMemberName(call) == "UseIndex");
            BoundCall? UnwrapCall(BoundExpression expression)
            {
                while (expression is BoundUnsafeExpression unsafeCall)
                {
                    expression = unsafeCall.Expression;
                }

                return expression as BoundCall;
            }

            var nestedCall = UnwrapCall(outerCall.Arguments.Single())
                ?? throw new AssertionException(
                    "Expected the nested FNI input call.");
            Assert.That(GetMemberName(nestedCall),
                Is.EqualTo("GetIndexWithBias"));
            Assert.That(outerCall.IsUnsafeAuthorized, Is.True);
            Assert.That(nestedCall.IsUnsafeAuthorized, Is.True);
            Assert.That(outerCall.ParameterIndex(0), Is.Zero);
            Assert.That(((BoundRead)nestedCall.Arguments[0]).Variable,
                Is.EqualTo(1));
            Assert.That(nestedCall.Arguments[1], Is.TypeOf<BoundConstant>());
            Assert.That(((BoundConstant)nestedCall.Arguments[1]).Value,
                Is.EqualTo(42L));
            Assert.That(nestedCall.ParameterIndex(0), Is.Zero);
            Assert.That(nestedCall.ParameterIndex(1), Is.EqualTo(1));
            Assert.That(outerCall.ForeignInterfaceCall!.Member
                .DeclaringInterface.ModuleImport?.ProducerIdentity,
                Is.EqualTo(expectedProvider));
            Assert.That(nestedCall.ForeignInterfaceCall!.Member
                .DeclaringInterface.ModuleImport?.ProducerIdentity,
                Is.EqualTo(expectedProvider));
        }

        static void AssertNestedFniTerminalCall(BoundProgram program,
            string? expectedProvider)
        {
            var invoke = program.Functions.Single(function =>
                function.Name == "InvokeNestedTerminal");
            Assert.That(invoke.DoesNotReturn, Is.True);
            Assert.That(invoke.ReturnType, Is.EqualTo(PrimitiveType.Void));
            var members = program.Interfaces.Single().Members;
            string GetMemberName(BoundCall call)
                => members.Single(member => member.Ordinal
                    == call.ForeignInterfaceCall!.Member.MemberOrdinal).Name;

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
            var outerCall = calls.Single(call =>
                GetMemberName(call) == "StopWithIndex");
            var nestedCall = calls.Single(call =>
                GetMemberName(call) == "GetIndexWithBias");
            Assert.That(outerCall.ForeignInterfaceCall!.DoesNotReturn,
                Is.True);
            Assert.That(outerCall.IsUnsafeAuthorized, Is.True);
            Assert.That(nestedCall.IsUnsafeAuthorized, Is.True);
            Assert.That(((BoundRead)nestedCall.Arguments[0]).Variable,
                Is.EqualTo(1));
            Assert.That(((BoundConstant)nestedCall.Arguments[1]).Value,
                Is.EqualTo(42L));
            Assert.That(nestedCall.ForeignInterfaceCall!
                .Member.DeclaringInterface.ModuleImport?.ProducerIdentity,
                Is.EqualTo(expectedProvider));
            Assert.That(outerCall.ForeignInterfaceCall!.Member
                .DeclaringInterface.ModuleImport?.ProducerIdentity,
                Is.EqualTo(expectedProvider));
        }

        static void AssertNestedFniVoidCallSequence(
            BoundProgram program, string? expectedProvider)
        {
            var invoke = program.Functions.Single(function =>
                function.Name == "InvokeSequence");
            var members = program.Interfaces.Single().Members;
            string GetMemberName(BoundCall call)
                => members.Single(member => member.Ordinal
                    == call.ForeignInterfaceCall!.Member.MemberOrdinal).Name;

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
            Assert.That(calls.Select(GetMemberName),
                Is.EqualTo(new[] { "Touch", "TouchValue" }));
            Assert.That(calls.All(static call =>
                    call.Type == PrimitiveType.Void
                    && call.IsUnsafeAuthorized
                    && call.ForeignInterfaceCall!
                        .NativeOutputContracts.IsEmpty),
                Is.True);
            Assert.That(((BoundRead)calls[0].Arguments.Single()).Variable,
                Is.EqualTo(1));
            Assert.That(calls[1].Arguments.Single(),
                Is.TypeOf<BoundConstant>());
            Assert.That(((BoundConstant)calls[1].Arguments.Single()).Value,
                Is.EqualTo(42L));
            foreach (var call in calls)
            {
                Assert.That(call.ForeignInterfaceCall!.Member
                    .DeclaringInterface.ModuleImport?.ProducerIdentity,
                    Is.EqualTo(expectedProvider));
            }
        }

        static void AssertNestedFniValueCallSequence(
            BoundProgram program, string? expectedProvider)
        {
            var invoke = program.Functions.Single(function =>
                function.Name == "InvokeValueSequence");
            var members = program.Interfaces.Single().Members;
            string GetMemberName(BoundCall call)
                => members.Single(member => member.Ordinal
                    == call.ForeignInterfaceCall!.Member.MemberOrdinal).Name;

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
            Assert.That(calls.Select(GetMemberName),
                Is.EqualTo(new[] { "TouchValue", "UseIndexWithDefault" }));
            Assert.That(calls.Select(static call => call.Type),
                Is.EqualTo(new[] {
                    PrimitiveType.Void, PrimitiveType.Int32,
                }));
            Assert.That(calls.All(static call =>
                    call.IsUnsafeAuthorized
                    && call.ForeignInterfaceCall!
                        .NativeOutputContracts.IsEmpty
                    && call.ForeignInterfaceCall.Receiver is BoundRead {
                        Variable: 0,
                        Type: PrimitiveType.NativeRawPointer,
                    }),
                Is.True);
            Assert.That(calls[0].Arguments.Single(),
                Is.TypeOf<BoundConstant>());
            Assert.That(((BoundConstant)calls[0].Arguments.Single()).Value,
                Is.EqualTo(42L));
            Assert.That(calls[1].Arguments, Has.Count.EqualTo(2));
            Assert.That(calls[1].Arguments[0],
                Is.TypeOf<BoundRead>());
            Assert.That(((BoundRead)calls[1].Arguments[0]).Variable,
                Is.EqualTo(1));
            Assert.That(calls[1].Arguments[1],
                Is.TypeOf<BoundConstant>());
            Assert.That(((BoundConstant)calls[1].Arguments[1]).Value,
                Is.EqualTo(7L));
            Assert.That(calls[1].ArgumentParameterIndices,
                Is.EqualTo([0, 1]));
            Assert.That(calls[1].ForeignInterfaceCall!.Member.Parameters[1]
                .DefaultValue?.Value, Is.EqualTo(7L));
            foreach (var call in calls)
            {
                Assert.That(call.ForeignInterfaceCall!.Member
                    .DeclaringInterface.ModuleImport?.ProducerIdentity,
                    Is.EqualTo(expectedProvider));
            }
        }

        static void AssertNestedFniNestedValueSequence(
            BoundProgram program, string? expectedProvider)
        {
            var invoke = program.Functions.Single(function =>
                function.Name == "InvokeNestedValueSequence");
            var members = program.Interfaces.Single().Members;
            string GetMemberName(BoundCall call)
                => members.Single(member => member.Ordinal
                    == call.ForeignInterfaceCall!.Member.MemberOrdinal).Name;

            static BoundCall? UnwrapCall(BoundExpression expression)
            {
                while (expression is BoundUnsafeExpression unsafeCall)
                {
                    expression = unsafeCall.Expression;
                }

                return expression is BoundCall {
                    ForeignInterfaceCall: not null,
                } call ? call : null;
            }

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
            Assert.That(calls, Has.Count.EqualTo(3));
            var prefixCall = calls.Single(call =>
                GetMemberName(call) == "TouchValue");
            var nestedCall = calls.Single(call =>
                GetMemberName(call) == "GetIndex");
            var valueCall = calls.Single(call =>
                GetMemberName(call) == "UseIndex");
            Assert.That(prefixCall.Type, Is.EqualTo(PrimitiveType.Void));
            Assert.That(nestedCall.Type, Is.EqualTo(PrimitiveType.Int32));
            Assert.That(valueCall.Type, Is.EqualTo(PrimitiveType.Int32));
            Assert.That(new[] { prefixCall, nestedCall, valueCall }.All(
                static call => call.IsUnsafeAuthorized
                    && call.ForeignInterfaceCall!
                        .NativeOutputContracts.IsEmpty
                    && call.ForeignInterfaceCall.Receiver is BoundRead {
                        Variable: 0,
                        Type: PrimitiveType.NativeRawPointer,
                    }), Is.True);
            Assert.That(prefixCall.Arguments.Single(),
                Is.TypeOf<BoundConstant>());
            Assert.That(((BoundConstant)prefixCall.Arguments.Single()).Value,
                Is.EqualTo(42L));
            Assert.That(UnwrapCall(valueCall.Arguments.Single()),
                Is.SameAs(nestedCall));
            Assert.That(((BoundRead)nestedCall.Arguments.Single()).Variable,
                Is.EqualTo(1));
            foreach (var call in new[] {
                        prefixCall, nestedCall, valueCall,
                    })
            {
                Assert.That(call.ForeignInterfaceCall!.Member
                    .DeclaringInterface.ModuleImport?.ProducerIdentity,
                    Is.EqualTo(expectedProvider));
            }
        }

        static void AssertNestedFniNestedPairSequence(
            BoundProgram program, string? expectedProvider)
        {
            var invoke = program.Functions.Single(function =>
                function.Name == "InvokeNestedPairSequence");
            var members = program.Interfaces.Single().Members;
            string GetMemberName(BoundCall call)
                => members.Single(member => member.Ordinal
                    == call.ForeignInterfaceCall!.Member.MemberOrdinal).Name;

            static BoundCall? UnwrapCall(BoundExpression expression)
            {
                while (expression is BoundUnsafeExpression unsafeCall)
                {
                    expression = unsafeCall.Expression;
                }

                return expression is BoundCall {
                    ForeignInterfaceCall: not null,
                } call ? call : null;
            }

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
            Assert.That(calls, Has.Count.EqualTo(4));
            var prefixCall = calls.Single(call =>
                GetMemberName(call) == "TouchValue");
            var secondSourceCall = calls.Single(call =>
                GetMemberName(call) == "GetOtherIndex");
            var firstSourceCall = calls.Single(call =>
                GetMemberName(call) == "GetIndex");
            var valueCall = calls.Single(call =>
                GetMemberName(call) == "UsePair");
            Assert.That(new[] {
                    prefixCall, secondSourceCall, firstSourceCall, valueCall,
                }.All(static call => call.IsUnsafeAuthorized
                    && call.ForeignInterfaceCall!
                        .NativeOutputContracts.IsEmpty
                    && call.ForeignInterfaceCall.Receiver is BoundRead {
                        Variable: 0,
                        Type: PrimitiveType.NativeRawPointer,
                    }), Is.True);
            Assert.That(((BoundConstant)prefixCall.Arguments.Single()).Value,
                Is.EqualTo(42L));
            Assert.That(valueCall.ArgumentParameterIndices,
                Is.EqualTo([1, 0]));
            Assert.That(valueCall.Arguments, Has.Count.EqualTo(2));
            Assert.That(UnwrapCall(valueCall.Arguments[0]),
                Is.SameAs(secondSourceCall));
            Assert.That(UnwrapCall(valueCall.Arguments[1]),
                Is.SameAs(firstSourceCall));
            Assert.That(((BoundRead)secondSourceCall.Arguments.Single())
                .Variable, Is.EqualTo(2));
            Assert.That(((BoundRead)firstSourceCall.Arguments.Single())
                .Variable, Is.EqualTo(1));
            foreach (var call in new[] {
                        prefixCall, secondSourceCall, firstSourceCall,
                        valueCall,
                    })
            {
                Assert.That(call.ForeignInterfaceCall!.Member
                    .DeclaringInterface.ModuleImport?.ProducerIdentity,
                    Is.EqualTo(expectedProvider));
            }
        }

        static void AssertNestedFniTerminalSequence(
            BoundProgram program, string? expectedProvider)
        {
            var invoke = program.Functions.Single(function =>
                function.Name == "InvokeNestedTerminalSequence");
            var members = program.Interfaces.Single().Members;
            string GetMemberName(BoundCall call)
                => members.Single(member => member.Ordinal
                    == call.ForeignInterfaceCall!.Member.MemberOrdinal).Name;

            static BoundCall? UnwrapCall(BoundExpression expression)
            {
                while (expression is BoundUnsafeExpression unsafeCall)
                {
                    expression = unsafeCall.Expression;
                }

                return expression is BoundCall {
                    ForeignInterfaceCall: not null,
                } call ? call : null;
            }

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
            Assert.That(calls, Has.Count.EqualTo(3));
            var prefixCall = calls.Single(call =>
                GetMemberName(call) == "TouchValue");
            var nestedCall = calls.Single(call =>
                GetMemberName(call) == "GetIndex");
            var terminalCall = calls.Single(call =>
                GetMemberName(call) == "StopWithIndex");
            Assert.That(invoke.DoesNotReturn, Is.True);
            Assert.That(invoke.ReturnType, Is.EqualTo(PrimitiveType.Void));
            Assert.That(prefixCall.Type, Is.EqualTo(PrimitiveType.Void));
            Assert.That(terminalCall.Type, Is.EqualTo(PrimitiveType.Int32));
            Assert.That(terminalCall.ForeignInterfaceCall!.DoesNotReturn,
                Is.True);
            Assert.That(new[] { prefixCall, nestedCall, terminalCall }.All(
                static call => call.IsUnsafeAuthorized
                    && call.ForeignInterfaceCall!
                        .NativeOutputContracts.IsEmpty
                    && call.ForeignInterfaceCall.Receiver is BoundRead {
                        Variable: 0,
                        Type: PrimitiveType.NativeRawPointer,
                    }), Is.True);
            Assert.That(((BoundConstant)prefixCall.Arguments.Single()).Value,
                Is.EqualTo(42L));
            Assert.That(UnwrapCall(terminalCall.Arguments.Single()),
                Is.SameAs(nestedCall));
            Assert.That(((BoundRead)nestedCall.Arguments.Single()).Variable,
                Is.EqualTo(1));
            foreach (var call in new[] {
                        prefixCall, nestedCall, terminalCall,
                    })
            {
                Assert.That(call.ForeignInterfaceCall!.Member
                    .DeclaringInterface.ModuleImport?.ProducerIdentity,
                    Is.EqualTo(expectedProvider));
            }
        }

        static void AssertNestedFniTerminalCallSequence(
            BoundProgram program, string? expectedProvider)
        {
            var invoke = program.Functions.Single(function =>
                function.Name == "InvokeTerminalSequence");
            var members = program.Interfaces.Single().Members;
            string GetMemberName(BoundCall call)
                => members.Single(member => member.Ordinal
                    == call.ForeignInterfaceCall!.Member.MemberOrdinal).Name;

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
            var prefixCall = calls.Single(call =>
                GetMemberName(call) == "TouchValue");
            var terminalCall = calls.Single(call =>
                GetMemberName(call) == "StopWithIndex");
            Assert.That(invoke.DoesNotReturn, Is.True);
            Assert.That(invoke.ReturnType, Is.EqualTo(PrimitiveType.Void));
            Assert.That(prefixCall.Type, Is.EqualTo(PrimitiveType.Void));
            Assert.That(terminalCall.Type, Is.EqualTo(PrimitiveType.Int32));
            Assert.That(terminalCall.ForeignInterfaceCall!.DoesNotReturn,
                Is.True);
            Assert.That(new[] { prefixCall, terminalCall }.All(
                static call => call.IsUnsafeAuthorized
                    && call.ForeignInterfaceCall!
                        .NativeOutputContracts.IsEmpty
                    && call.ForeignInterfaceCall.Receiver is BoundRead {
                        Variable: 0,
                        Type: PrimitiveType.NativeRawPointer,
                    }), Is.True);
            Assert.That(prefixCall.Arguments.Single(),
                Is.TypeOf<BoundConstant>());
            Assert.That(((BoundConstant)prefixCall.Arguments.Single()).Value,
                Is.EqualTo(42L));
            Assert.That(terminalCall.Arguments.Single(),
                Is.TypeOf<BoundConstant>());
            Assert.That(((BoundConstant)terminalCall.Arguments.Single()).Value,
                Is.EqualTo(42L));
            foreach (var call in calls)
            {
                Assert.That(call.ForeignInterfaceCall!.Member
                    .DeclaringInterface.ModuleImport?.ProducerIdentity,
                    Is.EqualTo(expectedProvider));
            }
        }

        static void AssertLocalHelperFniSequence(
            BoundProgram program, string? expectedProvider)
        {
            var invoke = program.Functions.Single(function =>
                function.Name == "InvokeHelperSequence");
            var helper = program.Functions.Single(function =>
                function.Name == "TouchViaHelper");
            var members = program.Interfaces.Single().Members;
            string GetMemberName(BoundCall call)
                => members.Single(member => member.Ordinal
                    == call.ForeignInterfaceCall!.Member.MemberOrdinal).Name;

            var localCalls = new List<BoundCall>();
            var foreignCalls = new List<BoundCall>();
            Assert.That(BoundTreeTraversal.VisitExpressions(invoke.Body,
                expression =>
                {
                    if (expression is BoundCall call)
                    {
                        if (call.ForeignInterfaceCall is null)
                        {
                            localCalls.Add(call);
                        }
                        else
                        {
                            foreignCalls.Add(call);
                        }
                    }
                }), Is.True);
            Assert.That(localCalls, Has.Count.EqualTo(1));
            Assert.That(foreignCalls, Has.Count.EqualTo(1));
            Assert.That(program.Functions[localCalls[0].Function].Name,
                Is.EqualTo("TouchViaHelper"));
            Assert.That(((BoundRead)localCalls[0].Arguments.Single()).Variable,
                Is.EqualTo(0));
            Assert.That(GetMemberName(foreignCalls.Single()),
                Is.EqualTo("UseIndex"));
            Assert.That(foreignCalls[0].ForeignInterfaceCall!.Receiver,
                Is.TypeOf<BoundRead>());
            Assert.That(((BoundRead)foreignCalls[0].Arguments.Single())
                .Variable, Is.EqualTo(1));
            Assert.That(foreignCalls[0].IsUnsafeAuthorized, Is.True);

            var helperCalls = new List<BoundCall>();
            Assert.That(BoundTreeTraversal.VisitExpressions(helper.Body,
                expression =>
                {
                    if (expression is BoundCall {
                            ForeignInterfaceCall: not null,
                        } call)
                    {
                        helperCalls.Add(call);
                    }
                }), Is.True);
            Assert.That(helperCalls, Has.Count.EqualTo(1));
            Assert.That(GetMemberName(helperCalls.Single()),
                Is.EqualTo("TouchValue"));
            Assert.That(((BoundConstant)helperCalls.Single().Arguments.Single())
                .Value, Is.EqualTo(42L));
            foreach (var call in foreignCalls.Concat(helperCalls))
            {
                Assert.That(call.ForeignInterfaceCall!.Member
                    .DeclaringInterface.ModuleImport?.ProducerIdentity,
                    Is.EqualTo(expectedProvider));
            }
        }

        static void AssertLocalValueHelperFniSequence(
            BoundProgram program, string? expectedProvider)
        {
            var invoke = program.Functions.Single(function =>
                function.Name == "InvokeNestedLocalValueSequence");
            var helper = program.Functions.Single(function =>
                function.Name == "GetIndexViaFni");
            var members = program.Interfaces.Single().Members;
            string GetMemberName(BoundCall call)
                => members.Single(member => member.Ordinal
                    == call.ForeignInterfaceCall!.Member.MemberOrdinal).Name;

            static BoundCall? UnwrapCall(BoundExpression expression)
            {
                while (expression is BoundUnsafeExpression unsafeCall)
                {
                    expression = unsafeCall.Expression;
                }

                return expression as BoundCall;
            }

            var localCalls = new List<BoundCall>();
            var foreignCalls = new List<BoundCall>();
            Assert.That(BoundTreeTraversal.VisitExpressions(invoke.Body,
                expression =>
                {
                    if (expression is BoundCall call)
                    {
                        if (call.ForeignInterfaceCall is null)
                        {
                            localCalls.Add(call);
                        }
                        else
                        {
                            foreignCalls.Add(call);
                        }
                    }
                }), Is.True);
            Assert.That(localCalls, Has.Count.EqualTo(1));
            Assert.That(foreignCalls, Has.Count.EqualTo(2));
            var localCall = localCalls.Single();
            Assert.That(program.Functions[localCall.Function].Name,
                Is.EqualTo("GetIndexViaFni"));
            Assert.That(((BoundRead)localCall.Arguments[0]).Variable,
                Is.EqualTo(0));
            Assert.That(((BoundRead)localCall.Arguments[1]).Variable,
                Is.EqualTo(1));
            Assert.That(((BoundConstant)localCall.Arguments[2]).Value,
                Is.EqualTo(7L));

            var prefixCall = foreignCalls.Single(call =>
                GetMemberName(call) == "TouchValue");
            var outerCall = foreignCalls.Single(call =>
                GetMemberName(call) == "UseIndex");
            Assert.That(((BoundConstant)prefixCall.Arguments.Single()).Value,
                Is.EqualTo(42L));
            Assert.That(UnwrapCall(outerCall.Arguments.Single()),
                Is.SameAs(localCalls.Single()));

            var helperCalls = new List<BoundCall>();
            Assert.That(BoundTreeTraversal.VisitExpressions(helper.Body,
                expression =>
                {
                    if (expression is BoundCall {
                            ForeignInterfaceCall: not null,
                        } call)
                    {
                        helperCalls.Add(call);
                    }
                }), Is.True);
            Assert.That(helperCalls, Has.Count.EqualTo(1));
            Assert.That(GetMemberName(helperCalls.Single()),
                Is.EqualTo("GetIndexWithBias"));
            Assert.That(((BoundRead)helperCalls.Single().Arguments[0])
                .Variable, Is.EqualTo(1));
            Assert.That(((BoundRead)helperCalls.Single().Arguments[1])
                .Variable, Is.EqualTo(2));
            foreach (var call in foreignCalls.Concat(helperCalls))
            {
                Assert.That(call.ForeignInterfaceCall!.Member
                    .DeclaringInterface.ModuleImport?.ProducerIdentity,
                    Is.EqualTo(expectedProvider));
            }
        }

        static void AssertNestedFniLocalValueHelper(
            BoundProgram program, string? expectedProvider)
        {
            var invoke = program.Functions.Single(function =>
                function.Name == "InvokeNestedFniValueViaLocalHelper");
            var helper = program.Functions.Single(function =>
                function.Name == "UseIndexViaFni");
            var members = program.Interfaces.Single().Members;
            string GetMemberName(BoundCall call)
                => members.Single(member => member.Ordinal
                    == call.ForeignInterfaceCall!.Member.MemberOrdinal).Name;

            static BoundCall? UnwrapCall(BoundExpression expression)
            {
                while (expression is BoundUnsafeExpression unsafeCall)
                {
                    expression = unsafeCall.Expression;
                }

                return expression as BoundCall;
            }

            var localCalls = new List<BoundCall>();
            var foreignCalls = new List<BoundCall>();
            Assert.That(BoundTreeTraversal.VisitExpressions(invoke.Body,
                expression =>
                {
                    if (expression is BoundCall call)
                    {
                        if (call.ForeignInterfaceCall is null)
                        {
                            localCalls.Add(call);
                        }
                        else
                        {
                            foreignCalls.Add(call);
                        }
                    }
                }), Is.True);
            Assert.That(localCalls, Has.Count.EqualTo(1));
            Assert.That(foreignCalls, Has.Count.EqualTo(3));
            var localCall = localCalls.Single();
            Assert.That(program.Functions[localCall.Function].Name,
                Is.EqualTo("UseIndexViaFni"));
            Assert.That(((BoundRead)localCall.Arguments[0]).Variable,
                Is.EqualTo(0));
            var nestedCall = UnwrapCall(localCall.Arguments[1]);
            Assert.That(nestedCall, Is.Not.Null);
            Assert.That(GetMemberName(nestedCall!), Is.EqualTo("GetIndex"));
            Assert.That(((BoundRead)nestedCall!.Arguments.Single()).Variable,
                Is.EqualTo(1));

            var prefixCall = foreignCalls.Single(call =>
                GetMemberName(call) == "TouchValue");
            var outerCall = foreignCalls.Single(call =>
                GetMemberName(call) == "UseIndex");
            Assert.That(((BoundConstant)prefixCall.Arguments.Single()).Value,
                Is.EqualTo(42L));
            Assert.That(UnwrapCall(outerCall.Arguments.Single()),
                Is.SameAs(localCall));

            var helperCalls = new List<BoundCall>();
            Assert.That(BoundTreeTraversal.VisitExpressions(helper.Body,
                expression =>
                {
                    if (expression is BoundCall {
                            ForeignInterfaceCall: not null,
                        } call)
                    {
                        helperCalls.Add(call);
                    }
                }), Is.True);
            Assert.That(helperCalls, Has.Count.EqualTo(1));
            Assert.That(GetMemberName(helperCalls.Single()),
                Is.EqualTo("UseIndex"));
            Assert.That(((BoundRead)helperCalls.Single().Arguments.Single())
                .Variable, Is.EqualTo(1));
            foreach (var call in foreignCalls.Concat(helperCalls))
            {
                Assert.That(call.ForeignInterfaceCall!.Member
                    .DeclaringInterface.ModuleImport?.ProducerIdentity,
                    Is.EqualTo(expectedProvider));
            }
        }

        static void AssertNestedFniMixedConstantCall(BoundProgram program,
            string? expectedProvider)
        {
            var invoke = program.Functions.Single(function =>
                function.Name == "InvokeNestedValuePairWithConstant");
            var members = program.Interfaces.Single().Members;
            string GetMemberName(BoundCall call)
                => members.Single(member => member.Ordinal
                    == call.ForeignInterfaceCall!.Member.MemberOrdinal).Name;

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
            Assert.That(calls, Has.Count.EqualTo(3));
            var outerCall = calls.Single(call =>
                GetMemberName(call) == "UseTriple");
            BoundCall? UnwrapCall(BoundExpression expression)
            {
                while (expression is BoundUnsafeExpression unsafeCall)
                {
                    expression = unsafeCall.Expression;
                }

                return expression as BoundCall;
            }

            var nestedCalls = new[] {
                UnwrapCall(outerCall.Arguments[0]),
                UnwrapCall(outerCall.Arguments[1]),
            };
            Assert.That(nestedCalls.All(static call => call is not null),
                Is.True);
            Assert.That(nestedCalls.Select(call => GetMemberName(call!)),
                Is.EqualTo(new[] { "GetOtherIndex", "GetIndex" }));
            Assert.That(new[] { 0, 1, 2 }.Select(outerCall.ParameterIndex),
                Is.EqualTo(new[] { 1, 0, 2 }));
            Assert.That(outerCall.Arguments[2], Is.TypeOf<BoundConstant>());
            var constant = (BoundConstant)outerCall.Arguments[2];
            Assert.That(constant.Type, Is.EqualTo(PrimitiveType.Int32));
            Assert.That(constant.Value, Is.EqualTo(42L));

            for (var index = 0; index < nestedCalls.Length; index++)
            {
                var nestedCall = nestedCalls[index]!;
                Assert.That(nestedCall.IsUnsafeAuthorized, Is.True);
                Assert.That(nestedCall.Type,
                    Is.EqualTo(PrimitiveType.Int32));
                if (index == 0)
                {
                    Assert.That(((BoundRead)nestedCall.Arguments[0])
                        .Variable, Is.EqualTo(2));
                    Assert.That(nestedCall.Arguments[1],
                        Is.TypeOf<BoundConstant>());
                    Assert.That(((BoundConstant)nestedCall.Arguments[1]).Value,
                        Is.EqualTo(42L));
                }
                else
                {
                    Assert.That(((BoundRead)nestedCall.Arguments.Single())
                        .Variable, Is.EqualTo(1));
                }

                Assert.That(nestedCall.ForeignInterfaceCall!
                    .Member.DeclaringInterface.ModuleImport?.ProducerIdentity,
                    Is.EqualTo(expectedProvider));
            }

            Assert.That(outerCall.ForeignInterfaceCall!.Member
                .DeclaringInterface.ModuleImport?.ProducerIdentity,
                Is.EqualTo(expectedProvider));
        }

        static void AssertNestedFniConditionalOutputCall(
            BoundProgram program, string? expectedProvider)
        {
            var invoke = program.Functions.Single(function =>
                function.Name == "InvokeNestedCorrelated");
            var members = program.Interfaces.Single().Members;
            string GetMemberName(BoundCall call)
                => members.Single(member => member.Ordinal
                    == call.ForeignInterfaceCall!.Member.MemberOrdinal).Name;

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
            Assert.That(calls, Has.Count.EqualTo(3));
            var outerCall = calls.Single(call =>
                GetMemberName(call) == "TryReadPair");
            BoundCall? UnwrapCall(BoundExpression expression)
            {
                while (expression is BoundUnsafeExpression unsafeCall)
                {
                    expression = unsafeCall.Expression;
                }

                return expression as BoundCall;
            }

            var nestedCalls = new[] {
                UnwrapCall(outerCall.Arguments[0]),
                UnwrapCall(outerCall.Arguments[1]),
            };
            Assert.That(nestedCalls.All(static call => call is not null),
                Is.True);
            Assert.That(nestedCalls.Select(call => GetMemberName(call!)),
                Is.EqualTo(new[] { "GetOtherIndex", "GetIndex" }));
            Assert.That(new[] { 0, 1, 2 }.Select(outerCall.ParameterIndex),
                Is.EqualTo(new[] { 1, 0, 2 }));
            Assert.That(outerCall.IsUnsafeAuthorized, Is.True);
            Assert.That(outerCall.ForeignInterfaceCall!
                .NativeOutputContracts.Single().FormalOrdinal,
                Is.EqualTo(2));
            Assert.That(outerCall.Arguments[2],
                Is.TypeOf<BoundNativeOutputReservation>());

            var expectedSourceVariables = new[] { 2, 1 };
            for (var index = 0; index < nestedCalls.Length; index++)
            {
                var nestedCall = nestedCalls[index]!;
                Assert.That(nestedCall.IsUnsafeAuthorized, Is.True);
                Assert.That(nestedCall.Type,
                    Is.EqualTo(PrimitiveType.Int32));
                Assert.That(((BoundRead)nestedCall.Arguments.Single())
                    .Variable, Is.EqualTo(expectedSourceVariables[index]));
                Assert.That(nestedCall.ForeignInterfaceCall!
                    .Member.DeclaringInterface.ModuleImport?.ProducerIdentity,
                    Is.EqualTo(expectedProvider));
            }

            Assert.That(outerCall.ForeignInterfaceCall!.Member
                .DeclaringInterface.ModuleImport?.ProducerIdentity,
                Is.EqualTo(expectedProvider));
        }

        static void AssertNestedFniMixedConditionalOutputCall(
            BoundProgram program, string? expectedProvider)
        {
            var invoke = program.Functions.Single(function =>
                function.Name == "InvokeNestedConstantCorrelated");
            var members = program.Interfaces.Single().Members;
            string GetMemberName(BoundCall call)
                => members.Single(member => member.Ordinal
                    == call.ForeignInterfaceCall!.Member.MemberOrdinal).Name;

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
            Assert.That(calls, Has.Count.EqualTo(3));
            var outerCall = calls.Single(call =>
                GetMemberName(call) == "TryReadTriple");
            BoundCall? UnwrapCall(BoundExpression expression)
            {
                while (expression is BoundUnsafeExpression unsafeCall)
                {
                    expression = unsafeCall.Expression;
                }

                return expression as BoundCall;
            }

            var nestedCalls = new[] {
                UnwrapCall(outerCall.Arguments[0]),
                UnwrapCall(outerCall.Arguments[1]),
            };
            Assert.That(nestedCalls.All(static call => call is not null),
                Is.True);
            Assert.That(nestedCalls.Select(call => GetMemberName(call!)),
                Is.EqualTo(new[] { "GetIndexWithBias", "GetIndex" }));
            Assert.That(new[] { 0, 1, 2, 3 }.Select(
                    outerCall.ParameterIndex),
                Is.EqualTo(new[] { 1, 0, 2, 3 }));
            Assert.That(outerCall.Arguments[2], Is.TypeOf<BoundConstant>());
            Assert.That(((BoundConstant)outerCall.Arguments[2]).Value,
                Is.EqualTo(42L));
            Assert.That(outerCall.Arguments[3],
                Is.TypeOf<BoundNativeOutputReservation>());
            Assert.That(outerCall.ForeignInterfaceCall!
                .NativeOutputContracts.Single().FormalOrdinal,
                Is.EqualTo(3));

            var expectedSourceVariables = new[] { 2, 1 };
            for (var index = 0; index < nestedCalls.Length; index++)
            {
                var nestedCall = nestedCalls[index]!;
                Assert.That(nestedCall.IsUnsafeAuthorized, Is.True);
                if (index == 0)
                {
                    Assert.That(((BoundRead)nestedCall.Arguments[0])
                        .Variable, Is.EqualTo(expectedSourceVariables[index]));
                    Assert.That(nestedCall.Arguments[1],
                        Is.TypeOf<BoundConstant>());
                    Assert.That(((BoundConstant)nestedCall.Arguments[1]).Value,
                        Is.EqualTo(42L));
                }
                else
                {
                    Assert.That(((BoundRead)nestedCall.Arguments.Single())
                        .Variable, Is.EqualTo(expectedSourceVariables[index]));
                }

                Assert.That(nestedCall.ForeignInterfaceCall!
                    .Member.DeclaringInterface.ModuleImport?.ProducerIdentity,
                    Is.EqualTo(expectedProvider));
            }

            Assert.That(outerCall.ForeignInterfaceCall!.Member
                .DeclaringInterface.ModuleImport?.ProducerIdentity,
                Is.EqualTo(expectedProvider));
        }

        void AssertMalformedNestedFniBodyRejected(int methodRow,
            TrRmRegisterBodyReader body, Action<byte[], int, int, int> mutate,
            int outerCallRecordIndex = 3)
        {
            var malformedBodySection = bodySectionBytes.ToArray();
            var bodyRow = consumerTables.Methods.GetBodyRow(methodRow);
            var bodyRowEnd = checked((bodyCount + 1) * sizeof(uint)
                + (int)BinaryPrimitives.ReadUInt32LittleEndian(
                    malformedBodySection.AsSpan(bodyRow * sizeof(uint),
                        sizeof(uint))));
            var codeStart = checked(bodyRowEnd
                - body.CodeByteLength);
            var recordDataLength = 0;
            for (var record = 0;
                record < body.RecordCount; record++)
            {
                recordDataLength = checked(recordDataLength
                    + body.GetRecord(record).Length);
            }

            var recordDataStart = checked(codeStart - recordDataLength);
            var secondNestedCallRecordStart = checked(recordDataStart
                + body.GetRecord(0).Length
                + body.GetRecord(1).Length);
            var outerCallRecordStart = recordDataStart;
            for (var record = 0; record < outerCallRecordIndex; record++)
            {
                outerCallRecordStart = checked(outerCallRecordStart
                    + body.GetRecord(record).Length);
            }
            var entryBlockStart = checked(codeStart + (int)
                BinaryPrimitives.ReadUInt32LittleEndian(
                    body.GetBlock(0)));
            mutate(malformedBodySection, entryBlockStart,
                secondNestedCallRecordStart, outerCallRecordStart);

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
    public void StandardForeignNativeInterfaceReadsPointerOutputUnderPs1()
    {
        var program = CreatePointerOutputConsumerProgram();
        var function = program.Functions.Single(
            static function => function.Name == "InvokePointer");
        Assert.That(NativePointerTypes.IsType(function.ReturnType), Is.True);
    }

    [Test]
    public void StandardForeignNativeInterfaceReadsFunctionPointerOutputUnderPs1()
    {
        var program = CreatePointerOutputConsumerProgram(
            functionPointerOutput: true,
            assertFunctionPointerProviderReplacement: true,
            floatInputConversion: true,
            multipleOutputs: true);
        var function = program.Functions.Single(
            static function => function.Name == "InvokePointer");
        Assert.That(NativeFunctionPointerTypes.IsType(function.ReturnType),
            Is.True);
        var signature = program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(function.ReturnType)];
        Assert.That(signature.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Cdecl));
        Assert.That(signature.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(signature.ParameterTypes,
            Is.EqualTo(new[] { PrimitiveType.Int32 }));
        var image = NativeEmitter.EmitLibrary(program, ["InvokePointer"],
            NativeRuntimePolicy.FailFast);
        Assert.That(image[..2], Is.EqualTo(new byte[] {
            (byte)'M', (byte)'Z',
        }));
    }

    [Test]
    public async Task StandardForeignNativeInterfacePointerOutputExecutesOnWindowsX64()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var program = CreatePointerOutputConsumerProgram();
        var image = NativeEmitter.EmitLibrary(program, ["InvokePointer"],
            NativeRuntimePolicy.FailFast);
        Assert.That(image[..2], Is.EqualTo(new byte[] {
            (byte)'M', (byte)'Z',
        }));
        await AssertPointerOutputAsync(image, "InvokePointer")
            .ConfigureAwait(true);
    }

    [Test]
    public async Task StandardForeignNativeInterfacePointerOutputUsesNonNegativeResultAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var program = CreatePointerOutputConsumerProgram("NonNegative",
            "status >= 0");
        var image = NativeEmitter.EmitLibrary(program, ["InvokePointer"],
            NativeRuntimePolicy.FailFast);
        Assert.That(image[..2], Is.EqualTo(new byte[] {
            (byte)'M', (byte)'Z',
        }));
        await AssertPointerOutputAsync(image, "InvokePointer",
            successResult: 0, failureResult: -1).ConfigureAwait(true);
    }

    [Test]
    public async Task StandardForeignNativeInterfaceFunctionPointerOutputExecutesOnWindowsX64()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var program = CreatePointerOutputConsumerProgram(
            functionPointerOutput: true);
        var image = NativeEmitter.EmitLibrary(program, ["InvokePointer"],
            NativeRuntimePolicy.FailFast);
        Assert.That(image[..2], Is.EqualTo(new byte[] {
            (byte)'M', (byte)'Z',
        }));
        await AssertFunctionPointerOutputAsync(image, "InvokePointer")
            .ConfigureAwait(true);
    }

    [Test]
    public async Task StandardForeignNativeInterfaceMultipleOutputFunctionPointerExecutesOnWindowsX64()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var program = CreatePointerOutputConsumerProgram(
            functionPointerOutput: true, multipleOutputs: true);
        var image = NativeEmitter.EmitLibrary(program, ["InvokePointer"],
            NativeRuntimePolicy.FailFast);
        Assert.That(image[..2], Is.EqualTo(new byte[] {
            (byte)'M', (byte)'Z',
        }));
        await AssertMultipleFunctionPointerOutputAsync(image, "InvokePointer")
            .ConfigureAwait(true);
    }

    private static BoundProgram CreatePointerOutputConsumerProgram(
        string outputCondition = "NonZero",
        string successPredicate = "status != 0",
        bool functionPointerOutput = false,
        bool assertFunctionPointerProviderReplacement = false,
        bool floatInputConversion = false,
        bool multipleOutputs = false)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-native-interface-parameter-surface-1";
        const string providerIdentity = "pointer-output-provider";
        const string consumerIdentity = "pointer-output-consumer";
        var outputValueType = functionPointerOutput
            ? "[Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Cdecl)] fnptr<int32, int32>"
            : "ptr<int32>";
        var outputParameterDependencies = functionPointerOutput
            ? string.Empty
            : """
                [Terrum.CodeAnalysis.DependsOn(nameof(source))]
                [Terrum.CodeAnalysis.ForwardsBacking(nameof(source))]
                """;
        var outputReturnDependencies = functionPointerOutput
            ? string.Empty
            : """
                [return: Terrum.CodeAnalysis.DependsOn(
                    nameof(source), nameof(fallback))]
                [return: Terrum.CodeAnalysis.ForwardsBacking(
                    nameof(source), nameof(fallback))]
                """;
        var additionalScalarOutputParameter = multipleOutputs
            ? $$"""
                    [param(statusValue): NativeOutput(
                        When: NativeOutputCondition.{{outputCondition}})]
                    write ptr<int32> statusValue,
                """
            : string.Empty;
        var additionalOutputLocal = multipleOutputs
            ? "init int32 statusValue;\n                "
            : string.Empty;
        var additionalOutputArgument = multipleOutputs
            ? "statusValue: &init statusValue,\n                        "
            : string.Empty;
        var tagSourceType = floatInputConversion ? "float64" : "int32";
        var tagExpression = floatInputConversion ? "int32(tag)" : "tag";
        var providerSource = $$"""
            using Terrum.Interop;
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IPointerProbe {
                [param(value): NativeOutput(
                    When: NativeOutputCondition.{{outputCondition}})]
                public unsafe int32 TryGetPointer(ptr<int32> source,
                    int32 tag,
                    {{additionalScalarOutputParameter}}
                    {{outputParameterDependencies}}
                    write ptr<{{outputValueType}}> value);
            }
            """;
        var consumerSource = $$"""
            {{outputReturnDependencies}}
            {{outputValueType}} InvokePointer(ptr<void> receiver,
                ptr<int32> source, {{tagSourceType}} tag,
                {{outputValueType}} fallback) {
                {{additionalOutputLocal}}
                init {{outputValueType}} value;
                unsafe {
                    int32 status = IPointerProbe.TryGetPointer(
                        receiver, value: &init value,
                        {{additionalOutputArgument}}
                        tag: {{tagExpression}},
                        source: source);
                    if ({{successPredicate}}) { return value; }
                    return fallback;
                }
            }
            """;
        var (native, output) = NativeOutputCoreRegistrationTests
            .CreateCombinedRegistrations(
                includeForeignNativeInterface: true);
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
        var providerProgram = providerCompilation.Program!;
        var providerMember = providerProgram.Interfaces.Single().Members
            .Single();
        var outputContract = providerMember.NativeOutputContracts.Single(
            contract => functionPointerOutput
                ? NativeFunctionPointerTypes.IsType(contract.PointeeType)
                : NativePointerTypes.IsType(contract.PointeeType));
        var outputType = outputContract.PointeeType;
        AssertOutputType(providerProgram, outputType);
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, providerIdentity, schemaIdentity, 1, [
                new TrRmSourceTypeExport("", "IPointerProbe",
                    "provider/IPointerProbe") {
                    GenericArity = 0,
                    InterfaceMethodExports = [
                        new TrRmSourceInterfaceMethodExport(
                            providerMember.SourceInputOrdinal,
                            providerMember.SourceDeclarationSpan,
                            "provider/IPointerProbe.TryGetPointer"),
                    ],
                },
            ]);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var providerReference = TrRmExecutableCompiler
            .CreateStandardReference(providerInput, providerIdentity, []);
        var referencedMember = providerReference.Interfaces.Single()
            .Members.Single();
        Assert.That(referencedMember.NativeOutputContracts.Length,
            Is.EqualTo(multipleOutputs ? 2 : 1));
        if (multipleOutputs)
        {
            Assert.That(referencedMember.NativeOutputContracts.Any(
                contract => contract.PointeeType == PrimitiveType.Int32),
                Is.True);
        }
        var referencedOutput
            = referencedMember.NativeOutputContracts.Single(contract =>
                contract.PointeeType == outputType);
        AssertOutputType(providerReference, referencedOutput.PointeeType);
        Assert.That(referencedOutput.Dependencies,
            Is.EqualTo(outputContract.Dependencies));
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(consumerSource), providerReference, options);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program!;
        if (multipleOutputs)
        {
            AssertOutputFormalOrder(consumerProgram);
        }
        if (floatInputConversion)
        {
            AssertFloatTagConversion(consumerProgram);
        }
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, consumerIdentity, schemaIdentity, 1, []);
        var schema
            = TrRmArtifactSchema
                .ForeignNativeInterfaceParameterSurfaceDraft;
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact, schema));
        var methodRow = Enumerable.Range(1, tables.Methods.Count).Single(
            row => tables.Methods.GetName(row) == "InvokePointer");
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(methodRow), tables.Contracts.Count);
        if (floatInputConversion)
        {
            Assert.That(body.GetBlockCode(0).ToArray().Contains((byte)0xD0),
                Is.True);
        }
        var outputValueHome = consumerProgram.Functions.Single(
            static function => function.Name == "InvokePointer")
            .ParameterCount + (multipleOutputs ? 1 : 0);
        Assert.That(body.GetBlockCode(1).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, checked((byte)outputValueHome),
                checked((byte)(outputValueHome >> 8)), 0x17, 0,
            }));
        Assert.That(body.GetBlockCode(2).ToArray(),
            Is.EqualTo(new byte[] { 0xD5, 0, 3, 0, 0x17, 0 }));

        if (assertFunctionPointerProviderReplacement)
        {
            if (!functionPointerOutput)
            {
                throw new InvalidOperationException(
                    "Function-pointer provider replacement requires a function-pointer output.");
            }

            var incompatibleProviderSource = providerSource.Replace(
                outputValueType,
                "[Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Cdecl)] fnptr<int64, int64>",
                StringComparison.Ordinal);
            var incompatibleProviderCompilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(incompatibleProviderSource), options);
            Assert.That(incompatibleProviderCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    incompatibleProviderCompilation.Diagnostics));
            var incompatibleProviderProgram
                = incompatibleProviderCompilation.Program!;
            var incompatibleProviderMember
                = incompatibleProviderProgram.Interfaces.Single().Members
                    .Single();
            var incompatibleProviderArtifact
                = TrRmExecutableCompiler.EmitStandard(
                    incompatibleProviderProgram, providerIdentity,
                    schemaIdentity, 1, [
                        new TrRmSourceTypeExport("", "IPointerProbe",
                            "provider/IPointerProbe") {
                            GenericArity = 0,
                            InterfaceMethodExports = [
                                new TrRmSourceInterfaceMethodExport(
                                    incompatibleProviderMember
                                        .SourceInputOrdinal,
                                    incompatibleProviderMember
                                        .SourceDeclarationSpan,
                                    "provider/IPointerProbe.TryGetPointer"),
                            ],
                        },
                    ]);
            var incompatibleProviderInput
                = new TrRmStandardArtifactInput(schemaIdentity,
                    incompatibleProviderArtifact);
            Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler.LoadStandard(
                    new TrRmStandardArtifactInput(schemaIdentity,
                        consumerArtifact), consumerIdentity,
                    [incompatibleProviderInput]));
        }

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            consumerIdentity, [providerInput]);
        var loadedFunction = loaded.Functions.Single(
            static function => function.Name == "InvokePointer");
        AssertOutputType(loaded, loadedFunction.ReturnType);
        if (floatInputConversion)
        {
            AssertFloatTagConversion(loaded);
        }
        if (multipleOutputs)
        {
            AssertOutputFormalOrder(loaded);
        }
        var successReturn = (BoundReturn)loadedFunction.Body.Statements
            .OfType<BoundIf>().Single().Then.Statements.Single();
        Assert.That(successReturn.Value, Is.TypeOf<BoundRead>());
        Assert.That(successReturn.Value.Type,
            Is.EqualTo(loadedFunction.ReturnType));
        Assert.That(((BoundRead)successReturn.Value).Variable,
            Is.EqualTo(loadedFunction.ParameterCount
                + (multipleOutputs ? 1 : 0)));

        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            consumerIdentity, schemaIdentity, 1, []);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reemittedArtifact), consumerIdentity, [providerInput]);
        var reloadedFunction = reloaded.Functions.Single(
            static function => function.Name == "InvokePointer");
        AssertOutputType(reloaded, reloadedFunction.ReturnType);
        if (floatInputConversion)
        {
            AssertFloatTagConversion(reloaded);
        }
        if (multipleOutputs)
        {
            AssertOutputFormalOrder(reloaded);
        }
        return reloaded;

        void AssertOutputType(BoundProgram candidate, PrimitiveType type)
        {
            if (functionPointerOutput)
            {
                Assert.That(NativeFunctionPointerTypes.IsType(type), Is.True);
                var signature = candidate.NativeFunctionPointerSignatures[
                    NativeFunctionPointerTypes.Index(type)];
                Assert.That(signature.CallingConvention,
                    Is.EqualTo(NativeCallingConvention.Cdecl));
                Assert.That(signature.ReturnType,
                    Is.EqualTo(PrimitiveType.Int32));
                Assert.That(signature.ParameterTypes,
                    Is.EqualTo(new[] { PrimitiveType.Int32 }));
            }
            else
            {
                Assert.That(NativePointerTypes.IsType(type), Is.True);
                Assert.That(candidate.NativePointerTypes[
                    NativePointerTypes.Index(type)].PointeeType,
                    Is.EqualTo(PrimitiveType.Int32));
            }
        }

        static void AssertFloatTagConversion(BoundProgram candidate)
        {
            var function = candidate.Functions.Single(
                static function => function.Name == "InvokePointer");
            BoundCall? outerCall = null;
            _ = BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall call
                        && call.ForeignInterfaceCall?.Member.Name
                            == "TryGetPointer")
                    {
                        outerCall = call;
                    }
                });
            var selectedCall = outerCall
                ?? throw new AssertionException(
                    "Expected the FNI TryGetPointer call.");
            var tagArgument = Enumerable.Range(0,
                    selectedCall.Arguments.Length)
                .Single(index => selectedCall.ParameterIndex(index) == 1);
            Assert.That(selectedCall.Arguments[tagArgument],
                Is.TypeOf<BoundCast>());
            var conversion = (BoundCast)selectedCall.Arguments[tagArgument];
            Assert.That(conversion.Type, Is.EqualTo(PrimitiveType.Int32));
            Assert.That(conversion.Operand.Type,
                Is.EqualTo(PrimitiveType.Float64));
        }

        static void AssertOutputFormalOrder(BoundProgram candidate)
        {
            var function = candidate.Functions.Single(
                static function => function.Name == "InvokePointer");
            BoundCall? outerCall = null;
            _ = BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall call
                        && call.ForeignInterfaceCall?.Member.Name
                            == "TryGetPointer")
                    {
                        outerCall = call;
                    }
                });
            var selectedCall = outerCall
                ?? throw new AssertionException(
                    "Expected the FNI TryGetPointer call.");
            var outputArguments = Enumerable.Range(0,
                    selectedCall.Arguments.Length)
                .Where(index => selectedCall.Arguments[index]
                    is BoundNativeOutputReservation)
                .ToArray();
            Assert.That(outputArguments, Has.Length.EqualTo(2));
            Assert.That(outputArguments.Select(index =>
                    selectedCall.ParameterIndex(index)),
                Is.EqualTo(new[] { 3, 2 }));
            Assert.That(selectedCall.ForeignInterfaceCall!
                .NativeOutputContracts.Length, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task StandardForeignNativeInterfaceConsumesNestedAggregateOutputLargerThanSixteenBytesUnderAc1()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string schemaIdentity
            = "trrm/1-draft-foreign-native-interface-aggregate-output-consumption-1";
        const string providerSource = """
            using Terrum.Interop;
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Inner {
                public int64 X;
                public int64 Y;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Payload {
                public Inner Inner;
                public int64 Z;
            }
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                public unsafe int32 GetTag(ptr<int32> tagSource);

                [param(value): NativeOutput(
                    When: NativeOutputCondition.NonZero)]
                public unsafe int32 TryReadPayload(ptr<int32> source,
                    int32 tag, int32 mode,
                    [Terrum.CodeAnalysis.DependsOn(
                        this, nameof(source), nameof(tag), nameof(mode))]
                    [Terrum.CodeAnalysis.ForwardsBacking(
                        this, nameof(source))]
                    write ptr<Payload> value);
            }
            """;
        const string consumerSource = """
            Payload InvokeLiteralPayload(ptr<void> receiver,
                ptr<int32> source, Payload fallback) {
                init Payload value;
                unsafe {
                    int32 status = IProbe.TryReadPayload(
                        receiver, source, 7, 9, &init value);
                    if (status != 0) { return value; }
                    return fallback;
                }
            }

            Payload InvokePayload(ptr<void> receiver, ptr<int32> source,
                ptr<int32> tagSource, int32 mode, Payload fallback) {
                init Payload value;
                unsafe {
                    int32 status = IProbe.TryReadPayload(
                        receiver, source,
                        IProbe.GetTag(receiver, tagSource), mode,
                        &init value);
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
        var members = providerProgram.Interfaces.Single().Members;
        Assert.That(members.Length, Is.EqualTo(2));
        var getTag = members.Single(member => member.Name == "GetTag");
        var tryReadPayload = members.Single(
            member => member.Name == "TryReadPayload");
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram,
            "provider.module", schemaIdentity, 1, [
                new TrRmSourceTypeExport("", "Inner", "provider/Inner") {
                    FieldExports = [
                        new TrRmSourceFieldExport("X", "provider/Inner.X"),
                        new TrRmSourceFieldExport("Y", "provider/Inner.Y"),
                    ],
                },
                new TrRmSourceTypeExport("", "Payload",
                    "provider/Payload") {
                    FieldExports = [
                        new TrRmSourceFieldExport("Inner",
                            "provider/Payload.Inner"),
                        new TrRmSourceFieldExport("Z", "provider/Payload.Z"),
                    ],
                },
                new TrRmSourceTypeExport("", "IProbe",
                    "provider/IProbe") {
                    GenericArity = 0,
                    InterfaceMethodExports = [
                        new TrRmSourceInterfaceMethodExport(
                            getTag.SourceInputOrdinal,
                            getTag.SourceDeclarationSpan,
                            "provider/IProbe.GetTag"),
                        new TrRmSourceInterfaceMethodExport(
                            tryReadPayload.SourceInputOrdinal,
                            tryReadPayload.SourceDeclarationSpan,
                            "provider/IProbe.TryReadPayload"),
                    ],
                },
            ]);
        var schema
            = TrRmArtifactSchema
                .ForeignNativeInterfaceAggregateOutputConsumptionDraft;
        var providerTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                providerArtifact, schema));
        foreach (var exportKey in new[] {
            "provider/Inner",
            "provider/Payload",
        })
        {
            Assert.That(providerTables.Exports.TryGetDefinitionForKey(1, exportKey,
                out var definition), Is.True);
            var typeDef = checked((int)(definition & 0x00FF_FFFF));
            Assert.That(providerTables.TypeDefs.HasDeclaredCopy(typeDef),
                Is.True);
        }
        Assert.That(TrRmFeatureManifestValidator.Requires(
            providerTables.Features, providerTables.Modules, 1,
            TrRmFeatureTableReader.RootlessNativeValuesFeatureId, schema),
            Is.True);
        Assert.That(TrRmFeatureManifestValidator.Requires(
            providerTables.Features, providerTables.Modules, 1,
            TrRmFeatureTableReader.LayoutQueriesFeatureId, schema), Is.True);

        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var providerReference
            = TrRmExecutableCompiler.CreateStandardReference(providerInput,
                "provider.module", []);
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(consumerSource), providerReference, options);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new AssertionException("Expected a bound consumer.");
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "consumer.module", schemaIdentity, 1, []);
        var consumerTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                consumerArtifact, schema));
        foreach (var exportKey in new[] {
            "provider/Inner",
            "provider/Payload",
        })
        {
            var importRow = Enumerable.Range(1,
                    consumerTables.Imports.Count).Single(row =>
                    (consumerTables.Imports
                        .GetExpectedDefinitionForImportRow(row) >> 24)
                        == 0x13
                    && StringComparer.Ordinal.Equals(
                        consumerTables.Imports.GetBindingForImportRow(row)
                            .ExportKey, exportKey));
            var expectedDefinition = consumerTables.Imports
                .GetExpectedDefinitionForImportRow(importRow);
            Assert.That(consumerTables.TypeDefs.HasDeclaredCopy(
                checked((int)(expectedDefinition & 0x00FF_FFFF))), Is.True);
        }

        var method = Enumerable.Range(1, consumerTables.Methods.Count)
            .Single(row =>
                consumerTables.Methods.GetName(row) == "InvokePayload");
        var body = consumerTables.Bodies.GetRegisterBody(
            consumerTables.Methods.GetBodyRow(method),
            consumerTables.Contracts.Count);
        Assert.That(body.BlockCount, Is.EqualTo(3));
        Assert.That(body.RecordCount, Is.EqualTo(5));
        Assert.That(body.MethodCount, Is.EqualTo(2));
        Assert.That(body.HomeCount, Is.EqualTo(7));
        Assert.That(body.GetBlockCode(0)[..2].ToArray(),
            Is.EqualTo(new byte[] { 0x90, 1 }));
        var nestedCallPayload = body.GetRecord(1)[4..];
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            nestedCallPayload[6..]), Is.EqualTo((ushort)1));
        var outerCallPayload = body.GetRecord(2)[4..];
        Assert.That(outerCallPayload[24], Is.EqualTo(1));
        Assert.That(outerCallPayload[25], Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            outerCallPayload[26..]), Is.EqualTo((ushort)1));
        var outputHome = body.HomeMap.Slice(5 * 8, 8);
        var outputTypeMapIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(outputHome[2..]);
        var outputTypeToken = BinaryPrimitives.ReadUInt32LittleEndian(
            body.TypeMap[outputTypeMapIndex * sizeof(uint)..]);
        Assert.That(outputTypeToken >> 24, Is.EqualTo(0x1Cu));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            body.MethodMap) >> 24, Is.EqualTo(0x1Cu));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            body.MethodMap[sizeof(uint)..]) >> 24, Is.EqualTo(0x1Cu));
        Assert.That(body.GetBlockCode(1).ToArray(),
            Is.EqualTo(new byte[] { 0xD5, 0, 5, 0, 0x17, 0 }));
        Assert.That(body.GetBlockCode(2).ToArray(),
            Is.EqualTo(new byte[] { 0xD5, 0, 4, 0, 0x17, 0 }));

        var literalMethod = Enumerable.Range(1, consumerTables.Methods.Count)
            .Single(row =>
                consumerTables.Methods.GetName(row) == "InvokeLiteralPayload");
        var literalBody = consumerTables.Bodies.GetRegisterBody(
            consumerTables.Methods.GetBodyRow(literalMethod),
            consumerTables.Contracts.Count);
        Assert.That(literalBody.BlockCount, Is.EqualTo(3));
        Assert.That(literalBody.RecordCount, Is.EqualTo(4));
        Assert.That(literalBody.MethodCount, Is.EqualTo(1));
        Assert.That(literalBody.HomeCount, Is.EqualTo(5));
        Assert.That(literalBody.ConstantCount, Is.EqualTo(2));
        Assert.That(literalBody.ConstantMap.Length, Is.EqualTo(16));
        Assert.That(literalBody.GetBlockCode(0)[..5].ToArray(),
            Is.EqualTo(new byte[] { 0x16, 1, 0x16, 0x12, 0xDF }));
        var literalCallPayload = literalBody.GetRecord(1)[4..];
        Assert.That(literalCallPayload[24], Is.EqualTo(1));
        Assert.That(literalCallPayload[25], Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            literalCallPayload[26..]), Is.EqualTo((ushort)1));
        Assert.That(literalCallPayload[28], Is.EqualTo(1));
        Assert.That(literalCallPayload[29], Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            literalCallPayload[30..]), Is.EqualTo((ushort)2));

        var consumerInput = new TrRmStandardArtifactInput(schemaIdentity,
            consumerArtifact);
        var providers = new[] { providerInput };
        var loaded = TrRmExecutableCompiler.LoadStandard(consumerInput,
            "consumer.module", providers);
        var function = loaded.Functions.Single(function =>
            function.Name == "InvokePayload");
        var conditional = function.Body.Statements.OfType<BoundIf>().Single();
        var outputReturn = (BoundReturn)conditional.Then.Statements.Single();
        var fallbackReturn = (BoundReturn)function.Body.Statements[^1];
        Assert.That(outputReturn.Value, Is.TypeOf<BoundRead>());
        Assert.That(fallbackReturn.Value, Is.TypeOf<BoundRead>());
        Assert.That(((BoundRead)outputReturn.Value).Variable,
            Is.EqualTo(function.ParameterCount));
        Assert.That(((BoundRead)fallbackReturn.Value).Variable,
            Is.EqualTo(4));
        var literalFunction = loaded.Functions.Single(
            function => function.Name == "InvokeLiteralPayload");
        var literalStatus = (BoundLocal)literalFunction.Body.Statements[1];
        var literalCall = (BoundCall)((BoundUnsafeExpression)
            literalStatus.Initializer).Expression;
        Assert.That(literalCall.Arguments[1], Is.TypeOf<BoundConstant>());
        Assert.That(((BoundConstant)literalCall.Arguments[1]).Value,
            Is.EqualTo(7));
        Assert.That(literalCall.Arguments[2], Is.TypeOf<BoundConstant>());
        Assert.That(((BoundConstant)literalCall.Arguments[2]).Value,
            Is.EqualTo(9));
        var image = NativeEmitter.EmitLibrary(loaded,
            ["InvokePayload", "InvokeLiteralPayload"],
            NativeRuntimePolicy.FailFast);
        Assert.That(image[..2], Is.EqualTo(new byte[] {
            (byte)'M', (byte)'Z',
        }));
        Assert.That(Marshal.SizeOf<NativeAggregateNestedPayload>(),
            Is.EqualTo(24));
        await AssertNestedAggregatePayloadOutputAsync(image, "InvokePayload")
            .ConfigureAwait(true);
        await AssertLiteralAggregatePayloadOutputAsync(image,
            "InvokeLiteralPayload").ConfigureAwait(true);
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            "consumer.module", schemaIdentity, 1, []);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reemittedArtifact), "consumer.module", providers);
        var reloadedImage = NativeEmitter.EmitLibrary(reloaded,
            ["InvokePayload", "InvokeLiteralPayload"],
            NativeRuntimePolicy.FailFast);
        await AssertNestedAggregatePayloadOutputAsync(reloadedImage,
            "InvokePayload").ConfigureAwait(true);
        await AssertLiteralAggregatePayloadOutputAsync(reloadedImage,
            "InvokeLiteralPayload").ConfigureAwait(true);
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
                    public ptr<int32> Link;
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
    public void StandardForeignNativeInterfaceRejectsDuplicateOutputSuccessState()
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
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IProbe {
                [param(value): NativeOutput(
                    When: NativeOutputCondition.NonZero)]
                public unsafe int32 TryRead(ptr<int32> source,
                    [Terrum.CodeAnalysis.DependsOn(this, nameof(source))]
                    [Terrum.CodeAnalysis.ForwardsBacking(
                        this, nameof(source))]
                    write ptr<int32> value);
            }
            """), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound FNI provider.");
        var member = program.Interfaces.Single().Members.Single();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "provider.module", schemaIdentity, 1, [
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
        var schema
            = TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft;
        var index = new TrRmArtifactIndex(artifact, schema);
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(index);
        byte[] RewriteContracts(TrRmContractRow[] contractRows)
        {
            var contractBytes
                = TrRmContractTableWriter.EncodeForeignInvocationDraft(
                    contractRows, tables.TypeDefs, tables.Methods,
                    tables.Signatures, tables.Parameters, tables.TypeSpecs,
                    tables.Features, tables.Modules,
                    tables.PredicateGraphs.Count, schema);
            var sections = new TrRmSection[index.SectionCount];
            for (var sectionIndex = 0; sectionIndex < index.SectionCount;
                sectionIndex++)
            {
                index.GetSection(sectionIndex, out var kind, out var flags,
                    out var bytes, out var count);
                sections[sectionIndex] = new TrRmSection(kind, flags,
                    kind == 0x001B
                        ? checked((uint)contractRows.Length)
                        : checked((uint)count),
                    kind == 0x001B ? contractBytes : bytes.ToArray());
            }

            return TrRmArtifactWriter.Encode(sections, schema);
        }

        var rows = new TrRmContractRow[tables.Contracts.Count];
        var mutated = false;
        for (var row = 1; row <= tables.Contracts.Count; row++)
        {
            var data = tables.Contracts.GetRow(row);
            var owner = BinaryPrimitives.ReadUInt32LittleEndian(data);
            var kind = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
            var payload = data[8..].ToArray();
            if (owner == 0x1600_0001 && kind == 2 && payload[8] == 0)
            {
                payload[8] = 1;
                mutated = true;
            }
            rows[row - 1] = new TrRmContractRow(owner, kind, payload);
        }
        Assert.That(mutated, Is.True);

        var malformedArtifact = RewriteContracts(rows);
        var malformedTables
            = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                new TrRmArtifactIndex(malformedArtifact, schema));
        Assert.That(malformedTables.Contracts.GetOutputStateContracts(1)
            .Select(static state => state.State),
            Is.EqualTo(new byte[] { 1, 1 }));
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    malformedArtifact), "provider.module", []));

        var wrongFormalRows = new TrRmContractRow[tables.Contracts.Count];
        var wrongFormalMutated = false;
        for (var row = 1; row <= tables.Contracts.Count; row++)
        {
            var data = tables.Contracts.GetRow(row);
            var owner = BinaryPrimitives.ReadUInt32LittleEndian(data);
            var kind = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
            var payload = data[8..].ToArray();
            if (owner == 0x1600_0001 && kind == 2)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(payload, 0);
                wrongFormalMutated = true;
            }

            wrongFormalRows[row - 1] = new TrRmContractRow(owner, kind,
                payload);
        }
        Assert.That(wrongFormalMutated, Is.True);
        Assert.Throws<InvalidDataException>(() => {
            var wrongFormalArtifact = RewriteContracts(wrongFormalRows);
            _ = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    wrongFormalArtifact), "provider.module", []);
        });

        uint successGraphRow = 0;
        uint complementGraphRow = 0;
        for (var row = 1; row <= tables.Contracts.Count; row++)
        {
            var data = tables.Contracts.GetRow(row);
            if (BinaryPrimitives.ReadUInt32LittleEndian(data)
                    != 0x1600_0001
                || BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) != 2)
            {
                continue;
            }

            var payload = data[8..];
            var graphRow = BinaryPrimitives.ReadUInt32LittleEndian(
                payload[4..]);
            if (payload[8] == 1)
            {
                successGraphRow = graphRow;
            }
            else
            {
                complementGraphRow = graphRow;
            }
        }
        Assert.That(successGraphRow, Is.Not.Zero);
        Assert.That(complementGraphRow, Is.Not.Zero);
        Assert.That(successGraphRow, Is.Not.EqualTo(complementGraphRow));
        var swappedSelectorRows = new TrRmContractRow[tables.Contracts.Count];
        for (var row = 1; row <= tables.Contracts.Count; row++)
        {
            var data = tables.Contracts.GetRow(row);
            var owner = BinaryPrimitives.ReadUInt32LittleEndian(data);
            var kind = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
            var payload = data[8..].ToArray();
            if (owner == 0x1600_0001 && kind == 2)
            {
                var swappedGraphRow
                    = payload[8] == 1
                        ? complementGraphRow : successGraphRow;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    payload.AsSpan(4, 4), swappedGraphRow);
            }

            swappedSelectorRows[row - 1] = new TrRmContractRow(owner, kind,
                payload);
        }
        var swappedSelectorArtifact = RewriteContracts(
            swappedSelectorRows);
        _ = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                swappedSelectorArtifact, schema));
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    swappedSelectorArtifact), "provider.module", []));
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
        var index = new TrRmArtifactIndex(artifact, schema);
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(index);
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

        Assert.That(index.TryGetSection(0x0011, out var typeSpecSection,
            out var typeSpecCount), Is.True);
        var malformedTypeSpecSection = typeSpecSection.ToArray();
        var anchorTypeSpecBytes = tables.TypeSpecs
            .GetTypeSpecBytes(anchorTypeSpecRows[0]).ToArray();
        var anchorTypeSpecOffset = typeSpecSection.IndexOf(
            anchorTypeSpecBytes);
        Assert.That(anchorTypeSpecOffset, Is.GreaterThanOrEqualTo(0));
        var interfaceMethodRow
            = tables.InterfaceMethods.GetMethodDefRow(1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedTypeSpecSection.AsSpan(anchorTypeSpecOffset + 3,
                sizeof(uint)), checked((uint)interfaceMethodRow));
        var malformedSections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var count);
            malformedSections.Add(kind == 0x0011
                ? new TrRmSection(kind, flags,
                    checked((uint)typeSpecCount), malformedTypeSpecSection)
                : new TrRmSection(kind, flags, checked((uint)count),
                    bytes.ToArray()));
        }

        var malformedAnchorArtifact
            = TrRmArtifactWriter.Encode(malformedSections, schema);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    malformedAnchorArtifact), "provider.module", []));

        const string callerSource = """
            using Terrum.Interop;
            int32 Invoke(ptr<void> receiver,
                [NativeCallConv(NativeCallingConvention.Cdecl)]
                fnptr<int32, int32> callback) {
                unsafe { return IProbe.Apply(receiver, callback); }
            }
            """;
        var caller = Compilation.CreateLibrary(SyntaxTree.Parse(
            callerSource), reference);
        Assert.That(caller.HasErrors, Is.False,
            string.Join(Environment.NewLine, caller.Diagnostics));
        var shiftedProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Terrum.Interop;
                [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
                [ForeignNativeInterface(VTableByteOffset: uint32(0))]
                public interface IExtra {
                    public unsafe int32 ApplyWide(
                        [NativeCallConv(NativeCallingConvention.Cdecl)]
                        fnptr<float64, float64> callback);
                }
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
        Assert.That(shiftedProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                shiftedProviderCompilation.Diagnostics));
        var shiftedProviderProgram = shiftedProviderCompilation.Program
            ?? throw new AssertionException(
                "Expected a provider with a leading FNI owner.");
        var shiftedProviderExports
            = shiftedProviderProgram.Interfaces.Select(
                shiftedDefinition =>
                    new TrRmSourceTypeExport("", shiftedDefinition.QualifiedName,
                        $"provider/{shiftedDefinition.QualifiedName}") {
                        GenericArity = 0,
                        InterfaceMethodExports = [
                            .. shiftedDefinition.Members.Select(
                                shiftedMember =>
                                    new TrRmSourceInterfaceMethodExport(
                                        shiftedMember.SourceInputOrdinal,
                                        shiftedMember.SourceDeclarationSpan,
                                        $"provider/{shiftedDefinition.QualifiedName}."
                                            + shiftedMember.Name)),
                        ],
                    }).ToArray();
        var shiftedProviderArtifact
            = TrRmExecutableCompiler.EmitStandard(
                shiftedProviderProgram, "provider.module", schemaIdentity, 1,
                shiftedProviderExports);
        var shiftedProviderIndex = new TrRmArtifactIndex(
            shiftedProviderArtifact, schema);
        var shiftedProviderTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(shiftedProviderIndex);
        var shiftedAnchorTypeSpecRows = new List<int>();
        for (var typeSpecRow = 1;
            typeSpecRow <= shiftedProviderTables.TypeSpecs.Count;
            typeSpecRow++)
        {
            if (shiftedProviderTables.NativeFunctionPointerAnchors
                .HasAnchorForTypeSpec(typeSpecRow))
            {
                shiftedAnchorTypeSpecRows.Add(typeSpecRow);
            }
        }
        Assert.That(shiftedAnchorTypeSpecRows, Has.Count.EqualTo(2));
        var shiftedApplyRow = Enumerable.Range(1,
                shiftedProviderTables.Methods.Count)
            .Single(row =>
                shiftedProviderTables.Methods.GetName(row) == "Apply");
        var baseApplyRow = Enumerable.Range(1, tables.Methods.Count)
            .Single(row => tables.Methods.GetName(row) == "Apply");
        Assert.That(shiftedApplyRow, Is.GreaterThan(baseApplyRow));

        var callerArtifact = TrRmExecutableCompiler.EmitStandard(
            caller.Program
                ?? throw new AssertionException(
                    "Expected a bound FNI callback caller."),
            "consumer.module", schemaIdentity, 1, []);
        var shiftedProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, shiftedProviderArtifact);
        var shiftedReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, callerArtifact),
            "consumer.module", [shiftedProviderInput]);
        var shiftedCaller = Compilation.CreateLibrary(
            SyntaxTree.Parse(callerSource), shiftedReference);
        Assert.That(shiftedCaller.HasErrors, Is.False,
            string.Join(Environment.NewLine, shiftedCaller.Diagnostics));
        AssertCallbackSignature(shiftedCaller.Program
            ?? throw new AssertionException(
                "Expected a caller bound to the shifted FNI provider."));
        var reemissionCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(""), shiftedReference);
        Assert.That(reemissionCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reemissionCompilation.Diagnostics));
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(
            reemissionCompilation.Program
                ?? throw new AssertionException(
                    "Expected a source-free FNI callback forwarder."),
            "forwarder.module", schemaIdentity, 1, []);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reemittedArtifact), "forwarder.module",
            [shiftedProviderInput]);
        AssertCallbackSignature(reloaded);

        static void AssertCallbackSignature(BoundProgram program)
        {
            var interfaceDefinition = program.Interfaces.Single(
                definition => definition.QualifiedName == "IProbe");
            var callbackType = interfaceDefinition.Members.Single(
                    member => member.Name == "Apply")
                .Parameters.Single().Type.PrimitiveType;
            Assert.That(NativeFunctionPointerTypes.IsType(callbackType),
                Is.True);
            var signature = program.NativeFunctionPointerSignatures.Single(
                candidate => candidate.ReturnType == PrimitiveType.Int32
                    && candidate.ParameterTypes
                        .SequenceEqual(new[] { PrimitiveType.Int32 }));
            Assert.That(signature.CallingConvention,
                Is.EqualTo(NativeCallingConvention.Cdecl));
            Assert.That(signature.ReturnType,
                Is.EqualTo(PrimitiveType.Int32));
            Assert.That(signature.ParameterTypes,
                Is.EqualTo(new[] { PrimitiveType.Int32 }));
        }
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
                public ptr<int32> Link;
                public ptr<Coordinates> Related;
                public [NativeCallConv(NativeCallingConvention.Cdecl)]
                    fnptr<ptr<Coordinates>, int32> Callback;
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
            var typeExports = new List<TrRmSourceTypeExport> {
                new("", "Coordinates", "provider/Coordinates") {
                    FieldExports = [
                        new TrRmSourceFieldExport("X",
                            "provider/Coordinates.X"),
                        new TrRmSourceFieldExport("Y",
                            "provider/Coordinates.Y"),
                    ],
                },
                new("", "Point", "provider/Point") {
                    FieldExports = [
                        new TrRmSourceFieldExport("Position",
                            "provider/Point.Position"),
                        new TrRmSourceFieldExport("Link",
                            "provider/Point.Link"),
                        new TrRmSourceFieldExport("Related",
                            "provider/Point.Related"),
                        new TrRmSourceFieldExport("Callback",
                            "provider/Point.Callback"),
                    ],
                },
            };
            typeExports.AddRange(providerProgram.Interfaces.Select(
                shiftedDefinition =>
                    new TrRmSourceTypeExport("", shiftedDefinition.QualifiedName,
                        $"provider/{shiftedDefinition.QualifiedName}") {
                        GenericArity = 0,
                        InterfaceMethodExports = [
                            .. shiftedDefinition.Members.Select(
                                shiftedMember =>
                                    new TrRmSourceInterfaceMethodExport(
                                        shiftedMember.SourceInputOrdinal,
                                        shiftedMember.SourceDeclarationSpan,
                                        $"provider/{shiftedDefinition.QualifiedName}."
                                            + shiftedMember.Name)),
                        ],
                    }));
            return TrRmExecutableCompiler.EmitStandard(providerProgram,
                "provider.module", schemaIdentity, 1, typeExports);
        }

        var providerArtifact = EmitProvider(providerSource);
        const string relocatedProviderSource = """
            using Terrum.Interop;
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Point {
                public Coordinates Position;
                public ptr<int32> Link;
                public ptr<Coordinates> Related;
                public [NativeCallConv(NativeCallingConvention.Cdecl)]
                    fnptr<ptr<Coordinates>, int32> Callback;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Coordinates {
                public int32 X;
                public int32 Y;
            }
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IExtra {
                public unsafe int32 Apply(
                    [NativeCallConv(NativeCallingConvention.Cdecl)]
                    fnptr<float64, float64> callback);
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
        var originalPointTypeDef = checked((int)(
            originalPointDefinition & 0x00FF_FFFF));
        var (originalFirstField, originalEndField)
            = providerTables.TypeDefs.GetFieldRange(originalPointTypeDef);
        var originalCallbackField = Enumerable.Range(originalFirstField,
                originalEndField - originalFirstField)
            .Single(fieldRow =>
                providerTables.Fields.GetName(fieldRow) == "Callback");
        var originalCallbackTypeSpec = providerTables.TypeSpecs.GetTypeSpecRef(
            providerTables.Fields.GetTypeSpecRef(originalCallbackField));
        Assert.That(originalCallbackTypeSpec.Form, Is.EqualTo(0x0D));
        var originalCallbackAnchorMethod
            = providerTables.NativeFunctionPointerAnchors
                .GetAnchorMethodDefRow(originalCallbackTypeSpec.Row);
        var relocatedPointTypeDef = checked((int)(
            relocatedPointDefinition & 0x00FF_FFFF));
        var (relocatedFirstField, relocatedEndField)
            = relocatedProviderTables.TypeDefs.GetFieldRange(
                relocatedPointTypeDef);
        var relocatedCallbackField = Enumerable.Range(relocatedFirstField,
                relocatedEndField - relocatedFirstField)
            .Single(fieldRow =>
                relocatedProviderTables.Fields.GetName(fieldRow)
                    == "Callback");
        var relocatedCallbackTypeSpec
            = relocatedProviderTables.TypeSpecs.GetTypeSpecRef(
                relocatedProviderTables.Fields.GetTypeSpecRef(
                    relocatedCallbackField));
        Assert.That(relocatedCallbackTypeSpec.Form, Is.EqualTo(0x0D));
        var relocatedCallbackAnchorMethod
            = relocatedProviderTables.NativeFunctionPointerAnchors
                .GetAnchorMethodDefRow(relocatedCallbackTypeSpec.Row);
        Assert.That(relocatedCallbackAnchorMethod,
            Is.Not.EqualTo(originalCallbackAnchorMethod));
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
        Assert.That(Enumerable.Range(1, consumerTables.Imports.Count)
            .Any(importRow =>
                (consumerTables.Imports.GetExpectedDefinitionForImportRow(
                    importRow) >> 24) == 0x14
                && consumerTables.Imports.GetBindingForImportRow(importRow)
                    .ExportKey == "provider/Point.Link"),
            Is.True);
        Assert.That(Enumerable.Range(1, consumerTables.Imports.Count)
            .Any(importRow =>
                (consumerTables.Imports.GetExpectedDefinitionForImportRow(
                    importRow) >> 24) == 0x14
                && consumerTables.Imports.GetBindingForImportRow(importRow)
                    .ExportKey == "provider/Point.Related"),
            Is.True);
        Assert.That(Enumerable.Range(1, consumerTables.Imports.Count)
            .Any(importRow =>
                (consumerTables.Imports.GetExpectedDefinitionForImportRow(
                    importRow) >> 24) == 0x14
                && consumerTables.Imports.GetBindingForImportRow(importRow)
                    .ExportKey == "provider/Point.Callback"),
            Is.True);

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
        Assert.That(NativePointerTypes.IsType(reboundPoint.Fields.Single(
            static field => field.Name == "Link").Type), Is.True);
        Assert.That(NativePointerTypes.IsType(reboundPoint.Fields.Single(
            static field => field.Name == "Related").Type), Is.True);
        var reboundRelatedPointerType
            = reboundPoint.Fields.Single(
                static field => field.Name == "Related").Type;
        var reboundRelatedPointee = reboundReference.NativePointerTypes[
            NativePointerTypes.Index(reboundRelatedPointerType)].PointeeType;
        Assert.That((int)reboundRelatedPointee, Is.GreaterThanOrEqualTo(100));
        var reboundCoordinates = reboundReference.Structures[
            (int)reboundRelatedPointee - 100];
        Assert.That(reboundCoordinates.Name, Is.EqualTo("Coordinates"));
        Assert.That(reboundCoordinates.StandardTypeOrigin?.Provider
            .ModuleIdentity, Is.EqualTo("provider.module"));
        var reboundCallbackType = reboundPoint.Fields.Single(
            static field => field.Name == "Callback").Type;
        Assert.That(NativeFunctionPointerTypes.IsType(reboundCallbackType),
            Is.True);
        var reboundCallbackSignature
            = reboundReference.NativeFunctionPointerSignatures[
                NativeFunctionPointerTypes.Index(reboundCallbackType)];
        Assert.That(reboundCallbackSignature.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Cdecl));
        Assert.That(reboundCallbackSignature.ReturnType,
            Is.EqualTo(PrimitiveType.Int32));
        var reboundCallbackParameter
            = reboundCallbackSignature.ParameterTypes.Single();
        Assert.That(NativePointerTypes.IsType(reboundCallbackParameter),
            Is.True);
        var reboundCallbackPointee = reboundReference.NativePointerTypes[
            NativePointerTypes.Index(reboundCallbackParameter)].PointeeType;
        Assert.That((int)reboundCallbackPointee,
            Is.GreaterThanOrEqualTo(100));
        var reboundCallbackCoordinates = reboundReference.Structures[
            (int)reboundCallbackPointee - 100];
        Assert.That(reboundCallbackCoordinates.Name,
            Is.EqualTo("Coordinates"));
        Assert.That(reboundCallbackCoordinates.StandardTypeOrigin?.Provider
            .ModuleIdentity, Is.EqualTo("provider.module"));
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
            .Any(importRow =>
                (reemittedTables.Imports.GetExpectedDefinitionForImportRow(
                    importRow) >> 24) == 0x14
                && reemittedTables.Imports.GetBindingForImportRow(importRow)
                    .ExportKey == "provider/Point.Link"),
            Is.True);
        Assert.That(Enumerable.Range(1, reemittedTables.Imports.Count)
            .Any(importRow =>
                (reemittedTables.Imports.GetExpectedDefinitionForImportRow(
                    importRow) >> 24) == 0x14
                && reemittedTables.Imports.GetBindingForImportRow(importRow)
                    .ExportKey == "provider/Point.Related"),
            Is.True);
        Assert.That(Enumerable.Range(1, reemittedTables.Imports.Count)
            .Any(importRow =>
                (reemittedTables.Imports.GetExpectedDefinitionForImportRow(
                    importRow) >> 24) == 0x14
                && reemittedTables.Imports.GetBindingForImportRow(importRow)
                    .ExportKey == "provider/Point.Callback"),
            Is.True);
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
        Assert.That(reloadedReference.Interfaces.Any(
            static definition => definition.QualifiedName == "IProbe"), Is.True);
        var reloadedPoint = reloadedReference.Structures.Single(
            static structure => structure.Name == "Point"
                && structure.StandardTypeOrigin?.IsLookupRoot == true);
        Assert.That(NativePointerTypes.IsType(reloadedPoint.Fields.Single(
            static field => field.Name == "Link").Type), Is.True);
        Assert.That(NativePointerTypes.IsType(reloadedPoint.Fields.Single(
            static field => field.Name == "Related").Type), Is.True);
        var reloadedRelatedPointerType
            = reloadedPoint.Fields.Single(
                static field => field.Name == "Related").Type;
        var reloadedRelatedPointee = reloadedReference.NativePointerTypes[
            NativePointerTypes.Index(reloadedRelatedPointerType)].PointeeType;
        Assert.That((int)reloadedRelatedPointee, Is.GreaterThanOrEqualTo(100));
        var reloadedCoordinates = reloadedReference.Structures[
            (int)reloadedRelatedPointee - 100];
        Assert.That(reloadedCoordinates.Name, Is.EqualTo("Coordinates"));
        Assert.That(reloadedCoordinates.StandardTypeOrigin?.Provider
            .ModuleIdentity, Is.EqualTo("provider.module"));
        var reloadedCallbackType = reloadedPoint.Fields.Single(
            static field => field.Name == "Callback").Type;
        Assert.That(NativeFunctionPointerTypes.IsType(reloadedCallbackType),
            Is.True);
        var reloadedCallbackSignature
            = reloadedReference.NativeFunctionPointerSignatures[
                NativeFunctionPointerTypes.Index(reloadedCallbackType)];
        Assert.That(reloadedCallbackSignature.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Cdecl));
        Assert.That(reloadedCallbackSignature.ReturnType,
            Is.EqualTo(PrimitiveType.Int32));
        var reloadedCallbackParameter
            = reloadedCallbackSignature.ParameterTypes.Single();
        Assert.That(NativePointerTypes.IsType(reloadedCallbackParameter),
            Is.True);
        var reloadedCallbackPointee = reloadedReference.NativePointerTypes[
            NativePointerTypes.Index(reloadedCallbackParameter)].PointeeType;
        Assert.That((int)reloadedCallbackPointee,
            Is.GreaterThanOrEqualTo(100));
        var reloadedCallbackCoordinates = reloadedReference.Structures[
            (int)reloadedCallbackPointee - 100];
        Assert.That(reloadedCallbackCoordinates.Name,
            Is.EqualTo("Coordinates"));
        Assert.That(reloadedCallbackCoordinates.StandardTypeOrigin?.Provider
            .ModuleIdentity, Is.EqualTo("provider.module"));
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

        var incompatiblePointerArtifact = EmitProvider(
            relocatedProviderSource.Replace("ptr<int32> Link",
                "ptr<float64> Link", StringComparison.Ordinal));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    consumerArtifact), "consumer.module", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatiblePointerArtifact),
                ]));

        var incompatibleCallbackArtifact = EmitProvider(
            relocatedProviderSource.Replace(
                "fnptr<ptr<Coordinates>, int32> Callback",
                "fnptr<ptr<int32>, int32> Callback",
                StringComparison.Ordinal));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    consumerArtifact), "consumer.module", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleCallbackArtifact),
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
        byte[] EmitMiddleProvider(string source)
        {
            var compilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(source), leafReference, options);
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = compilation.Program
                ?? throw new AssertionException(
                    "Expected a bound middle FNI provider.");
            var typeExports = new List<TrRmSourceTypeExport> {
                new("", "Middle", "middle/Middle") {
                    FieldExports = [
                        new TrRmSourceFieldExport("Child",
                            "middle/Middle.Child"),
                    ],
                },
            };
            foreach (var definition in program.Interfaces)
            {
                var exportKey = $"middle/{definition.QualifiedName}";
                typeExports.Add(new TrRmSourceTypeExport("",
                    definition.QualifiedName, exportKey) {
                    GenericArity = 0,
                    InterfaceMethodExports = [
                        .. definition.Members.Select(member =>
                            new TrRmSourceInterfaceMethodExport(
                                member.SourceInputOrdinal,
                                member.SourceDeclarationSpan,
                                $"{exportKey}.{member.Name}")),
                    ],
                });
            }

            return TrRmExecutableCompiler.EmitStandard(program,
                "middle.module", schemaIdentity, 1, typeExports);
        }

        byte[] AddLeadingPrefixModule(byte[] source)
        {
            var sourceIndex = new TrRmArtifactIndex(source, schema);
            var tables = TrRmStructuralMetadataValidator
                .ValidateAndGetExecutionTables(sourceIndex);
            Assert.That(sourceIndex.TryGetSection(0x0001,
                out var moduleSection, out var moduleCount), Is.True);
            Assert.That(moduleCount, Is.EqualTo(1));
            Assert.That(sourceIndex.TryGetSection(0x0010,
                out _, out var moduleRefCount), Is.True);
            Assert.That(moduleRefCount, Is.GreaterThan(0));
            Assert.That(sourceIndex.TryGetSection(0x0013,
                out var typeDefSection, out _), Is.True);

            var prefixTypeDefRow = Enumerable.Range(1,
                    tables.TypeDefs.Count)
                .Single(row => tables.TypeDefs.GetName(row) == "Prefix");
            Assert.That(prefixTypeDefRow, Is.EqualTo(1));
            var dummyIdentityStringRow = BinaryPrimitives
                .ReadInt32LittleEndian(typeDefSection.Slice(
                    ((prefixTypeDefRow - 1) * 44) + 4, 4));
            var originalModuleBytes = moduleSection.ToArray();
            int ReadModuleInt32(int offset)
                => BinaryPrimitives.ReadInt32LittleEndian(
                    originalModuleBytes.AsSpan(offset, 4));
            uint ReadModuleUInt32(int offset)
                => BinaryPrimitives.ReadUInt32LittleEndian(
                    originalModuleBytes.AsSpan(offset, 4));
            var originalModule = new TrRmModuleRow(
                ReadModuleInt32(0), ReadModuleUInt32(8),
                ReadModuleUInt32(12), ReadModuleInt32(16),
                ReadModuleInt32(32), 2,
                ReadModuleInt32(40), ReadModuleInt32(44),
                ReadModuleInt32(24), ReadModuleInt32(28),
                ReadModuleUInt32(4));
            var moduleRows = new[] {
                new TrRmModuleRow(dummyIdentityStringRow,
                    TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
                    0, 0, 1, 1, 1, 1),
                originalModule,
            };
            var moduleBytes = TrRmModuleTableWriter.Encode(moduleRows,
                tables.Strings, tables.Modules.ModuleRefCount,
                tables.Modules.TypeDefCount,
                tables.Modules.MethodDefCount,
                tables.Modules.SourceFileCount, tables.Features.Count);

            var sections = new List<TrRmSection>(
                sourceIndex.SectionCount);
            for (var sectionIndex = 0;
                sectionIndex < sourceIndex.SectionCount;
                sectionIndex++)
            {
                sourceIndex.GetSection(sectionIndex, out var kind,
                    out var flags, out var bytes, out var recordCount);
                if (kind == 0x0001)
                {
                    sections.Add(new TrRmSection(kind, flags, 2,
                        moduleBytes));
                }
                else
                {
                    sections.Add(new TrRmSection(kind, flags,
                        checked((uint)recordCount), bytes.ToArray()));
                }
            }

            var result = TrRmArtifactWriter.Encode(sections, schema);
            _ = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                new TrRmArtifactIndex(result, schema));
            return result;
        }

        var middleArtifact = EmitMiddleProvider(middleSource);
        const string relocatedMiddleSource = """
            using Terrum.Interop;
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Prefix {
                public uint32 Marker;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Middle {
                public Leaf Child;
            }
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(0))]
            public interface IExtra {
                public unsafe int32 Ping(int32 value);
            }
            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            [ForeignNativeInterface(VTableByteOffset: uint32(8))]
            public interface IMiddle {
                public unsafe int32 Read(ptr<Middle> middle);
            }
            """;
        var relocatedMiddleArtifact
            = EmitMiddleProvider(relocatedMiddleSource);
        var coLocatedRelocatedMiddleArtifact
            = AddLeadingPrefixModule(relocatedMiddleArtifact);
        var middleTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                middleArtifact, schema));
        var relocatedMiddleTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                relocatedMiddleArtifact, schema));
        Assert.That(middleTables.Exports.TryGetDefinitionForKey(1,
            "middle/Middle", out var middleDefinitionId), Is.True);
        Assert.That(relocatedMiddleTables.Exports.TryGetDefinitionForKey(1,
            "middle/Middle", out var relocatedMiddleDefinitionId), Is.True);
        Assert.That(relocatedMiddleDefinitionId,
            Is.Not.EqualTo(middleDefinitionId));
        Assert.That(middleTables.Exports.TryGetDefinitionForKey(1,
            "middle/IMiddle.Read", out var middleMethodDefinitionId), Is.True);
        Assert.That(relocatedMiddleTables.Exports.TryGetDefinitionForKey(1,
            "middle/IMiddle.Read",
            out var relocatedMiddleMethodDefinitionId), Is.True);
        Assert.That(relocatedMiddleMethodDefinitionId,
            Is.Not.EqualTo(middleMethodDefinitionId));
        Assert.That(relocatedMiddleTables.Exports.TryGetDefinitionForKey(1,
            "middle/IExtra.Ping", out _), Is.True);
        var coLocatedMiddleTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                coLocatedRelocatedMiddleArtifact, schema));
        Assert.That(coLocatedMiddleTables.Modules.GetIdentity(1),
            Is.EqualTo("Prefix"));
        Assert.That(coLocatedMiddleTables.Modules.GetIdentity(2),
            Is.EqualTo("middle.module"));
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
        var relocatedProviderReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    consumerArtifact), "consumer.module", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        leafArtifact),
                    new TrRmStandardArtifactInput(schemaIdentity,
                        coLocatedRelocatedMiddleArtifact),
                ]);
        var relocatedMiddle = relocatedProviderReference.Structures.Single(
            static structure => structure.Name == "Middle");
        Assert.That(relocatedMiddle.StandardTypeOrigin!.Provider
            .ModuleIdentity, Is.EqualTo("middle.module"));
        Assert.That(relocatedMiddle.StandardTypeOrigin.DefinitionId,
            Is.EqualTo(relocatedMiddleDefinitionId));
        var relocatedConsumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                int32 Invoke(ptr<void> receiver, ptr<Middle> middle) {
                    unsafe { return IMiddle.Read(receiver, middle); }
                }
                """), relocatedProviderReference);
        Assert.That(relocatedConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                relocatedConsumerCompilation.Diagnostics));
        var relocatedReemissionCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(""), relocatedProviderReference);
        Assert.That(relocatedReemissionCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                relocatedReemissionCompilation.Diagnostics));
        var relocatedReemittedArtifact = TrRmExecutableCompiler.EmitStandard(
            relocatedReemissionCompilation.Program
                ?? throw new AssertionException(
                    "Expected a relocated source-free FNI consumer."),
            "relocated.forwarder", schemaIdentity, 1, []);
        var reloadedRelocatedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    relocatedReemittedArtifact), "relocated.forwarder", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        coLocatedRelocatedMiddleArtifact),
                    new TrRmStandardArtifactInput(schemaIdentity,
                        leafArtifact),
                ]);
        Assert.That(reloadedRelocatedReference.Structures.Single(
            static structure => structure.Name == "Middle")
            .StandardTypeOrigin!.DefinitionId,
            Is.EqualTo(relocatedMiddleDefinitionId));
        var reloadedRelocatedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                int32 Invoke(ptr<void> receiver, ptr<Middle> middle) {
                    unsafe { return IMiddle.Read(receiver, middle); }
                }
                """), reloadedRelocatedReference);
        Assert.That(reloadedRelocatedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reloadedRelocatedCompilation.Diagnostics));
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
        var middleModuleRef = consumerTables.Imports
            .GetModuleRefForImportRow(middleImportRow);
        var wrongLeafModuleRefBytes = importBytesArray.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(
            wrongLeafModuleRefBytes.AsSpan(leafImportOffset, 4),
            middleModuleRef);
        var wrongLeafModuleRefArtifact = RewriteSection(consumerArtifact,
            0x001C, wrongLeafModuleRefBytes, importCount);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    wrongLeafModuleRefArtifact), "consumer.module", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        middleArtifact),
                    new TrRmStandardArtifactInput(schemaIdentity,
                        leafArtifact),
                ]));

        var wrongLeafTypeDefImportBytes = importBytesArray.ToArray();
        var middleExpectedDefinitionId = consumerTables.Imports
            .GetExpectedDefinitionForImportRow(middleImportRow);
        BinaryPrimitives.WriteUInt32LittleEndian(
            wrongLeafTypeDefImportBytes.AsSpan(leafImportOffset + 8, 4),
            middleExpectedDefinitionId);
        var wrongLeafTypeDefImportArtifact = RewriteSection(
            consumerArtifact, 0x001C, wrongLeafTypeDefImportBytes,
            importCount);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    wrongLeafTypeDefImportArtifact), "consumer.module", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        middleArtifact),
                    new TrRmStandardArtifactInput(schemaIdentity,
                        leafArtifact),
                ]));

        var leafModuleRef = consumerTables.Imports
            .GetModuleRefForImportRow(leafImportRow);
        Assert.That(middleModuleRef, Is.Not.EqualTo(leafModuleRef));
        var wrongOwnerModuleRefBytes = moduleRefBytes.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(
            wrongOwnerModuleRefBytes.AsSpan((leafModuleRef - 1) * 12, 4), 0);
        var wrongOwnerModuleRefArtifact = RewriteSection(consumerArtifact,
            0x0010, wrongOwnerModuleRefBytes, moduleRefCount);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    wrongOwnerModuleRefArtifact), "consumer.module", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        middleArtifact),
                    new TrRmStandardArtifactInput(schemaIdentity,
                        leafArtifact),
                ]));

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

        byte[] RewriteSurface(byte[] replacement, int rowCount)
        {
            var sourceIndex = new TrRmArtifactIndex(artifact, schema);
            var sections = new List<TrRmSection>(sourceIndex.SectionCount);
            for (var sectionIndex = 0;
                sectionIndex < sourceIndex.SectionCount;
                sectionIndex++)
            {
                sourceIndex.GetSection(sectionIndex, out var kind,
                    out var flags, out var bytes, out var count);
                sections.Add(kind == 0x0066
                    ? new TrRmSection(kind, flags,
                        checked((uint)rowCount), replacement)
                    : new TrRmSection(kind, flags,
                        checked((uint)count), bytes.ToArray()));
            }

            return TrRmArtifactWriter.Encode(sections, schema);
        }

        void AssertMalformedSurface(byte[] replacement, int rowCount)
            => Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler.CreateStandardReference(
                    new TrRmStandardArtifactInput(schemaIdentity,
                        RewriteSurface(replacement, rowCount)),
                    "provider.module", []));

        AssertMalformedSurface(surfaceBytes[..16].ToArray(), 1);
        var duplicateOrdinalSurface = surfaceBytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            duplicateOrdinalSurface.AsSpan(20, sizeof(uint)), 0);
        AssertMalformedSurface(duplicateOrdinalSurface, surfaceCount);
        var duplicateNameSurface = surfaceBytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            duplicateNameSurface.AsSpan(24, sizeof(uint)),
            checked((uint)valueNameRow));
        AssertMalformedSurface(duplicateNameSurface, surfaceCount);
        var invalidDefaultSurface = surfaceBytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            invalidDefaultSurface.AsSpan(28, sizeof(uint)), 0x00FF_FFFF);
        AssertMalformedSurface(invalidDefaultSurface, surfaceCount);
    }

    [Test]
    public void StandardForeignNativeInterfaceParameterSurfaceRemapsProviderRows()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-native-interface-parameter-surface-1";
        const string probeSource = """
            [Terrum.Interop.NativeCallable]
            [Terrum.Interop.ForeignNativeInterface(
                VTableByteOffset: uint32(0))]
            public interface IProbe {
                public unsafe int32 Read(ptr<int32> source,
                    int32 extra = 1);
            }
            """;
        const string shiftedProviderSource = """
            [Terrum.Interop.NativeCallable]
            [Terrum.Interop.ForeignNativeInterface(
                VTableByteOffset: uint32(0))]
            public interface IExtra {
                public unsafe int32 Prefix(ptr<float64> input,
                    int32 offset = 0);
                public unsafe int32 Wide(int64 value);
            }

            [Terrum.Interop.NativeCallable]
            [Terrum.Interop.ForeignNativeInterface(
                VTableByteOffset: uint32(0))]
            public interface IProbe {
                public unsafe int32 Read(ptr<int32> data,
                    int32 extra = 2);
            }
            """;
        var options = new CompilationOptions {
            NativeCoreRegistration = CreateRegistration(
                includeForeignNativeInterface: true),
        };

        byte[] EmitProvider(string source)
        {
            var compilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(source), options);
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    compilation.Diagnostics));
            var program = compilation.Program
                ?? throw new AssertionException(
                    "Expected a bound FNI provider.");
            var exports = program.Interfaces.Select(definition =>
                new TrRmSourceTypeExport("", definition.QualifiedName,
                    $"provider/{definition.QualifiedName}") {
                    GenericArity = 0,
                    InterfaceMethodExports = [
                        .. definition.Members.Select(member =>
                            new TrRmSourceInterfaceMethodExport(
                                member.SourceInputOrdinal,
                                member.SourceDeclarationSpan,
                                $"provider/{definition.QualifiedName}."
                                    + member.Name)),
                    ],
                }).ToArray();
            return TrRmExecutableCompiler.EmitStandard(program,
                "provider.module", schemaIdentity, 1, exports);
        }

        var providerArtifact = EmitProvider(probeSource);
        var shiftedProviderArtifact = EmitProvider(shiftedProviderSource);
        var schema
            = TrRmArtifactSchema.ForeignNativeInterfaceParameterSurfaceDraft;
        var providerTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                providerArtifact, schema));
        var shiftedProviderTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                shiftedProviderArtifact, schema));
        int FindReadMethodRow(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables)
            => Enumerable.Range(1, tables.Methods.Count).Single(row =>
                tables.Methods.GetName(row) == "Read");
        var providerReadRow = FindReadMethodRow(providerTables);
        var shiftedReadRow = FindReadMethodRow(shiftedProviderTables);
        Assert.That(shiftedReadRow, Is.GreaterThan(providerReadRow));
        Assert.That(providerTables.MethodParameterLabels
            .TryGetNameStringRow(providerReadRow, 0, out var providerNameRow),
            Is.True);
        Assert.That(providerTables.Strings.DecodeName(providerNameRow),
            Is.EqualTo("source"));
        Assert.That(shiftedProviderTables.MethodParameterLabels
            .TryGetNameStringRow(shiftedReadRow, 0,
                out var shiftedNameRow), Is.True);
        Assert.That(shiftedProviderTables.Strings.DecodeName(shiftedNameRow),
            Is.EqualTo("data"));
        Assert.That(shiftedNameRow, Is.Not.EqualTo(providerNameRow));
        Assert.That(providerTables.MethodParameterLabels
            .TryGetDefaultConstantSourceRow(providerReadRow, 1,
                out var providerDefaultRow), Is.True);
        Assert.That(shiftedProviderTables.MethodParameterLabels
            .TryGetDefaultConstantSourceRow(shiftedReadRow, 1,
                out var shiftedDefaultRow), Is.True);
        Assert.That(shiftedDefaultRow,
            Is.Not.EqualTo(providerDefaultRow));
        AssertDefaultPayload(providerTables, providerDefaultRow, 1);
        AssertDefaultPayload(shiftedProviderTables, shiftedDefaultRow, 2);

        byte[] RewriteSection(byte[] source, ushort sectionKind,
            byte[] replacement, int replacementCount)
        {
            var sourceIndex = new TrRmArtifactIndex(source, schema);
            var sections = new List<TrRmSection>(
            sourceIndex.SectionCount);
            for (var sectionIndex = 0;
            sectionIndex < sourceIndex.SectionCount;
            sectionIndex++)
            {
            sourceIndex.GetSection(sectionIndex, out var kind,
                out var flags, out var bytes, out var count);
            sections.Add(kind == sectionKind
                ? new TrRmSection(kind, flags,
                    checked((uint)replacementCount), replacement)
                : new TrRmSection(kind, flags,
                    checked((uint)count), bytes.ToArray()));
            }

            return TrRmArtifactWriter.Encode(sections, schema);
        }

        var shiftedProviderIndex = new TrRmArtifactIndex(
            shiftedProviderArtifact, schema);
        Assert.That(shiftedProviderIndex.TryGetSection(0x0018,
            out var parameterBytes, out var parameterCount), Is.True);
        var wideMethodRow = Enumerable.Range(1,
            shiftedProviderTables.Methods.Count)
            .Single(row =>
            shiftedProviderTables.Methods.GetName(row) == "Wide");
        var wideSignatureRow
            = shiftedProviderTables.Methods.GetSignature(wideMethodRow);
        var (wideParameterStart, wideParameterCount)
            = shiftedProviderTables.Signatures.GetParamRange(
            wideSignatureRow);
        Assert.That(wideParameterCount, Is.EqualTo(1));
        var wideTypeSpecRef = shiftedProviderTables.Parameters.GetTypeSpec(
            wideParameterStart);
        var readSignatureRow
            = shiftedProviderTables.Methods.GetSignature(shiftedReadRow);
        var (readParameterStart, readParameterCount)
            = shiftedProviderTables.Signatures.GetParamRange(
            readSignatureRow);
        Assert.That(readParameterCount, Is.EqualTo(2));
        var mismatchedParameterBytes = parameterBytes.ToArray();
        var readDefaultParameterOffset = checked(
            (readParameterStart * 16) + 8);
        BinaryPrimitives.WriteUInt32LittleEndian(
            mismatchedParameterBytes.AsSpan(
            readDefaultParameterOffset, sizeof(uint)),
            wideTypeSpecRef);
        var mismatchedDefaultArtifact = RewriteSection(
            shiftedProviderArtifact, 0x0018,
            mismatchedParameterBytes, parameterCount);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity,
                mismatchedDefaultArtifact), "provider.module", []));

        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var shiftedProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, shiftedProviderArtifact);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "provider.module", []);
        const string consumerSource = """
            int32 Invoke(ptr<void> receiver, ptr<int32> source) {
                unsafe { return IProbe.Read(receiver, source); }
            }
            """;
        const string replacementConsumerSource = """
            int32 Invoke(ptr<void> receiver, ptr<int32> source) {
                unsafe { return IProbe.Read(receiver, data: source); }
            }
            """;
        const string staleLabelConsumerSource = """
            int32 Invoke(ptr<void> receiver, ptr<int32> source) {
                unsafe { return IProbe.Read(receiver, source: source); }
            }
            """;
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(consumerSource), providerReference, options);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerCompilation.Program
                ?? throw new AssertionException(
                    "Expected a bound FNI consumer."),
            "consumer.module", schemaIdentity, 1, []);
        var consumerInput = new TrRmStandardArtifactInput(schemaIdentity,
            consumerArtifact);
        var replacementReference
            = TrRmExecutableCompiler.CreateStandardReference(
                consumerInput, "consumer.module", [shiftedProviderInput]);
        var replacementCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(replacementConsumerSource),
            replacementReference, options);
        Assert.That(replacementCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                replacementCompilation.Diagnostics));
        var replacementProgram = replacementCompilation.Program
            ?? throw new AssertionException(
                "Expected a provider-replaced FNI consumer.");
        AssertReadSurface(replacementProgram, "data", 2);
        AssertReadCallDefault(replacementProgram, 2);
        var staleLabelCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(staleLabelConsumerSource),
            replacementReference, options);
        Assert.That(staleLabelCompilation.HasErrors, Is.True);

        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(""), replacementReference, options);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(
            sourceFreeCompilation.Program
                ?? throw new AssertionException(
                    "Expected a source-free FNI consumer."),
            "forwarder.module", schemaIdentity, 1, []);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                reemittedArtifact, schema));
        var reemittedReadRow = FindReadMethodRow(reemittedTables);
        Assert.That(reemittedTables.MethodParameterLabels
            .TryGetNameStringRow(reemittedReadRow, 0,
                out var reemittedNameRow), Is.True);
        Assert.That(reemittedTables.Strings.DecodeName(reemittedNameRow),
            Is.EqualTo("source"));
        Assert.That(reemittedTables.MethodParameterLabels
            .TryGetDefaultConstantSourceRow(reemittedReadRow, 1,
                out var reemittedDefaultRow), Is.True);
        AssertDefaultPayload(reemittedTables, reemittedDefaultRow, 1);
        var reloadedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    reemittedArtifact), "forwarder.module",
                [shiftedProviderInput]);
        var reloadedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(""), reloadedReference, options);
        Assert.That(reloadedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reloadedCompilation.Diagnostics));
        var reloadedProgram = reloadedCompilation.Program
            ?? throw new AssertionException(
                "Expected the reloaded source-free FNI consumer.");
        AssertReadSurface(reloadedProgram, "data", 2);
        AssertReadCallDefault(reloadedProgram, 1);

        var sourceFreeProgram = sourceFreeCompilation.Program
            ?? throw new AssertionException(
                "Expected a source-free FNI consumer for multiple views.");
        var providerViewIndex = Enumerable.Range(0,
                sourceFreeProgram.Interfaces.Length)
            .Single(index => sourceFreeProgram.Interfaces[index]
                .QualifiedName == "IProbe");
        var providerView = sourceFreeProgram.Interfaces[providerViewIndex];
        var expectedView
            = providerView.ForeignNativeInterfaceExpectedViews.Single();
        var expectedRead = expectedView.Definition.Members.Single(
            static member => member.Name == "Read");
        var alternateExpectedRead = expectedRead with {
            Parameters = expectedRead.Parameters
                .SetItem(0, expectedRead.Parameters[0] with {
                    Name = "input",
                })
                .SetItem(1, expectedRead.Parameters[1] with {
                    DefaultValue = null,
                }),
        };
        var alternateExpectedDefinition = expectedView.Definition with {
            Members = expectedView.Definition.Members.SetItem(
                expectedRead.Ordinal, alternateExpectedRead),
        };
        var alternateExpectedView = expectedView with {
            SourceTypeDefRow = checked(expectedView.SourceTypeDefRow + 1),
            Definition = alternateExpectedDefinition,
        };
        var multipleViewProgram = sourceFreeProgram with {
            Interfaces = sourceFreeProgram.Interfaces.SetItem(
                providerViewIndex, providerView with {
                    ForeignNativeInterfaceExpectedViews = [
                        .. providerView.ForeignNativeInterfaceExpectedViews,
                        alternateExpectedView,
                    ],
                }),
        };
        var multipleViewArtifact = TrRmExecutableCompiler.EmitStandard(
            multipleViewProgram, "multiple-view.module", schemaIdentity, 1,
            []);
        AssertReadSurfaceRows(multipleViewArtifact, schema,
            ("source", (byte?)1), ("input", null));
        var multipleViewReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    multipleViewArtifact), "multiple-view.module",
                [shiftedProviderInput]);
        var retainedViews = multipleViewReference.Interfaces.Single(
                static definition =>
                    definition.QualifiedName == "IProbe")
            .ForeignNativeInterfaceExpectedViews;
        Assert.That(retainedViews.Length, Is.EqualTo(2));
        Assert.That(retainedViews.Select(static view =>
                view.Definition.Members.Single(
                    static member => member.Name == "Read")
                    .Parameters[0].Name),
            Is.EqualTo(new[] { "source", "input" }));
        Assert.That(retainedViews[0].Definition.Members
                .Single(static member => member.Name == "Read")
                .Parameters[1].DefaultValue?.Value,
            Is.EqualTo(1L));
        Assert.That(retainedViews[1].Definition.Members
            .Single(static member => member.Name == "Read")
            .Parameters[1].DefaultValue, Is.Null);

        var multipleViewCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(""), multipleViewReference, options);
        Assert.That(multipleViewCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                multipleViewCompilation.Diagnostics));
        var multipleViewProgramForEmission
            = multipleViewCompilation.Program
                ?? throw new AssertionException(
                    "Expected the source-free multiple-view FNI consumer.");
        AssertReadSurface(multipleViewProgramForEmission, "data", 2);
        AssertReadCallDefault(multipleViewProgramForEmission, 1);
        var multipleViewReemittedArtifact
            = TrRmExecutableCompiler.EmitStandard(
                multipleViewProgramForEmission, "multiple-view-forwarder.module",
                schemaIdentity, 1, []);
        AssertReadSurfaceRows(multipleViewReemittedArtifact, schema,
            ("source", (byte?)1), ("input", null));
        var multipleViewReloadedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    multipleViewReemittedArtifact),
                "multiple-view-forwarder.module", [shiftedProviderInput]);
        var multipleViewReloadedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(""), multipleViewReloadedReference, options);
        Assert.That(multipleViewReloadedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                multipleViewReloadedCompilation.Diagnostics));
        var multipleViewReloadedProgram
            = multipleViewReloadedCompilation.Program
                ?? throw new AssertionException(
                    "Expected the reloaded multiple-view FNI consumer.");
        AssertReadSurface(multipleViewReloadedProgram, "data", 2);
        AssertReadCallDefault(multipleViewReloadedProgram, 1);
        Assert.That(multipleViewReloadedReference.Interfaces.Single(
                static definition =>
                    definition.QualifiedName == "IProbe")
            .ForeignNativeInterfaceExpectedViews.Length,
            Is.EqualTo(2));

        static void AssertReadSurface(BoundProgram program,
            string expectedLabel, long expectedDefault)
        {
            var member = program.Interfaces.Single(definition =>
                    definition.QualifiedName == "IProbe")
                .Members.Single(candidate => candidate.Name == "Read");
            Assert.That(member.Parameters.Select(static parameter =>
                    parameter.Name),
                Is.EqualTo(new[] { expectedLabel, "extra" }));
            Assert.That(member.Parameters[1].DefaultValue?.Value,
                Is.EqualTo(expectedDefault));
        }

        static void AssertDefaultPayload(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            uint constantSourceRow, byte expectedValue)
        {
            var frozenValueNode = checked((int)tables.ConstantSources
                .GetPayloadReference(checked((int)constantSourceRow)));
            Assert.That(tables.FrozenValueNodes.GetScalarPayload(
                    frozenValueNode, tables.Blobs).ToArray(),
                Is.EqualTo(new byte[] { expectedValue, 0, 0, 0 }));
        }

        static void AssertReadSurfaceRows(byte[] artifact,
            TrRmArtifactSchema schema,
            params (string FirstParameter, byte? ExtraDefault)[]
                expectedSurfaces)
        {
            var tables = TrRmStructuralMetadataValidator
                .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                    artifact, schema));
            var readRows = Enumerable.Range(1, tables.Methods.Count)
                .Where(row => tables.Methods.GetName(row) == "Read")
                .ToArray();
            Assert.That(readRows, Has.Length.EqualTo(
                expectedSurfaces.Length));
            for (var index = 0; index < readRows.Length; index++)
            {
                var methodRow = readRows[index];
                Assert.That(tables.MethodParameterLabels
                    .TryGetNameStringRow(methodRow, 0,
                        out var nameStringRow), Is.True);
                Assert.That(tables.Strings.DecodeName(nameStringRow),
                    Is.EqualTo(expectedSurfaces[index].FirstParameter));
                Assert.That(tables.MethodParameterLabels
                    .TryGetNameStringRow(methodRow, 1,
                        out var defaultParameterNameRow), Is.True);
                Assert.That(tables.Strings.DecodeName(
                        defaultParameterNameRow),
                    Is.EqualTo("extra"));
                Assert.That(tables.MethodParameterLabels
                    .TryGetDefaultConstantSourceRow(methodRow, 1,
                        out var defaultConstantSourceRow), Is.True);
                if (expectedSurfaces[index].ExtraDefault is byte expectedDefault)
                {
                    Assert.That(defaultConstantSourceRow, Is.Not.Zero);
                    AssertDefaultPayload(tables, defaultConstantSourceRow,
                        expectedDefault);
                }
                else
                {
                    Assert.That(defaultConstantSourceRow, Is.Zero);
                }
            }
        }

        static void AssertReadCallDefault(BoundProgram program,
            long expectedDefault)
        {
            var invoke = program.Functions.Single(function =>
                function.Name == "Invoke");
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
            Assert.That(calls, Has.Count.EqualTo(1));
            Assert.That(calls[0].Arguments, Has.Count.EqualTo(2));
            Assert.That(((BoundConstant)calls[0].Arguments[1]).Value,
                Is.EqualTo(expectedDefault));
        }
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

    private static async Task AssertNestedAggregatePayloadOutputAsync(
        byte[] image, string functionName)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"native-fni-nested-aggregate-output-{Guid.NewGuid():N}.dll");
        var module = IntPtr.Zero;
        var receiver = IntPtr.Zero;
        var vtable = IntPtr.Zero;
        var source = IntPtr.Zero;
        var tagSource = IntPtr.Zero;
        NativeTagFunction? tagCallback = null;
        NativeOutputTaggedPayloadFunction? outputCallback = null;
        var tagCalls = 0;
        var payloadCalls = 0;
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            module = NativeLibrary.Load(path);
            receiver = Marshal.AllocHGlobal(IntPtr.Size);
            vtable = Marshal.AllocHGlobal(2 * IntPtr.Size);
            source = Marshal.AllocHGlobal(sizeof(int));
            tagSource = Marshal.AllocHGlobal(sizeof(int));
            tagCallback = (actualReceiver, actualSource) => {
                if (actualReceiver != receiver || actualSource != tagSource)
                {
                    return 0;
                }

                tagCalls++;
                return Marshal.ReadInt32(actualSource);
            };
            outputCallback = (actualReceiver, actualSource, tag, mode,
                value) => {
                payloadCalls++;
                if (actualReceiver != receiver || actualSource != source
                    || mode != 4
                    || tagCalls != payloadCalls)
                {
                    return 0;
                }

                if (tag == 0)
                {
                    Marshal.WriteInt64(value, -1);
                    return 0;
                }
                if (tag != 7)
                {
                    return 0;
                }

                Marshal.WriteInt64(value, 11);
                Marshal.WriteInt64(value, sizeof(long), 22);
                Marshal.WriteInt64(value, 2 * sizeof(long), 33);
                return 1;
            };
            Marshal.WriteIntPtr(vtable,
                Marshal.GetFunctionPointerForDelegate(tagCallback));
            Marshal.WriteIntPtr(vtable, IntPtr.Size,
                Marshal.GetFunctionPointerForDelegate(outputCallback));
            Marshal.WriteIntPtr(receiver, vtable);
            Marshal.WriteInt32(source, 1);
            Marshal.WriteInt32(tagSource, 7);

            var invoke = Marshal.GetDelegateForFunctionPointer<
                NativeAggregateNestedPayloadFunction>(
                    NativeLibrary.GetExport(module, functionName));
            var fallback = new NativeAggregateNestedPayload {
                Inner = new NativeAggregateNestedInner {
                    X = 4,
                    Y = 5,
                },
                Z = 6,
            };
            var result = invoke(receiver, source, tagSource, 4, fallback);
            Assert.That(result.Inner.X, Is.EqualTo(11));
            Assert.That(result.Inner.Y, Is.EqualTo(22));
            Assert.That(result.Z, Is.EqualTo(33));
            Assert.That(tagCalls, Is.EqualTo(1));
            Assert.That(payloadCalls, Is.EqualTo(1));

            Marshal.WriteInt32(tagSource, 0);
            result = invoke(receiver, source, tagSource, 4, fallback);
            Assert.That(result.Inner.X, Is.EqualTo(fallback.Inner.X));
            Assert.That(result.Inner.Y, Is.EqualTo(fallback.Inner.Y));
            Assert.That(result.Z, Is.EqualTo(fallback.Z));
            Assert.That(tagCalls, Is.EqualTo(2));
            Assert.That(payloadCalls, Is.EqualTo(2));
            GC.KeepAlive(tagCallback);
            GC.KeepAlive(outputCallback);
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
            if (tagSource != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(tagSource);
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

    private static async Task AssertLiteralAggregatePayloadOutputAsync(
        byte[] image, string functionName)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"native-fni-literal-aggregate-output-{Guid.NewGuid():N}.dll");
        var module = IntPtr.Zero;
        var receiver = IntPtr.Zero;
        var vtable = IntPtr.Zero;
        var source = IntPtr.Zero;
        NativeOutputTaggedPayloadFunction? callback = null;
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            module = NativeLibrary.Load(path);
            receiver = Marshal.AllocHGlobal(IntPtr.Size);
            vtable = Marshal.AllocHGlobal(2 * IntPtr.Size);
            source = Marshal.AllocHGlobal(sizeof(int));
            callback = (actualReceiver, actualSource, tag, mode, value) => {
                if (actualReceiver != receiver || actualSource != source
                    || tag != 7 || mode != 9)
                {
                    return 0;
                }

                if (Marshal.ReadInt32(actualSource) == 0)
                {
                    Marshal.WriteInt64(value, -1);
                    return 0;
                }

                Marshal.WriteInt64(value, 11);
                Marshal.WriteInt64(value, sizeof(long), 22);
                Marshal.WriteInt64(value, 2 * sizeof(long), 33);
                return 1;
            };
            Marshal.WriteIntPtr(vtable, IntPtr.Zero);
            Marshal.WriteIntPtr(vtable, IntPtr.Size,
                Marshal.GetFunctionPointerForDelegate(callback));
            Marshal.WriteIntPtr(receiver, vtable);
            Marshal.WriteInt32(source, 1);

            var invoke = Marshal.GetDelegateForFunctionPointer<
                NativeAggregateLiteralPayloadFunction>(
                    NativeLibrary.GetExport(module, functionName));
            var fallback = new NativeAggregateNestedPayload {
                Inner = new NativeAggregateNestedInner {
                    X = 4,
                    Y = 5,
                },
                Z = 6,
            };
            var result = invoke(receiver, source, fallback);
            Assert.That(result.Inner.X, Is.EqualTo(11));
            Assert.That(result.Inner.Y, Is.EqualTo(22));
            Assert.That(result.Z, Is.EqualTo(33));

            Marshal.WriteInt32(source, 0);
            result = invoke(receiver, source, fallback);
            Assert.That(result.Inner.X, Is.EqualTo(fallback.Inner.X));
            Assert.That(result.Inner.Y, Is.EqualTo(fallback.Inner.Y));
            Assert.That(result.Z, Is.EqualTo(fallback.Z));
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

    private static async Task AssertPointerOutputAsync(byte[] image,
        string functionName, int successResult = 1, int failureResult = 0)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"native-fni-pointer-output-{Guid.NewGuid():N}.dll");
        var module = IntPtr.Zero;
        var receiver = IntPtr.Zero;
        var vtable = IntPtr.Zero;
        var source = IntPtr.Zero;
        var fallback = IntPtr.Zero;
        NativePointerOutputCallback? callback = null;
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            module = NativeLibrary.Load(path);
            receiver = Marshal.AllocHGlobal(IntPtr.Size);
            vtable = Marshal.AllocHGlobal(IntPtr.Size);
            source = Marshal.AllocHGlobal(sizeof(int));
            fallback = Marshal.AllocHGlobal(sizeof(int));
            var succeed = true;
            callback = (IntPtr actualReceiver, IntPtr actualSource, int tag,
                out IntPtr value) => {
                if (actualReceiver != receiver || actualSource != source
                    || tag != 7)
                {
                    value = IntPtr.Zero;
                    return 0;
                }

                value = source;
                return succeed ? successResult : failureResult;
            };
            Marshal.WriteIntPtr(vtable,
                Marshal.GetFunctionPointerForDelegate(callback));
            Marshal.WriteIntPtr(receiver, vtable);
            Marshal.WriteInt32(source, 17);
            Marshal.WriteInt32(fallback, 42);

            var invoke = Marshal.GetDelegateForFunctionPointer<
                NativePointerOutputFunction>(
                    NativeLibrary.GetExport(module, functionName));
            var result = invoke(receiver, source, 7, fallback);
            Assert.That(result, Is.EqualTo(source));
            Assert.That(Marshal.ReadInt32(result), Is.EqualTo(17));

            succeed = false;
            result = invoke(receiver, source, 7, fallback);
            Assert.That(result, Is.EqualTo(fallback));
            Assert.That(Marshal.ReadInt32(result), Is.EqualTo(42));
            GC.KeepAlive(callback);
        }
        finally
        {
            if (module != IntPtr.Zero)
            {
                NativeLibrary.Free(module);
            }
            if (fallback != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(fallback);
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

    private static async Task AssertFunctionPointerOutputAsync(byte[] image,
        string functionName)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"native-fni-function-pointer-output-{Guid.NewGuid():N}.dll");
        var module = IntPtr.Zero;
        var receiver = IntPtr.Zero;
        var vtable = IntPtr.Zero;
        var source = IntPtr.Zero;
        NativePointerOutputCallback? callback = null;
        NativeFunctionPointerTarget? target = null;
        NativeFunctionPointerTarget? fallback = null;
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            module = NativeLibrary.Load(path);
            receiver = Marshal.AllocHGlobal(IntPtr.Size);
            vtable = Marshal.AllocHGlobal(IntPtr.Size);
            source = Marshal.AllocHGlobal(sizeof(int));
            target = static value => value + 37;
            fallback = static value => value + 42;
            var targetPointer = Marshal.GetFunctionPointerForDelegate(target);
            var fallbackPointer
                = Marshal.GetFunctionPointerForDelegate(fallback);
            var succeed = true;
            callback = (IntPtr actualReceiver, IntPtr actualSource, int tag,
                out IntPtr value) => {
                if (actualReceiver != receiver || actualSource != source
                    || tag != 7)
                {
                    value = IntPtr.Zero;
                    return 0;
                }

                value = targetPointer;
                return succeed ? 1 : 0;
            };
            Marshal.WriteIntPtr(vtable,
                Marshal.GetFunctionPointerForDelegate(callback));
            Marshal.WriteIntPtr(receiver, vtable);

            var invoke = Marshal.GetDelegateForFunctionPointer<
                NativePointerOutputFunction>(
                    NativeLibrary.GetExport(module, functionName));
            var result = invoke(receiver, source, 7, fallbackPointer);
            Assert.That(result, Is.EqualTo(targetPointer));
            Assert.That(Marshal.GetDelegateForFunctionPointer<
                NativeFunctionPointerTarget>(result)(5), Is.EqualTo(42));

            succeed = false;
            result = invoke(receiver, source, 7, fallbackPointer);
            Assert.That(result, Is.EqualTo(fallbackPointer));
            Assert.That(Marshal.GetDelegateForFunctionPointer<
                NativeFunctionPointerTarget>(result)(5), Is.EqualTo(47));
            GC.KeepAlive(callback);
            GC.KeepAlive(target);
            GC.KeepAlive(fallback);
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

    private static async Task AssertMultipleFunctionPointerOutputAsync(
        byte[] image, string functionName)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"native-fni-multiple-function-pointer-output-{Guid.NewGuid():N}.dll");
        var module = IntPtr.Zero;
        var receiver = IntPtr.Zero;
        var vtable = IntPtr.Zero;
        var source = IntPtr.Zero;
        NativeFunctionPointerMultiOutputCallback? callback = null;
        NativeFunctionPointerTarget? target = null;
        NativeFunctionPointerTarget? fallback = null;
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            module = NativeLibrary.Load(path);
            receiver = Marshal.AllocHGlobal(IntPtr.Size);
            vtable = Marshal.AllocHGlobal(IntPtr.Size);
            source = Marshal.AllocHGlobal(sizeof(int));
            target = static value => value + 37;
            fallback = static value => value + 42;
            var targetPointer = Marshal.GetFunctionPointerForDelegate(target);
            var fallbackPointer
                = Marshal.GetFunctionPointerForDelegate(fallback);
            var succeed = true;
            callback = (IntPtr actualReceiver, IntPtr actualSource, int tag,
                out int scalarValue, out IntPtr functionPointerValue) => {
                if (actualReceiver != receiver || actualSource != source
                    || tag != 7)
                {
                    scalarValue = 0;
                    functionPointerValue = IntPtr.Zero;
                    return 0;
                }

                scalarValue = 11;
                functionPointerValue = targetPointer;
                return succeed ? 1 : 0;
            };
            Marshal.WriteIntPtr(vtable,
                Marshal.GetFunctionPointerForDelegate(callback));
            Marshal.WriteIntPtr(receiver, vtable);

            var invoke = Marshal.GetDelegateForFunctionPointer<
                NativePointerOutputFunction>(
                    NativeLibrary.GetExport(module, functionName));
            var result = invoke(receiver, source, 7, fallbackPointer);
            Assert.That(result, Is.EqualTo(targetPointer));
            Assert.That(Marshal.GetDelegateForFunctionPointer<
                NativeFunctionPointerTarget>(result)(5), Is.EqualTo(42));

            succeed = false;
            result = invoke(receiver, source, 7, fallbackPointer);
            Assert.That(result, Is.EqualTo(fallbackPointer));
            Assert.That(Marshal.GetDelegateForFunctionPointer<
                NativeFunctionPointerTarget>(result)(5), Is.EqualTo(47));
            GC.KeepAlive(callback);
            GC.KeepAlive(target);
            GC.KeepAlive(fallback);
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
