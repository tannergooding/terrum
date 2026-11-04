// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmLocalAggregateLayoutNativeTests
{
    private const string SchemaIdentity
        = "trrm/1-draft-local-aggregate-layout-execution-1";

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetValueDelegate();

    [TestCase(false)]
    [TestCase(true)]
    public async Task StandardL1MethodProvidersUseImportedNominalArrayFieldsAsync(
        bool relocateTypeProvider)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var (rootArtifact, methodProviderArtifact, expectedTypeArtifact)
            = CreateImportedNominalMethodArtifacts();
        var actualTypeArtifact = relocateTypeProvider
            ? CreateNominalTypeProviderArtifact(relocate: true)
            : expectedTypeArtifact;
        var rootInput = new TrRmStandardArtifactInput(SchemaIdentity,
            rootArtifact);
        var providers = new[] {
            new TrRmStandardArtifactInput(SchemaIdentity,
                methodProviderArtifact),
            new TrRmStandardArtifactInput(SchemaIdentity,
                actualTypeArtifact),
        };
        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            "NominalRoot", providers);
        var closure = loaded.StandardMethodProviderClosure!;
        Assert.That(closure.Modules, Has.Length.EqualTo(3));
        Assert.That(closure.MethodEdges, Has.Length.EqualTo(1));
        Assert.That(closure.TypeEdges, Has.Length.EqualTo(4));
        var root = closure.Modules.Single(static module =>
            module.Identity == "NominalRoot");
        var methods = closure.Modules.Single(static module =>
            module.Identity == "NominalMethods");
        var types = closure.Modules.Single(static module =>
            module.Identity == "NominalTypes");
        Assert.That(closure.Artifacts[types.ArtifactIndex]
            .ArtifactSnapshot.ToArray(), Is.EqualTo(actualTypeArtifact));
        var methodEdge = closure.MethodEdges[0];
        Assert.That(methodEdge.ProducerIdentity,
            Is.EqualTo("NominalMethods"));
        Assert.That(methodEdge.ExportKey, Is.EqualTo("Read"));
        Assert.That(root.Program.Functions[methodEdge.Expected.FunctionIndex]
            .ModuleImport?.ExportKey, Is.EqualTo("Read"));
        var implementation = methods.Program.Functions[
            methodEdge.Actual.FunctionIndex];
        var parameterTypeIndex = (int)implementation.Variables[0].Type - 100;
        var parameter = methods.Program.Structures[parameterTypeIndex];
        var parameterOrigin = parameter.StandardTypeOrigin
            ?? throw new InvalidOperationException(
                "The provider MethodSig parameter must retain its TypeDef Import origin.");
        Assert.That(parameter.Name, Is.EqualTo("Packet"));
        Assert.That(parameterOrigin.Provider.ModuleIdentity,
            Is.EqualTo("NominalTypes"));
        Assert.That(parameterOrigin.Expectations.Any(
            static expectation =>
                expectation.UsingModule.ModuleIdentity == "NominalMethods"
                && expectation.ExportKey == "PacketKey"), Is.True);
        Assert.That(implementation.Variables.Any(variable =>
            NativeFixedArrayTypes.IsType(variable.Type)
            && methods.Program.NativeFixedArrayTypes[
                NativeFixedArrayTypes.Index(variable.Type)].ElementType
                    == implementation.Variables[0].Type), Is.True);
        foreach (var consumer in new[] { root, methods })
        {
            var expectedConsumerArtifact = consumer.Identity == "NominalRoot"
                ? rootArtifact : methodProviderArtifact;
            var edges = closure.TypeEdges.Where(edge =>
                edge.ConsumerArtifactIndex == consumer.ArtifactIndex
                && edge.ConsumerModuleRow == consumer.ModuleRow).ToArray();
            Assert.That(edges.Select(static edge => edge.ExportKey),
                Is.EquivalentTo(new[] { "PacketKey", "LeafKey" }));
            Assert.That(edges.All(edge =>
                edge.ProviderArtifactIndex == types.ArtifactIndex
                && edge.ProviderModuleRow == types.ModuleRow), Is.True);
            foreach (var structure in consumer.Program.Structures.Where(
                static structure => structure.StandardTypeOrigin is not null))
            {
                var origin = structure.StandardTypeOrigin!;
                Assert.That(origin.Expectations.Any(expectation =>
                    expectation.UsingModule.ModuleIdentity == consumer.Identity
                    && edges.Any(edge =>
                        edge.ImportRow == expectation.ImportRow
                        && edge.ExpectedDefinitionId
                            == expectation.ExpectedDefinitionId
                        && edge.ActualDefinitionId
                            == expectation.ProviderDefinitionId)), Is.True);
                var originalExpectation = origin.Expectations.First(
                    expectation =>
                        expectation.UsingModule.ModuleIdentity
                            == consumer.Identity);
                Assert.That(originalExpectation.UsingModule
                    .ArtifactSnapshot.ToArray(),
                    Is.EqualTo(expectedConsumerArtifact));
            }
        }
        Assert.That(root.Program.Structures.Single(
            static structure => structure.Name == "Holder").Size,
            Is.EqualTo(methods.Program.Structures.Single(
                static structure => structure.Name == "Holder").Size));
        if (relocateTypeProvider)
        {
            Assert.That(closure.TypeEdges.All(static edge =>
                edge.ExpectedDefinitionId != edge.ActualDefinitionId),
                Is.True);
            Assert.That(types.Program.Structures.Single(
                static structure => structure.Name == "Holder").Size,
                Is.EqualTo(root.Program.Structures.Single(
                    static structure => structure.Name == "Holder").Size));
        }

        var reemittedRootArtifact = TrRmExecutableCompiler.EmitStandard(
            loaded, "NominalRoot", SchemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity,
                reemittedRootArtifact), "NominalRoot", providers);
        var reemittedClosure = reloaded.StandardMethodProviderClosure
            ?? throw new InvalidOperationException(
                "L1 re-emission must retain provider origins.");
        Assert.That(reemittedClosure.TypeEdges.Select(static edge =>
                (edge.ConsumerArtifactIndex, edge.ConsumerModuleRow,
                    edge.ImportRow, edge.ExpectedDefinitionId,
                    edge.ActualDefinitionId, edge.ExportKey)),
            Is.EquivalentTo(closure.TypeEdges.Select(static edge =>
                (edge.ConsumerArtifactIndex, edge.ConsumerModuleRow,
                    edge.ImportRow, edge.ExpectedDefinitionId,
                    edge.ActualDefinitionId, edge.ExportKey))));
        Assert.That(reemittedClosure.MethodEdges.Select(static edge =>
                (edge.ConsumerModuleIdentity, edge.ImportRow,
                    edge.Expected.DefinitionId, edge.Actual.DefinitionId,
                    edge.ExportKey)),
            Is.EquivalentTo(closure.MethodEdges.Select(static edge =>
                (edge.ConsumerModuleIdentity, edge.ImportRow,
                    edge.Expected.DefinitionId, edge.Actual.DefinitionId,
                    edge.ExportKey))));

        WriteNativeTestIdentity("NominalRoot", rootArtifact);
        WriteNativeTestIdentity("NominalMethods", methodProviderArtifact);
        WriteNativeTestIdentity("NominalTypes", actualTypeArtifact);

        Assert.That(NativeModuleCompiler.ExecuteStandard(rootInput,
            "NominalRoot", providers, NativeRuntimePolicy.FailFast),
            Is.EqualTo(62));
        var image = NativeModuleCompiler.EmitStandardExecutable(rootInput,
            "NominalRoot", providers, NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1-imported-nominal-");
        try
        {
            await AssertNativeImageExitCodeAsync(image,
                Path.Combine(directory, "program.exe"), "NominalRoot", 62)
                .ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }

        Assert.That(root.Program.Functions[methodEdge.Expected.FunctionIndex]
            .ModuleImport?.ExportKey, Is.EqualTo("Read"));
        Assert.That(parameterOrigin.Expectations, Is.Not.Empty);
        Assert.That(closure.TypeEdges, Has.Length.EqualTo(4));
    }

    [Test]
    public async Task StandardL1RootOwnedBodyChangesSurviveProviderReemissionAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var (rootArtifact, providerArtifact)
            = TrRmLocalAggregateLayoutProducerTests
                .CreateL1MethodProviderArtifacts();
        var providers = new[] {
            new TrRmStandardArtifactInput(SchemaIdentity, providerArtifact),
        };
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
            "Root", providers);
        var firstFunctionIndex = loaded.Functions.IndexOf(
            loaded.Functions.Single(static function =>
                function.Name == "First"));
        var replacementCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                int32 Replacement() {
                    return int32(9);
                }
                """), new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
            });
        Assert.That(replacementCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                replacementCompilation.Diagnostics));
        var replacementBody = replacementCompilation.Program!
            .Functions.Single().Body;
        var changedProgram = loaded with {
            Functions = loaded.Functions.SetItem(firstFunctionIndex,
                loaded.Functions[firstFunctionIndex] with {
                    Body = replacementBody,
                }),
        };
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(
            changedProgram, "Root", SchemaIdentity, 1);
        var reemittedInput = new TrRmStandardArtifactInput(SchemaIdentity,
            reemittedArtifact);
        Assert.That(NativeModuleCompiler.ExecuteStandard(reemittedInput,
            "Root", providers, NativeRuntimePolicy.FailFast),
            Is.EqualTo(9));
        var image = NativeModuleCompiler.EmitStandardExecutable(
            reemittedInput, "Root", providers, NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1-reemitted-root-");
        try
        {
            await AssertNativeImageExitCodeAsync(image,
                Path.Combine(directory, "program.exe"), "Root", 9)
                .ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public void StandardL1ImportedNominalMethodRejectsSameSizeIncompatibleTypeProvider()
    {
        var (rootArtifact, methodProviderArtifact, expectedTypeArtifact)
            = CreateImportedNominalMethodArtifacts();
        var incompatibleTypeProvider = CreateNominalTypeProviderArtifact(
            relocate: true, unsignedElements: true);
        var expectedTypes = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity,
                expectedTypeArtifact), "NominalTypes", []);
        var incompatibleTypes = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity,
                incompatibleTypeProvider), "NominalTypes", []);
        foreach (var name in new[] { "Packet", "Leaf" })
        {
            Assert.That(incompatibleTypes.Structures.Single(
                structure => structure.Name == name).Size,
                Is.EqualTo(expectedTypes.Structures.Single(
                    structure => structure.Name == name).Size));
        }
        _ = Assert.Throws<InvalidDataException>(() =>
            NativeModuleCompiler.EmitStandardExecutable(
                new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
                "NominalRoot", [
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        methodProviderArtifact),
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        incompatibleTypeProvider),
                ], NativeRuntimePolicy.FailFast));
    }

    [TestCase(0, 12)]
    [TestCase(7, 19)]
    public async Task StandardL1MethodProvidersExecuteExactExportTargetsAsync(
        int adjustment, int expectedResult)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var (rootArtifact, _) = TrRmLocalAggregateLayoutProducerTests
            .CreateL1MethodProviderArtifacts();
        var providerArtifact = CreateMethodProviderArtifact(adjustment);
        var rootInput = new TrRmStandardArtifactInput(SchemaIdentity,
            rootArtifact);
        var providerInput = new TrRmStandardArtifactInput(SchemaIdentity,
            providerArtifact);
        var providers = new[] { providerInput };
        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput, "Root",
            providers);
        var closure = loaded.StandardMethodProviderClosure!;
        var edge = closure.MethodEdges.Single(static edge =>
            edge.ExportKey == "Actual");
        var provider = closure.Modules.Single(static module =>
            module.Identity == "Provider");
        Assert.That(provider.Program.Functions[edge.Actual.FunctionIndex]
            .Name, Is.EqualTo("First"));
        Assert.That(provider.Program.Functions[edge.Actual.FunctionIndex]
            .DirectNativeImport, Is.Null);
        Assert.That(loaded.Functions.Any(static function =>
            function.Name == "First" && !function.IsExternal), Is.True);
        Assert.That(loaded.Structures[0].Name,
            Is.EqualTo(provider.Program.Structures[0].Name));
        Assert.That(loaded.Structures[0].Size,
            Is.Not.EqualTo(provider.Program.Structures[0].Size));
        WriteNativeTestIdentity("Root", rootArtifact);
        WriteNativeTestIdentity("Provider", providerArtifact);

        Assert.That(NativeModuleCompiler.ExecuteStandard(rootInput, "Root",
            providers, NativeRuntimePolicy.FailFast),
            Is.EqualTo(expectedResult));
        var image = NativeModuleCompiler.EmitStandardExecutable(rootInput,
            "Root", providers, NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1-method-provider-");
        try
        {
            await AssertNativeImageExitCodeAsync(image,
                Path.Combine(directory, "program.exe"), "Root",
                expectedResult).ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }

        // Native lowering must not consume or erase the retained expectations.
        Assert.That(loaded.Functions[edge.Expected.FunctionIndex]
            .ModuleImport?.ExportKey, Is.EqualTo("Actual"));
        Assert.That(closure.MethodEdges, Has.Length.EqualTo(2));
    }

    [Test]
    public async Task StandardL1MethodProviderLibraryExportsOnlyRootImplementationsAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var (executableRoot, _) = TrRmLocalAggregateLayoutProducerTests
            .CreateL1MethodProviderArtifacts();
        var rootArtifact = MakeNoEntryPublicRoot(executableRoot);
        var providerArtifact = CreateMethodProviderArtifact(0);
        var rootInput = new TrRmStandardArtifactInput(SchemaIdentity,
            rootArtifact);
        var providers = new[] {
            new TrRmStandardArtifactInput(SchemaIdentity, providerArtifact),
        };
        _ = Assert.Throws<InvalidDataException>(() =>
            NativeModuleCompiler.EmitStandardExecutable(rootInput, "Root",
                providers, NativeRuntimePolicy.FailFast));
        foreach (var export in new[] {
            "First", "Second", "Actual", "UnusedActual", "Expected",
        })
        {
            _ = Assert.Throws<ArgumentException>(() =>
                NativeModuleCompiler.EmitStandardLibrary(rootInput, "Root",
                    providers, [export], NativeRuntimePolicy.FailFast));
        }

        var image = NativeModuleCompiler.EmitStandardLibrary(rootInput,
            "Root", providers, ["Main"], NativeRuntimePolicy.FailFast);
        WriteNativeTestIdentity("Root", rootArtifact);
        WriteNativeTestIdentity("Provider", providerArtifact);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1-method-library-");
        try
        {
            var imagePath = Path.Combine(directory, "library.dll");
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            var library = NativeLibrary.Load(imagePath);
            try
            {
                var function = Marshal.GetDelegateForFunctionPointer<
                    GetValueDelegate>(NativeLibrary.GetExport(library,
                    "Main"));
                Assert.That(function(), Is.EqualTo(12));
                foreach (var export in new[] {
                    "First", "Second", "Actual", "UnusedActual", "Expected",
                })
                {
                    Assert.That(NativeLibrary.TryGetExport(library, export,
                        out _), Is.False);
                }
                TestContext.WriteLine(
                    $"L1 provider library: root=Root; provider=Provider; exports=Main; expected=12; actual=12; image-sha256={Convert.ToHexString(SHA256.HashData(image))}.");
            }
            finally
            {
                NativeLibrary.Free(library);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public void StandardL1MethodProvidersFailBeforeNativePublication()
    {
        var (rootArtifact, _) = TrRmLocalAggregateLayoutProducerTests
            .CreateL1MethodProviderArtifacts();
        var rootInput = new TrRmStandardArtifactInput(SchemaIdentity,
            rootArtifact);
        _ = Assert.Throws<InvalidDataException>(() =>
            NativeModuleCompiler.EmitStandardExecutable(rootInput, "Root",
                [], NativeRuntimePolicy.FailFast));
        var incompatible = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Marker {
                public uint8 Value;
            }

            public int32 Actual(int32 first, uint8 second) {
                return first;
            }

            public int32 UnusedActual(int32 value) {
                return value;
            }
            """), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(incompatible.HasErrors, Is.False,
            string.Join(Environment.NewLine, incompatible.Diagnostics));
        var provider = new TrRmStandardArtifactInput(SchemaIdentity,
            TrRmExecutableCompiler.EmitStandard(incompatible.Program!,
                "Provider", SchemaIdentity, 1));
        _ = Assert.Throws<InvalidDataException>(() =>
            NativeModuleCompiler.EmitStandardExecutable(rootInput, "Root",
                [provider], NativeRuntimePolicy.FailFast));
        var libraryInput = new TrRmStandardArtifactInput(SchemaIdentity,
            MakeNoEntryPublicRoot(rootArtifact));
        _ = Assert.Throws<InvalidDataException>(() =>
            NativeModuleCompiler.EmitStandardLibrary(libraryInput, "Root",
                [provider], ["Main"], NativeRuntimePolicy.FailFast));
    }

    [Test]
    public async Task StandardL1ClosedRootExecutesThroughJitAndAotAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string moduleIdentity = "l1-closed-root-native";
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            internal struct Pair {
                public mut int32 Value;

                internal Pair() {
                    Value = int32(0);
                }
            }

            int32 Main() {
                mut Pair pair = new Pair();
                pair.Value = int32(42);
                return pair.Value;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, SchemaIdentity, moduleTargetRequirement: 1);
        var input = new TrRmStandardArtifactInput(SchemaIdentity, artifact);
        WriteNativeTestIdentity(moduleIdentity, artifact);
        TestContext.WriteLine(
            $"L1 closed-root source-sha256={Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source)))}.");

        var jitResult = NativeModuleCompiler.ExecuteStandard(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast);
        Assert.That(jitResult, Is.EqualTo(42));
        TestContext.WriteLine(
            $"L1 closed-root JIT: expected=42; actual={jitResult}.");

        var image = NativeModuleCompiler.EmitStandardExecutable(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1-native-");
        try
        {
            var imagePath = Path.Combine(directory, "program.exe");
            await AssertNativeImageExitCodeAsync(image, imagePath,
                moduleIdentity, 42).ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task StandardL1NestedArrayFieldPathExecutesThroughJitAndAotAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string moduleIdentity = "l1-nested-array-field-path-native";
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Leaf {
                public mut uint8 Value;

                internal Leaf() {
                    Value = uint8(0);
                }
            }

            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Item {
                public mut Leaf Nested;

                internal Item() {
                    Nested = new Leaf();
                }
            }

            int32 Main() {
                mut Item seed = new Item();
                mut [Item; 2] items = [seed; 2];
                mut uint8 rhs = uint8(42);
                items[0].Nested.Value = rhs;
                return int32(items[0].Nested.Value);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, SchemaIdentity, moduleTargetRequirement: 1);
        var input = new TrRmStandardArtifactInput(SchemaIdentity, artifact);
        WriteNativeTestIdentity(moduleIdentity, artifact);
        TestContext.WriteLine(
            $"L1 nested field-path source-sha256={Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source)))}.");

        var jitResult = NativeModuleCompiler.ExecuteStandard(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast);
        Assert.That(jitResult, Is.EqualTo(42));
        TestContext.WriteLine(
            $"L1 nested field-path JIT: expected=42; actual={jitResult}.");

        var image = NativeModuleCompiler.EmitStandardExecutable(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1-nested-field-path-");
        try
        {
            var imagePath = Path.Combine(directory, "program.exe");
            await AssertNativeImageExitCodeAsync(image, imagePath,
                moduleIdentity, 42).ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task StandardL1MultiDimensionalRepeatFieldPathExecutesThroughJitAndAotAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string moduleIdentity = "l1-multidimensional-repeat-field-path-native";
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Leaf {
                public mut uint8 Value;

                internal Leaf() {
                    Value = uint8(0);
                }
            }

            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Item {
                public mut Leaf Nested;

                internal Item() {
                    Nested = new Leaf();
                }
            }

            int32 Main() {
                mut Item seed = new Item();
                mut [[Item; 2]; 2] rows = [[seed; 2]; 2];
                mut uint8 rhs = uint8(42);
                rows[0][1].Nested.Value = rhs;
                return int32(rows[0][1].Nested.Value);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, SchemaIdentity, moduleTargetRequirement: 1);
        var input = new TrRmStandardArtifactInput(SchemaIdentity, artifact);
        WriteNativeTestIdentity(moduleIdentity, artifact);
        TestContext.WriteLine(
            $"L1 multidimensional field-path source-sha256={Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source)))}.");

        var jitResult = NativeModuleCompiler.ExecuteStandard(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast);
        Assert.That(jitResult, Is.EqualTo(42));
        TestContext.WriteLine(
            $"L1 multidimensional field-path JIT: expected=42; actual={jitResult}.");

        var image = NativeModuleCompiler.EmitStandardExecutable(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1-multidimensional-field-path-");
        try
        {
            var imagePath = Path.Combine(directory, "program.exe");
            await AssertNativeImageExitCodeAsync(image, imagePath,
                moduleIdentity, 42).ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task StandardL1EffectfulIndexAndRhsPreserveNestedFieldPathThroughJitAndAotAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string moduleIdentity = "l1-effectful-index-rhs-field-path-native";
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1, Alignment: 32)]
            public struct Leaf {
                public mut uint8 Before;
                public mut uint8 Value;
                public mut uint8 After;

                internal Leaf() {
                    Before = uint8(3);
                    Value = uint8(0);
                    After = uint8(4);
                }
            }

            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1, Alignment: 32)]
            public struct Item {
                public mut Leaf Nested;

                internal Item() {
                    Nested = new Leaf();
                }
            }

            uint8 MakeValue(int32 value) {
                return uint8(value);
            }

            int32 Main() {
                mut Item seed = new Item();
                mut [[Item; 2]; 2] rows = [[seed; 2]; 2];
                mut int32 index = 0;
                rows[0][index++].Nested.Value = MakeValue(
                    index == 1 ? index++ : index++);
                index = int32(0);
                rows[1][index++].Nested.Value = MakeValue(
                    index == 1 ? index++ : index++);
                return int32(rows[0][0].Nested.Value) * 1000
                    + int32(rows[1][0].Nested.Value) * 100
                    + int32(rows[0][0].Nested.Before) * 10
                    + int32(rows[0][0].Nested.After) * 3
                    + int32(rows[1][0].Nested.Before) * 2
                    + int32(rows[1][0].Nested.After)
                    + int32(rows[0][1].Nested.Value)
                    + int32(rows[1][1].Nested.Value)
                    + index;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var sourceResult = NativeJit.Execute(program,
            NativeRuntimePolicy.FailFast);
        Assert.That(sourceResult, Is.EqualTo(1154));
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, SchemaIdentity, moduleTargetRequirement: 1);
        var input = new TrRmStandardArtifactInput(SchemaIdentity, artifact);
        WriteNativeTestIdentity(moduleIdentity, artifact);
        TestContext.WriteLine(
            $"L1 effectful-index field-path source-sha256={Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source)))}.");

        var jitResult = NativeModuleCompiler.ExecuteStandard(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast);
        Assert.That(jitResult, Is.EqualTo(1154));
        TestContext.WriteLine(
            $"L1 effectful-index/RHS field-path JIT: expected=1154; actual={jitResult}.");

        var image = NativeModuleCompiler.EmitStandardExecutable(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1-effectful-index-field-path-");
        try
        {
            var imagePath = Path.Combine(directory, "program.exe");
            await AssertNativeImageExitCodeAsync(image, imagePath,
                moduleIdentity, 1154).ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task StandardL1NoEntryRootRunsOnlyItsOwnedLibraryExportAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string moduleIdentity = "l1-no-entry-library";
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            internal struct Pair {
                public mut int32 Value;

                internal Pair() {
                    Value = int32(0);
                }
            }

            public int32 GetValue() {
                mut Pair pair = new Pair();
                pair.Value = int32(42);
                return pair.Value;
            }
            """;

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound library.");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, SchemaIdentity, moduleTargetRequirement: 1);
        var input = new TrRmStandardArtifactInput(SchemaIdentity, artifact);
        WriteNativeTestIdentity(moduleIdentity, artifact);
        var image = NativeModuleCompiler.EmitStandardLibrary(input,
            moduleIdentity, [], ["GetValue"], NativeRuntimePolicy.FailFast);
        TestContext.WriteLine(
            $"L1 native library: module={moduleIdentity}; schema={SchemaIdentity}; runtime=FailFast; exports=GetValue; image-sha256={Convert.ToHexString(SHA256.HashData(image))}.");

        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1-library-");
        try
        {
            var imagePath = Path.Combine(directory, "library.dll");
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            var library = NativeLibrary.Load(imagePath);
            try
            {
                var function = Marshal.GetDelegateForFunctionPointer<
                    GetValueDelegate>(NativeLibrary.GetExport(library,
                    "GetValue"));
                Assert.That(function(), Is.EqualTo(42));
            }
            finally
            {
                NativeLibrary.Free(library);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task StandardL1NativePlanRevalidatesTypeProviderForLibraryAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string providerIdentity = "l1-native-type-provider";
        const string rootIdentity = "l1-native-type-consumer";
        const string providerSource = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Value {
                public mut int32 Field;
            }
            """;
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource), new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, providerIdentity, SchemaIdentity, 1,
            [new TrRmSourceTypeExport("", "Value", "ValueKey")]);
        var providerInput = new TrRmStandardArtifactInput(SchemaIdentity,
            providerArtifact);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, [], ["ValueKey"]);

        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                internal int32 TypeSignatureOnly(Value value) {
                    return int32(0);
                }

                public int32 GetValue() {
                    return int32(42);
                }
                """), providerReference, new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerCompilation.Program!, rootIdentity, SchemaIdentity, 1);
        var rootInput = new TrRmStandardArtifactInput(SchemaIdentity,
            consumerArtifact);
        WriteNativeTestIdentity(rootIdentity, consumerArtifact);
        var providers = new[] { providerInput };
        var image = NativeModuleCompiler.EmitStandardLibrary(rootInput,
            rootIdentity, providers, ["GetValue"], NativeRuntimePolicy.FailFast);
        TestContext.WriteLine(
            $"L1 native library: root={rootIdentity}; provider={providerIdentity}; schema={SchemaIdentity}; export=GetValue; provider-sha256={Convert.ToHexString(SHA256.HashData(providerArtifact))}; image-sha256={Convert.ToHexString(SHA256.HashData(image))}.");

        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1-native-type-provider-");
        try
        {
            var imagePath = Path.Combine(directory, "library.dll");
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            var library = NativeLibrary.Load(imagePath);
            try
            {
                var function = Marshal.GetDelegateForFunctionPointer<
                    GetValueDelegate>(NativeLibrary.GetExport(library,
                    "GetValue"));
                Assert.That(function(), Is.EqualTo(42));
            }
            finally
            {
                NativeLibrary.Free(library);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    internal static (byte[] RootArtifact, byte[] MethodProviderArtifact,
        byte[] TypeProviderArtifact) CreateImportedNominalMethodArtifacts()
    {
        var typeProviderArtifact = CreateNominalTypeProviderArtifact(
            relocate: false);
        var typeReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(SchemaIdentity,
                typeProviderArtifact), "NominalTypes", [], ["PacketKey"]);
        var options = new CompilationOptions {
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        };
        var methodProvider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1, Alignment: 32)]
            internal struct Holder {
                public mut [int32; 16] Values;

                internal Holder() {
                    Values = [int32(3); 16];
                }
            }

            public int32 Read(Packet value) {
                mut Holder holder = new Holder();
                mut [Packet; 2] snapshots = [value; 2];
                snapshots[1].Nested.Values[0] =
                    snapshots[0].Nested.Values[0] + holder.Values[0];
                return snapshots[1].Nested.Values[0]
                    + snapshots[0].Nested.Values[1]
                    + int32(snapshots[0].Tag);
            }
            """), typeReference, options);
        Assert.That(methodProvider.HasErrors, Is.False,
            string.Join(Environment.NewLine, methodProvider.Diagnostics));
        Assert.That(methodProvider.Program!.EntryPoint, Is.EqualTo(-1));
        var methodProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            methodProvider.Program!, "NominalMethods", SchemaIdentity, 1);
        var root = Compilation.Create(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1, Alignment: 32)]
            internal struct Holder {
                public mut Packet Payload;

                internal Holder() {
                    Payload.Tag = uint8(17);
                    Payload.Nested.Values = [int32(40), int32(2)];
                }
            }

            internal int32 Expected(Packet value) {
                return int32(0);
            }

            int32 Main() {
                mut Holder holder = new Holder();
                return Expected(holder.Payload);
            }
            """), typeReference, options);
        Assert.That(root.HasErrors, Is.False,
            string.Join(Environment.NewLine, root.Diagnostics));
        var localRootArtifact = TrRmExecutableCompiler.EmitStandard(
            root.Program!, "NominalRoot", SchemaIdentity, 1);
        var rootArtifact = ImportNominalRootMethod(localRootArtifact);
        return (rootArtifact, methodProviderArtifact, typeProviderArtifact);
    }

    private static byte[] CreateNominalTypeProviderArtifact(bool relocate,
        bool unsignedElements = false)
    {
        var prefix = relocate ? """
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1, Alignment: 32)]
            internal struct Holder {
                public [uint8; 64] Bytes;
            }
            """ : "";
        var elementType = unsignedElements ? "uint32" : "int32";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            prefix + Environment.NewLine + $$"""
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1, Alignment: 32)]
            public struct Leaf {
                public mut [{{elementType}}; 2] Values;
            }

            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1, Alignment: 32)]
            public struct Packet {
                public mut uint8 Tag;
                public mut Leaf Nested;
            }
            """), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return TrRmExecutableCompiler.EmitStandard(compilation.Program!,
            "NominalTypes", SchemaIdentity, 1, [
                new TrRmSourceTypeExport("", "Leaf", "LeafKey"),
                new TrRmSourceTypeExport("", "Packet", "PacketKey"),
            ]);
    }

    private static byte[] ImportNominalRootMethod(byte[] artifact)
    {
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                index);
        var expectedMethodRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Expected")
            {
                expectedMethodRow = row;
                break;
            }
        }
        Assert.That(expectedMethodRow, Is.GreaterThan(0));
        Assert.That(tables.Imports.Count, Is.EqualTo(2));
        // Keep the TypeDef Imports and direct expected-MethodDef call tokens
        // unchanged while seeding the independent consumer MethodDef expectation.
        var removedBodyRow = tables.Methods.GetBodyRow(expectedMethodRow);
        var methods = GetSectionBytes(index, 0x0016);
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var bodyRow = tables.Methods.GetBodyRow(row);
            if (row == expectedMethodRow)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    methods.AsSpan(((row - 1) * 28) + 12), 2);
                bodyRow = 0;
            }
            else if (bodyRow > removedBodyRow)
            {
                bodyRow--;
            }
            BinaryPrimitives.WriteInt32LittleEndian(
                methods.AsSpan(((row - 1) * 28) + 24), bodyRow);
        }

        var bodyIndex = new TrRmVariableTableIndex(
            GetSectionBytes(index, 0x0020), tables.Bodies.Count, 1, 1);
        var bodies = new List<ReadOnlyMemory<byte>>();
        for (var row = 1; row <= bodyIndex.Count; row++)
        {
            if (row != removedBodyRow)
            {
                bodies.Add(bodyIndex.GetRow(row).ToArray());
            }
        }
        var strings = new List<(string Text, int Encoding)>();
        for (var row = 1; row <= tables.Strings.Count; row++)
        {
            strings.Add((tables.Strings.DecodeNamespace(row), 1));
        }
        var providerIdentityRow = strings.Count + 1;
        strings.Add(("NominalMethods", 1));
        var exportKeyRow = strings.Count + 1;
        strings.Add(("Read", 1));
        var oldModuleRefs = GetSectionBytes(index, 0x0010);
        var moduleRefs = new byte[oldModuleRefs.Length + 12];
        oldModuleRefs.CopyTo(moduleRefs, 0);
        BinaryPrimitives.WriteInt32LittleEndian(
            moduleRefs.AsSpan(oldModuleRefs.Length), providerIdentityRow);
        BinaryPrimitives.WriteUInt32LittleEndian(
            moduleRefs.AsSpan(oldModuleRefs.Length + 4), 1);
        var oldImports = GetSectionBytes(index, 0x001C);
        var imports = new byte[oldImports.Length + 12];
        oldImports.CopyTo(imports, 0);
        BinaryPrimitives.WriteInt32LittleEndian(
            imports.AsSpan(oldImports.Length), tables.ModuleRefs.Count + 1);
        BinaryPrimitives.WriteInt32LittleEndian(
            imports.AsSpan(oldImports.Length + 4), exportKeyRow);
        BinaryPrimitives.WriteUInt32LittleEndian(
            imports.AsSpan(oldImports.Length + 8),
            0x1600_0000u | checked((uint)expectedMethodRow));
        return ReplaceSections(index, new Dictionary<ushort, TrRmSection> {
            [0x0002] = new(0x0002, 0, checked((uint)strings.Count),
                TrRmStringTableWriter.EncodeTextRows(strings)),
            [0x0010] = new(0x0010, 0,
                checked((uint)tables.ModuleRefs.Count + 1), moduleRefs),
            [0x0016] = new(0x0016, 0, checked((uint)tables.Methods.Count),
                methods),
            [0x001C] = new(0x001C, 0,
                checked((uint)tables.Imports.Count + 1), imports),
            [0x0020] = new(0x0020, 0, checked((uint)bodies.Count),
                TrRmVariableTableWriter.EncodeBodies(bodies)),
        });
    }

    private static byte[] CreateMethodProviderArtifact(int adjustment)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Marker {
                public mut int32 Value;

                internal Marker() {
                    Value = int32(0);
                }
            }

            internal int32 Calculate(int32 left, int32 right) {
                mut Marker value = new Marker();
                value.Value = left * int32(10) + right + int32({{adjustment}});
                mut [Marker; 2] values = [value; 2];
                return values[1].Value;
            }

            public int32 Actual(int32 left, int32 right) {
                return Calculate(left, right);
            }

            public int32 UnusedActual(int32 value) {
                return value;
            }
            """), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, "Provider", SchemaIdentity, 1);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                index);
        var strings = new List<(string Text, int Encoding)>();
        for (var row = 1; row <= tables.Strings.Count; row++)
        {
            strings.Add((tables.Strings.DecodeNamespace(row), 1));
        }
        var firstNameRow = strings.Count + 1;
        strings.Add(("First", 1));
        var secondNameRow = strings.Count + 1;
        strings.Add(("Second", 1));
        var methods = GetSectionBytes(index, 0x0016);
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var replacement = tables.Methods.GetName(row) switch {
                "Actual" => firstNameRow,
                "UnusedActual" => secondNameRow,
                _ => 0,
            };
            if (replacement != 0)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    methods.AsSpan(((row - 1) * 28) + 4), replacement);
            }
        }
        return ReplaceSections(index, new Dictionary<ushort, TrRmSection> {
            [0x0002] = new(0x0002, 0, checked((uint)strings.Count),
                TrRmStringTableWriter.EncodeTextRows(strings)),
            [0x0016] = new(0x0016, 0, checked((uint)tables.Methods.Count),
                methods),
        });
    }

    internal static byte[] MakeNoEntryPublicRoot(byte[] artifact)
    {
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                index);
        var entryMethodRow = tables.Modules.GetEntryMethod(1);
        Assert.That(entryMethodRow, Is.GreaterThan(0));
        var modules = GetSectionBytes(index, 0x0001);
        BinaryPrimitives.WriteUInt32LittleEndian(modules.AsSpan(16), 0);
        var methods = GetSectionBytes(index, 0x0016);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methods.AsSpan(((entryMethodRow - 1) * 28) + 12), 2);
        return ReplaceSections(index, new Dictionary<ushort, TrRmSection> {
            [0x0001] = new(0x0001, 0, 1, modules),
            [0x0016] = new(0x0016, 0, checked((uint)tables.Methods.Count),
                methods),
        });
    }

    private static byte[] GetSectionBytes(TrRmArtifactIndex index,
        ushort kind)
    {
        Assert.That(index.TryGetSection(kind, out var bytes, out _),
            Is.True);
        return bytes.ToArray();
    }

    private static byte[] ReplaceSections(TrRmArtifactIndex index,
        IReadOnlyDictionary<ushort, TrRmSection> replacements)
    {
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var count);
            sections.Add(replacements.TryGetValue(kind, out var replacement)
                ? replacement : new TrRmSection(kind, flags,
                    checked((uint)count), bytes.ToArray()));
        }
        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.LocalAggregateLayoutDraft);
    }

    private static async Task AssertNativeImageExitCodeAsync(byte[] image,
        string imagePath, string moduleIdentity, int expectedExitCode)
    {
        await File.WriteAllBytesAsync(imagePath, image)
            .ConfigureAwait(true);
        Process? process = null;
        try
        {
            process = Process.Start(new ProcessStartInfo(imagePath) {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }) ?? throw new InvalidOperationException(
                "The L1 native executable was not started.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(true);
            }
            catch (TimeoutException exception)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                throw new TimeoutException(
                    $"L1 native executable exceeded its 10-second deadline. {await stdout.ConfigureAwait(true)}{Environment.NewLine}{await stderr.ConfigureAwait(true)}",
                    exception);
            }

            var output = await stdout.ConfigureAwait(true);
            var error = await stderr.ConfigureAwait(true);
            TestContext.WriteLine(
                $"L1 native AOT: module={moduleIdentity}; pid={process.Id}; expected={expectedExitCode}; actual={process.ExitCode}; image-sha256={Convert.ToHexString(SHA256.HashData(image))}; deadline=10s.");
            Assert.That(output, Is.Empty);
            Assert.That(error, Is.Empty);
            Assert.That(process.ExitCode, Is.EqualTo(expectedExitCode),
                $"L1 native image {moduleIdentity} exited with 0x{process.ExitCode:X8}.");
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                process.Dispose();
            }
        }
    }

    private static void WriteNativeTestIdentity(string moduleIdentity,
        byte[] artifact)
    {
        var compilerAssemblyPath
            = typeof(NativeModuleCompiler).Assembly.Location;
        var compilerAssemblyHash = Convert.ToHexString(SHA256.HashData(
            File.ReadAllBytes(compilerAssemblyPath)));
        TestContext.WriteLine(
            $"L1 native input: module={moduleIdentity}; schema={SchemaIdentity}; target=WindowsX64; runtime=FailFast; compiler-assembly={compilerAssemblyPath}; compiler-sha256={compilerAssemblyHash}; artifact-sha256={Convert.ToHexString(SHA256.HashData(artifact))}.");
    }
}
