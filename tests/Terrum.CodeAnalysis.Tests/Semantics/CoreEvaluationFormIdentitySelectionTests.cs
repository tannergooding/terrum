// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class CoreEvaluationFormIdentitySelectionTests
{
    [Test]
    public void ConstEvalRequiresExactSelectedSchemaAndPreservesAliasIdentity()
    {
        const string source = """
            using Core;
            alias Evaluation = ConstEval;
            [Evaluation] int32 Identity(int32 value) {
                return value;
            }
            int32 Main() {
                const int32 result = Identity(42);
                return result;
            }
            """;

        var unselected = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(unselected.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3001"), Is.True);

        var selection = CreateConstEvalSelection();
        var selected = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreEvaluationFormIdentitySelection = selection,
            });

        Assert.That(selected.HasErrors, Is.False,
            string.Join(Environment.NewLine, selected.Diagnostics));
        Assert.That(selected.Program!.CoreEvaluationFormIdentitySelection,
            Is.SameAs(selection));
        Assert.That(selected.Program.CoreEvaluationFormIdentitySelections,
            Is.EqualTo([selection]));
        var main = selected.Program.Functions.Single(
            static function => function.Name == "Main");
        var local = (BoundLocal)main.Body.Statements[0];
        Assert.That(local.Initializer, Is.TypeOf<BoundConstant>());
        Assert.That(((BoundConstant)local.Initializer!).Value,
            Is.EqualTo(42));
        var constEvalFunction = selected.Program!.Functions.Single(
            static function => function.Name == "Identity");
        Assert.That(constEvalFunction.IsConstEval, Is.True);
        Assert.That(constEvalFunction.HasPureEvaluationForm, Is.True);
        var constEvalSchema = constEvalFunction.ConstEvalSchema!;
        Assert.That(constEvalSchema.Selector,
            Is.EqualTo(selection.ConstEvalSchema));
        Assert.That(ReferenceEquals(
            constEvalSchema.Definition.Catalog,
            selection.LocalSchemaDeclarations), Is.True);
    }

    [Test]
    public void PureRequiresExactSelectedSchemaAndPreservesAliasIdentity()
    {
        const string source = """
            using Core;
            alias Stable = Pure;
            [Pure] int32 Helper(int32 value) { return value; }
            [Stable] int32 Identity(int32 value) { return Helper(value); }
            [Pure] bool IsNonnegative(int32 value) { return value >= 0; }
            """;

        var unselected = Compilation.CreateLibrary(
            SyntaxTree.Parse(source));
        Assert.That(unselected.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3001"), Is.True);

        var selection = CreatePureSelection();
        var selected = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection = selection,
            });

        Assert.That(selected.HasErrors, Is.False,
            string.Join(Environment.NewLine, selected.Diagnostics));
        var function = selected.Program!.Functions.Single(
            static item => item.Name == "Identity");
        Assert.That(function.PureEvaluationFormSchema, Is.Not.Null);
        Assert.That(function.PureEvaluationFormSchema!.Selector,
            Is.EqualTo(selection.PureSchema));
        Assert.That(ReferenceEquals(
            function.PureEvaluationFormSchema.Definition.Catalog,
            selection.LocalSchemaDeclarations), Is.True);
        Assert.That(selected.Program.Functions.Single(
                static item => item.Name == "IsNonnegative")
            .PureEvaluationFormSchema, Is.Not.Null);
    }

    [Test]
    public void PureRejectsCallsToUnverifiedLocalCallees()
    {
        const string source = """
            using Core;
            struct State {
                int32 Instance;
                public static mut int32 Value = 0;
            }
            int32 Mutate(int32 value) {
                State.Value = value;
                return value;
            }
            [Pure] int32 Call(int32 value) { return Mutate(value); }
            """;

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3029"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void PureRejectsExternalCallsWithoutPublishedPureEffects()
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Impure() { return 42; }"));
        Assert.That(provider.HasErrors, Is.False,
            string.Join(Environment.NewLine, provider.Diagnostics));
        var artifact = ModuleCompiler.CreateProducer(provider.Program!,
            ["Impure"]);
        var reference = ModuleCompiler.CreateReference(artifact);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Core;
            [Pure] int32 Call() { return Impure(); }
            """), reference, new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3029"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void PureArithmeticUsesTheSelectedNonthrowingProfile()
    {
        const string source = """
            using Core;
            [Pure] int32 Add(int32 left, int32 right) {
                return left + right;
            }
            """;

        var checkedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });
        Assert.That(checkedCompilation.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3029"), Is.True);

        var wrappingCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                SourceArithmeticProfile = SourceArithmeticProfile.Release,
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });
        Assert.That(wrappingCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, wrappingCompilation.Diagnostics));
    }

    [Test]
    public void PureCheckedIntegerOperationsUseDeclarationPreconditions()
    {
        const string source = """
            using Core;
            [Pure] int32 Increment(int32 value)
                require<Failure> value < int32.MaxValue
            {
                return value + 1;
            }
            [Pure] int32 Divide(int32 value, int32 divisor)
                require<Failure> divisor > 0
            {
                return value / divisor;
            }
            [Pure] int32 Remainder(int32 value, int32 divisor)
                require<Failure> divisor > 0
            {
                return value % divisor;
            }
            [Pure] int32 Shift(int32 value, int32 count)
                require<Failure> count >= 0 && count < 32
            {
                return value << count;
            }
            [Pure] int32 Narrow(int64 value)
                require<Failure> value >= int64(int32.MinValue)
                require<Failure> value <= int64(int32.MaxValue)
            {
                return int32(value);
            }
            public class Failure {
                public int32 Value;
                public Failure() { Value = 0; }
            }
            """;

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var insufficientDomain = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Core;
                [Pure] int32 Increment(int32 value)
                    require<Failure> value <= int32.MaxValue
                {
                    return value + 1;
                }
                public class Failure {
                    public int32 Value;
                    public Failure() { Value = 0; }
                }
                """), new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });
        Assert.That(insufficientDomain.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3029"), Is.True,
            string.Join(Environment.NewLine,
                insufficientDomain.Diagnostics));
    }

    [Test]
    public void PureChecksDeclarationConditionExpressions()
    {
        const string source = """
            using Core;
            [Pure] int32 Identity(int32 value)
                require<Failure> value >= 0
                ensure return == value
            {
                return value;
            }
            public class Failure {
                public int32 Value;
                public Failure() { Value = 0; }
            }
            """;

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
                CoreContractFailureIdentitySelection
                    = new CoreContractFailureIdentitySelection(
                        "consumer.module",
                        new CoreContractFailureDeclarationIdentity(
                            "core.contracts", "requirement-exception"),
                        new CoreContractFailureDeclarationIdentity(
                            "core.contracts",
                            "postcondition-exception"),
                        new CoreContractFailureDeclarationIdentity(
                            "core.contracts",
                            "require-exception-schema")),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void PureCanInitializeDeclaredOutputDestinations()
    {
        const string source = """
            using Core;
            [Pure] void Initialize(ref<init int32> output) {
                *output = 42;
            }
            [Pure] bool TryInitialize(out int32 output) {
                output = 42;
                return true;
            }
            """;

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program!.Functions.Count(
            static function => function.PureEvaluationFormSchema is not null),
            Is.EqualTo(2));
    }

    [Test]
    public void PureMayUpdateLocalCopyFields()
    {
        const string source = """
            using Core;
            struct Meter {
                public mut float32 Value;
                [Pure] public Meter(float32 value) { Value = value; }
            }
            [Pure] float32 Advance(float32 value) {
                mut Meter meter = new Meter(value);
                meter.Value++;
                meter.Value += float32(1);
                return meter.Value;
            }
            """;

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void PureGenericCopyMethodCanBeVerifiedAndCalled()
    {
        const string source = """
            using Core;
            [Pure] T Identity<T>(T value) {
                return value;
            }
            [Pure] int32 Call(int32 value) {
                return Identity<int32>(value);
            }
            """;

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void PureGenericStructValuesAndMethodsCanBeVerified()
    {
        const string source = """
            using Core;
            struct Box<T> {
                public T Value;
                public Box(T value) { Value = value; }
            }
            [Pure] Box<T> Identity<T>(Box<T> value) {
                return value;
            }
            [Pure] int32 Read(Box<int32> value) {
                return Identity(value).Value;
            }
            int32 Main() {
                Box<int32> value = new Box<int32>(42);
                return Read(value);
            }
            """;
        var compilation = Compilation.Create(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void PureClosedGenericStructRejectsPointerPayloads()
    {
        const string source = """
            using Core;
            struct Box<T> {
                public T Value;
            }
            [Pure] unsafe Box<ptr<int32>> Identity(
                Box<ptr<int32>> value) {
                return value;
            }
            """;

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });

        Assert.That(compilation.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3029"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void PureGenericMethodCanReturnSourceGenericStruct()
    {
        const string source = """
            using Core;
            struct Box<T> {
                public T Value;
            }
            [Pure] Box<T> Identity<T>(Box<T> value) {
                return value;
            }
            [Pure] int32 Read(Box<int32> value) {
                return Identity<int32>(value).Value;
            }
            """;

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void PureGenericMethodCanConstructSourceGenericCopyResult()
    {
        const string source = """
            using Core;
            struct Box<T> {
                public T Value;
                [Pure] public Box(T value) { Value = value; }
            }
            [Pure] Box<T> Wrap<T>(T value) {
                return new Box<T>(value);
            }
            [Pure] int32 Read(int32 value) {
                return Wrap(value).Value;
            }
            int32 Main() {
                return Read(42);
            }
            """;

        var compilation = Compilation.Create(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void PureGenericMethodCanProveSourceGenericConstructorWithoutCall()
    {
        const string source = """
            using Core;
            struct Box<T> {
                public T Value;
                [Pure] public Box(T value) { Value = value; }
            }
            [Pure] Box<T> Wrap<T>(T value) {
                return new Box<T>(value);
            }
            """;

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void PureGenericMethodRejectsUnsafeSourceGenericConstructor()
    {
        const string source = """
            using Core;
            struct Box<T> {
                public T Value;
                [Pure] public unsafe Box(T value) { Value = value; }
            }
            [Pure] Box<T> Wrap<T>(T value) {
                return new Box<T>(value);
            }
            """;

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });

        Assert.That(compilation.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3028"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void PureGenericMethodRejectsImpureSourceGenericConstructor()
    {
        const string source = """
            using Core;
            int32 Impure() { return 1; }
            struct Box<T> {
                public T Value;
                [Pure] public Box(T value) {
                    Impure();
                    Value = value;
                }
            }
            [Pure] Box<T> Wrap<T>(T value) {
                return new Box<T>(value);
            }
            """;

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });

        Assert.That(compilation.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3007"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void PureGenericMethodRejectsUnverifiedSourceGenericConstructor()
    {
        const string source = """
            using Core;
            struct Box<T> {
                public T Value;
                public Box(T value) { Value = value; }
            }
            [Pure] Box<T> Wrap<T>(T value) {
                return new Box<T>(value);
            }
            """;

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });

        Assert.That(compilation.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3007"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void GenericMethodCannotConstructCopyGenericTypeFromUniqueFormal()
    {
        const string source = """
            struct Box<T> {
                public T Value;
                public Box(T value) { Value = value; }
            }
            Box<T> Wrap<T>(T value) where T : unique {
                return new Box<T>(move value);
            }
            """;

        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));

        Assert.That(compilation.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("ownership domain",
                    StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void ConstEvalResolvesExactQualifiedSchemaName()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
                "[Core.ConstEval] int32 Value() { return 42; }"),
            new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreateConstEvalSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void ExplicitTestHostSelectionBindsUnqualifiedSchemaName()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
                "[ConstEval] int32 Value() { return 42; }"),
            CoreEvaluationFormTestHelpers.Options());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void UnusedEvaluationFormRoleIsRejected()
    {
        Assert.That(() => Compilation.CreateLibrary(SyntaxTree.Parse(
                "public int32 Value() { return 42; }"),
            new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreateConstEvalSelection(),
            }), Throws.ArgumentException);
    }

    [Test]
    public void ConstEvalGetterUsesSelectedAccessorSchema()
    {
        const string source = """
            using Core;
            struct ValueProvider {
                public int32 Field;
                [get: ConstEval]
                public static int32 Value { get { return 42; } }
            }
            int32 Main() {
                const int32 result = ValueProvider.Value;
                return result;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreateConstEvalSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = compilation.Program!.Functions.Single(
            static function => function.Name == "Main");
        var constant = (BoundConstant)((BoundLocal)
            main.Body.Statements[0]).Initializer!;
        Assert.That(constant.Value, Is.EqualTo(42));
        var getter = compilation.Program.Functions.Single(
            static function => function.PropertyName == "Value");
        Assert.That(getter.IsConstEval, Is.True);
    }

    [Test]
    public void ConstEvalInlineGetterUsesSelectedAccessorSchema()
    {
        const string source = """
            using Core;
            struct ValueProvider {
                int32 Field;
                public static int32 Value {
                    [ConstEval] get { return 42; }
                }
            }
            int32 Main() {
                const int32 result = ValueProvider.Value;
                return result;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreateConstEvalSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var getter = compilation.Program!.Functions.Single(
            static function => function.PropertyName == "Value");
        Assert.That(getter.IsConstEval, Is.True);
    }

    [Test]
    public void PureGetterUsesSelectedAccessorSchema()
    {
        const string source = """
            using Core;
            struct ValueProvider {
                public int32 Field;
                [get: Pure]
                public static int32 Value { get { return 42; } }
            }
            """;

        var selection = CreatePureSelection();
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection = selection,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var getter = compilation.Program!.Functions.Single(
            static function => function.PropertyName == "Value");
        Assert.That(getter.PureEvaluationFormSchema, Is.Not.Null);
        Assert.That(getter.PureEvaluationFormSchema!.Selector,
            Is.EqualTo(selection.PureSchema));
    }

    [Test]
    public void PureInstanceGetterCanReadCopyReceiverState()
    {
        const string source = """
            using Core;
            struct ValueProvider {
                int32 Field;
                public ValueProvider(int32 field) { Field = field; }
                [get: Pure]
                public int32 Value { get { return Field; } }
            }
            """;

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var getter = compilation.Program!.Functions.Single(
            static function => function.PropertyName == "Value");
        Assert.That(getter.PureEvaluationFormSchema, Is.Not.Null);
    }

    [Test]
    public void PureCallableCanReadCheckedReferenceInput()
    {
        const string source = """
            using Core;
            [Pure] int32 Read(ref<int32> value) { return *value; }
            """;

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program!.Functions.Single(
                static function => function.Name == "Read")
            .PureEvaluationFormSchema, Is.Not.Null);
    }

    [Test]
    public void PureCallableCanReadImplicitReferenceInput()
    {
        const string source = """
            using Core;
            [Pure] int32 Read(implicit ref<int32> value) { return value; }
            """;

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program!.Functions.Single(
                static function => function.Name == "Read")
            .PureEvaluationFormSchema, Is.Not.Null);
    }

    [Test]
    public void OrdinaryCallToPureFunctionRetainsRuntimeBehavior()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture
                == Architecture.X64);
        const string source = """
            using Core;
            [Pure] int32 Identity(int32 value) { return value; }
            int32 Main() { return Identity(42); }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void PureInlineGetterUsesSelectedAccessorSchema()
    {
        const string source = """
            using Core;
            struct ValueProvider {
                int32 Field;
                public static int32 Value {
                    [Pure] get { return 42; }
                }
            }
            """;

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var getter = compilation.Program!.Functions.Single(
            static function => function.PropertyName == "Value");
        Assert.That(getter.PureEvaluationFormSchema, Is.Not.Null);
    }

    [Test]
    public void PureReadonlyClassGetterReadsReceiverState()
    {
        const string source = """
            using Core;
            struct Metrics {
                public int32 Value;
                public Metrics(int32 value) { Value = value; }
            }
            public mut class Box {
                mut Metrics _metrics;
                public Box(int32 value) {
                    _metrics = new Metrics(value);
                }
                [get: Pure] public int32 Count {
                    get { return _metrics.Value; }
                }
                [get: Pure] public Metrics Snapshot {
                    get { return _metrics; }
                }
            }
            [Pure] int32 ReadCount(Box box) {
                return box.Count;
            }
            [Pure] Metrics ReadSnapshot(Box box) {
                return box.Snapshot;
            }
            """;

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var getter = compilation.Program!.Functions.Single(
            static function => function.PropertyName == "Count");
        Assert.That(getter.PureEvaluationFormSchema, Is.Not.Null);
        var snapshotGetter = compilation.Program.Functions.Single(
            static function => function.PropertyName == "Snapshot");
        Assert.That(snapshotGetter.PureEvaluationFormSchema, Is.Not.Null);
    }

    [Test]
    public void PureRejectsWritableManagedClassReceiver()
    {
        const string source = """
            using Core;
            mut class Box {
                mut int32 _value;
                public Box(int32 value) { _value = value; }
                [Pure] public mut int32 Increment() {
                    _value = _value + 1;
                    return _value;
                }
            }
            """;

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });

        Assert.That(compilation.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3029"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void PureEligibleConstructorInitializesCopyResult()
    {
        const string source = """
            using Core;
            struct ValueProvider {
                int32 Field;
                [Pure] public ValueProvider(int32 field) {
                    Field = field;
                }
            }
            """;

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var constructor = compilation.Program!.Functions.Single(
            static function => function.IsConstructor);
        Assert.That(constructor.PureEvaluationFormSchema, Is.Not.Null);
    }

    [Test]
    public void PureAndConstEvalUseDistinctSelectedSchemaIdentities()
    {
        var pureDeclaration = CreateSchemaDeclaration("Core.Pure");
        var constEvalDeclaration
            = CreateSchemaDeclaration("Core.ConstEval");
        var catalog = CoreEvaluationFormSchemaDeclarations.Create(
            new CoreProviderModuleOrigin("consumer.module", "revision-1",
                new CoreProviderSchemaSelection("evaluation-form", 1),
                1, 0, 0),
            [pureDeclaration, constEvalDeclaration],
            [
                new("pure", pureDeclaration),
                new("const-eval", constEvalDeclaration),
            ]);
        var selection = new CoreEvaluationFormIdentitySelection(
            "consumer.module",
            new CoreEvaluationFormSchemaSelector("consumer.module",
                "pure"),
            new CoreEvaluationFormSchemaSelector("consumer.module",
                "const-eval"),
            catalog);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Core;
            [Pure] [ConstEval]
            int32 Identity(int32 value) { return value; }
            """), new CompilationOptions {
                CoreEvaluationFormIdentitySelection = selection,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var identity = compilation.Program!.Functions.Single(
            static function => function.Name == "Identity");
        Assert.That(identity.PureEvaluationFormSchema!.Selector,
            Is.EqualTo(selection.PureSchema));
        Assert.That(identity.ConstEvalSchema!.Selector,
            Is.EqualTo(selection.ConstEvalSchema));
        Assert.That(identity.HasPureEvaluationForm, Is.True);
    }

    [Test]
    public void ConstEvalNameLookupIncludesEnclosingNamespaces()
    {
        var declaration = CreateSchemaDeclaration("Root.ConstEval");
        var catalog = CreateCatalog(declaration, "const-eval");
        var selection = new CoreEvaluationFormIdentitySelection(
            "consumer.module",
            ConstEvalSchema: new CoreEvaluationFormSchemaSelector(
                "consumer.module", "const-eval"),
            LocalSchemaDeclarations: catalog);
        const string source = """
            namespace Root {
                namespace Nested {
                    [ConstEval] int32 Value() { return 42; }
                    int32 Main() {
                        const int32 result = Value();
                        return result;
                    }
                }
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreEvaluationFormIdentitySelection = selection,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void EvaluationFormSelectionRejectsMissingLocalCatalogAndRoleCollision()
    {
        var missingLocalCatalog = new CoreEvaluationFormIdentitySelection(
            "consumer.module",
            ConstEvalSchema: new CoreEvaluationFormSchemaSelector(
                "consumer.module", "const-eval"));
        var sharedSchema = CreateSchemaDeclaration("Core.Evaluation");
        var sharedCatalog = CreateCatalog(sharedSchema, "evaluation");
        var collidingRoles = new CoreEvaluationFormIdentitySelection(
            "consumer.module",
            PureSchema: new CoreEvaluationFormSchemaSelector(
                "consumer.module", "evaluation"),
            ConstEvalSchema: new CoreEvaluationFormSchemaSelector(
                "consumer.module", "evaluation"),
            LocalSchemaDeclarations: sharedCatalog);

        Assert.That(() => Compilation.CreateLibrary(
                SyntaxTree.Parse("public int32 Identity() { return 1; }"),
                new CompilationOptions {
                    CoreEvaluationFormIdentitySelection
                        = missingLocalCatalog,
                }), Throws.ArgumentException);
        Assert.That(() => Compilation.CreateLibrary(
                SyntaxTree.Parse("public int32 Identity() { return 1; }"),
                new CompilationOptions {
                    CoreEvaluationFormIdentitySelection = collidingRoles,
                }), Throws.ArgumentException);
    }

    [Test]
    public void SchemaCatalogRejectsUnsupportedProviderRequirements()
    {
        var declaration = CreateSchemaDeclaration("Core.ConstEval");
        var origins = new[] {
            new CoreProviderModuleOrigin("provider.module", "revision-1",
                new CoreProviderSchemaSelection("evaluation-form", 1),
                2, 0, 0),
            new CoreProviderModuleOrigin("provider.module", "revision-1",
                new CoreProviderSchemaSelection("evaluation-form", 1),
                1, 2, 0),
            new CoreProviderModuleOrigin("provider.module", "revision-1",
                new CoreProviderSchemaSelection("evaluation-form", 1),
                1, 0, 2),
        };

        foreach (var origin in origins)
        {
            Assert.That(() =>
                CoreEvaluationFormSchemaDeclarations.Create(origin,
                    [declaration], [new("const-eval", declaration)]),
                Throws.ArgumentException);
        }
    }

    [Test]
    public void SchemaCatalogRequiresTheExactNonrepeatableZeroArgumentContract()
    {
        var origin = new CoreProviderModuleOrigin("provider.module",
            "revision-1",
            new CoreProviderSchemaSelection("evaluation-form", 1),
            1, 0, 0);
        var targets = ImmutableArray.Create(
            CoreEvaluationFormTargetKind.OrdinaryNamedCallable,
            CoreEvaluationFormTargetKind.IndividualAccessor,
            CoreEvaluationFormTargetKind.EligibleInitializationConstructor);
        var invalidDeclarations = new[] {
            new CoreEvaluationFormSchemaDeclaration("Core.ConstEval", 1, 0,
                targets, true, true, true, false),
            new CoreEvaluationFormSchemaDeclaration("Core.ConstEval", 0, 1,
                targets, true, true, true, false),
            new CoreEvaluationFormSchemaDeclaration("Core.ConstEval", 0, 0,
                [CoreEvaluationFormTargetKind.OrdinaryNamedCallable],
                true, true, true, false),
            new CoreEvaluationFormSchemaDeclaration("Core.ConstEval", 0, 0,
                targets, true, true, true, true),
        };

        foreach (var declaration in invalidDeclarations)
        {
            Assert.That(() =>
                CoreEvaluationFormSchemaDeclarations.Create(origin,
                    [declaration], [new("const-eval", declaration)]),
                Throws.ArgumentException);
        }
    }

    [Test]
    public void AliasedLocalExportsRetainOneDeclarationIdentity()
    {
        var declaration = CreateSchemaDeclaration("Core.Evaluation");
        var catalog = CoreEvaluationFormSchemaDeclarations.Create(
            new CoreProviderModuleOrigin("consumer.module", "revision-1",
                new CoreProviderSchemaSelection("evaluation-form", 1),
                1, 0, 0),
            [declaration],
            [
                new("pure-alias", declaration),
                new("const-eval-alias", declaration),
            ]);
        var selection = new CoreEvaluationFormIdentitySelection(
            "consumer.module",
            new CoreEvaluationFormSchemaSelector("consumer.module",
                "pure-alias"),
            new CoreEvaluationFormSchemaSelector("consumer.module",
                "const-eval-alias"),
            catalog);

        Assert.That(() => new CompilationReference([], [],
                BoundRuntimePolicy.FailFast,
                coreEvaluationFormIdentitySelections: [selection]),
            Throws.ArgumentException);
    }

    [Test]
    public void SameNameAcrossProviderCatalogsRemainsAmbiguous()
    {
        var declaration = CreateSchemaDeclaration("Core.Evaluation");
        var pureCatalog = CreateCatalog(declaration, "pure",
            "provider.pure");
        var constEvalCatalog = CreateCatalog(declaration, "const-eval",
            "provider.const-eval");
        var selection = new CoreEvaluationFormIdentitySelection(
            "consumer.module",
            new CoreEvaluationFormSchemaSelector("provider.pure", "pure"),
            new CoreEvaluationFormSchemaSelector("provider.const-eval",
                "const-eval"));
        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            coreEvaluationFormSchemaProviders: [
                pureCatalog, constEvalCatalog,
            ]);

        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
                "[Core.Evaluation] int32 Value() { return 1; }"),
            reference, new CompilationOptions {
                CoreEvaluationFormIdentitySelection = selection,
            });

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3001"), Is.True);
    }

    [Test]
    public void SelectedSchemaRuntimeMustMatchTheCompilationPolicy()
    {
        var localDeclaration = CreateSchemaDeclaration("Core.ConstEval");
        var localCatalog = CreateCatalog(localDeclaration, "const-eval",
            runtimePolicy: 1);
        var localSelection = new CoreEvaluationFormIdentitySelection(
            "consumer.module",
            ConstEvalSchema: new CoreEvaluationFormSchemaSelector(
                "consumer.module", "const-eval"),
            LocalSchemaDeclarations: localCatalog);
        var externalDeclaration = CreateSchemaDeclaration("Core.ConstEval");
        var externalCatalog = CreateCatalog(externalDeclaration,
            "const-eval", "provider.module", runtimePolicy: 1);
        var externalSelection = new CoreEvaluationFormIdentitySelection(
            "consumer.module",
            ConstEvalSchema: new CoreEvaluationFormSchemaSelector(
                "provider.module", "const-eval"));
        var externalReference = new CompilationReference([], [],
            BoundRuntimePolicy.CheckedExceptions,
            coreEvaluationFormSchemaProviders: [externalCatalog]);

        Assert.That(() => Compilation.CreateLibrary(
                SyntaxTree.Parse("public int32 Identity() { return 1; }"),
                new CompilationOptions {
                    CoreEvaluationFormIdentitySelection = localSelection,
                }), Throws.ArgumentException);
        Assert.That(() => Compilation.CreateLibrary(
                SyntaxTree.Parse("public int32 Identity() { return 1; }"),
                externalReference, new CompilationOptions {
                    CoreEvaluationFormIdentitySelection = externalSelection,
                }), Throws.ArgumentException);
    }

    [Test]
    public void SameNamedAliasDoesNotAcquireSelectedSchemaIdentity()
    {
        const string source = """
            using Core;
            alias ConstEval = int32;
            [ConstEval] int32 Add(int32 left, int32 right) {
                return left + right;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreateConstEvalSelection(),
            });

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3001"), Is.True);
    }

    [Test]
    public void ReferenceAndOptionSelectionsCannotOverlap()
    {
        var selection = CreateConstEvalSelection();
        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            coreEvaluationFormIdentitySelections: [selection]);

        Assert.That(() => Compilation.CreateLibrary(SyntaxTree.Parse(
                "public int32 Identity() { return 1; }"),
            reference, new CompilationOptions {
                CoreEvaluationFormIdentitySelection = selection,
            }), Throws.ArgumentException);
    }

    [Test]
    public void ReferenceRejectsLocalCatalogAsExternalProvider()
    {
        var selection = CreateConstEvalSelection();
        var localCatalog = CreateCatalog(
            selection.LocalSchemaDeclarations!.Declarations[0],
            "const-eval");

        Assert.That(() => new CompilationReference([], [],
                BoundRuntimePolicy.FailFast,
                coreEvaluationFormIdentitySelections: [selection],
                coreEvaluationFormSchemaProviders: [localCatalog]),
            Throws.ArgumentException);
    }

    [Test]
    public void ExternalSelectionResolvesOnlyThroughCheckedReferenceCatalog()
    {
        var declaration = CreateSchemaDeclaration("Core.ConstEval");
        var catalog = CreateCatalog(declaration, "const-eval",
            "provider.module");
        var selection = new CoreEvaluationFormIdentitySelection(
            "consumer.module",
            ConstEvalSchema: new CoreEvaluationFormSchemaSelector(
                "provider.module", "const-eval"));
        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            coreEvaluationFormSchemaProviders: [catalog]);
        const string source = """
            using Core;
            [ConstEval] int32 Value() { return 42; }
            int32 Main() { const int32 result = Value(); return result; }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            reference, new CompilationOptions {
                CoreEvaluationFormIdentitySelection = selection,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program!
                .CoreEvaluationFormIdentitySelection,
            Is.SameAs(selection));
        Assert.That(compilation.Program.CoreEvaluationFormSchemaProviders,
            Is.EqualTo([catalog]));

        Assert.That(() => Compilation.CreateLibrary(
                SyntaxTree.Parse(source), new CompilationOptions {
                    CoreEvaluationFormIdentitySelection = selection,
                }), Throws.ArgumentException);
    }

    [Test]
    public void ExternalPureSelectionResolvesOnlyThroughCheckedReferenceCatalog()
    {
        var declaration = CreateSchemaDeclaration("Core.Pure");
        var catalog = CreateCatalog(declaration, "pure",
            "provider.module");
        var selection = new CoreEvaluationFormIdentitySelection(
            "consumer.module",
            PureSchema: new CoreEvaluationFormSchemaSelector(
                "provider.module", "pure"));
        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            coreEvaluationFormSchemaProviders: [catalog]);
        const string source = """
            using Core;
            [Pure] int32 Value(int32 value) { return value; }
            """;

        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            reference, new CompilationOptions {
                CoreEvaluationFormIdentitySelection = selection,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = compilation.Program!.Functions.Single(
            static item => item.Name == "Value");
        Assert.That(function.PureEvaluationFormSchema!.Selector,
            Is.EqualTo(selection.PureSchema));
        Assert.That(ReferenceEquals(
            function.PureEvaluationFormSchema.Definition.Catalog,
            catalog), Is.True);
    }

    [Test]
    public void ReferencedLocalSchemaDoesNotSelectItForTheConsumer()
    {
        var providerSelection
            = CreateConstEvalSelection("provider.module");
        var providerCatalog
            = providerSelection.LocalSchemaDeclarations!;
        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            coreEvaluationFormIdentitySelections: [providerSelection],
            coreEvaluationFormSchemaProviders: [providerCatalog]);
        var source = SyntaxTree.Parse("""
            using Core;
            [ConstEval] int32 Value() { return 42; }
            int32 Main() { const int32 result = Value(); return result; }
            """);

        var unselected = Compilation.Create(source, reference);
        Assert.That(unselected.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3001"), Is.True);

        var consumerSelection = new CoreEvaluationFormIdentitySelection(
            "consumer.module",
            ConstEvalSchema: new CoreEvaluationFormSchemaSelector(
                "provider.module", "const-eval"));
        var selected = Compilation.Create(source, reference,
            new CompilationOptions {
                CoreEvaluationFormIdentitySelection = consumerSelection,
            });

        Assert.That(selected.HasErrors, Is.False,
            string.Join(Environment.NewLine, selected.Diagnostics));
        Assert.That(selected.Program!
                .CoreEvaluationFormIdentitySelections,
            Is.EqualTo([providerSelection, consumerSelection]));
    }

    [Test]
    public void ModuleSetAndGraphReferencesRetainOriginatingSelections()
    {
        var firstSelection = CreateConstEvalSelection("consumer.first");
        var secondSelection = CreateConstEvalSelection("consumer.second");
        var references = new[] {
            new CompilationReference([], [], BoundRuntimePolicy.FailFast,
                coreEvaluationFormIdentitySelections: [firstSelection]),
            new CompilationReference([], [], BoundRuntimePolicy.FailFast,
                coreEvaluationFormIdentitySelections: [secondSelection]),
        };

        var moduleSet = ModuleCompiler.CreateReferenceSetFromReferences(
            references);
        var graph = ModuleCompiler.MergeGraphReferences(references);

        Assert.That(moduleSet.CoreEvaluationFormIdentitySelections,
            Is.EqualTo([firstSelection, secondSelection]));
        Assert.That(graph.CoreEvaluationFormIdentitySelections,
            Is.EqualTo([firstSelection, secondSelection]));
    }

    [Test]
    public void ArtifactRoutesWithoutEvaluationIdentitySupportRejectSelection()
    {
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(
                "using Core; [ConstEval] public int32 Value() { return 1; }"),
            new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreateConstEvalSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(() => ModuleCompiler.CreateProducer(
                compilation.Program!, ["Value"]),
            Throws.TypeOf<ModuleContractException>());
        Assert.That(() => TrRmExecutableCompiler.EmitStandard(
                compilation.Program!, "consumer.module",
                "selected-standard-schema", 0),
            Throws.TypeOf<NotSupportedException>());

        var pureCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(
                "using Core; [Pure] public int32 Value(int32 input) { return input; }"),
            new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreatePureSelection(),
            });
        Assert.That(pureCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, pureCompilation.Diagnostics));
        Assert.That(() => ModuleCompiler.CreateProducer(
                pureCompilation.Program!, ["Value"]),
            Throws.TypeOf<ModuleContractException>());
        Assert.That(() => TrRmExecutableCompiler.EmitStandard(
                pureCompilation.Program!, "consumer.module",
                "selected-standard-schema", 0),
            Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void MandatoryVerificationRejectsMismatchedBoundSchemaIdentity()
    {
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(
                "using Core; [ConstEval] int32 Value() { return 1; }"),
            new CompilationOptions {
                CoreEvaluationFormIdentitySelection
                    = CreateConstEvalSelection(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var function = program.Functions.Single(
            static function => function.Name == "Value");
        var functionIndex = program.Functions.IndexOf(function);
        var forgedFunction = function with {
            ConstEvalSchema = function.ConstEvalSchema! with {
                Selector = new CoreEvaluationFormSchemaSelector(
                    "consumer.module", "wrong-export"),
            },
        };
        var forgedProgram = program with {
            Functions = program.Functions.SetItem(functionIndex,
                forgedFunction),
        };

        Assert.That(() => SemanticVerifier.VerifyLibrary(forgedProgram),
            Throws.ArgumentException);

        var unboundFunction = function with { ConstEvalSchema = null };
        var unboundProgram = program with {
            Functions = program.Functions.SetItem(functionIndex,
                unboundFunction),
        };
        Assert.That(() => SemanticVerifier.VerifyLibrary(unboundProgram),
            Throws.ArgumentException);
    }

    [Test]
    public void MandatoryVerificationRejectsMismatchedPureSchemaIdentity()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Core;
            [Pure] int32 Identity(int32 value) { return value; }
            """), new CompilationOptions {
                CoreEvaluationFormIdentitySelection = CreatePureSelection(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var function = program.Functions.Single(
            static item => item.Name == "Identity");
        var functionIndex = program.Functions.IndexOf(function);
        var forgedFunction = function with {
            PureEvaluationFormSchema
                = function.PureEvaluationFormSchema! with {
                    Selector = new CoreEvaluationFormSchemaSelector(
                        "consumer.module", "wrong-export"),
                },
        };
        var forgedProgram = program with {
            Functions = program.Functions.SetItem(functionIndex,
                forgedFunction),
        };

        Assert.That(() => SemanticVerifier.VerifyLibrary(forgedProgram),
            Throws.ArgumentException);
    }

    private static CoreEvaluationFormIdentitySelection
        CreateConstEvalSelection(string usingModuleIdentity
            = "consumer.module")
    {
        var declaration = CreateSchemaDeclaration("Core.ConstEval");
        var catalog = CreateCatalog(declaration, "const-eval",
            usingModuleIdentity);
        return new CoreEvaluationFormIdentitySelection(
            usingModuleIdentity,
            ConstEvalSchema: new CoreEvaluationFormSchemaSelector(
                usingModuleIdentity, "const-eval"),
            LocalSchemaDeclarations: catalog);
    }

    private static CoreEvaluationFormIdentitySelection
        CreatePureSelection(string usingModuleIdentity
            = "consumer.module")
    {
        var declaration = CreateSchemaDeclaration("Core.Pure");
        var catalog = CreateCatalog(declaration, "pure",
            usingModuleIdentity);
        return new CoreEvaluationFormIdentitySelection(
            usingModuleIdentity,
            PureSchema: new CoreEvaluationFormSchemaSelector(
                usingModuleIdentity, "pure"),
            LocalSchemaDeclarations: catalog);
    }

    private static CoreEvaluationFormSchemaDeclaration
        CreateSchemaDeclaration(string qualifiedName)
        => new(qualifiedName, 0, 0, [
            CoreEvaluationFormTargetKind.OrdinaryNamedCallable,
            CoreEvaluationFormTargetKind.IndividualAccessor,
            CoreEvaluationFormTargetKind.EligibleInitializationConstructor,
        ], isPublic: true, isPrimary: true,
            isPrimaryTargetOnly: true, isRepeatable: false);

    private static CoreEvaluationFormSchemaDeclarations CreateCatalog(
        CoreEvaluationFormSchemaDeclaration declaration, string exportKey,
        string moduleIdentity = "consumer.module",
        uint runtimePolicy = 0)
        => CoreEvaluationFormSchemaDeclarations.Create(
            new CoreProviderModuleOrigin(moduleIdentity, "revision-1",
                new CoreProviderSchemaSelection("evaluation-form", 1),
                1, 0, runtimePolicy),
            [declaration],
            [new(exportKey, declaration)]);
}
