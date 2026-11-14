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
    private const string GeneralPredicateGraphsSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-1";
    private const string C1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1";
    private const string C1M1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1";

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
    private delegate nint PointerFunction(nint value);

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

    [Test]
    public async Task GeneralPredicateDefaultOutputPublishesAcrossProviderAsync()
    {
        const string providerIdentity = "general-init-output-provider";
        const string consumerIdentity = "general-init-output-consumer";
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
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var expectedOutputPredicate = new BoundOutputStatePredicate(
            2, 2, 1,
            new BoundRead(0, PrimitiveType.Bool, default));
        providerProgram = providerProgram with {
            Functions = providerProgram.Functions.Select(function =>
                function.Name == "Fill"
                    ? function with {
                        CheckedOutputStatePredicates = [
                            expectedOutputPredicate,
                        ],
                    }
                    : function).ToImmutableArray(),
        };
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, providerIdentity,
            GeneralPredicateGraphsSchemaIdentity, 1);
        var providerInput = new TrRmStandardArtifactInput(
            GeneralPredicateGraphsSchemaIdentity, providerArtifact);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, []);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                int32 Run(bool publish, int32 value, int32 fallback) {
                    init int32 result;
                    if (Fill(publish, value, ref init result)) {
                        return result;
                    }
                    return fallback;
                }
                int32 Main() {
                    return Run(true, 42, 7);
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
            Is.EqualTo([expectedOutputPredicate]));
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
            Is.EqualTo([expectedOutputPredicate]));
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
        var providerInputs = new[] { providerInput };
        var loadedConsumer = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(
                GeneralPredicateGraphsSchemaIdentity, consumerArtifact),
            "general-predicate-output-consumer", providerInputs);
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
}
