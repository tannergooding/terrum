using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmStandardInitializationReferenceBodyTests
{
    private const string SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-1";
    private const string LocalFieldLayoutSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-local-field-layout-1";
    private const string NestedFieldDestinationsSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-local-field-layout-2";
    private const string GeneralPredicateGraphsSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-1";
    private const string PointerLoadsSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-pointer-loads-1";
    private const string GeneralPredicateGraphsPointerLoadsSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-pointer-loads-1";
    private const string PointerLoadsNativeParameterSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-pointer-loads-native-parameter-contracts-1";
    private const string GeneralPredicateGraphsPointerLoadsNativeParameterSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-pointer-loads-native-parameter-contracts-1";
    private const string C1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1";
    private const string C1M1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1";
    private const string C1M1Construction2SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2";
    private const string StandardNativeWriteOrderSource = """
        [Terrum.Interop.NativeImport("kernel32.dll", "GetCurrentProcess")]
        extern unsafe ptr<void> GetCurrentProcess();

        [Terrum.Interop.NativeImport("kernel32.dll", "WriteProcessMemory")]
        [Terrum.Interop.NativeParameter(2, Write, NoEscape, 1)]
        [Terrum.Interop.NativeParameter(3, Read, NoEscape, 1)]
        extern unsafe int32 WriteProcessMemory(ptr<void> process,
            write ptr<int32> destination, ptr<int32> source, nuint size,
            ptr<void> writtenCount);

        public unsafe int32 ReadBeforeWrite(write ptr<int32> value,
            ptr<int32> replacement, ptr<void> writtenCount, nuint size) {
            unsafe {
                int32 snapshot = *value;
                int32 writeResult = WriteProcessMemory(GetCurrentProcess(),
                    value, replacement, size, writtenCount);
                return snapshot;
            }
        }

        public unsafe int32 ReadAfterWrite(write ptr<int32> value,
            ptr<int32> replacement, ptr<void> writtenCount, nuint size) {
            unsafe {
                int32 writeResult = WriteProcessMemory(GetCurrentProcess(),
                    value, replacement, size, writtenCount);
                int32 current = *value;
                return current;
            }
        }
        """;
    private const string StandardNativeWriteOrderConsumerSource = """
        [Terrum.Interop.NativeImport("kernel32.dll", "GetCurrentProcess")]
        extern unsafe ptr<void> GetCurrentProcess();

        public unsafe int32 ReadBeforeWrite(write ptr<int32> value,
            ptr<int32> replacement, ptr<void> writtenCount, nuint size) {
            unsafe {
                int32 snapshot = *value;
                int32 writeResult = WriteProcessMemory(GetCurrentProcess(),
                    value, replacement, size, writtenCount);
                return snapshot;
            }
        }

        public unsafe int32 ReadAfterWrite(write ptr<int32> value,
            ptr<int32> replacement, ptr<void> writtenCount, nuint size) {
            unsafe {
                int32 writeResult = WriteProcessMemory(GetCurrentProcess(),
                    value, replacement, size, writtenCount);
                int32 current = *value;
                return current;
            }
        }
        """;

    public enum GeneralOutputStatePredicateShape
    {
        AndThen,
        OrElse,
        BooleanNot,
        EagerAnd,
        EagerOr,
        EagerXor,
        Conditional,
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int32Function(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int32PairFunction(int first, int second);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint PointerFunction(nint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PointerReadFunction(nint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte PointerReadByteFunction(nint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint PointerReadUInt32Function(nint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PointerReadWithArgumentFunction(nint pointer,
        int ignored);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PointerWriteOrderFunction(nint destination,
        nint source, nint writtenCount, nuint size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PointerReadWithTwoPointersFunction(
        nint first, int ignored, nint second);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PointerReadWithLongHomeFunction(
        int ignored0, int ignored1, int ignored2, int ignored3, int ignored4,
        int ignored5, int ignored6, int ignored7, int ignored8, int ignored9,
        int ignored10, int ignored11, int ignored12, int ignored13,
        int ignored14, int ignored15, nint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong PointerReadUInt64Function(nint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long PointerReadInt64Function(nint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nuint PointerReadNativeUIntFunction(nint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float PointerReadFloat32Function(nint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double PointerReadFloat64Function(nint value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint GetCommandLineFunction();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ConditionalInt32Function(
        byte publish, int value, int fallback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte ConditionalTwoOutputFunction(
        byte publish, int first, int second,
        out int firstResult, out int secondResult);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte ConditionalFourInputFunction(
        byte publish, byte eligible, int value, out int result);


    [Test]
    public async Task StandardInitReferenceBodiesRoundTripAndExecuteAsync()
    {
        const string source = """
            public void Fill(ref<init int32> result, int32 value) {
                *result = value;
            }

            public int32 Run(int32 value) {
                init int32 result;
                Fill(ref init result, value);
                return result;
            }
            """;
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "init-reference-body", SchemaIdentity, 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, artifact),
            "init-reference-body", []);

        var fill = loaded.Functions.Single(static function =>
            function.Name == "Fill");
        Assert.That(fill.Variables[0].IsInit, Is.True);
        Assert.That(fill.Variables[0].IsOut, Is.False);
        Assert.That(fill.Body.Statements.OfType<BoundExpressionStatement>()
            .Single().Expression, Is.TypeOf<BoundReferenceWrite>());

        var run = loaded.Functions.Single(static function =>
            function.Name == "Run");
        Assert.That(run.Body.Statements[0],
            Is.TypeOf<BoundVacantLocal>());
        var call = (BoundCall)((BoundExpressionStatement)
            run.Body.Statements[1]).Expression;
        Assert.That(call.Arguments[0],
            Is.TypeOf<BoundReferenceFormation>());
        Assert.That(run.Body.Statements[2],
            Is.TypeOf<BoundReturn>());

        var image = NativeEmitter.EmitLibrary(loaded, ["Run"],
            NativeRuntimePolicy.FailFast);
        var libraryPath = Path.Combine(Path.GetTempPath(),
            $"terrum-standard-init-{Guid.NewGuid():N}.dll");
        nint library = 0;
        try
        {
            await File.WriteAllBytesAsync(libraryPath, image)
                .ConfigureAwait(false);
            library = NativeLibrary.Load(libraryPath);
            var runFunction = Marshal.GetDelegateForFunctionPointer<
                Int32Function>(NativeLibrary.GetExport(library, "Run"));
            Assert.That(runFunction(42), Is.EqualTo(42));
        }
        finally
        {
            if (library != 0)
            {
                NativeLibrary.Free(library);
            }

            await TestFileCleanup.DeleteWithRetryAsync(libraryPath)
                .ConfigureAwait(false);
        }

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(SchemaIdentity, artifact),
            "init-reference-body", []);
        var importedFill = reference.Functions.Single(static function =>
            function.Name == "Fill");
        Assert.That(importedFill.Variables[0].IsInit, Is.True);
    }

    [Test]
    public async Task StandardWritableInitReferenceUsesReplacementStoresAsync()
    {
        const string source = """
            public void Replace(write ref<init int32> destination,
                int32 first, int32 second) {
                *destination = first;
                *destination = second;
            }

            public int32 Run(int32 first, int32 second) {
                init mut int32 result;
                Replace(ref init result, first, second);
                return result;
            }
            """;
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!, "init-replacement-body",
            SchemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(
                new TrRmArtifactIndex(artifact));
        var replaceMethodRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Replace")
            {
                replaceMethodRow = row;
                break;
            }
        }
        Assert.That(replaceMethodRow, Is.GreaterThan(0));
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(replaceMethodRow),
            tables.Contracts.Count);
        Assert.That(body.GetRecord(1)[6], Is.EqualTo((byte)1));
        Assert.That(body.GetRecord(2)[6], Is.EqualTo((byte)2));
        var writableReferenceTypeSpecRow = 0;
        for (var row = 1; row <= tables.TypeSpecs.Count; row++)
        {
            var typeSpec = tables.TypeSpecs.GetTypeSpecRef(
                checked((uint)row), allowInitQualifier: true);
            if (typeSpec.Form == 0x0E && typeSpec.Qualifiers == 1)
            {
                writableReferenceTypeSpecRow = row;
                break;
            }
        }
        Assert.That(writableReferenceTypeSpecRow, Is.GreaterThan(0));
        var typeSpecCount = tables.TypeSpecs.Count;
        var readonlyReplacement = RewriteSection(artifact, 0x0011,
            bytes => {
                var dataOffset = checked((typeSpecCount + 1)
                    * sizeof(uint));
                var rowOffset = BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes.AsSpan((writableReferenceTypeSpecRow - 1)
                        * sizeof(uint)));
                bytes[checked(dataOffset + (int)rowOffset + 1)] = 0;
            });
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    readonlyReplacement),
                "init-replacement-body", []));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, artifact),
            "init-replacement-body", []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            "init-replacement-reemitted", SchemaIdentity, 1);
        Assert.That(reemitted.AsSpan().SequenceEqual(artifact), Is.False);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, reemitted),
            "init-replacement-reemitted", []);
        var reloadedTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(
                new TrRmArtifactIndex(reemitted));
        var reloadedReplaceMethodRow = 0;
        for (var row = 1; row <= reloadedTables.Methods.Count; row++)
        {
            if (reloadedTables.Methods.GetName(row) == "Replace")
            {
                reloadedReplaceMethodRow = row;
                break;
            }
        }
        Assert.That(reloadedReplaceMethodRow, Is.GreaterThan(0));
        var reloadedBody = reloadedTables.Bodies.GetRegisterBody(
            reloadedTables.Methods.GetBodyRow(reloadedReplaceMethodRow),
            reloadedTables.Contracts.Count);
        Assert.That(reloadedBody.GetRecord(1)[6], Is.EqualTo((byte)1));
        Assert.That(reloadedBody.GetRecord(2)[6], Is.EqualTo((byte)2));

        var replace = reloaded.Functions.Single(static function =>
            function.Name == "Replace");
        Assert.That(replace.Body.Statements
            .OfType<BoundExpressionStatement>()
            .Count(static statement =>
                statement.Expression is BoundReferenceWrite),
            Is.EqualTo(2));

        var image = NativeEmitter.EmitLibrary(reloaded, ["Run"],
            NativeRuntimePolicy.FailFast);
        var libraryPath = Path.Combine(Path.GetTempPath(),
            $"terrum-standard-init-replace-{Guid.NewGuid():N}.dll");
        nint library = 0;
        try
        {
            await File.WriteAllBytesAsync(libraryPath, image)
                .ConfigureAwait(false);
            library = NativeLibrary.Load(libraryPath);
            var run = Marshal.GetDelegateForFunctionPointer<
                Int32PairFunction>(NativeLibrary.GetExport(library, "Run"));
            Assert.That(run(10, 42), Is.EqualTo(42));
        }
        finally
        {
            if (library != 0)
            {
                NativeLibrary.Free(library);
            }

            await TestFileCleanup.DeleteWithRetryAsync(libraryPath)
                .ConfigureAwait(false);
        }
    }

    [Test]
    public async Task StandardWritableInitReferenceReplacementSurvivesProviderReemissionAsync()
    {
        const string providerIdentity = "init-replacement-provider";
        const string consumerIdentity = "init-replacement-consumer";
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public void Replace(write ref<init int32> destination,
                    int32 first, int32 second) {
                    *destination = first;
                    *destination = second;
                }
                """), new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
            });
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)providerCompilation.Program!, providerIdentity,
            LocalFieldLayoutSchemaIdentity, 1);
        var providerInput = new TrRmStandardArtifactInput(
            LocalFieldLayoutSchemaIdentity, providerArtifact);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, []);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            public int32 CallReplace(int32 first, int32 second) {
                init mut int32 result;
                Replace(ref init result, first, second);
                return result;
            }
            int32 Main() { return CallReplace(10, 42); }
            """), providerReference,
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
            });
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)consumerCompilation.Program!, consumerIdentity,
            LocalFieldLayoutSchemaIdentity, 1);
        var consumerInput = new TrRmStandardArtifactInput(
            LocalFieldLayoutSchemaIdentity, consumerArtifact);
        var loadedConsumer = TrRmExecutableCompiler.LoadStandard(
            consumerInput, consumerIdentity, [providerInput]);
        var importedReplace = loadedConsumer.Functions.Single(
            static function => function.Name == "Replace");
        Assert.That(importedReplace.IsExternal, Is.True);
        Assert.That(importedReplace.ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        Assert.That(NativeModuleCompiler.ExecuteStandard(consumerInput,
            consumerIdentity, [providerInput],
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));

        var reemittedConsumer = TrRmExecutableCompiler.EmitStandard(
            loadedConsumer, consumerIdentity, LocalFieldLayoutSchemaIdentity, 1);
        var reemittedConsumerInput = new TrRmStandardArtifactInput(
            LocalFieldLayoutSchemaIdentity, reemittedConsumer);
        var loadedProvider = TrRmExecutableCompiler.LoadStandard(
            providerInput, providerIdentity, []);
        var reemittedProvider = TrRmExecutableCompiler.EmitStandard(
            loadedProvider, providerIdentity, LocalFieldLayoutSchemaIdentity, 1);
        var reemittedProviderInput = new TrRmStandardArtifactInput(
            LocalFieldLayoutSchemaIdentity, reemittedProvider);
        _ = TrRmExecutableCompiler.LoadStandard(reemittedConsumerInput,
            consumerIdentity, [reemittedProviderInput]);
        Assert.That(NativeModuleCompiler.ExecuteStandard(
            reemittedConsumerInput, consumerIdentity,
            [reemittedProviderInput], NativeRuntimePolicy.FailFast),
            Is.EqualTo(42));
        var image = NativeModuleCompiler.EmitStandardExecutable(
            reemittedConsumerInput, consumerIdentity,
            [reemittedProviderInput], NativeRuntimePolicy.FailFast);
        await TrRmForeignInvocationAddressEmitterTests
            .AssertNativeImageExitCodeAsync(image, consumerIdentity, 42)
            .ConfigureAwait(false);
    }

    [Test]
    public async Task StandardUnconditionalInitPostconditionPublishesAcrossProviderAsync()
    {
        const string providerIdentity = "init-postcondition-provider";
        const string consumerIdentity = "init-postcondition-consumer";
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public void Create(ref<init int32> destination, int32 value)
                    ensure *destination is init
                {
                    *destination = value;
                }
                """), new CompilationOptions {
                CoreContractFailureIdentitySelection = new(
                    "sample.consumer",
                    new CoreContractFailureDeclarationIdentity(
                        "core.contracts", "requirement-exception"),
                    new CoreContractFailureDeclarationIdentity(
                        "core.contracts", "postcondition-exception"),
                    new CoreContractFailureDeclarationIdentity(
                        "core.contracts", "require-exception-schema")),
            });
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, providerIdentity,
            LocalFieldLayoutSchemaIdentity, 1);
        var providerInput = new TrRmStandardArtifactInput(
            LocalFieldLayoutSchemaIdentity, providerArtifact);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, []);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                int32 CallCreate(int32 value) {
                    init int32 result;
                    Create(ref init result, value);
                    return result;
                }
                int32 Main() {
                    return CallCreate(42);
                }
                """),
            providerReference,
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
            });
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = (BoundProgram)consumerCompilation.Program!;
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, consumerIdentity,
            LocalFieldLayoutSchemaIdentity, 1);
        var consumerInput = new TrRmStandardArtifactInput(
            LocalFieldLayoutSchemaIdentity, consumerArtifact);
        var loadedConsumer = TrRmExecutableCompiler.LoadStandard(
            consumerInput, consumerIdentity, [providerInput]);
        var importedCreate = loadedConsumer.Functions.Single(
            static function => function.Name == "Create");
        Assert.That(importedCreate.IsExternal, Is.True);
        Assert.That(importedCreate.ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        Assert.That(NativeModuleCompiler.ExecuteStandard(consumerInput,
            consumerIdentity, [providerInput],
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));

        var reemittedConsumer = TrRmExecutableCompiler.EmitStandard(
            loadedConsumer, consumerIdentity,
            LocalFieldLayoutSchemaIdentity, 1);
        var reemittedConsumerInput = new TrRmStandardArtifactInput(
            LocalFieldLayoutSchemaIdentity, reemittedConsumer);
        Assert.That(NativeModuleCompiler.ExecuteStandard(
            reemittedConsumerInput, consumerIdentity, [providerInput],
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));

        var loadedProvider = TrRmExecutableCompiler.LoadStandard(
            providerInput, providerIdentity, []);
        var reemittedProvider = TrRmExecutableCompiler.EmitStandard(
            loadedProvider, providerIdentity,
            LocalFieldLayoutSchemaIdentity, 1);
        var reemittedProviderInput = new TrRmStandardArtifactInput(
            LocalFieldLayoutSchemaIdentity, reemittedProvider);
        _ = TrRmExecutableCompiler.LoadStandard(reemittedConsumerInput,
            consumerIdentity, [reemittedProviderInput]);
        Assert.That(NativeModuleCompiler.ExecuteStandard(
            reemittedConsumerInput, consumerIdentity,
            [reemittedProviderInput], NativeRuntimePolicy.FailFast),
            Is.EqualTo(42));
        var image = NativeModuleCompiler.EmitStandardExecutable(
            reemittedConsumerInput, consumerIdentity,
            [reemittedProviderInput], NativeRuntimePolicy.FailFast);
        await TrRmForeignInvocationAddressEmitterTests
            .AssertNativeImageExitCodeAsync(image, consumerIdentity, 42)
            .ConfigureAwait(false);
    }

    [TestCase(SourceArithmeticProfile.Development, (byte)1)]
    [TestCase(SourceArithmeticProfile.Release, (byte)2)]
    public async Task GeneralPredicateDefaultOutputPublishesAcrossProviderAsync(
        SourceArithmeticProfile arithmeticProfile,
        byte expectedAdditionPolicy)
    {
        const string providerIdentity = "general-init-output-provider";
        const string consumerIdentity = "general-init-output-consumer";
        const string providerSource = """
            public bool Fill(bool publish, int32 value, int32 threshold,
                ref<init int32> result) {
                if (publish || value > threshold || value > 0
                    || value + 1 > threshold || -value > threshold
                    || int64(value) > 0
                    || (publish ? value : 0) > threshold) {
                    *result = value;
                    return true;
                }
                return false;
            }
            """;
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource),
            new CompilationOptions {
                SourceArithmeticProfile = arithmeticProfile,
            });
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var expectedBooleanPredicate = new BoundOutputStatePredicate(
            3, 2, 1, new BoundRead(0, PrimitiveType.Bool, default));
        var expectedNumericPredicate = new BoundOutputStatePredicate(
            3, 3, 1,
            new BoundBinary(TokenKind.Greater,
                new BoundRead(1, PrimitiveType.Int32, default),
                new BoundRead(2, PrimitiveType.Int32, default),
                PrimitiveType.Bool, default));
        var expectedLiteralPredicate = new BoundOutputStatePredicate(
            3, 4, 1,
            new BoundBinary(TokenKind.Greater,
                new BoundRead(1, PrimitiveType.Int32, default),
                new BoundConstant(0, PrimitiveType.Int32, default),
                PrimitiveType.Bool, default));
        var expectedArithmeticPredicate = new BoundOutputStatePredicate(
            3, 5, 1,
            new BoundBinary(TokenKind.Greater,
                new BoundBinary(TokenKind.Plus,
                    new BoundRead(1, PrimitiveType.Int32, default),
                    new BoundConstant(1, PrimitiveType.Int32, default),
                    PrimitiveType.Int32, default) {
                    OverflowBehavior = arithmeticProfile
                        == SourceArithmeticProfile.Release
                            ? BoundIntegerOverflowBehavior.Wrapping
                            : BoundIntegerOverflowBehavior.Checked,
                },
                new BoundRead(2, PrimitiveType.Int32, default),
                PrimitiveType.Bool, default));
        var expectedUnaryPredicate = new BoundOutputStatePredicate(
            3, 6, 1,
            new BoundBinary(TokenKind.Greater,
                new BoundUnary(TokenKind.Minus,
                    new BoundRead(1, PrimitiveType.Int32, default),
                    PrimitiveType.Int32, default) {
                    OverflowBehavior = arithmeticProfile
                        == SourceArithmeticProfile.Release
                            ? BoundIntegerOverflowBehavior.Wrapping
                            : BoundIntegerOverflowBehavior.Checked,
                },
                new BoundRead(2, PrimitiveType.Int32, default),
                PrimitiveType.Bool, default));
        var expectedReturnPredicate = new BoundOutputStatePredicate(
            3, 7, 1,
            new BoundContractReturnValue(PrimitiveType.Bool, default));
        var expectedConversionPredicate = new BoundOutputStatePredicate(
            3, 8, 1,
            new BoundBinary(TokenKind.Greater,
                new BoundCast(
                    new BoundRead(1, PrimitiveType.Int32, default),
                    PrimitiveType.Int64, default),
                new BoundConstant(0, PrimitiveType.Int64, default),
                PrimitiveType.Bool, default));
        var expectedConditionalPredicate = new BoundOutputStatePredicate(
            3, 9, 1,
            new BoundBinary(TokenKind.Greater,
                new BoundConditional(
                    new BoundRead(0, PrimitiveType.Bool, default),
                    new BoundRead(1, PrimitiveType.Int32, default),
                    new BoundConstant(0, PrimitiveType.Int32, default),
                    PrimitiveType.Int32, default),
                new BoundRead(2, PrimitiveType.Int32, default),
                PrimitiveType.Bool, default));
        providerProgram = providerProgram with {
            Functions = providerProgram.Functions.Select(function =>
                function.Name == "Fill"
                    ? function with {
                        CheckedOutputStatePredicates = [
                            expectedBooleanPredicate,
                            expectedNumericPredicate,
                            expectedLiteralPredicate,
                            expectedArithmeticPredicate,
                            expectedUnaryPredicate,
                            expectedReturnPredicate,
                            expectedConversionPredicate,
                            expectedConditionalPredicate,
                        ],
                    }
                    : function).ToImmutableArray(),
        };
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, providerIdentity,
            GeneralPredicateGraphsSchemaIdentity, 1);
        var providerInput = new TrRmStandardArtifactInput(
            GeneralPredicateGraphsSchemaIdentity, providerArtifact);
        var providerTables
            = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(
                    providerArtifact));
        Assert.That(providerTables.PredicateOperations.Count,
            Is.EqualTo(9));
        Assert.That(Enumerable.Range(1, 9).Select(operationRow =>
                providerTables.PredicateOperations.GetOperationCode(
                    operationRow)),
            Is.EqualTo(new ushort[] {
                0x0013, 0x0013, 0x0013, 0x0004, 0x0013, 0x0002,
                0x0013, 0x0015, 0x0013,
            }));
        Assert.That(Enumerable.Range(1, 9).Select(operationRow =>
                providerTables.PredicateOperations.GetPolicy(operationRow)),
            Is.EqualTo(new byte[] {
                0, 0, 0, expectedAdditionPolicy, 0,
                arithmeticProfile == SourceArithmeticProfile.Release
                    ? (byte)2 : (byte)1, 0, 0, 0,
            }));
        Assert.That(providerTables.Features.TryGetFeature(11, out _),
            Is.True);
        Assert.That(providerTables.FrozenValueNodes.Count, Is.EqualTo(3));
        Assert.That(providerTables.TypeSpecs.AreEquivalentTypeSpecRefs(
                providerTables.FrozenValueNodes.GetTypeSpecRef(1),
                0xF200_0008),
            Is.True);
        Assert.That(providerTables.FrozenValueNodes.GetScalarPayload(1,
                providerTables.Blobs).ToArray(),
            Is.EqualTo(new byte[] { 0, 0, 0, 0 }));
        Assert.That(providerTables.FrozenValueNodes.GetScalarPayload(2,
                providerTables.Blobs).ToArray(),
            Is.EqualTo(new byte[] { 1, 0, 0, 0 }));
        Assert.That(providerTables.TypeSpecs.AreEquivalentTypeSpecRefs(
                providerTables.FrozenValueNodes.GetTypeSpecRef(3),
                0xF200_0009),
            Is.True);
        Assert.That(providerTables.FrozenValueNodes.GetScalarPayload(3,
                providerTables.Blobs).ToArray(),
            Is.EqualTo(new byte[sizeof(long)]));
        Assert.That(Enumerable.Range(1,
                providerTables.PredicateReadRoots.Count).Any(rootRow =>
                providerTables.PredicateReadRoots.GetRootKind(rootRow) == 5
                && providerTables.PredicateReadRoots
                    .GetRootIdentity(rootRow) == 0),
            Is.True);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, []);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                int32 Run(bool publish, int32 value, int32 threshold,
                    int32 fallback) {
                    init int32 result;
                    if (Fill(publish, value, threshold,
                            ref init result)) {
                        return result;
                    }
                    return fallback;
                }
                int32 Main() {
                    return Run(false, 42, 0, 7);
                }
                """),
            providerReference,
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
            });
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)consumerCompilation.Program!, consumerIdentity,
            GeneralPredicateGraphsSchemaIdentity, 1);
        var consumerInput = new TrRmStandardArtifactInput(
            GeneralPredicateGraphsSchemaIdentity, consumerArtifact);
        var loadedConsumer = TrRmExecutableCompiler.LoadStandard(
            consumerInput, consumerIdentity, [providerInput]);
        var importedFill = loadedConsumer.Functions.Single(
            static function => function.Name == "Fill");
        Assert.That(importedFill.IsExternal, Is.True);
        Assert.That(importedFill.CheckedOutputStates.Length,
            Is.EqualTo(2));
        Assert.That(importedFill.CheckedOutputStatePredicates,
            Is.EqualTo([
                expectedBooleanPredicate,
                expectedNumericPredicate,
                expectedLiteralPredicate,
                expectedArithmeticPredicate,
                expectedUnaryPredicate,
                expectedReturnPredicate,
                expectedConversionPredicate,
                expectedConditionalPredicate,
            ]));
        Assert.That(NativeModuleCompiler.ExecuteStandard(consumerInput,
            consumerIdentity, [providerInput],
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));

        var reemittedConsumer = TrRmExecutableCompiler.EmitStandard(
            loadedConsumer, consumerIdentity,
            GeneralPredicateGraphsSchemaIdentity, 1);
        var reemittedConsumerInput = new TrRmStandardArtifactInput(
            GeneralPredicateGraphsSchemaIdentity, reemittedConsumer);
        var loadedProvider = TrRmExecutableCompiler.LoadStandard(
            providerInput, providerIdentity, []);
        var reemittedProvider = TrRmExecutableCompiler.EmitStandard(
            loadedProvider, providerIdentity,
            GeneralPredicateGraphsSchemaIdentity, 1);
        var reemittedProviderInput = new TrRmStandardArtifactInput(
            GeneralPredicateGraphsSchemaIdentity, reemittedProvider);
        var reloadedConsumer = TrRmExecutableCompiler.LoadStandard(
            reemittedConsumerInput,
            consumerIdentity, [reemittedProviderInput]);
        Assert.That(reloadedConsumer.Functions.Single(
            static function => function.Name == "Fill")
            .CheckedOutputStatePredicates,
            Is.EqualTo([
                expectedBooleanPredicate,
                expectedNumericPredicate,
                expectedLiteralPredicate,
                expectedArithmeticPredicate,
                expectedUnaryPredicate,
                expectedReturnPredicate,
                expectedConversionPredicate,
                expectedConditionalPredicate,
            ]));
        Assert.That(NativeModuleCompiler.ExecuteStandard(
            reemittedConsumerInput, consumerIdentity,
            [reemittedProviderInput], NativeRuntimePolicy.FailFast),
            Is.EqualTo(42));
        var image = NativeModuleCompiler.EmitStandardExecutable(
            reemittedConsumerInput, consumerIdentity,
            [reemittedProviderInput], NativeRuntimePolicy.FailFast);
        await TrRmForeignInvocationAddressEmitterTests
            .AssertNativeImageExitCodeAsync(image, consumerIdentity, 42)
            .ConfigureAwait(false);
    }

    [Test]
    public async Task StandardInitReferenceBodiesFormLocalFieldDescriptorsAsync()
    {
        const string source = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Cell {
                public int32 Value;
            }

            public void Fill(ref<init int32> result, int32 value) {
                *result = value;
            }

            public int32 Run(int32 value) {
                init Cell cell;
                Fill(ref init cell.Value, value);
                return cell.Value;
            }
            """;
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "init-reference-field-body", SchemaIdentity, 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, artifact),
            "init-reference-field-body", []);
        var run = loaded.Functions.Single(static function =>
            function.Name == "Run");
        var call = (BoundCall)((BoundExpressionStatement)
            run.Body.Statements[1]).Expression;
        var formation = (BoundReferenceFormation)call.Arguments[0];
        Assert.That(formation.Place, Is.TypeOf<BoundFieldRead>());

        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            "init-reference-field-body-reemitted", SchemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity,
                reemittedArtifact),
            "init-reference-field-body-reemitted", []);
        var image = NativeEmitter.EmitLibrary(reloaded, ["Run"],
            NativeRuntimePolicy.FailFast);
        var libraryPath = Path.Combine(Path.GetTempPath(),
            $"terrum-standard-init-field-{Guid.NewGuid():N}.dll");
        nint library = 0;
        try
        {
            await File.WriteAllBytesAsync(libraryPath, image)
                .ConfigureAwait(false);
            library = NativeLibrary.Load(libraryPath);
            var runFunction = Marshal.GetDelegateForFunctionPointer<
                Int32Function>(NativeLibrary.GetExport(library, "Run"));
            Assert.That(runFunction(42), Is.EqualTo(42));
        }
        finally
        {
            if (library != 0)
            {
                NativeLibrary.Free(library);
            }

            await TestFileCleanup.DeleteWithRetryAsync(libraryPath)
                .ConfigureAwait(false);
        }

        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.TryGetSection(0x0014, out _, out var fieldCount),
            Is.True);
        Assert.That(fieldCount, Is.EqualTo(1));
        var malformedSections
            = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < index.SectionCount; sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            var bytes = section.ToArray();
            if (kind == 0x0014)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8),
                    0xF200_000D);
            }

            malformedSections.Add(new TrRmSection(kind, flags,
                checked((uint)recordCount), bytes));
        }

        var malformedArtifact = TrRmArtifactWriter.Encode(malformedSections);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    malformedArtifact),
                "init-reference-field-body", []));
    }

    [Test]
    public async Task StandardInitReferenceNestedFieldDestinationsUseFieldPathRowsAsync()
    {
        const string source = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Leaf {
                public int32 Value;
                public int32 Other;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Middle {
                public Leaf Child;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Root {
                public Middle Inner;
            }

            public void Fill(ref<init int32> result, int32 value) {
                *result = value;
            }

            public int32 Run(int32 value) {
                init Root root;
                Fill(ref init root.Inner.Child.Value, value);
                return root.Inner.Child.Value;
            }

            public int32 RunOther(int32 value) {
                init Root root;
                Fill(ref init root.Inner.Child.Other, value);
                return root.Inner.Child.Other;
            }
            """;
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "init-reference-nested-field-body",
            NestedFieldDestinationsSchemaIdentity, 1);
        var index =
            TrRmArtifactIndex.ForInitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft(
                artifact);
        Assert.That(index.TryGetSection(0x004B, out _, out var fieldPathCount),
            Is.True);
        Assert.That(fieldPathCount, Is.EqualTo(2));
        Assert.That(index.TryGetSection(0x0004, out var featureBytes,
            out var featureCount), Is.True);
        var features = new TrRmFeatureTableReader(featureBytes,
            featureCount);
        Assert.That(features.TryGetFeature(13, out var fieldPathFeature),
            Is.True);
        Assert.That(fieldPathFeature.RuleRevision, Is.EqualTo(1));
        var underalignedArtifact = RewriteSection(artifact, 0x0015,
            bytes => {
                for (var offset = 0; offset < bytes.Length; offset += 28)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        bytes.AsSpan(offset + 16), 1);
                }
            });
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(
                    NestedFieldDestinationsSchemaIdentity,
                    underalignedArtifact),
                "init-reference-nested-field-body", []));
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(
                    LocalFieldLayoutSchemaIdentity, artifact),
                "init-reference-nested-field-body", []));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(
                NestedFieldDestinationsSchemaIdentity, artifact),
            "init-reference-nested-field-body", []);
        var run = loaded.Functions.Single(static function =>
            function.Name == "Run");
        var call = (BoundCall)((BoundExpressionStatement)
            run.Body.Statements[1]).Expression;
        var formation = (BoundReferenceFormation)call.Arguments[0];
        var path = (BoundFieldRead)formation.Place;
        var fieldCount = 1;
        var root = path.Receiver;
        while (root is BoundFieldRead field)
        {
            fieldCount++;
            root = field.Receiver;
        }
        Assert.That(fieldCount, Is.EqualTo(3));
        Assert.That(root, Is.TypeOf<BoundRead>());
        var returnPath = (BoundFieldRead)(
            (BoundReturn)run.Body.Statements[2]).Value!;
        var readFieldCount = 1;
        var readRoot = returnPath.Receiver;
        while (readRoot is BoundFieldRead readField)
        {
            readFieldCount++;
            readRoot = readField.Receiver;
        }
        Assert.That(readFieldCount, Is.EqualTo(3));
        Assert.That(readRoot, Is.TypeOf<BoundRead>());

        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            "init-reference-nested-field-body-reemitted",
            NestedFieldDestinationsSchemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(
                NestedFieldDestinationsSchemaIdentity,
                reemittedArtifact),
            "init-reference-nested-field-body-reemitted", []);
        var reemittedIndex =
            TrRmArtifactIndex.ForInitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft(
                reemittedArtifact);
        Assert.That(reemittedIndex.TryGetSection(0x004B, out _,
            out var reemittedFieldPathCount), Is.True);
        Assert.That(reemittedFieldPathCount, Is.EqualTo(fieldPathCount));

        var image = NativeEmitter.EmitLibrary(reloaded, ["Run", "RunOther"],
            NativeRuntimePolicy.FailFast);
        var libraryPath = Path.Combine(Path.GetTempPath(),
            $"terrum-standard-init-nested-field-{Guid.NewGuid():N}.dll");
        nint library = 0;
        try
        {
            await File.WriteAllBytesAsync(libraryPath, image)
                .ConfigureAwait(false);
            library = NativeLibrary.Load(libraryPath);
            var runFunction = Marshal.GetDelegateForFunctionPointer<
                Int32Function>(NativeLibrary.GetExport(library, "Run"));
            Assert.That(runFunction(42), Is.EqualTo(42));
            var runOtherFunction = Marshal.GetDelegateForFunctionPointer<
                Int32Function>(NativeLibrary.GetExport(library, "RunOther"));
            Assert.That(runOtherFunction(17), Is.EqualTo(17));
        }
        finally
        {
            if (library != 0)
            {
                NativeLibrary.Free(library);
            }

            await TestFileCleanup.DeleteWithRetryAsync(libraryPath)
                .ConfigureAwait(false);
        }
    }

    [Test]
    public void StandardInitReferenceNestedFieldPathRejectsWrongRoot()
    {
        const string source = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Inner {
                public int32 Value;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct FirstRoot {
                public Inner Nested;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct SecondRoot {
                public Inner Nested;
            }

            public void Fill(ref<init int32> result, int32 value) {
                *result = value;
            }

            public int32 RunFirst(int32 value) {
                init FirstRoot root;
                Fill(ref init root.Nested.Value, value);
                return root.Nested.Value;
            }

            public int32 RunSecond(int32 value) {
                init SecondRoot root;
                Fill(ref init root.Nested.Value, value);
                return root.Nested.Value;
            }
            """;
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!,
            "init-reference-nested-wrong-root",
            NestedFieldDestinationsSchemaIdentity, 1);
        var tables
            = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForInitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft(
                        artifact));
        Assert.That(tables.FieldPaths.Count, Is.EqualTo(2));
        var firstRootTypeSpecRef
            = tables.FieldPaths.GetRootTypeSpecRef(1);
        var secondRootTypeSpecRef
            = tables.FieldPaths.GetRootTypeSpecRef(2);
        Assert.That(secondRootTypeSpecRef,
            Is.Not.EqualTo(firstRootTypeSpecRef));

        var offsetTableSize
            = checked((tables.FieldPaths.Count + 1) * sizeof(uint));
        var wrongRootArtifact = RewriteSection(artifact, 0x004B,
            bytes => BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(offsetTableSize), secondRootTypeSpecRef));
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(
                    NestedFieldDestinationsSchemaIdentity,
                    wrongRootArtifact),
                "init-reference-nested-wrong-root", []));

        var wrongOwnerArtifact = RewriteSection(artifact, 0x004B,
            bytes => BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(checked(offsetTableSize + 8)),
                tables.FieldPaths.GetFieldReference(1, 1)));
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(
                    NestedFieldDestinationsSchemaIdentity,
                    wrongOwnerArtifact),
                "init-reference-nested-wrong-owner", []));
    }

    [Test]
    public void StandardInitReferenceNestedFieldDestinationsRejectUnderAlignedLeaves()
    {
        const string source = """
            [Blittable] [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Inner {
                public int32 Value;
            }
            [Blittable] [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Root {
                public Inner Nested;
            }

            public void Fill(ref<init int32> result, int32 value) {
                *result = value;
            }

            public int32 Run(int32 value) {
                init Root root;
                Fill(ref init root.Nested.Value, value);
                return root.Nested.Value;
            }
            """;
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(
                (BoundProgram)compilation.Program!,
                "init-reference-nested-packed-field",
                NestedFieldDestinationsSchemaIdentity, 1));
    }

    [Test]
    public async Task StandardInitReferenceNestedFieldDestinationsBindAndReemitImportedPathsAsync()
    {
        const string providerIdentity = "init-nested-field-provider";
        const string consumerIdentity = "init-nested-field-consumer";
        TrRmSourceTypeExport[] exports = [
            new("", "Cell", "CellKey") {
                FieldExports = [
                    new TrRmSourceFieldExport("Value", "CellValueKey"),
                ],
            },
        ];
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Cell {
                    public int32 Value;
                }
                """), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)providerCompilation.Program!, providerIdentity,
            NestedFieldDestinationsSchemaIdentity, 1, exports);
        var providerInput = new TrRmStandardArtifactInput(
            NestedFieldDestinationsSchemaIdentity, providerArtifact);
        var referenceView = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, [], ["CellKey"],
            ["CellValueKey"]);
        var reference = TrRmExecutableCompiler.CreateStandardReferenceSet([
            referenceView, referenceView,
        ]);

        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Wrapper {
                    public Cell Inner;
                }

                public void Fill(ref<init int32> result, int32 value) {
                    *result = value;
                }

                public int32 ReadImportedRoot(int32 value) {
                    init Cell cell;
                    Fill(ref init cell.Value, value);
                    return cell.Value;
                }

                public int32 Main() {
                    init Wrapper wrapper;
                    Fill(ref init wrapper.Inner.Value, 42);
                    return wrapper.Inner.Value;
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)consumerCompilation.Program!, consumerIdentity,
            NestedFieldDestinationsSchemaIdentity, 1);
        var consumerTables
            = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForInitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft(
                        consumerArtifact));
        Assert.That(consumerTables.FieldPaths.Count, Is.EqualTo(2));
        var pathLengths = Enumerable.Range(1,
                consumerTables.FieldPaths.Count)
            .Select(consumerTables.FieldPaths.GetFieldCount)
            .OrderBy(static length => length)
            .ToArray();
        Assert.That(pathLengths, Is.EqualTo(new[] { 1, 2 }));
        foreach (var fieldPathRow in Enumerable.Range(1,
            consumerTables.FieldPaths.Count))
        {
            var leafOrdinal
                = consumerTables.FieldPaths.GetFieldCount(fieldPathRow) - 1;
            Assert.That(consumerTables.FieldPaths.GetFieldReference(
                fieldPathRow, leafOrdinal) >> 24, Is.EqualTo(0x1C));
        }
        var fieldImportRow = Enumerable.Range(1,
                consumerTables.Imports.Count)
            .Single(importRow => (consumerTables.Imports
                .GetExpectedDefinitionForImportRow(importRow) >> 24)
                    == 0x14);
        Assert.That(consumerTables.Imports
            .GetBindingForImportRow(fieldImportRow).ExportKey,
            Is.EqualTo("CellValueKey"));
        var importedFieldDefRow = checked((int)(
            consumerTables.Imports.GetExpectedDefinitionForImportRow(
                fieldImportRow) & 0x00FF_FFFF));
        var fieldPathOffsetTableSize
            = checked((consumerTables.FieldPaths.Count + 1) * sizeof(uint));
        var directStubFieldReferenceArtifact = RewriteSection(
            consumerArtifact, 0x004B,
            bytes => BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(fieldPathOffsetTableSize + 8),
                0x1400_0000u | checked((uint)importedFieldDefRow)));
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(
                    NestedFieldDestinationsSchemaIdentity,
                    directStubFieldReferenceArtifact),
                consumerIdentity, [providerInput]));
        var consumerInput = new TrRmStandardArtifactInput(
            NestedFieldDestinationsSchemaIdentity, consumerArtifact);
        var loadedConsumer = TrRmExecutableCompiler.LoadStandard(
            consumerInput, consumerIdentity, [providerInput]);
        Assert.That(loadedConsumer.StandardFieldProviderClosure,
            Is.Not.Null);
        Assert.That(NativeModuleCompiler.ExecuteStandard(consumerInput,
            consumerIdentity, [providerInput],
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));

        var reemittedConsumer = TrRmExecutableCompiler.EmitStandard(
            loadedConsumer, consumerIdentity,
            NestedFieldDestinationsSchemaIdentity, 1);
        var reemittedTables
            = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForInitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft(
                        reemittedConsumer));
        Assert.That(reemittedTables.FieldPaths.Count, Is.EqualTo(2));
        Assert.That(Enumerable.Range(1, reemittedTables.FieldPaths.Count)
            .Select(reemittedTables.FieldPaths.GetFieldCount)
            .OrderBy(static length => length).ToArray(),
            Is.EqualTo(new[] { 1, 2 }));
        foreach (var fieldPathRow in Enumerable.Range(1,
            reemittedTables.FieldPaths.Count))
        {
            var leafOrdinal
                = reemittedTables.FieldPaths.GetFieldCount(fieldPathRow) - 1;
            Assert.That(reemittedTables.FieldPaths.GetFieldReference(
                fieldPathRow, leafOrdinal) >> 24, Is.EqualTo(0x1C));
        }
        var reemittedFieldImportRow = Enumerable.Range(1,
                reemittedTables.Imports.Count)
            .Single(importRow => (reemittedTables.Imports
                .GetExpectedDefinitionForImportRow(importRow) >> 24)
                    == 0x14);
        Assert.That(reemittedTables.Imports
            .GetBindingForImportRow(reemittedFieldImportRow).ExportKey,
            Is.EqualTo("CellValueKey"));
        var reemittedInput = new TrRmStandardArtifactInput(
            NestedFieldDestinationsSchemaIdentity, reemittedConsumer);
        var reloadedConsumer = TrRmExecutableCompiler.LoadStandard(
            reemittedInput, consumerIdentity, [providerInput]);
        var reloadedRun = reloadedConsumer.Functions.Single(
            static function => function.Name == "Main");
        var reloadedReturn = (BoundFieldRead)(
            (BoundReturn)reloadedRun.Body.Statements[2]).Value!;
        var reloadedFieldCount = 1;
        var reloadedRoot = reloadedReturn.Receiver;
        while (reloadedRoot is BoundFieldRead reloadedField)
        {
            reloadedFieldCount++;
            reloadedRoot = reloadedField.Receiver;
        }
        Assert.That(reloadedFieldCount, Is.EqualTo(2));
        Assert.That(reloadedRoot, Is.TypeOf<BoundRead>());
        var reloadedImportedRoot = reloadedConsumer.Functions.Single(
            static function => function.Name == "ReadImportedRoot");
        var importedRootReturn = (BoundFieldRead)(
            (BoundReturn)reloadedImportedRoot.Body.Statements[2]).Value!;
        Assert.That(importedRootReturn.Receiver, Is.TypeOf<BoundRead>());
        var importedRootImage = NativeEmitter.EmitLibrary(
            reloadedConsumer, ["ReadImportedRoot"],
            NativeRuntimePolicy.FailFast);
        var importedRootLibraryPath = Path.Combine(Path.GetTempPath(),
            $"terrum-standard-init-imported-root-{Guid.NewGuid():N}.dll");
        nint importedRootLibrary = 0;
        try
        {
            await File.WriteAllBytesAsync(importedRootLibraryPath,
                importedRootImage).ConfigureAwait(false);
            importedRootLibrary
                = NativeLibrary.Load(importedRootLibraryPath);
            var readImportedRoot = Marshal
                .GetDelegateForFunctionPointer<Int32Function>(
                    NativeLibrary.GetExport(importedRootLibrary,
                        "ReadImportedRoot"));
            Assert.That(readImportedRoot(42), Is.EqualTo(42));
        }
        finally
        {
            if (importedRootLibrary != 0)
            {
                NativeLibrary.Free(importedRootLibrary);
            }

            await TestFileCleanup.DeleteWithRetryAsync(
                importedRootLibraryPath).ConfigureAwait(false);
        }
        Assert.That(NativeModuleCompiler.ExecuteStandard(reemittedInput,
            consumerIdentity, [providerInput],
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));

        var relocatedProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Aardvark {
                    public int32 Padding;
                }
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Cell {
                    public int32 Value;
                }
                """), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(relocatedProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                relocatedProviderCompilation.Diagnostics));
        var relocatedProviderArtifact
            = TrRmExecutableCompiler.EmitStandard(
                (BoundProgram)relocatedProviderCompilation.Program!,
                providerIdentity, NestedFieldDestinationsSchemaIdentity, 1,
                exports);
        var relocatedProviderInput = new TrRmStandardArtifactInput(
            NestedFieldDestinationsSchemaIdentity,
            relocatedProviderArtifact);
        var originalProviderTables
            = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForInitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft(
                        providerArtifact));
        var relocatedProviderTables
            = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForInitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft(
                        relocatedProviderArtifact));
        Assert.That(originalProviderTables.Exports
            .TryGetDefinitionForKey(1, "CellKey",
                out var originalCellDefinition), Is.True);
        Assert.That(relocatedProviderTables.Exports
            .TryGetDefinitionForKey(1, "CellKey",
                out var relocatedCellDefinition), Is.True);
        Assert.That(relocatedCellDefinition & 0x00FF_FFFF,
            Is.GreaterThan(originalCellDefinition & 0x00FF_FFFF));
        Assert.That(originalProviderTables.Exports
            .TryGetDefinitionForKey(1, "CellValueKey",
                out var originalValueDefinition), Is.True);
        Assert.That(relocatedProviderTables.Exports
            .TryGetDefinitionForKey(1, "CellValueKey",
                out var relocatedValueDefinition), Is.True);
        Assert.That(relocatedValueDefinition & 0x00FF_FFFF,
            Is.GreaterThan(originalValueDefinition & 0x00FF_FFFF));
        Assert.That(NativeModuleCompiler.ExecuteStandard(reemittedInput,
            consumerIdentity, [relocatedProviderInput],
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        var relocatedImage = NativeModuleCompiler.EmitStandardExecutable(
            reemittedInput, consumerIdentity, [relocatedProviderInput],
            NativeRuntimePolicy.FailFast);
        await TrRmForeignInvocationAddressEmitterTests
            .AssertNativeImageExitCodeAsync(relocatedImage,
                consumerIdentity, 42)
            .ConfigureAwait(false);

        var image = NativeModuleCompiler.EmitStandardExecutable(
            consumerInput, consumerIdentity, [providerInput],
            NativeRuntimePolicy.FailFast);
        await TrRmForeignInvocationAddressEmitterTests
            .AssertNativeImageExitCodeAsync(image, consumerIdentity, 42)
            .ConfigureAwait(false);

        var incompatibleProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Cell {
                    public int64 Value;
                }
                """), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(incompatibleProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                incompatibleProviderCompilation.Diagnostics));
        var incompatibleProvider = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)incompatibleProviderCompilation.Program!,
            providerIdentity, NestedFieldDestinationsSchemaIdentity, 1,
            exports);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(consumerInput,
                consumerIdentity, [
                    new TrRmStandardArtifactInput(
                        NestedFieldDestinationsSchemaIdentity,
                        incompatibleProvider),
                ]));
    }

    [Test]
    public async Task StandardInitNestedFieldPathPreservesTransitiveFieldProvidersAsync()
    {
        const string leafProviderIdentity
            = "init-nested-transitive-leaf-provider";
        const string wrapperProviderIdentity
            = "init-nested-transitive-wrapper-provider";
        const string consumerIdentity
            = "init-nested-transitive-consumer";
        var options = new CompilationOptions {
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        };

        var leafCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Cell {
                    public int32 Value;
                }
                """), options);
        Assert.That(leafCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, leafCompilation.Diagnostics));
        var leafArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)leafCompilation.Program!, leafProviderIdentity,
            NestedFieldDestinationsSchemaIdentity, 1, [
                new TrRmSourceTypeExport("", "Cell", "CellKey") {
                    FieldExports = [
                        new TrRmSourceFieldExport("Value", "CellValueKey"),
                    ],
                },
            ]);
        var leafInput = new TrRmStandardArtifactInput(
            NestedFieldDestinationsSchemaIdentity, leafArtifact);
        var leafReference = TrRmExecutableCompiler.CreateStandardReference(
            leafInput, leafProviderIdentity, [], ["CellKey"],
            ["CellValueKey"]);

        var wrapperCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Wrapper {
                    public Cell Inner;
                }
                """), leafReference, options);
        Assert.That(wrapperCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                wrapperCompilation.Diagnostics));
        var wrapperArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)wrapperCompilation.Program!,
            wrapperProviderIdentity, NestedFieldDestinationsSchemaIdentity,
            1, [
                new TrRmSourceTypeExport("", "Wrapper", "WrapperKey") {
                    FieldExports = [
                        new TrRmSourceFieldExport("Inner",
                            "WrapperInnerKey"),
                    ],
                },
            ]);
        var wrapperInput = new TrRmStandardArtifactInput(
            NestedFieldDestinationsSchemaIdentity, wrapperArtifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            wrapperInput, wrapperProviderIdentity, [leafInput],
            ["WrapperKey"], ["WrapperInnerKey", "CellValueKey"]);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                public void Fill(ref<init int32> result, int32 value) {
                    *result = value;
                }

                public int32 Main() {
                    init Wrapper wrapper;
                    Fill(ref init wrapper.Inner.Value, 42);
                    return wrapper.Inner.Value;
                }
                """), reference, options);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));

        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)consumerCompilation.Program!, consumerIdentity,
            NestedFieldDestinationsSchemaIdentity, 1);
        var consumerTables
            = TrRmStructuralMetadataValidator
                .ValidateAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForInitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft(
                            consumerArtifact));
        var twoStepPath = Enumerable.Range(1,
                consumerTables.FieldPaths.Count)
            .Single(row => consumerTables.FieldPaths.GetFieldCount(row) == 2);
        for (var stepIndex = 0; stepIndex < 2; stepIndex++)
        {
            Assert.That(consumerTables.FieldPaths.GetFieldReference(
                twoStepPath, stepIndex) >> 24, Is.EqualTo(0x1C));
        }

        var consumerInput = new TrRmStandardArtifactInput(
            NestedFieldDestinationsSchemaIdentity, consumerArtifact);
        var providers = new[] { wrapperInput, leafInput };
        var loaded = TrRmExecutableCompiler.LoadStandard(consumerInput,
            consumerIdentity, providers);
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            consumerIdentity, NestedFieldDestinationsSchemaIdentity, 1);
        var reemittedInput = new TrRmStandardArtifactInput(
            NestedFieldDestinationsSchemaIdentity, reemittedArtifact);
        var relocatedLeafCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Aardvark {
                    public int32 Padding;
                }

                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Cell {
                    public int32 Value;
                }
                """), options);
        Assert.That(relocatedLeafCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                relocatedLeafCompilation.Diagnostics));
        var relocatedLeafArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)relocatedLeafCompilation.Program!,
            leafProviderIdentity, NestedFieldDestinationsSchemaIdentity, 1, [
                new TrRmSourceTypeExport("", "Cell", "CellKey") {
                    FieldExports = [
                        new TrRmSourceFieldExport("Value", "CellValueKey"),
                    ],
                },
            ]);
        var relocatedLeafInput = new TrRmStandardArtifactInput(
            NestedFieldDestinationsSchemaIdentity, relocatedLeafArtifact);
        var originalLeafTables
            = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForInitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft(
                        leafArtifact));
        var relocatedLeafTables
            = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForInitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft(
                        relocatedLeafArtifact));
        Assert.That(originalLeafTables.Exports.TryGetDefinitionForKey(1,
            "CellKey", out var originalCellDefinition), Is.True);
        Assert.That(relocatedLeafTables.Exports.TryGetDefinitionForKey(1,
            "CellKey", out var relocatedCellDefinition), Is.True);
        Assert.That(relocatedCellDefinition & 0x00FF_FFFF,
            Is.GreaterThan(originalCellDefinition & 0x00FF_FFFF));
        Assert.That(originalLeafTables.Exports.TryGetDefinitionForKey(1,
            "CellValueKey", out var originalValueDefinition), Is.True);
        Assert.That(relocatedLeafTables.Exports.TryGetDefinitionForKey(1,
            "CellValueKey", out var relocatedValueDefinition), Is.True);
        Assert.That(relocatedValueDefinition & 0x00FF_FFFF,
            Is.GreaterThan(originalValueDefinition & 0x00FF_FFFF));
        var reorderedProviders = new[] {
            relocatedLeafInput, wrapperInput,
        };
        _ = TrRmExecutableCompiler.LoadStandard(reemittedInput,
            consumerIdentity, reorderedProviders);
        Assert.That(NativeModuleCompiler.ExecuteStandard(reemittedInput,
            consumerIdentity, reorderedProviders,
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        var image = NativeModuleCompiler.EmitStandardExecutable(
            reemittedInput, consumerIdentity, reorderedProviders,
            NativeRuntimePolicy.FailFast);
        await TrRmForeignInvocationAddressEmitterTests
            .AssertNativeImageExitCodeAsync(image, consumerIdentity, 42)
            .ConfigureAwait(false);
    }

    [Test]
    public async Task StandardInitNestedFieldPathReferenceSetRemapsPointerCellTypesAsync()
    {
        static (CompilationReference Reference,
            TrRmStandardArtifactInput ProviderInput)
            CreatePointerCellReference(string moduleIdentity, string typeName,
                string pointeeType)
        {
            var options = new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            };
            var compilation = Compilation.CreateLibrary(
                SyntaxTree.Parse($"""
                    [Blittable] [Layout(LayoutKind.Sequential)]
                    public struct {typeName} {{
                        public ptr<{pointeeType}> Value;
                    }}
                    """), options);
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    compilation.Diagnostics));

            var typeKey = $"{moduleIdentity}-type";
            var fieldKey = $"{moduleIdentity}-field";
            var artifact = TrRmExecutableCompiler.EmitStandard(
                (BoundProgram)compilation.Program!, moduleIdentity,
                NestedFieldDestinationsSchemaIdentity, 1, [
                    new TrRmSourceTypeExport("", typeName, typeKey) {
                        FieldExports = [
                            new TrRmSourceFieldExport("Value", fieldKey),
                        ],
                    },
                ]);
            var providerInput = new TrRmStandardArtifactInput(
                NestedFieldDestinationsSchemaIdentity, artifact);
            return (TrRmExecutableCompiler.CreateStandardReference(
                providerInput, moduleIdentity, [], [typeKey], [fieldKey]),
                providerInput);
        }

        var int32Selection = CreatePointerCellReference(
            "pointer-cell-int32-provider", "PointerCellInt32", "int32");
        var uint8Selection = CreatePointerCellReference(
            "pointer-cell-uint8-provider", "PointerCellUInt8", "uint8");
        var uint8AliasSelection = CreatePointerCellReference(
            "pointer-cell-uint8-alias-provider",
            "PointerCellUInt8Alias", "uint8");
        var conflictingInt32Reference = CreatePointerCellReference(
            "pointer-cell-int32-provider", "PointerCellInt32", "uint32")
            .Reference;
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReferenceSet([
                int32Selection.Reference, conflictingInt32Reference,
            ]));
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReferenceSet([
                conflictingInt32Reference, int32Selection.Reference,
            ]));
        var reference = TrRmExecutableCompiler.CreateStandardReferenceSet([
            int32Selection.Reference, uint8Selection.Reference,
            uint8AliasSelection.Reference,
        ]);

        Assert.That(reference.NativePointerTypes, Has.Length.EqualTo(2));
        var pointer32 = reference.Structures.Single(
            static structure => structure.Name == "PointerCellInt32")
            .Fields.Single().Type;
        var pointer8 = reference.Structures.Single(
            static structure => structure.Name == "PointerCellUInt8")
            .Fields.Single().Type;
        var pointer8Alias = reference.Structures.Single(
            static structure => structure.Name == "PointerCellUInt8Alias")
            .Fields.Single().Type;
        Assert.That(reference.NativePointerTypes[
            NativePointerTypes.Index(pointer32)].PointeeType,
            Is.EqualTo(PrimitiveType.Int32));
        Assert.That(reference.NativePointerTypes[
            NativePointerTypes.Index(pointer8)].PointeeType,
            Is.EqualTo(PrimitiveType.UInt8));
        Assert.That(pointer8Alias, Is.EqualTo(pointer8));

        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                public void ObserveInt32(PointerCellInt32 cell,
                    ptr<int32> value) {}

                public void ObserveUInt8(PointerCellUInt8 cell,
                    ptr<uint8> value) {}

                public void ObserveUInt8Alias(PointerCellUInt8Alias cell,
                    ptr<uint8> value) {}

                public void ObserveInt32Pointer(ptr<int32> value) {}

                public void PublishInt32Pointer(
                    ref<init ptr<int32>> output, ptr<int32> value) {
                    *output = value;
                }

                public ptr<int32> InitializeInt32Pointer(
                    ptr<int32> value) {
                    init PointerCellInt32 cell;
                    PublishInt32Pointer(ref init cell.Value, value);
                    return cell.Value;
                }

                public int32 Main() {
                    init ptr<int32> pointer;
                    ptr<int32> initialized
                        = InitializeInt32Pointer(pointer);
                    ObserveInt32Pointer(initialized);
                    return 42;
                }
                """), reference, new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)consumerCompilation.Program!,
            "pointer-cell-reference-consumer",
            NestedFieldDestinationsSchemaIdentity, 1);
        var consumerIndex = TrRmArtifactIndex
            .ForInitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft(
                consumerArtifact);
        var consumerTables
            = TrRmStructuralMetadataValidator
                .ValidateAndGetExecutionTables(consumerIndex);
        Assert.That(consumerTables.FieldPaths.Count, Is.EqualTo(1));
        Assert.That(consumerTables.FieldPaths.GetFieldCount(1),
            Is.EqualTo(1));
        Assert.That(consumerTables.FieldPaths.GetFieldReference(1, 0) >> 24,
            Is.EqualTo(0x1C));
        var consumerInput = new TrRmStandardArtifactInput(
            NestedFieldDestinationsSchemaIdentity, consumerArtifact);
        var providers = new[] {
            int32Selection.ProviderInput,
            uint8Selection.ProviderInput,
            uint8AliasSelection.ProviderInput,
        };
        _ = TrRmExecutableCompiler.LoadStandard(consumerInput,
            "pointer-cell-reference-consumer", providers);
        Assert.That(NativeModuleCompiler.ExecuteStandard(consumerInput,
            "pointer-cell-reference-consumer", providers,
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        var image = NativeModuleCompiler.EmitStandardExecutable(
            consumerInput, "pointer-cell-reference-consumer", providers,
            NativeRuntimePolicy.FailFast);
        await TrRmForeignInvocationAddressEmitterTests
            .AssertNativeImageExitCodeAsync(image,
                "pointer-cell-reference-consumer", 42)
            .ConfigureAwait(false);
    }

    [Test]
    public async Task StandardInitReferenceLocalFieldLayoutBindsAndReemitsProvidersAsync()
    {
        const string providerIdentity = "init-field-layout-provider";
        const string consumerIdentity = "init-field-layout-consumer";
        const string providerSource = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Cell {
                public int32 Value;
            }

            public void Fill(ref<init int32> result, int32 value) {
                *result = value;
            }

            public int32 Run(int32 value) {
                init Cell cell;
                Fill(ref init cell.Value, value);
                return cell.Value;
            }

            """;
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        TrRmSourceTypeExport[] typeExports = [
            new("", "Cell", "CellKey"),
        ];
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, providerIdentity,
            LocalFieldLayoutSchemaIdentity, 1, typeExports);
        var providerInput = new TrRmStandardArtifactInput(
            LocalFieldLayoutSchemaIdentity, providerArtifact);

        const string referenceProviderSource = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Cell {
                public int32 Value;
            }

            public void Fill(ref<init int32> result, int32 value) {
                *result = value;
            }
            """;
        var referenceProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(referenceProviderSource), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(referenceProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                referenceProviderCompilation.Diagnostics));
        var referenceProviderProgram
            = (BoundProgram)referenceProviderCompilation.Program!;
        var referenceProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            referenceProviderProgram, providerIdentity,
            LocalFieldLayoutSchemaIdentity, 1, typeExports);
        var referenceProviderInput = new TrRmStandardArtifactInput(
            LocalFieldLayoutSchemaIdentity, referenceProviderArtifact);

        var index = new TrRmArtifactIndex(providerArtifact);
        Assert.That(index.TryGetSection(0x0013, out var typeDefBytes,
            out var typeDefCount), Is.True);
        Assert.That(typeDefCount, Is.EqualTo(1));
        var typeDefFlags = BinaryPrimitives.ReadUInt32LittleEndian(
            typeDefBytes[8..]);
        Assert.That(typeDefFlags, Is.EqualTo(0x26));
        Assert.That(index.TryGetSection(0x0004, out var featureBytes,
            out var featureCount), Is.True);
        var featureTable = new TrRmFeatureTableReader(featureBytes,
            featureCount);
        Assert.That(featureTable.TryGetFeature(1, out var nativeBoundary),
            Is.True);
        Assert.That(nativeBoundary.RuleRevision, Is.EqualTo(1));
        Assert.That(featureTable.TryGetFeature(2, out var rootlessValues),
            Is.True);
        Assert.That(rootlessValues.RuleRevision, Is.EqualTo(1));
        Assert.That(featureTable.TryGetFeature(10, out _), Is.False);
        Assert.That(featureTable.TryGetFeature(13, out _), Is.False);

        var typeReference = TrRmExecutableCompiler.CreateStandardReference(
            referenceProviderInput, providerIdentity, [], ["CellKey"]);
        var referencedCell = typeReference.Structures.Single(
            static structure => structure.Name == "Cell");
        Assert.That(referencedCell.IsSequential, Is.True);
        Assert.That(referencedCell.StandardTypeOrigin!.Provider.ModuleIdentity,
            Is.EqualTo(providerIdentity));
        Assert.That(referencedCell.StandardTypeOrigin.Provider.SchemaIdentity,
            Is.EqualTo(LocalFieldLayoutSchemaIdentity));

        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            referenceProviderInput, providerIdentity, [], ["CellKey"]);
        var importedFill = providerReference.Functions.Single(
            static function => function.Name == "Fill");
        Assert.That(importedFill.Variables[0].IsInit, Is.True);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Wrapper {
                    public Cell Value;
                }

                int32 CallFill(int32 value) {
                    init int32 result;
                    Fill(ref init result, value);
                    return result;
                }

                int32 Main() {
                    return CallFill(42);
                }
                """),
            providerReference,
            new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = (BoundProgram)consumerCompilation.Program!;
        Assert.That(consumerProgram.Functions.Single(
            static function => function.Name == "Fill").Variables[0].IsInit,
            Is.True);
        Assert.That(consumerProgram.Structures.Any(static structure =>
            structure.StandardTypeOrigin is not null), Is.True);
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, consumerIdentity,
            LocalFieldLayoutSchemaIdentity, 1);
        var providerTables
            = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForInitializationReferenceLocalFieldLayoutDraft(
                        referenceProviderArtifact));
        var providerFillRow = 0;
        for (var methodRow = 1;
            methodRow <= providerTables.Methods.Count; methodRow++)
        {
            if (providerTables.Methods.GetName(methodRow) == "Fill")
            {
                Assert.That(providerFillRow, Is.Zero);
                providerFillRow = methodRow;
            }
        }
        Assert.That(providerFillRow, Is.Not.Zero);
        var providerFillSignature
            = providerTables.Methods.GetSignature(providerFillRow);
        var (providerFillParamStart, _) = providerTables.Signatures
            .GetParamRange(providerFillSignature);
        Assert.That(providerTables.TypeSpecs
            .TryGetInitializationReferenceReferentTypeSpecRef(
                providerTables.Parameters.GetTypeSpec(
                    providerFillParamStart), out _), Is.True);
        var consumerTables
            = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForInitializationReferenceLocalFieldLayoutDraft(
                        consumerArtifact));
        var fillImportRow = 0;
        for (var importRow = 1;
            importRow <= consumerTables.Imports.Count; importRow++)
        {
            if ((consumerTables.Imports
                    .GetExpectedDefinitionForImportRow(importRow) >> 24)
                == 0x16)
            {
                Assert.That(fillImportRow, Is.Zero);
                fillImportRow = importRow;
            }
        }
        Assert.That(fillImportRow, Is.Not.Zero);
        var expectedFillRow = checked((int)(
            consumerTables.Imports.GetExpectedDefinitionForImportRow(
                fillImportRow) & 0x00FF_FFFF));
        Assert.That(consumerTables.Methods.GetName(expectedFillRow),
            Is.EqualTo("Fill"));
        var consumerFillSignature
            = consumerTables.Methods.GetSignature(expectedFillRow);
        var (consumerFillParamStart, consumerFillParamCount)
            = consumerTables.Signatures.GetParamRange(consumerFillSignature);
        Assert.That(consumerFillParamCount, Is.EqualTo(2));
        Assert.That(consumerTables.TypeSpecs
            .TryGetInitializationReferenceReferentTypeSpecRef(
                consumerTables.Parameters.GetTypeSpec(
                    consumerFillParamStart), out _), Is.True);
        var consumerInput = new TrRmStandardArtifactInput(
            LocalFieldLayoutSchemaIdentity, consumerArtifact);
        var loadedConsumer = TrRmExecutableCompiler.LoadStandard(
            consumerInput, consumerIdentity, [referenceProviderInput]);
        Assert.That(NativeModuleCompiler.ExecuteStandard(consumerInput,
            consumerIdentity, [referenceProviderInput],
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));

        var reemittedConsumer = TrRmExecutableCompiler.EmitStandard(
            loadedConsumer, consumerIdentity,
            LocalFieldLayoutSchemaIdentity, 1);
        var reemittedConsumerInput = new TrRmStandardArtifactInput(
            LocalFieldLayoutSchemaIdentity, reemittedConsumer);
        var reloadedConsumer = TrRmExecutableCompiler.LoadStandard(
            reemittedConsumerInput,
            consumerIdentity, [referenceProviderInput]);
        Assert.That(NativeModuleCompiler.ExecuteStandard(
            reemittedConsumerInput, consumerIdentity,
            [referenceProviderInput], NativeRuntimePolicy.FailFast),
            Is.EqualTo(42));
        var consumerImage = NativeModuleCompiler.EmitStandardExecutable(
            consumerInput, consumerIdentity, [referenceProviderInput],
            NativeRuntimePolicy.FailFast);
        await TrRmForeignInvocationAddressEmitterTests
            .AssertNativeImageExitCodeAsync(consumerImage, consumerIdentity,
                42)
            .ConfigureAwait(false);

        var loadedProvider = TrRmExecutableCompiler.LoadStandard(
            providerInput, providerIdentity, []);
        var reemittedProvider = TrRmExecutableCompiler.EmitStandard(
            loadedProvider, providerIdentity,
            LocalFieldLayoutSchemaIdentity, 1);
        var reloadedProvider = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(
                LocalFieldLayoutSchemaIdentity, reemittedProvider),
            providerIdentity, []);
        var image = NativeEmitter.EmitLibrary(reloadedProvider, ["Run"],
            NativeRuntimePolicy.FailFast);
        var libraryPath = Path.Combine(Path.GetTempPath(),
            $"terrum-standard-init-layout-{Guid.NewGuid():N}.dll");
        nint library = 0;
        try
        {
            await File.WriteAllBytesAsync(libraryPath, image)
                .ConfigureAwait(false);
            library = NativeLibrary.Load(libraryPath);
            var runFunction = Marshal.GetDelegateForFunctionPointer<
                Int32Function>(NativeLibrary.GetExport(library, "Run"));
            Assert.That(runFunction(42), Is.EqualTo(42));
        }
        finally
        {
            if (library != 0)
            {
                NativeLibrary.Free(library);
            }

            await TestFileCleanup.DeleteWithRetryAsync(libraryPath)
                .ConfigureAwait(false);
        }

        var oldSchemaProvider = TrRmExecutableCompiler.EmitStandard(
            providerProgram, providerIdentity, SchemaIdentity, 1);
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(consumerInput,
                consumerIdentity,
                [new TrRmStandardArtifactInput(SchemaIdentity,
                    oldSchemaProvider)]));

        var mismatchedProvider = RewriteSection(referenceProviderArtifact, 0x0013,
            bytes => BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(8), typeDefFlags & ~0x20u));
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(consumerInput,
                consumerIdentity,
                [new TrRmStandardArtifactInput(
                    LocalFieldLayoutSchemaIdentity,
                    mismatchedProvider)]));
    }

    [Test]
    public void StandardInitReferenceLocalFieldLayoutLoadsCoLocatedMethodProviders()
    {
        const string providerIdentity = "init-layout-colocated-provider";
        const string consumerIdentity = "init-layout-colocated-consumer";
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Cell {
                    public int32 Value;
                }

                public void Fill(ref<init int32> result, int32 value) {
                    *result = value;
                }
                """), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, providerIdentity,
            LocalFieldLayoutSchemaIdentity, 1, [
                new TrRmSourceTypeExport("", "Cell", "CellKey"),
            ]);
        var providerInput = new TrRmStandardArtifactInput(
            LocalFieldLayoutSchemaIdentity, providerArtifact);
        var loadedProvider = TrRmExecutableCompiler.LoadStandard(
            providerInput, providerIdentity, []);
        var providerFunction = loadedProvider.Functions.Single(
            static function => function.Name == "Fill");
        var providerReference = new CompilationReference([], [
            providerFunction with {
                Body = new BoundBlock([], default),
                IsExternal = true,
                IsModuleReference = true,
                ModuleImport = new BoundModuleImport(providerIdentity,
                    "Fill", 1, 0),
            },
        ], BoundRuntimePolicy.FailFast,
            checkedReferenceTypes: loadedProvider.CheckedReferenceTypes);
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public void Store(ref<init int32> result, int32 value) {
                    *result = value;
                }
                """),
            providerReference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)consumerCompilation.Program!, consumerIdentity,
            LocalFieldLayoutSchemaIdentity, 1);
        var combinedArtifact
            = TrRmForeignInvocationAddressEmitterTests
                .CombinePopulatedNativeAddressModules(providerArtifact,
                    consumerArtifact,
                    TrRmArtifactSchema
                        .InitializationReferenceLocalFieldLayoutDraft,
                    requireFunctionPointerAnchors: false);
        var input = new TrRmStandardArtifactInput(
            LocalFieldLayoutSchemaIdentity, combinedArtifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForInitializationReferenceLocalFieldLayoutDraft(
                        combinedArtifact));
        Assert.That(tables.Modules.Count, Is.EqualTo(2));
        Assert.That(tables.Modules.TryFindModule(consumerIdentity,
            out var consumerModuleRow), Is.True);
        Assert.That(tables.Modules.TryFindModule(providerIdentity,
            out var providerModuleRow), Is.True);
        Assert.That(consumerModuleRow, Is.Not.EqualTo(providerModuleRow));
        var loadedConsumer = TrRmExecutableCompiler.LoadStandard(input,
            consumerIdentity, []);
        var importedFill = loadedConsumer.Functions.Single(
            static function => function.Name == "Fill");
        Assert.That(importedFill.IsExternal, Is.True);
        Assert.That(importedFill.ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        Assert.That(loadedConsumer.Functions.Any(
            static function => function.Name == "Store"), Is.True);
        var image = NativeModuleCompiler.EmitStandardLibrary(input,
            consumerIdentity, [], ["Store"],
            NativeRuntimePolicy.FailFast);
        Assert.That(image, Is.Not.Empty);
    }

    [Test]
    public void StandardInitReferenceLocalFieldLayoutRejectsChangedProviderTypesAndRows()
    {
        const string providerIdentity = "init-field-layout-invalid-provider";
        const string consumerIdentity = "init-field-layout-invalid-consumer";
        byte[] EmitProvider(string fieldType, string layoutKind)
        {
            var compilation = Compilation.CreateLibrary(
                SyntaxTree.Parse($$"""
                    [Blittable] [Layout(LayoutKind.{{layoutKind}})]
                    public struct Cell {
                        public {{fieldType}} Value;
                    }

                    public void Fill(ref<init int32> result, int32 value) {
                        *result = value;
                    }
                    """), new CompilationOptions {
                    LayoutCoreRegistration =
                        LayoutCoreRegistrationTestData.Create(),
                });
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            return TrRmExecutableCompiler.EmitStandard(
                (BoundProgram)compilation.Program!, providerIdentity,
                LocalFieldLayoutSchemaIdentity, 1, [
                    new TrRmSourceTypeExport("", "Cell", "CellKey"),
                ]);
        }

        var providerArtifact = EmitProvider("int32", "Sequential");
        var providerInput = new TrRmStandardArtifactInput(
            LocalFieldLayoutSchemaIdentity, providerArtifact);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, [], ["CellKey"]);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Wrapper {
                    public Cell Value;
                }

                int32 CallFill(int32 value) {
                    init int32 result;
                    Fill(ref init result, value);
                    return result;
                }

                int32 Main() {
                    return CallFill(42);
                }
                """),
            providerReference,
            new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)consumerCompilation.Program!, consumerIdentity,
            LocalFieldLayoutSchemaIdentity, 1);
        var consumerInput = new TrRmStandardArtifactInput(
            LocalFieldLayoutSchemaIdentity, consumerArtifact);

        void AssertProviderRejected(byte[] artifact)
            => Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler.LoadStandard(consumerInput,
                    consumerIdentity, [
                        new TrRmStandardArtifactInput(
                            LocalFieldLayoutSchemaIdentity, artifact),
                    ]));

        AssertProviderRejected(EmitProvider("int64", "Sequential"));
        AssertProviderRejected(EmitProvider("int32", "Transparent"));

        var invalidFieldOwner = RewriteSection(providerArtifact, 0x0014,
            bytes => BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(0), 2));
        AssertProviderRejected(invalidFieldOwner);
        var invalidTypeExportRow = RewriteSection(providerArtifact, 0x001D,
            bytes => BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(0), 0x1300_0002u));
        AssertProviderRejected(invalidTypeExportRow);
    }

    [Test]
    public void StandardInitReferenceLocalFieldLayoutReemitsRemappedIndexedSwitchBodies()
    {
        const string moduleIdentity = "init-layout-indexed-switch";
        var variables = ImmutableArray.Create(
            new BoundVariable("selector", PrimitiveType.Bool, false,
                default),
            new BoundVariable("matched", PrimitiveType.Int32, false,
                default),
            new BoundVariable("fallback", PrimitiveType.Int32, false,
                default));
        var graph = TrRmScalarGraph.FromAuditedBody([
            new TrRmScalarBlock([], [
                new BoundLocal(0,
                    new BoundConstant(1, PrimitiveType.Bool, default),
                    default),
            ], 0, null, null, 0, 0, SwitchValue: 1,
                SwitchCases: [
                    new TrRmScalarSwitchCase(1,
                        new TrRmScalarEdge(2, [])),
                ],
                SwitchDefault: new TrRmScalarEdge(3, [])),
            new TrRmScalarBlock([], [
                new BoundLocal(1,
                    new BoundConstant(40, PrimitiveType.Int32, default),
                    default),
            ], 0, null, null, 2, 0),
            new TrRmScalarBlock([], [
                new BoundLocal(2,
                    new BoundConstant(2, PrimitiveType.Int32, default),
                    default),
            ], 0, null, null, 3, 0),
        ], [
            PrimitiveType.Bool,
            PrimitiveType.Int32,
            PrimitiveType.Int32,
        ], 0, PrimitiveType.Int32, false);
        var function = new BoundFunction("Main", PrimitiveType.Int32,
            variables, 0, new BoundBlock([], default), default) {
            IndexedBody = graph,
        };
        var program = new BoundProgram([function], 0) {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
        };
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, LocalFieldLayoutSchemaIdentity, 1);
        var input = new TrRmStandardArtifactInput(
            LocalFieldLayoutSchemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        Assert.That(NativeModuleCompiler.ExecuteStandard(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast), Is.EqualTo(40));

        var loadedFunction = loaded.Functions.Single(
            static function => function.Name == "Main");
        var remappedFunction = BoundTreeTraversal.RewriteVariables(
            loadedFunction, [
                loadedFunction.Variables[1],
                loadedFunction.Variables[0],
                loadedFunction.Variables[2],
            ], static variable => variable switch {
                0 => 1,
                1 => 0,
                _ => variable,
            });
        var remappedProgram = loaded with {
            Functions = [remappedFunction],
        };
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            remappedProgram, "init-layout-indexed-switch-reordered",
            LocalFieldLayoutSchemaIdentity, 1);
        var reemittedInput = new TrRmStandardArtifactInput(
            LocalFieldLayoutSchemaIdentity, reemitted);
        Assert.That(NativeModuleCompiler.ExecuteStandard(reemittedInput,
            "init-layout-indexed-switch-reordered", [],
            NativeRuntimePolicy.FailFast), Is.EqualTo(40));
    }

    [Test]
    public void StandardInitReferenceLocalFieldLayoutReemitsIndexedCheckedDivRemBodies()
    {
        const string moduleIdentity = "init-layout-indexed-divrem";
        var variables = ImmutableArray.Create(
            new BoundVariable("dividend", PrimitiveType.Int32, false,
                default),
            new BoundVariable("divisor", PrimitiveType.Int32, false,
                default),
            new BoundVariable("quotient", PrimitiveType.Int32, false,
                default),
            new BoundVariable("remainder", PrimitiveType.Int32, false,
                default));
        var graph = TrRmScalarGraph.FromAuditedBody([
            new TrRmScalarBlock([], [
                new BoundLocal(0,
                    new BoundConstant(42, PrimitiveType.Int32, default),
                    default),
                new BoundLocal(1,
                    new BoundConstant(5, PrimitiveType.Int32, default),
                    default),
                new TrRmScalarDivRem(2, 3,
                    new BoundRead(0, PrimitiveType.Int32, default),
                    new BoundRead(1, PrimitiveType.Int32, default),
                    PrimitiveType.Int32),
            ], 0, null, null, 3, 0),
        ], [
            PrimitiveType.Int32,
            PrimitiveType.Int32,
            PrimitiveType.Int32,
            PrimitiveType.Int32,
        ], 0, PrimitiveType.Int32, false);
        var function = new BoundFunction("Main", PrimitiveType.Int32,
            variables, 0, new BoundBlock([], default), default) {
            IndexedBody = graph,
        };
        var program = new BoundProgram([function], 0) {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
        };
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, LocalFieldLayoutSchemaIdentity, 1);
        var input = new TrRmStandardArtifactInput(
            LocalFieldLayoutSchemaIdentity, artifact);
        Assert.That(NativeModuleCompiler.ExecuteStandard(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast), Is.EqualTo(8));

        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, LocalFieldLayoutSchemaIdentity, 1);
        var reemittedInput = new TrRmStandardArtifactInput(
            LocalFieldLayoutSchemaIdentity, reemitted);
        Assert.That(NativeModuleCompiler.ExecuteStandard(reemittedInput,
            moduleIdentity, [], NativeRuntimePolicy.FailFast), Is.EqualTo(8));
    }

    [Test]
    public void StandardInitReferenceLocalFieldLayoutReemitsIndexedLoopBodies()
    {
        const string moduleIdentity = "init-layout-indexed-loop";
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut int32 value = 0;
                while (value < 3) {
                    value = value + 1;
                }
                return value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!, moduleIdentity,
            LocalFieldLayoutSchemaIdentity, 1);
        var input = new TrRmStandardArtifactInput(
            LocalFieldLayoutSchemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        Assert.That(NativeModuleCompiler.ExecuteStandard(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast), Is.EqualTo(3));

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, LocalFieldLayoutSchemaIdentity, 1);
        var reemittedInput = new TrRmStandardArtifactInput(
            LocalFieldLayoutSchemaIdentity, reemitted);
        Assert.That(NativeModuleCompiler.ExecuteStandard(reemittedInput,
            moduleIdentity, [], NativeRuntimePolicy.FailFast), Is.EqualTo(3));
    }

    [Test]
    public void StandardInitReferenceLocalFieldLayoutSupportsTypeOnlyTransparentProviders()
    {
        const string providerIdentity = "init-field-layout-type-only";
        const string source = """
            [Blittable] [Layout(LayoutKind.Transparent)]
            public struct Number {
                public int32 Value;
            }
            """;
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!, providerIdentity,
            LocalFieldLayoutSchemaIdentity, 1,
            [new TrRmSourceTypeExport("", "Number", "NumberKey")]);
        var input = new TrRmStandardArtifactInput(
            LocalFieldLayoutSchemaIdentity, artifact);

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            input, providerIdentity, [], ["NumberKey"]);
        var number = reference.Structures.Single(
            static structure => structure.Name == "Number");
        Assert.That(number.IsTransparent, Is.True);
        Assert.That(number.StandardTypeOrigin!.ReferenceRoot!.SchemaIdentity,
            Is.EqualTo(LocalFieldLayoutSchemaIdentity));

        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(
                (BoundProgram)compilation.Program!, providerIdentity,
                LocalFieldLayoutSchemaIdentity, 1,
                [new TrRmSourceTypeExport("", "Number", "NumberKey") {
                    FieldExports = [
                        new TrRmSourceFieldExport("Value", "ValueKey"),
                    ],
                }]));

    }

    private static byte[] RewriteSection(byte[] artifact, ushort sectionKind,
        Action<byte[]> rewrite)
    {
        var index = new TrRmArtifactIndex(artifact);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var recordCount);
            var snapshot = bytes.ToArray();
            if (kind == sectionKind)
            {
                rewrite(snapshot);
            }
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)recordCount), snapshot));
        }

        return TrRmArtifactWriter.Encode(sections);
    }

    private static byte[] RemoveNativeParameterFeature(byte[] artifact,
        string schemaIdentity)
    {
        var schema = GetNativeParameterSchema(schemaIdentity);
        var index = new TrRmArtifactIndex(artifact, schema, null,
            usesNativePointerLoads: true);
        Assert.That(index.TryGetSection(0x0004, out var featureBytes,
            out var featureCount), Is.True);
        var features = new TrRmFeatureTableReader(featureBytes, featureCount,
            schema);
        var rows = new List<TrRmFeatureRow>(featureCount - 1);
        var found = false;
        for (var row = 1; row <= featureCount; row++)
        {
            var feature = features.GetFeature(row);
            if (feature.FeatureId == 16)
            {
                found = true;
            }
            else
            {
                rows.Add(feature);
            }
        }
        Assert.That(found, Is.True);
        var rewrittenFeatures = TrRmFeatureTableWriter.Encode(rows, schema);

        Assert.That(index.TryGetSection(0x0001, out var moduleBytes,
            out var moduleCount), Is.True);
        Assert.That(moduleCount, Is.EqualTo(1));
        var rewrittenModules = moduleBytes.ToArray();
        var featureCountOffset = 28;
        var moduleFeatureCount = BinaryPrimitives.ReadUInt32LittleEndian(
            rewrittenModules.AsSpan(featureCountOffset));
        Assert.That(moduleFeatureCount, Is.EqualTo((uint)featureCount));
        BinaryPrimitives.WriteUInt32LittleEndian(
            rewrittenModules.AsSpan(featureCountOffset),
            moduleFeatureCount - 1);

        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var count);
            if (kind == 0x0001)
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)count),
                    rewrittenModules));
            }
            else if (kind == 0x0004)
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)rows.Count), rewrittenFeatures));
            }
            else
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)count), bytes.ToArray()));
            }
        }

        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static byte[] ReplaceNativeParameterFeatureRevision(
        byte[] artifact, string schemaIdentity, uint revision)
    {
        var schema = GetNativeParameterSchema(schemaIdentity);
        var index = new TrRmArtifactIndex(artifact, schema, null,
            usesNativePointerLoads: true);
        Assert.That(index.TryGetSection(0x0004, out var featureBytes,
            out var featureCount), Is.True);
        var features = new TrRmFeatureTableReader(featureBytes, featureCount,
            schema);
        var featureRow = 0;
        for (var row = 1; row <= featureCount; row++)
        {
            if (features.GetFeature(row).FeatureId == 16)
            {
                featureRow = row;
                break;
            }
        }
        Assert.That(featureRow, Is.Not.Zero);

        var directoryCount = BinaryPrimitives.ReadUInt32LittleEndian(
            artifact.AsSpan(12));
        var featureOffset = -1;
        for (var indexInDirectory = 0;
            indexInDirectory < directoryCount; indexInDirectory++)
        {
            var entryOffset = checked(32 + (int)indexInDirectory * 16);
            if (BinaryPrimitives.ReadUInt16LittleEndian(
                    artifact.AsSpan(entryOffset)) == 0x0004)
            {
                featureOffset = checked((int)
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        artifact.AsSpan(entryOffset + 8)));
                break;
            }
        }
        Assert.That(featureOffset, Is.GreaterThanOrEqualTo(0));
        var malformed = artifact.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformed.AsSpan(featureOffset + ((featureRow - 1) * 8) + 4),
            revision);
        return malformed;
    }

    private static TrRmArtifactSchema GetNativeParameterSchema(
        string schemaIdentity)
        => schemaIdentity switch {
            GeneralPredicateGraphsPointerLoadsNativeParameterSchemaIdentity
                => TrRmArtifactSchema
                    .InitializationReferenceGeneralPredicatePointerLoadNativeParameterDraft,
            PointerLoadsNativeParameterSchemaIdentity
                => TrRmArtifactSchema
                    .InitializationReferencePointerLoadNativeParameterDraft,
            _ => throw new ArgumentOutOfRangeException(nameof(schemaIdentity)),
        };

    [Test]
    public void StandardBooleanInitOutputBranchBodyRoundTrips()
    {
        const string source = """
            public bool Fill(bool publish, int32 value,
                ref<init int32> result) {
                if (publish) {
                    *result = value;
                    return true;
                }
                return false;
            }
            """;
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!,
            "init-reference-conditional", SchemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(
                new TrRmArtifactIndex(artifact));
        var outputStates = tables.Contracts.GetOutputStateContracts(1);
        Assert.That(outputStates.Select(static state =>
                (state.ParameterOrdinal, state.State)),
            Is.EqualTo(new[] { (2, (byte)1), (2, (byte)0) }));
        Assert.That(tables.Contracts.GetPredicateGraphContracts(1)
            .Select(static predicate => predicate.PhaseLocalOrdinal),
            Is.EqualTo(new uint[] { 0, 1 }));
        Assert.That(TrRmFeatureManifestValidator.Requires(
            tables.Features, tables.Modules, 1,
            TrRmFeatureTableReader.PublishedPredicatesFeatureId), Is.True);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, artifact),
            "init-reference-conditional", []);

        var fill = loaded.Functions.Single(static function =>
            function.Name == "Fill");
        Assert.That(fill.Body.Statements[0], Is.TypeOf<BoundIf>());
        Assert.That(fill.Body.Statements[1], Is.TypeOf<BoundReturn>());
    }

    [TestCase(SchemaIdentity)]
    [TestCase(GeneralPredicateGraphsSchemaIdentity)]
    public void StandardPointerCellInitOutputRoundTrips(string schemaIdentity)
    {
        const string source = """
            public unsafe void Publish(ref<init ptr<int32>> output,
                ptr<int32> value) {
                *output = value;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, "standard-pointer-cell-init", schemaIdentity,
            1);
        if (StringComparer.Ordinal.Equals(schemaIdentity, SchemaIdentity))
        {
            var tables = TrRmStructuralMetadataValidator
                .ValidateAndGetExecutionTables(
                    new TrRmArtifactIndex(artifact));
            Assert.That(tables.Features.TryGetFeature(
                TrRmFeatureTableReader.NativeBoundaryContractsFeatureId,
                out var nativeBoundary), Is.True);
            Assert.That(nativeBoundary,
                Is.EqualTo(TrRmFeatureRow.NativeBoundaryContracts));
            Assert.That(tables.Features.TryGetFeature(
                TrRmFeatureTableReader.RootlessNativeValuesFeatureId,
                out var rootlessValues), Is.True);
            Assert.That(rootlessValues,
                Is.EqualTo(TrRmFeatureRow.RootlessNativeValues));
        }
        else
        {
            var tables = TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(
                        artifact));
            Assert.That(tables.Features.TryGetFeature(
                TrRmFeatureTableReader.NativeBoundaryContractsFeatureId,
                out var nativeBoundary), Is.True);
            Assert.That(nativeBoundary,
                Is.EqualTo(TrRmFeatureRow.NativeBoundaryContracts));
            Assert.That(tables.Features.TryGetFeature(
                TrRmFeatureTableReader.RootlessNativeValuesFeatureId,
                out var rootlessValues), Is.True);
            Assert.That(rootlessValues,
                Is.EqualTo(TrRmFeatureRow.RootlessNativeValues));
        }
        var input = new TrRmStandardArtifactInput(schemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            "standard-pointer-cell-init", []);
        var publish = loaded.Functions.Single(static function =>
            function.Name == "Publish");
        var pointerType = publish.Variables[1].Type;
        Assert.That(NativePointerTypes.IsType(pointerType), Is.True);
        var outputType = publish.Variables[0].Type;
        var output = loaded.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(outputType)];
        Assert.That(output.IsInitialization, Is.True);
        Assert.That(output.ReferentType, Is.EqualTo(pointerType));
        Assert.That(loaded.NativePointerTypes[
            NativePointerTypes.Index(pointerType)].PointeeType,
            Is.EqualTo(PrimitiveType.Int32));
        Assert.That(publish.RequiresUnsafeCaller, Is.True);
        Assert.That(publish.Body.Statements
            .OfType<BoundExpressionStatement>().Single().Expression,
            Is.TypeOf<BoundReferenceWrite>());

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            "standard-pointer-cell-init-reemitted", schemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            "standard-pointer-cell-init-reemitted", []);
        var reloadedPublish = reloaded.Functions.Single(static function =>
            function.Name == "Publish");
        Assert.That(reloadedPublish.Variables[0].IsInit, Is.True);
        Assert.That(reloadedPublish.Variables[0].Type,
            Is.EqualTo(outputType));
        Assert.That(reloadedPublish.Variables[1].Type,
            Is.EqualTo(pointerType));
    }

    [TestCase(PointerLoadsSchemaIdentity)]
    [TestCase(GeneralPredicateGraphsPointerLoadsSchemaIdentity)]
    public void StandardNativePointerLoadsRoundTripUnderExactSuccessors(
        string schemaIdentity)
    {
        var highFormalParameters = string.Join(", ",
            Enumerable.Range(0, 16).Select(static index =>
                $"int32 ignored{index}"));
        var source = $$"""
            public unsafe int32 ReadInt32(ptr<int32> value) {
                unsafe { return *value; }
            }

            public unsafe bool ReadBool(ptr<bool> value) {
                unsafe { return *value; }
            }

            public unsafe char8 ReadChar8(ptr<char8> value) {
                unsafe { return *value; }
            }

            public unsafe char32 ReadChar32(ptr<char32> value) {
                unsafe { return *value; }
            }

            public unsafe rune ReadRune(ptr<rune> value) {
                unsafe { return *value; }
            }

            public unsafe int32 ReadInt8(ptr<int8> value) {
                unsafe { return int32(*value); }
            }

            public unsafe int32 ReadInt16(ptr<int16> value) {
                unsafe { return int32(*value); }
            }

            public unsafe uint32 ReadUInt8(ptr<uint8> value) {
                unsafe { return uint32(*value); }
            }

            public unsafe uint32 ReadUInt16(ptr<uint16> value) {
                unsafe { return uint32(*value); }
            }

            public unsafe uint32 ReadUInt32(ptr<uint32> value) {
                unsafe { return *value; }
            }

            public unsafe ptr<int32> ReadPointer(ptr<ptr<int32>> value) {
                unsafe { return *value; }
            }

            public unsafe int32 ReadWritablePointer(write ptr<int32> value) {
                unsafe { return *value; }
            }

            public unsafe int32 ReadReboundPointer(ptr<int32> first,
                int32 ignored, ptr<int32> second) {
                mut ptr<int32> current = first;
                current = SelectPointer(ignored, second);
                unsafe { return *current; }
            }

            ptr<int32> SelectPointer(int32 ignored, ptr<int32> value) {
                return value;
            }

            public unsafe int32 ReadSelectedPointer(ptr<int32> value,
                int32 ignored) {
                unsafe { return *SelectPointer(ignored, value); }
            }

            public unsafe uint64 ReadUInt64(ptr<uint64> value) {
                unsafe { return *value; }
            }

            public unsafe int64 ReadInt64(ptr<int64> value) {
                unsafe { return *value; }
            }

            public unsafe nint ReadNativeInt(ptr<nint> value) {
                unsafe { return *value; }
            }

            public unsafe nuint ReadNativeUInt(ptr<nuint> value) {
                unsafe { return *value; }
            }

            public unsafe float32 ReadFloat32(ptr<float32> value) {
                unsafe { return *value; }
            }

            public unsafe float64 ReadFloat64(ptr<float64> value) {
                unsafe { return *value; }
            }

            public unsafe int32 ReadHighFormal({{highFormalParameters}},
                ptr<int32> value) {
                unsafe { return *value; }
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, "standard-pointer-load", schemaIdentity, 1);
        var index = StringComparer.Ordinal.Equals(schemaIdentity,
                PointerLoadsSchemaIdentity)
            ? TrRmArtifactIndex.ForNativePointerLoadsDraft(artifact)
            : TrRmArtifactIndex
                .ForGeneralPredicateGraphsNativePointerLoadsDraft(artifact);
        var tables = StringComparer.Ordinal.Equals(schemaIdentity,
                PointerLoadsSchemaIdentity)
            ? TrRmStructuralMetadataValidator
                .ValidateAndGetExecutionTables(index)
            : TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    index);
        Assert.That(tables.Features.TryGetFeature(
            TrRmFeatureTableReader.NativeBoundaryContractsFeatureId,
            out _), Is.True);
        Assert.That(tables.Features.TryGetFeature(
            TrRmFeatureTableReader.RootlessNativeValuesFeatureId,
            out _), Is.True);
        foreach (var methodName in new[] {
            "ReadInt32", "ReadBool", "ReadChar8", "ReadChar32", "ReadRune",
            "ReadInt8", "ReadInt16", "ReadUInt8", "ReadUInt16", "ReadUInt32",
            "ReadPointer", "ReadWritablePointer", "ReadReboundPointer",
            "ReadSelectedPointer",
            "ReadInt64", "ReadUInt64", "ReadNativeInt", "ReadNativeUInt",
            "ReadFloat32", "ReadFloat64", "ReadHighFormal",
        })
        {
            var methodRow = 0;
            for (var row = 1; row <= tables.Methods.Count; row++)
            {
                if (tables.Methods.GetName(row) == methodName)
                {
                    methodRow = row;
                    break;
                }
            }
            Assert.That(methodRow, Is.Not.Zero);
            var bodyRow = tables.Methods.GetBodyRow(methodRow);
            Assert.That(tables.Bodies.GetRow(bodyRow)[0], Is.EqualTo(0x01));
            var body = tables.Bodies.GetRegisterBody(bodyRow,
                tables.Contracts.Count,
                generalPredicateGraphsInitializationReferenceBodies:
                    StringComparer.Ordinal.Equals(schemaIdentity,
                        GeneralPredicateGraphsPointerLoadsSchemaIdentity));
            var code = body.GetBlockCode(0);
            var containsPointerLoad = false;
            var containsLongHomeLoad = false;
            for (var position = 0; position < code.Length;)
            {
                var opcode = code[position];
                containsPointerLoad |= opcode == 0x1B;
                containsLongHomeLoad |= opcode == 0xD5;
                position += TrRmRegisterBodyReader.InstructionLength(opcode,
                    body.Schema, body.UsesNativePointerLoads);
            }
            Assert.That(containsPointerLoad, Is.True, methodName);
            if (methodName == "ReadHighFormal")
            {
                Assert.That(containsLongHomeLoad, Is.True);
            }
        }
        var input = new TrRmStandardArtifactInput(schemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            "standard-pointer-load", []);
        AssertStandardNativePointerLoad(loaded, "ReadInt32",
            PrimitiveType.Int32);
        AssertStandardNativePointerLoad(loaded, "ReadBool",
            PrimitiveType.Bool);
        AssertStandardNativePointerLoad(loaded, "ReadChar8",
            PrimitiveType.Char8);
        AssertStandardNativePointerLoad(loaded, "ReadChar32",
            PrimitiveType.Char32);
        AssertStandardNativePointerLoad(loaded, "ReadRune",
            PrimitiveType.Rune);
        AssertStandardNativePointerLoad(loaded, "ReadInt8",
            PrimitiveType.Int8);
        AssertStandardNativePointerLoad(loaded, "ReadInt16",
            PrimitiveType.Int16);
        AssertStandardNativePointerLoad(loaded, "ReadUInt8",
            PrimitiveType.UInt8);
        AssertStandardNativePointerLoad(loaded, "ReadUInt16",
            PrimitiveType.UInt16);
        AssertStandardNativePointerLoad(loaded, "ReadUInt32",
            PrimitiveType.UInt32);
        AssertStandardNativePointerLoad(loaded, "ReadPointer",
            loaded.NativePointerTypes[NativePointerTypes.Index(
                loaded.Functions.Single(static function =>
                    function.Name == "ReadPointer").Variables[0].Type)]
                .PointeeType);
        AssertStandardNativePointerLoad(loaded, "ReadWritablePointer",
            PrimitiveType.Int32);
        AssertStandardNativePointerLoad(loaded, "ReadReboundPointer",
            PrimitiveType.Int32);
        AssertStandardNativePointerLoad(loaded, "ReadSelectedPointer",
            PrimitiveType.Int32, addressIsCall: true);
        AssertStandardNativePointerLoad(loaded, "ReadInt64",
            PrimitiveType.Int64);
        AssertStandardNativePointerLoad(loaded, "ReadUInt64",
            PrimitiveType.UInt64);
        AssertStandardNativePointerLoad(loaded, "ReadNativeInt",
            PrimitiveType.NativeInt);
        AssertStandardNativePointerLoad(loaded, "ReadNativeUInt",
            PrimitiveType.NativeUInt);
        AssertStandardNativePointerLoad(loaded, "ReadFloat32",
            PrimitiveType.Float32);
        AssertStandardNativePointerLoad(loaded, "ReadFloat64",
            PrimitiveType.Float64);
        AssertStandardNativePointerLoad(loaded, "ReadHighFormal",
            PrimitiveType.Int32);

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            "standard-pointer-load-reemitted", schemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            "standard-pointer-load-reemitted", []);
        AssertStandardNativePointerLoad(reloaded, "ReadInt32",
            PrimitiveType.Int32);
        AssertStandardNativePointerLoad(reloaded, "ReadBool",
            PrimitiveType.Bool);
        AssertStandardNativePointerLoad(reloaded, "ReadChar8",
            PrimitiveType.Char8);
        AssertStandardNativePointerLoad(reloaded, "ReadChar32",
            PrimitiveType.Char32);
        AssertStandardNativePointerLoad(reloaded, "ReadRune",
            PrimitiveType.Rune);
        AssertStandardNativePointerLoad(reloaded, "ReadInt8",
            PrimitiveType.Int8);
        AssertStandardNativePointerLoad(reloaded, "ReadInt16",
            PrimitiveType.Int16);
        AssertStandardNativePointerLoad(reloaded, "ReadUInt8",
            PrimitiveType.UInt8);
        AssertStandardNativePointerLoad(reloaded, "ReadUInt16",
            PrimitiveType.UInt16);
        AssertStandardNativePointerLoad(reloaded, "ReadUInt32",
            PrimitiveType.UInt32);
        AssertStandardNativePointerLoad(reloaded, "ReadPointer",
            reloaded.NativePointerTypes[NativePointerTypes.Index(
                reloaded.Functions.Single(static function =>
                    function.Name == "ReadPointer").Variables[0].Type)]
                .PointeeType);
        AssertStandardNativePointerLoad(reloaded, "ReadWritablePointer",
            PrimitiveType.Int32);
        AssertStandardNativePointerLoad(reloaded, "ReadReboundPointer",
            PrimitiveType.Int32);
        AssertStandardNativePointerLoad(reloaded, "ReadSelectedPointer",
            PrimitiveType.Int32, addressIsCall: true);
        AssertStandardNativePointerLoad(reloaded, "ReadInt64",
            PrimitiveType.Int64);
        AssertStandardNativePointerLoad(reloaded, "ReadUInt64",
            PrimitiveType.UInt64);
        AssertStandardNativePointerLoad(reloaded, "ReadNativeInt",
            PrimitiveType.NativeInt);
        AssertStandardNativePointerLoad(reloaded, "ReadNativeUInt",
            PrimitiveType.NativeUInt);
        AssertStandardNativePointerLoad(reloaded, "ReadFloat32",
            PrimitiveType.Float32);
        AssertStandardNativePointerLoad(reloaded, "ReadFloat64",
            PrimitiveType.Float64);
        AssertStandardNativePointerLoad(reloaded, "ReadHighFormal",
            PrimitiveType.Int32);

        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity, artifact),
                "standard-pointer-load-unselected", []));
    }

    [Test]
    public void StandardNativePointerLoadsRequireLexicalUnsafeScope()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public unsafe int32 ReadInt32(ptr<int32> value) {
                return *value;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [TestCase(PointerLoadsSchemaIdentity, 0xE1)]
    [TestCase(PointerLoadsSchemaIdentity, 0x1E)]
    [TestCase(GeneralPredicateGraphsPointerLoadsSchemaIdentity, 0xE1)]
    [TestCase(GeneralPredicateGraphsPointerLoadsSchemaIdentity, 0x1E)]
    public void StandardNativePointerLoadsRejectReservedRegisterOperands(
        string schemaIdentity, int invalidOperand)
    {
        const string source = """
            public unsafe int32 ReadInt32(ptr<int32> value) {
                unsafe { return *value; }
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, "standard-pointer-load-invalid-register",
            schemaIdentity, 1);
        var index = StringComparer.Ordinal.Equals(schemaIdentity,
                PointerLoadsSchemaIdentity)
            ? TrRmArtifactIndex.ForNativePointerLoadsDraft(artifact)
            : TrRmArtifactIndex
                .ForGeneralPredicateGraphsNativePointerLoadsDraft(artifact);
        var tables = StringComparer.Ordinal.Equals(schemaIdentity,
                PointerLoadsSchemaIdentity)
            ? TrRmStructuralMetadataValidator
                .ValidateAndGetExecutionTables(index)
            : TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    index);
        var methodRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "ReadInt32")
            {
                methodRow = row;
                break;
            }
        }
        Assert.That(methodRow, Is.Not.Zero);
        var malformed = ReplaceStandardBodyOperand(artifact, schemaIdentity,
            tables.Methods.GetBodyRow(methodRow), 0x1B,
            (byte)invalidOperand);

        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity, malformed),
                "standard-pointer-load-invalid-register", []));
    }

    [TestCase(PointerLoadsSchemaIdentity)]
    [TestCase(GeneralPredicateGraphsPointerLoadsSchemaIdentity)]
    public void StandardNativePointerLoadsRejectFormalHomeStores(
        string schemaIdentity)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public unsafe int32 Read(ptr<int32> value) {
                ptr<int32> local = value;
                unsafe { return *local; }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, "standard-pointer-load-invalid-home",
            schemaIdentity, 1);
        var index = StringComparer.Ordinal.Equals(schemaIdentity,
                PointerLoadsSchemaIdentity)
            ? TrRmArtifactIndex.ForNativePointerLoadsDraft(artifact)
            : TrRmArtifactIndex
                .ForGeneralPredicateGraphsNativePointerLoadsDraft(artifact);
        var tables = StringComparer.Ordinal.Equals(schemaIdentity,
                PointerLoadsSchemaIdentity)
            ? TrRmStructuralMetadataValidator
                .ValidateAndGetExecutionTables(index)
            : TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    index);
        var methodRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Read")
            {
                methodRow = row;
                break;
            }
        }
        Assert.That(methodRow, Is.Not.Zero);
        var malformed = ReplaceStandardBodyOperand(artifact, schemaIdentity,
            tables.Methods.GetBodyRow(methodRow), 0x14, 0x00);

        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity, malformed),
                "standard-pointer-load-invalid-home", []));
    }

    [TestCase(PointerLoadsSchemaIdentity)]
    [TestCase(GeneralPredicateGraphsPointerLoadsSchemaIdentity)]
    public async Task StandardNativePointerLoadsExecuteInNativeLibraryAsync(
        string schemaIdentity)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string source = """
            public unsafe int32 ReadInt32(ptr<int32> value) {
                unsafe { return *value; }
            }

            public unsafe bool ReadBool(ptr<bool> value) {
                unsafe { return *value; }
            }

            public unsafe char8 ReadChar8(ptr<char8> value) {
                unsafe { return *value; }
            }

            public unsafe char32 ReadChar32(ptr<char32> value) {
                unsafe { return *value; }
            }

            public unsafe rune ReadRune(ptr<rune> value) {
                unsafe { return *value; }
            }

            public unsafe int32 ReadInt8(ptr<int8> value) {
                unsafe { return int32(*value); }
            }

            public unsafe int32 ReadInt16(ptr<int16> value) {
                unsafe { return int32(*value); }
            }

            public unsafe uint32 ReadUInt8(ptr<uint8> value) {
                unsafe { return uint32(*value); }
            }

            public unsafe uint32 ReadUInt16(ptr<uint16> value) {
                unsafe { return uint32(*value); }
            }

            public unsafe uint32 ReadUInt32(ptr<uint32> value) {
                unsafe { return *value; }
            }

            public unsafe ptr<int32> ReadPointer(ptr<ptr<int32>> value) {
                unsafe { return *value; }
            }

            public unsafe int32 ReadWritablePointer(write ptr<int32> value) {
                unsafe { return *value; }
            }

            public unsafe int32 ReadReboundPointer(ptr<int32> first,
                int32 ignored, ptr<int32> second) {
                mut ptr<int32> current = first;
                current = SelectPointer(ignored, second);
                unsafe { return *current; }
            }

            ptr<int32> SelectPointer(int32 ignored, ptr<int32> value) {
                return value;
            }

            public unsafe int32 ReadSelectedPointer(ptr<int32> value,
                int32 ignored) {
                unsafe { return *SelectPointer(ignored, value); }
            }

            public unsafe uint64 ReadUInt64(ptr<uint64> value) {
                unsafe { return *value; }
            }

            public unsafe int64 ReadInt64(ptr<int64> value) {
                unsafe { return *value; }
            }

            public unsafe nint ReadNativeInt(ptr<nint> value) {
                unsafe { return *value; }
            }

            public unsafe nuint ReadNativeUInt(ptr<nuint> value) {
                unsafe { return *value; }
            }

            public unsafe float32 ReadFloat32(ptr<float32> value) {
                unsafe { return *value; }
            }

            public unsafe float64 ReadFloat64(ptr<float64> value) {
                unsafe { return *value; }
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, "standard-pointer-load-execution",
            schemaIdentity, 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            "standard-pointer-load-execution", []);
        var image = NativeEmitter.EmitLibrary(loaded,
            ["ReadInt32", "ReadBool", "ReadChar8", "ReadChar32", "ReadRune",
                "ReadInt8", "ReadInt16", "ReadUInt8", "ReadUInt16",
                "ReadUInt32", "ReadPointer", "ReadWritablePointer",
                "ReadReboundPointer", "ReadSelectedPointer",
                "ReadInt64", "ReadUInt64", "ReadNativeInt", "ReadNativeUInt",
                "ReadFloat32", "ReadFloat64"],
            NativeRuntimePolicy.FailFast);
        var libraryPath = Path.Combine(Path.GetTempPath(),
            $"terrum-standard-pointer-load-{Guid.NewGuid():N}.dll");
        nint library = 0;
        var backing = Marshal.AllocHGlobal(sizeof(int));
        var secondBacking = Marshal.AllocHGlobal(sizeof(int));
        var pointerCell = Marshal.AllocHGlobal(IntPtr.Size);
        var wideBacking = Marshal.AllocHGlobal(sizeof(long));
        try
        {
            Marshal.WriteInt32(backing, 42);
            Marshal.WriteInt32(secondBacking, 99);
            Marshal.WriteIntPtr(pointerCell, backing);
            await File.WriteAllBytesAsync(libraryPath, image)
                .ConfigureAwait(false);
            library = NativeLibrary.Load(libraryPath);
            var readInt32 = Marshal.GetDelegateForFunctionPointer<
                PointerReadFunction>(NativeLibrary.GetExport(library,
                "ReadInt32"));
            var readBool = Marshal.GetDelegateForFunctionPointer<
                PointerReadByteFunction>(NativeLibrary.GetExport(library,
                "ReadBool"));
            var readChar8 = Marshal.GetDelegateForFunctionPointer<
                PointerReadByteFunction>(NativeLibrary.GetExport(library,
                "ReadChar8"));
            var readChar32 = Marshal.GetDelegateForFunctionPointer<
                PointerReadUInt32Function>(NativeLibrary.GetExport(library,
                "ReadChar32"));
            var readRune = Marshal.GetDelegateForFunctionPointer<
                PointerReadUInt32Function>(NativeLibrary.GetExport(library,
                "ReadRune"));
            var readInt8 = Marshal.GetDelegateForFunctionPointer<
                PointerReadFunction>(NativeLibrary.GetExport(library,
                "ReadInt8"));
            var readInt16 = Marshal.GetDelegateForFunctionPointer<
                PointerReadFunction>(NativeLibrary.GetExport(library,
                "ReadInt16"));
            var readUInt8 = Marshal.GetDelegateForFunctionPointer<
                PointerReadUInt32Function>(NativeLibrary.GetExport(library,
                "ReadUInt8"));
            var readUInt16 = Marshal.GetDelegateForFunctionPointer<
                PointerReadUInt32Function>(NativeLibrary.GetExport(library,
                "ReadUInt16"));
            var readUInt32 = Marshal.GetDelegateForFunctionPointer<
                PointerReadUInt32Function>(NativeLibrary.GetExport(library,
                "ReadUInt32"));
            var readPointer = Marshal.GetDelegateForFunctionPointer<
                PointerFunction>(NativeLibrary.GetExport(library,
                "ReadPointer"));
            var readWritablePointer = Marshal.GetDelegateForFunctionPointer<
                PointerReadFunction>(NativeLibrary.GetExport(library,
                "ReadWritablePointer"));
            var readReboundPointer = Marshal.GetDelegateForFunctionPointer<
                PointerReadWithTwoPointersFunction>(NativeLibrary.GetExport(
                library, "ReadReboundPointer"));
            var readSelectedPointer = Marshal.GetDelegateForFunctionPointer<
                PointerReadWithArgumentFunction>(NativeLibrary.GetExport(
                library, "ReadSelectedPointer"));
            var readUInt64 = Marshal.GetDelegateForFunctionPointer<
                PointerReadUInt64Function>(NativeLibrary.GetExport(library,
                "ReadUInt64"));
            var readInt64 = Marshal.GetDelegateForFunctionPointer<
                PointerReadInt64Function>(NativeLibrary.GetExport(library,
                "ReadInt64"));
            var readNativeInt = Marshal.GetDelegateForFunctionPointer<
                PointerFunction>(NativeLibrary.GetExport(library,
                "ReadNativeInt"));
            var readNativeUInt = Marshal.GetDelegateForFunctionPointer<
                PointerReadNativeUIntFunction>(NativeLibrary.GetExport(library,
                "ReadNativeUInt"));
            var readFloat32 = Marshal.GetDelegateForFunctionPointer<
                PointerReadFloat32Function>(NativeLibrary.GetExport(library,
                "ReadFloat32"));
            var readFloat64 = Marshal.GetDelegateForFunctionPointer<
                PointerReadFloat64Function>(NativeLibrary.GetExport(library,
                "ReadFloat64"));
            Assert.That(readInt32(backing), Is.EqualTo(42));
            Marshal.WriteByte(backing, 2);
            Assert.That(readBool(backing), Is.EqualTo(2));
            Marshal.WriteByte(backing, byte.MaxValue);
            Assert.That(readChar8(backing), Is.EqualTo(byte.MaxValue));
            Marshal.WriteInt32(backing, -1);
            Assert.That(readChar32(backing), Is.EqualTo(uint.MaxValue));
            Marshal.WriteInt32(backing, 0xD800);
            Assert.That(readRune(backing), Is.EqualTo(0xD800));
            foreach (var (stored, expected) in new (byte Stored, int Expected)[] {
                (0x80, sbyte.MinValue), (0xFF, -1),
            })
            {
                Marshal.WriteByte(backing, stored);
                Assert.That(readInt8(backing), Is.EqualTo(expected));
            }
            foreach (var (stored, expected) in new (short Stored, int Expected)[] {
                (short.MinValue, short.MinValue), (-1, -1),
            })
            {
                Marshal.WriteInt16(backing, stored);
                Assert.That(readInt16(backing), Is.EqualTo(expected));
            }
            Marshal.WriteByte(backing, byte.MaxValue);
            Assert.That(readUInt8(backing), Is.EqualTo((uint)byte.MaxValue));
            Marshal.WriteInt16(backing, -1);
            Assert.That(readUInt16(backing), Is.EqualTo((uint)ushort.MaxValue));
            Marshal.WriteInt32(backing, -1);
            Assert.That(readUInt32(backing), Is.EqualTo(uint.MaxValue));
            Assert.That(readPointer(pointerCell), Is.EqualTo(backing));
            Marshal.WriteInt32(backing, 42);
            Assert.That(readWritablePointer(backing), Is.EqualTo(42));
            Assert.That(readReboundPointer(backing, 17, secondBacking),
                Is.EqualTo(99));
            Assert.That(Marshal.ReadInt32(backing), Is.EqualTo(42));
            Assert.That(Marshal.ReadInt32(secondBacking), Is.EqualTo(99));
            Assert.That(readSelectedPointer(backing, 17), Is.EqualTo(42));
            const long expectedInt64 = long.MinValue;
            Marshal.WriteInt64(wideBacking, expectedInt64);
            Assert.That(readInt64(wideBacking), Is.EqualTo(expectedInt64));
            const ulong expectedUInt64 = 0xFEDC_BA98_7654_3210;
            Marshal.WriteInt64(wideBacking,
                unchecked((long)expectedUInt64));
            Assert.That(readUInt64(wideBacking), Is.EqualTo(expectedUInt64));
            Assert.That(readNativeInt(wideBacking),
                Is.EqualTo(unchecked((nint)expectedUInt64)));
            Assert.That(readNativeUInt(wideBacking),
                Is.EqualTo(unchecked((nuint)expectedUInt64)));
            foreach (var expectedFloatBits in new[] {
                int.MinValue, unchecked((int)0x7FC0_1234),
            })
            {
                Marshal.WriteInt32(backing, expectedFloatBits);
                Assert.That(BitConverter.SingleToInt32Bits(
                    readFloat32(backing)),
                    Is.EqualTo(expectedFloatBits));
            }
            foreach (var expectedFloatBits in new long[] {
                long.MinValue, unchecked((long)0x7FF8_0000_0000_1234UL),
            })
            {
                Marshal.WriteInt64(wideBacking, expectedFloatBits);
                Assert.That(BitConverter.DoubleToInt64Bits(
                    readFloat64(wideBacking)),
                    Is.EqualTo(expectedFloatBits));
            }
        }
        finally
        {
            if (library != 0)
            {
                NativeLibrary.Free(library);
            }

            Marshal.FreeHGlobal(pointerCell);
            Marshal.FreeHGlobal(backing);
            Marshal.FreeHGlobal(secondBacking);
            Marshal.FreeHGlobal(wideBacking);
            await TestFileCleanup.DeleteWithRetryAsync(libraryPath)
                .ConfigureAwait(false);
        }
    }

    [TestCase(PointerLoadsNativeParameterSchemaIdentity)]
    [TestCase(GeneralPredicateGraphsPointerLoadsNativeParameterSchemaIdentity)]
    public async Task StandardNativePointerLoadsPreserveNativeWriteOrderAsync(
        string schemaIdentity)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(StandardNativeWriteOrderSource), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, "standard-pointer-load-write-order",
            schemaIdentity, 1);
        AssertStandardNativePointerLoadOrderArtifact(artifact, schemaIdentity);

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            "standard-pointer-load-write-order", []);
        var loadedImport = loaded.Functions.Single(static function =>
            function.Name == "WriteProcessMemory").DirectNativeImport;
        Assert.That(loadedImport, Is.Not.Null);
        AssertWriteProcessMemoryParameters(loadedImport!);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            "standard-pointer-load-write-order-reemitted", schemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            "standard-pointer-load-write-order-reemitted", []);
        var reloadedImport = reloaded.Functions.Single(static function =>
            function.Name == "WriteProcessMemory").DirectNativeImport;
        Assert.That(reloadedImport, Is.Not.Null);
        AssertWriteProcessMemoryParameters(reloadedImport!);
        var image = NativeEmitter.EmitLibraryWithNativeImports(reloaded,
            ["ReadBeforeWrite", "ReadAfterWrite"],
            NativeRuntimePolicy.FailFast);
        var libraryPath = Path.Combine(Path.GetTempPath(),
            $"terrum-standard-pointer-load-order-{Guid.NewGuid():N}.dll");
        nint library = 0;
        var backing = Marshal.AllocHGlobal(sizeof(int));
        var replacementBacking = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            await File.WriteAllBytesAsync(libraryPath, image)
                .ConfigureAwait(false);
            library = NativeLibrary.Load(libraryPath);
            var readBeforeWrite = Marshal.GetDelegateForFunctionPointer<
                PointerWriteOrderFunction>(NativeLibrary.GetExport(
                library, "ReadBeforeWrite"));
            var readAfterWrite = Marshal.GetDelegateForFunctionPointer<
                PointerWriteOrderFunction>(NativeLibrary.GetExport(
                library, "ReadAfterWrite"));

            Marshal.WriteInt32(backing, 42);
            Marshal.WriteInt32(replacementBacking, 99);
            Assert.That(readBeforeWrite(backing, replacementBacking,
                nint.Zero, (nuint)sizeof(int)), Is.EqualTo(42));
            Assert.That(Marshal.ReadInt32(backing), Is.EqualTo(99));

            Marshal.WriteInt32(backing, 42);
            Assert.That(readAfterWrite(backing, replacementBacking,
                nint.Zero, (nuint)sizeof(int)), Is.EqualTo(99));
            Assert.That(Marshal.ReadInt32(backing), Is.EqualTo(99));
        }
        finally
        {
            if (library != 0)
            {
                NativeLibrary.Free(library);
            }

            Marshal.FreeHGlobal(backing);
            Marshal.FreeHGlobal(replacementBacking);
            await TestFileCleanup.DeleteWithRetryAsync(libraryPath)
                .ConfigureAwait(false);
        }
    }

    [TestCase(PointerLoadsNativeParameterSchemaIdentity)]
    [TestCase(GeneralPredicateGraphsPointerLoadsNativeParameterSchemaIdentity)]
    public async Task StandardNativePointerLoadsBindAndReemitNativeImportProviderAsync(
        string schemaIdentity)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string providerIdentity = "pointer-load-native-provider";
        const string consumerIdentity = "pointer-load-native-consumer";
        var coreNativeRegistration
            = NativeCoreRegistrationTests.CreateRegistration();

        TrRmStandardArtifactInput CreateProvider(bool includeParameters,
            uint extent = 1, string libraryName = "kernel32.dll")
        {
            var nativeParameterAttributes = includeParameters
                ? $$"""
                    [Terrum.Interop.NativeParameter(2, Write, NoEscape, {{extent}})]
                    [Terrum.Interop.NativeParameter(3, Read, NoEscape, 1)]
                    """
                : "";
            var source = $$"""
                [Terrum.Interop.NativeImport("{{libraryName}}",
                    "WriteProcessMemory")]
                {{nativeParameterAttributes}}
                public extern unsafe int32 WriteProcessMemory(
                    ptr<void> process, write ptr<int32> destination,
                    ptr<int32> source, nuint size, ptr<void> writtenCount);
                """;
            var compilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(source), new CompilationOptions {
                    NativeCoreRegistration = coreNativeRegistration,
                });
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            var artifact = TrRmExecutableCompiler.EmitStandard(
                compilation.Program!, providerIdentity, schemaIdentity, 1);
            return new TrRmStandardArtifactInput(schemaIdentity, artifact);
        }

        var providerInput = CreateProvider(includeParameters: true);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, []);
        var importedExpectation = providerReference.Functions.Single(
            static function => function.Name == "WriteProcessMemory");
        Assert.That(importedExpectation.DirectNativeImport!.Parameters,
            Is.EqualTo([
                new DirectNativeParameter(1, PrimitiveType.Int32,
                    NativeParameterAccess.Write,
                    NativeParameterRetention.NoEscape, 1),
                new DirectNativeParameter(2, PrimitiveType.Int32,
                    NativeParameterAccess.Read,
                    NativeParameterRetention.NoEscape, 1),
            ]));

        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(StandardNativeWriteOrderConsumerSource),
            providerReference,
            new CompilationOptions {
                NativeCoreRegistration = coreNativeRegistration,
            });
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerCompilation.Program!, consumerIdentity, schemaIdentity, 1);
        var consumerInput = new TrRmStandardArtifactInput(schemaIdentity,
            consumerArtifact);
        var loadedConsumer = TrRmExecutableCompiler.LoadStandard(
            consumerInput, consumerIdentity, [providerInput]);
        var closure = loadedConsumer.StandardMethodProviderClosure
            ?? throw new InvalidOperationException(
                "The pointer-load provider binding must retain its method closure.");
        Assert.That(closure.MethodEdges, Has.Length.EqualTo(1));
        var providerMethod = loadedConsumer.Functions.Single(function =>
            function.DirectNativeImport is {
                ExportName: "WriteProcessMemory",
            });
        Assert.That(providerMethod.IsExternal, Is.True);

        var reemittedConsumer = TrRmExecutableCompiler.EmitStandard(
            loadedConsumer, consumerIdentity, schemaIdentity, 1);
        var reemittedConsumerInput = new TrRmStandardArtifactInput(
            schemaIdentity, reemittedConsumer);
        var loadedProvider = TrRmExecutableCompiler.LoadStandard(
            providerInput, providerIdentity, []);
        var reemittedProvider = TrRmExecutableCompiler.EmitStandard(
            loadedProvider, providerIdentity, schemaIdentity, 1);
        var reemittedProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, reemittedProvider);
        var reloadedConsumer = TrRmExecutableCompiler.LoadStandard(
            reemittedConsumerInput, consumerIdentity,
            [reemittedProviderInput]);

        var bindingPlan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            reemittedConsumerInput, consumerIdentity,
            [reemittedProviderInput],
            TrRmStandardNativeOperation.EmitLibrary,
            NativeRuntimePolicy.FailFast);
        var (loweredProgram, nativeImports) = bindingPlan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitLibrary);
        Assert.That(nativeImports.Any(static binding =>
            binding.DllName == "kernel32.dll"
                && binding.ExportName == "WriteProcessMemory"), Is.True);
        var image = NativeEmitter.EmitLibraryWithNativeImports(
            loweredProgram, ["ReadBeforeWrite", "ReadAfterWrite"],
            NativeRuntimePolicy.FailFast);
        var libraryPath = Path.Combine(Path.GetTempPath(),
            $"terrum-standard-pointer-load-provider-{Guid.NewGuid():N}.dll");
        nint library = 0;
        var backing = Marshal.AllocHGlobal(sizeof(int));
        var replacementBacking = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            await File.WriteAllBytesAsync(libraryPath, image)
                .ConfigureAwait(false);
            library = NativeLibrary.Load(libraryPath);
            var readBeforeWrite = Marshal.GetDelegateForFunctionPointer<
                PointerWriteOrderFunction>(NativeLibrary.GetExport(
                library, "ReadBeforeWrite"));
            var readAfterWrite = Marshal.GetDelegateForFunctionPointer<
                PointerWriteOrderFunction>(NativeLibrary.GetExport(
                library, "ReadAfterWrite"));

            Marshal.WriteInt32(backing, 42);
            Marshal.WriteInt32(replacementBacking, 99);
            Assert.That(readBeforeWrite(backing, replacementBacking,
                nint.Zero, (nuint)sizeof(int)), Is.EqualTo(42));
            Assert.That(Marshal.ReadInt32(backing), Is.EqualTo(99));

            Marshal.WriteInt32(backing, 42);
            Assert.That(readAfterWrite(backing, replacementBacking,
                nint.Zero, (nuint)sizeof(int)), Is.EqualTo(99));
            Assert.That(Marshal.ReadInt32(backing), Is.EqualTo(99));
        }
        finally
        {
            if (library != 0)
            {
                NativeLibrary.Free(library);
            }

            Marshal.FreeHGlobal(backing);
            Marshal.FreeHGlobal(replacementBacking);
            await TestFileCleanup.DeleteWithRetryAsync(libraryPath)
                .ConfigureAwait(false);
        }

        Assert.That(reloadedConsumer.StandardMethodProviderClosure!
            .MethodEdges, Has.Length.EqualTo(1));
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(consumerInput,
                consumerIdentity, [CreateProvider(includeParameters: false)]));
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(consumerInput,
                consumerIdentity,
                [CreateProvider(includeParameters: true, extent: 2)]));
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(consumerInput,
                consumerIdentity,
                [CreateProvider(includeParameters: true,
                    libraryName: "user32.dll")]));
    }

    [Test]
    public void StandardNativePointerLoadsPreserveGeneralPredicateGraphComposition()
    {
        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var source = StandardNativeWriteOrderSource + """

            public bool Fill(bool publish, bool eligible, int32 value,
                ref<init int32> result) {
                if (publish) {
                    *result = value;
                    return true;
                }
                return false;
            }
            """;
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var expectedPredicate = new BoundOutputStatePredicate(3, 2, 1,
            CreateGeneralOutputStatePredicate(
                GeneralOutputStatePredicateShape.AndThen));
        var functions = program.Functions.Select(function =>
            function.Name == "Fill"
                ? function with {
                    CheckedOutputStatePredicates = [expectedPredicate],
                }
                : function).ToImmutableArray();
        var selectedProgram = program with { Functions = functions };
        var artifact = TrRmExecutableCompiler.EmitStandard(selectedProgram,
            "pointer-load-general-predicate",
            GeneralPredicateGraphsPointerLoadsNativeParameterSchemaIdentity,
            1);
        var index = TrRmArtifactIndex
            .ForInitializationReferenceGeneralPredicatePointerLoadNativeParameterDraft(
                artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateInitializationReferenceGeneralPredicatePointerLoadNativeParameterDraftAndGetExecutionTables(
                index);
        foreach (var featureId in new uint[] { 1, 2, 3, 10, 16, 17 })
        {
            Assert.That(tables.Features.TryGetFeature(featureId, out _),
                Is.True, $"Missing Feature {featureId}/1.");
        }
        Assert.That(tables.Features.TryGetFeature(8, out _), Is.False);

        var input = new TrRmStandardArtifactInput(
            GeneralPredicateGraphsPointerLoadsNativeParameterSchemaIdentity,
            artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            "pointer-load-general-predicate", []);
        Assert.That(loaded.Functions.Single(static function =>
                function.Name == "Fill").CheckedOutputStatePredicates,
            Is.EqualTo([expectedPredicate]));
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            "pointer-load-general-predicate-reemitted",
            GeneralPredicateGraphsPointerLoadsNativeParameterSchemaIdentity,
            1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(
                GeneralPredicateGraphsPointerLoadsNativeParameterSchemaIdentity,
                reemitted),
            "pointer-load-general-predicate-reemitted", []);
        Assert.That(reloaded.Functions.Single(static function =>
                function.Name == "Fill").CheckedOutputStatePredicates,
            Is.EqualTo([expectedPredicate]));
    }

    [TestCase("int32", PrimitiveType.Int32)]
    [TestCase("int128", PrimitiveType.Int128)]
    public void GeneralPredicateGraphProducerPreservesScalarComparisonGuards(
        string scalarTypeName, PrimitiveType scalarType)
    {
        var source = SyntaxTree.Parse(string.Concat(
            "void MaybeFill(", scalarTypeName,
            " value, ref<init int32> destination) ",
            "ensure value <= 0 || (*destination is init) {\n",
            "if (value > 0) { *destination = 42; }\n",
            "}"));
        var compilation = Compilation.CreateLibrary(source,
            CreateDeclarationContractOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var function = ((BoundProgram)compilation.Program!).Functions
            .Single(static candidate => candidate.Name == "MaybeFill");
        var predicate = function.CheckedOutputStatePredicates.Single();
        Assert.That((predicate.FormalOrdinal, predicate.State),
            Is.EqualTo((1, (byte)1)));

        var hasScalarComparison = false;
        _ = BoundTreeTraversal.VisitExpressionTree(predicate.Predicate,
            expression => {
                if (expression is BoundBinary {
                        Operator: TokenKind.LessEqual,
                        Left: BoundRead { Variable: 0 },
                        Right: BoundConstant {
                            Type: var rightType,
                        },
                    } comparison
                    && comparison.Left.Type == scalarType
                    && rightType == scalarType)
                {
                    hasScalarComparison = true;
                }
            });
        Assert.That(hasScalarComparison, Is.True);
    }

    [TestCase("int128", PrimitiveType.Int128)]
    [TestCase("uint128", PrimitiveType.UInt128)]
    [TestCase("float16", PrimitiveType.Float16)]
    [TestCase("bfloat16", PrimitiveType.BFloat16)]
    [TestCase("float128", PrimitiveType.Float128)]
    [TestCase("decimal32", PrimitiveType.Decimal32)]
    [TestCase("decimal64", PrimitiveType.Decimal64)]
    [TestCase("decimal128", PrimitiveType.Decimal128)]
    public void GeneralPredicateGraphRoundTripsUnmappedScalarInputRoots(
        string scalarTypeName, PrimitiveType scalarType)
    {
        var source = SyntaxTree.Parse(string.Concat(
            "void MaybeFill(", scalarTypeName,
            " value, bool write, ref<init int32> destination) {\n",
            "if (write) { *destination = 42; }\n",
            "}"));
        var compilation = Compilation.CreateLibrary(source,
            CreateDeclarationContractOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var function = program.Functions.Single(
            static candidate => candidate.Name == "MaybeFill");
        var equality = new BoundBinary(TokenKind.EqualsEquals,
            new BoundRead(0, scalarType, default),
            new BoundRead(0, scalarType, default), PrimitiveType.Bool,
            default);
        var expectedPredicate = new BoundOutputStatePredicate(2, 0, 1,
            new BoundBinary(TokenKind.AmpersandAmpersand,
                equality,
                new BoundRead(1, PrimitiveType.Bool, default),
                PrimitiveType.Bool, default));
        program = program with {
            Functions = program.Functions.Select(candidate =>
                candidate.Name == "MaybeFill"
                    ? candidate with {
                        CheckedOutputStatePredicates = [expectedPredicate],
                        HasStandardCheckedOutputStateMetadata = true,
                    }
                    : candidate).ToImmutableArray(),
        };

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "general-predicate-wide-scalar",
            GeneralPredicateGraphsSchemaIdentity, 1);
        var input = new TrRmStandardArtifactInput(
            GeneralPredicateGraphsSchemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            "general-predicate-wide-scalar", []);
        var loadedFunction = loaded.Functions.Single(
            static candidate => candidate.Name == "MaybeFill");
        Assert.That(loadedFunction.Variables[0].Type,
            Is.EqualTo(scalarType));
        Assert.That(loadedFunction.CheckedOutputStatePredicates,
            Is.EqualTo([expectedPredicate]));

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            "general-predicate-wide-scalar-reemitted",
            GeneralPredicateGraphsSchemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(
                GeneralPredicateGraphsSchemaIdentity, reemitted),
            "general-predicate-wide-scalar-reemitted", []);
        Assert.That(reloaded.Functions.Single(
                static candidate => candidate.Name == "MaybeFill")
            .CheckedOutputStatePredicates,
            Is.EqualTo([expectedPredicate]));
    }

    [Test]
    public void GeneralPredicateGraphProducerPreservesArithmeticGuards()
    {
        var source = SyntaxTree.Parse("""
            void MaybeFill(int32 value, ref<init int32> destination)
                ensure value + 1 <= 0 || (*destination is init)
            {
                if (value + 1 > 0) {
                    *destination = 42;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(source,
            CreateDeclarationContractOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var function = ((BoundProgram)compilation.Program!).Functions
            .Single(static candidate => candidate.Name == "MaybeFill");
        var predicate = function.CheckedOutputStatePredicates.Single();
        Assert.That((predicate.FormalOrdinal, predicate.State),
            Is.EqualTo((1, (byte)1)));

        var hasAddition = false;
        _ = BoundTreeTraversal.VisitExpressionTree(predicate.Predicate,
            expression => {
                if (expression is BoundBinary {
                        Operator: TokenKind.Plus,
                        Left: BoundRead { Variable: 0 },
                        Right: BoundConstant {
                            Type: PrimitiveType.Int32,
                        },
                    })
                {
                    hasAddition = true;
                }
            });
        Assert.That(hasAddition, Is.True);
    }

    [Test]
    public void GeneralPredicateGraphProducerPreservesTotalConversionGuards()
    {
        var source = SyntaxTree.Parse("""
            void MaybeFill(int32 value, ref<init int32> destination)
                ensure int64(value) <= 0 || (*destination is init)
            {
                if (int64(value) > 0) {
                    *destination = 42;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(source,
            CreateDeclarationContractOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var function = ((BoundProgram)compilation.Program!).Functions
            .Single(static candidate => candidate.Name == "MaybeFill");
        var predicate = function.CheckedOutputStatePredicates.Single();
        var hasTotalConversion = false;
        _ = BoundTreeTraversal.VisitExpressionTree(predicate.Predicate,
            expression => {
                if (expression is BoundCast {
                        Type: PrimitiveType.Int64,
                        Operand: BoundRead {
                            Variable: 0,
                            Type: PrimitiveType.Int32,
                        },
                    })
                {
                    hasTotalConversion = true;
                }
            });
        Assert.That(hasTotalConversion, Is.True);
    }

    [Test]
    public void Construction2EmitsSourcePhasePredicateGraphsWithScopedFailureSelection()
    {
        const string moduleIdentity = "phase-predicate-producer";
        const string source = """
            public int32 Identity(int32 value)
                require value > 0
                ensure return == value
            {
                return value;
            }
            """;
        var requirementIdentity
            = new CoreContractFailureDeclarationIdentity(
                "core.contracts", "requirement-exception");
        var postconditionIdentity
            = new CoreContractFailureDeclarationIdentity(
                "core.contracts", "postcondition-exception");
        var failureSelection = new CoreContractFailureIdentitySelection(
            moduleIdentity, requirementIdentity, postconditionIdentity,
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "require-exception-schema"));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection = failureSelection,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var function = program.Functions.Single(
            static candidate => candidate.Name == "Identity");
        Assert.That(function.DeclarationContractClauses.Select(
                static clause => (clause.Phase, clause.PhaseLocalOrdinal)),
            Is.EqualTo([
                (BoundDeclarationContractPhase.Precondition, 0u),
                (BoundDeclarationContractPhase.NormalPostcondition, 0u),
            ]));
        Assert.That(function.DeclarationContractClauses[0]
            .SelectedFailureClass, Is.EqualTo(requirementIdentity));
        Assert.That(function.DeclarationContractClauses[1]
            .SelectedFailureClass, Is.EqualTo(postconditionIdentity));
        Assert.That(function.DeclarationContractClauses.Select(
                static clause => clause.UsingModuleIdentity),
            Is.All.EqualTo(moduleIdentity));
        Assert.That(function.CheckedOutputStatePredicates.IsDefaultOrEmpty,
            Is.True);

        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, moduleIdentity,
                GeneralPredicateGraphsSchemaIdentity, 1));
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, moduleIdentity,
                C1M1SchemaIdentity, 1,
                out ImmutableArray<CoreContractFailureIdentitySelection> _));
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, moduleIdentity,
                C1M1Construction2SchemaIdentity, 1));

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, C1M1Construction2SchemaIdentity, 1,
            out var emittedFailureSelections);
        Assert.That(emittedFailureSelections, Is.EqualTo([failureSelection]));

        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        var predicates = tables.Contracts.GetPredicateGraphContracts(1);
        Assert.That(predicates.Select(static predicate =>
                (predicate.PhaseTag, predicate.PhaseLocalOrdinal)),
            Is.EqualTo([(1, 0u), (2, 0u)]));
        Assert.That(predicates.Select(static predicate =>
                predicate.PredicateGraphRow).Distinct().Count(),
            Is.EqualTo(2));
        Assert.That(tables.Contracts.GetOutputStateContracts(1), Is.Empty);

        foreach (var predicate in predicates)
        {
            var footprint = tables.PredicateGraphs.GetReadFootprintRow(
                predicate.PredicateGraphRow);
            Assert.That(tables.PredicateReadFootprints
                .GetDependencyRowCount(footprint), Is.Zero);

            var rootNode = tables.PredicateGraphs.GetRootNodeRow(
                predicate.PredicateGraphRow);
            Assert.That(tables.PredicateNodes.GetNodeKind(rootNode),
                Is.EqualTo(0x21u));
            Assert.That(tables.PredicateChildEdges.GetChildCount(rootNode),
                Is.EqualTo(2));
            Assert.That(tables.TypeSpecs.GetTypeSpecRef(
                    tables.PredicateNodes.GetResultTypeSpecRef(rootNode))
                .TypeCode, Is.EqualTo((byte)0x01));

            for (var childOrdinal = 0;
                childOrdinal < tables.PredicateChildEdges.GetChildCount(
                    rootNode); childOrdinal++)
            {
                var childNode = tables.PredicateChildEdges
                    .GetChildNodeRowByOrdinal(rootNode,
                        checked((uint)childOrdinal));
                Assert.That(tables.TypeSpecs.GetTypeSpecRef(
                        tables.PredicateNodes.GetResultTypeSpecRef(
                            childNode)).TypeCode,
                    Is.EqualTo((byte)0x08));
            }
        }

        var phaseOne = predicates.Single(static predicate =>
            predicate.PhaseTag == 1);
        var phaseOneFootprint = tables.PredicateGraphs
            .GetReadFootprintRow(phaseOne.PredicateGraphRow);
        Assert.That(tables.PredicateReadFootprints
            .GetRootRowCount(phaseOneFootprint), Is.EqualTo(1u));
        var phaseOneRoot = tables.PredicateReadFootprints.GetRootFirstRow(
            phaseOneFootprint);
        Assert.That((tables.PredicateReadRoots.GetRootKind(
                checked((int)phaseOneRoot)),
                tables.PredicateReadRoots.GetRootIdentity(
                    checked((int)phaseOneRoot))),
            Is.EqualTo(((byte)2, 0u)));

        var phaseTwo = predicates.Single(static predicate =>
            predicate.PhaseTag == 2);
        var phaseTwoFootprint = tables.PredicateGraphs
            .GetReadFootprintRow(phaseTwo.PredicateGraphRow);
        Assert.That(tables.PredicateReadFootprints
            .GetRootRowCount(phaseTwoFootprint), Is.EqualTo(2u));
        var phaseTwoRootFirst = tables.PredicateReadFootprints
            .GetRootFirstRow(phaseTwoFootprint);
        Assert.That(Enumerable.Range(0, 2).Select(offset =>
                (tables.PredicateReadRoots.GetRootKind(
                    checked((int)phaseTwoRootFirst + offset)),
                    tables.PredicateReadRoots.GetRootIdentity(
                        checked((int)phaseTwoRootFirst + offset)))),
            Is.EquivalentTo([((byte)2, 0u), ((byte)5, 0u)]));
    }

    [Test]
    public void Construction2EmitsPhase2InitializedOutputAsMatchGraph()
    {
        const string moduleIdentity = "phase-output-state-producer";
        const string source = """
            void Fill(ref<init int32> destination)
                ensure *destination is init
            {
                *destination = 1;
            }
            """;
        var failureSelection = new CoreContractFailureIdentitySelection(
            moduleIdentity,
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "requirement-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "postcondition-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "require-exception-schema"));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection = failureSelection,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var function = program.Functions.Single(
            static candidate => candidate.Name == "Fill");
        Assert.That(function.DeclarationContractClauses.Select(
                static clause => (clause.Phase, clause.PhaseLocalOrdinal)),
            Is.EqualTo([
                (BoundDeclarationContractPhase.NormalPostcondition, 0u),
            ]));
        Assert.That(function.CheckedOutputStatePredicates.IsDefaultOrEmpty,
            Is.True);

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, C1M1Construction2SchemaIdentity, 1,
            out var emittedFailureSelections);
        Assert.That(emittedFailureSelections, Is.EqualTo([failureSelection]));

        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        var predicates = tables.Contracts.GetPredicateGraphContracts(1);
        var predicate = predicates.Single();
        Assert.That((predicate.PhaseTag, predicate.PhaseLocalOrdinal),
            Is.EqualTo(((byte)2, 0u)));
        Assert.That(tables.Contracts.GetOutputStateContracts(1), Is.Empty);

        var footprint = tables.PredicateGraphs.GetReadFootprintRow(
            predicate.PredicateGraphRow);
        Assert.That(tables.PredicateReadFootprints
            .GetRootRowCount(footprint), Is.EqualTo(1u));
        var rootRow = checked((int)tables.PredicateReadFootprints
            .GetRootFirstRow(footprint));
        Assert.That((tables.PredicateReadRoots.GetRootKind(rootRow),
                tables.PredicateReadRoots.GetRootIdentity(rootRow)),
            Is.EqualTo(((byte)4, 0u)));

        var matchNode = tables.PredicateGraphs.GetRootNodeRow(
            predicate.PredicateGraphRow);
        Assert.That(tables.PredicateNodes.GetNodeKind(matchNode),
            Is.EqualTo(0x34u));
        Assert.That(tables.PredicateNodes.GetOperandCount(matchNode),
            Is.Zero);
        Assert.That(tables.PredicateChildEdges.GetChildCount(matchNode),
            Is.EqualTo(2));
        Assert.That(tables.TypeSpecs.GetTypeSpecRef(
                tables.PredicateNodes.GetResultTypeSpecRef(matchNode))
            .TypeCode, Is.EqualTo((byte)0x01));

        var subjectNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 0);
        Assert.That(tables.PredicateNodes.GetNodeKind(subjectNode),
            Is.EqualTo(0x10u));
        Assert.That(tables.TypeSpecs.GetTypeSpecRef(
                tables.PredicateNodes.GetResultTypeSpecRef(subjectNode))
            .TypeCode, Is.EqualTo((byte)0x08));
        Assert.That(tables.PredicateNodes.GetOperandCount(subjectNode),
            Is.EqualTo(1u));
        var subjectOperand = checked((int)tables.PredicateNodes
            .GetOperandFirstRow(subjectNode));
        Assert.That(tables.PredicateNodeOperands.GetOperandKind(
            subjectOperand), Is.EqualTo((byte)1));
        Assert.That(tables.PredicateNodeOperands.GetOperandRef(
                subjectOperand), Is.EqualTo(checked((uint)rootRow)));

        var initializedPattern = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 1);
        Assert.That(tables.PredicateNodes.GetNodeKind(initializedPattern),
            Is.EqualTo(0x41u));
        Assert.That(tables.PredicateNodes.GetOperandCount(
            initializedPattern), Is.Zero);
        Assert.That(tables.TypeSpecs.GetTypeSpecRef(
                tables.PredicateNodes.GetResultTypeSpecRef(
                    initializedPattern)).TypeCode,
            Is.EqualTo((byte)0x01));
    }

    [Test]
    public void Construction2EmitsPhase2InitializedValueAsMatchGraph()
    {
        const string moduleIdentity = "phase-value-state-producer";
        const string source = """
            int32 Identity(int32 value)
                ensure value is init
            {
                return value;
            }
            """;
        var failureSelection = new CoreContractFailureIdentitySelection(
            moduleIdentity,
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "requirement-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "postcondition-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "require-exception-schema"));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection = failureSelection,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var function = program.Functions.Single(
            static candidate => candidate.Name == "Identity");
        Assert.That(function.DeclarationContractClauses.Select(
                static clause => (clause.Phase, clause.PhaseLocalOrdinal)),
            Is.EqualTo([
                (BoundDeclarationContractPhase.NormalPostcondition, 0u),
            ]));
        Assert.That(function.CheckedOutputStatePredicates.IsDefaultOrEmpty,
            Is.True);

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, C1M1Construction2SchemaIdentity, 1,
            out var emittedFailureSelections);
        Assert.That(emittedFailureSelections, Is.EqualTo([failureSelection]));

        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        var predicates = tables.Contracts.GetPredicateGraphContracts(1);
        var predicate = predicates.Single();
        Assert.That((predicate.PhaseTag, predicate.PhaseLocalOrdinal),
            Is.EqualTo(((byte)2, 0u)));
        Assert.That(tables.Contracts.GetOutputStateContracts(1), Is.Empty);

        var footprint = tables.PredicateGraphs.GetReadFootprintRow(
            predicate.PredicateGraphRow);
        Assert.That(tables.PredicateReadFootprints
            .GetRootRowCount(footprint), Is.EqualTo(1u));
        var rootRow = checked((int)tables.PredicateReadFootprints
            .GetRootFirstRow(footprint));
        Assert.That((tables.PredicateReadRoots.GetRootKind(rootRow),
                tables.PredicateReadRoots.GetRootIdentity(rootRow)),
            Is.EqualTo(((byte)2, 0u)));

        var matchNode = tables.PredicateGraphs.GetRootNodeRow(
            predicate.PredicateGraphRow);
        Assert.That(tables.PredicateNodes.GetNodeKind(matchNode),
            Is.EqualTo(0x34u));
        Assert.That(tables.PredicateChildEdges.GetChildCount(matchNode),
            Is.EqualTo(2));
        var subjectNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 0);
        Assert.That(tables.PredicateNodes.GetNodeKind(subjectNode),
            Is.EqualTo(0x10u));
        Assert.That(tables.TypeSpecs.GetTypeSpecRef(
                tables.PredicateNodes.GetResultTypeSpecRef(subjectNode))
            .TypeCode, Is.EqualTo((byte)0x08));
        var subjectOperand = checked((int)tables.PredicateNodes
            .GetOperandFirstRow(subjectNode));
        Assert.That(tables.PredicateNodeOperands.GetOperandKind(
            subjectOperand), Is.EqualTo((byte)1));
        Assert.That(tables.PredicateNodeOperands.GetOperandRef(
                subjectOperand), Is.EqualTo(checked((uint)rootRow)));

        var initializedPattern = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 1);
        Assert.That(tables.PredicateNodes.GetNodeKind(initializedPattern),
            Is.EqualTo(0x41u));
    }

    [Test]
    public void Construction2EmitsPhase2SomeCaptureAndCaptureReadGraph()
    {
        const string moduleIdentity = "phase-some-capture-producer";
        const string source = """
            void Check(int32? value)
                ensure value is some present && present > 0
            {
            }
            """;
        var failureSelection = new CoreContractFailureIdentitySelection(
            moduleIdentity,
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "requirement-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "postcondition-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "require-exception-schema"));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection = failureSelection,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var function = program.Functions.Single(
            static candidate => candidate.Name == "Check");
        Assert.That(function.DeclarationContractClauses.Select(
                static clause => (clause.Phase, clause.PhaseLocalOrdinal)),
            Is.EqualTo([
                (BoundDeclarationContractPhase.NormalPostcondition, 0u),
            ]));
        Assert.That(function.CheckedOutputStatePredicates.IsDefaultOrEmpty,
            Is.True);

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, C1M1Construction2SchemaIdentity, 1,
            out var emittedFailureSelections);
        Assert.That(emittedFailureSelections, Is.EqualTo([failureSelection]));

        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        var predicate = tables.Contracts.GetPredicateGraphContracts(1)
            .Single();
        Assert.That((predicate.PhaseTag, predicate.PhaseLocalOrdinal),
            Is.EqualTo(((byte)2, 0u)));
        Assert.That(tables.Contracts.GetOutputStateContracts(1), Is.Empty);

        var graphRow = predicate.PredicateGraphRow;
        var footprint = tables.PredicateGraphs.GetReadFootprintRow(graphRow);
        Assert.That(tables.PredicateReadFootprints.GetRootRowCount(footprint),
            Is.EqualTo(1));
        var rootRow = checked((int)tables.PredicateReadFootprints
            .GetRootFirstRow(footprint));
        Assert.That((tables.PredicateReadRoots.GetRootKind(rootRow),
                tables.PredicateReadRoots.GetRootIdentity(rootRow)),
            Is.EqualTo(((byte)2, 0u)));

        Assert.That(tables.PredicateCaptures.GetGraphRow(1),
            Is.EqualTo(graphRow));
        Assert.That(tables.PredicateCaptures.GetOrdinal(1), Is.Zero);
        Assert.That(tables.PredicateCaptures.GetRole(1), Is.EqualTo((byte)1));
        Assert.That(tables.PredicateCaptures.GetFlags(1), Is.Zero);
        var capturedTypeSpecRef
            = tables.PredicateCaptures.GetCapturedTypeSpecRef(1);
        Assert.That(tables.TypeSpecs.GetTypeSpecRef(capturedTypeSpecRef)
            .TypeCode, Is.EqualTo((byte)0x08));

        var rootNode = tables.PredicateGraphs.GetRootNodeRow(graphRow);
        Assert.That(tables.PredicateNodes.GetNodeKind(rootNode),
            Is.EqualTo(0x27u));
        var matchNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(rootNode, 0);
        Assert.That(tables.PredicateNodes.GetNodeKind(matchNode),
            Is.EqualTo(0x34u));
        var optionalSubjectNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 0);
        Assert.That(tables.PredicateNodes.GetNodeKind(optionalSubjectNode),
            Is.EqualTo(0x11u));
        var optionalTypeSpecRef = tables.PredicateNodes
            .GetResultTypeSpecRef(optionalSubjectNode);
        Assert.That(tables.TypeSpecs.GetTypeSpecRef(optionalTypeSpecRef).Form,
            Is.EqualTo((byte)0x06));
        var bindingPatternNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 1);
        Assert.That(tables.PredicateNodes.GetNodeKind(bindingPatternNode),
            Is.EqualTo(0x46u));
        var bindingOperand = checked((int)tables.PredicateNodes
            .GetOperandFirstRow(bindingPatternNode));
        Assert.That(tables.PredicateNodeOperands.GetOperandKind(
            bindingOperand), Is.EqualTo((byte)3));
        var captureRow = tables.PredicateNodeOperands.GetOperandRef(
            bindingOperand);
        Assert.That(captureRow, Is.EqualTo(1u));

        var comparisonNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(rootNode, 1);
        Assert.That(tables.PredicateNodes.GetNodeKind(comparisonNode),
            Is.EqualTo(0x21u));
        var captureLoadNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(comparisonNode, 0);
        Assert.That(tables.PredicateNodes.GetNodeKind(captureLoadNode),
            Is.EqualTo(0x13u));
        var capturePlaceNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(captureLoadNode, 0);
        Assert.That(tables.PredicateNodes.GetNodeKind(capturePlaceNode),
            Is.EqualTo(0x12u));
        var capturePlaceOperand = checked((int)tables.PredicateNodes
            .GetOperandFirstRow(capturePlaceNode));
        Assert.That(tables.PredicateNodeOperands.GetOperandKind(
            capturePlaceOperand), Is.EqualTo((byte)3));
        Assert.That(tables.PredicateNodeOperands.GetOperandRef(
                capturePlaceOperand),
            Is.EqualTo(captureRow));
        Assert.That(tables.PredicateCaptures.GetOrdinal(
                checked((int)captureRow)), Is.Zero);
    }

    [Test]
    public void Construction2EmitsPhase2NullPatternGraph()
    {
        const string moduleIdentity = "phase-null-pattern-producer";
        const string source = """
            void Check(int32? value)
                ensure value is null
            {
            }
            """;
        var failureSelection = new CoreContractFailureIdentitySelection(
            moduleIdentity,
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "requirement-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "postcondition-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "require-exception-schema"));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection = failureSelection,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var function = program.Functions.Single(
            static candidate => candidate.Name == "Check");
        Assert.That(function.CheckedOutputStatePredicates.IsDefaultOrEmpty,
            Is.True);

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, C1M1Construction2SchemaIdentity, 1,
            out var emittedFailureSelections);
        Assert.That(emittedFailureSelections, Is.EqualTo([failureSelection]));

        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        var predicate = tables.Contracts.GetPredicateGraphContracts(1)
            .Single();
        Assert.That(predicate.PhaseTag, Is.EqualTo((byte)2));
        Assert.That(tables.Contracts.GetOutputStateContracts(1), Is.Empty);
        Assert.That(tables.PredicateCaptures.Count, Is.Zero);

        var footprint = tables.PredicateGraphs.GetReadFootprintRow(
            predicate.PredicateGraphRow);
        Assert.That(tables.PredicateReadFootprints.GetRootRowCount(footprint),
            Is.EqualTo(1));
        var rootRow = checked((int)tables.PredicateReadFootprints
            .GetRootFirstRow(footprint));
        Assert.That((tables.PredicateReadRoots.GetRootKind(rootRow),
                tables.PredicateReadRoots.GetRootIdentity(rootRow)),
            Is.EqualTo(((byte)2, 0u)));

        var matchNode = tables.PredicateGraphs.GetRootNodeRow(
            predicate.PredicateGraphRow);
        Assert.That(tables.PredicateNodes.GetNodeKind(matchNode),
            Is.EqualTo(0x34u));
        var subjectNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 0);
        Assert.That(tables.PredicateNodes.GetNodeKind(subjectNode),
            Is.EqualTo(0x11u));
        Assert.That(tables.TypeSpecs.GetTypeSpecRef(
                tables.PredicateNodes.GetResultTypeSpecRef(subjectNode)).Form,
            Is.EqualTo((byte)0x06));
        var nullPattern = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 1);
        Assert.That(tables.PredicateNodes.GetNodeKind(nullPattern),
            Is.EqualTo(0x42u));
        Assert.That(tables.PredicateNodes.GetOperandCount(nullPattern),
            Is.Zero);
    }

    [Test]
    public void Construction2PhasePredicateEmissionRequiresOriginatingModuleFailureSelection()
    {
        const string moduleIdentity = "phase-predicate-context";
        const string source = """
            public int32 Identity(int32 value)
                require value > 0
                ensure return == value
            {
                return value;
            }
            """;
        var failureSelection = new CoreContractFailureIdentitySelection(
            moduleIdentity,
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "requirement-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "postcondition-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "require-exception-schema"));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection = failureSelection,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var missingSelectionProgram = program with {
            CoreContractFailureIdentitySelection = null,
            CoreContractFailureIdentitySelections = [],
        };
        Assert.That(missingSelectionProgram.CoreContractFailureIdentitySelection,
            Is.Null);
        Assert.That(missingSelectionProgram.CoreContractFailureIdentitySelections,
            Is.Empty);
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(missingSelectionProgram,
                moduleIdentity, C1M1Construction2SchemaIdentity, 1,
                out ImmutableArray<CoreContractFailureIdentitySelection> _));

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, C1M1Construction2SchemaIdentity, 1,
            out var emittedFailureSelections);
        Assert.That(artifact, Is.Not.Empty);
        Assert.That(emittedFailureSelections, Is.EqualTo([failureSelection]));
    }

    [Test]
    public void Construction2EmitsPhase2InferredNullableCaptureGraph()
    {
        const string moduleIdentity = "phase-nullable-capture-producer";
        const string source = """
            void Check(int32? value)
                ensure value is var? captured
            {
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection
                    = new CoreContractFailureIdentitySelection(
                        moduleIdentity,
                        new CoreContractFailureDeclarationIdentity(
                            "core.contracts", "requirement-exception"),
                        new CoreContractFailureDeclarationIdentity(
                            "core.contracts", "postcondition-exception"),
                        new CoreContractFailureDeclarationIdentity(
                            "core.contracts", "require-exception-schema")),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var function = program.Functions.Single(
            static candidate => candidate.Name == "Check");
        Assert.That(function.DeclarationContractClauses.Single().Phase,
            Is.EqualTo(BoundDeclarationContractPhase.NormalPostcondition));
        Assert.That(function.CheckedOutputStatePredicates.IsDefaultOrEmpty,
            Is.True);

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, C1M1Construction2SchemaIdentity, 1,
            out var emittedFailureSelections);
        Assert.That(emittedFailureSelections,
            Is.EqualTo(program.CoreContractFailureIdentitySelections));

        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        var predicate = tables.Contracts.GetPredicateGraphContracts(1)
            .Single();
        Assert.That((predicate.PhaseTag, predicate.PhaseLocalOrdinal),
            Is.EqualTo(((byte)2, 0u)));
        Assert.That(tables.Contracts.GetOutputStateContracts(1), Is.Empty);

        var graphRow = predicate.PredicateGraphRow;
        var footprint = tables.PredicateGraphs.GetReadFootprintRow(graphRow);
        Assert.That(tables.PredicateReadFootprints.GetRootRowCount(footprint),
            Is.EqualTo(1));
        var rootRow = checked((int)tables.PredicateReadFootprints
            .GetRootFirstRow(footprint));
        Assert.That((tables.PredicateReadRoots.GetRootKind(rootRow),
                tables.PredicateReadRoots.GetRootIdentity(rootRow)),
            Is.EqualTo(((byte)2, 0u)));

        Assert.That(tables.PredicateCaptures.GetGraphRow(1),
            Is.EqualTo(graphRow));
        Assert.That(tables.PredicateCaptures.GetOrdinal(1), Is.Zero);
        Assert.That(tables.PredicateCaptures.GetRole(1), Is.EqualTo((byte)1));
        Assert.That(tables.PredicateCaptures.GetFlags(1), Is.Zero);
        var captureTypeSpecRef
            = tables.PredicateCaptures.GetCapturedTypeSpecRef(1);
        Assert.That(tables.TypeSpecs.GetTypeSpecRef(captureTypeSpecRef).Form,
            Is.EqualTo((byte)0x06));

        var matchNode = tables.PredicateGraphs.GetRootNodeRow(graphRow);
        Assert.That(tables.PredicateNodes.GetNodeKind(matchNode),
            Is.EqualTo(0x34u));
        var subjectNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 0);
        Assert.That(tables.PredicateNodes.GetNodeKind(subjectNode),
            Is.EqualTo(0x11u));
        var subjectTypeSpecRef = tables.PredicateNodes
            .GetResultTypeSpecRef(subjectNode);
        Assert.That(tables.TypeSpecs.GetTypeSpecRef(subjectTypeSpecRef).Form,
            Is.EqualTo((byte)0x06));
        Assert.That(captureTypeSpecRef, Is.EqualTo(subjectTypeSpecRef));
        var patternNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 1);
        Assert.That(tables.PredicateNodes.GetNodeKind(patternNode),
            Is.EqualTo(0x47u));
        Assert.That(tables.PredicateNodes.GetOperandCount(patternNode),
            Is.EqualTo(1u));
        var patternOperand = checked((int)tables.PredicateNodes
            .GetOperandFirstRow(patternNode));
        Assert.That(tables.PredicateNodeOperands.GetOperandKind(
            patternOperand), Is.EqualTo((byte)3));
        Assert.That(tables.PredicateNodeOperands.GetOperandRef(
                patternOperand), Is.EqualTo(1u));
    }

    [Test]
    public void Construction2RejectsNullablePatternCaptureBoundToFormal()
    {
        const string moduleIdentity = "phase-nullable-capture-rejection";
        const string source = """
            void Check(int32? value)
                ensure value is var? captured
            {
            }
            """;
        var failureSelection = new CoreContractFailureIdentitySelection(
            moduleIdentity,
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "requirement-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "postcondition-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "require-exception-schema"));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection = failureSelection,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var function = program.Functions.Single(
            static candidate => candidate.Name == "Check");
        var clause = function.DeclarationContractClauses.Single();
        var pattern = (BoundIsVariablePattern)clause.Predicate;
        var malformedFunction = function with {
            DeclarationContractClauses = [
                clause with {
                    Predicate = pattern with { Variable = 0 },
                },
            ],
        };
        var malformedProgram = program with {
            Functions = program.Functions.Select(candidate =>
                candidate.Name == function.Name
                    ? malformedFunction : candidate).ToImmutableArray(),
        };

        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(malformedProgram,
                moduleIdentity, C1M1Construction2SchemaIdentity, 1,
                out ImmutableArray<CoreContractFailureIdentitySelection> _));
    }

    [Test]
    public void Construction2EmitsPhase2InferredNonNullVarCaptureGraph()
    {
        const string moduleIdentity = "phase-nonnull-var-producer";
        const string source = """
            void Check(int32? value)
                ensure value is var present
            {
            }
            """;
        var failureSelection = new CoreContractFailureIdentitySelection(
            moduleIdentity,
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "requirement-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "postcondition-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "require-exception-schema"));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection = failureSelection,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, C1M1Construction2SchemaIdentity, 1,
            out var emittedFailureSelections);
        Assert.That(emittedFailureSelections, Is.EqualTo([failureSelection]));

        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        var predicate = tables.Contracts.GetPredicateGraphContracts(1)
            .Single();
        Assert.That(predicate.PhaseTag, Is.EqualTo((byte)2));
        Assert.That(tables.Contracts.GetOutputStateContracts(1), Is.Empty);

        var matchNode = tables.PredicateGraphs.GetRootNodeRow(
            predicate.PredicateGraphRow);
        Assert.That(tables.PredicateNodes.GetNodeKind(matchNode),
            Is.EqualTo(0x34u));
        var inputSubjectNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 0);
        Assert.That(tables.PredicateNodes.GetNodeKind(inputSubjectNode),
            Is.EqualTo(0x11u));
        var inputType = tables.TypeSpecs.GetTypeSpecRef(
            tables.PredicateNodes.GetResultTypeSpecRef(inputSubjectNode));
        Assert.That(inputType.Form, Is.EqualTo((byte)0x06));

        var bindingPatternNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 1);
        Assert.That(tables.PredicateNodes.GetNodeKind(bindingPatternNode),
            Is.EqualTo(0x46u));
        var patternOperand = checked((int)tables.PredicateNodes
            .GetOperandFirstRow(bindingPatternNode));
        Assert.That(tables.PredicateNodeOperands.GetOperandKind(
            patternOperand), Is.EqualTo((byte)3));
        var captureRow = tables.PredicateNodeOperands.GetOperandRef(
            patternOperand);
        Assert.That(tables.PredicateCaptures.GetGraphRow(
                checked((int)captureRow)),
            Is.EqualTo(predicate.PredicateGraphRow));
        Assert.That(tables.PredicateCaptures.GetOrdinal(
                checked((int)captureRow)), Is.Zero);
        Assert.That(tables.PredicateCaptures.GetRole(
            checked((int)captureRow)), Is.EqualTo((byte)1));
        var capturedTypeSpecRef
            = tables.PredicateCaptures.GetCapturedTypeSpecRef(
            checked((int)captureRow));
        var capturedType = tables.TypeSpecs.GetTypeSpecRef(
            capturedTypeSpecRef);
        Assert.That(capturedType.Form, Is.EqualTo((byte)0x01));
        Assert.That(capturedType.TypeCode, Is.EqualTo((byte)0x08));
    }

    [Test]
    public void Construction2EmitsPhase2ScalarConstantPatternGraph()
    {
        const string moduleIdentity = "phase-constant-pattern-producer";
        const string source = """
            void Check(int32 value)
                ensure value is 5
            {
            }
            """;
        var failureSelection = new CoreContractFailureIdentitySelection(
            moduleIdentity,
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "requirement-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "postcondition-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "require-exception-schema"));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection = failureSelection,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var function = program.Functions.Single(
            static candidate => candidate.Name == "Check");
        var boundPattern = (BoundBinary)
            function.DeclarationContractClauses.Single().Predicate;
        Assert.That(boundPattern.IsConstantPatternTest, Is.True);

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, C1M1Construction2SchemaIdentity, 1,
            out var emittedFailureSelections);
        Assert.That(emittedFailureSelections, Is.EqualTo([failureSelection]));

        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        var predicate = tables.Contracts.GetPredicateGraphContracts(1)
            .Single();
        Assert.That((predicate.PhaseTag, predicate.PhaseLocalOrdinal),
            Is.EqualTo(((byte)2, 0u)));
        Assert.That(tables.Contracts.GetOutputStateContracts(1), Is.Empty);

        var graphRow = predicate.PredicateGraphRow;
        var footprint = tables.PredicateGraphs.GetReadFootprintRow(graphRow);
        Assert.That(tables.PredicateReadFootprints.GetRootRowCount(footprint),
            Is.EqualTo(1));
        var rootRow = checked((int)tables.PredicateReadFootprints
            .GetRootFirstRow(footprint));
        Assert.That((tables.PredicateReadRoots.GetRootKind(rootRow),
                tables.PredicateReadRoots.GetRootIdentity(rootRow)),
            Is.EqualTo(((byte)2, 0u)));

        var matchNode = tables.PredicateGraphs.GetRootNodeRow(graphRow);
        Assert.That(tables.PredicateNodes.GetNodeKind(matchNode),
            Is.EqualTo(0x34u));
        var subjectNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 0);
        Assert.That(tables.PredicateNodes.GetNodeKind(subjectNode),
            Is.EqualTo(0x11u));
        var subjectTypeSpecRef = tables.PredicateNodes
            .GetResultTypeSpecRef(subjectNode);
        Assert.That(tables.TypeSpecs.GetTypeSpecRef(subjectTypeSpecRef)
            .TypeCode, Is.EqualTo((byte)0x08));

        var constantPatternNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 1);
        Assert.That(tables.PredicateNodes.GetNodeKind(constantPatternNode),
            Is.EqualTo(0x43u));
        var literalOperand = checked((int)tables.PredicateNodes
            .GetOperandFirstRow(constantPatternNode));
        Assert.That(tables.PredicateNodeOperands.GetOperandKind(
            literalOperand), Is.EqualTo((byte)2));
        var frozenValueNode = checked((int)tables.PredicateNodeOperands
            .GetOperandRef(literalOperand));
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                subjectTypeSpecRef,
                tables.FrozenValueNodes.GetTypeSpecRef(frozenValueNode)),
            Is.True);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
                tables.FrozenValueNodes.GetScalarPayload(frozenValueNode,
                    tables.Blobs)),
            Is.EqualTo(5));

        var featureIds = Enumerable.Range(1, tables.Features.Count)
            .Select(row => tables.Features.GetFeature(row).FeatureId)
            .ToArray();
        Assert.That(featureIds, Does.Contain(10u));
        Assert.That(featureIds, Does.Contain(11u));
        Assert.That(featureIds, Does.Contain(17u));
    }

    [Test]
    public void Construction2RejectsScalarConstantPatternWithMismatchedLiteralType()
    {
        const string moduleIdentity = "phase-constant-pattern-rejection";
        const string source = """
            void Check(int32 value)
                ensure value is 5
            {
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection
                    = new CoreContractFailureIdentitySelection(
                        moduleIdentity,
                        new CoreContractFailureDeclarationIdentity(
                            "core.contracts", "requirement-exception"),
                        new CoreContractFailureDeclarationIdentity(
                            "core.contracts", "postcondition-exception"),
                        new CoreContractFailureDeclarationIdentity(
                            "core.contracts", "require-exception-schema")),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var function = program.Functions.Single(
            static candidate => candidate.Name == "Check");
        var clause = function.DeclarationContractClauses.Single();
        var pattern = (BoundBinary)clause.Predicate;
        var literal = (BoundConstant)pattern.Right;
        var malformedFunction = function with {
            DeclarationContractClauses = [
                clause with {
                    Predicate = pattern with {
                        Right = literal with {
                            Type = PrimitiveType.UInt32,
                        },
                    },
                },
            ],
        };
        var malformedProgram = program with {
            Functions = program.Functions.Select(candidate =>
                candidate.Name == function.Name
                    ? malformedFunction : candidate).ToImmutableArray(),
        };

        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(malformedProgram,
                moduleIdentity, C1M1Construction2SchemaIdentity, 1,
                out ImmutableArray<CoreContractFailureIdentitySelection> _));
    }

    [TestCase("int32", PrimitiveType.Int32, (byte)0x08)]
    [TestCase("int64", PrimitiveType.Int64, (byte)0x09)]
    public void Construction2EmitsPhase2ScalarTypeTestPatternGraph(
        string testedTypeName, PrimitiveType testedType, byte typeCode)
    {
        const string moduleIdentity = "phase-type-test-pattern-producer";
        var source = $$"""
            void Check(int32? value)
                ensure value is {{testedTypeName}}
            {
            }
            """;
        var failureSelection = new CoreContractFailureIdentitySelection(
            moduleIdentity,
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "requirement-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "postcondition-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "require-exception-schema"));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection = failureSelection,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var function = program.Functions.Single(
            static candidate => candidate.Name == "Check");
        var clause = function.DeclarationContractClauses.Single();
        Assert.That((clause.Phase, clause.PhaseLocalOrdinal),
            Is.EqualTo((BoundDeclarationContractPhase.NormalPostcondition,
                0u)));
        Assert.That(clause.Predicate, Is.TypeOf<BoundTypeTestPattern>());
        var boundTypeTest = (BoundTypeTestPattern)clause.Predicate;
        Assert.That(boundTypeTest.TestedType, Is.EqualTo(testedType));
        Assert.That(function.CheckedOutputStatePredicates.IsDefaultOrEmpty,
            Is.True);

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, C1M1Construction2SchemaIdentity, 1,
            out var emittedFailureSelections);
        Assert.That(emittedFailureSelections, Is.EqualTo([failureSelection]));

        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        var predicate = tables.Contracts.GetPredicateGraphContracts(1)
            .Single();
        Assert.That((predicate.PhaseTag, predicate.PhaseLocalOrdinal),
            Is.EqualTo(((byte)2, 0u)));
        Assert.That(tables.Contracts.GetOutputStateContracts(1), Is.Empty);
        Assert.That(tables.PredicateTypeUses.Count, Is.EqualTo(1));
        Assert.That(tables.PredicateCaptures.Count, Is.Zero);

        var footprint = tables.PredicateGraphs.GetReadFootprintRow(
            predicate.PredicateGraphRow);
        Assert.That(tables.PredicateReadFootprints.GetRootRowCount(footprint),
            Is.EqualTo(1));
        var rootRow = checked((int)tables.PredicateReadFootprints
            .GetRootFirstRow(footprint));
        Assert.That((tables.PredicateReadRoots.GetRootKind(rootRow),
                tables.PredicateReadRoots.GetRootIdentity(rootRow)),
            Is.EqualTo(((byte)2, 0u)));

        var matchNode = tables.PredicateGraphs.GetRootNodeRow(
            predicate.PredicateGraphRow);
        Assert.That(tables.PredicateNodes.GetNodeKind(matchNode),
            Is.EqualTo(0x34u));
        var subjectNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 0);
        Assert.That(tables.PredicateNodes.GetNodeKind(subjectNode),
            Is.EqualTo(0x11u));
        var optionalSubjectType = tables.TypeSpecs.GetTypeSpecRef(
            tables.PredicateNodes.GetResultTypeSpecRef(subjectNode));
        Assert.That(optionalSubjectType.Form,
            Is.EqualTo((byte)0x06));
        var optionalSubjectTypeBytes = tables.TypeSpecs.GetTypeSpecBytes(
            optionalSubjectType.Value);
        var optionalPayloadTypeRef = BinaryPrimitives
            .ReadUInt32LittleEndian(optionalSubjectTypeBytes.Slice(2, 4));
        var optionalPayloadType = tables.TypeSpecs.GetTypeSpecRef(
            optionalPayloadTypeRef);
        Assert.That((optionalPayloadType.Form,
                optionalPayloadType.TypeCode),
            Is.EqualTo(((byte)0x01, (byte)0x08)));

        var typeTestNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 1);
        Assert.That(tables.PredicateNodes.GetNodeKind(typeTestNode),
            Is.EqualTo(0x44u));
        var typeOperand = checked((int)tables.PredicateNodes
            .GetOperandFirstRow(typeTestNode));
        Assert.That(tables.PredicateNodeOperands.GetOperandKind(typeOperand),
            Is.EqualTo((byte)6));
        var typeUseRow = checked((int)tables.PredicateNodeOperands
            .GetOperandRef(typeOperand));
        var targetTypeSpecRef
            = tables.PredicateTypeUses.GetTypeSpecRef(typeUseRow);
        var targetType = tables.TypeSpecs.GetTypeSpecRef(
            targetTypeSpecRef);
        Assert.That(targetType.Form, Is.EqualTo((byte)0x01));
        Assert.That(targetType.TypeCode, Is.EqualTo(typeCode));

        var featureIds = Enumerable.Range(1, tables.Features.Count)
            .Select(row => tables.Features.GetFeature(row).FeatureId)
            .ToArray();
        Assert.That(featureIds, Does.Contain(17u));

        if (testedType == PrimitiveType.Int32)
        {
            Assert.Throws<NotSupportedException>(() =>
                TrRmExecutableCompiler.EmitStandard(program, moduleIdentity,
                    GeneralPredicateGraphsSchemaIdentity, 1));
        }
    }

    [Test]
    public void Construction2RejectsUnsupportedTypeTestTypeUse()
    {
        const string moduleIdentity = "phase-type-test-pattern-rejection";
        const string source = """
            void Check(int32? value)
                ensure value is int32
            {
            }
            """;
        var failureSelection = new CoreContractFailureIdentitySelection(
            moduleIdentity,
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "requirement-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "postcondition-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "require-exception-schema"));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection = failureSelection,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var function = program.Functions.Single(
            static candidate => candidate.Name == "Check");
        var clause = function.DeclarationContractClauses.Single();
        var typeTest = (BoundTypeTestPattern)clause.Predicate;
        var malformedFunction = function with {
            DeclarationContractClauses = [
                clause with {
                    Predicate = typeTest with {
                        TestedType = PrimitiveType.OwnedInt32Buffer,
                    },
                },
            ],
        };
        var malformedProgram = program with {
            Functions = program.Functions.Select(candidate =>
                candidate.Name == function.Name
                    ? malformedFunction : candidate).ToImmutableArray(),
        };

        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(malformedProgram,
                moduleIdentity, C1M1Construction2SchemaIdentity, 1,
                out ImmutableArray<CoreContractFailureIdentitySelection> _));
    }

    [TestCase("int32", PrimitiveType.Int32, (byte)0x08, true)]
    [TestCase("int64", PrimitiveType.Int64, (byte)0x09, false)]
    public void Construction2EmitsPhase2TypedValueBindPatternGraph(
        string testedTypeName, PrimitiveType testedType, byte typeCode,
        bool includesCaptureRead)
    {
        const string moduleIdentity = "phase-typed-value-bind-producer";
        var captureRead = includesCaptureRead
            ? " && captured > 0" : string.Empty;
        var source = $$"""
            void Check(int32? value)
                ensure value is {{testedTypeName}} captured{{captureRead}}
            {
            }
            """;
        var failureSelection = new CoreContractFailureIdentitySelection(
            moduleIdentity,
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "requirement-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "postcondition-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "require-exception-schema"));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection = failureSelection,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var function = program.Functions.Single(
            static candidate => candidate.Name == "Check");
        var clause = function.DeclarationContractClauses.Single();
        Assert.That(clause.Phase,
            Is.EqualTo(BoundDeclarationContractPhase.NormalPostcondition));
        var typedValuePredicate = clause.Predicate;
        if (includesCaptureRead)
        {
            Assert.That(clause.Predicate, Is.TypeOf<BoundBinary>());
            var logical = (BoundBinary)clause.Predicate;
            Assert.That(logical.Operator,
                Is.EqualTo(TokenKind.AmpersandAmpersand));
            typedValuePredicate = logical.Left;
        }
        Assert.That(typedValuePredicate,
            Is.TypeOf<BoundTypedValuePattern>());
        var typedValuePattern
            = (BoundTypedValuePattern)typedValuePredicate;
        Assert.That(typedValuePattern.TestedType, Is.EqualTo(testedType));
        Assert.That(function.Variables[typedValuePattern.Variable].Type,
            Is.EqualTo(testedType));
        Assert.That(function.CheckedOutputStatePredicates.IsDefaultOrEmpty,
            Is.True);

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, C1M1Construction2SchemaIdentity, 1,
            out var emittedFailureSelections);
        Assert.That(emittedFailureSelections, Is.EqualTo([failureSelection]));

        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        var predicate = tables.Contracts.GetPredicateGraphContracts(1)
            .Single();
        Assert.That((predicate.PhaseTag, predicate.PhaseLocalOrdinal),
            Is.EqualTo(((byte)2, 0u)));
        Assert.That(tables.PredicateCaptures.Count, Is.EqualTo(1));
        Assert.That(tables.PredicateTypeUses.Count, Is.EqualTo(1));

        var footprint = tables.PredicateGraphs.GetReadFootprintRow(
            predicate.PredicateGraphRow);
        Assert.That(tables.PredicateReadFootprints.GetRootRowCount(footprint),
            Is.EqualTo(1));
        var rootRow = checked((int)tables.PredicateReadFootprints
            .GetRootFirstRow(footprint));
        Assert.That((tables.PredicateReadRoots.GetRootKind(rootRow),
                tables.PredicateReadRoots.GetRootIdentity(rootRow)),
            Is.EqualTo(((byte)2, 0u)));

        var rootNode = tables.PredicateGraphs.GetRootNodeRow(
            predicate.PredicateGraphRow);
        Assert.That(tables.PredicateNodes.GetNodeKind(rootNode),
            Is.EqualTo(includesCaptureRead ? 0x27u : 0x34u));
        var matchNode = includesCaptureRead
            ? tables.PredicateChildEdges.GetChildNodeRowByOrdinal(
                rootNode, 0)
            : rootNode;
        Assert.That(tables.PredicateNodes.GetNodeKind(matchNode),
            Is.EqualTo(0x34u));
        var subjectNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 0);
        Assert.That(tables.TypeSpecs.GetTypeSpecRef(
                tables.PredicateNodes.GetResultTypeSpecRef(subjectNode)).Form,
            Is.EqualTo((byte)0x06));
        var patternNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 1);
        Assert.That(tables.PredicateNodes.GetNodeKind(patternNode),
            Is.EqualTo(0x45u));
        Assert.That(tables.PredicateNodes.GetOperandCount(patternNode),
            Is.EqualTo(2u));
        var typeOperand = checked((int)tables.PredicateNodes
            .GetOperandFirstRow(patternNode));
        Assert.That(tables.PredicateNodeOperands.GetOperandKind(
            typeOperand), Is.EqualTo((byte)6));
        var typeUseRow = checked((int)tables.PredicateNodeOperands
            .GetOperandRef(typeOperand));
        var typeUseSpecRef
            = tables.PredicateTypeUses.GetTypeSpecRef(typeUseRow);
        Assert.That(typeUseSpecRef, Is.EqualTo(capturedTypeSpecRef));
        var captureOperand = typeOperand + 1;
        Assert.That(tables.PredicateNodeOperands.GetOperandKind(
            captureOperand), Is.EqualTo((byte)3));
        var captureRow = checked((int)tables.PredicateNodeOperands
            .GetOperandRef(captureOperand));
        Assert.That(captureRow, Is.EqualTo(1));
        Assert.That(tables.PredicateCaptures.GetGraphRow(captureRow),
            Is.EqualTo(predicate.PredicateGraphRow));
        Assert.That(tables.PredicateCaptures.GetOrdinal(captureRow),
            Is.Zero);
        Assert.That(tables.PredicateCaptures.GetRole(captureRow),
            Is.EqualTo((byte)1));
        Assert.That(tables.PredicateCaptures.GetFlags(captureRow),
            Is.Zero);
        var capturedTypeSpecRef
            = tables.PredicateCaptures.GetCapturedTypeSpecRef(captureRow);
        var capturedType = tables.TypeSpecs.GetTypeSpecRef(
            capturedTypeSpecRef);
        Assert.That((capturedType.Form, capturedType.TypeCode),
            Is.EqualTo(((byte)0x01, typeCode)));
        Assert.That(typeUseSpecRef, Is.EqualTo(capturedTypeSpecRef));

        if (includesCaptureRead)
        {
            var comparisonNode = tables.PredicateChildEdges
                .GetChildNodeRowByOrdinal(rootNode, 1);
            Assert.That(tables.PredicateNodes.GetNodeKind(comparisonNode),
                Is.EqualTo(0x21u));
            var captureLoad = tables.PredicateChildEdges
                .GetChildNodeRowByOrdinal(comparisonNode, 0);
            Assert.That(tables.PredicateNodes.GetNodeKind(captureLoad),
                Is.EqualTo(0x13u));
            var capturePlace = tables.PredicateChildEdges
                .GetChildNodeRowByOrdinal(captureLoad, 0);
            Assert.That(tables.PredicateNodes.GetNodeKind(capturePlace),
                Is.EqualTo(0x12u));
            var capturePlaceOperand = checked((int)tables.PredicateNodes
                .GetOperandFirstRow(capturePlace));
            Assert.That(tables.PredicateNodeOperands.GetOperandKind(
                capturePlaceOperand), Is.EqualTo((byte)3));
            Assert.That(tables.PredicateNodeOperands.GetOperandRef(
                    capturePlaceOperand),
                Is.EqualTo((uint)captureRow));
        }
    }

    [Test]
    public void Construction2RejectsTypedValueCaptureBoundToFormal()
    {
        const string moduleIdentity = "phase-typed-value-bind-rejection";
        const string source = """
            void Check(int32? value)
                ensure value is int32 captured
            {
            }
            """;
        var failureSelection = new CoreContractFailureIdentitySelection(
            moduleIdentity,
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "requirement-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "postcondition-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "require-exception-schema"));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection = failureSelection,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var function = program.Functions.Single(
            static candidate => candidate.Name == "Check");
        var clause = function.DeclarationContractClauses.Single();
        var typedValuePattern
            = (BoundTypedValuePattern)clause.Predicate;
        var malformedFunction = function with {
            DeclarationContractClauses = [
                clause with {
                    Predicate = typedValuePattern with { Variable = 0 },
                },
            ],
        };
        var malformedProgram = program with {
            Functions = program.Functions.Select(candidate =>
                candidate.Name == function.Name
                    ? malformedFunction : candidate).ToImmutableArray(),
        };

        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(malformedProgram,
                moduleIdentity, C1M1Construction2SchemaIdentity, 1,
                out ImmutableArray<CoreContractFailureIdentitySelection> _));
    }

    [Test]
    public void Construction2EmitsPhaseOneTypeTestAndTypedValueBindGraphs()
    {
        const string moduleIdentity = "phase-one-type-pattern-producer";
        const string source = """
            void Check(int32? value)
                require value is int32
                require value is int32 captured && captured > 0
                ensure value is int64
            {
            }
            """;
        var failureSelection = new CoreContractFailureIdentitySelection(
            moduleIdentity,
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "requirement-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "postcondition-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "require-exception-schema"));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection = failureSelection,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, C1M1Construction2SchemaIdentity, 1,
            out var emittedFailureSelections);
        Assert.That(emittedFailureSelections, Is.EqualTo([failureSelection]));

        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        var predicates = tables.Contracts.GetPredicateGraphContracts(1)
            .OrderBy(static predicate => predicate.PhaseTag)
            .ThenBy(static predicate => predicate.PhaseLocalOrdinal)
            .ToArray();
        Assert.That(predicates.Select(static predicate =>
                (predicate.PhaseTag, predicate.PhaseLocalOrdinal)),
            Is.EqualTo([
                ((byte)1, 0u),
                ((byte)1, 1u),
                ((byte)2, 0u),
            ]));
        Assert.That(tables.PredicateTypeUses.Count, Is.EqualTo(2));
        Assert.That(tables.PredicateCaptures.Count, Is.EqualTo(1));

        var firstRoot = tables.PredicateGraphs.GetRootNodeRow(
            predicates[0].PredicateGraphRow);
        Assert.That(tables.PredicateNodes.GetNodeKind(firstRoot),
            Is.EqualTo(0x34u));
        var firstPattern = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(firstRoot, 1);
        Assert.That(tables.PredicateNodes.GetNodeKind(firstPattern),
            Is.EqualTo(0x44u));

        var secondRoot = tables.PredicateGraphs.GetRootNodeRow(
            predicates[1].PredicateGraphRow);
        Assert.That(tables.PredicateNodes.GetNodeKind(secondRoot),
            Is.EqualTo(0x27u));
        var matchNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(secondRoot, 0);
        var typedPattern = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 1);
        Assert.That(tables.PredicateNodes.GetNodeKind(typedPattern),
            Is.EqualTo(0x45u));
        Assert.That(tables.PredicateCaptures.GetGraphRow(1),
            Is.EqualTo(predicates[1].PredicateGraphRow));
        Assert.That(tables.PredicateCaptures.GetRole(1),
            Is.EqualTo((byte)1));

        var thirdRoot = tables.PredicateGraphs.GetRootNodeRow(
            predicates[2].PredicateGraphRow);
        Assert.That(tables.PredicateNodes.GetNodeKind(thirdRoot),
            Is.EqualTo(0x34u));
        var thirdPattern = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(thirdRoot, 1);
        Assert.That(tables.PredicateNodes.GetNodeKind(thirdPattern),
            Is.EqualTo(0x44u));
        for (var index = 0; index < predicates.Length; index++)
        {
            var footprint = tables.PredicateGraphs.GetReadFootprintRow(
                predicates[index].PredicateGraphRow);
            Assert.That(tables.PredicateReadFootprints.GetRootRowCount(
                    footprint), Is.EqualTo(1));
            var rootRow = checked((int)tables.PredicateReadFootprints
                .GetRootFirstRow(footprint));
            Assert.That((tables.PredicateReadRoots.GetRootKind(rootRow),
                    tables.PredicateReadRoots.GetRootIdentity(rootRow)),
                Is.EqualTo(((byte)2, 0u)));
        }
    }

    [Test]
    public void Construction2EmitsPhaseOneDirectScalarTypePatterns()
    {
        const string moduleIdentity = "phase-one-direct-type-pattern-producer";
        const string source = """
            void Check(int32 value)
                require value is int32
                require value is int32 captured && captured > 0
            {
            }
            """;
        var failureSelection = new CoreContractFailureIdentitySelection(
            moduleIdentity,
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "requirement-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "postcondition-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "require-exception-schema"));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection = failureSelection,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, C1M1Construction2SchemaIdentity, 1,
            out var emittedFailureSelections);
        Assert.That(emittedFailureSelections, Is.EqualTo([failureSelection]));

        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        var predicates = tables.Contracts.GetPredicateGraphContracts(1)
            .OrderBy(static predicate => predicate.PhaseLocalOrdinal)
            .ToArray();
        Assert.That(predicates.Select(static predicate =>
                (predicate.PhaseTag, predicate.PhaseLocalOrdinal)),
            Is.EqualTo([
                ((byte)1, 0u),
                ((byte)1, 1u),
            ]));
        Assert.That(tables.PredicateTypeUses.Count, Is.EqualTo(1));
        Assert.That(tables.PredicateCaptures.Count, Is.EqualTo(1));

        var typeTestRoot = tables.PredicateGraphs.GetRootNodeRow(
            predicates[0].PredicateGraphRow);
        Assert.That(tables.PredicateNodes.GetNodeKind(typeTestRoot),
            Is.EqualTo(0x34u));
        var typeTestPattern = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(typeTestRoot, 1);
        Assert.That(tables.PredicateNodes.GetNodeKind(typeTestPattern),
            Is.EqualTo(0x44u));

        var typedBindRoot = tables.PredicateGraphs.GetRootNodeRow(
            predicates[1].PredicateGraphRow);
        Assert.That(tables.PredicateNodes.GetNodeKind(typedBindRoot),
            Is.EqualTo(0x27u));
        var matchNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(typedBindRoot, 0);
        Assert.That(tables.PredicateNodes.GetNodeKind(matchNode),
            Is.EqualTo(0x34u));
        var subjectNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 0);
        var subjectType = tables.TypeSpecs.GetTypeSpecRef(
            tables.PredicateNodes.GetResultTypeSpecRef(subjectNode));
        Assert.That((subjectType.Form, subjectType.TypeCode),
            Is.EqualTo(((byte)0x01, (byte)0x08)));
        var typedValuePattern = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 1);
        Assert.That(tables.PredicateNodes.GetNodeKind(typedValuePattern),
            Is.EqualTo(0x45u));
        var captureOperand = checked((int)tables.PredicateNodes
            .GetOperandFirstRow(typedValuePattern)) + 1;
        Assert.That(tables.PredicateNodeOperands.GetOperandKind(
            captureOperand), Is.EqualTo((byte)3));
        var captureRow = checked((int)tables.PredicateNodeOperands
            .GetOperandRef(captureOperand));
        Assert.That(tables.PredicateCaptures.GetGraphRow(captureRow),
            Is.EqualTo(predicates[1].PredicateGraphRow));
        Assert.That(tables.PredicateCaptures.GetRole(captureRow),
            Is.EqualTo((byte)1));

        for (var index = 0; index < predicates.Length; index++)
        {
            var footprint = tables.PredicateGraphs.GetReadFootprintRow(
                predicates[index].PredicateGraphRow);
            Assert.That(tables.PredicateReadFootprints.GetRootRowCount(
                    footprint), Is.EqualTo(1));
            var rootRow = checked((int)tables.PredicateReadFootprints
                .GetRootFirstRow(footprint));
            Assert.That((tables.PredicateReadRoots.GetRootKind(rootRow),
                    tables.PredicateReadRoots.GetRootIdentity(rootRow)),
                Is.EqualTo(((byte)2, 0u)));
        }
    }

    [TestCase("ref<int32>", "ref<int32>", (byte)9, (byte)0x08, (byte)0)]
    [TestCase("write ref<int32>", "ref<int32>", (byte)9, (byte)0x08, (byte)1)]
    [TestCase("ref<int32>", "write ref<int32>", (byte)10, (byte)0x08, (byte)0)]
    [TestCase("ref<int32>", "ref<int64>", (byte)9, (byte)0x09, (byte)0)]
    public void Construction2EmitsNoCaptureReferencePatternGraph(
        string subjectTypeName, string patternTypeName,
        byte referencePatternKind, byte referentTypeCode,
        byte subjectQualifiers)
    {
        const string moduleIdentity = "reference-pattern-producer";
        var source = $$"""
            void Check({{subjectTypeName}} value)
                ensure value is {{patternTypeName}} _
            {
            }
            """;
        var failureSelection = new CoreContractFailureIdentitySelection(
            moduleIdentity,
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "requirement-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "postcondition-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "require-exception-schema"));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection = failureSelection,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var function = program.Functions.Single(
            static candidate => candidate.Name == "Check");
        var clause = function.DeclarationContractClauses.Single();
        Assert.That(clause.Predicate,
            Is.TypeOf<BoundReferenceTypeTestPattern>());
        var referencePattern
            = (BoundReferenceTypeTestPattern)clause.Predicate;
        var boundReferencePatternType = program.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(referencePattern.TestedType)];
        Assert.That(boundReferencePatternType.IsWritable,
            Is.EqualTo(referencePatternKind == 10));
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, C1M1Construction2SchemaIdentity, 1,
            out var emittedFailureSelections);
        Assert.That(emittedFailureSelections, Is.EqualTo([failureSelection]));

        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        var predicate = tables.Contracts.GetPredicateGraphContracts(1)
            .Single();
        Assert.That(predicate.PhaseTag, Is.EqualTo((byte)2));
        Assert.That(tables.PredicateCaptures.Count, Is.Zero);
        Assert.That(tables.PredicateTypeUses.Count, Is.EqualTo(1));
        Assert.That(tables.PredicateTypePatterns.Count, Is.EqualTo(2));

        var footprint = tables.PredicateGraphs.GetReadFootprintRow(
            predicate.PredicateGraphRow);
        Assert.That(tables.PredicateReadFootprints.GetRootRowCount(footprint),
            Is.EqualTo(1));
        var rootRow = checked((int)tables.PredicateReadFootprints
            .GetRootFirstRow(footprint));
        Assert.That((tables.PredicateReadRoots.GetRootKind(rootRow),
                tables.PredicateReadRoots.GetRootIdentity(rootRow)),
            Is.EqualTo(((byte)2, 0u)));

        var matchNode = tables.PredicateGraphs.GetRootNodeRow(
            predicate.PredicateGraphRow);
        Assert.That(tables.PredicateNodes.GetNodeKind(matchNode),
            Is.EqualTo(0x34u));
        var subjectNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 0);
        var subjectType = tables.TypeSpecs.GetTypeSpecRef(
            tables.PredicateNodes.GetResultTypeSpecRef(subjectNode));
        Assert.That((subjectType.Form, subjectType.Qualifiers),
            Is.EqualTo(((byte)0x07, subjectQualifiers)));
        var patternNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 1);
        Assert.That(tables.PredicateNodes.GetNodeKind(patternNode),
            Is.EqualTo(0x48u));
        var patternOperand = checked((int)tables.PredicateNodes
            .GetOperandFirstRow(patternNode));
        Assert.That(tables.PredicateNodeOperands.GetOperandKind(
            patternOperand), Is.EqualTo((byte)0x0B));
        var typePatternRow = checked((int)tables.PredicateNodeOperands
            .GetOperandRef(patternOperand));
        Assert.That(tables.PredicateTypePatterns.GetKind(typePatternRow),
            Is.EqualTo(referencePatternKind));
        Assert.That(tables.PredicateTypePatterns.GetTypeFlags(
            typePatternRow), Is.Zero);
        Assert.That(tables.PredicateTypePatterns.GetTargetRef(
            typePatternRow), Is.Zero);
        Assert.That(tables.PredicateTypePatterns.GetArgumentCount(
            typePatternRow), Is.EqualTo(1u));
        var exactTypePatternRow = checked((int)tables.PredicateTypePatterns
            .GetArgumentRef(typePatternRow, 0));
        Assert.That(tables.PredicateTypePatterns.GetKind(
            exactTypePatternRow), Is.EqualTo((byte)1));
        Assert.That(tables.PredicateTypePatterns.GetArgumentCount(
            exactTypePatternRow), Is.Zero);
        var typeUseRow = checked((int)tables.PredicateTypePatterns
            .GetTargetRef(exactTypePatternRow));
        var typeUseSpecRef
            = tables.PredicateTypeUses.GetTypeSpecRef(typeUseRow);
        var referentType = tables.TypeSpecs.GetTypeSpecRef(typeUseSpecRef);
        Assert.That((referentType.Form, referentType.TypeCode),
            Is.EqualTo(((byte)0x01, referentTypeCode)));
    }

    [TestCase("write ref<int32>", "mut ref<int32>", (byte)9,
        (byte)0x08, (byte)1, (byte)1, (byte)0)]
    [TestCase("write ref<int32>", "mut write ref<int32>", (byte)10,
        (byte)0x08, (byte)1, (byte)1, (byte)1)]
    [TestCase("ref<int32>", "mut write ref<int32>", (byte)10,
        (byte)0x08, (byte)0, (byte)1, (byte)1)]
    [TestCase("ref<int32>", "mut ref<int64>", (byte)9,
        (byte)0x09, (byte)0, (byte)1, (byte)0)]
    public void Construction2EmitsRoleTwoReferencePatternCaptureGraph(
        string subjectTypeName, string patternTypeName,
        byte referencePatternKind, byte referentTypeCode,
        byte subjectQualifiers, byte captureFlags,
        byte capturedQualifiers)
    {
        const string moduleIdentity = "reference-pattern-capture-producer";
        var source = $$"""
            void Check({{subjectTypeName}} value)
                ensure value is {{patternTypeName}} captured
            {
            }
            """;
        var failureSelection = new CoreContractFailureIdentitySelection(
            moduleIdentity,
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "requirement-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "postcondition-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "require-exception-schema"));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection = failureSelection,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var function = program.Functions.Single(
            static candidate => candidate.Name == "Check");
        var clause = function.DeclarationContractClauses.Single();
        Assert.That(clause.Predicate,
            Is.TypeOf<BoundReferenceTypeTestPattern>());
        var referencePattern
            = (BoundReferenceTypeTestPattern)clause.Predicate;
        Assert.That(referencePattern.Variable, Is.GreaterThanOrEqualTo(1));
        var captureVariable
            = function.Variables[referencePattern.Variable];
        Assert.That(captureVariable.IsMutable, Is.True);
        var captureType = program.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(captureVariable.Type)];
        Assert.That(captureType.IsWritable,
            Is.EqualTo(referencePatternKind == 10));

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, C1M1Construction2SchemaIdentity, 1,
            out var emittedFailureSelections);
        Assert.That(emittedFailureSelections, Is.EqualTo([failureSelection]));

        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        var predicate = tables.Contracts.GetPredicateGraphContracts(1)
            .Single();
        Assert.That(tables.PredicateCaptures.Count, Is.EqualTo(1));
        const int captureRow = 1;
        Assert.That(tables.PredicateCaptures.GetGraphRow(captureRow),
            Is.EqualTo(predicate.PredicateGraphRow));
        Assert.That(tables.PredicateCaptures.GetOrdinal(captureRow),
            Is.Zero);
        Assert.That(tables.PredicateCaptures.GetRole(captureRow),
            Is.EqualTo((byte)2));
        Assert.That(tables.PredicateCaptures.GetFlags(captureRow),
            Is.EqualTo(captureFlags));
        var capturedTypeSpecRef = tables.PredicateCaptures
            .GetCapturedTypeSpecRef(captureRow);
        var capturedType = tables.TypeSpecs.GetTypeSpecRef(
            capturedTypeSpecRef);
        Assert.That((capturedType.Form, capturedType.Qualifiers),
            Is.EqualTo(((byte)0x07, capturedQualifiers)));

        var footprint = tables.PredicateGraphs.GetReadFootprintRow(
            predicate.PredicateGraphRow);
        Assert.That(tables.PredicateReadFootprints.GetRootRowCount(footprint),
            Is.EqualTo(1));
        var rootRow = checked((int)tables.PredicateReadFootprints
            .GetRootFirstRow(footprint));
        Assert.That((tables.PredicateReadRoots.GetRootKind(rootRow),
                tables.PredicateReadRoots.GetRootIdentity(rootRow)),
            Is.EqualTo(((byte)2, 0u)));

        var matchNode = tables.PredicateGraphs.GetRootNodeRow(
            predicate.PredicateGraphRow);
        var subjectNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 0);
        var subjectType = tables.TypeSpecs.GetTypeSpecRef(
            tables.PredicateNodes.GetResultTypeSpecRef(subjectNode));
        Assert.That((subjectType.Form, subjectType.Qualifiers),
            Is.EqualTo(((byte)0x07, subjectQualifiers)));
        var patternNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 1);
        Assert.That(tables.PredicateNodes.GetNodeKind(patternNode),
            Is.EqualTo(0x48u));
        Assert.That(tables.PredicateNodes.GetOperandCount(patternNode),
            Is.EqualTo(2u));
        var patternOperandFirstRow = checked((int)tables.PredicateNodes
            .GetOperandFirstRow(patternNode));
        Assert.That(tables.PredicateNodeOperands.GetOperandKind(
            patternOperandFirstRow), Is.EqualTo((byte)0x0B));
        var typePatternRow = checked((int)tables.PredicateNodeOperands
            .GetOperandRef(patternOperandFirstRow));
        Assert.That(tables.PredicateTypePatterns.GetKind(typePatternRow),
            Is.EqualTo(referencePatternKind));
        var captureOperandRow = patternOperandFirstRow + 1;
        Assert.That(tables.PredicateNodeOperands.GetOperandKind(
            captureOperandRow), Is.EqualTo((byte)3));
        Assert.That(tables.PredicateNodeOperands.GetOperandRef(
                captureOperandRow),
            Is.EqualTo((uint)captureRow));
        Assert.That(tables.PredicateTypePatterns.GetArgumentCount(
            typePatternRow), Is.EqualTo(1u));
        var exactTypePatternRow = checked((int)tables.PredicateTypePatterns
            .GetArgumentRef(typePatternRow, 0));
        var typeUseRow = checked((int)tables.PredicateTypePatterns
            .GetTargetRef(exactTypePatternRow));
        var referentType = tables.TypeSpecs.GetTypeSpecRef(
            tables.PredicateTypeUses.GetTypeSpecRef(typeUseRow));
        Assert.That((referentType.Form, referentType.TypeCode),
            Is.EqualTo(((byte)0x01, referentTypeCode)));
    }

    [Test]
    public void StructuralValidatorRejectsReferenceCaptureAccessMismatch()
    {
        const string moduleIdentity
            = "reference-pattern-capture-access-validation";
        const string source = """
            void Check(write ref<int32> value, ref<int64> other)
                ensure value is ref<int32> captured
            {
            }
            """;
        var failureSelection = new CoreContractFailureIdentitySelection(
            moduleIdentity,
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "requirement-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "postcondition-exception"),
            new CoreContractFailureDeclarationIdentity(
                "core.contracts", "require-exception-schema"));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection = failureSelection,
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, C1M1Construction2SchemaIdentity, 1, out _);
        var index = TrRmArtifactIndex.ForC1M1Construction2Draft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(index);
        var graph = tables.Contracts.GetPredicateGraphContracts(1)
            .Single().PredicateGraphRow;
        var matchNode = tables.PredicateGraphs.GetRootNodeRow(graph);
        var subjectNode = tables.PredicateChildEdges
            .GetChildNodeRowByOrdinal(matchNode, 0);
        var writableSubjectTypeSpecRef = tables.PredicateNodes
            .GetResultTypeSpecRef(subjectNode);
        var mismatchedReferentTypeSpecRef = 0u;
        for (var row = 1; row <= tables.TypeSpecs.Count; row++)
        {
            var typeSpecRef = checked((uint)row);
            var typeSpec = tables.TypeSpecs.GetTypeSpecRef(typeSpecRef);
            if (typeSpec.Form != 0x07 || typeSpec.Qualifiers != 0)
            {
                continue;
            }

            var typeSpecBytes = tables.TypeSpecs.GetTypeSpecBytes(typeSpecRef);
            var referentTypeSpecRef
                = BinaryPrimitives.ReadUInt32LittleEndian(
                    typeSpecBytes[2..]);
            if (tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                    referentTypeSpecRef, 0xF200_0009))
            {
                mismatchedReferentTypeSpecRef = typeSpecRef;
                break;
            }
        }
        Assert.That(mismatchedReferentTypeSpecRef,
            Is.GreaterThan(0u));

        void AssertMalformedCapture(uint replacementTypeSpecRef)
        {
            var malformedArtifact = (byte[])artifact.Clone();
            var directoryOffset = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(
                    malformedArtifact.AsSpan(20)));
            var captureSectionOffset = -1;
            for (var section = 0; section < index.SectionCount; section++)
            {
                var entryOffset = checked(directoryOffset + section * 16);
                if (BinaryPrimitives.ReadUInt16LittleEndian(
                        malformedArtifact.AsSpan(entryOffset))
                    != 0x003E)
                {
                    continue;
                }

                captureSectionOffset = checked((int)
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        malformedArtifact.AsSpan(entryOffset + 8)));
                break;
            }

            Assert.That(captureSectionOffset, Is.GreaterThanOrEqualTo(32));
            BinaryPrimitives.WriteUInt32LittleEndian(
                malformedArtifact.AsSpan(captureSectionOffset + 12),
                replacementTypeSpecRef);
            Assert.Throws<InvalidDataException>(() =>
                TrRmStructuralMetadataValidator
                    .ValidateC1AndGetExecutionTables(
                        TrRmArtifactIndex.ForC1M1Construction2Draft(
                            malformedArtifact)));
        }

        AssertMalformedCapture(writableSubjectTypeSpecRef);
        AssertMalformedCapture(mismatchedReferentTypeSpecRef);
    }

    [Test]
    public void NativeEmitterEmitsNoCaptureReferencePatternContracts()
    {
        const string moduleIdentity = "reference-pattern-native-emission";
        const string source = """
            void Check(ref<int32> value)
                ensure value is ref<int32> _
            {
            }
            public int32 Run() { return 0; }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection
                    = new CoreContractFailureIdentitySelection(
                        moduleIdentity,
                        new CoreContractFailureDeclarationIdentity(
                            "core.contracts", "requirement-exception"),
                        new CoreContractFailureDeclarationIdentity(
                            "core.contracts", "postcondition-exception"),
                        new CoreContractFailureDeclarationIdentity(
                            "core.contracts", "require-exception-schema")),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        Assert.That(NativeEmitter.EmitLibrary(program, ["Run"],
            NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void NativeEmitterEmitsScalarTypePatternContracts()
    {
        const string moduleIdentity = "type-pattern-native-emission";
        const string source = """
            void CheckType(int32? value)
                ensure value is int32
            {
            }
            void CheckCapture(int32? value)
                ensure value is int32 captured && captured > 0
            {
            }
            public int32 Run()
            {
                CheckType(null);
                CheckCapture(null);
                return 0;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection
                    = new CoreContractFailureIdentitySelection(
                        moduleIdentity,
                        new CoreContractFailureDeclarationIdentity(
                            "core.contracts", "requirement-exception"),
                        new CoreContractFailureDeclarationIdentity(
                            "core.contracts", "postcondition-exception"),
                        new CoreContractFailureDeclarationIdentity(
                            "core.contracts", "require-exception-schema")),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        Assert.That(NativeEmitter.EmitLibrary(program, ["Run"],
            NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void ScalarTypeTestBindsAndEmitsOutsideDeclarationContracts()
    {
        const string source = """
            bool Check()
            {
                int32? value = null;
                return value is int32;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        Assert.That(NativeEmitter.EmitLibrary(program, ["Check"],
            NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [TestCase("int32")]
    [TestCase("int64")]
    public void ScalarTypedValueBindBindsAndEmitsOutsideDeclarationContracts(
        string testedTypeName)
    {
        var source = $$"""
            int32 Check()
            {
                int32? input = null;
                if (input is {{testedTypeName}} captured
                    && captured > 0)
                {
                    return 1;
                }
                return 0;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        Assert.That(NativeEmitter.EmitLibrary(program, ["Check"],
            NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void ScalarTypedValueBindCopiesDirectScalarOutsideDeclarationContracts()
    {
        const string source = """
            bool Check(float32 input)
            {
                return input is float32 captured;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        Assert.That(NativeEmitter.EmitLibrary(program, ["Check"],
            NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [TestCase("(enabled && ready) == (*destination is init)",
        "if (enabled && ready) { *destination = 42; }",
        "1:0001|0:1110")]
    [TestCase("(enabled && ready) != (*destination is init)",
        "if (!(enabled && ready)) { *destination = 42; }",
        "0:0001|1:1110")]
    [TestCase("(enabled && ready) || (*destination is init)", "", "1:1110")]
    [TestCase("(*destination is init) || (enabled && ready)", "", "1:1110")]
    [TestCase("(enabled && ready) ? (*destination is init) : true",
        "if (enabled && ready) { *destination = 42; }", "1:0001")]
    [TestCase("enabled && (*destination > 0)",
        "*destination = 42;", "1:0011")]
    [TestCase("enabled || (*destination > 0)",
        "*destination = 42;", "1:1100")]
    [TestCase("(enabled & ready) == (*destination is init)",
        "if (enabled & ready) { *destination = 42; }",
        "1:0001|0:1110")]
    [TestCase("(*destination is init) || true", "", "")]
    [TestCase("*destination is init", "*destination = 42;", "")]
    public void GeneralPredicateGraphProducerProjectsCheckedEnsureStateRelations(
        string ensure, string body, string expected)
    {
        var source = SyntaxTree.Parse(string.Concat(
            "void MaybeFill(bool enabled, bool ready, "
            + "ref<init int32> destination) ensure ", ensure, " {\n",
            body, "\n}"));
        var options = new CompilationOptions {
            CoreContractFailureIdentitySelection = new(
                "sample.consumer",
                new CoreContractFailureDeclarationIdentity(
                    "core.contracts", "requirement-exception"),
                new CoreContractFailureDeclarationIdentity(
                    "core.contracts", "postcondition-exception"),
                new CoreContractFailureDeclarationIdentity(
                    "core.contracts", "require-exception-schema")),
        };
        var compilation = Compilation.CreateLibrary(source, options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var function = ((BoundProgram)compilation.Program!).Functions
            .Single(static candidate => candidate.Name == "MaybeFill");
        Assert.That(function.DeclarationContractClauses, Has.Length.EqualTo(1));
        Assert.That(function.DeclarationContractClauses[0].Phase,
            Is.EqualTo(BoundDeclarationContractPhase.NormalPostcondition));
        Assert.That(function.HasStandardCheckedOutputStateMetadata, Is.True);
        var assignments = new (bool Enabled, bool Ready)[] {
            (false, false), (false, true), (true, false), (true, true),
        };
        var actual = string.Join("|",
            function.CheckedOutputStatePredicates.Select(predicate =>
                $"{predicate.State}:"
                + string.Concat(assignments.Select(assignment =>
                    Evaluate(predicate.Predicate, assignment.Enabled,
                        assignment.Ready) ? '1' : '0'))));
        Assert.That(actual, Is.EqualTo(expected));

        static bool Evaluate(BoundExpression expression, bool enabled,
            bool ready)
            => expression switch {
                BoundRead { Variable: 0 } => enabled,
                BoundRead { Variable: 1 } => ready,
                BoundConstant { Type: PrimitiveType.Bool } constant
                    => constant.Value != 0,
                BoundUnary { Operator: TokenKind.Bang } unary
                    => !Evaluate(unary.Operand, enabled, ready),
                BoundBinary binary
                    => binary.Operator switch {
                        TokenKind.AmpersandAmpersand or TokenKind.Ampersand
                            => Evaluate(binary.Left, enabled, ready)
                                & Evaluate(binary.Right, enabled, ready),
                        TokenKind.PipePipe or TokenKind.Pipe
                            => Evaluate(binary.Left, enabled, ready)
                                | Evaluate(binary.Right, enabled, ready),
                        TokenKind.Caret
                            => Evaluate(binary.Left, enabled, ready)
                                ^ Evaluate(binary.Right, enabled, ready),
                        TokenKind.EqualsEquals
                            => Evaluate(binary.Left, enabled, ready)
                                == Evaluate(binary.Right, enabled, ready),
                        TokenKind.BangEquals
                            => Evaluate(binary.Left, enabled, ready)
                                != Evaluate(binary.Right, enabled, ready),
                        _ => throw new InvalidOperationException(
                            "Unexpected projected Boolean operator."),
                    },
                BoundConditional conditional
                    => Evaluate(conditional.Condition, enabled, ready)
                        ? Evaluate(conditional.WhenTrue, enabled, ready)
                        : Evaluate(conditional.WhenFalse, enabled, ready),
                _ => throw new InvalidOperationException(
                    "Unexpected projected Boolean node."),
            };
    }

    [Test]
    public void GeneralPredicateGraphProducerPreservesMultipleOutputOrigins()
    {
        var source = SyntaxTree.Parse("""
            void Fill(bool first, bool second,
                ref<init int32> firstDestination,
                ref<init int32> secondDestination)
                ensure first == (*firstDestination is init)
                ensure second == (*secondDestination is init)
            {
                if (first) {
                    *firstDestination = 1;
                }
                if (second) {
                    *secondDestination = 2;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(source,
            CreateDeclarationContractOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var function = ((BoundProgram)compilation.Program!).Functions
            .Single(static candidate => candidate.Name == "Fill");
        Assert.That(function.CheckedOutputStatePredicates.Select(
                static predicate => (predicate.FormalOrdinal,
                    predicate.PhaseLocalOrdinal, predicate.State)),
            Is.EqualTo([
                (2, 0u, (byte)1), (2, 1u, (byte)0),
                (3, 2u, (byte)1), (3, 3u, (byte)0),
            ]));
    }

    [Test]
    public void GeneralPredicateGraphProducerCoalescesRepeatedClauseConsequences()
    {
        var source = SyntaxTree.Parse("""
            void MaybeFill(bool enabled, bool ready,
                ref<init int32> destination)
                ensure !enabled || (*destination is init)
                ensure !enabled || (*destination is init)
                ensure !ready || (*destination is init)
            {
                if (enabled || ready) {
                    *destination = 42;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(source,
            CreateDeclarationContractOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var function = program.Functions.Single(
            static candidate => candidate.Name == "MaybeFill");
        Assert.That(function.DeclarationContractClauses,
            Has.Length.EqualTo(3));
        var predicates = function.CheckedOutputStatePredicates;
        Assert.That(predicates, Has.Length.EqualTo(3));
        Assert.That(predicates.Select(
                static predicate => (predicate.FormalOrdinal,
                    predicate.PhaseLocalOrdinal, predicate.State)),
            Is.EqualTo([
                (2, 0u, (byte)1), (2, 1u, (byte)1),
                (2, 2u, (byte)1),
            ]));
        var coveragePredicate = predicates.Single(static predicate =>
            predicate.Predicate is BoundBinary {
                Operator: TokenKind.PipePipe,
            });
        Assert.That(coveragePredicate.State, Is.EqualTo(1));

        var readVariables = predicates
            .Select(predicate => {
                var reads = new SortedSet<int>();
                _ = BoundTreeTraversal.VisitExpressionTree(
                    predicate.Predicate, expression => {
                        if (expression is BoundRead read)
                        {
                            _ = reads.Add(read.Variable);
                        }
                    });
                return string.Join(",", reads);
            });
        Assert.That(readVariables, Is.EquivalentTo(["0", "1", "0,1"]));

        // Phase-2 clause persistence is separate from this phase-3 graph
        // round-trip.
        var graphProgram = CreatePhaseThreeGraphCodecInput(program);
        const string moduleIdentity = "cross-clause-output-state";
        var artifact = TrRmExecutableCompiler.EmitStandard(graphProgram,
            moduleIdentity, GeneralPredicateGraphsSchemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(artifact));
        var outputs = tables.Contracts.GetOutputStateContracts(1);
        Assert.That(outputs, Has.Length.EqualTo(3));
        var footprintRootCounts = outputs.Select(output => {
            var footprintRow
                = tables.PredicateGraphs.GetReadFootprintRow(
                    checked((int)output.PredicateGraphRow));
            return tables.PredicateReadFootprints.GetRootRowCount(
                footprintRow);
        });
        Assert.That(footprintRootCounts,
            Is.EquivalentTo(new[] { 1u, 1u, 2u }));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(
                GeneralPredicateGraphsSchemaIdentity, artifact),
            moduleIdentity, []);
        Assert.That(loaded.Functions.Single(
                static candidate => candidate.Name == "MaybeFill")
            .CheckedOutputStatePredicates, Has.Length.EqualTo(3));

    }

    [Test]
    public void GeneralPredicateGraphProducerKeepsDefaultForTautologicalClauses()
    {
        var source = SyntaxTree.Parse("""
            bool MaybeFill(bool enabled, ref<init int32> destination)
                ensure (*destination is init) || true
                ensure (*destination is init) || true
            {
                if (enabled) {
                    *destination = 42;
                    return true;
                }
                return false;
            }
            """);
        var compilation = Compilation.CreateLibrary(source,
            CreateDeclarationContractOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var function = ((BoundProgram)compilation.Program!).Functions
            .Single(static candidate => candidate.Name == "MaybeFill");
        Assert.That(function.DeclarationContractClauses,
            Has.Length.EqualTo(2));
        Assert.That(function.CheckedOutputSelectors,
            Is.EqualTo([
                new NativeFunctionPointerSelector(1, 0, 1),
                new NativeFunctionPointerSelector(1, 1, 2),
            ]));
        Assert.That(function.CheckedOutputStates,
            Is.EqualTo([
                new NativeFunctionPointerOutputState(1, 0, 1),
                new NativeFunctionPointerOutputState(1, 1, 0),
            ]));
        Assert.That(function.CheckedOutputStatePredicates, Is.Empty);
    }

    [Test]
    public void GeneralPredicateGraphProducerKeepsBoundedDefaultsForOtherOutputs()
    {
        var source = SyntaxTree.Parse("""
            bool MaybeFill(bool enabled,
                ref<init int32> firstDestination,
                ref<init int32> secondDestination)
                ensure enabled == (*firstDestination is init)
            {
                if (enabled) {
                    *firstDestination = 1;
                    *secondDestination = 2;
                    return true;
                }
                return false;
            }
            """);
        var compilation = Compilation.CreateLibrary(source,
            CreateDeclarationContractOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var function = ((BoundProgram)compilation.Program!).Functions
            .Single(static candidate => candidate.Name == "MaybeFill");
        Assert.That(function.HasStandardCheckedOutputStateMetadata, Is.True);
        Assert.That(function.CheckedOutputSelectors,
            Is.EqualTo([
                new NativeFunctionPointerSelector(3, 0, 1),
                new NativeFunctionPointerSelector(3, 1, 2),
            ]));
        Assert.That(function.CheckedOutputStates,
            Is.EqualTo([
                new NativeFunctionPointerOutputState(2, 0, 1),
                new NativeFunctionPointerOutputState(2, 1, 0),
            ]));
        Assert.That(function.CheckedOutputStatePredicates.Select(
                static predicate => (predicate.FormalOrdinal,
                    predicate.PhaseLocalOrdinal, predicate.State)),
            Is.EqualTo([
                (1, 2u, (byte)1), (1, 3u, (byte)0),
            ]));
    }

    [TestCase("""
        bool MaybeFill(bool enabled, ref<init int32> destination)
            ensure return == (*destination is init)
        {
            if (enabled) {
                *destination = 1;
                return true;
            }
            return false;
        }
        """, (byte)1, (byte)2)]
    [TestCase("""
        int32 MaybeFill(bool enabled, ref<init int32> destination)
            ensure (return >= 0) == (*destination is init)
        {
            if (enabled) {
                *destination = 1;
                return 0;
            }
            return -1;
        }
        """, (byte)5, (byte)6)]
    public void GeneralPredicateGraphProducerUsesBoundedResultSelectors(
        string source, byte completeNodeKind, byte vacantNodeKind)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            CreateDeclarationContractOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var function = ((BoundProgram)compilation.Program!).Functions
            .Single(static candidate => candidate.Name == "MaybeFill");
        Assert.That(function.HasStandardCheckedOutputStateMetadata, Is.True);
        Assert.That(function.CheckedOutputSelectors.Select(
                static selector => selector.NodeKind),
            Is.EqualTo([completeNodeKind, vacantNodeKind]));
        Assert.That(function.CheckedOutputStates,
            Is.EqualTo([
                new NativeFunctionPointerOutputState(1, 0, 1),
                new NativeFunctionPointerOutputState(1, 1, 0),
            ]));
        Assert.That(function.CheckedOutputStatePredicates, Is.Empty);
    }

    [Test]
    public void GeneralPredicateGraphDerivesOutputStateFromSavedIntegerResult()
    {
        var source = SyntaxTree.Parse("""
            int32 MaybeFill(bool enabled, ref<init int32> destination)
                ensure return == 7
                    ? *destination is init
                    : !(*destination is init)
            {
                if (enabled) {
                    *destination = 42;
                    return 7;
                }
                return 0;
            }

            int32 Main(bool enabled) {
                init int32 result;
                int32 status = MaybeFill(enabled, ref init result);
                if (status == 7) {
                    return result;
                }
                return 0;
            }
            """);
        var compilation = Compilation.CreateLibrary(source,
            CreateDeclarationContractOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var function = program.Functions.Single(
            static candidate => candidate.Name == "MaybeFill");
        Assert.That(function.HasStandardCheckedOutputStateMetadata, Is.True);
        Assert.That(function.CheckedOutputSelectors, Is.Empty);
        Assert.That(function.CheckedOutputStatePredicates, Has.Length.EqualTo(2));
        Assert.That(function.CheckedOutputStatePredicates.Select(
                static predicate => (predicate.FormalOrdinal, predicate.State)),
            Is.EquivalentTo(new[] { (1, (byte)0), (1, (byte)1) }));
        foreach (var predicate in function.CheckedOutputStatePredicates)
        {
            var hasSavedResult = false;
            Assert.That(BoundTreeTraversal.VisitExpressionTree(
                predicate.Predicate, expression => hasSavedResult |=
                    expression is BoundContractReturnValue), Is.True);
            Assert.That(hasSavedResult, Is.True);
        }

        var graphProgram = CreatePhaseThreeGraphCodecInput(program);
        const string moduleIdentity = "saved-result-output-state";
        var artifact = TrRmExecutableCompiler.EmitStandard(graphProgram,
            moduleIdentity, GeneralPredicateGraphsSchemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(artifact));
        Assert.That(Enumerable.Range(1, tables.PredicateReadRoots.Count)
            .Any(rootRow => tables.PredicateReadRoots.GetRootKind(rootRow) == 5
                && tables.PredicateReadRoots.GetRootIdentity(rootRow) == 0),
            Is.True);

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(
                GeneralPredicateGraphsSchemaIdentity, artifact),
            moduleIdentity, []);
        var loadedPredicates = loaded.Functions.Single(
                static candidate => candidate.Name == "MaybeFill")
            .CheckedOutputStatePredicates;
        Assert.That(loadedPredicates.Select(
                static predicate => (predicate.FormalOrdinal,
                    predicate.State)),
            Is.EquivalentTo(new[] { (1, (byte)0), (1, (byte)1) }));

        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            "saved-result-output-state-reemitted",
            GeneralPredicateGraphsSchemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(
                GeneralPredicateGraphsSchemaIdentity, reemittedArtifact),
            "saved-result-output-state-reemitted", []);
        Assert.That(reloaded.Functions.Single(
                static candidate => candidate.Name == "MaybeFill")
            .CheckedOutputStatePredicates.Select(
                static predicate => (predicate.FormalOrdinal,
                    predicate.State)),
            Is.EquivalentTo(new[] { (1, (byte)0), (1, (byte)1) }));
    }

    [Test]
    public void SavedIntegerResultPostconditionDoesNotOverstateVacantOutput()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 MaybeFill(bool enabled, ref<init int32> destination)
                ensure return == 7
                    ? *destination is init
                    : !(*destination is init)
            {
                if (enabled) {
                    return 7;
                }
                *destination = 42;
                return 0;
            }
            """), CreateDeclarationContractOptions());

        Assert.That(compilation.HasErrors, Is.True,
            "The saved-result branch promises a live output on a return path that leaves it vacant.");
    }

    [Test]
    public void GeneralPredicateGraphProjectsUnconditionalVacantOutput()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            void KeepVacant(ref<init int32> destination)
                ensure !(*destination is init)
            {
            }
            """), CreateDeclarationContractOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var function = program.Functions.Single(
            static candidate => candidate.Name == "KeepVacant");
        Assert.That(function.CheckedOutputStatePredicates, Has.Length.EqualTo(1));
        Assert.That(function.CheckedOutputStatePredicates[0].State,
            Is.Zero);
        Assert.That(function.CheckedOutputStatePredicates[0].Predicate,
            Is.TypeOf<BoundConstant>());
        Assert.That(((BoundConstant)function
                .CheckedOutputStatePredicates[0].Predicate).Value,
            Is.EqualTo(1));
    }

    [Test]
    public void GeneralPredicateGraphCodecRoundTripsRootlessPredicatesForBothStates()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            void KeepVacant(ref<init int32> destination) {}
            void AlwaysFill(ref<init int32> destination) {
                *destination = 42;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var graphProgram = program with {
            Functions = program.Functions.Select(function =>
                function.Name switch {
                    "KeepVacant" => function with {
                        CheckedOutputStatePredicates = [
                            new BoundOutputStatePredicate(0, 0, 0,
                                new BoundConstant(1, PrimitiveType.Bool,
                                    default)),
                        ],
                        HasStandardCheckedOutputStateMetadata = true,
                    },
                    "AlwaysFill" => function with {
                        CheckedOutputStatePredicates = [
                            new BoundOutputStatePredicate(0, 0, 1,
                                new BoundConstant(1, PrimitiveType.Bool,
                                    default)),
                        ],
                        HasStandardCheckedOutputStateMetadata = true,
                    },
                    _ => function,
                }).ToImmutableArray(),
        };
        const string moduleIdentity = "rootless-output-state";
        var artifact = TrRmExecutableCompiler.EmitStandard(graphProgram,
            moduleIdentity, GeneralPredicateGraphsSchemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(artifact));
        for (var methodRow = 1; methodRow <= 2; methodRow++)
        {
            var output = tables.Contracts.GetOutputStateContracts(methodRow)
                .Single();
            Assert.That(output.State,
                Is.EqualTo(methodRow == 1 ? (byte)0 : (byte)1));
            var footprintRow
                = tables.PredicateGraphs.GetReadFootprintRow(
                    checked((int)output.PredicateGraphRow));
            Assert.That(tables.PredicateReadFootprints.GetRootFirstRow(
                footprintRow), Is.Zero);
            Assert.That(tables.PredicateReadFootprints.GetRootRowCount(
                footprintRow), Is.Zero);
        }

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(
                GeneralPredicateGraphsSchemaIdentity, artifact),
            moduleIdentity, []);
        Assert.That(loaded.Functions.Single(
                static candidate => candidate.Name == "KeepVacant")
            .CheckedOutputStatePredicates.Single().State, Is.Zero);
        Assert.That(loaded.Functions.Single(
                static candidate => candidate.Name == "AlwaysFill")
            .CheckedOutputStatePredicates.Single().State, Is.EqualTo(1));

        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            "rootless-output-state-reemitted",
            GeneralPredicateGraphsSchemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(
                GeneralPredicateGraphsSchemaIdentity, reemittedArtifact),
            "rootless-output-state-reemitted", []);
        Assert.That(reloaded.Functions.Single(
                static candidate => candidate.Name == "KeepVacant")
            .CheckedOutputStatePredicates.Single().State, Is.Zero);
        Assert.That(reloaded.Functions.Single(
                static candidate => candidate.Name == "AlwaysFill")
            .CheckedOutputStatePredicates.Single().State, Is.EqualTo(1));
    }

    [Test]
    public void GeneralPredicateGraphProducerDoesNotTreatDescriptorTestsAsPlaces()
    {
        var source = SyntaxTree.Parse("""
            void Check(ref<init int32> destination)
                ensure destination is init
            {
                *destination = 42;
            }
            """);
        var compilation = Compilation.CreateLibrary(source,
            CreateDeclarationContractOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var function = ((BoundProgram)compilation.Program!).Functions
            .Single(static candidate => candidate.Name == "Check");
        Assert.That(function.CheckedOutputStatePredicates, Is.Empty);
        Assert.That(function.HasStandardCheckedOutputStateMetadata, Is.False);
    }

    private static CompilationOptions CreateDeclarationContractOptions()
        => new() {
            CoreContractFailureIdentitySelection = new(
                "sample.consumer",
                new CoreContractFailureDeclarationIdentity(
                    "core.contracts", "requirement-exception"),
                new CoreContractFailureDeclarationIdentity(
                    "core.contracts", "postcondition-exception"),
                new CoreContractFailureDeclarationIdentity(
                    "core.contracts", "require-exception-schema")),
        };

    private static BoundProgram CreatePhaseThreeGraphCodecInput(
        BoundProgram program)
        => program with {
            Functions = program.Functions.Select(static function =>
                function with {
                    DeclarationContractClauses = [],
                }).ToImmutableArray(),
            CoreContractFailureIdentitySelections = [],
            CoreContractFailureIdentitySelection = null,
        };

    [TestCase(PointerLoadsNativeParameterSchemaIdentity)]
    [TestCase(GeneralPredicateGraphsPointerLoadsNativeParameterSchemaIdentity)]
    public void StandardNativePointerLoadsPreserveGeneralNativeParameterContracts(
        string schemaIdentity)
    {
        const string source = """
            [Terrum.Interop.NativeImport("terrum-contract-test.dll", "Mutate")]
            [Terrum.Interop.NativeParameter(1, ReadWrite, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            extern unsafe float64 Mutate(write ptr<int32> destination,
                ptr<float64> factor);

            [Terrum.Interop.NativeImport("terrum-contract-test.dll", "Clear")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            extern unsafe void Clear(write ptr<int32> destination);

            public unsafe float64 MutateAndRead(
                write ptr<int32> destination, ptr<float64> factor,
                ptr<float64> value) {
                unsafe {
                    float64 result = Mutate(destination, factor);
                    return *value;
                }
            }

            public unsafe float64 ClearAndRead(
                write ptr<int32> destination, ptr<float64> value) {
                unsafe {
                    Clear(destination);
                    return *value;
                }
            }
            """;
        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, "pointer-load-general-native-parameters",
            schemaIdentity, 1);
        var input = new TrRmStandardArtifactInput(schemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            "pointer-load-general-native-parameters", []);
        var mutate = loaded.Functions.Single(static function =>
            function.Name == "Mutate");
        Assert.That(mutate.DirectNativeImport!.Parameters,
            Is.EqualTo([
                new DirectNativeParameter(0, PrimitiveType.Int32,
                    NativeParameterAccess.ReadWrite,
                    NativeParameterRetention.NoEscape, 1),
                new DirectNativeParameter(1, PrimitiveType.Float64,
                    NativeParameterAccess.Read,
                    NativeParameterRetention.NoEscape, 1),
            ]));
        var clear = loaded.Functions.Single(static function =>
            function.Name == "Clear");
        Assert.That(clear.ReturnType, Is.EqualTo(PrimitiveType.Void));
        Assert.That(clear.DirectNativeImport!.Parameters,
            Is.EqualTo([
                new DirectNativeParameter(0, PrimitiveType.Int32,
                    NativeParameterAccess.Write,
                    NativeParameterRetention.NoEscape, 1),
            ]));

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            "pointer-load-general-native-parameters-reemitted",
            schemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            "pointer-load-general-native-parameters-reemitted", []);
        Assert.That(reloaded.Functions.Single(static function =>
                function.Name == "Mutate").DirectNativeImport!.Parameters,
            Is.EqualTo(mutate.DirectNativeImport.Parameters));
        Assert.That(reloaded.Functions.Single(static function =>
                function.Name == "Clear").DirectNativeImport!.Parameters,
            Is.EqualTo(clear.DirectNativeImport.Parameters));
    }

    private static void AssertStandardNativePointerLoadOrderArtifact(
        byte[] artifact, string schemaIdentity)
    {
        var usesGeneralPredicateGraphs
            = StringComparer.Ordinal.Equals(schemaIdentity,
                GeneralPredicateGraphsPointerLoadsNativeParameterSchemaIdentity);
        var index = usesGeneralPredicateGraphs
            ? TrRmArtifactIndex
                .ForInitializationReferenceGeneralPredicatePointerLoadNativeParameterDraft(
                    artifact)
            : TrRmArtifactIndex
                .ForInitializationReferencePointerLoadNativeParameterDraft(
                    artifact);
        var tables = usesGeneralPredicateGraphs
            ? TrRmStructuralMetadataValidator
                .ValidateInitializationReferenceGeneralPredicatePointerLoadNativeParameterDraftAndGetExecutionTables(
                    index)
            : TrRmStructuralMetadataValidator
                .ValidateInitializationReferencePointerLoadNativeParameterDraftAndGetExecutionTables(
                    index);
        foreach (var featureId in new uint[] { 1, 2, 3, 16 })
        {
            Assert.That(tables.Features.TryGetFeature(featureId, out _),
                Is.True, $"Missing Feature {featureId}/1.");
        }
        Assert.That(tables.Features.TryGetFeature(8, out _), Is.False);

        AssertStandardNativePointerLoadCallOrder(tables,
            usesGeneralPredicateGraphs, "ReadBeforeWrite",
            readBeforeCall: true);
        AssertStandardNativePointerLoadCallOrder(tables,
            usesGeneralPredicateGraphs, "ReadAfterWrite",
            readBeforeCall: false);
    }

    private static void AssertStandardNativePointerLoadCallOrder(
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        bool usesGeneralPredicateGraphs, string methodName,
        bool readBeforeCall)
    {
        var methodRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == methodName)
            {
                methodRow = row;
                break;
            }
        }

        Assert.That(methodRow, Is.Not.Zero, methodName);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(methodRow), tables.Contracts.Count,
            generalPredicateGraphsInitializationReferenceBodies:
                usesGeneralPredicateGraphs);
        var code = body.GetBlockCode(0);
        var pointerLoadPosition = -1;
        var firstCallPosition = -1;
        var lastCallPosition = -1;
        var pointerLoadCount = 0;
        var callCount = 0;
        for (var position = 0; position < code.Length;)
        {
            var opcode = code[position];
            if (opcode == 0x1B)
            {
                pointerLoadPosition = position;
                pointerLoadCount++;
            }
            else if (opcode == 0x90)
            {
                if (firstCallPosition < 0)
                {
                    firstCallPosition = position;
                }

                lastCallPosition = position;
                callCount++;
            }

            position += TrRmRegisterBodyReader.InstructionLength(opcode,
                body.Schema, body.UsesNativePointerLoads);
        }

        Assert.That(pointerLoadCount, Is.EqualTo(1), methodName);
        Assert.That(callCount, Is.EqualTo(2), methodName);
        Assert.That(readBeforeCall
                ? pointerLoadPosition < firstCallPosition
                : lastCallPosition < pointerLoadPosition,
            Is.True, methodName);
    }

    private static void AssertWriteProcessMemoryParameters(
        DirectNativeImport import)
    {
        Assert.That(import.Parameters, Has.Length.EqualTo(2));
        Assert.That(import.Parameters[0],
            Is.EqualTo(new DirectNativeParameter(1, PrimitiveType.Int32,
                NativeParameterAccess.Write,
                NativeParameterRetention.NoEscape, 1)));
        Assert.That(import.Parameters[1],
            Is.EqualTo(new DirectNativeParameter(2, PrimitiveType.Int32,
                NativeParameterAccess.Read,
                NativeParameterRetention.NoEscape, 1)));
    }

    [TestCase(PointerLoadsSchemaIdentity)]
    [TestCase(GeneralPredicateGraphsPointerLoadsSchemaIdentity)]
    public void StandardNativePointerLoadsRequireNativeParameterComposition(
        string schemaIdentity)
    {
        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(StandardNativeWriteOrderSource), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(compilation.Program!,
                "standard-pointer-load-native-parameter-unselected",
                schemaIdentity, 1));
    }

    [TestCase(PointerLoadsNativeParameterSchemaIdentity)]
    [TestCase(GeneralPredicateGraphsPointerLoadsNativeParameterSchemaIdentity)]
    public void StandardNativePointerLoadsRequireNativeParameterFeature(
        string schemaIdentity)
    {
        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(StandardNativeWriteOrderSource), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, "standard-pointer-load-write-order",
            schemaIdentity, 1);
        var missingFeature = RemoveNativeParameterFeature(artifact,
            schemaIdentity);

        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity, missingFeature),
                "standard-pointer-load-write-order", []));

        var invalidRevision = ReplaceNativeParameterFeatureRevision(
            artifact, schemaIdentity, 2);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity, invalidRevision),
                "standard-pointer-load-write-order", []));
    }

    [TestCase(PointerLoadsSchemaIdentity)]
    [TestCase(GeneralPredicateGraphsPointerLoadsSchemaIdentity)]
    public async Task StandardNativePointerLoadsUseLongLocalHomeAsync(
        string schemaIdentity)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var parameters = string.Join(", ",
            Enumerable.Range(0, 16).Select(static index =>
                $"int32 ignored{index}"));
        var source = $$"""
            public unsafe int32 Read({{parameters}}, ptr<int32> value) {
                ptr<int32> local = value;
                unsafe { return *local; }
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, "standard-pointer-load-long-home",
            schemaIdentity, 1);
        var index = StringComparer.Ordinal.Equals(schemaIdentity,
                PointerLoadsSchemaIdentity)
            ? TrRmArtifactIndex.ForNativePointerLoadsDraft(artifact)
            : TrRmArtifactIndex
                .ForGeneralPredicateGraphsNativePointerLoadsDraft(artifact);
        var tables = StringComparer.Ordinal.Equals(schemaIdentity,
                PointerLoadsSchemaIdentity)
            ? TrRmStructuralMetadataValidator
                .ValidateAndGetExecutionTables(index)
            : TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    index);
        var methodRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Read")
            {
                methodRow = row;
                break;
            }
        }
        Assert.That(methodRow, Is.Not.Zero);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(methodRow), tables.Contracts.Count,
            generalPredicateGraphsInitializationReferenceBodies:
                StringComparer.Ordinal.Equals(schemaIdentity,
                    GeneralPredicateGraphsPointerLoadsSchemaIdentity));
        var code = body.GetBlockCode(0);
        var hasLongHomeStore = false;
        var hasLongHomeLoad = false;
        for (var position = 0; position < code.Length;)
        {
            var opcode = code[position];
            hasLongHomeStore |= opcode == 0xD6;
            hasLongHomeLoad |= opcode == 0xD5;
            position += TrRmRegisterBodyReader.InstructionLength(opcode,
                body.Schema, body.UsesNativePointerLoads);
        }
        Assert.That(hasLongHomeStore, Is.True);
        Assert.That(hasLongHomeLoad, Is.True);

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            "standard-pointer-load-long-home", []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            "standard-pointer-load-long-home-reemitted", schemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            "standard-pointer-load-long-home-reemitted", []);
        var image = NativeEmitter.EmitLibrary(reloaded, ["Read"],
            NativeRuntimePolicy.FailFast);
        var libraryPath = Path.Combine(Path.GetTempPath(),
            $"terrum-standard-pointer-load-home-{Guid.NewGuid():N}.dll");
        nint library = 0;
        var backing = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(backing, 42);
            await File.WriteAllBytesAsync(libraryPath, image)
                .ConfigureAwait(false);
            library = NativeLibrary.Load(libraryPath);
            var read = Marshal.GetDelegateForFunctionPointer<
                PointerReadWithLongHomeFunction>(NativeLibrary.GetExport(
                library, "Read"));
            Assert.That(read(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13,
                14, 15, backing), Is.EqualTo(42));
        }
        finally
        {
            if (library != 0)
            {
                NativeLibrary.Free(library);
            }

            Marshal.FreeHGlobal(backing);
            await TestFileCleanup.DeleteWithRetryAsync(libraryPath)
                .ConfigureAwait(false);
        }
    }

    [TestCase(PointerLoadsSchemaIdentity)]
    [TestCase(GeneralPredicateGraphsPointerLoadsSchemaIdentity)]
    public void StandardNativePointerLoadsExecuteInJit(
        string schemaIdentity)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string source = """
            [Terrum.Interop.NativeImport("kernel32.dll", "GetCommandLineW")]
            extern unsafe ptr<char16> GetCommandLineW();

            int32 Main() {
                unsafe { return int32(*GetCommandLineW()); }
            }
            """;
        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, "standard-pointer-load-jit", schemaIdentity,
            1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            "standard-pointer-load-jit", []);
        var main = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var unsafeBlock = (BoundUnsafe)main.Body.Statements.Single();
        var returned = (BoundReturn)unsafeBlock.Body.Statements.Single();
        var conversion = (BoundCast)returned.Value!;
        var read = (BoundNativePointerRead)conversion.Operand;
        Assert.That(read.IsUnsafeAuthorized, Is.True);
        Assert.That(read.Address, Is.TypeOf<BoundCall>());
        Assert.That(conversion.Type, Is.EqualTo(PrimitiveType.Int32));
        var kernel32 = NativeLibrary.Load(
            Path.Combine(Environment.SystemDirectory, "kernel32.dll"));
        int expected;
        try
        {
            var getCommandLine = Marshal.GetDelegateForFunctionPointer<
                GetCommandLineFunction>(NativeLibrary.GetExport(kernel32,
                "GetCommandLineW"));
            expected = (int)Marshal.ReadInt16(getCommandLine());
        }
        finally
        {
            NativeLibrary.Free(kernel32);
        }

        Assert.That(NativeJit.Execute(loaded,
            NativeRuntimePolicy.FailFast),
            Is.EqualTo(expected));
    }

    [TestCase(PointerLoadsSchemaIdentity)]
    [TestCase(GeneralPredicateGraphsPointerLoadsSchemaIdentity)]
    public async Task StandardNativePointerLoadsAllowSameR13AndReadExactInt32WidthAsync(
        string schemaIdentity)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public unsafe int32 ReadInt32(ptr<int32> value) {
                unsafe { return *value; }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, "standard-pointer-load-guard",
            schemaIdentity, 1);
        var index = StringComparer.Ordinal.Equals(schemaIdentity,
                PointerLoadsSchemaIdentity)
            ? TrRmArtifactIndex.ForNativePointerLoadsDraft(artifact)
            : TrRmArtifactIndex
                .ForGeneralPredicateGraphsNativePointerLoadsDraft(artifact);
        var tables = StringComparer.Ordinal.Equals(schemaIdentity,
                PointerLoadsSchemaIdentity)
            ? TrRmStructuralMetadataValidator
                .ValidateAndGetExecutionTables(index)
            : TrRmStructuralMetadataValidator
                .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                    index);
        var methodRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "ReadInt32")
            {
                methodRow = row;
                break;
            }
        }
        Assert.That(methodRow, Is.Not.Zero);
        var bodyRow = tables.Methods.GetBodyRow(methodRow);
        var r13Artifact = ReplaceStandardBodyOperand(artifact,
            schemaIdentity, bodyRow, 0x11, 0x0D);
        r13Artifact = ReplaceStandardBodyOperand(r13Artifact, schemaIdentity,
            bodyRow, 0x1B, 0xDD);
        r13Artifact = ReplaceStandardBodyOperand(r13Artifact, schemaIdentity,
            bodyRow, 0x17, 0x0D);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, r13Artifact),
            "standard-pointer-load-guard", []);
        var image = NativeEmitter.EmitLibrary(loaded, ["ReadInt32"],
            NativeRuntimePolicy.FailFast);
        var libraryPath = Path.Combine(Path.GetTempPath(),
            $"terrum-standard-pointer-load-guard-{Guid.NewGuid():N}.dll");
        nint library = 0;
        nint memory = 0;
        var freeSucceeded = false;
        var freeError = 0;
        try
        {
            await File.WriteAllBytesAsync(libraryPath, image)
                .ConfigureAwait(false);
            library = NativeLibrary.Load(libraryPath);
            var readInt32 = Marshal.GetDelegateForFunctionPointer<
                PointerReadFunction>(NativeLibrary.GetExport(library,
                "ReadInt32"));

            var pageSize = Environment.SystemPageSize;
            memory = VirtualAlloc(0,
                checked((nuint)(pageSize * 2)), 0x3000, 0x04);
            Assert.That(memory, Is.Not.Zero,
                $"VirtualAlloc failed with error "
                + $"{Marshal.GetLastPInvokeError()}.");
            var valueAddress = IntPtr.Add(memory, pageSize - sizeof(int));
            Assert.That(valueAddress.ToInt64() % sizeof(int), Is.Zero);
            Marshal.WriteInt32(valueAddress, 42);
            Assert.That(VirtualProtect(IntPtr.Add(memory, pageSize),
                    checked((nuint)pageSize), 0x01, out _),
                Is.True,
                $"VirtualProtect failed with error "
                + $"{Marshal.GetLastPInvokeError()}.");
            Assert.That(readInt32(valueAddress), Is.EqualTo(42));
        }
        finally
        {
            if (library != 0)
            {
                NativeLibrary.Free(library);
            }

            if (memory != 0)
            {
                freeSucceeded = VirtualFree(memory, 0, 0x8000);
                if (!freeSucceeded)
                {
                    freeError = Marshal.GetLastPInvokeError();
                }
            }

            await TestFileCleanup.DeleteWithRetryAsync(libraryPath)
                .ConfigureAwait(false);
        }
        Assert.That(freeSucceeded, Is.True,
            $"VirtualFree failed with error {freeError}.");
    }

    [TestCase(PointerLoadsSchemaIdentity)]
    [TestCase(GeneralPredicateGraphsPointerLoadsSchemaIdentity)]
    public async Task StandardPointerCellProviderAndLoadExecuteInLinkedJit(
        string schemaIdentity)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string providerIdentity = "standard-pointer-load-provider";
        const string consumerIdentity = "consumer.module";
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public void Publish(ref<init ptr<char16>> output,
                    ptr<char16> value) {
                    *output = value;
                }
                """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, providerIdentity, schemaIdentity, 1);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, []);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("kernel32.dll", "GetCommandLineW")]
            extern unsafe ptr<char16> GetCommandLineW();

            public ptr<char16> Run(ptr<char16> value) {
                init ptr<char16> pointer;
                Publish(ref init pointer, value);
                return pointer;
            }

            int32 Main() {
                unsafe {
                    ptr<char16> commandLine = GetCommandLineW();
                    ptr<char16> forwarded = commandLine;
                    return int32(*Run(forwarded));
                }
            }
            """), providerReference, new CompilationOptions {
                NativeCoreRegistration
                    = NativeCoreRegistrationTests.CreateRegistration(),
            });
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerCompilation.Program!, consumerIdentity, schemaIdentity, 1);
        var consumerInput = new TrRmStandardArtifactInput(schemaIdentity,
            consumerArtifact);
        var loadedConsumer = TrRmExecutableCompiler.LoadStandard(
            consumerInput, consumerIdentity, [providerInput]);
        var reemittedConsumer = TrRmExecutableCompiler.EmitStandard(
            loadedConsumer, consumerIdentity, schemaIdentity, 1);
        var loadedProvider = TrRmExecutableCompiler.LoadStandard(providerInput,
            providerIdentity, []);
        var reemittedProvider = TrRmExecutableCompiler.EmitStandard(
            loadedProvider, providerIdentity, schemaIdentity, 1);
        var reemittedConsumerInput = new TrRmStandardArtifactInput(
            schemaIdentity, reemittedConsumer);
        var reemittedProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, reemittedProvider);
        _ = TrRmExecutableCompiler.LoadStandard(reemittedConsumerInput,
            consumerIdentity, [reemittedProviderInput]);

        var kernel32 = NativeLibrary.Load(
            Path.Combine(Environment.SystemDirectory, "kernel32.dll"));
        int expected;
        try
        {
            var getCommandLine = Marshal.GetDelegateForFunctionPointer<
                GetCommandLineFunction>(NativeLibrary.GetExport(kernel32,
                "GetCommandLineW"));
            expected = (int)Marshal.ReadInt16(getCommandLine());
        }
        finally
        {
            NativeLibrary.Free(kernel32);
        }

        Assert.That(NativeModuleCompiler.ExecuteStandard(consumerInput,
            consumerIdentity, [providerInput], NativeRuntimePolicy.FailFast),
            Is.EqualTo(expected));
        Assert.That(NativeModuleCompiler.ExecuteStandard(
            reemittedConsumerInput, consumerIdentity,
            [reemittedProviderInput], NativeRuntimePolicy.FailFast),
            Is.EqualTo(expected));
        var image = NativeModuleCompiler.EmitStandardExecutable(
            reemittedConsumerInput, consumerIdentity,
            [reemittedProviderInput], NativeRuntimePolicy.FailFast);
        await TrRmForeignInvocationAddressEmitterTests
            .AssertNativeImageExitCodeAsync(image, consumerIdentity, expected)
            .ConfigureAwait(false);
    }

    private static byte[] ReplaceStandardBodyOperand(byte[] artifact,
        string schemaIdentity, int bodyRow, byte opcode,
        byte replacement)
    {
        var index = StringComparer.Ordinal.Equals(schemaIdentity,
                PointerLoadsSchemaIdentity)
            ? TrRmArtifactIndex.ForNativePointerLoadsDraft(artifact)
            : TrRmArtifactIndex
                .ForGeneralPredicateGraphsNativePointerLoadsDraft(artifact);
        Assert.That(index.TryGetSection(0x20, out var bodyBytes,
            out var bodyCount), Is.True);
        var bodies = new TrRmBodyTableReader(bodyBytes, bodyCount);
        var bodyRows = new ReadOnlyMemory<byte>[bodyCount];
        for (var row = 1; row <= bodyCount; row++)
        {
            bodyRows[row - 1] = bodies.GetRow(row).ToArray();
        }

        var body = bodyRows[bodyRow - 1].ToArray();
        var isGeneralPredicateGraphs = StringComparer.Ordinal.Equals(
            schemaIdentity, GeneralPredicateGraphsPointerLoadsSchemaIdentity);
        var bodyReader = new TrRmRegisterBodyReader(body,
            generalPredicateGraphsInitializationReferenceBodies:
                isGeneralPredicateGraphs,
            schema: isGeneralPredicateGraphs
                ? TrRmArtifactSchema.GeneralPredicateGraphsDraft
                : TrRmArtifactSchema.Standard,
            usesNativePointerLoads: true);
        var code = bodyReader.GetBlockCode(0);
        var codeOffset = body.AsSpan().IndexOf(code);
        var operandOffset = -1;
        for (var position = 0; position < code.Length;)
        {
            var currentOpcode = code[position];
            var instructionLength = TrRmRegisterBodyReader
                .InstructionLength(currentOpcode, bodyReader.Schema,
                    bodyReader.UsesNativePointerLoads);
            if (currentOpcode == opcode)
            {
                operandOffset = position + 1;
                break;
            }

            position += instructionLength;
        }
        Assert.That(codeOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(operandOffset, Is.GreaterThan(0));
        body[codeOffset + operandOffset] = replacement;
        bodyRows[bodyRow - 1] = body;

        var rewrittenBodies = TrRmVariableTableWriter.EncodeBodies(bodyRows);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags, (uint)count,
                kind == 0x20 ? rewrittenBodies : bytes.ToArray()));
        }

        return TrRmArtifactWriter.EncodeDevelopment(sections);
    }

    private static void AssertStandardNativePointerLoad(
        BoundProgram program, string functionName,
        PrimitiveType expectedPointeeType, bool addressIsCall = false)
    {
        var function = program.Functions.Single(function =>
            function.Name == functionName);
        var statements = function.Body.Statements;
        if (statements.Length == 1
            && statements[0] is BoundUnsafe outerUnsafe)
        {
            statements = outerUnsafe.Body.Statements;
        }

        BoundReturn? returnStatement = null;
        foreach (var statement in statements)
        {
            if (statement is BoundReturn directReturn)
            {
                Assert.That(returnStatement, Is.Null);
                returnStatement = directReturn;
            }
            else if (statement is BoundUnsafe unsafeBlock)
            {
                foreach (var nested in unsafeBlock.Body.Statements)
                {
                    if (nested is BoundReturn unsafeReturn)
                    {
                        Assert.That(returnStatement, Is.Null);
                        returnStatement = unsafeReturn;
                    }
                }
            }
        }
        Assert.That(returnStatement, Is.Not.Null);
        var value = returnStatement!.Value!;
        var read = value is BoundCast conversion
            ? (BoundNativePointerRead)conversion.Operand
            : (BoundNativePointerRead)value;
        if (value is BoundCast resultConversion)
        {
            Assert.That(resultConversion.Type,
                Is.EqualTo(function.ReturnType));
        }
        Assert.That(read.Type, Is.EqualTo(expectedPointeeType));
        Assert.That(read.IsUnsafeAuthorized, Is.True);
        if (addressIsCall)
        {
            Assert.That(read.Address, Is.TypeOf<BoundCall>());
        }
        else
        {
            Assert.That(read.Address, Is.TypeOf<BoundRead>());
        }
        Assert.That(function.RequiresUnsafeCaller, Is.True);
        var pointerType = program.NativePointerTypes[
            NativePointerTypes.Index(read.Address.Type)];
        Assert.That(pointerType.PointeeType,
            Is.EqualTo(expectedPointeeType));
    }

    [TestCase(SchemaIdentity)]
    [TestCase(GeneralPredicateGraphsSchemaIdentity)]
    public void StandardPointerCellTypeBindsThroughProviderReference(
        string schemaIdentity)
    {
        const string providerSource = """
            public void Publish(ref<init ptr<int32>> output,
                ptr<int32> value) {
                *output = value;
            }
            """;
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)providerCompilation.Program!,
            "standard-pointer-cell-provider", schemaIdentity, 1);
        var providerInput = new TrRmStandardArtifactInput(
            schemaIdentity, providerArtifact);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "standard-pointer-cell-provider", []);
        var importedPublish = providerReference.Functions.Single(
            static function => function.Name == "Publish");
        var importedPointer = importedPublish.Variables[1].Type;
        var importedOutputType = importedPublish.Variables[0].Type;
        var importedOutput = providerReference.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(importedOutputType)];
        Assert.That(importedPublish.RequiresUnsafeCaller, Is.False);
        Assert.That(importedOutput.IsInitialization, Is.True);
        Assert.That(importedOutput.ReferentType,
            Is.EqualTo(importedPointer));
        Assert.That(providerReference.NativePointerTypes[
            NativePointerTypes.Index(importedPointer)].PointeeType,
            Is.EqualTo(PrimitiveType.Int32));

        const string consumerSource = """
            public void Forward(ref<init ptr<int32>> output,
                ptr<int32> value) {
                Publish(ref init *output, value);
            }

            public ptr<int32> Run(ptr<int32> value) {
                init ptr<int32> pointer;
                Forward(ref init pointer, value);
                return pointer;
            }
            """;
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(consumerSource), providerReference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumer = (BoundProgram)consumerCompilation.Program!;
        var forward = consumer.Functions.Single(
            static function => function.Name == "Forward");
        Assert.That(forward.Variables[0].Type,
            Is.EqualTo(importedOutputType));
        Assert.That(forward.Variables[1].Type,
            Is.EqualTo(importedPointer));
        BoundCall? importedCall = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(forward.Body,
            expression =>
            {
                if (expression is BoundCall call)
                {
                    importedCall = call;
                }
            }), Is.True);
        Assert.That(importedCall, Is.Not.Null);
        var call = importedCall!;
        var target = consumer.Functions[call.Function];
        Assert.That(target.IsExternal, Is.True);
        Assert.That(target.ModuleImport?.ProducerIdentity,
            Is.EqualTo("standard-pointer-cell-provider"));
        Assert.That(call.Arguments[0].Type,
            Is.EqualTo(importedOutputType));
        Assert.That(call.Arguments[1].Type, Is.EqualTo(importedPointer));

        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(consumer,
            "standard-pointer-cell-consumer", schemaIdentity, 1);
        var providerInputs = new[] { providerInput };
        var loadedConsumer = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "standard-pointer-cell-consumer", providerInputs);
        var loadedForward = loadedConsumer.Functions.Single(
            static function => function.Name == "Forward");
        var loadedForwardCall = (BoundCall)((BoundExpressionStatement)
            loadedForward.Body.Statements[0]).Expression;
        Assert.That(loadedForwardCall.Arguments[0],
            Is.TypeOf<BoundReferenceReborrow>());
        var loadedReborrow
            = (BoundReferenceReborrow)loadedForwardCall.Arguments[0];
        Assert.That(loadedReborrow.Descriptor,
            Is.EqualTo(new BoundRead(0, importedOutputType, default)));
        var loadedForwardProvider
            = loadedConsumer.Functions[loadedForwardCall.Function];
        Assert.That(loadedForwardProvider.ModuleImport?.ProducerIdentity,
            Is.EqualTo("standard-pointer-cell-provider"));
        Assert.That(loadedForwardProvider.Variables[0].Type,
            Is.EqualTo(importedOutputType));
        Assert.That(loadedForwardProvider.Variables[1].Type,
            Is.EqualTo(importedPointer));
        var loadedRun = loadedConsumer.Functions.Single(
            static function => function.Name == "Run");
        var loadedRunCall = (BoundCall)((BoundExpressionStatement)
            loadedRun.Body.Statements[1]).Expression;
        Assert.That(loadedRunCall.Arguments[0],
            Is.TypeOf<BoundReferenceFormation>());
        Assert.That(loadedConsumer.Functions[loadedRunCall.Function].Name,
            Is.EqualTo("Forward"));
        var loadedPublish = loadedConsumer.Functions.Single(
            static function => function.Name == "Publish");
        Assert.That(loadedPublish.ModuleImport?.ProducerIdentity,
            Is.EqualTo("standard-pointer-cell-provider"));
        Assert.That(loadedPublish.Variables[0].Type,
            Is.EqualTo(importedOutputType));
        Assert.That(loadedPublish.Variables[1].Type,
            Is.EqualTo(importedPointer));

        var reemittedConsumer = TrRmExecutableCompiler.EmitStandard(
            loadedConsumer, "standard-pointer-cell-consumer-reemitted",
            schemaIdentity, 1);
        var reloadedConsumer = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reemittedConsumer),
            "standard-pointer-cell-consumer-reemitted", providerInputs);
        var reloadedForward = reloadedConsumer.Functions.Single(
            static function => function.Name == "Forward");
        var reloadedForwardCall = (BoundCall)((BoundExpressionStatement)
            reloadedForward.Body.Statements[0]).Expression;
        Assert.That(reloadedForwardCall.Arguments[0],
            Is.TypeOf<BoundReferenceReborrow>());
        Assert.That(reloadedConsumer.Functions[
                reloadedForwardCall.Function].ModuleImport?.ProducerIdentity,
            Is.EqualTo("standard-pointer-cell-provider"));
        Assert.That(reloadedConsumer.Functions.Single(
                static function => function.Name == "Publish")
            .ModuleImport?.ProducerIdentity,
            Is.EqualTo("standard-pointer-cell-provider"));
        var reloadedRun = reloadedConsumer.Functions.Single(
            static function => function.Name == "Run");
        var reloadedRunCall = (BoundCall)((BoundExpressionStatement)
            reloadedRun.Body.Statements[1]).Expression;
        Assert.That(reloadedRunCall.Arguments[0],
            Is.TypeOf<BoundReferenceFormation>());
        Assert.That(reloadedConsumer.Functions[
                reloadedRunCall.Function].Name,
            Is.EqualTo("Forward"));

        const string incompatibleProviderSource = """
            public void Publish(ref<init ptr<int64>> output,
                ptr<int64> value) {
                *output = value;
            }
            """;
        var incompatibleProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(incompatibleProviderSource));
        Assert.That(incompatibleProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                incompatibleProviderCompilation.Diagnostics));
        var incompatibleProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)incompatibleProviderCompilation.Program!,
            "standard-pointer-cell-provider", schemaIdentity, 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    consumerArtifact),
                "standard-pointer-cell-consumer",
                [new TrRmStandardArtifactInput(schemaIdentity,
                    incompatibleProviderArtifact)]));
    }

    [TestCase(SchemaIdentity)]
    [TestCase(GeneralPredicateGraphsSchemaIdentity)]
    public async Task StandardPointerCellInitializationExecutesAsync(
        string schemaIdentity)
    {
        const string source = """
            public void Publish(ref<init ptr<int32>> output,
                ptr<int32> value) {
                *output = value;
            }

            public ptr<int32> Run(ptr<int32> value) {
                init ptr<int32> pointer;
                Publish(ref init pointer, value);
                return pointer;
            }
            """;
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!,
            "standard-pointer-cell-execution", schemaIdentity, 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            "standard-pointer-cell-execution", []);
        var image = NativeEmitter.EmitLibrary(loaded, ["Run"],
            NativeRuntimePolicy.FailFast);
        var libraryPath = Path.Combine(Path.GetTempPath(),
            $"terrum-standard-pointer-cell-{Guid.NewGuid():N}.dll");
        nint library = 0;
        var backing = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(backing, 42);
            await File.WriteAllBytesAsync(libraryPath, image)
                .ConfigureAwait(false);
            library = NativeLibrary.Load(libraryPath);
            var run = Marshal.GetDelegateForFunctionPointer<PointerFunction>(
                NativeLibrary.GetExport(library, "Run"));
            Assert.That(run(backing), Is.EqualTo(backing));
            Assert.That(Marshal.ReadInt32(backing), Is.EqualTo(42));
        }
        finally
        {
            if (library != 0)
            {
                NativeLibrary.Free(library);
            }

            Marshal.FreeHGlobal(backing);
            await TestFileCleanup.DeleteWithRetryAsync(libraryPath)
                .ConfigureAwait(false);
        }

    }

    [TestCase(LocalFieldLayoutSchemaIdentity)]
    [TestCase(GeneralPredicateGraphsSchemaIdentity)]
    public async Task StandardPointerCellProviderExecutionAsync(
        string schemaIdentity)
    {
        const string providerIdentity = "standard-pointer-cell-exec-provider";
        const string consumerIdentity = "standard-pointer-cell-exec-consumer";
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public void Publish(ref<init ptr<int32>> output,
                    ptr<int32> value) {
                    *output = value;
                }
                """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)providerCompilation.Program!, providerIdentity,
            schemaIdentity, 1);
        var providerInput = new TrRmStandardArtifactInput(
            schemaIdentity, providerArtifact);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, []);
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public ptr<int32> Run(ptr<int32> value) {
                    init ptr<int32> pointer;
                    Publish(ref init pointer, value);
                    return pointer;
                }
                """),
            providerReference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)consumerCompilation.Program!, consumerIdentity,
            schemaIdentity, 1);
        var consumerInput = new TrRmStandardArtifactInput(schemaIdentity,
            consumerArtifact);
        var loadedConsumer = TrRmExecutableCompiler.LoadStandard(
            consumerInput, consumerIdentity, [providerInput]);
        var importedPublish = loadedConsumer.Functions.Single(
            static function => function.Name == "Publish");
        Assert.That(importedPublish.IsExternal, Is.True);
        Assert.That(importedPublish.Variables[0].IsInit, Is.True);

        var reemittedConsumer = TrRmExecutableCompiler.EmitStandard(
            loadedConsumer, consumerIdentity, schemaIdentity, 1);
        var reemittedConsumerInput = new TrRmStandardArtifactInput(
            schemaIdentity, reemittedConsumer);
        var loadedProvider = TrRmExecutableCompiler.LoadStandard(
            providerInput, providerIdentity, []);
        var reemittedProvider = TrRmExecutableCompiler.EmitStandard(
            loadedProvider, providerIdentity, schemaIdentity, 1);
        var reemittedProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, reemittedProvider);
        _ = TrRmExecutableCompiler.LoadStandard(reemittedConsumerInput,
            consumerIdentity, [reemittedProviderInput]);
        var image = NativeModuleCompiler.EmitStandardLibrary(
            reemittedConsumerInput, consumerIdentity,
            [reemittedProviderInput], ["Run"],
            NativeRuntimePolicy.FailFast);
        var libraryPath = Path.Combine(Path.GetTempPath(),
            $"terrum-standard-pointer-cell-provider-{Guid.NewGuid():N}.dll");
        nint library = 0;
        var backing = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(backing, 42);
            await File.WriteAllBytesAsync(libraryPath, image)
                .ConfigureAwait(false);
            library = NativeLibrary.Load(libraryPath);
            var run = Marshal.GetDelegateForFunctionPointer<PointerFunction>(
                NativeLibrary.GetExport(library, "Run"));
            Assert.That(run(backing), Is.EqualTo(backing));
            Assert.That(Marshal.ReadInt32(backing), Is.EqualTo(42));
        }
        finally
        {
            if (library != 0)
            {
                NativeLibrary.Free(library);
            }

            Marshal.FreeHGlobal(backing);
            await TestFileCleanup.DeleteWithRetryAsync(libraryPath)
                .ConfigureAwait(false);
        }

    }

    [TestCase(LocalFieldLayoutSchemaIdentity)]
    [TestCase(GeneralPredicateGraphsSchemaIdentity)]
    public void StandardReferenceOmitsInitializationBodyLocalsFromExportedSignature(
        string schemaIdentity)
    {
        const string providerIdentity
            = "standard-pointer-cell-signature-provider";
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public void Publish(ref<init ptr<int32>> output,
                    ptr<int32> value) {
                    *output = value;
                }

                public ptr<int32> Run(ptr<int32> value) {
                    init ptr<int32> pointer;
                    Publish(ref init pointer, value);
                    return pointer;
                }
                """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)providerCompilation.Program!, providerIdentity,
            schemaIdentity, 1);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, []);
        var importedRun = providerReference.Functions.Single(
            static function => function.Name == "Run");
        Assert.That(importedRun.ParameterCount, Is.EqualTo(1));
        Assert.That(importedRun.Variables.Length, Is.EqualTo(1));
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public ptr<int32> Forward(ptr<int32> value) {
                    unsafe { return Run(value); }
                }
                """), providerReference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
    }

    [Test]
    public async Task GeneralPredicateGraphsProfilePreservesBoundedInitOutputsAsync()
    {
        const string source = """
            public bool Fill(bool publish, int32 value,
                ref<init int32> result) {
                if (publish) {
                    *result = value;
                    return true;
                }
                return false;
            }

            public int32 Run(bool publish, int32 value, int32 fallback) {
                init int32 result;
                if (Fill(publish, value, ref init result)) {
                    return result;
                }
                return fallback;
            }
            """;
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!,
            "general-predicate-init-output", GeneralPredicateGraphsSchemaIdentity,
            1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateGeneralPredicateGraphsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForGeneralPredicateGraphsDraft(artifact));
        Assert.That(TrRmFeatureManifestValidator.Requires(
            tables.Features, tables.Modules, 1,
            TrRmFeatureTableReader.PublishedPredicatesFeatureId), Is.True);
        Assert.That(TrRmFeatureManifestValidator.Requires(
            tables.Features, tables.Modules, 1,
            TrRmFeatureTableReader.GeneralPredicateGraphsFeatureId), Is.False);

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(
                GeneralPredicateGraphsSchemaIdentity, artifact),
            "general-predicate-init-output", []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            "general-predicate-init-output-reemitted",
            GeneralPredicateGraphsSchemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(
                GeneralPredicateGraphsSchemaIdentity, reemitted),
            "general-predicate-init-output-reemitted", []);
        var image = NativeEmitter.EmitLibrary(reloaded, ["Run"],
            NativeRuntimePolicy.FailFast);
        var libraryPath = Path.Combine(Path.GetTempPath(),
            $"terrum-general-predicate-init-{Guid.NewGuid():N}.dll");
        nint library = 0;
        try
        {
            await File.WriteAllBytesAsync(libraryPath, image)
                .ConfigureAwait(false);
            library = NativeLibrary.Load(libraryPath);
            var run = Marshal.GetDelegateForFunctionPointer<
                ConditionalInt32Function>(NativeLibrary.GetExport(
                    library, "Run"));
            Assert.That(run(1, 42, 7), Is.EqualTo(42));
            Assert.That(run(0, 42, 7), Is.EqualTo(7));
        }
        finally
        {
            if (library != 0)
            {
                NativeLibrary.Free(library);
            }

            await TestFileCleanup.DeleteWithRetryAsync(libraryPath)
                .ConfigureAwait(false);
        }
    }

    [TestCase(C1SchemaIdentity)]
    [TestCase(C1M1SchemaIdentity)]
    public void C1ProfilesRoundTripInitializationBodiesWithoutAlias(
        string schemaIdentity)
    {
        const string source = """
            public bool Fill(bool publish, int32 value,
                ref<init int32> result) {
                if (publish) {
                    *result = value;
                    return true;
                }
                return false;
            }

            public int32 Run(bool publish, int32 value, int32 fallback) {
                init int32 result;
                if (Fill(publish, value, ref init result)) {
                    return result;
                }
                return fallback;
            }
            """;
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!,
            "c1-init-output", schemaIdentity, 1);
        var index = StringComparer.Ordinal.Equals(schemaIdentity,
                C1SchemaIdentity)
            ? TrRmArtifactIndex.ForC1Draft(artifact)
            : TrRmArtifactIndex.ForC1M1Draft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(index);
        Assert.That(tables.Modules.Count, Is.EqualTo(1));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            "c1-init-output", []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            "c1-init-output-reemitted", schemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            "c1-init-output-reemitted", []);
        Assert.That(reloaded.Functions.Select(static function =>
            function.Name), Is.EquivalentTo(["Fill", "Run"]));
    }

    [TestCase(GeneralOutputStatePredicateShape.AndThen)]
    [TestCase(GeneralOutputStatePredicateShape.OrElse)]
    [TestCase(GeneralOutputStatePredicateShape.BooleanNot)]
    [TestCase(GeneralOutputStatePredicateShape.EagerAnd)]
    [TestCase(GeneralOutputStatePredicateShape.EagerOr)]
    [TestCase(GeneralOutputStatePredicateShape.EagerXor)]
    [TestCase(GeneralOutputStatePredicateShape.Conditional)]
    public async Task GeneralOutputStatePredicateSurvivesSourceFreeReemissionAsync(
        GeneralOutputStatePredicateShape shape)
    {
        const string source = """
            public bool Fill(bool publish, bool eligible, int32 value,
                ref<init int32> result) {
                if (publish) {
                    *result = value;
                    return true;
                }
                return false;
            }
            """;
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = (BoundProgram)compilation.Program!;
        var predicate = CreateGeneralOutputStatePredicate(shape);
        var expectedOutputPredicate
            = new BoundOutputStatePredicate(3, 2, 1, predicate);
        var functions = program.Functions.Select(function =>
            function.Name == "Fill"
                ? function with {
                    CheckedOutputStatePredicates = [expectedOutputPredicate],
                }
                : function).ToImmutableArray();
        var selectedProgram = program with { Functions = functions };
        var artifact = TrRmExecutableCompiler.EmitStandard(selectedProgram,
            "general-predicate-output-state", GeneralPredicateGraphsSchemaIdentity,
            1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(
                GeneralPredicateGraphsSchemaIdentity, artifact),
            "general-predicate-output-state", []);

        Assert.That(loaded.Functions.Single(static function =>
                function.Name == "Fill").CheckedOutputStatePredicates,
            Is.EqualTo([expectedOutputPredicate]));

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            "general-predicate-output-state-reemitted",
            GeneralPredicateGraphsSchemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(
                GeneralPredicateGraphsSchemaIdentity, reemitted),
            "general-predicate-output-state-reemitted", []);
        Assert.That(reloaded.Functions.Single(static function =>
                function.Name == "Fill").CheckedOutputStatePredicates,
            Is.EqualTo([expectedOutputPredicate]));

        var image = NativeEmitter.EmitLibrary(reloaded, ["Fill"],
            NativeRuntimePolicy.FailFast);
        var libraryPath = Path.Combine(Path.GetTempPath(),
            $"terrum-general-output-state-{Guid.NewGuid():N}.dll");
        nint library = 0;
        try
        {
            await File.WriteAllBytesAsync(libraryPath, image)
                .ConfigureAwait(false);
            library = NativeLibrary.Load(libraryPath);
            var fill = Marshal.GetDelegateForFunctionPointer<
                ConditionalFourInputFunction>(NativeLibrary.GetExport(
                    library, "Fill"));
            Assert.That(fill(1, 1, 42, out var result),
                Is.EqualTo((byte)1));
            Assert.That(result, Is.EqualTo(42));
            Assert.That(fill(0, 1, 42, out _), Is.EqualTo((byte)0));
        }
        finally
        {
            if (library != 0)
            {
                NativeLibrary.Free(library);
            }

            await TestFileCleanup.DeleteWithRetryAsync(libraryPath)
                .ConfigureAwait(false);
        }

        const string consumerSource = """
            public int32 Run(int32 value) {
                return value;
            }
            """;
        var providerInput = new TrRmStandardArtifactInput(
            GeneralPredicateGraphsSchemaIdentity, artifact);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "general-predicate-output-state", []);
        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(consumerSource), providerReference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)consumerCompilation.Program!,
            "general-predicate-output-consumer",
            GeneralPredicateGraphsSchemaIdentity, 1);
        var consumerInput = new TrRmStandardArtifactInput(
            GeneralPredicateGraphsSchemaIdentity, consumerArtifact);
        var providerInputs = new[] { providerInput };
        if (shape == GeneralOutputStatePredicateShape.BooleanNot)
        {
            var replacementPredicate = expectedOutputPredicate with {
                Predicate = CreateGeneralOutputStatePredicate(
                    GeneralOutputStatePredicateShape.AndThen),
            };
            var replacementProgram = program with {
                Functions = program.Functions.Select(function =>
                    function.Name == "Fill"
                        ? function with {
                            CheckedOutputStatePredicates = [
                                replacementPredicate,
                            ],
                        }
                        : function).ToImmutableArray(),
            };
            var replacementArtifact = TrRmExecutableCompiler.EmitStandard(
                replacementProgram, "general-predicate-output-state",
                GeneralPredicateGraphsSchemaIdentity, 1);
            Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler.LoadStandard(consumerInput,
                    "general-predicate-output-consumer", [
                        new TrRmStandardArtifactInput(
                            GeneralPredicateGraphsSchemaIdentity,
                            replacementArtifact),
                    ]));
        }

        var loadedConsumer = TrRmExecutableCompiler.LoadStandard(
            consumerInput, "general-predicate-output-consumer", providerInputs);
        Assert.That(loadedConsumer.Functions.Single(
                static function => function.Name == "Fill")
            .CheckedOutputStatePredicates, Is.EqualTo([expectedOutputPredicate]));

        var reemittedConsumer = TrRmExecutableCompiler.EmitStandard(
            loadedConsumer, "general-predicate-output-consumer-reemitted",
            GeneralPredicateGraphsSchemaIdentity, 1);
        var reloadedConsumer = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(
                GeneralPredicateGraphsSchemaIdentity, reemittedConsumer),
            "general-predicate-output-consumer-reemitted",
            providerInputs);
        Assert.That(reloadedConsumer.Functions.Single(
                static function => function.Name == "Fill")
            .CheckedOutputStatePredicates, Is.EqualTo([expectedOutputPredicate]));

        var loadedFill = reloadedConsumer.Functions.Single(
            static function => function.Name == "Fill");
        var originalReadVariables = new List<int>();
        Assert.That(BoundTreeTraversal.VisitExpressionTree(predicate,
            expression => {
                if (expression is BoundRead read)
                {
                    originalReadVariables.Add(read.Variable);
                }
            }), Is.True);
        var reorderedVariables = ImmutableArray.Create(
            loadedFill.Variables[1], loadedFill.Variables[0],
            loadedFill.Variables[2], loadedFill.Variables[3]);
        var remappedFill = BoundTreeTraversal.RewriteVariables(
            loadedFill, reorderedVariables,
            static variable => variable switch {
                0 => 1,
                1 => 0,
                _ => variable,
            });
        var remappedOutputPredicate
            = remappedFill.CheckedOutputStatePredicates.Single();
        Assert.That((remappedOutputPredicate.FormalOrdinal,
                remappedOutputPredicate.PhaseLocalOrdinal,
                remappedOutputPredicate.State),
            Is.EqualTo((3, 2u, (byte)1)));
        var remappedReadVariables = new List<int>();
        Assert.That(BoundTreeTraversal.VisitExpressionTree(
            remappedOutputPredicate.Predicate, expression => {
                if (expression is BoundRead read)
                {
                    remappedReadVariables.Add(read.Variable);
                }
            }), Is.True);
        Assert.That(remappedReadVariables,
            Is.EqualTo(originalReadVariables.Select(static variable
                => variable switch {
                    0 => 1,
                    1 => 0,
                    _ => variable,
                })));
    }

    private static BoundExpression CreateGeneralOutputStatePredicate(
        GeneralOutputStatePredicateShape shape)
    {
        var publish = new BoundRead(0, PrimitiveType.Bool, default);
        var eligible = new BoundRead(1, PrimitiveType.Bool, default);
        var andThen = new BoundBinary(TokenKind.AmpersandAmpersand,
            publish, eligible, PrimitiveType.Bool, default);
        var eagerAnd = new BoundBinary(TokenKind.Ampersand,
            publish, eligible, PrimitiveType.Bool, default);
        return shape switch {
            GeneralOutputStatePredicateShape.AndThen => andThen,
            GeneralOutputStatePredicateShape.OrElse =>
                new BoundBinary(TokenKind.PipePipe, publish, andThen,
                    PrimitiveType.Bool, default),
            GeneralOutputStatePredicateShape.BooleanNot =>
                new BoundUnary(TokenKind.Bang,
                    new BoundUnary(TokenKind.Bang, publish,
                        PrimitiveType.Bool, default),
                    PrimitiveType.Bool, default),
            GeneralOutputStatePredicateShape.EagerAnd => eagerAnd,
            GeneralOutputStatePredicateShape.EagerOr =>
                new BoundBinary(TokenKind.Pipe, publish, eagerAnd,
                    PrimitiveType.Bool, default),
            GeneralOutputStatePredicateShape.EagerXor =>
                new BoundBinary(TokenKind.Caret, publish,
                    new BoundBinary(TokenKind.Ampersand, publish,
                        new BoundUnary(TokenKind.Bang, eligible,
                            PrimitiveType.Bool, default),
                        PrimitiveType.Bool, default),
                    PrimitiveType.Bool, default),
            GeneralOutputStatePredicateShape.Conditional =>
                new BoundConditional(publish, eligible, andThen,
                    PrimitiveType.Bool, default),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }

    [Test]
    public async Task StandardBooleanInitOutputStoresSupportMultipleDestinationsAsync()
    {
        const string source = """
            public bool Fill(bool publish, int32 first, int32 second,
                ref<init int32> firstResult,
                ref<init int32> secondResult) {
                if (publish) {
                    *firstResult = first;
                    *secondResult = second;
                    return true;
                }
                return false;
            }
            """;
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!,
            "init-reference-multiple-outputs", SchemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(artifact));
        Assert.That(tables.Contracts.GetOutputStateContracts(1)
            .Select(static state =>
                (state.ParameterOrdinal, state.PredicateGraphRow, state.State)),
            Is.EqualTo(new[] {
                (3, 1, (byte)1),
                (3, 2, (byte)0),
                (4, 1, (byte)1),
                (4, 2, (byte)0),
            }));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, artifact),
            "init-reference-multiple-outputs", []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            "init-reference-multiple-outputs-reemitted", SchemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, reemitted),
            "init-reference-multiple-outputs-reemitted", []);
        var image = NativeEmitter.EmitLibrary(reloaded, ["Fill"],
            NativeRuntimePolicy.FailFast);
        var libraryPath = Path.Combine(Path.GetTempPath(),
            $"terrum-standard-init-multiple-{Guid.NewGuid():N}.dll");
        nint library = 0;
        try
        {
            await File.WriteAllBytesAsync(libraryPath, image)
                .ConfigureAwait(false);
            library = NativeLibrary.Load(libraryPath);
            var fill = Marshal.GetDelegateForFunctionPointer<
                ConditionalTwoOutputFunction>(
                NativeLibrary.GetExport(library, "Fill"));
            Assert.That(fill(1, 42, 7, out var firstResult,
                out var secondResult),
                Is.EqualTo((byte)1));
            Assert.That((firstResult, secondResult), Is.EqualTo((42, 7)));

            Assert.That(fill(0, 42, 7, out _, out _),
                Is.EqualTo((byte)0));
        }
        finally
        {
            if (library != 0)
            {
                NativeLibrary.Free(library);
            }

            await TestFileCleanup.DeleteWithRetryAsync(libraryPath)
                .ConfigureAwait(false);
        }
    }

    [Test]
    public async Task StandardBooleanInitCallBranchesPreservePublicationAndExecuteAsync()
    {
        const string source = """
            public bool Fill(bool publish, int32 value,
                ref<init int32> result) {
                if (publish) {
                    *result = value;
                    return true;
                }
                return false;
            }

            public int32 Run(bool publish, int32 value, int32 fallback) {
                init int32 result;
                if (Fill(publish, value, ref init result)) {
                    return result;
                }
                return fallback;
            }
            """;
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)compilation.Program!,
            "init-reference-conditional-call", SchemaIdentity, 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, artifact),
            "init-reference-conditional-call", []);
        var run = loaded.Functions.Single(static function =>
            function.Name == "Run");
        Assert.That(run.Body.Statements[0],
            Is.TypeOf<BoundVacantLocal>());
        Assert.That(run.Body.Statements[1], Is.TypeOf<BoundIf>());
        Assert.That(run.Body.Statements[2], Is.TypeOf<BoundReturn>());

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            "init-reference-conditional-call-reemitted", SchemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, reemitted),
            "init-reference-conditional-call-reemitted", []);
        var image = NativeEmitter.EmitLibrary(reloaded, ["Run"],
            NativeRuntimePolicy.FailFast);
        var libraryPath = Path.Combine(Path.GetTempPath(),
            $"terrum-standard-init-conditional-{Guid.NewGuid():N}.dll");
        nint library = 0;
        try
        {
            await File.WriteAllBytesAsync(libraryPath, image)
                .ConfigureAwait(false);
            library = NativeLibrary.Load(libraryPath);
            var runFunction = Marshal.GetDelegateForFunctionPointer<
                ConditionalInt32Function>(NativeLibrary.GetExport(library,
                "Run"));
            Assert.That(runFunction(1, 42, 7), Is.EqualTo(42));
            Assert.That(runFunction(0, 42, 7), Is.EqualTo(7));
        }
        finally
        {
            if (library != 0)
            {
                NativeLibrary.Free(library);
            }

            await TestFileCleanup.DeleteWithRetryAsync(libraryPath)
                .ConfigureAwait(false);
        }
    }

    [Test]
    public void StandardInitReferenceProviderBindsAndReemitsConsumerCall()
    {
        const string providerSource = """
            public void Fill(ref<init int32> result, int32 value) {
                *result = value;
            }
            """;
        const string consumerSource = """
            public int32 Run(int32 value) {
                init int32 result;
                Fill(ref init result, value);
                return result;
            }
            """;
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)providerCompilation.Program!,
            "init-provider", SchemaIdentity, 1);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(SchemaIdentity, providerArtifact),
            "init-provider", []);

        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(consumerSource), providerReference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)consumerCompilation.Program!,
            "init-consumer", SchemaIdentity, 1);
        var providerInputs = new[] {
            new TrRmStandardArtifactInput(SchemaIdentity, providerArtifact),
        };
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, consumerArtifact),
            "init-consumer", providerInputs);
        var importedFill = loaded.Functions.Single(static function =>
            function.Name == "Fill");
        Assert.That(importedFill.IsExternal, Is.True);
        Assert.That(importedFill.ModuleImport?.ProducerIdentity,
            Is.EqualTo("init-provider"));
        Assert.That(importedFill.Variables[0].IsInit, Is.True);

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            "init-consumer", SchemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, reemitted),
            "init-consumer", providerInputs);
        Assert.That(reloaded.Functions.Single(static function =>
            function.Name == "Fill").ModuleImport?.ProducerIdentity,
            Is.EqualTo("init-provider"));

        const string incompatibleProviderSource = """
            public void Fill(ref<init int64> result, int64 value) {
                *result = value;
            }
            """;
        var incompatibleProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(incompatibleProviderSource));
        Assert.That(incompatibleProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                incompatibleProviderCompilation.Diagnostics));
        var incompatibleProviderArtifact
            = TrRmExecutableCompiler.EmitStandard(
                (BoundProgram)incompatibleProviderCompilation.Program!,
                "init-provider", SchemaIdentity, 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    consumerArtifact),
                "init-consumer",
                [new TrRmStandardArtifactInput(SchemaIdentity,
                    incompatibleProviderArtifact)]));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    consumerArtifact),
                "init-consumer",
                [new TrRmStandardArtifactInput(
                    "trrm/1-draft-layout-queries-1",
                    providerArtifact)]));
    }

    [Test]
    public void StandardBooleanInitOutputStatesBindAndSurviveReemission()
    {
        const string providerSource = """
            public bool Fill(bool publish, int32 value,
                ref<init int32> result) {
                if (publish) {
                    *result = value;
                    return true;
                }
                return false;
            }
            """;
        const string consumerSource = """
            public int32 Run(bool publish, int32 value, int32 fallback) {
                init int32 result;
                if (Fill(publish, value, ref init result)) {
                    return result;
                }
                return fallback;
            }
            """;
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)providerCompilation.Program!,
            "boolean-init-provider", SchemaIdentity, 1);
        var providerInput = new TrRmStandardArtifactInput(SchemaIdentity,
            providerArtifact);
        var providerTables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(new TrRmArtifactIndex(
                providerArtifact));
        var providerOutputStates = providerTables.Contracts
            .GetOutputStateContracts(1);
        Assert.That(providerOutputStates.Select(static state =>
                (state.ParameterOrdinal, state.State)),
            Is.EqualTo(new[] { (2, (byte)1), (2, (byte)0) }));

        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "boolean-init-provider", []);
        var importedFill = providerReference.Functions.Single(
            static function => function.Name == "Fill");
        Assert.That(importedFill.CheckedOutputStates.Length, Is.EqualTo(2));

        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(consumerSource), providerReference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var boundFill = ((BoundProgram)consumerCompilation.Program!)
            .Functions.Single(static function => function.Name == "Fill");
        Assert.That(boundFill.IsExternal, Is.True);
        Assert.That(boundFill.ModuleImport?.ProducerIdentity,
            Is.EqualTo("boolean-init-provider"));
        Assert.That(boundFill.CheckedOutputSelectors,
            Is.EqualTo(importedFill.CheckedOutputSelectors));
        Assert.That(boundFill.CheckedOutputStates,
            Is.EqualTo(importedFill.CheckedOutputStates));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)consumerCompilation.Program!,
            "boolean-init-consumer", SchemaIdentity, 1);
        var providerInputs = new[] { providerInput };
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, consumerArtifact),
            "boolean-init-consumer", providerInputs);
        var loadedFill = loaded.Functions.Single(
            static function => function.Name == "Fill");
        Assert.That(loadedFill.CheckedOutputStates,
            Is.EqualTo(importedFill.CheckedOutputStates));

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            "boolean-init-consumer", SchemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, reemitted),
            "boolean-init-consumer", providerInputs);
        Assert.That(reloaded.Functions.Single(
                static function => function.Name == "Fill")
            .CheckedOutputStates, Is.EqualTo(loadedFill.CheckedOutputStates));

        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var fillIndex = Array.FindIndex(providerProgram.Functions.ToArray(),
            static function => function.Name == "Fill");
        var replacementProgram = providerProgram with {
            Functions = providerProgram.Functions.SetItem(fillIndex,
                providerProgram.Functions[fillIndex] with {
                    CheckedOutputSelectors = [],
                    CheckedOutputStates = [],
                    HasStandardCheckedOutputStateMetadata = true,
                }),
        };
        var replacementArtifact = TrRmExecutableCompiler.EmitStandard(
            replacementProgram, "boolean-init-provider",
            SchemaIdentity, 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    consumerArtifact),
                "boolean-init-consumer",
                [new TrRmStandardArtifactInput(SchemaIdentity,
                    replacementArtifact)]));
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern nint VirtualAlloc(nint address, nuint size,
        uint allocationType, uint protection);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualProtect(nint address, nuint size,
        uint protection, out uint previousProtection);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFree(nint address, nuint size,
        uint freeType);
}
