// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Semantics.Generics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class StaticProtocolTests
{
    private static readonly int[] s_expectedGenericSourceOrdinals = [0, 1];
    private static readonly int[] s_defaultTypeDependencies = [0];
    private static readonly string s_sample = File.ReadAllText(
        Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..")),
            "samples", "LanguageFeatures", "StaticProtocols", "static-protocol-quadratic.terrum"));

    [Test]
    public async Task SourceDefinedNominalProtocolsSpecializeTwoConcreteWrappersAsync()
    {
        var tree = SyntaxTree.Parse(s_sample);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(s_sample));
        Assert.That(global::System.Linq.Enumerable.Any(tree.Root.DescendantNodes(),node => node.Kind == SyntaxKind.InterfaceDeclaration),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(tree.Root.DescendantNodes(),node => node.Kind == SyntaxKind.GenericNameExpression),Is.True);
        var compilation = Compilation.Create(tree);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual0 = compilation.Program;
            Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual0!;
        }))();
        Assert.That(program.Functions.Count(function => function.Name.StartsWith("EvaluateQuadratic<",
            StringComparison.Ordinal)), Is.EqualTo(2));
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,function => function.Name == "EvaluateQuadratic"),Is.False);
        Assert.That(await RunAsync(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast)).ConfigureAwait(true), Is.EqualTo(46));
    }

    [Test]
    public async Task ConcreteWrapperPublishesWithoutGenericWitnessMetadataAsync()
    {
        var source = s_sample[..s_sample.IndexOf("int32 Main()", StringComparison.Ordinal)]
            + "public int32 Closed(int32 x) { return EvaluateQuadratic<Meters>("
            + "new Meters(x), new Meters(3), new Meters(4), new Meters(5)).Value; }";
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual1 = library.Program;
            Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual1!;
        }))(), ["Closed"]);
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.That(global::System.Linq.Enumerable.Single(reference.Functions).Name, Is.EqualTo("Closed"));
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Closed(2); }"), reference);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual2 = compilation.Program;
            Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual2!;
        }))(), producer);
        Assert.That(await RunAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), NativeRuntimePolicy.FailFast)).ConfigureAwait(true), Is.EqualTo(25));
    }
    [TestCase(": Addition<Meters, Meters, Meters>, Multiplication<Meters, Meters, Meters>","","TRM3007")]
    [TestCase("public static Meters operator *(Meters left, Meters right)","private static Meters operator *(Meters left, Meters right)","TRM3007")]
    [TestCase("operator *(Meters left, Meters right)","NotMultiply(Meters left, Meters right)","TRM3007")]
    [TestCase("operator *(Meters left, Meters right)","operator *(Meters left, int32 right)","TRM3007")]
    [TestCase("struct Meters","unique struct Meters","TRM3007")]
    [TestCase("Addition<T, T, T>, Multiplication<T, T, T>","Addition<T, T, T>","TRM3007")]
    [TestCase("Addition<Meters, Meters, Meters>","Addition<Meters, Seconds, Meters>","TRM3007")]
    [TestCase("Addition<Meters, Meters, Meters>","Addition.NotAddition<Meters, Meters, Meters>","TRM3007")]
    [TestCase("Addition<Meters, Meters, Meters>","Addition<Meters, Meters, Meters>?","TRM3007")]
    [TestCase("where T : Addition<T, T, T>","where T : Unknown<T, T, T>","TRM3007")]
    [TestCase("where T : Addition<T, T, T>","where T : Addition.NotAddition<T, T, T>","TRM3007")]
    [TestCase("where T : Addition<T, T, T>","where T : Addition<T, T, T>?","TRM3007")]
    [TestCase("T EvaluateQuadratic<T>","[NoThrow] T EvaluateQuadratic<T>","TRM3017")]
    [TestCase("T EvaluateQuadratic<T>","T EvaluateQuadratic<T, T>","TRM3007")]
    [TestCase("operator +(Meters left, Meters right)","op_Addition(Meters left, Meters right)","TRM3007")]
    [TestCase("static TResult operator +(TSelf left, TOther right)",        "[NoThrow] [NoThrow] static TResult operator +(TSelf left, TOther right)","TRM3017")]
    [TestCase("static TResult operator +(TSelf left, TOther right);",        "static TResult operator +(TSelf left, TOther right); static TResult operator -(TSelf left, TOther right);",        "TRM3001")]
    [TestCase("static TResult operator +(TSelf left, TOther right)","static TOther operator +(TSelf left, TOther right)","TRM3001")]
    [TestCase("=> (a * x + b) * x + c;","=> EvaluateQuadratic<Meters>(x, a, b, c);","TRM3007")]
    public void RejectsMissingOrFalseProtocolPromises(string before, string after, string expected)
    {
        Assert.That(s_sample.Contains(before, StringComparison.Ordinal), Is.True);
        var compilation = Compilation.Create(SyntaxTree.Parse(s_sample.Replace(before, after, StringComparison.Ordinal)));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == expected),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void PublicGenericDefinitionsRetainCheckedBodiesAndRequirements()
    {
        var source = s_sample.Replace("T EvaluateQuadratic<T>",
            "public T EvaluateQuadratic<T>", StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        Assert.That(program.GenericMethodOperatorProtocols, Has.Length.EqualTo(2));
        var definition = global::System.Linq.Enumerable.Single(
            program.GenericMethodDefinitions,
            static method => method.Name == "EvaluateQuadratic");
        Assert.That(definition.IsPublic, Is.True);
        Assert.That(definition.Formals, Has.Length.EqualTo(1));
        Assert.That(definition.Formals[0].Name, Is.EqualTo("T"));
        Assert.That(definition.Formals[0].OwnershipDomain, Is.EqualTo(1));
        Assert.That(definition.Formals[0].NullabilityDomain, Is.EqualTo(1));
        Assert.That(definition.Formals[0].Requirements,
            Has.Length.EqualTo(2));
        Assert.That(definition.Formals[0].Requirements.All(requirement =>
                requirement.ProtocolOrdinal
                    < program.GenericMethodOperatorProtocols.Length
                && program.GenericMethodOperatorProtocols[
                    requirement.ProtocolOrdinal].Name == requirement.Protocol
                && program.GenericMethodOperatorProtocols[
                    requirement.ProtocolOrdinal].Operator
                    == requirement.Operator),
            Is.True);
        Assert.That(program.GenericMethodOperatorProtocols.All(protocol =>
                protocol.SourceInputOrdinal == 0
                && !protocol.HasSelf
                && !protocol.RequiresUnsafeCaller),
            Is.True);
        var malformedProtocolProgram = program with {
            GenericMethodOperatorProtocols = [
                .. program.GenericMethodOperatorProtocols.Select(
                    (protocol, index) => index == 0
                        ? protocol with { SourceInputOrdinal = -1 }
                        : protocol),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(
            malformedProtocolProgram, out _, out _, out _, out _),
            Is.False);
        var function = definition.ProofProgram.Functions[
            definition.FunctionIndex];
        Assert.That(function.IsExternal, Is.False);
        Assert.That(function.Body.Statements, Is.Not.Empty);
        var specializations = program.Functions.Where(static item =>
            item.Name.StartsWith("EvaluateQuadratic<",
                StringComparison.Ordinal)).ToArray();
        Assert.That(specializations, Has.Length.EqualTo(2));
        Assert.That(specializations.All(item =>
                item.GenericMethodInstantiation is
                    BoundGenericMethodInstantiation instantiation
                && instantiation.DefinitionOrdinal
                    == definition.DefinitionOrdinal
                && instantiation.TypeArguments.Length == 1),
            Is.True);
        var main = program.Functions.Single(static item =>
            item.Name == "Main");
        var calls = new System.Collections.Generic.List<
            BoundGenericMethodInstantiation>();
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall: BoundGenericMethodInstantiation call,
                    })
                {
                    calls.Add(call);
                }
            }), Is.True);
        Assert.That(calls, Has.Count.EqualTo(2));
        Assert.That(calls.All(call => call.DefinitionOrdinal
                == definition.DefinitionOrdinal
            && call.TypeArguments.Length == 1), Is.True);
        Assert.That(calls.Select(static call => call.TypeArguments[0])
            .Distinct().Count(), Is.EqualTo(2));
        var remappedType = calls[0].TypeArguments[0];
        var remapType = new Func<PrimitiveType, PrimitiveType>(type =>
            type == remappedType ? PrimitiveType.UInt32 : type);
        var rewrittenMain = BoundTreeTraversal.RewriteTypes(main, remapType);
        BoundGenericMethodInstantiation? rewrittenCall = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(
            rewrittenMain.Body, expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall: BoundGenericMethodInstantiation call,
                    } && call.DefinitionOrdinal
                        == definition.DefinitionOrdinal
                    && call.TypeArguments[0] == PrimitiveType.UInt32)
                {
                    rewrittenCall = call;
                }
            }), Is.True);
        Assert.That(rewrittenCall, Is.Not.Null);
        var specialization = specializations.Single(item =>
            item.GenericMethodInstantiation!.TypeArguments[0]
                == remappedType);
        var rewrittenSpecialization = BoundTreeTraversal.RewriteTypes(
            specialization, remapType);
        Assert.That(rewrittenSpecialization.GenericMethodInstantiation!
                .TypeArguments[0],
            Is.EqualTo(PrimitiveType.UInt32));
    }

    [Test]
    public void CopyIsNotAnExplicitGenericConstraint()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public T Identity<T>(T value) where T : [Copy] {
                return value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3007"), Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public async Task GenericMethodConstantActualsBindMixedValuesAndDefaultsAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            public enum Mode : int32 { Read = 1 }

            int32 Read<const int32 Start, T,
                const int32 Next = Start + 1, U = int32,
                const Mode Selected = Mode.Read>(T value) {
                return Next;
            }

            int32 Main() {
                const int32 start = 3;
                return Read<3, int32, default, uint32, Mode.Read>(0)
                    + Read<3, int32>(0)
                    + Read<3, int32, 9, uint32, Mode.Read>(0)
                    + Read<start, int32>(0);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var signature = program.GenericMethodSignatures.Single();
        Assert.That(signature.ConstantFormals.Select(static formal =>
                formal.Name),
            Is.EqualTo(["Start", "Next", "Selected"]));
        var main = program.Functions.Single(static function =>
            function.Name == "Main");
        var calls = new List<BoundGenericMethodInstantiation>();
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation instantiation,
                    })
                {
                    calls.Add(instantiation);
                }
            }), Is.True);
        Assert.That(calls, Has.Count.EqualTo(4));
        Assert.That(calls.Select(static call =>
                call.ConstantArguments[1].Bits.Low)
            .OrderBy(static value => value).ToArray(),
            Is.EqualTo([4UL, 4UL, 4UL, 9UL]));
        Assert.That(await RunAsync(NativeEmitter.Emit(program,
            NativeRuntimePolicy.FailFast)).ConfigureAwait(true),
            Is.EqualTo(21));
    }

    [Test]
    public async Task GenericMethodsSubstituteClosedOwnersBeforeMethodInferenceAsync()
    {
        var source = """
            struct Box<T = int32> where T : [Blittable] {
                public T Value;

                public Box(T value) { this.Value = value; }

                public int32 Select<U>(T owner, U value) {
                    return 2;
                }

                public int32 Echo<T>(T value) { return 4; }

                public int32 DefaultOwner<U = T>(T owner) {
                    return this.Value == owner ? 42 : 0;
                }

                public int32 Constant<const int32 Value = 42>(
                    T owner) {
                    return Value;
                }

                public static int32 StaticOwner<U>(T owner, U value) {
                    return 5;
                }
            }

            struct Pair<T = int32, U = T> {
                public T First;
                public U Second;

                public int32 Select<V>(T first, U second, V value) {
                    return 3;
                }
            }

            int32 SelectPair(Pair<int32, uint32> pair) {
                return pair.Select(1, uint32(2), uint32(3));
            }

            int32 SelectDefaultPair(Pair pair) {
                return pair.Select(1, 2, uint32(3));
            }

            int32 Main() {
                Box box = new Box(40);
                Box<uint32> other = new Box<uint32>(uint32(5));
                int32 selected = box.Select(3, uint32(2));
                int32 shadowed = box.Echo(uint32(4));
                int32 defaulted = box.DefaultOwner(40);
                int32 constantDefaulted = box.Constant(40);
                int32 otherSelected = other.Select(
                    uint32(5), uint32(3));
                int32 staticSelected = Box<int32>.StaticOwner(
                    3, uint32(2));
                return selected + shadowed + defaulted + constantDefaulted
                    + otherSelected + staticSelected;
            }
            """;
        var options = new CompilationOptions {
            LayoutCoreRegistration =
                LayoutCoreRegistrationTestData.Create(),
        };
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var ownerDefinitions = program.GenericMethodDefinitions.Where(
            static definition => definition.Owner is not null).ToArray();
        Assert.That(ownerDefinitions, Has.Length.EqualTo(12));
        var boxOwnerDefinitions = ownerDefinitions.Where(
            static definition => definition.Name.Contains("Box<",
                StringComparison.Ordinal)).ToArray();
        Assert.That(boxOwnerDefinitions, Has.Length.EqualTo(10));
        Assert.That(boxOwnerDefinitions.Select(static definition =>
                definition.Owner!.TypeArguments.Single())
            .Distinct().OrderBy(static type => type).ToArray(),
            Is.EqualTo([PrimitiveType.Int32, PrimitiveType.UInt32]));

        var echoDefinition = ownerDefinitions.Single(static definition =>
            definition.Name.Contains("Box<int32>.Echo",
                StringComparison.Ordinal));
        Assert.That(echoDefinition.Formals.Single().Name, Is.EqualTo("T"));
        Assert.That(echoDefinition.Owner!.TypeArguments,
            Is.EqualTo([PrimitiveType.Int32]));
        var echoSignature = program.GenericMethodSignatures[
            echoDefinition.DefinitionOrdinal];
        Assert.That(echoSignature.Formals.Single().Name, Is.EqualTo("T"));
        Assert.That(echoSignature.Owner!.TypeArguments,
            Is.EqualTo([PrimitiveType.Int32]));
        var staticDefinition = ownerDefinitions.Single(static definition =>
            definition.Name.Contains("Box<int32>.StaticOwner",
                StringComparison.Ordinal));
        Assert.That(staticDefinition.IsStatic, Is.True);
        Assert.That(staticDefinition.Owner!.TypeArguments,
            Is.EqualTo([PrimitiveType.Int32]));
        var pairDefinition = ownerDefinitions.Single(static definition =>
            definition.Name.Contains("Pair<int32, uint32>.Select",
                StringComparison.Ordinal));
        Assert.That(pairDefinition.Owner!.TypeArguments,
            Is.EqualTo([PrimitiveType.Int32, PrimitiveType.UInt32]));
        var defaultPairDefinition = ownerDefinitions.Single(static definition =>
            definition.Name.Contains("Pair<int32, int32>.Select",
                StringComparison.Ordinal));
        Assert.That(defaultPairDefinition.Owner!.TypeArguments,
            Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32]));
        var selectPair = program.Functions.Single(static function =>
            function.Name == "SelectPair");
        BoundGenericMethodInstantiation? pairCall = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(selectPair.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation instantiation,
                    })
                {
                    pairCall = instantiation;
                }
            }), Is.True);
        Assert.That(pairCall, Is.Not.Null);
        Assert.That(pairCall!.TypeArguments,
            Is.EqualTo([PrimitiveType.UInt32]));
        Assert.That(program.GenericMethodDefinitions[
                pairCall.DefinitionOrdinal].Owner!.TypeArguments,
            Is.EqualTo([PrimitiveType.Int32, PrimitiveType.UInt32]));

        var main = program.Functions.Single(static function =>
            function.Name == "Main");
        BoundGenericMethodInstantiation? shadowed = null;
        BoundGenericMethodInstantiation? defaulted = null;
        BoundGenericMethodInstantiation? constantDefaulted = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is not BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation instantiation,
                    })
                {
                    return;
                }

                var definition = program.GenericMethodDefinitions[
                    instantiation.DefinitionOrdinal];
                if (definition.Name.Contains(".Echo",
                        StringComparison.Ordinal))
                {
                    shadowed = instantiation;
                }
                else if (definition.Name.Contains(".DefaultOwner",
                    StringComparison.Ordinal))
                {
                    defaulted = instantiation;
                }
                else if (definition.Name.Contains(".Constant",
                    StringComparison.Ordinal))
                {
                    constantDefaulted = instantiation;
                }
            }), Is.True);
        Assert.That(shadowed, Is.Not.Null);
        Assert.That(shadowed!.TypeArguments, Is.EqualTo(
            [PrimitiveType.UInt32]));
        Assert.That(defaulted, Is.Not.Null);
        Assert.That(defaulted!.TypeArguments, Is.EqualTo(
            [PrimitiveType.Int32]));
        Assert.That(constantDefaulted, Is.Not.Null);
        Assert.That(constantDefaulted!.ConstantArguments.Single().Bits.Low,
            Is.EqualTo(42));
        var malformedOwnerDefinitions =
            program.GenericMethodDefinitions.Select((definition, index) =>
                index == 0
                    ? definition with {
                        Owner = definition.Owner! with {
                            TypeArguments = [PrimitiveType.UInt32],
                        },
                    }
                    : definition).ToImmutableArray();
        Assert.That(SemanticVerifier.TryVerifySource(program with {
            GenericMethodDefinitions = malformedOwnerDefinitions,
        }, out _, out _, out _, out _), Is.False);
        var malformedOwnerSignatures = program.GenericMethodSignatures.SetItem(
            echoDefinition.DefinitionOrdinal,
            echoSignature with {
                Owner = echoSignature.Owner! with {
                    TypeArguments = [PrimitiveType.UInt32],
                },
            });
        Assert.That(SemanticVerifier.TryVerifySource(program with {
            GenericMethodSignatures = malformedOwnerSignatures,
        }, out _, out _, out _, out _), Is.False);
        Assert.That(await RunAsync(NativeEmitter.Emit(program,
            NativeRuntimePolicy.FailFast)).ConfigureAwait(true),
            Is.EqualTo(97));

        var invalidOwner = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct NotBlittable { public int32 Value; }
            struct Constrained<T> where T : [Blittable] {
                public int32 Select<U>(T owner, U value) { return 1; }
            }
            void Use(Constrained<NotBlittable> value) { }
            """), options);
        Assert.That(invalidOwner.HasErrors, Is.True);
        Assert.That(invalidOwner.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("Blittable constraint",
                    StringComparison.Ordinal)), Is.True);
    }

    [TestCase("int8", "-128")]
    [TestCase("int8", "127")]
    [TestCase("uint8", "255")]
    [TestCase("int128", "-170141183460469231731687303715884105728")]
    [TestCase("int128", "170141183460469231731687303715884105727")]
    [TestCase("uint128", "340282366920938463463374607431768211455")]
    public void GenericMethodConstantActualsRetainIntegerBoundaries(
        string domain, string value)
    {
        var source = "int32 Read<const " + domain
            + " Value>(int32 input) { return input; }"
            + " int32 Main() { return Read< " + value + ">(0); }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var main = compilation.Program!.Functions.Single(static function =>
            function.Name == "Main");
        BoundGenericMethodConstantArgument? actual = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation instantiation,
                    }
                    && instantiation.ConstantArguments.Length == 1)
                {
                    actual = instantiation.ConstantArguments[0];
                }
            }), Is.True);
        Assert.That(actual, Is.Not.Null);
        Assert.That(actual!.Kind,
            Is.EqualTo(BoundGenericMethodConstantArgumentKind.Value));
        Assert.That(actual.Bits.IsCanonicalFor(actual.CarrierType), Is.True);
    }

    [TestCase("int8", "128")]
    [TestCase("uint8", "256")]
    public void GenericMethodConstantActualsRejectIntegerOverflow(
        string domain, string value)
    {
        var source = "int32 Read<const " + domain
            + " Value>(int32 input) { return input; }"
            + " int32 Main() { return Read< " + value + ">(0); }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public async Task GenericWrapperConstEvalActualConstructsAndLowersAsync()
    {
        const string source = """
            [Blittable] [Layout(LayoutKind.Transparent)]
            public struct ScalarBox {
                public int32 Value;

                [ConstEval]
                public ScalarBox(int32 value) {
                    Value = value;
                }

                [ConstEval]
                public static ScalarBox Create(int32 value) {
                    return new ScalarBox(value);
                }
            }

            int32 Read<const ScalarBox Value>(int32 input) {
                return Value.Value;
            }

            int32 Main() {
                return Read<(ScalarBox.Create(42))>(0)
                    + Read<(ScalarBox.Create(40 + 2))>(0);
            }
            """;
        var options = CoreEvaluationFormTestHelpers.WithConstEval(
            new CompilationOptions {
            LayoutCoreRegistration =
                Terrum.CodeAnalysis.Tests.Semantics
                    .LayoutCoreRegistrationTestData.Create(),
            CoreContractFailureIdentitySelection =
                new CoreContractFailureIdentitySelection(
                    "sample.consumer",
                    new CoreContractFailureDeclarationIdentity(
                        "core.contracts", "requirement-exception"),
                    new CoreContractFailureDeclarationIdentity(
                        "core.contracts", "postcondition-exception"),
                    new CoreContractFailureDeclarationIdentity(
                        "core.contracts", "require-exception-schema")),
            });
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var main = program.Functions.Single(static function =>
            function.Name == "Main");
        BoundGenericMethodConstantArgument? actual = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation {
                                ConstantArguments:
                                    [BoundGenericMethodConstantArgument
                                        argument],
                            },
                    })
                {
                    actual = argument;
                }
            }), Is.True);
        Assert.That(actual, Is.Not.Null);
        Assert.That(actual!.DomainType, Is.Not.EqualTo(actual.CarrierType));
        Assert.That(actual.CarrierType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(actual.Bits.Low, Is.EqualTo(42UL));

        static BoundGenericMethodInstantiation CorruptCarrier(
            BoundGenericMethodInstantiation instantiation)
        {
            return instantiation with {
                ConstantArguments = [
                    .. instantiation.ConstantArguments.Select(argument =>
                        argument with {
                            CarrierType = PrimitiveType.UInt32,
                        }),
                ],
            };
        }

        var malformedFunctions = program.Functions.Select(function =>
        {
            var rewritten = function.GenericMethodInstantiation is
                BoundGenericMethodInstantiation instantiation
                    ? function with {
                        GenericMethodInstantiation =
                            CorruptCarrier(instantiation),
                    }
                    : function;
            return BoundTreeTraversal.RewriteExpressions(rewritten,
                expression => expression is BoundCall call
                    && call.GenericMethodCall is
                        BoundGenericMethodInstantiation instantiation
                    ? call with {
                        GenericMethodCall =
                            CorruptCarrier(instantiation),
                    }
                    : expression);
        }).ToImmutableArray();
        Assert.That(SemanticVerifier.TryVerifySource(program with {
            Functions = malformedFunctions,
        }, out _, out _, out _, out _), Is.False);

        Assert.That(program.Functions.Count(static function =>
                function.GenericMethodInstantiation is not null),
            Is.EqualTo(1));
        Assert.That(await RunAsync(NativeEmitter.Emit(program,
            NativeRuntimePolicy.FailFast)).ConfigureAwait(true),
            Is.EqualTo(84));

        const string invalidSource = """
            [Blittable] [Layout(LayoutKind.Transparent)]
            public struct NonNegative {
                public int32 Value;

                [ConstEval]
                public NonNegative(int32 value) require value >= 0 {
                    Value = value;
                }

                [ConstEval]
                public static NonNegative Create(int32 value)
                    require value > 0 {
                    return new NonNegative(value);
                }
            }

            int32 Read<const NonNegative Value>(int32 input) {
                return input;
            }

            int32 Use() {
                return Read<(NonNegative.Create(-1))>(0);
            }
            """;
        var invalid = Compilation.CreateLibrary(
            SyntaxTree.Parse(invalidSource), options);
        Assert.That(invalid.HasErrors, Is.True);
        Assert.That(invalid.Program, Is.Null);
        Assert.That(invalid.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True);
    }

    [Test]
    public void GenericWrapperConstEvalRunsConstructionPostconditions()
    {
        const string source = """
            [Blittable] [Layout(LayoutKind.Transparent)]
            public struct NonNegative {
                public int32 Value;

                [ConstEval]
                public NonNegative(int32 value)
                    require value >= 0
                    ensure return.Value >= 0 {
                    Value = value;
                }

                [ConstEval]
                public static NonNegative Create(int32 value)
                    require value >= 0 {
                    return new NonNegative(value);
                }
            }

            NonNegative ConstructUnchecked(int32 value) {
                return new NonNegative(value);
            }

            public int32 Read<const NonNegative Value>(int32 input) {
                return input;
            }

            public int32 CreateValue() {
                return Read<(NonNegative.Create(3))>(0);
            }
            """;
        var options = CoreEvaluationFormTestHelpers.WithConstEval(
            new CompilationOptions {
            LayoutCoreRegistration =
                Terrum.CodeAnalysis.Tests.Semantics
                    .LayoutCoreRegistrationTestData.Create(),
            CoreContractFailureIdentitySelection =
                new CoreContractFailureIdentitySelection(
                    "sample.consumer",
                    new CoreContractFailureDeclarationIdentity(
                        "core.contracts", "requirement-exception"),
                    new CoreContractFailureDeclarationIdentity(
                        "core.contracts", "postcondition-exception"),
                    new CoreContractFailureDeclarationIdentity(
                        "core.contracts", "require-exception-schema")),
            });
        var valid = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            options);
        Assert.That(valid.HasErrors, Is.False,
            string.Join(Environment.NewLine, valid.Diagnostics));

        var invalid = Compilation.CreateLibrary(SyntaxTree.Parse(
            source.Replace("NonNegative.Create(3)",
                "NonNegative.Create(-1)",
                StringComparison.Ordinal)), options);
        Assert.That(invalid.HasErrors, Is.True);
        Assert.That(invalid.Program, Is.Null);
        Assert.That(invalid.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True);
    }

    [Test]
    public async Task GenericBooleanWrapperConstEvalActualPreservesCarrierAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Transparent)]
            public struct Flag {
                public bool Value;

                [ConstEval]
                public Flag(bool value) { Value = value; }

                [ConstEval]
                public static Flag Create(bool value) {
                    return new Flag(value);
                }
            }

            int32 Read<const Flag Value>(int32 input) {
                return Value.Value ? 1 : input;
            }

            int32 Main() { return Read<(Flag.Create(true))>(0); }
            """), CoreEvaluationFormTestHelpers.WithConstEval(
                new CompilationOptions {
                    LayoutCoreRegistration =
                        Terrum.CodeAnalysis.Tests.Semantics
                            .LayoutCoreRegistrationTestData.Create(),
                }));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(await RunAsync(NativeEmitter.Emit(compilation.Program!,
            NativeRuntimePolicy.FailFast)).ConfigureAwait(true),
            Is.EqualTo(1));
    }

    [Test]
    public async Task GenericWrapperConstEvalDefaultUsesEarlierConstantAsync()
    {
        var source = """
            [Blittable] [Layout(LayoutKind.Transparent)]
            public struct ScalarBox {
                public int32 Value;

                [ConstEval]
                public ScalarBox(int32 value) { Value = value; }

                [ConstEval]
                public static ScalarBox Create(int32 value) {
                    return new ScalarBox(value);
                }
            }

            int32 Read<const int32 Start,
                const ScalarBox Value = ScalarBox.Create(Start)>(
                int32 input) {
                return Value.Value;
            }

            int32 Main() { return Read<42, default>(0); }
            """;
        var options = CoreEvaluationFormTestHelpers.WithConstEval(
            new CompilationOptions {
                LayoutCoreRegistration =
                    Terrum.CodeAnalysis.Tests.Semantics
                        .LayoutCoreRegistrationTestData.Create(),
            });
        var librarySource = source[..source.IndexOf("int32 Main()",
            StringComparison.Ordinal)];
        var library = Compilation.CreateLibrary(
            SyntaxTree.Parse(librarySource), options);
        Assert.That(library.HasErrors, Is.False,
            string.Join(Environment.NewLine, library.Diagnostics));
        var definition = library.Program!.GenericMethodDefinitions.Single();
        var defaultInfo = definition.ConstantFormals
            .Single(static formal => formal.Name == "Value").Default!;
        var defaultFunction = definition.ProofProgram.Functions[
            defaultInfo.ProofFunctionIndex];
        BoundCall? defaultCall = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(
            defaultFunction.Body, expression =>
            {
                if (expression is BoundCall call)
                {
                    defaultCall = call;
                }
            }), Is.True);
        Assert.That(defaultCall, Is.Not.Null);
        Assert.That(definition.ProofProgram.Functions[
                defaultCall!.Function].Name,
            Is.EqualTo("ScalarBox.Create"));
        var malformedProofProgram = definition.ProofProgram with {
            Functions = definition.ProofProgram.Functions.SetItem(
                defaultInfo.ProofFunctionIndex,
                BoundTreeTraversal.RewriteExpressions(defaultFunction,
                    expression => expression is BoundCall call
                        ? call with { Function = int.MaxValue }
                        : expression)),
        };
        Assert.That(SemanticVerifier.TryVerifySource(library.Program with {
            GenericMethodDefinitions = [
                definition with {
                    ProofProgram = malformedProofProgram,
                },
            ],
        }, out _, out _, out _, out _), Is.False);

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            options);
        var diagnosticMessage = string.Join(Environment.NewLine,
            compilation.Diagnostics.Select(diagnostic =>
            {
                var start = Math.Max(0, diagnostic.Span.Start - 4);
                var length = Math.Min(12, source.Length - start);
                return diagnostic + " near '"
                    + source.Substring(start, length) + "'";
            }));
        Assert.That(compilation.HasErrors, Is.False,
            diagnosticMessage);
        Assert.That(await RunAsync(NativeEmitter.Emit(compilation.Program!,
            NativeRuntimePolicy.FailFast)).ConfigureAwait(true),
            Is.EqualTo(42));
    }

    [Test]
    public void GenericWrapperConstEvalDefaultEnforcesConstructionPreconditions()
    {
        const string source = """
            [Blittable] [Layout(LayoutKind.Transparent)]
            public struct NonNegative {
                public int32 Value;

                [ConstEval]
                public NonNegative(int32 value) require value > 0 {
                    Value = value;
                }

                [ConstEval]
                public static NonNegative Create(int32 value)
                    require value > 0 {
                    return new NonNegative(value);
                }
            }

            int32 Read<const int32 Start,
                const NonNegative Value = NonNegative.Create(Start)>(
                int32 input) {
                return Value.Value;
            }

            int32 Main() { return Read<42, default>(0); }
            """;
        var options = CoreEvaluationFormTestHelpers.WithConstEval(
            new CompilationOptions {
                LayoutCoreRegistration =
                    Terrum.CodeAnalysis.Tests.Semantics
                        .LayoutCoreRegistrationTestData.Create(),
                CoreContractFailureIdentitySelection =
                    new CoreContractFailureIdentitySelection(
                        "sample.consumer",
                        new CoreContractFailureDeclarationIdentity(
                            "core.contracts", "requirement-exception"),
                        new CoreContractFailureDeclarationIdentity(
                            "core.contracts", "postcondition-exception"),
                        new CoreContractFailureDeclarationIdentity(
                            "core.contracts",
                            "require-exception-schema")),
            });
        var valid = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            options);
        Assert.That(valid.HasErrors, Is.False,
            string.Join(Environment.NewLine, valid.Diagnostics));

        var invalid = Compilation.CreateLibrary(SyntaxTree.Parse(
            source.Replace("Read<42, default>", "Read<0, default>",
                StringComparison.Ordinal)), options);
        Assert.That(invalid.HasErrors, Is.True);
        Assert.That(invalid.Program, Is.Null);
        Assert.That(invalid.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True);
    }

    [Test]
    public async Task GenericMethodConstantActualsForwardAndShareCanonicalValuesAsync()
    {
        var source = """
            int32 Identity<const int32 Value>(int32 input) {
                return Value;
            }

            int32 Forward<const int32 Value>(int32 input) {
                return Identity<Value>(input);
            }

            int32 Enabled<const bool Value>(int32 input) {
                return Value ? 1 : input;
            }

            int32 Main() {
                return Forward<13>(0) + Enabled<true>(0)
                    + Identity<3>(0) + Identity<(1 + 2)>(0);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        var diagnosticMessage = string.Join(Environment.NewLine,
            compilation.Diagnostics.Select(diagnostic =>
            {
                var start = Math.Max(0, diagnostic.Span.Start - 4);
                var length = Math.Min(12, source.Length - start);
                return diagnostic + " near '"
                    + source.Substring(start, length) + "'";
            }));
        Assert.That(compilation.HasErrors, Is.False,
            diagnosticMessage);

        var program = compilation.Program!;
        Assert.That(program.Functions.Count(static function =>
                function.GenericMethodInstantiation is
                    BoundGenericMethodInstantiation instantiation
                && instantiation.DefinitionOrdinal == 0
                && instantiation.ConstantArguments[0].Bits.Low == 3),
            Is.EqualTo(1));
        var forwardingDefinition = program.GenericMethodDefinitions
            .Single(static definition => definition.Name == "Forward");
        var proofBody = forwardingDefinition.ProofProgram.Functions[
            forwardingDefinition.FunctionIndex];
        BoundGenericMethodConstantArgument? forwarded = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(proofBody.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation call,
                    }
                    && call.ConstantArguments.Length == 1
                    && call.ConstantArguments[0].Kind
                        == BoundGenericMethodConstantArgumentKind.Formal)
                {
                    forwarded = call.ConstantArguments[0];
                }
            }), Is.True);
        Assert.That(forwarded, Is.Not.Null);
        Assert.That(forwarded!.CallerFormalOrdinal, Is.Zero);
        Assert.That(await RunAsync(NativeEmitter.Emit(program,
            NativeRuntimePolicy.FailFast)).ConfigureAwait(true),
            Is.EqualTo(20));
    }

    [Test]
    public void GenericMethodConstantActualRejectsWrongDomain()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Read<const int32 Value>(int32 input) {
                return Value;
            }

            int32 Main() {
                const uint32 wide = 3;
                return Read<wide>(0);
            }
            """));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True);
    }

    [Test]
    public void GenericConstantInferenceHoleDoesNotSelectDefault()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Read<const int32 Value = 3>(int32 input) {
                return Value;
            }

            int32 Main() { return Read<_>(0); }
            """));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3011"), Is.True);
    }

    [Test]
    public async Task GenericMethodConstantActualExecutesConstEvalAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [ConstEval]
            int32 Add(int32 left, int32 right) {
                return left + right;
            }

            int32 Read<const int32 Value>(int32 input) {
                return Value;
            }

            int32 Main() { return Read<(Add(2, 3))>(0); }
            """), CoreEvaluationFormTestHelpers.WithConstEval(
                new CompilationOptions {
                    SourceArithmeticProfile
                        = SourceArithmeticProfile.Release,
                }));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(await RunAsync(NativeEmitter.Emit(compilation.Program!,
            NativeRuntimePolicy.FailFast)).ConfigureAwait(true),
            Is.EqualTo(5));
    }

    [Test]
    public void GenericMethodConstantActualConstEvalFailureIsDiagnosed()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [ConstEval]
            int32 Divide(int32 value) {
                return 1 / value;
            }

            int32 Read<const int32 Value>(int32 input) {
                return Value;
            }

            int32 Main() { return Read<(Divide(0))>(0); }
            """), CoreEvaluationFormTestHelpers.Options());
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True);
    }

    [Test]
    public void GenericMethodConstantDefaultEvaluationFailureIsDiagnosed()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Read<const int32 Start,
                const int32 Next = Start / (Start - Start)>(
                int32 input) {
                return Next;
            }

            int32 Main() { return Read<3, default>(0); }
            """));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True);
    }

    [Test]
    public void GenericMethodActualKindsDoNotCrossTypeAndConstantFormals()
    {
        var typeFormal = Compilation.Create(SyntaxTree.Parse("""
            T Identity<T>(T input) { return input; }

            int32 Main() { return Identity<3>(3); }
            """));
        Assert.That(typeFormal.HasErrors, Is.True);
        Assert.That(typeFormal.Program, Is.Null);
        Assert.That(typeFormal.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True,
            string.Join(Environment.NewLine, typeFormal.Diagnostics));

        var constantFormal = Compilation.Create(SyntaxTree.Parse("""
            int32 Read<const int32 Value>(int32 input) {
                return Value;
            }

            int32 Main() { return Read<int32>(0); }
            """));
        Assert.That(constantFormal.HasErrors, Is.True);
        Assert.That(constantFormal.Program, Is.Null);
        Assert.That(constantFormal.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True);
    }

    [Test]
    public void GenericMethodConstantInstantiationVerifierChecksActualDomain()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Read<const int32 Value>(int32 input) {
                return Value;
            }

            int32 Main() { return Read<3>(0); }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;

        static BoundGenericMethodInstantiation Corrupt(
            BoundGenericMethodInstantiation instantiation)
        {
            return instantiation with {
                ConstantArguments = [
                    .. instantiation.ConstantArguments.Select(argument =>
                        argument with {
                            DomainType = PrimitiveType.UInt32,
                            CarrierType = PrimitiveType.UInt32,
                        }),
                ],
            };
        }

        var malformedFunctions = program.Functions.Select(function =>
        {
            var rewritten = function.GenericMethodInstantiation is
                BoundGenericMethodInstantiation instantiation
                    ? function with {
                        GenericMethodInstantiation = Corrupt(instantiation),
                    }
                    : function;
            return BoundTreeTraversal.RewriteExpressions(rewritten,
                expression => expression is BoundCall call
                    && call.GenericMethodCall is
                        BoundGenericMethodInstantiation instantiation
                    ? call with {
                        GenericMethodCall = Corrupt(instantiation),
                    }
                    : expression);
        }).ToImmutableArray();
        Assert.That(SemanticVerifier.TryVerifySource(program with {
            Functions = malformedFunctions,
        }, out _, out _, out _, out _), Is.False);

        var booleanCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Read<const bool Value>(int32 input) { return input; }

            int32 Main() { return Read<true>(0); }
            """));
        Assert.That(booleanCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                booleanCompilation.Diagnostics));
        var booleanProgram = booleanCompilation.Program!;
        static BoundGenericMethodInstantiation CorruptBoolean(
            BoundGenericMethodInstantiation instantiation)
        {
            return instantiation with {
                ConstantArguments = [
                    .. instantiation.ConstantArguments.Select(argument =>
                        argument with {
                            Bits = new ScalarBits(2, 0),
                        }),
                ],
            };
        }

        var malformedBooleanFunctions = booleanProgram.Functions.Select(
            function =>
            {
                var rewritten = function.GenericMethodInstantiation is
                    BoundGenericMethodInstantiation instantiation
                        ? function with {
                            GenericMethodInstantiation =
                                CorruptBoolean(instantiation),
                        }
                        : function;
                return BoundTreeTraversal.RewriteExpressions(rewritten,
                    expression => expression is BoundCall call
                        && call.GenericMethodCall is
                            BoundGenericMethodInstantiation instantiation
                        ? call with {
                            GenericMethodCall =
                                CorruptBoolean(instantiation),
                        }
                        : expression);
            }).ToImmutableArray();
        Assert.That(SemanticVerifier.TryVerifySource(
            booleanProgram with {
                Functions = malformedBooleanFunctions,
            }, out _, out _, out _, out _), Is.False);
    }

    [Test]
    public void GenericMethodConstantFormalsRetainMixedOrderAndDefaults()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public enum Mode : int32 { Read = 1 }

            public int32 Read<const int32 Start, T,
                const int32 Next = Start + 1, U = int32,
                const Mode Selected>(T value) {
                return Next;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definition = global::System.Linq.Enumerable.Single(
            program.GenericMethodDefinitions);
        Assert.That(definition.Formals.Select(static formal => formal.Name),
            Is.EqualTo(["T", "U"]));
        Assert.That(definition.ConstantFormals.Select(static formal =>
                formal.Name),
            Is.EqualTo(["Start", "Next", "Selected"]));
        Assert.That(definition.FormalOrder.Select(static formal =>
                (formal.Kind, formal.TypeFormalOrdinal,
                    formal.ConstantFormalOrdinal)),
            Is.EqualTo([
                (BoundGenericMethodFormalKind.Constant, -1, 0),
                (BoundGenericMethodFormalKind.Type, 0, -1),
                (BoundGenericMethodFormalKind.Constant, -1, 1),
                (BoundGenericMethodFormalKind.Type, 1, -1),
                (BoundGenericMethodFormalKind.Constant, -1, 2),
            ]));
        Assert.That(definition.ConstantFormals[0].Domain.ConcreteType,
            Is.EqualTo(PrimitiveType.Int32));
        Assert.That(definition.ConstantFormals[1].Default!
                .FormalDependencies,
            Is.EqualTo([0]));
        Assert.That(definition.ConstantFormals[2].Domain.ConcreteType,
            Is.EqualTo(BoundEnumTypes.Type(0)));
        var defaultFunction = definition.ProofProgram.Functions[
            definition.ConstantFormals[1].Default!.ProofFunctionIndex];
        Assert.That(defaultFunction.GenericMethodConstantDefaultFormalOrdinal,
            Is.EqualTo(2));
        Assert.That(defaultFunction.GenericMethodConstantDefaultDependencies,
            Is.EqualTo([0]));
        Assert.That(defaultFunction.ParameterCount, Is.EqualTo(1));
        Assert.That(defaultFunction.Variables[0].Name, Is.EqualTo("Start"));
        var proofFunction = definition.ProofProgram.Functions[
            definition.FunctionIndex];
        Assert.That(proofFunction.ParameterCount, Is.EqualTo(4));
        Assert.That(proofFunction.Variables.Select(static variable =>
                variable.Name),
            Is.EqualTo(["value", "Start", "Next", "Selected"]));
        Assert.That(proofFunction.Body.Statements.Single(),
            Is.TypeOf<BoundReturn>());
        Assert.That(((BoundReturn)proofFunction.Body.Statements.Single())
                .Value,
            Is.TypeOf<BoundRead>());
        var malformedOrder = definition with {
            FormalOrder = [.. definition.FormalOrder.Reverse()],
        };
        Assert.That(SemanticVerifier.TryVerifySource(program with {
            GenericMethodDefinitions = [malformedOrder],
        }, out _, out _, out _, out _), Is.False);
        var malformedDomain = definition with {
            ConstantFormals = [
                .. definition.ConstantFormals.Select((formal, ordinal) =>
                    ordinal == 1
                        ? formal with {
                            Domain = formal.Domain with {
                                ConcreteType = PrimitiveType.Float32,
                            },
                        }
                        : formal),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(program with {
            GenericMethodDefinitions = [malformedDomain],
        }, out _, out _, out _, out _), Is.False);
        var malformedDefault = definition with {
            ConstantFormals = [
                .. definition.ConstantFormals.Select((formal, ordinal) =>
                    ordinal == 1
                        ? formal with {
                            Default = formal.Default! with {
                                FormalDependencies = [2],
                            },
                        }
                        : formal),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(program with {
            GenericMethodDefinitions = [malformedDefault],
        }, out _, out _, out _, out _), Is.False);
    }

    [TestCase("float32")]
    [TestCase("char16")]
    public void GenericMethodConstantFormalsRejectUnsupportedDomains(
        string domain)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"public int32 Read<const {domain} Value, T>(T value) {{ return 1; }}"));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3007"), Is.True);
    }

    [Test]
    public void GenericMethodConstantFormalRetainsTransparentWrapperDomain()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Transparent)]
            public struct SmallInt {
                public int32 Value;
                public SmallInt(int32 value) { Value = value; }
            }

            public int32 Read<const SmallInt Value, T>(T value) {
                return 1;
            }
            """), new CompilationOptions {
            LayoutCoreRegistration =
                Terrum.CodeAnalysis.Tests.Semantics
                    .LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var sourceType = program.Structures.Single();
        var formal = program.GenericMethodDefinitions.Single()
            .ConstantFormals.Single();
        Assert.That(formal.Domain.ConcreteType,
            Is.EqualTo((PrimitiveType)(100
                + program.Structures.IndexOf(sourceType))));
        Assert.That(formal.PlaceholderType,
            Is.Not.EqualTo(formal.Domain.ConcreteType));
        Assert.That(program.GenericMethodDefinitions.Single().ProofProgram
                .Structures[(int)formal.PlaceholderType - 100].Name,
            Is.EqualTo(sourceType.Name));
    }

    [Test]
    public void GenericMethodConstantFormalPreservesOpenWrapperArguments()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Transparent)]
            struct Tagged<T> {
                private int32 Value;
            }

            int32 Read<T, const Tagged<T> Tag>(T value) { return 1; }
            """), new CompilationOptions {
            LayoutCoreRegistration =
                Terrum.CodeAnalysis.Tests.Semantics
                    .LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single();
        var formal = definition.ConstantFormals.Single();
        Assert.That(formal.Domain.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.SourceGeneric));
        Assert.That(formal.Domain.SourceGenericTypeDefinitionIndex,
            Is.EqualTo(program.SourceGenericTypeDefinitions.Single()
                .DefinitionIndex));
        Assert.That(formal.Domain.TypeArguments, Has.Length.EqualTo(1));
        Assert.That(formal.Domain.TypeArguments[0].Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Formal));
        Assert.That(formal.Domain.TypeArguments[0].FormalOrdinal, Is.Zero);
        Assert.That(definition.ProofProgram.Structures[
                (int)formal.PlaceholderType - 100].Name,
            Is.EqualTo("$C0"));
        var malformedDefinition = definition with {
            ConstantFormals = [
                formal with {
                    Domain = formal.Domain with {
                        TypeArguments = [
                            formal.Domain.TypeArguments[0] with {
                                FormalOrdinal = int.MaxValue,
                            },
                        ],
                    },
                },
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(program with {
            GenericMethodDefinitions = [malformedDefinition],
        }, out _, out _, out _, out _), Is.False);
        Assert.That(SemanticVerifier.TryVerifySource(program with {
            SourceGenericTypeDefinitions = [
                program.SourceGenericTypeDefinitions.Single() with {
                    TransparentConstantCarrierType =
                        PrimitiveType.Float32,
                },
            ],
        }, out _, out _, out _, out _), Is.False);
    }

    [Test]
    public void GenericMethodConstantDomainExpandsGenericAliasForProofForwarding()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Transparent)]
            struct Tagged<T> {
                private int32 Value;
            }
            alias Wrapper<T> = Tagged<T>;

            int32 Read<T, const Wrapper<T> Marker>(T value) {
                return 1;
            }
            int32 Forward<T, const Wrapper<T> Marker>(T value) {
                return Read<T, Marker>(value);
            }
            """), new CompilationOptions {
            LayoutCoreRegistration =
                Terrum.CodeAnalysis.Tests.Semantics
                    .LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var wrapper = program.SourceGenericTypeDefinitions.Single();
        var read = program.GenericMethodDefinitions.Single(
            static definition => definition.Name == "Read");
        var forward = program.GenericMethodDefinitions.Single(
            static definition => definition.Name == "Forward");
        var domain = read.ConstantFormals.Single().Domain;
        Assert.That(domain.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.SourceGeneric));
        Assert.That(domain.SourceGenericTypeDefinitionIndex,
            Is.EqualTo(wrapper.DefinitionIndex));
        Assert.That(domain.TypeArguments, Has.Length.EqualTo(1));
        Assert.That(domain.TypeArguments[0].Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Formal));
        Assert.That(domain.TypeArguments[0].FormalOrdinal, Is.Zero);

        var proofBody = forward.ProofProgram.Functions[
            forward.FunctionIndex];
        BoundGenericMethodInstantiation? call = null;
        var visited = BoundTreeTraversal.VisitExpressions(proofBody.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation instantiation,
                    })
                {
                    call = instantiation;
                }
            });
        Assert.That(visited, Is.True);
        Assert.That(call, Is.Not.Null);
        var forwarded = call!.ConstantArguments.Single();
        Assert.That(forwarded.Kind,
            Is.EqualTo(BoundGenericMethodConstantArgumentKind.Formal));
        Assert.That(forwarded.CallerDefinitionOrdinal,
            Is.EqualTo(forward.DefinitionOrdinal));
        Assert.That(forwarded.CallerConstantFormalOrdinal, Is.Zero);
        Assert.That(forwarded.Domain.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.SourceGeneric));
        Assert.That(forwarded.Domain.SourceGenericTypeDefinitionIndex,
            Is.EqualTo(wrapper.DefinitionIndex));
        SemanticVerifier.VerifyLibrary(program);
    }

    [Test]
    public void GenericMethodConstantDomainRejectsGrowingAliasCycles()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Transparent)]
            struct Tagged<T> {
                private int32 Value;
            }
            alias First<T> = Second<option<T>>;
            alias Second<U> = First<U>;
            int32 Read<T, const First<T> Marker>(T value) {
                return 1;
            }
            """), new CompilationOptions {
            LayoutCoreRegistration =
                Terrum.CodeAnalysis.Tests.Semantics
                    .LayoutCoreRegistrationTestData.Create(),
        });

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
            && diagnostic.Message.Contains(
                "First -> Second -> First",
                StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [TestCase("const int32 First = First + 1, T")]
    [TestCase("const int32 First = Second + 1, const int32 Second = 2, T")]
    public void GenericMethodConstantDefaultsRejectSelfAndForwardReferences(
        string formals)
    {
        var source = "int32 Read<" + formals
            + ">(T value) { return 1; }";
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3007"), Is.True);
    }

    [Test]
    public void GenericMethodConstantDefaultsRejectWrongDomainValues()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            int32 Read<const int32 Value = true, T>(T input) {
                return 1;
            }
            """));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void GenericMethodConstantFormalNamesShareTheTypeFormalNamespace()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            int32 Read<const int32 T, T>(T input) { return 1; }
            """));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3007"), Is.True);
    }

    [Test]
    public async Task GenericMethodTypeDefaultsApplyInFormalOrderAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Pair<T> { public T Value; }

            public int32 Accumulate<T = int32, U = T, V = Pair<T>,
                W = ref<T>>(int32 value) {
                return value;
            }

            int32 Main() {
                return Accumulate(42) + Accumulate<uint32>(1);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definition = global::System.Linq.Enumerable.Single(
            program.GenericMethodDefinitions);
        Assert.That(definition.Formals.Select(static formal =>
                formal.Name),
            Is.EqualTo(["T", "U", "V", "W"]));
        Assert.That(definition.Formals[0].TypeDefault!.Term.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Concrete));
        Assert.That(definition.Formals[0].TypeDefault!.Term.ConcreteType,
            Is.EqualTo(PrimitiveType.Int32));
        Assert.That(definition.Formals[0].TypeDefault!.FormalDependencies,
            Is.Empty);
        Assert.That(definition.Formals[1].TypeDefault!.Term.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Formal));
        Assert.That(definition.Formals[1].TypeDefault!.Term.FormalOrdinal,
            Is.Zero);
        Assert.That(definition.Formals[1].TypeDefault!.FormalDependencies,
            Is.EqualTo(s_defaultTypeDependencies));
        Assert.That(definition.Formals[2].TypeDefault!.FormalDependencies,
            Is.EqualTo(s_defaultTypeDependencies));
        Assert.That(definition.Formals[2].TypeDefault!.Term.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.SourceGeneric));
        Assert.That(definition.Formals[2].TypeDefault!.Term
                .SourceGenericTypeDefinitionIndex,
            Is.EqualTo(program.SourceGenericTypeDefinitions.Single()
                .DefinitionIndex));
        Assert.That(definition.Formals[2].TypeDefault!.Term.TypeArguments,
            Has.Length.EqualTo(1));
        Assert.That(definition.Formals[2].TypeDefault!.Term.TypeArguments[0]
                .FormalOrdinal,
            Is.Zero);
        Assert.That(definition.ProofProgram.SourceGenericTypeInstances,
            Is.Empty);
        Assert.That(definition.Formals[3].TypeDefault!.Term.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.CheckedReference));
        Assert.That(definition.Formals[3].TypeDefault!.Term.TypeArguments[0]
                .FormalOrdinal,
            Is.Zero);
        Assert.That(definition.Formals[3].TypeDefault!.FormalDependencies,
            Is.EqualTo(s_defaultTypeDependencies));
        var signature = global::System.Linq.Enumerable.Single(
            program.GenericMethodSignatures);
        Assert.That(signature.Formals.Select(static formal =>
                formal.HasTypeDefault),
            Is.EqualTo([true, true, true, true]));
        Assert.That(signature.Formals[0].DefaultTypeParameterDependencies,
            Is.Empty);
        Assert.That(signature.Formals[1].DefaultTypeParameterDependencies,
            Is.EqualTo(s_defaultTypeDependencies));
        Assert.That(signature.Formals[2].DefaultTypeParameterDependencies,
            Is.EqualTo(s_defaultTypeDependencies));
        Assert.That(signature.Formals[3].DefaultTypeParameterDependencies,
            Is.EqualTo(s_defaultTypeDependencies));
        var malformedSignature = signature with {
            Formals = [
                .. signature.Formals.Select((formal, ordinal) =>
                    ordinal == 1
                        ? formal with {
                            DefaultTypeParameterDependencies = [],
                        }
                        : formal),
            ],
        };
        var malformedProgram = program with {
            GenericMethodSignatures = [malformedSignature],
        };
        Assert.That(SemanticVerifier.TryVerifySource(malformedProgram,
            out _, out _, out _, out _), Is.False);
        var malformedSourceGenericDefault =
            definition.Formals[2].TypeDefault! with {
                Term = definition.Formals[2].TypeDefault!.Term with {
                    SourceGenericTypeDefinitionIndex = int.MaxValue,
                },
            };
        var malformedDefinition = definition with {
            Formals = [
                .. definition.Formals.Select((formal, ordinal) =>
                    ordinal == 2
                        ? formal with {
                            TypeDefault = malformedSourceGenericDefault,
                        }
                        : formal),
            ],
        };
        var malformedTypeTermProgram = program with {
            GenericMethodDefinitions = [malformedDefinition],
        };
        Assert.That(SemanticVerifier.TryVerifySource(
            malformedTypeTermProgram, out _, out _, out _, out _),
            Is.False);
        var main = program.Functions.Single(static function =>
            function.Name == "Main");
        var calls = new System.Collections.Generic.List<
            BoundGenericMethodInstantiation>();
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall: BoundGenericMethodInstantiation call,
                    })
                {
                    calls.Add(call);
                }
            }), Is.True);
        Assert.That(calls, Has.Count.EqualTo(2));
        Assert.That(HasExpectedCallArguments(PrimitiveType.Int32), Is.True);
        Assert.That(HasExpectedCallArguments(PrimitiveType.UInt32), Is.True);
        Assert.That(await RunAsync(NativeEmitter.Emit(program,
            NativeRuntimePolicy.FailFast)).ConfigureAwait(true),
            Is.EqualTo(43));

        bool HasExpectedCallArguments(PrimitiveType type)
        {
            return calls.Any(call => call.TypeArguments.Length == 4
                && call.TypeArguments[0] == type
                && call.TypeArguments[1] == type
                && program.SourceGenericTypeInstances.Any(instance =>
                    instance.TypeArguments.Length == 1
                    && instance.TypeArguments[0] == type
                    && (PrimitiveType)(100 + instance.StructureIndex)
                        == call.TypeArguments[2])
                && CheckedReferenceTypes.IsType(call.TypeArguments[3]));
        }
    }

    [Test]
    public void GenericMethodDefaultTermsSubstituteAndRemapWithoutCapture()
    {
        var nominalIdentity = new BoundGenericNominalTypeIdentity(
            "provider", "types", 0x1300_0001);
        var defaultTerm = new BoundGenericMethodTypeTerm(
            BoundGenericMethodTypeTermKind.SourceGeneric,
            PrimitiveType.Void, -1, 2,
            [
                new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.ImportedGeneric,
                    PrimitiveType.Void, -1, -1,
                    [
                        new BoundGenericMethodTypeTerm(
                            BoundGenericMethodTypeTermKind.Formal,
                            PrimitiveType.Void, 0, -1, [],
                            false, false, false, false),
                        new BoundGenericMethodTypeTerm(
                            BoundGenericMethodTypeTermKind.CheckedReference,
                            PrimitiveType.Void, -1, -1,
                            [
                                new BoundGenericMethodTypeTerm(
                                    BoundGenericMethodTypeTermKind.Formal,
                                    PrimitiveType.Void, 1, -1, [],
                                    false, false, false, false),
                            ],
                            true, false, false, false),
                    ],
                    false, false, false, false) {
                    NominalTypeIdentity = nominalIdentity,
                },
            ],
            false, false, false, false);
        var typeDefault = new BoundGenericMethodTypeDefault(defaultTerm,
            [0, 1]);
        var actuals = ImmutableArray.Create(
            new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.Formal,
                PrimitiveType.Void, 2, -1, [],
                false, false, false, false),
            new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.Concrete,
                PrimitiveType.UInt32, -1, -1, [],
                false, false, false, false));

        Assert.That(BoundTreeTraversal.TrySubstituteGenericMethodTypeDefault(
            typeDefault, actuals, out var substituted), Is.True);
        Assert.That(substituted.FormalDependencies, Is.EqualTo([2]));
        var substitutedSourceGeneric = substituted.Term;
        Assert.That(substitutedSourceGeneric.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.SourceGeneric));
        Assert.That(substitutedSourceGeneric.SourceGenericTypeDefinitionIndex,
            Is.EqualTo(2));
        var substitutedImported
            = substitutedSourceGeneric.TypeArguments[0];
        Assert.That(substitutedImported.NominalTypeIdentity,
            Is.EqualTo(nominalIdentity));
        Assert.That(substitutedImported.TypeArguments[0].FormalOrdinal,
            Is.EqualTo(2));
        Assert.That(substitutedImported.TypeArguments[1].IsWritable,
            Is.True);
        Assert.That(substitutedImported.TypeArguments[1]
                .TypeArguments[0].Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Concrete));
        Assert.That(substitutedImported.TypeArguments[1]
                .TypeArguments[0].ConcreteType,
            Is.EqualTo(PrimitiveType.UInt32));

        var remappedIdentity = new BoundGenericNominalTypeIdentity(
            "consumer", "types", 0x1300_0002);
        Assert.That(BoundTreeTraversal.TryRewriteGenericMethodTypeDefault(
            substituted,
            static type => type == PrimitiveType.UInt32
                ? PrimitiveType.UInt64 : type,
            static ordinal => ordinal + 1,
            static definition => definition + 5,
            identity => identity == nominalIdentity
                ? remappedIdentity : identity,
            out var remapped), Is.True);
        Assert.That(remapped.FormalDependencies, Is.EqualTo([3]));
        Assert.That(remapped.Term.SourceGenericTypeDefinitionIndex,
            Is.EqualTo(7));
        var remappedImported = remapped.Term.TypeArguments[0];
        Assert.That(remappedImported.NominalTypeIdentity,
            Is.EqualTo(remappedIdentity));
        Assert.That(remappedImported.TypeArguments[0].FormalOrdinal,
            Is.EqualTo(3));
        Assert.That(remappedImported.TypeArguments[1]
                .TypeArguments[0].ConcreteType,
            Is.EqualTo(PrimitiveType.UInt64));

        var invalidDefault = typeDefault with {
            FormalDependencies = [1],
        };
        Assert.That(BoundTreeTraversal.TrySubstituteGenericMethodTypeDefault(
            invalidDefault, actuals, out _), Is.False);
        var invalidActuals = actuals.RemoveAt(1);
        Assert.That(BoundTreeTraversal.TrySubstituteGenericMethodTypeDefault(
            typeDefault, invalidActuals, out _), Is.False);
        var noDependencyDefault = new BoundGenericMethodTypeDefault(
            new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.Concrete,
                PrimitiveType.Int32, -1, -1, [],
                false, false, false, false), []);
        var malformedUnusedActual = new BoundGenericMethodTypeTerm(
            BoundGenericMethodTypeTermKind.Formal,
            PrimitiveType.Void, -1, -1, [],
            false, false, false, false);
        Assert.That(BoundTreeTraversal.TrySubstituteGenericMethodTypeDefault(
            noDependencyDefault, [malformedUnusedActual], out _), Is.False);
    }

    [Test]
    public async Task GenericMethodPointerDefaultRetainsBlittableFormalEvidenceAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            public int32 Read<T, U = ptr<T>>(int32 value)
                where T : [Blittable] {
                return value;
            }
            int32 Main() { return Read<int32>(42); }
            """), new CompilationOptions {
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single();
        Assert.That(definition.Formals[0].RequiresBlittable, Is.True);
        Assert.That(definition.Formals[1].TypeDefault!.Term.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.NativePointer));
        Assert.That(definition.Formals[1].TypeDefault!.Term.TypeArguments[0]
                .FormalOrdinal,
            Is.Zero);
        Assert.That(await RunAsync(NativeEmitter.Emit(program,
            NativeRuntimePolicy.FailFast)).ConfigureAwait(true),
            Is.EqualTo(42));
    }

    [Test]
    public void GenericMethodRetainsAndVerifiesNominalInterfaceConstraints()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            public interface I<T> { }
            public int32 Constrained<T>(T value) where T : I<T> {
                return 7;
            }
            int32 Main() { return 42; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single();
        var formal = definition.Formals.Single();
        Assert.That(formal.InterfaceConstraints, Has.Length.EqualTo(1));
        var constraint = formal.InterfaceConstraints[0];
        Assert.That(constraint.Kind,
            Is.EqualTo(BoundInterfaceTypeKind.Nominal));
        Assert.That(constraint.NominalName, Is.EqualTo("I"));
        Assert.That(constraint.TypeArguments[0].Type!.Kind,
            Is.EqualTo(BoundInterfaceTypeKind.TypeParameter));
        Assert.That(constraint.TypeArguments[0].Type!.TypeParameterScope,
            Is.EqualTo(BoundInterfaceTypeParameterScope.Member));
        Assert.That(constraint.TypeArguments[0].Type!.TypeParameterOrdinal,
            Is.Zero);

        var nominalConstraintCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                internal interface I<T> { }
                internal struct Widget { int32 value; }
                internal int32 Constrained<U>(U value)
                    where U : I<Widget> {
                    return 7;
                }
                int32 Main() { return 42; }
                """));
        var nominalProgram = nominalConstraintCompilation.Program!;
        Assert.That(nominalConstraintCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                nominalConstraintCompilation.Diagnostics));
        var nominalArgument = nominalProgram
            .GenericMethodDefinitions.Single().Formals.Single()
            .InterfaceConstraints.Single().TypeArguments.Single().Type!;
        Assert.That(nominalArgument.Kind,
            Is.EqualTo(BoundInterfaceTypeKind.Primitive));
        var widgetIndex = nominalProgram.Structures.Select(
                static (structure, index) => (structure, index))
            .Single(static item => item.structure.Name == "Widget").index;
        Assert.That(nominalArgument.PrimitiveType,
            Is.EqualTo((PrimitiveType)(100 + widgetIndex)));

        var malformedConstraint = constraint.TypeArguments[0].Type! with {
            TypeParameterOrdinal = 1,
        };
        var malformedInterfaceConstraint = constraint with {
            TypeArguments = [constraint.TypeArguments[0] with {
                Type = malformedConstraint,
            }],
        };
        var malformedDefinition = definition with {
            Formals = [
                formal with {
                    InterfaceConstraints = [
                        malformedInterfaceConstraint,
                    ],
                },
            ],
        };
        var signature = program.GenericMethodSignatures.Single();
        var malformedSignature = signature with {
            Formals = [
                signature.Formals.Single() with {
                    InterfaceConstraints = [
                        malformedInterfaceConstraint,
                    ],
                },
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(program with {
            GenericMethodDefinitions = [malformedDefinition],
            GenericMethodSignatures = [malformedSignature],
        }, out _, out _, out _, out _), Is.False);
    }

    [Test]
    public void GenericMethodInterfaceConstraintRequiresWitnessEvidenceAtCall()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            public interface I<T> { }
            public int32 Constrained<T>(T value) where T : I<T> {
                return 7;
            }
            int32 Main() { return Constrained<int32>(42); }
            """));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
            && diagnostic.Message.Contains(
                "interface witness requirement",
                StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public async Task GenericMethodInterfaceConstraintUsesExactDeclaredWitnessAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<T> { }
            internal struct Widget { int32 Value; }
            internal struct Conforming : I<Widget> {
                int32 Value;
                public Conforming(int32 value) { Value = value; }
            }
            int32 Constrained<U>(U value) where U : I<Widget> {
                return 7;
            }
            int32 Main() {
                return Constrained<Conforming>(new Conforming(42));
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var witness = program.InterfaceWitnesses.Single();
        var typeIndices = program.Structures.Select(
                static (structure, index) => (structure.Name, index))
            .ToDictionary(static item => item.Name,
                static item => item.index, StringComparer.Ordinal);
        Assert.That(witness.ImplementingType,
            Is.EqualTo((PrimitiveType)(100
                + typeIndices["Conforming"])));
        Assert.That(witness.InterfaceType.NominalName, Is.EqualTo("I"));
        Assert.That(witness.InterfaceType.TypeArguments.Single().Type!
            .PrimitiveType, Is.EqualTo((PrimitiveType)(100
                + typeIndices["Widget"])));
        Assert.That(await RunAsync(NativeEmitter.Emit(program,
            NativeRuntimePolicy.FailFast)).ConfigureAwait(true),
            Is.EqualTo(7));
    }

    [Test]
    public async Task GenericMethodInterfaceConstraintRetainsMixedFormalOrdinalAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<T> { }
            internal struct S : I<S> {
                int32 Value;
                public S(int32 value) { Value = value; }
            }
            int32 Constrained<const int32 Count, T>(T value)
                where T : I<T> {
                return Count;
            }
            int32 Main() {
                return Constrained<7, S>(new S(42));
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var definition = compilation.Program!.GenericMethodDefinitions
            .Single(static method => method.Name == "Constrained");
        Assert.That(definition.Formals.Single().InterfaceConstraints
            .Single().TypeArguments.Single().Type!
            .TypeParameterOrdinal, Is.EqualTo(1));
        Assert.That(await RunAsync(NativeEmitter.Emit(
            compilation.Program, NativeRuntimePolicy.FailFast))
            .ConfigureAwait(true), Is.EqualTo(7));
    }

    [Test]
    public async Task GenericMethodInterfaceConstraintForwardsMixedConstantFormalAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<T, const int32 Count> { }
            internal struct S : I<S, 7> {
                int32 Value;
                public S(int32 value) { Value = value; }
            }
            int32 Needs<const int32 Count, T>(T value)
                where T : I<T, Count> {
                return Count;
            }
            int32 Outer<const int32 Value, T>(T value)
                where T : I<T, Value> {
                return Needs<Value, T>(value);
            }
            int32 Main() {
                return Outer<7, S>(new S(42));
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(await RunAsync(NativeEmitter.Emit(
            compilation.Program!, NativeRuntimePolicy.FailFast))
            .ConfigureAwait(true), Is.EqualTo(7));
    }

    [Test]
    public void GenericMethodInterfaceConstraintRejectsStructuralMatch()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<T> { }
            internal struct Structural { int32 Value; }
            int32 Constrained<U>(U value) where U : I<U> {
                return 7;
            }
            int32 Main() {
                return Constrained<Structural>(new Structural());
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
            && diagnostic.Message.Contains(
                "interface witness requirement",
                StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public async Task GenericMethodConstraintBindsCallableWitnessByExactSelfTypeAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<this T> {
                bool Equals(T other);
            }
            internal struct Conforming : I<Conforming> {
                int32 Value;
                public Conforming(int32 value) { Value = value; }
                public bool Equals(Conforming other) {
                    return Value == other.Value;
                }
            }
            int32 Constrained<U>(U value) where U : I<U> {
                return 7;
            }
            int32 Main() {
                return Constrained<Conforming>(new Conforming(42));
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var witness = program.InterfaceWitnesses.Single();
        Assert.That(witness.Members, Has.Length.EqualTo(1));
        Assert.That(witness.Members[0].TargetKind,
            Is.EqualTo(BoundInterfaceWitnessTargetKind.Implementation));
        Assert.That(witness.Members[0].FunctionIndex,
            Is.GreaterThanOrEqualTo(0));
        Assert.That(SemanticVerifier.TryVerifySource(program with {
            InterfaceWitnesses = program.InterfaceWitnesses.SetItem(0,
                witness with { Members = [] }),
        }, out _, out _, out _, out _), Is.False);
        Assert.That(SemanticVerifier.TryVerifySource(program with {
            InterfaceWitnesses = program.InterfaceWitnesses.SetItem(0,
                witness with {
                    Members = [witness.Members[0] with {
                        FunctionIndex = program.Functions.Length,
                    }],
                }),
        }, out _, out _, out _, out _), Is.False);
        Assert.That(await RunAsync(NativeEmitter.Emit(program,
            NativeRuntimePolicy.FailFast)).ConfigureAwait(true),
            Is.EqualTo(7));
    }

    [Test]
    public void InterfaceWitnessValidatesAssociatedConstantDomain()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I {
                const int32 Count;
            }
            internal struct S { int32 Value; }
            int32 Main() { return 42; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var interfaceDefinition = program.Interfaces.Single();
        var requirement = interfaceDefinition.Members.Single();
        var interfaceType = new BoundInterfaceType(
            BoundInterfaceTypeKind.Nominal, PrimitiveType.Void, "I", -1,
            [], false, false, false, false);
        var memberIdentity = new BoundInterfaceMemberIdentity(
            interfaceType, requirement.Ordinal, requirement.DeclarationKey);
        var structureIndex = program.Structures.Select(
                static (structure, index) => (structure.Name, index))
            .Single(static item => item.Name == "S").index;
        var witness = new BoundInterfaceWitness(
            (PrimitiveType)(100 + structureIndex), interfaceType, 0,
            default, [], [],
            [new BoundInterfaceAssociatedConstantBinding(memberIdentity,
                new BoundInterfaceType(BoundInterfaceTypeKind.Primitive,
                    PrimitiveType.Int32, null, -1, [], false, false,
                    false, false),
                new BoundConstant(3, PrimitiveType.Int32,
                    default))]);
        var validProgram = program with {
            InterfaceWitnesses = [witness],
        };
        Assert.That(SemanticVerifier.TryVerifySource(validProgram,
            out _, out _, out _, out _), Is.True);

        var wrongDomain = witness with {
            AssociatedConstants = [
                witness.AssociatedConstants[0] with {
                    Type = new BoundInterfaceType(
                        BoundInterfaceTypeKind.Primitive,
                        PrimitiveType.UInt32, null, -1, [], false,
                        false, false, false),
                },
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(validProgram with {
            InterfaceWitnesses = [wrongDomain],
        }, out _, out _, out _, out _), Is.False);
    }

    [Test]
    public async Task GenericMethodInterfaceConstraintForwardsCallerEvidenceAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface I<T> { }
            int32 Inner<U>(U value) where U : I<U> { return 7; }
            int32 Outer<T>(T value) where T : I<T> {
                return Inner<T>(value);
            }
            int32 Main() { return 42; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(await RunAsync(NativeEmitter.Emit(
            compilation.Program!, NativeRuntimePolicy.FailFast))
            .ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public async Task GenericMethodInterfaceConstraintUsesInheritedWitnessViewAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            internal interface IBase<T> { }
            internal interface IDerived<T> : IBase<T> { }
            internal struct Widget { int32 Value; }
            internal struct Conforming : IDerived<Widget> {
                int32 Value;
                public Conforming(int32 value) { Value = value; }
            }
            int32 NeedsBase<T>(T value) where T : IBase<Widget> {
                return 1;
            }
            int32 Forward<T>(T value) where T : IDerived<Widget> {
                return NeedsBase<T>(value);
            }
            int32 Main() {
                return Forward<Conforming>(new Conforming(42));
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program!.InterfaceWitnesses, Has.Length.EqualTo(1));
        Assert.That(await RunAsync(NativeEmitter.Emit(
            compilation.Program, NativeRuntimePolicy.FailFast))
            .ConfigureAwait(true), Is.EqualTo(1));
    }

    [Test]
    public void GenericOwnerMethodInterfaceConstraintRetainsOwnerFormalScope()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public interface I<V> { }
            internal struct Box<T> {
                T value;
                internal static int32 Constrained<U>(U value)
                    where U : I<T> {
                    return 7;
                }
            }
            internal int32 Take(Box<int32> value) { return 42; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var definition = compilation.Program!.GenericMethodDefinitions
            .Single(static method => method.Name.Contains("Constrained",
                StringComparison.Ordinal));
        var argument = definition.Formals.Single()
            .InterfaceConstraints.Single().TypeArguments.Single().Type!;
        Assert.That(argument.Kind,
            Is.EqualTo(BoundInterfaceTypeKind.TypeParameter));
        Assert.That(argument.TypeParameterScope,
            Is.EqualTo(BoundInterfaceTypeParameterScope.Interface));
        Assert.That(argument.TypeParameterOrdinal, Is.Zero);
        Assert.That(definition.Owner!.TypeArguments,
            Is.EqualTo([PrimitiveType.Int32]));
    }

    [Test]
    public async Task GenericMethodFixedArrayDefaultRetainsBlittableEvidenceAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            public int32 Read<T, U = [T; 2]>(int32 value)
                where T : [Blittable] {
                return value;
            }
            int32 Main() { return Read<int32>(42); }
            """), new CompilationOptions {
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single();
        var arrayTerm = definition.Formals[1].TypeDefault!.Term;
        Assert.That(definition.Formals[0].RequiresBlittable, Is.True);
        Assert.That(arrayTerm.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.NativeFixedArray));
        Assert.That(arrayTerm.ElementCount, Is.EqualTo(2));
        Assert.That(arrayTerm.TypeArguments.Single().FormalOrdinal,
            Is.Zero);
        Assert.That(await RunAsync(NativeEmitter.Emit(program,
            NativeRuntimePolicy.FailFast)).ConfigureAwait(true),
            Is.EqualTo(42));
    }

    [Test]
    public async Task GenericMethodFunctionPointerDefaultRetainsBlittableEvidenceAsync()
    {
        var nativeCoreRegistration
            = NativeCoreRegistrationTests.CreateRegistration();
        var layoutCoreRegistration
            = LayoutCoreRegistrationTestData.CreateForBindings(
                nativeCoreRegistration.ModuleBindings,
                LayoutCoreRegistrationTestData.CreateDeclarations(
                    "core.native"));
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Terrum.Interop;
            public int32 Read<T, U =
                [NativeCallConv(NativeCallingConvention.Cdecl)] fnptr<ptr<T>, void>>(int32 value)
                where T : [Blittable] {
                return value;
            }
            int32 Main() { return Read<int32>(42); }
            """), new CompilationOptions {
            LayoutCoreRegistration = layoutCoreRegistration,
            NativeCoreRegistration = nativeCoreRegistration,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single();
        var functionPointerTerm = definition.Formals[1].TypeDefault!.Term;
        Assert.That(functionPointerTerm.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.NativeFunctionPointer));
        Assert.That(functionPointerTerm.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Cdecl));
        Assert.That(functionPointerTerm.ParameterCount, Is.EqualTo(1));
        Assert.That(functionPointerTerm.TypeArguments[0].Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.NativePointer));
        Assert.That(functionPointerTerm.TypeArguments[1].AllowsVoid, Is.True);
        var malformedTerm = functionPointerTerm with {
            ParameterCount = 0,
        };
        var malformedDefinition = definition with {
            Formals = [
                .. definition.Formals.Select((formal, ordinal) =>
                    ordinal == 1
                        ? formal with {
                            TypeDefault = formal.TypeDefault! with {
                                Term = malformedTerm,
                            },
                        }
                        : formal),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(program with {
            GenericMethodDefinitions = [malformedDefinition],
        }, out _, out _, out _, out _), Is.False);
        Assert.That(await RunAsync(NativeEmitter.Emit(program,
            NativeRuntimePolicy.FailFast)).ConfigureAwait(true),
            Is.EqualTo(42));
    }

    [Test]
    public void GenericMethodFunctionPointerRejectsUnprovenParameterKinds()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            public int32 Read<T, U = fnptr<T, void>>(int32 value)
                where T : [Blittable] {
                return value;
            }
            int32 Main() { return Read<int32>(42); }
            """), new CompilationOptions {
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        });

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3007"), Is.True);
    }

    [Test]
    public void GenericMethodPointerDefaultRequiresBlittableEvidence()
    {
        var missingEvidence = Compilation.Create(SyntaxTree.Parse("""
            public int32 Read<T, U = ptr<T>>(int32 value) {
                return value;
            }
            int32 Main() { return Read<int32>(42); }
            """));
        Assert.That(missingEvidence.HasErrors, Is.True);
        Assert.That(missingEvidence.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3007"), Is.True);

        var invalidActual = Compilation.Create(SyntaxTree.Parse("""
            struct NotBlittable { public int32 Value; }
            public int32 Read<T, U = ptr<T>>(int32 value)
                where T : [Blittable] {
                return value;
            }
            int32 Main() { return Read<NotBlittable>(42); }
            """), new CompilationOptions {
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(invalidActual.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("Blittable constraint",
                    StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void GenericMethodDefaultsAndCallsRespectNonnullFormalDomains()
    {
        var nullableDefault = Compilation.Create(SyntaxTree.Parse("""
            public int32 Read<T = int32, U = T?>(int32 value) {
                return value;
            }
            int32 Main() { return Read(42); }
            """));
        Assert.That(nullableDefault.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3007"), Is.True);

        var nullableActual = Compilation.Create(SyntaxTree.Parse("""
            class Node { public int32 Value; public Node() { Value = 0; } }
            public int32 Take<T>(T value) { return 0; }
            int32 Main() { return Take<Node?>(null); }
            """));
        Assert.That(nullableActual.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("nullability domain",
                    StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void GenericMethodNullableDomainAcceptsOptionalActuals()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Node { public int32 Value; public Node() { Value = 0; } }
            public T Echo<T>(T value) where T : null? {
                return value;
            }
            int32 Main() {
                Node? value = null;
                Node? echoed = Echo<Node?>(value);
                return 0;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single();
        Assert.That(definition.Formals[0].NullabilityDomain, Is.EqualTo(2));
        var main = program.Functions.Single(static function =>
            function.Name == "Main");
        BoundGenericMethodInstantiation? call = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall: BoundGenericMethodInstantiation
                            instantiation,
                    })
                {
                    call = instantiation;
                }
            }), Is.True);
        Assert.That(call, Is.Not.Null);
        Assert.That(ManagedClassTypes.IsOptional(
            call!.TypeArguments.Single()), Is.True);
    }

    [Test]
    public async Task GenericMethodWriteRequirementConstrainsActualAndDefaultAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            public int32 Use<T, U = write T>(int32 value)
                where T : write
                where U : write {
                return value;
            }
            int32 Main() {
                return Use<write ptr<int32>>(42);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var definition = compilation.Program!.GenericMethodDefinitions
            .Single();
        Assert.That(definition.Formals.Select(static formal =>
                formal.RequiresWritable),
            Is.EqualTo([true, true]));
        Assert.That(definition.Formals[1].TypeDefault!.Term.IsWritable,
            Is.True);
        Assert.That(await RunAsync(NativeEmitter.Emit(compilation.Program,
            NativeRuntimePolicy.FailFast)).ConfigureAwait(true),
            Is.EqualTo(42));

        var invalid = Compilation.Create(SyntaxTree.Parse("""
            public int32 Use<T>(int32 value) where T : write {
                return value;
            }
            int32 Main() { return Use<ptr<int32>>(42); }
            """));
        Assert.That(invalid.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("write requirement",
                    StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void GenericMethodWriteRequirementDistinguishesSpanElementFromOuterAccess()
    {
        var writable = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Use<T>(Span<write T> values)
                where T : write { return 42; }
            """));
        Assert.That(writable.HasErrors, Is.False,
            string.Join(Environment.NewLine, writable.Diagnostics));
        SemanticVerifier.Verify(writable.Program!);
        var definition = writable.Program!.GenericMethodDefinitions.Single();
        var spanType = definition.ProofProgram.SpanTypes.Single();
        Assert.That(spanType.ElementFormalDefinitionOrdinal,
            Is.EqualTo(definition.DefinitionOrdinal));
        Assert.That(spanType.ElementFormalOrdinal, Is.Zero);
        Assert.That(spanType.IsElementWritable, Is.True);

        var missingRequirement = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public int32 Use<T>(Span<write T> values) {
                    return 42;
                }
                """));
        Assert.That(missingRequirement.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3005"),
            Is.True);

        var outerAccess = Compilation.Create(SyntaxTree.Parse("""
            int32 Use<T>(int32 value) where T : write { return value; }
            int32 Main() {
                return Use<write Span<int32>>(42);
            }
            """));
        Assert.That(outerAccess.HasErrors, Is.False,
            string.Join(Environment.NewLine, outerAccess.Diagnostics));
        SemanticVerifier.Verify(outerAccess.Program!);

        var innerAccess = Compilation.Create(SyntaxTree.Parse("""
            int32 Use<T>(int32 value) where T : write { return value; }
            int32 Main() {
                return Use<Span<write ptr<int32>>>(42);
            }
            """));
        Assert.That(innerAccess.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("write requirement",
                    StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void GenericMethodInferencePreservesSpanElementFormals()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Read<T>(Span<T> values) { return 1; }
            public int32 Forward<U>(Span<U> values) {
                return Read(values);
            }
            public int32 Write<T>(Span<write T> values)
                where T : write { return 1; }
            public int32 ForwardWrite<U>(Span<write U> values)
                where U : write {
                return Write(values);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.Verify(compilation.Program!);

        var definitions = compilation.Program!.GenericMethodDefinitions;
        var read = definitions.Single(static definition =>
            definition.Name == "Read");
        var forward = definitions.Single(static definition =>
            definition.Name == "Forward");
        var write = definitions.Single(static definition =>
            definition.Name == "Write");
        var forwardWrite = definitions.Single(static definition =>
            definition.Name == "ForwardWrite");

        static BoundGenericMethodInstantiation GetCall(
            BoundGenericMethodDefinition definition)
        {
            var function = definition.ProofProgram.Functions[
                definition.FunctionIndex];
            var calls = new List<BoundGenericMethodInstantiation>();
            Assert.That(BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall {
                            GenericMethodCall:
                                BoundGenericMethodInstantiation call,
                        })
                    {
                        calls.Add(call);
                    }
                }), Is.True);
            return calls.Single();
        }

        var readCall = GetCall(forward);
        Assert.That(readCall.DefinitionOrdinal,
            Is.EqualTo(read.DefinitionOrdinal));
        Assert.That(readCall.TypeArguments,
            Is.EqualTo(forward.ProofProgram.Functions[
                forward.FunctionIndex].GenericMethodProofTypeParameters));

        var writeCall = GetCall(forwardWrite);
        Assert.That(writeCall.DefinitionOrdinal,
            Is.EqualTo(write.DefinitionOrdinal));
        Assert.That(writeCall.TypeArguments,
            Is.EqualTo(forwardWrite.ProofProgram.Functions[
                forwardWrite.FunctionIndex]
                    .GenericMethodProofTypeParameters));
    }

    [Test]
    public void GenericMethodInferencePreservesSpanAliasElementAccess()
    {
        var compilation = Compilation.CreateLibrary([
            SyntaxTree.Parse("""
                public int32 Write<T>(NestedElementWritableSpan<T> values)
                    where T : write { return 1; }
                public int32 Forward<U>(NestedElementWritableSpan<U> values)
                    where U : write {
                    return Write(values);
                }
                public bool SameAliasSpanType<V>() {
                    return typeof(NestedElementWritableSpan<V>)
                        == typeof(Span<write V>);
                }
                """),
            SyntaxTree.Parse("""
                alias ElementWritableSpan<T> = Span<write T>;
                alias NestedElementWritableSpan<T>
                    = ElementWritableSpan<T>;
                """),
        ]);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.Verify(compilation.Program!);

        var program = compilation.Program!;
        var write = program.GenericMethodDefinitions.Single(
            static definition => definition.Name == "Write");
        var writeSpan = write.ProofProgram.SpanTypes.Single();
        Assert.That(writeSpan.ElementFormalDefinitionOrdinal,
            Is.EqualTo(write.DefinitionOrdinal));
        Assert.That(writeSpan.ElementFormalOrdinal, Is.Zero);
        Assert.That(writeSpan.IsElementWritable, Is.True);

        var forward = program.GenericMethodDefinitions.Single(
            static definition => definition.Name == "Forward");
        var proofBody = forward.ProofProgram.Functions[
            forward.FunctionIndex];
        BoundGenericMethodInstantiation? call = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(proofBody.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation instantiation,
                    })
                {
                    call = instantiation;
                }
            }), Is.True);
        Assert.That(call, Is.Not.Null);
        var inferredSpan = forward.ProofProgram.SpanTypes[
            BoundSpanTypeTypes.Index(call!.TypeArguments.Single())];
        Assert.That(inferredSpan.ElementFormalDefinitionOrdinal,
            Is.EqualTo(forward.DefinitionOrdinal));
        Assert.That(inferredSpan.ElementFormalOrdinal, Is.Zero);
        Assert.That(inferredSpan.IsElementWritable, Is.True);

        var sameAliasType = program.GenericMethodDefinitions.Single(
            static definition =>
                definition.Name == "SameAliasSpanType");
        BoundGenericMethodTypeTerm? aliasQueryTerm = null;
        var aliasQueryType = PrimitiveType.Void;
        var aliasQueryBody = sameAliasType.ProofProgram.Functions[
            sameAliasType.FunctionIndex];
        Assert.That(BoundTreeTraversal.VisitExpressions(aliasQueryBody.Body,
            expression =>
            {
                if (expression is BoundTypeIdentityQuery query)
                {
                    aliasQueryTerm = query.Subject.TypeIdentityTerm;
                    aliasQueryType = query.Subject.Type;
                }
            }), Is.True);
        Assert.That(aliasQueryTerm?.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Span));
        Assert.That(aliasQueryTerm!.IsElementWritable, Is.True);
        Assert.That(aliasQueryTerm.TypeArguments.Single().Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Formal));
        Assert.That(aliasQueryTerm.TypeArguments.Single().FormalOrdinal,
            Is.Zero);
        Assert.That(BoundSpanTypeTypes.IsValidType(aliasQueryType,
            sameAliasType.ProofProgram.SpanTypes), Is.True);
        Assert.That(sameAliasType.ProofProgram.SpanTypes[
                BoundSpanTypeTypes.Index(aliasQueryType)]
                .ElementFormalDefinitionOrdinal,
            Is.EqualTo(sameAliasType.DefinitionOrdinal));

        var foreignOwnerSpanIndex = forward.ProofProgram.SpanTypes
            .Select((span, index) => (span, index))
            .Single(pair => pair.span.ElementFormalDefinitionOrdinal
                    == forward.DefinitionOrdinal
                && pair.span.ElementFormalOrdinal == 0
                && pair.span.IsElementWritable)
            .index;
        var malformedSpanTypes = forward.ProofProgram.SpanTypes.ToBuilder();
        malformedSpanTypes[foreignOwnerSpanIndex]
            = malformedSpanTypes[foreignOwnerSpanIndex] with {
                ElementFormalDefinitionOrdinal = write.DefinitionOrdinal,
            };
        var malformedForward = forward with {
            ProofProgram = forward.ProofProgram with {
                SpanTypes = malformedSpanTypes.ToImmutable(),
            },
        };
        var malformedProgram = program with {
            GenericMethodDefinitions = [
                .. program.GenericMethodDefinitions.Select(candidate =>
                    candidate.DefinitionOrdinal
                        == forward.DefinitionOrdinal
                        ? malformedForward : candidate),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(malformedProgram,
            out _, out _, out _, out _), Is.False);

        var missingRequirement = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                alias ElementWritableSpan<T> = Span<write T>;
                alias NestedElementWritableSpan<T>
                    = ElementWritableSpan<T>;
                int32 Write<T>(NestedElementWritableSpan<T> values) {
                    return 1;
                }
                """));
        Assert.That(missingRequirement.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3005"), Is.True);
    }

    [Test]
    public void GenericSourceTypeArgumentsPreserveSpanFormalOwner()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Marker<T> { public int32 Value; }
            public bool Same<T>() where T : write {
                return typeof(Marker<Span<write T>>)
                    == typeof(Marker<Span<write T>>);
            }
            public bool Other<U>() where U : write {
                return typeof(Marker<Span<write U>>)
                    == typeof(Marker<Span<write U>>);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.Verify(compilation.Program!);

        var program = compilation.Program!;
        var same = program.GenericMethodDefinitions.Single(
            static definition => definition.Name == "Same");
        var other = program.GenericMethodDefinitions.Single(
            static definition => definition.Name == "Other");
        var marker = same.ProofProgram.SourceGenericTypeDefinitions.Single();
        var sameInstanceIndex = same.ProofProgram.SourceGenericTypeInstances
            .Select((instance, index) => (instance, index))
            .Single(pair => pair.instance.DefinitionIndex
                    == marker.DefinitionIndex
                && BoundSpanTypeTypes.IsValidType(
                    pair.instance.TypeArguments.Single(),
                    same.ProofProgram.SpanTypes))
            .index;
        var sameSpanType = same.ProofProgram.SourceGenericTypeInstances[
            sameInstanceIndex].TypeArguments.Single();
        var sameSpanIndex = BoundSpanTypeTypes.Index(sameSpanType);
        Assert.That(same.ProofProgram.SpanTypes[
                sameSpanIndex]
                .ElementFormalDefinitionOrdinal,
            Is.EqualTo(same.DefinitionOrdinal));

        var malformedSpanTypes = same.ProofProgram.SpanTypes.ToBuilder();
        malformedSpanTypes[sameSpanIndex]
            = malformedSpanTypes[sameSpanIndex] with {
                ElementFormalDefinitionOrdinal = other.DefinitionOrdinal,
            };
        var malformedSame = same with {
            ProofProgram = same.ProofProgram with {
                SpanTypes = malformedSpanTypes.ToImmutable(),
            },
        };
        var malformedProgram = program with {
            GenericMethodDefinitions = [
                .. program.GenericMethodDefinitions.Select(candidate =>
                    candidate.DefinitionOrdinal == same.DefinitionOrdinal
                        ? malformedSame : candidate),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(malformedProgram,
            out _, out _, out _, out _), Is.False);
    }

    [Test]
    public void GenericSourceTypeTermsForwardEarlierConstantDefaults()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Container<const int32 Count, T = Cell<Count>,
                const int32 Size = Count> { public int32 Value; }
            struct Cell<const int32 Count> { public int32 Value; }
            public bool Same<const int32 Count>() {
                return typeof(Container<Count>)
                    == typeof(Container<Count, Cell<Count>, Count>)
                    && typeof(Container<Count, default, default>)
                    == typeof(Container<Count, Cell<Count>, Count>);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.Verify(compilation.Program!);

        var definition = compilation.Program!.GenericMethodDefinitions
            .Single(static method => method.Name == "Same");
        var proofProgram = definition.ProofProgram;
        var containerDefinitionIndex = proofProgram
            .SourceGenericTypeDefinitions.Single(
                static type => type.Name == "Container").DefinitionIndex;
        var cellDefinitionIndex = proofProgram.SourceGenericTypeDefinitions
            .Single(static type => type.Name == "Cell").DefinitionIndex;
        var function = proofProgram.Functions[
            definition.FunctionIndex];
        var terms = new List<BoundGenericMethodTypeTerm>();
        Assert.That(BoundTreeTraversal.VisitExpressions(function.Body,
            expression =>
            {
                if (expression is BoundTypeIdentityQuery query
                    && query.Subject.TypeIdentityTerm
                        is BoundGenericMethodTypeTerm term)
                {
                    terms.Add(term);
                }
            }), Is.True);
        Assert.That(terms, Has.Count.EqualTo(4));
        foreach (var term in terms)
        {
            Assert.That(term.Kind,
                Is.EqualTo(BoundGenericMethodTypeTermKind.SourceGeneric));
            Assert.That(term.SourceGenericTypeDefinitionIndex,
                Is.EqualTo(containerDefinitionIndex));
            Assert.That(term.ConstantArguments, Has.Length.EqualTo(2));
            Assert.That(term.ConstantArguments[0],
                Is.EqualTo(term.ConstantArguments[1]));
            Assert.That(term.ConstantArguments[0].Kind,
                Is.EqualTo(BoundGenericMethodTypeConstantArgumentKind.Formal));
            Assert.That(term.ConstantArguments[0].FormalOwnerKind,
                Is.EqualTo(BoundGenericTypeFormalOwnerKind.GenericMethod));
            Assert.That(term.ConstantArguments[0].FormalDefinitionOrdinal,
                Is.EqualTo(definition.DefinitionOrdinal));
            Assert.That(term.ConstantArguments[0].FormalOrdinal, Is.Zero);

            var defaultType = term.TypeArguments.Single();
            Assert.That(defaultType.Kind,
                Is.EqualTo(BoundGenericMethodTypeTermKind.SourceGeneric));
            Assert.That(defaultType.SourceGenericTypeDefinitionIndex,
                Is.EqualTo(cellDefinitionIndex));
            Assert.That(defaultType.ConstantArguments, Has.Length.EqualTo(1));
            Assert.That(defaultType.ConstantArguments.Single(),
                Is.EqualTo(term.ConstantArguments[0]));
        }
    }

    [Test]
    public void GenericMethodInferencePreservesSpanAccessAndInit()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Read<T>(Span<T> values) { return 1; }
            public int32 ForwardWritable<U>(write Span<U> values) {
                return Read(values);
            }
            public int32 ReadInitialized<T>(Span<init T> values) {
                return 1;
            }
            public int32 ForwardInitialized<U>(Span<init U> values) {
                return ReadInitialized(move values);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.Verify(compilation.Program!);

        var invalid = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Write<T>(write Span<T> values)
                where T : write { return 1; }
            public int32 ForwardInner<U>(Span<write U> values)
                where U : write {
                return Write(values);
            }
            public int32 ReadInitialized<T>(Span<init T> values) {
                return 1;
            }
            public int32 ForwardReadonly<U>(Span<U> values) {
                return ReadInitialized(values);
            }
            """));
        Assert.That(invalid.HasErrors, Is.True);
        Assert.That(invalid.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True);
        Assert.That(invalid.Program, Is.Null);
    }

    [Test]
    public void GenericMethodTypeDefaultsAndQueriesPreserveNestedSpanTypes()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            public int32 Identity<T>(int32 value) { return value; }
            public int32 Apply<T, U = Span<Span<T>>>(int32 value) {
                return Identity<U>(value);
            }
            public bool SameSpanType<T>() {
                return typeof(Span<T>) == typeof(Span<T>);
            }
            int32 Main() { return Apply<int32>(42); }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.Verify(compilation.Program!);

        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single(
            static method => method.Name == "Apply");
        var typeDefault = definition.Formals[1].TypeDefault!;
        Assert.That(typeDefault.Term.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Span));
        Assert.That(typeDefault.Term.TypeArguments[0].Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Span));
        Assert.That(typeDefault.Term.TypeArguments[0].TypeArguments[0]
                .FormalOrdinal,
            Is.Zero);
        Assert.That(typeDefault.FormalDependencies,
            Is.EqualTo(new[] { 0 }));

        BoundGenericMethodInstantiation? call = null;
        var main = program.Functions.Single(static function =>
            function.Name == "Main");
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation instantiation,
                    })
                {
                    call = instantiation;
                }
            }), Is.True);
        Assert.That(call, Is.Not.Null);
        Assert.That(call!.TypeArguments, Has.Length.EqualTo(2));
        var nestedSpanType = call.TypeArguments[1];
        Assert.That(BoundSpanTypeTypes.IsValidType(nestedSpanType,
            program.SpanTypes), Is.True);
        var outerSpan = program.SpanTypes[
            BoundSpanTypeTypes.Index(nestedSpanType)];
        Assert.That(BoundSpanTypeTypes.IsValidType(outerSpan.ElementType,
            program.SpanTypes), Is.True);

        var queryDefinition = program.GenericMethodDefinitions.Single(
            static method => method.Name == "SameSpanType");
        BoundGenericMethodTypeTerm? queryTypeTerm = null;
        var queryFunction = queryDefinition.ProofProgram.Functions[
            queryDefinition.FunctionIndex];
        Assert.That(BoundTreeTraversal.VisitExpressions(queryFunction.Body,
            expression =>
            {
                if (expression is BoundTypeIdentityQuery query)
                {
                    queryTypeTerm = query.Subject.TypeIdentityTerm;
                }
            }), Is.True);
        Assert.That(queryTypeTerm?.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Span));
        Assert.That(queryTypeTerm!.TypeArguments[0].Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Formal));
        Assert.That(queryTypeTerm.TypeArguments[0].FormalOrdinal,
            Is.Zero);
    }

    [Test]
    public void RejectsForeignSpanFormalOwnerInGenericTypeIdentityQuery()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public bool SameSpanType<T>() {
                return typeof(Span<T>) == typeof(Span<T>);
            }
            public bool OtherSpanType<U>() {
                return typeof(Span<U>) == typeof(Span<U>);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single(
            static method => method.Name == "SameSpanType");
        var otherDefinition = program.GenericMethodDefinitions.Single(
            static method => method.Name == "OtherSpanType");
        var foreignSpanType = BoundSpanTypeTypes.Type(
            program.SpanTypes
                .Select((spanType, index) => (spanType, index))
                .Single(pair => pair.spanType.ElementFormalDefinitionOrdinal
                    == otherDefinition.DefinitionOrdinal).index);
        var proofFunctions
            = definition.ProofProgram.Functions.ToBuilder();
        proofFunctions[definition.FunctionIndex]
            = BoundTreeTraversal.RewriteExpressions(
                proofFunctions[definition.FunctionIndex],
                expression => expression is BoundTypeIdentityQuery identity
                    ? identity with {
                        Subject = identity.Subject with {
                            Type = foreignSpanType,
                        },
                    }
                    : expression);
        var malformedDefinition = definition with {
            ProofProgram = definition.ProofProgram with {
                Functions = proofFunctions.ToImmutable(),
            },
        };
        var malformedProgram = program with {
            GenericMethodDefinitions = [
                .. program.GenericMethodDefinitions.Select(candidate =>
                    candidate.DefinitionOrdinal
                        == definition.DefinitionOrdinal
                        ? malformedDefinition : candidate),
            ],
        };

        Assert.That(SemanticVerifier.TryVerifySource(malformedProgram,
            out _, out _, out _, out _), Is.False);
    }

    [Test]
    public async Task GenericMethodOwnershipDomainsAcceptCopyAndUniqueActualsAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public int32 Value;
                public Owner(int32 value) { Value = value; }
            }
            public int32 UniqueOnly<T>(int32 value) where T : unique {
                return value;
            }
            public int32 UniqueDefault<T = Owner>(int32 value)
                where T : unique {
                return value;
            }
            public T ForwardUnique<T>(T value) where T : unique {
                return move value;
            }
            public int32 Either<T>(int32 value) where T : unique? {
                return value;
            }
            int32 Main() {
                Owner source = new Owner(2);
                Owner forwarded = ForwardUnique<Owner>(move source);
                return UniqueOnly<Owner>(40)
                    + UniqueDefault(0)
                    + Either<int32>(1)
                    + Either<Owner>(1)
                    + forwarded.Value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definitions = program.GenericMethodDefinitions
            .ToDictionary(static definition => definition.Name,
                StringComparer.Ordinal);
        Assert.That(definitions["UniqueOnly"].Formals[0].OwnershipDomain,
            Is.EqualTo(2));
        Assert.That(definitions["UniqueDefault"].Formals[0].TypeDefault!.Term
                .ConcreteType,
            Is.EqualTo((PrimitiveType)(100
                + program.Structures.Select((structure, index) =>
                    (structure, index))
                .Single(static item => item.structure.Name == "Owner")
                .index)));
        Assert.That(definitions["Either"].Formals[0].OwnershipDomain,
            Is.EqualTo(3));
        Assert.That(await RunAsync(NativeEmitter.Emit(program,
            NativeRuntimePolicy.FailFast)).ConfigureAwait(true),
            Is.EqualTo(44));

        var invalid = Compilation.Create(SyntaxTree.Parse("""
            public int32 UniqueOnly<T>(int32 value) where T : unique {
                return value;
            }
            int32 Main() { return UniqueOnly<int32>(42); }
            """));
        Assert.That(invalid.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("ownership domain",
                    StringComparison.Ordinal)), Is.True);

        var unrefinedMove = Compilation.Create(SyntaxTree.Parse("""
            public T Forward<T>(T value) where T : unique? {
                return move value;
            }
            int32 Main() { return 0; }
            """));
        Assert.That(unrefinedMove.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3007"
                && diagnostic.Message.Contains("type-property refinement",
                    StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public async Task GenericUniquePropertyRefinesOwnershipInBothBranchesAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public int32 Value;
                public Owner(int32 value) { Value = value; }
            }
            public T ForwardUnique<T>(T value) where T : unique {
                return move value;
            }
            public T ForwardCopy<T>(T value) {
                return value;
            }
            public T Forward<T>(T value) where T : unique? {
                if (T is unique) {
                    return ForwardUnique<T>(move value);
                }
                else {
                    return ForwardCopy<T>(value);
                }
            }
            public T ForwardSaved<T>(T value) where T : unique? {
                bool isUnique = (T is unique);
                bool savedUnique = isUnique;
                if (savedUnique) {
                    return ForwardUnique<T>(move value);
                }
                else {
                    return ForwardCopy<T>(value);
                }
            }
            public T ForwardNegated<T>(T value) where T : unique? {
                bool isCopy = !(T is unique);
                if (isCopy) {
                    return ForwardCopy<T>(value);
                }
                else {
                    return ForwardUnique<T>(move value);
                }
            }
            public bool IsUnique<T>(int32 value) where T : unique? {
                return T is unique;
            }
            int32 Main() {
                Owner source = new Owner(2);
                Owner moved = Forward<Owner>(move source);
                Owner savedSource = new Owner(3);
                Owner savedMoved = ForwardSaved<Owner>(move savedSource);
                Owner negatedSource = new Owner(4);
                Owner negatedMoved = ForwardNegated<Owner>(move negatedSource);
                return Forward<int32>(40) + moved.Value
                    + savedMoved.Value + ForwardSaved<int32>(1)
                    + negatedMoved.Value + ForwardNegated<int32>(1)
                    + (IsUnique<Owner>(0) ? 1 : 0)
                    + (IsUnique<int32>(0) ? 0 : 1);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(await RunAsync(NativeEmitter.Emit(compilation.Program!,
            NativeRuntimePolicy.FailFast)).ConfigureAwait(true),
            Is.EqualTo(53));

        var invalid = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                int32 value = 42;
                return value is unique ? 1 : 0;
            }
            """));
        Assert.That(invalid.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3007"), Is.True);
    }

    [Test]
    public async Task GenericUniquePropertyFlowsThroughLogicalAndReturningGuardsAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public int32 Value;
                public Owner(int32 value) { Value = value; }
            }
            public T ForwardUnique<T>(T value) where T : unique {
                return move value;
            }
            public T ForwardCopy<T>(T value) {
                return value;
            }
            public T ForwardAfterGuard<T>(T value) where T : unique? {
                if (T is unique) {
                    return ForwardUnique<T>(move value);
                }
                return ForwardCopy<T>(value);
            }
            public T ForwardAnd<T>(T value, bool allowed)
                where T : unique? {
                bool shouldMove = (T is unique) && allowed;
                bool saved = shouldMove;
                if (saved) {
                    return ForwardUnique<T>(move value);
                }
                if (!(T is unique)) {
                    return ForwardCopy<T>(value);
                } else {
                    return ForwardUnique<T>(move value);
                }
            }
            public T ForwardOr<T>(T value, bool allowed)
                where T : unique? {
                if ((T is unique) || allowed) {
                    if (T is unique) {
                        return ForwardUnique<T>(move value);
                    } else {
                        return ForwardCopy<T>(value);
                    }
                } else {
                    return ForwardCopy<T>(value);
                }
            }
            public T ForwardConditional<T>(T value, bool allowed)
                where T : unique? {
                if ((T is unique) ? allowed : false) {
                    return ForwardUnique<T>(move value);
                } else {
                    if (T is unique) {
                        return ForwardUnique<T>(move value);
                    } else {
                        return ForwardCopy<T>(value);
                    }
                }
            }
            public T ForwardPairUnique<T, U>(T value, U other)
                where T : unique
                where U : unique {
                return move value;
            }
            public T ForwardBoth<T, U>(T value, U other)
                where T : unique?
                where U : unique? {
                if ((T is unique) && (U is unique)) {
                    return ForwardPairUnique<T, U>(
                        move value, move other);
                }
                if (T is unique) {
                    return ForwardUnique<T>(move value);
                } else {
                    return ForwardCopy<T>(value);
                }
            }
            int32 Main() {
                Owner afterOwnerSource = new Owner(2);
                Owner afterOwner = ForwardAfterGuard<Owner>(
                    move afterOwnerSource);
                int32 afterCopy = ForwardAfterGuard<int32>(10);

                Owner andOwnerSource = new Owner(3);
                Owner andOwner = ForwardAnd<Owner>(
                    move andOwnerSource, true);
                Owner andFallbackSource = new Owner(4);
                Owner andFallback = ForwardAnd<Owner>(
                    move andFallbackSource, false);

                Owner orOwnerSource = new Owner(5);
                Owner orOwner = ForwardOr<Owner>(
                    move orOwnerSource, false);
                int32 orFalse = ForwardOr<int32>(12, false);
                int32 orTrue = ForwardOr<int32>(13, true);

                Owner conditionalSource = new Owner(8);
                Owner conditional = ForwardConditional<Owner>(
                    move conditionalSource, true);
                int32 conditionalCopy
                    = ForwardConditional<int32>(14, false);

                Owner bothSource = new Owner(6);
                Owner otherSource = new Owner(7);
                Owner both = ForwardBoth<Owner, Owner>(
                    move bothSource, move otherSource);
                return afterOwner.Value + afterCopy + andOwner.Value
                    + andFallback.Value + orOwner.Value + orFalse
                    + orTrue + conditional.Value + conditionalCopy
                    + both.Value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(await RunAsync(NativeEmitter.Emit(compilation.Program!,
            NativeRuntimePolicy.FailFast)).ConfigureAwait(true),
            Is.EqualTo(77));

        var invalid = Compilation.Create(SyntaxTree.Parse("""
            public T ForwardUnique<T>(T value) where T : unique {
                return move value;
            }
            public T ForwardCopy<T>(T value) {
                return value;
            }
            public T InvalidAnd<T>(T value, bool allowed)
                where T : unique? {
                if ((T is unique) && allowed) {
                    return ForwardUnique<T>(move value);
                }
                return ForwardCopy<T>(value);
            }
            int32 Main() { return 0; }
            """));
        Assert.That(invalid.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3011"
                && diagnostic.Message.Contains("default Copy domain",
                    StringComparison.Ordinal)), Is.True);
    }

    [TestCase("T = T")]
    [TestCase("T = U, U = int32")]
    public void RejectsCyclicOrForwardGenericTypeDefaults(string formals)
    {
        var source = "public int32 Use<" + formals
            + ">(int32 value) { return value; }"
            + "int32 Main() { return Use(1); }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(
            compilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3007"), Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public async Task ExplicitGenericInferenceHoleDoesNotSelectDefaultAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            public T Echo<T = int32>(T value) { return value; }

            int32 Main() {
                uint32 value = 5;
                return Echo<_>(value) == value ? 1 : 0;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var main = program.Functions.Single(static function =>
            function.Name == "Main");
        BoundGenericMethodInstantiation? call = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall: BoundGenericMethodInstantiation instantiation,
                    })
                {
                    call = instantiation;
                }
            }), Is.True);
        Assert.That(call, Is.Not.Null);
        Assert.That(call!.TypeArguments.Single(),
            Is.EqualTo(PrimitiveType.UInt32));
        Assert.That(await RunAsync(NativeEmitter.Emit(program,
            NativeRuntimePolicy.FailFast)).ConfigureAwait(true),
            Is.EqualTo(1));
    }

    [Test]
    public async Task ExplicitDefaultSelectsGenericTypeDefaultAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            public T Echo<T = int32>(T value) { return value; }
            int32 Main() { return Echo<default>(9); }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var main = program.Functions.Single(static function =>
            function.Name == "Main");
        BoundGenericMethodInstantiation? call = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall: BoundGenericMethodInstantiation instantiation,
                    })
                {
                    call = instantiation;
                }
            }), Is.True);
        Assert.That(call, Is.Not.Null);
        Assert.That(call!.TypeArguments.Single(),
            Is.EqualTo(PrimitiveType.Int32));
        Assert.That(await RunAsync(NativeEmitter.Emit(program,
            NativeRuntimePolicy.FailFast)).ConfigureAwait(true),
            Is.EqualTo(9));
    }

    [Test]
    public void ExplicitGenericInferenceHoleCannotFallBackToDefault()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            public int32 Unused<T = int32>(int32 value) {
                return value;
            }
            int32 Main() { return Unused<_>(1); }
            """));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(
            compilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3011"), Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void PublicGenericReferenceResultsRetainDependencyOrdinals()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Terrum.CodeAnalysis;
            [return: DependsOn(nameof(left), nameof(right))]
            public ref<T> Select<T>(ref<T> left, ref<T> right,
                bool useLeft) {
                return useLeft ? left : right;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var definition = global::System.Linq.Enumerable.Single(
            compilation.Program!.GenericMethodDefinitions);
        Assert.That(definition.IsPublic, Is.True);
        var function = definition.ProofProgram.Functions[
            definition.FunctionIndex];
        var dependency = global::System.Linq.Enumerable.Single(
            function.DependencyContracts);
        Assert.That(dependency.Relation,
            Is.EqualTo(BoundDependencyRelation.DependsOn));
        Assert.That(dependency.Destination,
            Is.EqualTo(BoundDependencyDestination.Result));
        Assert.That(dependency.Sources.Select(
                static source => source.FormalOrdinal),
            Is.EqualTo(s_expectedGenericSourceOrdinals));
    }

    [Test]
    public void TypedArgumentsInferTheSameClosedProtocolSpecialization()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(s_sample.Replace(
            "EvaluateQuadratic<Meters>(", "EvaluateQuadratic(", StringComparison.Ordinal)));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(global::System.Linq.Enumerable.Any(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual3 = compilation.Program;
            Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual3!;
        }))().Functions,function => function.Name == "EvaluateQuadratic<Meters>"),Is.True);
    }

    [Test]
    public void AmbiguousImportedTypeArgumentsAreRejected()
    {
        var prefix = s_sample[..s_sample.IndexOf("int32 Main()", StringComparison.Ordinal)];
        var compilation = Compilation.Create([
            SyntaxTree.Parse("namespace One; struct Value { public int32 Number; }"),
            SyntaxTree.Parse("namespace Two; struct Value { public int32 Number; }"),
            SyntaxTree.Parse("using One; using Two; " + prefix
                + "int32 Main() { EvaluateQuadratic<Value>(); return 0; }"),
        ]);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,
            diagnostic => diagnostic.Code == "TRM3011"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public async Task QualifiedProtocolIdentitiesAndLongQualifiedTypeArgumentExecuteAsync()
    {
        const string Namespace = "A.B.C.D.E.F.G.H.I.J.K.L.M.N.O";
        var source = "namespace " + Namespace + ";\n"
            + s_sample.Replace(": Addition<Meters", ": " + Namespace + ".Addition<Meters",
                StringComparison.Ordinal)
                .Replace(": Addition<Seconds", ": " + Namespace + ".Addition<Seconds",
                    StringComparison.Ordinal)
                .Replace("where T : Addition<T", "where T : " + Namespace + ".Addition<T",
                    StringComparison.Ordinal)
                .Replace("EvaluateQuadratic<Meters>(", "EvaluateQuadratic<" + Namespace + ".Meters>(",
                    StringComparison.Ordinal)
                .Replace("EvaluateQuadratic<Seconds>(", "EvaluateQuadratic<" + Namespace + ".Seconds>(",
                    StringComparison.Ordinal);
        var tree = SyntaxTree.Parse(source);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
        var callStart = source.IndexOf("EvaluateQuadratic<" + Namespace + ".Meters>(", StringComparison.Ordinal)
            + "EvaluateQuadratic".Length;
        var callEnd = source.IndexOf('>', callStart, StringComparison.Ordinal) + 1;
        Assert.That(tree.Tokens.Count(token =>
            token.Span.Start >= callStart && token.Span.End <= callEnd), Is.EqualTo(33));
        var compilation = Compilation.Create(tree);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(await RunAsync(NativeEmitter.Emit(
            ((global::System.Func<BoundProgram>)(() => {
                var __nunitActual4 = compilation.Program;
                Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>());
                return (BoundProgram)__nunitActual4!;
            }))(), NativeRuntimePolicy.FailFast)).ConfigureAwait(true), Is.EqualTo(46));
    }

    [Test]
    public void RetiredNoThrowOperatorUseIsDiagnosed()
    {
        var source = s_sample.Replace("public static Meters operator +(Meters left, Meters right)",
            "[NoThrow] public static Meters operator +(Meters left, Meters right)", StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3017"),Is.True);
    }
    [TestCase(32)]
    [TestCase(33)]
    public void ClosedSpecializationsHaveAnExplicitBound(int instantiations)
    {
        var extra = new System.Text.StringBuilder();
        for (var i = 0; i < instantiations; i++)
        {
            var name = "Measure" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _ = extra.Append("struct ").Append(name).Append(" : Addition<").Append(name).Append(", ")
                .Append(name).Append(", ").Append(name).Append("> { public int32 Value; public ")
                .Append(name).Append("(int32 value) { Value = value; } public static ").Append(name)
                .Append(" operator +(").Append(name).Append(" left, ").Append(name)
                .Append(" right) { return new ").Append(name).Append("(left.Value + right.Value); } }");
        }
        _ = extra.Append("T Double<T>(T x) where T : Addition<T, T, T> { return x + x; } int32 Main() {");
        for (var i = 0; i < instantiations; i++)
        {
            var name = "Measure" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _ = extra.Append("Double<").Append(name).Append(">(new ").Append(name).Append("(1));");
        }
        _ = extra.Append("return 0; }");
        var source = s_sample[..s_sample.IndexOf("struct Meters", StringComparison.Ordinal)] + extra;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        if (instantiations == 32)
        {
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
        else
        {
            Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3016"),Is.True);
        }
    }
    [TestCase("Example<int32>(2;")]
    [TestCase("Example<int32(2);")]
    [TestCase("Example<int32> (2;")]
    public void SyntaxRecoversAndPreservesMalformedStaticProtocolSource(string invocation)
    {
        var source = "interface Example<T, U, V> { static V operator +(T a, U b); } "
            + "int32 Main() { return " + invocation + " }";
        var tree = SyntaxTree.Parse(source);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
        Assert.That(tree.HasErrors || Compilation.Create(tree).HasErrors, Is.True);
    }

    [Test]
    public void RepeatedUnclosedTypeArgumentsStayWithinParserNestingAndRetainSource()
    {
        var source = "int32 Main() { return " + string.Concat(Enumerable.Repeat("a<", 512)) + "a; }";
        var tree = SyntaxTree.Parse(source);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
        Assert.That(tree.HasErrors || Compilation.Create(tree).HasErrors, Is.True);
    }

    [Test]
    public void ImpossibleGenericPunctuationDoesNotBecomeAnInvocation()
    {
        const string Source = "int32 Main() { return Call<,Meters>(value); }";
        var tree = SyntaxTree.Parse(Source);
        Assert.That(tree.HasErrors, Is.True);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(Source));
        Assert.That(global::System.Linq.Enumerable.Any(tree.Root.DescendantNodes(),node => node.Kind == SyntaxKind.GenericNameExpression),Is.False);
    }

    [Test]
    public void RejectedTemplateReportsItsOwningSourceSnapshot()
    {
        var source = s_sample.Replace("=> (a * x + b) * x + c;", "=> a / x;",
            StringComparison.Ordinal);
        var tree = SyntaxTree.Parse(source, "quadratic.terrum");
        var compilation = Compilation.Create(tree);
        var diagnostic = global::System.Linq.Enumerable.Single(compilation.SourceDiagnostics, item => item.Diagnostic.Code == "TRM3007"
                && item.Diagnostic.Message.StartsWith("Generic function requires", StringComparison.Ordinal));
        Assert.That(diagnostic.SyntaxTree, Is.SameAs(tree));
        Assert.That(source.AsSpan(diagnostic.Diagnostic.Span.Start,
            "T EvaluateQuadratic".Length).ToString(), Is.EqualTo("T EvaluateQuadratic"));
    }

    internal static async Task<int> RunAsync(byte[] image)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-protocol-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Native process did not start.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }
                throw;
            }
            return process.ExitCode;
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }
}
