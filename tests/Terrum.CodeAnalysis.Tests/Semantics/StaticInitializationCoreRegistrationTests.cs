using System;
using System.Collections.Immutable;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class StaticInitializationCoreRegistrationTests
{
    [Test]
    public void RegistrationRetainsExactProviderDeclarations()
    {
        var registration = CreateRegistration();

        Assert.That(registration.IsValid(), Is.True);
        Assert.That(registration.UsingModuleIdentity,
            Is.EqualTo("sample.consumer"));
        Assert.That(registration.GroupedStaticFieldAccess.Identity.ExportKey,
            Is.EqualTo("grouped-static-field-access"));
        Assert.That(registration.RuntimeStaticMemberAccess.Identity.ExportKey,
            Is.EqualTo("runtime-static-member-access"));
        Assert.That(registration.ModuleBindings.Declarations,
            Has.Length.EqualTo(2));
    }

    [Test]
    public void RegistrationRejectsChangedTriggerMemberMapping()
    {
        Assert.That(() => CreateRegistration(triggerEnum:
                CreateTriggerEnum() with {
                    Members = [
                        CreateMember("grouped-static-field-access",
                            "GroupedStaticFieldAccess", 1),
                        CreateMember("runtime-static-member-access",
                            "RuntimeStaticMemberAccess", 0),
                    ],
                }),
            Throws.ArgumentException);
    }

    [Test]
    public void RegistrationRejectsNonPositionalOrDefaultedArgument()
    {
        var schema = CreateSchema() with {
            Arguments = [
                new CoreStaticInitializerAttributeArgument("Trigger",
                    TriggerEnumIdentity, true, false, true, true),
            ],
        };
        Assert.That(() => CreateRegistration(schema: schema),
            Throws.ArgumentException);
    }

    [Test]
    public void RegistrationRequiresProviderSnapshotsForSelectedDeclarations()
    {
        var bindings = CreateBindings(CreateTriggerEnum(), CreateSchema(),
            includeAttributeSnapshot: false);

        Assert.That(() => StaticInitializationCoreRegistrationSet.Create(
            bindings, CreateTriggerEnum(), CreateSchema()),
            Throws.ArgumentException);
    }

    [Test]
    public void CompilationOptionsCarryRegistrationIntoBoundProgram()
    {
        var registration = CreateRegistration();
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Identity(int32 value) { return value; }"),
            new CompilationOptions {
                StaticInitializationCoreRegistration = registration,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program!.StaticInitializationCoreRegistration,
            Is.SameAs(registration));
    }

    [Test]
    public void CompilationReferenceCarriesRegistrationIntoConsumerBinding()
    {
        var registration = CreateRegistration();
        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            staticInitializationCoreRegistration: registration);
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return 0; }"), reference);

        Assert.That(reference.StaticInitializationCoreRegistration,
            Is.SameAs(registration));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program!.StaticInitializationCoreRegistration,
            Is.SameAs(registration));
    }

    [Test]
    public void LazyRegistrationIsIndependentAndFlowsThroughOptionsAndReferences()
    {
        var registration = CreateLazyRegistration();
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Identity(int32 value) { return value; }"),
            new CompilationOptions {
                LazyInitializationCoreRegistration = registration,
            });

        Assert.That(library.HasErrors, Is.False,
            string.Join(Environment.NewLine, library.Diagnostics));
        Assert.That(library.Program!.LazyInitializationCoreRegistration,
            Is.SameAs(registration));
        Assert.That(library.Program.StaticInitializationCoreRegistration,
            Is.Null);

        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            lazyInitializationCoreRegistration: registration);
        var consumer = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return 0; }"), reference);

        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        Assert.That(consumer.Program!.LazyInitializationCoreRegistration,
            Is.SameAs(registration));
        Assert.That(consumer.Program.StaticInitializationCoreRegistration,
            Is.Null);
    }

    [Test]
    public void LazyRegistrationRejectsDuplicateAndIncompatibleSelection()
    {
        var lazyRegistration = CreateLazyRegistration();
        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            lazyInitializationCoreRegistration: lazyRegistration);

        Assert.That(() => Compilation.CreateLibrary(SyntaxTree.Parse(
                "public int32 Identity(int32 value) { return value; }"),
            reference, new CompilationOptions {
                LazyInitializationCoreRegistration = lazyRegistration,
            }), Throws.ArgumentException);
        Assert.That(() => Compilation.CreateLibrary(SyntaxTree.Parse(
                "public int32 Identity(int32 value) { return value; }"),
            new CompilationOptions {
                StaticInitializationCoreRegistration = CreateRegistration(),
                LazyInitializationCoreRegistration = lazyRegistration,
            }), Throws.ArgumentException);
    }

    [TestCase("GroupedStaticFieldAccess",
        BoundStaticInitializationTrigger.GroupedStaticFieldAccess)]
    [TestCase("RuntimeStaticMemberAccess",
        BoundStaticInitializationTrigger.RuntimeStaticMemberAccess)]
    public void StaticConstructorBindsExactRegisteredTrigger(string member,
        BoundStaticInitializationTrigger expectedTrigger)
    {
        var source = $$"""
            struct Example {
                int32 Value;
                [Terrum.StaticInitializer(Terrum.StaticInitializationTrigger.{{member}})]
                static Example() {}
            }
            public int32 Identity(int32 value) { return value; }
            """;
        var registration = CreateRegistration();
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                StaticInitializationCoreRegistration = registration,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = compilation.Program!.Functions.Single(
            static function => function.StaticConstructorIdentity is not null);
        Assert.That(function.IsStatic, Is.True);
        Assert.That(function.IsConstructor, Is.False);
        Assert.That(function.IsClassConstructor, Is.False);
        Assert.That(function.ReturnType, Is.EqualTo(PrimitiveType.Void));
        Assert.That(function.ParameterCount, Is.Zero);
        Assert.That(function.StaticConstructorIdentity!.ModuleIdentity,
            Is.EqualTo("sample.consumer"));
        Assert.That(function.StaticConstructorIdentity.OwnerName,
            Is.EqualTo("Example"));
        Assert.That(function.StaticInitializationTrigger,
            Is.EqualTo(expectedTrigger));
        Assert.That(() => NativeEmitter.EmitLibrary(compilation.Program,
                ["Identity"], NativeRuntimePolicy.FailFast),
            Throws.TypeOf<NotSupportedException>());
        Assert.That(() => ModuleCompiler.CreateProducer(
                compilation.Program, []),
            Throws.TypeOf<ModuleContractException>());
        Assert.That(() => TrRmExecutableCompiler.EmitStandard(
                compilation.Program, "sample.static",
                "trrm/1-draft-layout-queries-1", 1),
            Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void StaticConstructorResolvesAliasesParenthesesAndMethodTarget()
    {
        const string source = """
            alias Initializer = Terrum.StaticInitializer;
            alias Trigger = Terrum.StaticInitializationTrigger;
            struct Example {
                int32 Value;
                [method: Initializer(((Trigger.GroupedStaticFieldAccess)))]
                static Example() {}
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                StaticInitializationCoreRegistration = CreateRegistration(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program!.Functions.Single(static function =>
            function.StaticConstructorIdentity is not null)
                .StaticInitializationTrigger,
            Is.EqualTo(BoundStaticInitializationTrigger.GroupedStaticFieldAccess));
    }

    [Test]
    public void StaticConstructorRejectsComputedOrNamedTriggerValues()
    {
        var computed = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Example {
                int32 Value;
                [Terrum.StaticInitializer(
                    Terrum.StaticInitializationTrigger.GroupedStaticFieldAccess + 0)]
                static Example() {}
            }
            """), new CompilationOptions {
                StaticInitializationCoreRegistration = CreateRegistration(),
            });
        var named = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Example {
                int32 Value;
                [Terrum.StaticInitializer(Trigger:
                    Terrum.StaticInitializationTrigger.GroupedStaticFieldAccess)]
                static Example() {}
            }
            """), new CompilationOptions {
                StaticInitializationCoreRegistration = CreateRegistration(),
            });

        Assert.That(computed.HasErrors, Is.True);
        Assert.That(computed.Diagnostics.Any(static diagnostic =>
            diagnostic.Message.Contains("registered trigger case",
                StringComparison.Ordinal)), Is.True);
        Assert.That(named.HasErrors, Is.True);
    }

    [Test]
    public void StaticAndInstanceConstructorsHaveDistinctIdentities()
    {
        const string source = """
            class Example {
                public int32 Value;
                public Example() { Value = 0; }
                [Terrum.StaticInitializer(
                    Terrum.StaticInitializationTrigger.GroupedStaticFieldAccess)]
                static Example() {}
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                StaticInitializationCoreRegistration = CreateRegistration(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var functions = compilation.Program!.Functions;
        var instanceConstructor = functions.Single(static function =>
            function.IsClassConstructor);
        var staticConstructor = functions.Single(static function =>
            function.StaticConstructorIdentity is not null);
        Assert.That(staticConstructor.Name,
            Is.Not.EqualTo(instanceConstructor.Name));
    }

    [Test]
    public void PartialStaticConstructorComposesPrimaryContractsAndImplementation()
    {
        const string source = """
            struct Example {
                int32 Value;
                [Terrum.StaticInitializer(
                    Terrum.StaticInitializationTrigger.GroupedStaticFieldAccess)]
                static partial Example() ensure true;
                static partial Example() {}
            }
            """;
        var postconditionFailure = new CoreContractFailureDeclarationIdentity(
            "core.contracts", "postcondition-exception");
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                StaticInitializationCoreRegistration = CreateRegistration(),
                CoreContractFailureIdentitySelection =
                    new CoreContractFailureIdentitySelection(
                        "sample.consumer", null, postconditionFailure, null),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = compilation.Program!.Functions.Single(
            static function => function.StaticConstructorIdentity is not null);
        Assert.That(function.IsExternal, Is.False);
        Assert.That(function.StaticInitializationTrigger,
            Is.EqualTo(BoundStaticInitializationTrigger.GroupedStaticFieldAccess));
        Assert.That(function.DeclarationContractClauses, Has.Length.EqualTo(1));
        Assert.That(function.DeclarationContractClauses[0].SelectedFailureClass,
            Is.EqualTo(postconditionFailure));
        Assert.That(function.Span.Start,
            Is.EqualTo(source.IndexOf("static partial Example() {}", StringComparison.Ordinal)));
    }

    [Test]
    public void PartialStaticConstructorComposesWhenImplementationAppearsFirst()
    {
        const string source = """
            struct Example {
                int32 Value;
                static partial Example() {}
                [Terrum.StaticInitializer(
                    Terrum.StaticInitializationTrigger.RuntimeStaticMemberAccess)]
                static partial Example();
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                StaticInitializationCoreRegistration = CreateRegistration(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = compilation.Program!.Functions.Single(
            static function => function.StaticConstructorIdentity is not null);
        Assert.That(function.StaticInitializationTrigger,
            Is.EqualTo(BoundStaticInitializationTrigger.RuntimeStaticMemberAccess));
        Assert.That(function.Span.Start,
            Is.EqualTo(source.IndexOf("static partial Example() {}", StringComparison.Ordinal)));
    }

    [TestCase("""
        [Terrum.StaticInitializer(
            Terrum.StaticInitializationTrigger.GroupedStaticFieldAccess)]
        static partial Example();
        [Terrum.StaticInitializer(
            Terrum.StaticInitializationTrigger.GroupedStaticFieldAccess)]
        static partial Example();
        """)]
    [TestCase("""
        static partial Example() {}
        static partial Example() {}
        """)]
    public void PartialStaticConstructorRejectsDuplicateParts(string parts)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $$"""
            struct Example {
                int32 Value;
                {{parts}}
            }
            """), new CompilationOptions {
                StaticInitializationCoreRegistration = CreateRegistration(),
            });

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Message.Contains(
                "partial static constructor requires",
                StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public void PartialStaticConstructorImplementationCannotRepeatPrimaryContracts()
    {
        const string source = """
            struct Example {
                int32 Value;
                [Terrum.StaticInitializer(
                    Terrum.StaticInitializationTrigger.GroupedStaticFieldAccess)]
                static partial Example() ensure true;
                [Terrum.StaticInitializer(
                    Terrum.StaticInitializationTrigger.GroupedStaticFieldAccess)]
                static partial Example() ensure true {}
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                StaticInitializationCoreRegistration = CreateRegistration(),
                CoreContractFailureIdentitySelection =
                    new CoreContractFailureIdentitySelection(
                        "sample.consumer", null,
                        new CoreContractFailureDeclarationIdentity(
                            "core.contracts", "postcondition-exception"),
                        null),
            });

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Message.Contains(
                "cannot repeat primary trigger policy or contracts",
                StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void PartialStaticConstructorModifierMustFollowStatic()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Example {
                int32 Value;
                [Terrum.StaticInitializer(
                    Terrum.StaticInitializationTrigger.GroupedStaticFieldAccess)]
                partial static Example() {}
            }
            """), new CompilationOptions {
                StaticInitializationCoreRegistration = CreateRegistration(),
            });

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Message.Contains(
                "partial modifier must follow static",
                StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void StaticFieldsKeepSeparateStorageAndDeclarationOrder()
    {
        const string source = """
            struct Example {
                int32 Instance;
                public static int32 First = 1;
                public static mut int32 Second = First + 1;
            }
            public int32 Identity(int32 value) { return value; }
            public int32 ReadFirst() { return Example.First; }
            public void UpdateSecond() { Example.Second = 3; }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(program.Structures.Single().Fields.Select(
                static field => field.Name),
            Is.EqualTo(["Instance"]));
        Assert.That(program.StaticFields.Select(
                static field => (field.Name, field.Type,
                    field.DeclarationOrdinal, field.IsMutable)),
            Is.EqualTo([
                ("First", PrimitiveType.Int32, 0u, false),
                ("Second", PrimitiveType.Int32, 1u, true),
            ]));
        Assert.That(program.StaticInitializationGroups, Has.Length.EqualTo(1));
        var group = program.StaticInitializationGroups[0];
        Assert.That(group.Trigger,
            Is.EqualTo(BoundStaticInitializationTrigger.GroupedStaticFieldAccess));
        Assert.That(group.StaticFieldIndexes, Is.EqualTo([0, 1]));
        Assert.That(group.EagerInitializerFunctionIndexes.Select(index =>
                program.Functions[index].StaticFieldInitializerIndex),
            Is.EqualTo([0, 1]));
        var remappedField = BoundTreeTraversal.RewriteTypes(
            program.StaticFields[0], static type =>
                type == PrimitiveType.Int32 ? PrimitiveType.UInt32 : type);
        Assert.That(remappedField.Type, Is.EqualTo(PrimitiveType.UInt32));
        var remappedGroup = BoundTreeTraversal.RewriteTypes(group,
            static type => type);
        Assert.That(remappedGroup.StaticFieldIndexes,
            Is.EqualTo(group.StaticFieldIndexes));
        var readFunction = program.Functions.Single(
            static function => function.Name == "ReadFirst");
        var foundStaticRead = false;
        Assert.That(BoundTreeTraversal.VisitExpressions(readFunction.Body,
            expression => foundStaticRead
                |= expression is BoundStaticFieldRead), Is.True);
        Assert.That(foundStaticRead, Is.True);
        var updateFunction = program.Functions.Single(
            static function => function.Name == "UpdateSecond");
        var foundStaticWrite = false;
        Assert.That(BoundTreeTraversal.VisitStatements(updateFunction.Body,
            statement => foundStaticWrite
                |= statement is BoundStaticFieldAssignment), Is.True);
        Assert.That(foundStaticWrite, Is.True);
        Assert.That(() => NativeEmitter.EmitLibrary(program, ["Identity"],
                NativeRuntimePolicy.FailFast),
            Throws.TypeOf<NotSupportedException>());
        Assert.That(() => ModuleCompiler.CreateProducer(program, ["Identity"]),
            Throws.TypeOf<ModuleContractException>());
        Assert.That(() => TrRmExecutableCompiler.EmitStandard(program,
                "sample.static-fields",
                "trrm/1-draft-layout-queries-1", 1),
            Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void StaticInitializerCannotReadUninitializedLaterField()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Example {
                int32 Instance;
                public static int32 First = Second;
                public static int32 Second = 2;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void StaticAutoPropertyBackingJoinsItsDeclarationOrderedGroup()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Example {
                int32 Instance;
                public static int32 First = 1;
                public static int32 BackingProbe { get; } = First + 1;
                public static int32 Last = BackingProbe + 1;
                public static int32 Read() { return BackingProbe; }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(program.StaticFields.Select(static field =>
                (field.Name, field.BackingPropertyName,
                    field.DeclarationOrdinal)),
            Is.EqualTo([
                ("First", null, 0u),
                ("$property:BackingProbe", "BackingProbe", 1u),
                ("Last", null, 2u),
            ]));

        var getter = program.Functions.Single(
            static function => function.PropertyName == "BackingProbe");
        BoundStaticFieldRead? getterRead = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(getter.Body,
            expression => getterRead ??=
                expression as BoundStaticFieldRead), Is.True);
        Assert.That(getterRead!.Field, Is.EqualTo(1));
        Assert.That(getterRead.Authority,
            Is.EqualTo(BoundStaticFieldAccessAuthority.EagerGroupCompletion));

        var lastInitializer = program.Functions.Single(
            static function => function.StaticFieldInitializerIndex == 2);
        BoundStaticFieldRead? initializerRead = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(lastInitializer.Body,
            expression => initializerRead ??=
                expression as BoundStaticFieldRead), Is.True);
        Assert.That(initializerRead!.Field, Is.EqualTo(1));
        Assert.That(initializerRead.Authority,
            Is.EqualTo(BoundStaticFieldAccessAuthority.InitializationPhase));

        var classCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            class Holder {
                public int32 Field;
                public Holder() { Field = 0; }
                public static int32 Value { get; } = 42;
            }
            public int32 Read() { return Holder.Value; }
            """));
        Assert.That(classCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, classCompilation.Diagnostics));
        Assert.That(classCompilation.Program!.StaticFields.Single()
                .OwnerType,
            Is.EqualTo((PrimitiveType)ManagedClassTypes.First));
    }

    [Test]
    public void StaticConstructorInitializesGetterOnlyPropertyBacking()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Example {
                int32 Instance;
                public static int32 Value { get; }
                [Terrum.StaticInitializer(
                    Terrum.StaticInitializationTrigger.GroupedStaticFieldAccess)]
                static Example() { Value = 42; }
            }
            """), new CompilationOptions {
                StaticInitializationCoreRegistration = CreateRegistration(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(program.StaticFields.Single().BackingPropertyName,
            Is.EqualTo("Value"));
        var constructor = program.Functions.Single(
            static function => function.StaticConstructorIdentity is not null);
        var hasInitializationAssignment = false;
        Assert.That(BoundTreeTraversal.VisitStatements(constructor.Body,
            statement => hasInitializationAssignment
                |= statement is BoundStaticFieldAssignment {
                    Field: 0,
                    IsInitializationAuthority: true,
                }), Is.True);
        Assert.That(hasInitializationAssignment, Is.True);
    }

    [Test]
    public void StaticAutoPropertyRequiresInitializationAndComputedPropertiesHaveNoBacking()
    {
        var missingInitialization = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                struct Example {
                    int32 Instance;
                    public static int32 Value { get; }
                }
                """));
        var computedPropertyInitializer = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                struct Example {
                    int32 Instance;
                    public static int32 Value { get { return 1; } } = 2;
                }
                """));
        var nonStaticAutoProperty = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                struct Example {
                    int32 Field;
                    public int32 Value { get; }
                    public Example(int32 field) { Field = field; }
                }
                """));
        var malformedAutoProperty = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                struct Example {
                    int32 Instance;
                    public static int32 Value { get }
                }
                """));

        Assert.That(missingInitialization.HasErrors, Is.True);
        Assert.That(missingInitialization.Diagnostics.Any(
            static diagnostic => diagnostic.Message.Contains(
                "without a declaration initializer requires an authored static constructor",
                StringComparison.Ordinal)), Is.True);
        Assert.That(computedPropertyInitializer.HasErrors, Is.True);
        Assert.That(computedPropertyInitializer.Diagnostics.Any(
            static diagnostic => diagnostic.Message.Contains(
                "requires supported backing storage",
                StringComparison.Ordinal)), Is.True);
        Assert.That(nonStaticAutoProperty.HasErrors, Is.True);
        Assert.That(nonStaticAutoProperty.Diagnostics.Any(
            static diagnostic => diagnostic.Message.Contains(
                "Instance auto-property backing storage requires",
                StringComparison.Ordinal)), Is.True);
        Assert.That(malformedAutoProperty.HasErrors, Is.True);
        Assert.That(malformedAutoProperty.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM1002"), Is.True);
    }

    [Test]
    public void ClosedGenericOwnersHaveSubstitutedIndependentStaticGroups()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Holder<T> {
                T Instance;
                public Holder(T value) { Instance = value; }
                public static T Value = 1;
                public static T Auto { get; } = 2;
                public int32 Read() {
                    return int32(Value) + int32(Auto);
                }
            }
            int32 Main() {
                Holder<int32> first = new Holder<int32>(1);
                Holder<float64> second = new Holder<float64>(2);
                return first.Read() + second.Read()
                    + Holder<int32>.Value
                    + int32(Holder<float64>.Auto);
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(program.StaticFields.Select(static field =>
                (field.Type, field.Owner.TypeArguments["T"],
                    field.BackingPropertyName)),
            Is.EqualTo([
                (PrimitiveType.Int32, PrimitiveType.Int32, null),
                (PrimitiveType.Int32, PrimitiveType.Int32, "Auto"),
                (PrimitiveType.Float64, PrimitiveType.Float64, null),
                (PrimitiveType.Float64, PrimitiveType.Float64, "Auto"),
            ]));
        Assert.That(program.StaticInitializationGroups, Has.Length.EqualTo(2));
        Assert.That(program.StaticInitializationGroups.Select(
                static group => group.Owner.TypeArguments["T"]),
            Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Float64]));
        Assert.That(program.StaticInitializationGroups[0].StaticFieldIndexes,
            Is.EqualTo([0, 1]));
        Assert.That(program.StaticInitializationGroups[1].StaticFieldIndexes,
            Is.EqualTo([2, 3]));

        var openOwner = Compilation.Create(SyntaxTree.Parse("""
            struct Holder<T> {
                T Instance;
                public static T Value = 1;
            }
            int32 Main() { return Holder.Value; }
            """));
        Assert.That(openOwner.HasErrors, Is.True);
        Assert.That(openOwner.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True);
    }

    [Test]
    public void ClosedGenericStaticConstructorsRetainOwnerSubstitution()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Holder<T> {
                T Instance;
                public static T Value;
                [Terrum.StaticInitializer(
                    Terrum.StaticInitializationTrigger.GroupedStaticFieldAccess)]
                static Holder() { Value = 1; }
            }
            struct Reader {
                int32 Instance;
                private int32 Read(Holder<int32> first,
                    Holder<float64> second) { return 0; }
            }
            """), new CompilationOptions {
                StaticInitializationCoreRegistration = CreateRegistration(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(program.StaticFields.Select(static field =>
                (field.Type, field.Owner.TypeArguments["T"])),
            Is.EqualTo([
                (PrimitiveType.Int32, PrimitiveType.Int32),
                (PrimitiveType.Float64, PrimitiveType.Float64),
            ]));
        Assert.That(program.StaticInitializationGroups, Has.Length.EqualTo(2));
        foreach (var group in program.StaticInitializationGroups)
        {
            var constructor = program.Functions[
                group.StaticConstructorFunctionIndex];
            Assert.That(constructor.StaticConstructorIdentity!.OwnerName,
                Is.EqualTo(group.Owner.Name));
            Assert.That(constructor.StaticConstructorOwnerTypeArguments,
                Is.EqualTo(group.Owner.TypeArguments));
        }
    }

    [Test]
    public void StaticConstructorInitializesFieldsWithoutDeclarationInitializers()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Example {
                int32 Instance;
                public static int32 Value;
                [Terrum.StaticInitializer(
                    Terrum.StaticInitializationTrigger.GroupedStaticFieldAccess)]
                static Example() { Value = 1; }
            }
            public int32 Read() { return Example.Value; }
            """), new CompilationOptions {
                StaticInitializationCoreRegistration = CreateRegistration(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void StaticConstructorInitializesOnEveryConditionalPath()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Example {
                int32 Instance;
                public static int32 Value;
                [Terrum.StaticInitializer(
                    Terrum.StaticInitializationTrigger.GroupedStaticFieldAccess)]
                static Example() {
                    if (ShouldInitialize()) {
                        Value = 1;
                    } else {
                        Value = 2;
                    }
                }
                static bool ShouldInitialize() { return true; }
            }
            """), new CompilationOptions {
                StaticInitializationCoreRegistration = CreateRegistration(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void StaticConstructorMergesInitializationAcrossCaughtFailures()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Example {
                int32 Instance;
                public static int32 Value;
                [Terrum.StaticInitializer(
                    Terrum.StaticInitializationTrigger.GroupedStaticFieldAccess)]
                static Example() {
                    unsafe {
                        try {
                            require ShouldInitialize();
                            Value = 1;
                        } catch {
                            Value = 2;
                        }
                    }
                }
                static bool ShouldInitialize() { return true; }
            }
            """), new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.CheckedExceptions,
                StaticInitializationCoreRegistration = CreateRegistration(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [TestCase("static Example() {}")]
    [TestCase("""
        static Example() {
            if (ShouldInitialize()) { Value = 1; }
        }
        """)]
    [TestCase("""
        static Example() {
            Value = 1;
            if (ShouldInitialize()) { Value = 2; }
        }
        """)]
    [TestCase("""
        static Example() {
            while (ShouldInitialize()) { Value = 1; }
        }
        """)]
    public void StaticConstructorRequiresOneInitializationOnEveryNormalPath(
        string constructor)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $$"""
            struct Example {
                int32 Instance;
                public static int32 Value;
                [Terrum.StaticInitializer(
                    Terrum.StaticInitializationTrigger.GroupedStaticFieldAccess)]
                {{constructor}}
                static bool ShouldInitialize() { return true; }
            }
            """), new CompilationOptions {
                StaticInitializationCoreRegistration = CreateRegistration(),
            });

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void StaticFieldAccessPreservesPrivateOwnerScope()
    {
        var localAccess = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Example {
                int32 Instance;
                private static int32 Value = 1;
                public static int32 Read() { return Value; }
            }
            """));
        var externalAccess = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Example {
                int32 Instance;
                private static int32 Value = 1;
            }
            public int32 Read() { return Example.Value; }
            """));

        Assert.That(localAccess.HasErrors, Is.False,
            string.Join(Environment.NewLine, localAccess.Diagnostics));
        Assert.That(externalAccess.HasErrors, Is.True);
        Assert.That(externalAccess.Diagnostics.Any(static diagnostic =>
            diagnostic.Message.Contains(
                "is inaccessible from this declaration",
                StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void FileScopedStaticFieldsRequireTheDeclaringSourceUnit()
    {
        var sameUnit = Compilation.CreateLibrary(SyntaxTree.Parse("""
            namespace N;
            struct Example {
                int32 Instance;
                file static int32 Value = 1;
                public static int32 Read() { return Value; }
            }
            """));

        Assert.That(sameUnit.HasErrors, Is.False,
            string.Join(Environment.NewLine, sameUnit.Diagnostics));
        Assert.That(sameUnit.Program!.StaticFields.Single().IsFile, Is.True);
        Assert.That(sameUnit.Program.StaticFields.Single()
                .SourceInputOrdinal,
            Is.EqualTo(0));
        Assert.That(sameUnit.Program.Functions.Single(
                static function => function.Name == "N.Example.Read")
                .SourceInputOrdinal,
            Is.EqualTo(0));

        var fieldTree = SyntaxTree.Parse("""
            namespace N;
            struct Example {
                int32 Instance;
                file static int32 Value = 1;
            }
            """);
        var otherUnit = SyntaxTree.Parse("""
            namespace N;
            public int32 Read() { return Example.Value; }
            """);
        var crossUnitRead = Compilation.CreateLibrary(
            [fieldTree, otherUnit]);
        Assert.That(crossUnitRead.HasErrors, Is.True);
        Assert.That(crossUnitRead.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3023"
                && diagnostic.Message.Contains(
                    "File-scoped static field 'Value' is inaccessible",
                    StringComparison.Ordinal)), Is.True);

        var uninitializedFieldTree = SyntaxTree.Parse("""
            namespace N;
            partial struct Example {
                int32 Instance;
                file static int32 Value;
            }
            """);
        var constructorUnit = SyntaxTree.Parse("""
            namespace N;
            partial struct Example {
                [Terrum.StaticInitializer(
                    Terrum.StaticInitializationTrigger.GroupedStaticFieldAccess)]
                static Example() { Value = 1; }
            }
            """);
        var crossUnitInitialization = Compilation.CreateLibrary(
            [uninitializedFieldTree, constructorUnit],
            new CompilationOptions {
                StaticInitializationCoreRegistration = CreateRegistration(),
            });
        Assert.That(crossUnitInitialization.HasErrors, Is.True);
        Assert.That(crossUnitInitialization.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3023"
                && diagnostic.Message.Contains(
                    "File-scoped static field 'Value' is inaccessible",
                    StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void FriendStaticFieldIsAccessibleOnlyFromItsDeclaredFriendType()
    {
        var friendAccess = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Owner {
                int32 Instance;
                friend<Reader> static int32 Value = 1;
            }
            struct Reader {
                int32 Instance;
                public int32 Read() { return Owner.Value; }
            }
            """));

        Assert.That(friendAccess.HasErrors, Is.False,
            string.Join(Environment.NewLine, friendAccess.Diagnostics));
        var friendProgram = friendAccess.Program!;
        var friendField = friendProgram.StaticFields.Single();
        Assert.That(friendField.IsPrivate, Is.True);
        Assert.That(friendField.FriendTargets, Has.Length.EqualTo(1));
        var readerType = (PrimitiveType)(100
            + friendProgram.Structures.IndexOf(
                friendProgram.Structures.Single(
                    static structure => structure.Name == "Reader")));
        Assert.That(friendField.FriendTargets[0].Type,
            Is.EqualTo(readerType));
        var remappedFriendField = BoundTreeTraversal.RewriteTypes(
            friendField,
            type => type == readerType ? PrimitiveType.UInt32 : type);
        Assert.That(remappedFriendField.FriendTargets[0].Type,
            Is.EqualTo(PrimitiveType.UInt32));
        _ = Assert.Throws<ArgumentException>(() =>
            SemanticVerifier.Verify(friendProgram with {
                StaticFields = friendProgram.StaticFields.SetItem(0,
                    friendField with { FriendTargets = [] }),
            }))!;

        var friendProperty = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Owner {
                int32 Instance;
                friend<Reader> static int32 Value { get; } = 1;
            }
            struct Reader {
                int32 Instance;
                public int32 Read() { return Owner.Value; }
            }
            """));
        Assert.That(friendProperty.HasErrors, Is.False,
            string.Join(Environment.NewLine, friendProperty.Diagnostics));
        var friendGetter = friendProperty.Program!.Functions.Single(
            static function => function.PropertyName == "Value");
        Assert.That(friendGetter.IsPrivate, Is.True);
        Assert.That(friendGetter.FriendTargets, Has.Length.EqualTo(1));

        var friendMethod = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Owner {
                int32 Value;
                friend<Reader> int32 Read() { return Value; }
            }
            struct Reader {
                int32 Instance;
                public int32 Read(Owner owner) { return owner.Read(); }
            }
            """));
        Assert.That(friendMethod.HasErrors, Is.False,
            string.Join(Environment.NewLine, friendMethod.Diagnostics));

        var unrelatedAccess = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Owner {
                int32 Instance;
                friend<Reader> static int32 Value = 1;
            }
            struct Reader { int32 Instance; }
            struct Unrelated {
                int32 Instance;
                public int32 Read() { return Owner.Value; }
            }
            """));
        Assert.That(unrelatedAccess.HasErrors, Is.True);
        Assert.That(unrelatedAccess.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3023"
                && diagnostic.Message.Contains(
                    "is inaccessible from this declaration",
                    StringComparison.Ordinal)), Is.True);

        var friendProvider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            namespace N;
            public struct Friend {
                int32 Value;
                public Friend(int32 value) { Value = value; }
                public int32 Read() { return Value; }
            }
            public int32 Use() {
                Friend friend = new Friend(42);
                return friend.Read();
            }
            """));
        Assert.That(friendProvider.HasErrors, Is.False,
            string.Join(Environment.NewLine, friendProvider.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            friendProvider.Program!, ["N.Use"]);
        var friendReference = ModuleCompiler.CreateReference(producer);
        var crossModule = Compilation.CreateLibrary(SyntaxTree.Parse("""
            namespace N;
            struct Owner {
                int32 Instance;
                friend<Friend> static int32 Value = 1;
            }
            """), friendReference);
        Assert.That(crossModule.HasErrors, Is.True);
        Assert.That(crossModule.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3023"
                && diagnostic.Message.Contains(
                    "same-module nominal type",
                    StringComparison.Ordinal)), Is.True);
    }

    [TestCase("Reader<T>")]
    [TestCase("Reader<_>")]
    public void GenericStaticFriendsPreserveExactAndWildcardTypeIdentity(
        string friendTarget)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            $$"""
            struct Owner<T> {
                T Instance;
                friend<{{friendTarget}}> static T Value = 1;
            }
            struct Reader<T> {
                T Instance;
                public Reader(T value) { Instance = value; }
                public int32 Read() { return int32(Owner<T>.Value); }
            }
            int32 Main() {
                Reader<int32> first = new Reader<int32>(1);
                Reader<float64> second = new Reader<float64>(2);
                return first.Read() + second.Read();
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var ownerFields = compilation.Program!.StaticFields.Where(
            static field => field.Owner.Name.StartsWith(
                "Owner<", StringComparison.Ordinal)).ToArray();
        Assert.That(ownerFields, Has.Length.EqualTo(2));
        Assert.That(ownerFields.Select(static field =>
                field.FriendTargets.Single().GenericDefinitionIndex >= 0),
            Is.EqualTo([
                friendTarget.Contains('_', StringComparison.Ordinal),
                friendTarget.Contains('_', StringComparison.Ordinal),
            ]));
    }

    [Test]
    public void GenericFriendWildcardDoesNotGrantAnUnlistedType()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Owner<T> {
                T Instance;
                friend<Reader<_>> static T Value = 1;
            }
            struct Reader<T> {
                T Instance;
                public Reader(T value) { Instance = value; }
                public int32 Read() { return int32(Owner<T>.Value); }
            }
            struct Other<T> {
                T Instance;
                public Other(T value) { Instance = value; }
                public int32 Read() { return int32(Owner<T>.Value); }
            }
            int32 Main() {
                Other<int32> value = new Other<int32>(1);
                return value.Read();
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3023"
                && diagnostic.Message.Contains(
                    "is inaccessible from this declaration",
                    StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void LazyFieldUsesIndependentExactCoreRegistration()
    {
        const string source = """
            struct Example {
                int32 Instance;
                public static int32 Eager = 1;
                [Terrum.LazyInitialization()]
                public static int32 Deferred = Eager + 1;
            }
            public int32 Read() { return Example.Deferred; }
            """;
        var registration = CreateLazyRegistration();
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                LazyInitializationCoreRegistration = registration,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(program.StaticFields.Select(
                static field => (field.Name, field.IsLazy)),
            Is.EqualTo([("Eager", false), ("Deferred", true)]));
        Assert.That(program.StaticInitializationGroups,
            Has.Length.EqualTo(1));
        Assert.That(program.StaticInitializationGroups[0]
                .EagerInitializerFunctionIndexes.Select(index =>
                    program.Functions[index].StaticFieldInitializerIndex),
            Is.EqualTo([0]));
        var readFunction = program.Functions.Single(
            static function => function.Name == "Read");
        var lazyFieldRead = -1;
        BoundStaticFieldAccessAuthority? readAuthority = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(readFunction.Body,
            expression => {
                if (expression is BoundStaticFieldRead staticFieldRead)
                {
                    lazyFieldRead = staticFieldRead.Field;
                    readAuthority = staticFieldRead.Authority;
                }
            }), Is.True);
        Assert.That(lazyFieldRead, Is.EqualTo(1));
        Assert.That(readAuthority,
            Is.EqualTo(BoundStaticFieldAccessAuthority.EagerGroupCompletion));
        Assert.That(program.LazyInitializationCoreRegistration,
            Is.SameAs(registration));

        var unregistered = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(unregistered.HasErrors, Is.True);
    }

    [Test]
    public void LazyFieldSelectorPreservesAliasAndFieldIdentity()
    {
        const string source = """
            alias Lazy = Terrum.LazyInitialization;
            [field(Deferred): Lazy]
            struct Example {
                int32 Instance;
                public static int32 Eager = 1;
                public static int32 Deferred = Eager + 1;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                LazyInitializationCoreRegistration =
                    CreateLazyRegistration(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program!.StaticFields.Select(
                static field => (field.Name, field.IsLazy)),
            Is.EqualTo([("Eager", false), ("Deferred", true)]));
        Assert.That(compilation.Program.StaticInitializationGroups[0]
                .EagerInitializerFunctionIndexes.Select(index =>
                    compilation.Program.Functions[index]
                        .StaticFieldInitializerIndex),
            Is.EqualTo([0]));
    }

    [Test]
    public void LazyFieldRejectsRepeatedDirectAndSelectedPlacement()
    {
        const string source = """
            [field(Deferred): Terrum.LazyInitialization]
            struct Example {
                int32 Instance;
                [Terrum.LazyInitialization]
                public static int32 Deferred = 1;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                LazyInitializationCoreRegistration =
                    CreateLazyRegistration(),
            });

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Message.Contains(
                "nonrepeatable across direct and selected field placements",
                StringComparison.Ordinal)), Is.True);
    }

    [TestCase("""
        [Terrum.StaticInitializer(
            Terrum.StaticInitializationTrigger.GroupedStaticFieldAccess)]
        static partial Example();
        """)]
    [TestCase("""
        static partial Example() {}
        """)]
    public void PartialStaticConstructorRequiresBothParts(string parts)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $$"""
            struct Example {
                {{parts}}
            }
            """), new CompilationOptions {
                StaticInitializationCoreRegistration = CreateRegistration(),
            });

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Message.Contains(
                "partial static constructor requires exactly one",
                StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public void StaticConstructorRequiresRegisteredExplicitTrigger()
    {
        var missing = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Example {
                int32 Value;
                static Example() {}
            }
            """));
        var unregistered = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Example {
                int32 Value;
                [Terrum.StaticInitializer(
                    Terrum.StaticInitializationTrigger.GroupedStaticFieldAccess)]
                static Example() {}
            }
            """));

        Assert.That(missing.HasErrors, Is.True);
        Assert.That(missing.Diagnostics.Any(static diagnostic =>
            diagnostic.Message.Contains("requires one registered StaticInitializer",
                StringComparison.Ordinal)), Is.True);
        Assert.That(unregistered.HasErrors, Is.True);
    }

    private static StaticInitializationCoreRegistrationSet CreateRegistration(
        CoreEnumDefinition? triggerEnum = null,
        CoreStaticInitializerAttributeSchema? schema = null)
    {
        var selectedEnum = triggerEnum ?? CreateTriggerEnum();
        var selectedSchema = schema ?? CreateSchema();
        var bindings = CreateBindings(selectedEnum, selectedSchema,
            includeAttributeSnapshot: true);
        return StaticInitializationCoreRegistrationSet.Create(bindings,
            selectedEnum, selectedSchema);
    }

    private static LazyInitializationCoreRegistrationSet
        CreateLazyRegistration()
    {
        var schema = new CoreLazyInitializationAttributeSchema(
            LazyInitializationIdentity, "Terrum.LazyInitialization",
            CoreLazyInitializationAttributeTarget.PrimaryStaticField,
            true, 0, true, true, false);
        var bindings = CoreProviderModuleBindings.Create("sample.consumer", [
            new CoreProviderModuleOrigin("core.lazy", "revision-1",
                new CoreProviderSchemaSelection("core.lazy-initialization", 1),
                1, 0, 0),
        ], [CoreProviderDeclarationSnapshot.From(schema)]);
        return LazyInitializationCoreRegistrationSet.Create(bindings, schema);
    }

    private static CoreProviderModuleBindings CreateBindings(
        CoreEnumDefinition triggerEnum,
        CoreStaticInitializerAttributeSchema schema,
        bool includeAttributeSnapshot)
    {
        var declarations = ImmutableArray.CreateBuilder<
            CoreProviderDeclarationSnapshot>();
        declarations.Add(CoreProviderDeclarationSnapshot.From(triggerEnum));
        if (includeAttributeSnapshot)
        {
            declarations.Add(CoreProviderDeclarationSnapshot.From(schema));
        }

        return CoreProviderModuleBindings.Create("sample.consumer", [
            new CoreProviderModuleOrigin("core.provider", "revision-1",
                new CoreProviderSchemaSelection("core.static-initializer", 1),
                1, 0, 0),
        ], declarations.ToImmutable());
    }

    private static CoreEnumDefinition CreateTriggerEnum()
        => new(TriggerEnumIdentity, "Terrum.StaticInitializationTrigger",
            PrimitiveType.Int32, [
                CreateMember("grouped-static-field-access",
                    "GroupedStaticFieldAccess", 0),
                CreateMember("runtime-static-member-access",
                    "RuntimeStaticMemberAccess", 1),
            ], 0, true, true, false);

    private static CoreEnumMemberDefinition CreateMember(string exportKey,
        string name, int value)
        => new(new CoreDeclarationIdentity("core.provider", exportKey,
                CoreDeclarationKind.EnumMember),
            TriggerEnumIdentity, name, value, true) {
            DeclarationKey = exportKey,
        };

    private static CoreStaticInitializerAttributeSchema CreateSchema()
        => new(StaticInitializerIdentity, "Terrum.StaticInitializer", [
                new CoreStaticInitializerAttributeArgument("Trigger",
                    TriggerEnumIdentity, true, true, false, false),
            ], CoreStaticInitializerAttributeTarget.PrimaryStaticConstructor,
            true, 0, true, true, false);

    private static CoreDeclarationIdentity TriggerEnumIdentity
        => new("core.provider", "static-initialization-trigger",
            CoreDeclarationKind.EnumType);

    private static CoreDeclarationIdentity StaticInitializerIdentity
        => new("core.provider", "static-initializer",
            CoreDeclarationKind.AttributeSchema);

    private static CoreDeclarationIdentity LazyInitializationIdentity
        => new("core.lazy", "lazy-initialization",
            CoreDeclarationKind.AttributeSchema);
}
