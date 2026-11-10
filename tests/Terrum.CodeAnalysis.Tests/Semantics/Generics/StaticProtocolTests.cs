// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3011"),Is.True);
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
