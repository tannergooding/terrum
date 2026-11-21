// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class NativeOutputCoreRegistrationTests
{
    internal static CompilationOptions CreateRegisteredCompilationOptions(
        BoundRuntimePolicy runtimePolicy = BoundRuntimePolicy.FailFast)
    {
        var (native, output) = CreateCombinedRegistrations();
        return new CompilationOptions {
            RuntimePolicy = runtimePolicy,
            NativeCoreRegistration = native,
            LayoutCoreRegistration =
                NativeCoreRegistrationTests.CreateLayoutRegistration(native),
            NativeOutputCoreRegistration = output,
        };
    }

    internal static CompilationReference CreateNativeOutputReference(
        BoundRuntimePolicy runtimePolicy = BoundRuntimePolicy.FailFast)
    {
        var (native, output) = CreateCombinedRegistrations();
        var layout
            = NativeCoreRegistrationTests.CreateLayoutRegistration(native);
        ImmutableArray<BoundEnumDefinition> enumDefinitions = [
            .. native.EnumDefinitions.Select(BoundEnumDefinition.FromCore),
            BoundEnumDefinition.FromCore(output.OutputConditions),
            BoundEnumDefinition.FromCore(layout.LayoutKinds),
        ];
        return new CompilationReference([], [], runtimePolicy,
            nativeCoreRegistration: native,
            enumDefinitions: enumDefinitions,
            layoutCoreRegistration: layout,
            nativeOutputCoreRegistration: output);
    }

    [Test]
    public void RegistrationRetainsExactSchemaAndSupportsSplitProviders()
    {
        var registration = CreateOutputOnlyRegistration();

        Assert.That(registration.IsValid(), Is.True);
        Assert.That(registration.OutputConditions.QualifiedName,
            Is.EqualTo("Terrum.Interop.NativeOutputCondition"));
        Assert.That(registration.OutputConditions.Members.Select(
            static member => (member.Name, member.Value)),
            Is.EquivalentTo(ImmutableArray.Create(
                ("NonZero", 0),
                ("NonNegative", 1))));
        Assert.That(registration.NativeOutput.QualifiedName,
            Is.EqualTo("Terrum.Interop.NativeOutput"));
        Assert.That(registration.NativeOutput.Placement,
            Is.EqualTo("primary-callable-contract"));
        Assert.That(registration.NativeOutput.Targets,
            Is.EquivalentTo(OutputContexts()));
        var argument = registration.NativeOutput.Arguments.Single();
        Assert.That(argument.Name, Is.EqualTo("When"));
        Assert.That(argument.Kind,
            Is.EqualTo(CoreNativeAttributeArgumentKind.Enum));
        Assert.That(argument.EnumType,
            Is.EqualTo(registration.OutputConditions.Identity));
        Assert.That(argument.IsRequired, Is.True);
        Assert.That(argument.AllowsPositional, Is.False);
        Assert.That(argument.AllowsNamed, Is.True);
        Assert.That(argument.Contexts, Is.EquivalentTo(OutputContexts()));
        Assert.That(argument.Default, Is.Null);
        Assert.That(registration.ModuleBindings.Declarations.Any(
            static declaration =>
                declaration.NativeOutputAttributeSchema is not null),
            Is.True);

        var split = CreateSplitProviderRegistration();
        Assert.That(split.IsValid(), Is.True);
        Assert.That(split.OutputConditions.Identity.ProviderIdentity,
            Is.Not.EqualTo(split.NativeOutput.Identity.ProviderIdentity));
        Assert.That(split.ModuleBindings.Providers, Has.Length.EqualTo(2));
    }

    [Test]
    public void RegistrationRejectsMalformedEnumSchemaAndMissingExports()
    {
        var registration = CreateOutputOnlyRegistration();
        var conditions = registration.OutputConditions;
        var schema = registration.NativeOutput;

        var invalidValues = conditions with {
            Members = conditions.Members.SetItem(0,
                conditions.Members[0] with { Value = 1 }),
        };
        _ = Assert.Throws<ArgumentException>(() =>
            NativeOutputCoreRegistrationSet.Create(registration.ModuleBindings,
                invalidValues, schema));

        var positionalWhen = schema with {
            Arguments = schema.Arguments.SetItem(0,
                schema.Arguments[0] with { AllowsPositional = true }),
        };
        _ = Assert.Throws<ArgumentException>(() =>
            NativeOutputCoreRegistrationSet.Create(registration.ModuleBindings,
                conditions, positionalWhen));

        var caseVariantTargets = schema with {
            Targets = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
                "Direct-Import-Parameter",
                "Foreign-Interface-Method-Parameter"),
        };
        _ = Assert.Throws<ArgumentException>(() =>
            NativeOutputCoreRegistrationSet.Create(registration.ModuleBindings,
                conditions, caseVariantTargets));

        var caseVariantContexts = schema with {
            Arguments = schema.Arguments.SetItem(0,
                schema.Arguments[0] with {
                    Contexts = ImmutableHashSet.Create(
                        StringComparer.OrdinalIgnoreCase,
                        "Direct-Import-Parameter",
                        "Foreign-Interface-Method-Parameter"),
                }),
        };
        _ = Assert.Throws<ArgumentException>(() =>
            NativeOutputCoreRegistrationSet.Create(registration.ModuleBindings,
                conditions, caseVariantContexts));

        var nulSchemaIdentity = schema with {
            Identity = schema.Identity with {
                ExportKey = "NativeOutput\0schema",
            },
        };
        _ = Assert.Throws<ArgumentException>(() =>
            NativeOutputCoreRegistrationSet.Create(registration.ModuleBindings,
                conditions, nulSchemaIdentity));

        var nulMemberIdentity = conditions with {
            Members = conditions.Members.SetItem(0,
                conditions.Members[0] with {
                    Identity = conditions.Members[0].Identity with {
                        ExportKey = "NativeOutputCondition.NonZero\0",
                    },
                }),
        };
        _ = Assert.Throws<ArgumentException>(() =>
            NativeOutputCoreRegistrationSet.Create(registration.ModuleBindings,
                nulMemberIdentity, schema));

        var wrongEnum = schema with {
            Arguments = schema.Arguments.SetItem(0,
                schema.Arguments[0] with {
                    EnumType = new CoreDeclarationIdentity(
                        "core.output-enums", "OtherCondition",
                        CoreDeclarationKind.EnumType),
                }),
        };
        _ = Assert.Throws<ArgumentException>(() =>
            NativeOutputCoreRegistrationSet.Create(registration.ModuleBindings,
                conditions, wrongEnum));

        var missingSchemaBindings = CoreProviderModuleBindings.Create(
            registration.UsingModuleIdentity,
            registration.ModuleBindings.Providers,
            [CoreProviderDeclarationSnapshot.From(conditions)]);
        _ = Assert.Throws<ArgumentException>(() =>
            NativeOutputCoreRegistrationSet.Create(missingSchemaBindings,
                conditions, schema));
    }

    [Test]
    public void CompilationOptionsValidateIndependentRegistrationAndSnapshotComposition()
    {
        var outputOnly = CreateOutputOnlyRegistration();
        var outputOnlyCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("internal int32 Identity(int32 value) { return value; }"),
            new CompilationOptions {
                NativeOutputCoreRegistration = outputOnly,
            });
        Assert.That(outputOnlyCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, outputOnlyCompilation.Diagnostics));
        Assert.That(outputOnlyCompilation.Program!.NativeOutputCoreRegistration,
            Is.SameAs(outputOnly));
        SemanticVerifier.VerifyLibrary(outputOnlyCompilation.Program);

        var (native, output) = CreateCombinedRegistrations();
        var combined = Compilation.CreateLibrary(
            SyntaxTree.Parse("internal int32 Identity(int32 value) { return value; }"),
            new CompilationOptions {
                NativeCoreRegistration = native,
                NativeOutputCoreRegistration = output,
            });
        Assert.That(combined.HasErrors, Is.False,
            string.Join(Environment.NewLine, combined.Diagnostics));
        Assert.That(output.ModuleBindings,
            Is.SameAs(native.ModuleBindings));

        var unrelatedNative = NativeCoreRegistrationTests.CreateRegistration();
        _ = Assert.Throws<ArgumentException>(() =>
            Compilation.CreateLibrary(
                SyntaxTree.Parse("internal int32 Identity(int32 value) { return value; }"),
                new CompilationOptions {
                    NativeCoreRegistration = unrelatedNative,
                    NativeOutputCoreRegistration = output,
                }));

        var program = combined.Program!;
        var reference = new CompilationReference(program.Structures,
            program.Functions, program.RuntimePolicy,
            nativeCoreRegistration: native,
            enumDefinitions: program.EnumDefinitions,
            nativeOutputCoreRegistration: output);
        var referencedOutput = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;
            [NativeImport("example.dll", "ReadValue")]
            extern unsafe int32 ReadValue(
                [NativeOutput(When: NativeOutputCondition.NonZero)]
                write ptr<int32> value);
            """), reference);
        Assert.That(referencedOutput.HasErrors, Is.False,
            string.Join(Environment.NewLine, referencedOutput.Diagnostics));
    }

    [Test]
    public void PublicNativeOutputImportRemainsAddressable()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [param(result): DependsOn(nameof(source))]
            [param(result): ForwardsBacking(nameof(source))]
            [Terrum.Interop.NativeImport("synthetic.dll", "ReadAndProbe")]
            [Terrum.Interop.NativeParameter(3, Read, NoEscape, 4)]
            public extern unsafe int32 ReadAndProbe(
                ptr<int32> source,
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonNegative)]
                write ptr<ptr<int32>> result,
                ptr<uint32> values);

            public int32 Address() {
                unsafe {
                    var readAndProbe = &ReadAndProbe;
                    return 0;
                }
            }
            """), CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var output = compilation.Program!.Functions.Single(static function =>
            function.Name == "ReadAndProbe").NativeOutputContracts.Single();
        Assert.That(output.SuccessSelector,
            Is.EqualTo(NativeOutputSelector.NonNegative));
    }

    [Test]
    public void NativeOutputUsesExactRegisteredEnumAndConstEvalForwarding()
    {
        var (native, output) = CreateCombinedRegistrations();
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;

            [NativeImport("example.dll", "ReadSigned")]
            extern unsafe int32 ReadSigned(
                [NativeOutput(When: ForwardCondition())]
                write ptr<int32> value);

            [NativeImport("example.dll", "ReadUnsigned")]
            extern unsafe uint32 ReadUnsigned(
                [NativeOutput(When: NativeOutputCondition.NonZero)]
                write ptr<uint32> value);

            [ConstEval]
            internal NativeOutputCondition BaseCondition() {
                return NativeOutputCondition.NonNegative;
            }

            [ConstEval]
            internal NativeOutputCondition ForwardCondition() {
                return BaseCondition();
            }
            """), CoreEvaluationFormTestHelpers.WithConstEval(
                new CompilationOptions {
                    NativeCoreRegistration = native,
                    NativeOutputCoreRegistration = output,
                }));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var signed = compilation.Program!.Functions.Single(static function =>
            function.Name == "ReadSigned");
        Assert.That(signed.DirectNativeImport!.Outputs.Single().ResultCondition,
            Is.EqualTo(DirectNativeImportContract
                .Int32NonNegativeResultCondition));
        Assert.That(signed.NativeOutputContracts.Single().SuccessSelector,
            Is.EqualTo(NativeOutputSelector.NonNegative));
        var unsigned = compilation.Program.Functions.Single(static function =>
            function.Name == "ReadUnsigned");
        Assert.That(unsigned.DirectNativeImport!.Outputs.Single().ResultCondition,
            Is.EqualTo(DirectNativeImportContract
                .UInt32NonZeroResultCondition));

        var aliased = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;
            alias OutputAttribute = Terrum.Interop.NativeOutput;
            alias OutputCondition = Terrum.Interop.NativeOutputCondition;
            [NativeImport("example.dll", "ReadValue")]
            extern unsafe int32 ReadValue(
                [OutputAttribute(When: OutputCondition.NonZero)]
                write ptr<int32> value);
            """), new CompilationOptions {
                NativeCoreRegistration = native,
                NativeOutputCoreRegistration = output,
            });
        Assert.That(aliased.HasErrors, Is.False,
            string.Join(Environment.NewLine, aliased.Diagnostics));

        var missingRegistration = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
                extern unsafe int32 ReadValue(
                    [Terrum.Interop.NativeOutput(
                        When: Terrum.Interop.NativeOutputCondition.NonZero)]
                    write ptr<int32> value);
                """), new CompilationOptions {
                    NativeCoreRegistration = native,
                });
        Assert.That(missingRegistration.HasErrors, Is.True,
            string.Join(Environment.NewLine, missingRegistration.Diagnostics));
        Assert.That(missingRegistration.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, missingRegistration.Diagnostics));

        var wrongEnum = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;
            [NativeImport("example.dll", "ReadValue")]
            extern unsafe int32 ReadValue(
                [NativeOutput(When: NativeCallingConvention.Cdecl)]
                write ptr<int32> value);
            """), new CompilationOptions {
                NativeCoreRegistration = native,
                NativeOutputCoreRegistration = output,
            });
        Assert.That(wrongEnum.HasErrors, Is.True,
            string.Join(Environment.NewLine, wrongEnum.Diagnostics));
        Assert.That(wrongEnum.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, wrongEnum.Diagnostics));
    }

    [Test]
    public void ForeignNativeInterfaceBindsOutputContractsAndDependencySources()
    {
        var compilation = CreateForeignNativeOutputCompilation("""
            [param(result): Terrum.Interop.NativeOutput(
                When: Terrum.Interop.NativeOutputCondition.NonNegative)]
            public unsafe int32 Read(ptr<int32> source,
                [Terrum.CodeAnalysis.DependsOn(this, nameof(source))]
                [Terrum.CodeAnalysis.ForwardsBacking(this, nameof(source))]
                write ptr<int32> result);

            [param(result): Terrum.CodeAnalysis.DependsOn(
                this, nameof(source))]
            [param(result): Terrum.CodeAnalysis.ForwardsBacking(
                this, nameof(source))]
            public unsafe int32 ReadInline(ptr<int32> source,
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<int32> result);
            """);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var definition = program.Interfaces.Single();
        var members = definition.Members.ToDictionary(
            static member => member.Name, StringComparer.Ordinal);
        Assert.That(members["Read"].NativeCallableConvention,
            Is.EqualTo(NativeCallingConvention.Cdecl));
        Assert.That(members["ReadInline"].NativeCallableConvention,
            Is.EqualTo(NativeCallingConvention.Cdecl));

        var nonNegative = members["Read"].NativeOutputContracts.Single();
        Assert.That(nonNegative.FormalOrdinal, Is.EqualTo(1));
        Assert.That(nonNegative.PointeeType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(nonNegative.Formation,
            Is.EqualTo(NativeOutputFormation.VacantWholeObject));
        Assert.That(nonNegative.PublicationPhase,
            Is.EqualTo(NativeOutputPublicationPhase.NormalCompletion));
        Assert.That(nonNegative.SuccessSelector,
            Is.EqualTo(NativeOutputSelector.NonNegative));
        Assert.That(nonNegative.SuccessState, Is.EqualTo(NativeOutputState.Live));
        Assert.That(nonNegative.ComplementSelector,
            Is.EqualTo(NativeOutputSelector.Negative));
        Assert.That(nonNegative.ComplementState,
            Is.EqualTo(NativeOutputState.Vacant));
        AssertOutputDependencies(nonNegative);

        var nonZero = members["ReadInline"].NativeOutputContracts.Single();
        Assert.That(nonZero.FormalOrdinal, Is.EqualTo(1));
        Assert.That(nonZero.SuccessSelector,
            Is.EqualTo(NativeOutputSelector.NonZero));
        Assert.That(nonZero.ComplementSelector,
            Is.EqualTo(NativeOutputSelector.Zero));
        AssertOutputDependencies(nonZero);

        SemanticVerifier.VerifyLibrary(program);

        var missingOutputContract = program with {
            Interfaces = program.Interfaces.SetItem(0, definition with {
                Members = definition.Members.SetItem(0,
                    members["Read"] with {
                        NativeOutputContracts = [],
                    }),
            }),
        };
        Assert.Throws<ArgumentException>(() =>
            SemanticVerifier.VerifyLibrary(missingOutputContract));

        var mismatchedPointee = program with {
            Interfaces = program.Interfaces.SetItem(0, definition with {
                Members = definition.Members.SetItem(0,
                    members["Read"] with {
                        NativeOutputContracts = [
                            nonNegative with {
                                PointeeType = PrimitiveType.UInt32,
                            },
                        ],
                    }),
            }),
        };
        Assert.Throws<ArgumentException>(() =>
            SemanticVerifier.VerifyLibrary(mismatchedPointee));
    }

    [Test]
    public void ForeignNativeInterfaceCallStagesReceiverAndPublishesOutputByResult()
    {
        var (native, output) = CreateCombinedRegistrations(
            includeForeignNativeInterface: true);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable(
                CallConv: Terrum.Interop.NativeCallingConvention.Cdecl)]
            [Terrum.Interop.ForeignNativeInterface(
                VTableByteOffset: uint32(0))]
            public interface IProbe {
                [param(value): Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonNegative)]
                public unsafe int32 TryRead(write ptr<int32> value);
            }

            int32 Invoke(ptr<void> receiver) {
                unsafe {
                    init int32 value;
                    int32 status = IProbe.TryRead(receiver, &init value);
                    if (status >= 0) { return value; }
                }
                return 0;
            }
            """), new CompilationOptions {
                NativeCoreRegistration = native,
                LayoutCoreRegistration =
                    NativeCoreRegistrationTests.CreateLayoutRegistration(native),
                NativeOutputCoreRegistration = output,
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
        Assert.That(foreignCall?.ForeignInterfaceCall
            ?.NativeOutputContracts.Single().FormalOrdinal, Is.EqualTo(0));
        Assert.That(foreignCall?.ForeignInterfaceCall?.Receiver.Type,
            Is.EqualTo(PrimitiveType.NativeRawPointer));
        SemanticVerifier.VerifyLibrary(program);
    }

    [Test]
    public void ForeignNativeInterfaceOutputTargetFailureDoesNotRetryAnotherMember()
    {
        var (native, output) = CreateCombinedRegistrations(
            includeForeignNativeInterface: true);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable(
                CallConv: Terrum.Interop.NativeCallingConvention.Cdecl)]
            [Terrum.Interop.ForeignNativeInterface(
                VTableByteOffset: uint32(0))]
            public interface IProbe {
                [param(value): Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonNegative)]
                public unsafe int32 Read(write ptr<uint32> value,
                    write ptr<int32> source);

                [param(value): Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                public unsafe int32 Read(write ptr<int32> value,
                    ptr<int32> source);
            }

            int32 Invoke(ptr<void> receiver,
                write ptr<int32> source) {
                unsafe {
                    init int32 value;
                    return IProbe.Read(receiver, &init value, source);
                }
            }
            """), new CompilationOptions {
                NativeCoreRegistration = native,
                LayoutCoreRegistration =
                    NativeCoreRegistrationTests.CreateLayoutRegistration(native),
                NativeOutputCoreRegistration = output,
            });

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Message.Contains("ambiguous",
                StringComparison.OrdinalIgnoreCase)), Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void ForeignNativeInterfaceRejectsUnregisteredOrIneligibleOutputs()
    {
        const string output = """
            public unsafe int32 Read(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<int32> result);
            """;

        var missingRegistration = CreateForeignNativeOutputCompilation(
            output, includeOutputRegistration: false);
        AssertNativeOutputDiagnostic(missingRegistration);

        var nonForeignInterface = CreateForeignNativeOutputCompilation(
            output, includeForeignNativeInterface: false);
        AssertNativeOutputDiagnostic(nonForeignInterface);

        var readonlyPointer = CreateForeignNativeOutputCompilation("""
            public unsafe int32 Read(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                ptr<int32> result);
            """);
        AssertNativeOutputDiagnostic(readonlyPointer);

        var unsupportedCellType = CreateForeignNativeOutputCompilation("""
            public unsafe int32 Read(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<void> result);
            """);
        Assert.That(unsupportedCellType.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                unsupportedCellType.Diagnostics));

        var duplicateOutputTarget = CreateForeignNativeOutputCompilation("""
            [param(result): Terrum.Interop.NativeOutput(
                When: Terrum.Interop.NativeOutputCondition.NonZero)]
            public unsafe int32 Read(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<int32> result);
            """);
        AssertNativeOutputDiagnostic(duplicateOutputTarget);

        var invalidCondition = CreateForeignNativeOutputCompilation("""
            public unsafe uint32 Read(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonNegative)]
                write ptr<int32> result);
            """);
        AssertNativeOutputDiagnostic(invalidCondition);

        var receiverOutput = CreateForeignNativeOutputCompilation("""
            [param(this): Terrum.Interop.NativeOutput(
                When: Terrum.Interop.NativeOutputCondition.NonZero)]
            public unsafe int32 Read();
            """);
        AssertNativeOutputDiagnostic(receiverOutput);

        var unsupportedDependency = CreateForeignNativeOutputCompilation("""
            [param(result): Terrum.CodeAnalysis.Captures(this)]
            public unsafe int32 Read(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<int32> result);
            """);
        Assert.That(unsupportedDependency.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                unsupportedDependency.Diagnostics));
        Assert.That(unsupportedDependency.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3023"), Is.True,
            string.Join(Environment.NewLine,
                unsupportedDependency.Diagnostics));

        var unpairedForwarding = CreateForeignNativeOutputCompilation("""
            [param(result): Terrum.CodeAnalysis.ForwardsBacking(this)]
            public unsafe int32 Read(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<int32> result);
            """);
        Assert.That(unpairedForwarding.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                unpairedForwarding.Diagnostics));
        Assert.That(unpairedForwarding.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3023"), Is.True,
            string.Join(Environment.NewLine,
                unpairedForwarding.Diagnostics));

        var nativeParameterOverlap = CreateForeignNativeOutputCompilation("""
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            public unsafe int32 Read(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<int32> result);
            """);
        AssertNativeOutputDiagnostic(nativeParameterOverlap);
    }

    private static void AssertOutputDependencies(NativeOutputContract output)
    {
        Assert.That(output.Dependencies, Has.Length.EqualTo(2));
        Assert.That(output.Dependencies.All(dependency =>
                dependency.DestinationKind == 2
                && dependency.DestinationOrdinal == output.FormalOrdinal
                && dependency.Phase
                    == NativeOutputPublicationPhase.NormalCompletion
                && dependency.Relationship
                    == NativeOutputDependencyRelationship.Forward),
            Is.True);
        Assert.That(output.Dependencies.Any(dependency =>
                dependency.SourceKind
                    == NativeOutputDependencySourceKind.Formal
                && dependency.SourceOrdinal == 0),
            Is.True);
        Assert.That(output.Dependencies.Any(dependency =>
                dependency.SourceKind
                    == NativeOutputDependencySourceKind.Receiver
                && dependency.SourceOrdinal == 0),
            Is.True);
    }

    private static void AssertNativeOutputDiagnostic(Compilation compilation)
    {
        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    private static Compilation CreateForeignNativeOutputCompilation(
        string memberDeclarations,
        bool includeOutputRegistration = true,
        bool includeForeignNativeInterface = true)
    {
        var (native, output) = CreateCombinedRegistrations(
            includeForeignNativeInterface: true);
        var options = new CompilationOptions {
            NativeCoreRegistration = native,
            LayoutCoreRegistration =
                NativeCoreRegistrationTests.CreateLayoutRegistration(native),
            NativeOutputCoreRegistration = includeOutputRegistration
                ? output : null,
        };
        var foreignInterfaceAttribute = includeForeignNativeInterface
            ? "[Terrum.Interop.ForeignNativeInterface(VTableByteOffset: uint32(0))]"
            : "";
        var source = $$"""
            [Terrum.Interop.NativeCallable(
                CallConv: Terrum.Interop.NativeCallingConvention.Cdecl)]
            {{foreignInterfaceAttribute}}
            public interface IProbe {
            {{memberDeclarations}}
            }
            """;
        return Compilation.CreateLibrary(SyntaxTree.Parse(source), options);
    }

    internal static (
        NativeCoreRegistrationSet NativeCore,
        NativeOutputCoreRegistrationSet NativeOutput)
        CreateCombinedRegistrations(bool includeForeignNativeInterface = false)
    {
        var native = NativeCoreRegistrationTests.CreateRegistration(
            includeForeignNativeInterface);
        var providerIdentity = native.ModuleBindings.Providers.Single()
            .ModuleIdentity;
        var outputConditions = CreateOutputConditions(providerIdentity);
        var nativeOutput = CreateNativeOutputSchema(providerIdentity,
            outputConditions.Identity);
        var declarations = native.ModuleBindings.Declarations
            .Add(CoreProviderDeclarationSnapshot.From(outputConditions))
            .Add(CoreProviderDeclarationSnapshot.From(nativeOutput));
        var bindings = CoreProviderModuleBindings.Create(
            native.UsingModuleIdentity, native.ModuleBindings.Providers,
            declarations);
        var nativeWithOutput = NativeCoreRegistrationSet.Create(bindings,
            native.CallingConventions, native.ResolutionModes,
            native.ResolutionPolicies, native.NativeImport,
            native.NativeCallable, native.NativeCallConv, native.NativeExport,
            native.ForeignNativeInterface);
        var outputRegistration = NativeOutputCoreRegistrationSet.Create(
            bindings, outputConditions, nativeOutput);
        return (nativeWithOutput, outputRegistration);
    }

    private static NativeOutputCoreRegistrationSet
        CreateOutputOnlyRegistration()
        => CreateStandaloneRegistration("core.output", "core.output");

    private static NativeOutputCoreRegistrationSet
        CreateSplitProviderRegistration()
        => CreateStandaloneRegistration("core.output-enums",
            "core.output-schemas");

    private static NativeOutputCoreRegistrationSet
        CreateStandaloneRegistration(string enumProvider,
            string schemaProvider)
    {
        var outputConditions = CreateOutputConditions(enumProvider);
        var nativeOutput = CreateNativeOutputSchema(
            schemaProvider, outputConditions.Identity);
        var providers = ImmutableArray.CreateBuilder<CoreProviderModuleOrigin>();
        providers.Add(new CoreProviderModuleOrigin(enumProvider,
            "revision-enums",
            new CoreProviderSchemaSelection("output-enums", 1), 1, 0, 0));
        if (schemaProvider != enumProvider)
        {
            providers.Add(new CoreProviderModuleOrigin(schemaProvider,
                "revision-schemas",
                new CoreProviderSchemaSelection("output-schemas", 1), 1, 0, 0));
        }
        var declarations = ImmutableArray.Create(
            CoreProviderDeclarationSnapshot.From(outputConditions),
            CoreProviderDeclarationSnapshot.From(nativeOutput));
        var bindings = CoreProviderModuleBindings.Create("consumer.module",
            providers.ToImmutable(), declarations);
        return NativeOutputCoreRegistrationSet.Create(bindings,
            outputConditions, nativeOutput);
    }

    private static CoreEnumDefinition CreateOutputConditions(
        string providerIdentity)
    {
        var identity = new CoreDeclarationIdentity(providerIdentity,
            "NativeOutputCondition", CoreDeclarationKind.EnumType);
        return new CoreEnumDefinition(identity,
            "Terrum.Interop.NativeOutputCondition", PrimitiveType.Int32, [
                new CoreEnumMemberDefinition(
                    new CoreDeclarationIdentity(providerIdentity,
                        "NativeOutputCondition.NonZero",
                        CoreDeclarationKind.EnumMember),
                    identity, "NonZero", 0, true) {
                    DeclarationKey = "member.non-zero",
                },
                new CoreEnumMemberDefinition(
                    new CoreDeclarationIdentity(providerIdentity,
                        "NativeOutputCondition.NonNegative",
                        CoreDeclarationKind.EnumMember),
                    identity, "NonNegative", 1, true) {
                    DeclarationKey = "member.non-negative",
                },
            ], 0, true, true, false);
    }

    private static CoreNativeOutputAttributeSchema
        CreateNativeOutputSchema(
            string providerIdentity,
            CoreDeclarationIdentity outputConditions)
        => new(
            new CoreDeclarationIdentity(providerIdentity, "NativeOutput",
                CoreDeclarationKind.AttributeSchema),
            "Terrum.Interop.NativeOutput",
            [
                new CoreNativeAttributeArgument("When",
                    CoreNativeAttributeArgumentKind.Enum, outputConditions,
                    true, false, true, OutputContexts(), null),
            ],
            "primary-callable-contract", OutputContexts(), 0, true, true,
            false);

    private static ImmutableHashSet<string> OutputContexts()
        => ImmutableHashSet.Create(StringComparer.Ordinal,
            "direct-import-parameter",
            "foreign-interface-method-parameter");
}
