using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Modules.TrRm;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class ReferenceInitializationCapabilityTests
{
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void RefInitFormationPreservesVacantDestination(
        NativeRuntimePolicy policy)
    {
        const string source = """
            bool KeepVacant(ref<init int32> destination) { return false; }
            int32 Main() {
                init int32 result;
                if (KeepVacant(ref init result)) { return 1; }
                if (result is init) { return 2; }
                return 42;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The initialization-reference program did not bind.");
        Assert.That(program.Functions[0].Variables[0].IsInit, Is.True,
            $"{program.Functions[0].Name}: "
                + $"{program.Functions[0].Variables[0].Type}");
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void RefInitTracksDestinationStateAndWrites(
        NativeRuntimePolicy policy)
    {
        const string source = """
            bool Initialize(ref<init int32> destination) {
                if (*destination is init) { return false; }
                *destination = 42;
                return *destination is init;
            }
            int32 Main() {
                init int32 result;
                return Initialize(ref init result) ? result : 1;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The initialization-reference program did not bind.");
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void LocalRefInitCapabilityTargetsExactPlace(
        NativeRuntimePolicy policy)
    {
        const string source = """
            int32 Main() {
                init int32 result;
                ref<init int32> destination = ref init result;
                if (!(destination is init)) { return 1; }
                if (*destination is init) { return 2; }
                *destination = 42;
                return *destination is init ? *destination : 3;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The local initialization-reference program did not bind.");
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void RefInitCanTargetVacantInlineField(
        NativeRuntimePolicy policy)
    {
        const string source = """
            struct Cell {
                public mut int32 First;
                public mut int32 Second;
            }
            int32 Main() {
                init Cell cell;
                ref<init int32> first = ref init cell.First;
                *first = 40;
                if (!(*first is init)) { return 1; }
                ref<init int32> second = ref init cell.Second;
                *second = 2;
                return cell.First + cell.Second;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The inline-field initialization-reference program did not bind.");
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void RefInitCanTargetVacantNestedField(
        NativeRuntimePolicy policy)
    {
        const string source = """
            struct Inner {
                public mut int32 Value;
            }
            struct Outer {
                public mut Inner Nested;
            }
            int32 Main() {
                init Outer outer;
                ref<init int32> value = ref init outer.Nested.Value;
                *value = 42;
                return outer.Nested.Value;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The nested-field initialization-reference program did not bind.");
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void WritableRefInitPermitsReplacementAfterInitialization(
        NativeRuntimePolicy policy)
    {
        const string source = """
            int32 Replace(write ref<init int32> destination) {
                *destination = 42;
                *destination = 43;
                return *destination;
            }
            int32 Main() {
                init int32 result;
                return Replace(ref init result);
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The writable initialization-reference program did not bind.");
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(43));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void ReadonlyRefInitCannotReplaceAfterInitialization(
        NativeRuntimePolicy policy)
    {
        const string source = """
            void Replace(ref<init int32> destination) {
                *destination = 42;
                *destination = 43;
            }
            int32 Main() {
                init int32 result;
                Replace(ref init result);
                return 0;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.True,
            "The readonly initialization capability cannot replace a live value.");
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void RefInitAttenuatesAfterInitialization(
        NativeRuntimePolicy policy)
    {
        const string source = """
            int32 Read(ref<int32> value) { return *value; }
            int32 Initialize(ref<init int32> destination) {
                *destination = 42;
                return Read(destination);
            }
            int32 Main() {
                init int32 result;
                return Initialize(ref init result);
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The attenuated initialization-reference program did not bind.");
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void RefInitAttenuatesIntoLocalReference(
        NativeRuntimePolicy policy)
    {
        const string source = """
            int32 Initialize(ref<init int32> destination) {
                *destination = 42;
                ref<int32> value = destination;
                return *value;
            }
            int32 Main() {
                init int32 result;
                return Initialize(ref init result);
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The local attenuated initialization-reference program did not bind.");
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task InitPostconditionPublishesCallerDestination(
        NativeRuntimePolicy policy)
    {
        const string source = """
            void Create(ref<init int32> destination, int32 value)
                ensure *destination is init
            {
                *destination = value;
            }
            int32 CallCreate(int32 value) {
                init int32 result;
                Create(ref init result, value);
                return result;
            }
            int32 Main() {
                return CallCreate(42);
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                CoreContractFailureIdentitySelection = PostconditionSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The initialization postcondition program did not bind.");
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
        if (policy == NativeRuntimePolicy.FailFast)
        {
            const string moduleIdentity = "init-postcondition-standard";
            const string schemaIdentity
                = "trrm/1-draft-init-reference-bodies-local-field-layout-1";
            const string baseSchemaIdentity
                = "trrm/1-draft-init-reference-bodies-1";
            var baseArtifact = TrRmExecutableCompiler.EmitStandard(program,
                moduleIdentity, baseSchemaIdentity, 1);
            var baseInput = new TrRmStandardArtifactInput(
                baseSchemaIdentity, baseArtifact);
            var baseLoaded = TrRmExecutableCompiler.LoadStandard(baseInput,
                moduleIdentity, []);
            var baseReemitted = TrRmExecutableCompiler.EmitStandard(
                baseLoaded, moduleIdentity, baseSchemaIdentity, 1);
            _ = TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(baseSchemaIdentity,
                    baseReemitted), moduleIdentity, []);

            var artifact = TrRmExecutableCompiler.EmitStandard(program,
                moduleIdentity, schemaIdentity, 1);
            var input = new TrRmStandardArtifactInput(schemaIdentity,
                artifact);
            Assert.That(NativeModuleCompiler.ExecuteStandard(input,
                moduleIdentity, [], NativeRuntimePolicy.FailFast),
                Is.EqualTo(42));
            var loaded = TrRmExecutableCompiler.LoadStandard(input,
                moduleIdentity, []);
            var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1);
            var reemittedInput = new TrRmStandardArtifactInput(
                schemaIdentity, reemitted);
            Assert.That(NativeModuleCompiler.ExecuteStandard(reemittedInput,
                moduleIdentity, [], NativeRuntimePolicy.FailFast),
                Is.EqualTo(42));
            var image = NativeModuleCompiler.EmitStandardExecutable(
                reemittedInput,
                moduleIdentity, [], NativeRuntimePolicy.FailFast);
            await TrRmForeignInvocationAddressEmitterTests
                .AssertNativeImageExitCodeAsync(image, moduleIdentity, 42)
                .ConfigureAwait(false);
        }
    }

    [Test]
    public void GenericInitReferenceProofPreservesParameterModeAndPostcondition()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            bool TrySelectCopyInit<T>(int32 index, T first, T second,
                ref<init T> result)
                ensure return ? *result is init : true
            {
                if (index == 0) {
                    *result = first;
                    return true;
                }
                if (index == 1) {
                    *result = second;
                    return true;
                }
                return false;
            }

            bool TrySelectCopy<T>(int32 index, T first, T second, out T result) {
                init T selected;
                if (!TrySelectCopyInit(index, first, second, ref init selected)) {
                    return false;
                }
                result = selected;
                return true;
            }
            """), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            CoreContractFailureIdentitySelection = PostconditionSelection(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var definition = compilation.Program!.GenericMethodDefinitions.Single(
            static method => method.Name == "TrySelectCopyInit");
        var proofFunction = definition.ProofProgram.Functions[
            definition.FunctionIndex];
        Assert.That(proofFunction.Variables[3].IsOut, Is.False);
        Assert.That(proofFunction.Variables[3].IsInit, Is.True);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitReferenceReborrowForwardsTheReservedDestination(
        NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            void Initialize(ref<init int32> destination) {
                *destination = 42;
            }

            void Forward(ref<init int32> destination) {
                Initialize(ref init *destination);
            }

            int32 Main() {
                init int32 value;
                Forward(ref init value);
                if (!(value is init)) { return 1; }
                return value;
            }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The initialization-reference reborrow program did not bind.");
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void GenericInitReferenceCallPublishesBooleanDefault(
        NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            bool MaybeWrite<T>(bool shouldWrite, T value,
                ref<init T> destination)
            {
                if (shouldWrite) {
                    *destination = value;
                    return true;
                }
                return false;
            }

            bool TryWrite<T>(bool shouldWrite, T value, out T result)
            {
                init T selected;
                if (!MaybeWrite(shouldWrite, value, ref init selected)) {
                    return false;
                }
                if (!(selected is init)) {
                    return false;
                }
                result = selected;
                return true;
            }

            int32 Main()
            {
                if (TryWrite(true, int32(42), out var written)) {
                    if (!(written is init)) { return 1; }
                    if (written != 42) { return 2; }
                } else {
                    return 3;
                }
                if (TryWrite(false, int32(13), out var unwritten)) {
                    return 4;
                }
                if (unwritten is init) {
                    return 5;
                }
                return 0;
            }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The generic initialization-reference output program did not bind.");
        Assert.That(NativeJit.Execute(program, policy), Is.Zero);
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitReferenceOutputUsesStackArgument(
        NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            bool Sum(int32 first, int32 second, int32 third, int32 fourth,
                ref<init int32> result)
            {
                *result = first + second + third + fourth;
                return true;
            }

            int32 Main() {
                init int32 result;
                if (!Sum(10, 11, 12, 9, ref init result)) {
                    return 1;
                }
                return result;
            }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The stack-argument initialization-output program did not bind.");
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void SourceContextAndInitReferenceStackArgumentCompose(
        NativeRuntimePolicy policy)
    {
        var source = SyntaxTree.Parse("""
            bool ReadSource(int32 start, int32 length, uint32 radix,
                uint64 maximum, ref<init uint64> result)
            {
                if (start < 0 || length <= 0
                    || start >= Host.Source.Length
                    || length > Host.Source.Length - start
                    || radix < 2 || maximum < uint64(Host.Source[start]))
                {
                    return false;
                }
                *result = uint64(Host.Source[start]);
                return true;
            }

            int32 Main() {
                init uint64 result;
                if (!ReadSource(0, 1, 10, 100, ref init result)) {
                    return 1;
                }
                return int32(result);
            }
            """);
        var options = new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
        };
        var compilation = Compilation.Create([source],
            [new HostBuffer("Source", PrimitiveType.Char16, [], false) {
                IsInvocationScoped = true,
            }], options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The source-dependent init-output program did not bind.");
        var grant = new HostBuffer("Source", PrimitiveType.Char16, [42],
            false) {
            IsInvocationScoped = true,
        };
        Assert.That(NativeJit.ExecuteWithSourceGrant(program, [grant], policy),
            Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitReferencePublicationSurvivesPrivateModuleRoundTrip(
        NativeRuntimePolicy policy)
    {
        var producerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public static bool Initialize(ref<init int32> destination) {
                    *destination = 42;
                    return true;
                }
                """), new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });
        Assert.That(producerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                producerCompilation.Diagnostics));
        var producerProgram = producerCompilation.Program
            ?? throw new InvalidOperationException(
                "The init-reference module producer did not bind.");
        var producer = ModuleCompiler.CreateProducer(producerProgram,
            ["Initialize"]);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                init int32 result;
                if (!Initialize(ref init result)) { return 1; }
                return result;
            }
            """), ModuleCompiler.CreateReference(producer),
            (BoundRuntimePolicy)policy);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new InvalidOperationException(
                "The init-reference module consumer did not bind.");
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram,
            producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(NativeJit.Execute(linked, policy), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void PrivateInitReferenceFalseOutcomeLeavesCallerVacant(
        NativeRuntimePolicy policy)
    {
        var producerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public static bool LeaveVacant(ref<init int32> destination) {
                    return false;
                }
                """), new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });
        Assert.That(producerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                producerCompilation.Diagnostics));
        var producerProgram = producerCompilation.Program
            ?? throw new InvalidOperationException(
                "The init-reference module producer did not bind.");
        var producer = ModuleCompiler.CreateProducer(producerProgram,
            ["LeaveVacant"]);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                init int32 result;
                if (LeaveVacant(ref init result)) { return 1; }
                if (result is init) { return 2; }
                return 0;
            }
            """), ModuleCompiler.CreateReference(producer),
            (BoundRuntimePolicy)policy);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new InvalidOperationException(
                "The init-reference module consumer did not bind.");
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram,
            producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(NativeJit.Execute(linked, policy), Is.Zero);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void PrivateInitReferenceVoidStorePublishesCallerValue(
        NativeRuntimePolicy policy)
    {
        var producerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public static void Initialize(ref<init int32> destination) {
                    *destination = 42;
                }
                """), new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });
        Assert.That(producerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                producerCompilation.Diagnostics));
        var producerProgram = producerCompilation.Program
            ?? throw new InvalidOperationException(
                "The init-reference module producer did not bind.");
        var producer = ModuleCompiler.CreateProducer(producerProgram,
            ["Initialize"]);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                init int32 result;
                Initialize(ref init result);
                return result;
            }
            """), ModuleCompiler.CreateReference(producer),
            (BoundRuntimePolicy)policy);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new InvalidOperationException(
                "The init-reference module consumer did not bind.");
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram,
            producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(NativeJit.Execute(linked, policy), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void PrivateBooleanInitOutputLoadsCanonicalValue(
        NativeRuntimePolicy policy)
    {
        var producerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public static bool Fill(ref<init bool> destination) {
                    *destination = false;
                    return true;
                }
                """), new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });
        Assert.That(producerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                producerCompilation.Diagnostics));
        var producerProgram = producerCompilation.Program
            ?? throw new InvalidOperationException(
                "The Boolean init-reference module producer did not bind.");
        var producer = ModuleCompiler.CreateProducer(producerProgram,
            ["Fill"]);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                init bool result;
                if (!Fill(ref init result)) { return 1; }
                return result ? 42 : 0;
            }
            """), ModuleCompiler.CreateReference(producer),
            (BoundRuntimePolicy)policy);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new InvalidOperationException(
                "The Boolean init-reference module consumer did not bind.");
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram,
            producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(NativeJit.Execute(linked, policy), Is.Zero);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void PrivateInitReferenceReadLeavesCallerVacant(
        NativeRuntimePolicy policy)
    {
        var producerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public static bool CheckVacant(ref<init int32> destination) {
                    if (destination is init) { return false; }
                    if (*destination is init) { return false; }
                    return false;
                }
                """), new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });
        Assert.That(producerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                producerCompilation.Diagnostics));
        var producerProgram = producerCompilation.Program
            ?? throw new InvalidOperationException(
                "The init-reference module producer did not bind.");
        var producer = ModuleCompiler.CreateProducer(producerProgram,
            ["CheckVacant"]);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                init int32 result;
                if (CheckVacant(ref init result)) { return 1; }
                if (result is init) { return 2; }
                return 0;
            }
            """), ModuleCompiler.CreateReference(producer),
            (BoundRuntimePolicy)policy);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new InvalidOperationException(
                "The init-reference module consumer did not bind.");
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram,
            producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(NativeJit.Execute(linked, policy), Is.Zero);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void PrivateInitReferenceReborrowForwardsCallerDestination(
        NativeRuntimePolicy policy)
    {
        var producerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public static bool Fill(ref<init int32> destination) {
                    *destination = 42;
                    return true;
                }
                public static bool Forward(ref<init int32> destination) {
                    return Fill(ref init *destination);
                }
                """), new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });
        Assert.That(producerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                producerCompilation.Diagnostics));
        var producerProgram = producerCompilation.Program
            ?? throw new InvalidOperationException(
                "The init-reference module producer did not bind.");
        var producer = ModuleCompiler.CreateProducer(producerProgram,
            ["Forward"]);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                init int32 result;
                if (!Forward(ref init result)) { return 1; }
                return result;
            }
            """), ModuleCompiler.CreateReference(producer),
            (BoundRuntimePolicy)policy);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new InvalidOperationException(
                "The init-reference module consumer did not bind.");
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram,
            producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(NativeJit.Execute(linked, policy), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void PrivateWritableReferenceStorePublishesCallerValue(
        NativeRuntimePolicy policy)
    {
        var producerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public static void Set(write ref<int32> destination) {
                    *destination = 42;
                }
                """), new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });
        Assert.That(producerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                producerCompilation.Diagnostics));
        var producerProgram = producerCompilation.Program
            ?? throw new InvalidOperationException(
                "The checked-reference module producer did not bind.");
        var producer = ModuleCompiler.CreateProducer(producerProgram,
            ["Set"]);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut int32 result = 0;
                Set(ref result);
                return result;
            }
            """), ModuleCompiler.CreateReference(producer),
            (BoundRuntimePolicy)policy);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new InvalidOperationException(
                "The checked-reference module consumer did not bind.");
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram,
            producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(NativeJit.Execute(linked, policy), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void PrivateReadonlyReferenceReadSeesCallerValue(
        NativeRuntimePolicy policy)
    {
        var producerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public static int32 Read(ref<int32> value) {
                    return *value;
                }
                """), new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });
        Assert.That(producerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                producerCompilation.Diagnostics));
        var producerProgram = producerCompilation.Program
            ?? throw new InvalidOperationException(
                "The checked-reference module producer did not bind.");
        var producer = ModuleCompiler.CreateProducer(producerProgram,
            ["Read"]);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut int32 value = 42;
                return Read(ref value);
            }
            """), ModuleCompiler.CreateReference(producer),
            (BoundRuntimePolicy)policy);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program
            ?? throw new InvalidOperationException(
                "The checked-reference module consumer did not bind.");
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram,
            producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(NativeJit.Execute(linked, policy), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void FalseInitOutputReleasesReservationForRetry(
        NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            bool LeaveVacant(ref<init int32> destination) {
                return false;
            }
            bool Fill(ref<init int32> destination) {
                *destination = 42;
                return true;
            }

            int32 Main() {
                init int32 result;
                if (LeaveVacant(ref init result)) { return 1; }
                require Fill(ref init result);
                return result;
            }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The init-reference retry program did not bind.");
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitPostconditionCorrelatesSavedBooleanResults(
        NativeRuntimePolicy policy)
    {
        const string source = """
            bool TryCreate(bool create, ref<init int32> destination)
                ensure return ? *destination is init : true
            {
                if (create) {
                    *destination = 42;
                    return true;
                }
                return false;
            }
            struct Inner {
                public mut int32 Value;
            }
            struct Outer {
                public mut Inner Nested;
            }
            int32 Main() {
                init int32 direct;
                if (TryCreate(true, ref init direct)) { return direct; }
                init int32 saved;
                bool created = TryCreate(true, ref init saved);
                bool copied = created;
                if (copied) { return saved; }
                init Outer outer;
                bool nested = TryCreate(true,
                    ref init outer.Nested.Value);
                if (nested) { return outer.Nested.Value; }
                return 0;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                CoreContractFailureIdentitySelection = PostconditionSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitPostconditionCorrelatesMultipleDestinations(
        NativeRuntimePolicy policy)
    {
        const string source = """
            bool TryCreatePair(ref<init int32> first,
                ref<init int32> second)
                ensure return
                    ? (*first is init && *second is init)
                    : true
            {
                *first = 1;
                *second = 2;
                return true;
            }
            int32 Main() {
                init int32 first;
                init int32 second;
                bool created = TryCreatePair(ref init first,
                    ref init second);
                if (created) { return first + second; }
                return 0;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                CoreContractFailureIdentitySelection = PostconditionSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void RejectsOverlappingInitializationReservations(
        NativeRuntimePolicy policy)
    {
        const string source = """
            struct Outer {
                public mut int32 Value;
                public Outer(int32 value) { Value = value; }
            }
            bool InitializeEither(bool initializeWhole,
                ref<init Outer> whole, ref<init int32> field)
                ensure return
                    ? (*whole is init || *field is init)
                    : true
            {
                if (initializeWhole) {
                    *whole = new Outer(1);
                }
                else {
                    *field = 42;
                }
                return true;
            }
            int32 ReadInitializedField(bool initializeWhole) {
                init Outer outer;
                bool initialized = InitializeEither(initializeWhole,
                    ref init outer, ref init outer.Value);
                if (initialized) { return outer.Value; }
                return 0;
            }
            """;

        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                CoreContractFailureIdentitySelection = PostconditionSelection(),
            });

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void RejectsAliasedInitializationReservationsInOneCall()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            bool InitializePair(ref<init int32> first,
                ref<init int32> second)
            {
                *first = 1;
                *second = 2;
                return true;
            }

            int32 Main() {
                init int32 result;
                bool initialized = InitializePair(
                    ref init result, ref init result);
                if (initialized) { return result; }
                return 0;
            }
            """), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
        });

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void RejectsReborrowReuseByALaterCallArgument()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            void Complete(ref<init int32> destination, int32 value) {
                *destination = value;
            }

            int32 InitializeAndReturn(ref<init int32> destination) {
                *destination = 1;
                return 0;
            }

            void Forward(ref<init int32> destination) {
                Complete(ref init *destination,
                    InitializeAndReturn(ref init *destination));
            }
            """));

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void RejectsObservingAnInitializationReferentDuringReborrow()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            void Complete(ref<init int32> destination,
                bool referentInitialized) {
                *destination = 42;
            }

            void Forward(ref<init int32> destination) {
                Complete(ref init *destination, *destination is init);
            }
            """));

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitPostconditionDoesNotPublishOnFalseSavedResult(
        NativeRuntimePolicy policy)
    {
        const string source = """
            bool TryCreate(bool create, ref<init int32> destination)
                ensure return ? *destination is init : true
            {
                if (create) {
                    *destination = 42;
                    return true;
                }
                return false;
            }
            int32 Main() {
                init int32 saved;
                bool created = TryCreate(false, ref init saved);
                if (!created) { return saved; }
                return 0;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                CoreContractFailureIdentitySelection = PostconditionSelection(),
            });

        Assert.That(compilation.HasErrors, Is.True,
            "The false return arm does not guarantee initialization.");
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitPostconditionInvalidatesAfterStatusReassignment(
        NativeRuntimePolicy policy)
    {
        const string source = """
            bool TryCreate(ref<init int32> destination)
            {
                *destination = 42;
                return true;
            }
            void Reset(write ref<bool> status) {
                *status = false;
            }
            int32 ReadAfterStatusWrite() {
                init int32 saved;
                bool created = TryCreate(ref init saved);
                Reset(write ref created);
                if (created) { return saved; }
                return 0;
            }
            int32 Main() { return 0; }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                CoreContractFailureIdentitySelection = PostconditionSelection(),
            });

        Assert.That(compilation.HasErrors, Is.True,
            "Reassigning the discriminator invalidates its old output correlation.");
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitPostconditionInvalidatesAfterDestinationLifetimeEnd(
        NativeRuntimePolicy policy)
    {
        const string source = """
            bool TryCreate(ref<init int32> destination)
            {
                *destination = 42;
                return true;
            }
            int32 ReadAfterDestinationEnd() {
                init int32 saved;
                bool created = TryCreate(ref init saved);
                if (created) {
                    saved = _;
                    if (created) { return saved; }
                }
                return 0;
            }
            int32 Main() { return 0; }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                CoreContractFailureIdentitySelection = PostconditionSelection(),
            });

        Assert.That(compilation.HasErrors, Is.True,
            "Ending the destination invalidates its saved call-result correlation.");
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitPostconditionInvalidatesAfterDestinationAliasWrite(
        NativeRuntimePolicy policy)
    {
        const string source = """
            bool TryCreate(ref<init int32> destination)
            {
                *destination = 42;
                return true;
            }
            void End(write ref<int32> destination) {
                *destination = _;
            }
            int32 ReadAfterDestinationWrite() {
                init int32 saved;
                bool created = TryCreate(ref init saved);
                if (created) {
                    End(write ref saved);
                    if (created) { return saved; }
                }
                return 0;
            }
            int32 Main() { return 0; }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                CoreContractFailureIdentitySelection = PostconditionSelection(),
            });

        Assert.That(compilation.HasErrors, Is.True,
            "A writable alias that ends the destination invalidates the saved correlation.");
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitPostconditionRemainsWithOriginalPlaceAfterAliasRebind(
        NativeRuntimePolicy policy)
    {
        const string source = """
            bool TryCreate(ref<init int32> destination) {
                *destination = 42;
                return true;
            }
            int32 Main() {
                init int32 first;
                init int32 second;
                mut ref<init int32> selected = ref init first;
                bool created = TryCreate(ref init *selected);
                selected = ref init second;
                if (created) { return first; }
                return 0;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitPostconditionDoesNotFollowReboundAlias(
        NativeRuntimePolicy policy)
    {
        const string source = """
            bool TryCreate(ref<init int32> destination)
                ensure return ? *destination is init : true
            {
                *destination = 42;
                return true;
            }
            int32 Main() {
                init int32 first;
                init int32 second;
                mut ref<init int32> selected = ref init first;
                bool created = TryCreate(ref init *selected);
                selected = ref init second;
                if (created) { return *selected; }
                return 0;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                CoreContractFailureIdentitySelection = PostconditionSelection(),
            });

        Assert.That(compilation.HasErrors, Is.True,
            "The saved result must not publish the newly rebound destination.");
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitPostconditionDoesNotOverpublishAmbiguousAliasTargets(
        NativeRuntimePolicy policy)
    {
        const string source = """
            bool TryCreate(ref<init int32> destination) {
                *destination = 42;
                return true;
            }
            int32 ReadSelectedDestination(bool selectSecond) {
                init int32 first;
                init int32 second;
                mut ref<init int32> selected = ref init first;
                if (selectSecond) { selected = ref init second; }
                bool created = TryCreate(ref init *selected);
                if (created) { return first; }
                return 0;
            }
            int32 Main() { return ReadSelectedDestination(false); }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.True,
            "A successful call through an ambiguous alias does not identify which candidate was initialized.");
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitPostconditionPublishesThroughUnchangedAmbiguousAlias(
        NativeRuntimePolicy policy)
    {
        const string source = """
            bool TryCreate(ref<init int32> destination) {
                *destination = 42;
                return true;
            }
            int32 ReadSelectedDestination(bool selectSecond) {
                init int32 first;
                init int32 second;
                mut ref<init int32> selected = ref init first;
                if (selectSecond) { selected = ref init second; }
                bool created = TryCreate(ref init *selected);
                if (created) { return *selected; }
                return 0;
            }
            int32 Main() { return ReadSelectedDestination(true); }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitPostconditionDoesNotReadThroughVacantAmbiguousAlias(
        NativeRuntimePolicy policy)
    {
        const string source = """
            bool TryCreate(bool publish, ref<init int32> destination) {
                if (!publish) { return false; }
                *destination = 42;
                return true;
            }
            int32 ReadSelectedDestination(bool selectSecond) {
                init int32 first;
                init int32 second;
                mut ref<init int32> selected = ref init first;
                if (selectSecond) { selected = ref init second; }
                bool created = TryCreate(false, ref init *selected);
                if (!created) { return *selected; }
                return 0;
            }
            int32 Main() { return ReadSelectedDestination(true); }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.True,
            "The false result publishes the selected destination as vacant.");
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitPostconditionCorrelatesUnchangedAliasAcrossNestedFields(
        NativeRuntimePolicy policy)
    {
        const string source = """
            struct Inner {
                public mut int32 Value;
            }
            struct Pair {
                public mut Inner Left;
                public mut Inner Right;
            }
            bool TryCreate(ref<init int32> destination) {
                *destination = 42;
                return true;
            }
            int32 ReadSelectedField(bool selectSecond) {
                init Pair first;
                init Pair second;
                mut ref<init int32> selected = ref init first.Left.Value;
                if (selectSecond) {
                    selected = ref init second.Right.Value;
                }
                bool created = TryCreate(ref init *selected);
                if (created) { return *selected; }
                return 0;
            }
            int32 Main() { return ReadSelectedField(true); }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitPostconditionDoesNotOverpublishNestedFieldAliasTargets(
        NativeRuntimePolicy policy)
    {
        const string source = """
            struct Inner {
                public mut int32 Value;
            }
            struct Pair {
                public mut Inner Left;
                public mut Inner Right;
            }
            bool TryCreate(ref<init int32> destination) {
                *destination = 42;
                return true;
            }
            int32 ReadSelectedField(bool selectSecond) {
                init Pair first;
                init Pair second;
                mut ref<init int32> selected = ref init first.Left.Value;
                if (selectSecond) {
                    selected = ref init second.Right.Value;
                }
                bool created = TryCreate(ref init *selected);
                if (created) { return first.Left.Value; }
                return 0;
            }
            int32 Main() { return ReadSelectedField(true); }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.True,
            "The call result does not identify which nested-field candidate was initialized.");
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitPostconditionDoesNotSurviveAliasRebindingToSameTargetSet(
        NativeRuntimePolicy policy)
    {
        const string source = """
            bool TryCreate(ref<init int32> destination) {
                *destination = 42;
                return true;
            }
            int32 ReadSelectedDestination(bool selectSecond) {
                init int32 first;
                init int32 second;
                mut ref<init int32> selected = ref init first;
                if (selectSecond) { selected = ref init second; }
                bool created = TryCreate(ref init *selected);
                if (selectSecond) { selected = ref init first; }
                else { selected = ref init second; }
                if (created) { return *selected; }
                return 0;
            }
            int32 Main() { return ReadSelectedDestination(false); }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.True,
            "Rebinding to the same candidate set invalidates the saved output relationship.");
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitPostconditionPublishesCompoundAndValuePredicates(
        NativeRuntimePolicy policy)
    {
        const string source = """
            void CreateWithConjunction(ref<init int32> destination)
                ensure !(!(*destination is init)) && *destination > 0
            {
                *destination = 42;
            }
            void CreateByValue(ref<init int32> destination)
                ensure *destination > 0
            {
                *destination = 24;
            }
            int32 Main() {
                init int32 first;
                CreateWithConjunction(ref init first);
                init int32 second;
                CreateByValue(ref init second);
                return first + second;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                CoreContractFailureIdentitySelection = PostconditionSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitPostconditionFollowsShortCircuitAndConditionalInputs(
        NativeRuntimePolicy policy)
    {
        const string source = """
            void MaybeCreate(bool create, ref<init int32> destination)
                ensure !create || *destination is init
            {
                if (create) { *destination = 42; }
            }
            void MaybeCreateConditional(bool create,
                ref<init int32> destination)
                ensure create ? *destination is init : true
            {
                if (create) { *destination = 24; }
            }
            int32 Main() {
                init int32 vacant;
                MaybeCreate(false, ref init vacant);
                init int32 result;
                MaybeCreate(true, ref init result);
                init int32 conditionalResult;
                MaybeCreateConditional(true, ref init conditionalResult);
                return result + conditionalResult;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                CoreContractFailureIdentitySelection = PostconditionSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitPostconditionAllowsGuardedOutputValueRead(
        NativeRuntimePolicy policy)
    {
        const string source = """
            void MaybeCreateAnd(bool create,
                ref<init int32> destination)
                ensure create && *destination > 0
            {
                if (create) { *destination = 42; }
            }
            void MaybeCreate(bool create, ref<init int32> destination)
                ensure !create || *destination > 0
            {
                if (create) { *destination = 42; }
            }
            void MaybeCreateConditional(bool create,
                ref<init int32> destination)
                ensure create ? *destination > 0 : true
            {
                if (create) { *destination = 24; }
            }
            void MaybeCreateWhenReady(bool create, bool ready,
                ref<init int32> destination)
                ensure create && ready && *destination > 0
            {
                if (create && ready) { *destination = 12; }
            }
            void MaybeCreateWhenEither(bool create, bool ready,
                ref<init int32> destination)
                ensure create && *destination > 0
            {
                if (create || ready) { *destination = 6; }
            }
            void MaybeCreateForPositiveCount(int32 count,
                ref<init int32> destination)
                ensure count > 0 && *destination > 0
            {
                if (count > 0) { *destination = 8; }
            }
            int32 Main() {
                init int32 vacant;
                MaybeCreate(false, ref init vacant);
                return 0;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                CoreContractFailureIdentitySelection = PostconditionSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitPostconditionRejectsUnguardedOutputValueRead(
        NativeRuntimePolicy policy)
    {
        const string source = """
            void MaybeCreate(bool create, ref<init int32> destination)
                ensure create || *destination > 0
            {
                if (create) { *destination = 42; }
            }
            void MaybeCreateWhenReady(bool create, bool ready,
                ref<init int32> destination)
                ensure create && *destination > 0
            {
                if (create && ready) { *destination = 42; }
            }
            void MaybeCreateForPositiveCount(int32 count,
                ref<init int32> destination)
                ensure count >= 0 && *destination > 0
            {
                if (count > 0) { *destination = 8; }
            }
            int32 Main() { return 0; }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                CoreContractFailureIdentitySelection = PostconditionSelection(),
            });

        Assert.That(compilation.HasErrors, Is.True,
            "The output value read is reached when the output may be vacant.");
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void ShortCircuitOutputChainRetainsNinthVacantDestination(
        NativeRuntimePolicy policy)
    {
        const string source = """
            bool Fill(bool publish, ref<init int32> destination) {
                if (!publish) { return false; }
                *destination = 42;
                return true;
            }
            int32 Validate(bool publish) {
                init int32 first0;
                init int32 first1;
                init int32 first2;
                init int32 first3;
                init int32 first4;
                init int32 first5;
                init int32 first6;
                init int32 first7;
                init int32 final;
                if (!Fill(publish, ref init first0)
                    || !Fill(publish, ref init first1)
                    || !Fill(publish, ref init first2)
                    || !Fill(publish, ref init first3)
                    || !Fill(publish, ref init first4)
                    || !Fill(publish, ref init first5)
                    || !Fill(publish, ref init first6)
                    || !Fill(publish, ref init first7)
                    || !Fill(false, ref init final)) {
                        if (!(final is init)) { return 42; }
                        return 1;
                }
                return 0;
            }
            int32 Main() { return Validate(true); }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!, policy),
            Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitPostconditionDisjunctionDoesNotOverpublish(
        NativeRuntimePolicy policy)
    {
        const string source = """
            void MaybeCreate(ref<init int32> destination)
                ensure *destination is init || true
            {
            }
            int32 Main() {
                init int32 result;
                MaybeCreate(ref init result);
                return result;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                CoreContractFailureIdentitySelection = PostconditionSelection(),
            });

        Assert.That(compilation.HasErrors, Is.True,
            "A disjunction that can succeed without initialization must not publish the destination.");
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitPostconditionMentionWithoutRelationKeepsBooleanDefault(
        NativeRuntimePolicy policy)
    {
        const string source = """
            bool MaybeCreate(bool create, ref<init int32> destination)
                ensure *destination is init || true
            {
                if (create) { *destination = 42; }
                return create;
            }

            int32 Main() {
                init int32 result;
                bool created = MaybeCreate(true, ref init result);
                if (created) { return result; }
                return 0;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                CoreContractFailureIdentitySelection =
                    PostconditionSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var maybeCreate = program.Functions.Single(
            static function => function.Name == "MaybeCreate");
        Assert.That(maybeCreate.HasStandardCheckedOutputStateMetadata,
            Is.True);
        Assert.That(maybeCreate.CheckedOutputSelectors
            .Select(static selector => selector.NodeKind),
            Is.EqualTo(new byte[] { 1, 2 }));
        Assert.That(maybeCreate.CheckedOutputStates,
            Is.EquivalentTo(new[] {
                new NativeFunctionPointerOutputState(1, 0, 1),
                new NativeFunctionPointerOutputState(1, 1, 0),
            }));
        Assert.That(CheckedOutputStateProjection.GetExplicitOutputFormals(
            maybeCreate, program.CheckedReferenceTypes), Is.Empty);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitPostconditionUnconditionalCompletionReplacesBooleanDefault(
        NativeRuntimePolicy policy)
    {
        const string source = """
            bool AlwaysCreate(ref<init int32> destination)
                ensure *destination is init
            {
                *destination = 42;
                return false;
            }

            int32 Main() {
                init int32 result;
                bool created = AlwaysCreate(ref init result);
                if (created) { return 0; }
                return result;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                CoreContractFailureIdentitySelection =
                    PostconditionSelection(),
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var alwaysCreate = program.Functions.Single(
            static function => function.Name == "AlwaysCreate");
        Assert.That(alwaysCreate.HasStandardCheckedOutputStateMetadata,
            Is.True);
        Assert.That(alwaysCreate.CheckedOutputSelectors, Is.Empty);
        Assert.That(alwaysCreate.CheckedOutputStates, Is.Empty);
        Assert.That(CheckedOutputStateProjection.GetExplicitOutputFormals(
            alwaysCreate, program.CheckedReferenceTypes),
            Does.Contain(0));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitPostconditionDoesNotPublishForFalseInput(
        NativeRuntimePolicy policy)
    {
        const string source = """
            void MaybeCreate(bool create, ref<init int32> destination)
                ensure !create || *destination is init
            {
                if (create) { *destination = 42; }
            }
            int32 Main() {
                init int32 result;
                MaybeCreate(false, ref init result);
                return result;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                CoreContractFailureIdentitySelection = PostconditionSelection(),
            });

        Assert.That(compilation.HasErrors, Is.True,
            "A false input arm must retain the uninitialized destination state.");
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitPostconditionRejectsReadBeforeInitialization(
        NativeRuntimePolicy policy)
    {
        const string source = """
            void ReadBeforeInitialization(ref<init int32> destination)
                ensure *destination > 0
            {
            }
            int32 Main() { return 0; }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                CoreContractFailureIdentitySelection = PostconditionSelection(),
            });

        Assert.That(compilation.HasErrors, Is.True,
            "The value predicate cannot inspect a vacant destination.");
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void RefInitStateJoinsAcrossBranches(NativeRuntimePolicy policy)
    {
        const string source = """
            bool MaybeInitialize(ref<init int32> destination, bool initialize) {
                if (initialize) { *destination = 42; }
                return *destination is init;
            }
            int32 Main() {
                init int32 vacant;
                if (MaybeInitialize(ref init vacant, false)) { return 1; }
                init int32 initialized;
                return MaybeInitialize(ref init initialized, true) ? 42 : 2;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The branch-joined initialization-reference program did not bind.");
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void WritableRefInitStateFlowsThroughLoop(NativeRuntimePolicy policy)
    {
        const string source = """
            int32 InitializeRepeatedly(write ref<init int32> destination, int32 count) {
                *destination = 0;
                mut int32 index = 0;
                while (index < count) {
                    *destination = index;
                    index += 1;
                }
                return index;
            }
            int32 Main() {
                init int32 result;
                return InitializeRepeatedly(ref init result, 4);
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The looped initialization-reference program did not bind.");
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(4));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }

    [TestCase("""
        void Keep(ref<init int32> destination) {}
        void Caller() {
            init int64 result;
            Keep(ref init result);
        }
        """)]
    [TestCase("""
        void Keep(ref<init int32> destination) {}
        void Caller() {
            int32 result = 42;
            Keep(ref init result);
        }
        """)]
    [TestCase("""
        void Keep(ref<init int32> destination) {}
        void Caller() {
            init int32 result;
            Keep(ref result);
        }
        """)]
    [TestCase("""
        int32 Read(ref<init int32> destination) {
            return *destination;
        }
        """)]
    [TestCase("""
        void Initialize(ref<init int32> destination) {
            *destination = 42;
            *destination = 43;
        }
        """)]
    [TestCase("""
        int32 Read(ref<int32> value) { return *value; }
        int32 Initialize(ref<init int32> destination) {
            return Read(destination);
        }
        """)]
    [TestCase("""
        int32 Read(ref<int32> value) { return *value; }
        int32 Initialize(ref<init int32> destination) {
            *destination = 42;
            ref<int32> first = destination;
            ref<int32> second = destination;
            return Read(second);
        }
        """)]
    public void RejectsInvalidInitializationReferenceOperations(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void RejectsLegacyParameterOnlyInitializationSyntax()
    {
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("void Keep(init ref<int32> destination) {}"));

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void InitParameterDoesNotPublishCallerStateOnFalseOutcome()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            bool MaybeCreate(ref<init int32> destination) {
                return false;
            }
            int32 Main() {
                init int32 result;
                bool created = MaybeCreate(ref init result);
                if (created) { return 0; }
                return result;
            }
            """), new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
            });

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    private static CoreContractFailureIdentitySelection PostconditionSelection()
        => new("sample.consumer",
            new CoreContractFailureDeclarationIdentity("core.contracts",
                "requirement-exception"),
            new CoreContractFailureDeclarationIdentity("core.contracts",
                "postcondition-exception"),
            new CoreContractFailureDeclarationIdentity("core.contracts",
                "require-exception-schema"));
}
