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
    private static readonly string[] s_methodTypeExportKeys = ["PacketKey", "LeafKey"];
    private static readonly string[] s_methodTypeNames = ["Packet", "Leaf"];

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetValueDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetPointerValueDelegate(nint source);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate double GetDoublePointerValueDelegate(nint source);

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
                Is.EquivalentTo(s_methodTypeExportKeys));
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
        foreach (var name in s_methodTypeNames)
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
    public async Task StandardL1ImportedImplicitReferenceExecutesAfterProviderBindingAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var (rootArtifact, providerArtifact)
            = TrRmLocalAggregateLayoutProducerTests
                .CreateL1MethodProviderArtifacts(implicitReference: true);
        var rootInput = new TrRmStandardArtifactInput(SchemaIdentity,
            rootArtifact);
        var providers = new[] {
            new TrRmStandardArtifactInput(SchemaIdentity, providerArtifact),
        };
        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput, "Root",
            providers);
        var closure = loaded.StandardMethodProviderClosure
            ?? throw new InvalidOperationException(
                "The L1 imported-reference call must retain its provider closure.");
        var edge = closure.MethodEdges.Single(static candidate =>
            candidate.ExportKey == "Actual");
        var provider = closure.Modules.Single(static module =>
            module.Identity == "Provider");
        Assert.That(provider.Program.Functions[edge.Actual.FunctionIndex]
            .Variables[0].IsImplicitRef, Is.True);
        WriteNativeTestIdentity("Root", rootArtifact);
        WriteNativeTestIdentity("Provider", providerArtifact);

        Assert.That(NativeModuleCompiler.ExecuteStandard(rootInput, "Root",
            providers, NativeRuntimePolicy.FailFast), Is.EqualTo(1));
        var image = NativeModuleCompiler.EmitStandardExecutable(rootInput,
            "Root", providers, NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1-implicit-reference-provider-");
        try
        {
            await AssertNativeImageExitCodeAsync(image,
                Path.Combine(directory, "program.exe"), "Root", 1)
                .ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
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
    public async Task StandardL1RepeatReassignmentExecutesThroughJitAndAotAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string moduleIdentity = "l1-repeat-reassignment-native";
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Item {
                public mut uint32 Value;

                internal Item(uint32 value) {
                    Value = value;
                }
            }

            int32 Main() {
                mut Item seed = new Item(uint32(1));
                mut [Item; 2] items = [seed; 2];
                items = [new Item(uint32(42)); 2];
                return int32(items[1].Value);
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
            $"L1 Repeat reassignment source-sha256={Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source)))}.");

        var jitResult = NativeModuleCompiler.ExecuteStandard(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast);
        Assert.That(jitResult, Is.EqualTo(42));
        TestContext.WriteLine(
            $"L1 Repeat reassignment JIT: expected=42; actual={jitResult}.");

        var image = NativeModuleCompiler.EmitStandardExecutable(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1-repeat-reassignment-");
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
    public async Task L1PointerLoadReadsAnAggregateAcrossStandardRoundTripAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string moduleIdentity = "l1p-aggregate-pointer-load";
        const string l1pSchemaIdentity
            = "trrm/1-draft-local-aggregate-layout-execution-pointer-loads-1";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct Snapshot {
                public mut int32 First;
                public mut int32 Second;
                public mut int32 Third;
            }
            Snapshot ReadSnapshot(ptr<Snapshot> source) {
                unsafe { return *source; }
            }
            public int32 Main(ptr<Snapshot> source) {
                unsafe {
                    Snapshot captured = ReadSnapshot(source);
                    return captured.First;
                }
            }
            """), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, l1pSchemaIdentity, moduleTargetRequirement: 1);
        var input = new TrRmStandardArtifactInput(l1pSchemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity + "-reemitted", l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var image = NativeModuleCompiler.EmitStandardLibrary(
            new TrRmStandardArtifactInput(l1pSchemaIdentity, reemitted),
            moduleIdentity + "-reemitted", [], ["Main"],
            NativeRuntimePolicy.FailFast);

        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1p-aggregate-pointer-load-");
        try
        {
            var imagePath = Path.Combine(directory, "library.dll");
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            var library = NativeLibrary.Load(imagePath);
            try
            {
                var function = Marshal.GetDelegateForFunctionPointer<
                    GetPointerValueDelegate>(NativeLibrary.GetExport(library,
                    "Main"));
                var source = Marshal.AllocHGlobal(12);
                try
                {
                    Marshal.WriteInt32(source, 0, 42);
                    Marshal.WriteInt32(source, 4, 1);
                    Marshal.WriteInt32(source, 8, 2);
                    Assert.That(function(source), Is.EqualTo(42));
                }
                finally
                {
                    Marshal.FreeHGlobal(source);
                }
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
    public async Task L1PointerLoadPreservesAggregateFloatingBitsAcrossStandardRoundTripAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string moduleIdentity = "l1p-aggregate-floating-bits";
        const string l1pSchemaIdentity
            = "trrm/1-draft-local-aggregate-layout-execution-pointer-loads-1";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct Snapshot {
                public mut float64 First;
                public mut int64 Second;
                public mut int64 Third;
            }
            public float64 Main(ptr<Snapshot> source) {
                unsafe {
                    Snapshot captured = *source;
                    return captured.First;
                }
            }
            """), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program!.Structures.Single(
            static structure => structure.Name == "Snapshot").Size,
            Is.GreaterThan(16));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(l1pSchemaIdentity, artifact),
            moduleIdentity, []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity + "-reemitted", l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var reemittedIdentity = moduleIdentity + "-reemitted";
        var image = NativeModuleCompiler.EmitStandardLibrary(
            new TrRmStandardArtifactInput(l1pSchemaIdentity, reemitted),
            reemittedIdentity, [], ["Main"],
            NativeRuntimePolicy.FailFast);

        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1p-aggregate-floating-bits-");
        try
        {
            var imagePath = Path.Combine(directory, "library.dll");
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            var library = NativeLibrary.Load(imagePath);
            try
            {
                var function = Marshal.GetDelegateForFunctionPointer<
                    GetDoublePointerValueDelegate>(NativeLibrary.GetExport(
                    library, "Main"));
                var source = Marshal.AllocHGlobal(3 * sizeof(long));
                try
                {
                    Marshal.WriteInt64(source, sizeof(long), 1);
                    Marshal.WriteInt64(source, 2 * sizeof(long), 2);
                    var values = new[] {
                        long.MinValue,
                        unchecked((long)0x7FF8_0000_0000_0042UL),
                    };
                    foreach (var expectedBits in values)
                    {
                        Marshal.WriteInt64(source, expectedBits);
                        var result = function(source);
                        Assert.That(BitConverter.DoubleToInt64Bits(result),
                            Is.EqualTo(expectedBits));
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(source);
                }
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
    public async Task L1PointerLoadsPreserveDirectScalarWidthsThroughStandardJitAndAotAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string moduleIdentity = "l1p-direct-scalar-widths";
        const string l1pSchemaIdentity
            = "trrm/1-draft-local-aggregate-layout-execution-pointer-loads-1";
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            public int32 Main() {
                mut uint8 byteValue = 0xA5;
                mut uint16 wordValue = 0xBEEF;
                mut uint32 dwordValue = 0x89ABCDEF;
                mut uint64 qwordValue = 0xFEDCBA9876543210;
                unsafe {
                    ptr<uint8> byteSource = &byteValue;
                    ptr<uint16> wordSource = &wordValue;
                    ptr<uint32> dwordSource = &dwordValue;
                    ptr<uint64> qwordSource = &qwordValue;
                    uint8 byteRead = *byteSource;
                    uint16 wordRead = *wordSource;
                    uint32 dwordRead = *dwordSource;
                    uint64 qwordRead = *qwordSource;
                    if (byteRead != uint8(0xA5)) { return -1; }
                    if (wordRead != uint16(0xBEEF)) { return -2; }
                    if (dwordRead != uint32(0x89ABCDEF)) { return -3; }
                    if (qwordRead != uint64(0xFEDCBA9876543210)) { return -4; }
                    return 42;
                }
            }
            """), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(l1pSchemaIdentity, artifact),
            moduleIdentity, []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity + "-reemitted", l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var reemittedIdentity = moduleIdentity + "-reemitted";
        var reemittedInput = new TrRmStandardArtifactInput(
            l1pSchemaIdentity, reemitted);
        Assert.That(NativeModuleCompiler.ExecuteStandard(reemittedInput,
            reemittedIdentity, [], NativeRuntimePolicy.FailFast),
            Is.EqualTo(42));
        var image = NativeModuleCompiler.EmitStandardExecutable(
            reemittedInput, reemittedIdentity, [],
            NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1p-direct-scalar-widths-");
        try
        {
            await AssertNativeImageExitCodeAsync(image,
                Path.Combine(directory, "program.exe"),
                reemittedIdentity, 42).ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task L1PointerLoadSnapshotsLocalAggregateThroughStandardJitAndAotAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string moduleIdentity = "l1p-local-snapshot-executable";
        const string l1pSchemaIdentity
            = "trrm/1-draft-local-aggregate-layout-execution-pointer-loads-1";
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Union)]
            struct RawWord {
                public mut uint32 Narrow;
                public mut uint64 Wide;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct Snapshot {
                public mut RawWord Value;
                public mut int32 First;
                public mut int32 Second;
                public mut int32 Third;
                public Snapshot(uint64 wide, int32 first, int32 second,
                    int32 third) {
                    Value.Wide = wide;
                    First = first;
                    Second = second;
                    Third = third;
                }
            }
            unsafe ptr<Snapshot> GetSnapshotAddress(
                ptr<Snapshot> source) {
                return source;
            }
            Snapshot ReadSnapshot(ptr<Snapshot> source) {
                unsafe { return *GetSnapshotAddress(source); }
            }
            int32 Capture(int32 selector) {
                mut Snapshot value = new Snapshot(42, 1, 2, 3);
                unsafe {
                    ptr<Snapshot> source = &value;
                    mut Snapshot before;
                    if (selector == 0) {
                        before = ReadSnapshot(source);
                    } else {
                        before = ReadSnapshot(source);
                    }
                    value.Value.Wide = 99;
                    Snapshot after = ReadSnapshot(source);
                    return int32(before.Value.Wide)
                        + int32(after.Value.Wide);
                }
            }
            public int32 Main() { return Capture(0) + Capture(1); }
            """), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program!.Structures.Single(
            static structure => structure.Name == "Snapshot").Size,
            Is.GreaterThan(16));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(l1pSchemaIdentity, artifact),
            moduleIdentity, []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity + "-reemitted", l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var reemittedIdentity = moduleIdentity + "-reemitted";
        var reemittedInput = new TrRmStandardArtifactInput(
            l1pSchemaIdentity, reemitted);
        Assert.That(NativeModuleCompiler.ExecuteStandard(reemittedInput,
            reemittedIdentity, [], NativeRuntimePolicy.FailFast),
            Is.EqualTo(282));
        var image = NativeModuleCompiler.EmitStandardExecutable(
            reemittedInput, reemittedIdentity, [],
            NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1p-local-snapshot-executable-");
        try
        {
            await AssertNativeImageExitCodeAsync(image,
                Path.Combine(directory, "program.exe"),
                reemittedIdentity, 282).ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task L1PointerLoadSnapshotsFixedArrayElementAddressAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string moduleIdentity = "l1p-fixed-array-element-snapshot";
        const string l1pSchemaIdentity
            = "trrm/1-draft-local-aggregate-layout-execution-pointer-loads-1";
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            public int32 Main() {
                mut [int32; 3] values = [4, 5, 6];
                unsafe {
                    ptr<int32> source = &values[1];
                    int32 captured = *source;
                    values[1] = 99;
                    return captured;
                }
            }
            """), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(l1pSchemaIdentity, artifact),
            moduleIdentity, []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity + "-reemitted", l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var reemittedIdentity = moduleIdentity + "-reemitted";
        var reemittedInput = new TrRmStandardArtifactInput(
            l1pSchemaIdentity, reemitted);
        Assert.That(NativeModuleCompiler.ExecuteStandard(reemittedInput,
            reemittedIdentity, [], NativeRuntimePolicy.FailFast),
            Is.EqualTo(5));
        var image = NativeModuleCompiler.EmitStandardExecutable(
            reemittedInput, reemittedIdentity, [],
            NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1p-fixed-array-element-snapshot-");
        try
        {
            await AssertNativeImageExitCodeAsync(image,
                Path.Combine(directory, "program.exe"),
                reemittedIdentity, 5).ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task L1PointerLoadSnapshotsFixedArrayFieldElementAddressAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string moduleIdentity = "l1p-array-field-address-snapshot";
        const string l1pSchemaIdentity
            = "trrm/1-draft-local-aggregate-layout-execution-pointer-loads-1";
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct Holder {
                public mut [int32; 3] Values;
                public Holder(int32 first, int32 second, int32 third) {
                    Values = [first, second, third];
                }
            }
            public int32 Main() {
                mut Holder holder = new Holder(4, 5, 6);
                unsafe {
                    ptr<int32> source = &holder.Values[1];
                    int32 captured = *source;
                    holder.Values[1] = 99;
                    return captured;
                }
            }
            """), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(l1pSchemaIdentity, artifact),
            moduleIdentity, []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity + "-reemitted", l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var reemittedIdentity = moduleIdentity + "-reemitted";
        var reemittedInput = new TrRmStandardArtifactInput(
            l1pSchemaIdentity, reemitted);
        Assert.That(NativeModuleCompiler.ExecuteStandard(reemittedInput,
            reemittedIdentity, [], NativeRuntimePolicy.FailFast),
            Is.EqualTo(5));
        var image = NativeModuleCompiler.EmitStandardExecutable(
            reemittedInput, reemittedIdentity, [],
            NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1p-array-field-address-snapshot-");
        try
        {
            await AssertNativeImageExitCodeAsync(image,
                Path.Combine(directory, "program.exe"),
                reemittedIdentity, 5).ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task L1PointerReadSnapshotsBeforeMutatingLaterCallArgumentAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string moduleIdentity = "l1p-call-argument-snapshot";
        const string l1pSchemaIdentity
            = "trrm/1-draft-local-aggregate-layout-execution-pointer-loads-1";
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Union)]
            struct RawWord {
                public mut uint32 Narrow;
                public mut uint64 Wide;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct Snapshot {
                public mut RawWord Value;
                public mut int32 First;
                public mut int32 Second;
                public mut int32 Third;
                public Snapshot(uint64 wide, int32 first, int32 second,
                    int32 third) {
                    Value.Wide = wide;
                    First = first;
                    Second = second;
                    Third = third;
                }
            }
            int32 Echo(int32 value) {
                return value;
            }
            int32 ObserveFirst(Snapshot captured, int32 updated) {
                return captured.First + updated;
            }
            int32 ObserveSecond(Snapshot captured, int32 updated) {
                return captured.Second + updated;
            }
            public int32 Main() {
                mut Snapshot value = new Snapshot(42, 1, 2, 3);
                unsafe {
                    ptr<Snapshot> source = &value;
                    return ObserveFirst(*source, Echo(++value.First))
                        + ObserveSecond(*source, Echo(value.Second++));
                }
            }
            """), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program!.Structures.Single(
            static structure => structure.Name == "Snapshot").Size,
            Is.GreaterThan(16));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(l1pSchemaIdentity, artifact),
            moduleIdentity, []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity + "-reemitted", l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var reemittedIdentity = moduleIdentity + "-reemitted";
        var reemittedInput = new TrRmStandardArtifactInput(
            l1pSchemaIdentity, reemitted);
        Assert.That(NativeModuleCompiler.ExecuteStandard(reemittedInput,
            reemittedIdentity, [], NativeRuntimePolicy.FailFast),
            Is.EqualTo(7));
        var image = NativeModuleCompiler.EmitStandardExecutable(
            reemittedInput, reemittedIdentity, [],
            NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1p-call-argument-snapshot-");
        try
        {
            await AssertNativeImageExitCodeAsync(image,
                Path.Combine(directory, "program.exe"),
                reemittedIdentity, 7).ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task L1PointerLoadLinksImportedAggregateThroughStandardJitAndAotAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string schemaIdentity
            = "trrm/1-draft-local-aggregate-layout-execution-pointer-loads-1";
        const string typeProviderIdentity = "l1p-jit-snapshot-types";
        const string methodProviderIdentity = "l1p-jit-snapshot-methods";
        const string rootIdentity = "l1p-jit-snapshot-root";
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        };
        var typeProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Union)]
                public struct RawWord {
                    public mut uint32 Narrow;
                    public mut uint64 Wide;
                }
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Snapshot {
                    public mut RawWord Value;
                    public mut int32 First;
                    public mut int32 Second;
                    public mut int32 Third;
                    public mut [int32; 3] Values;
                    public Snapshot(uint64 wide, int32 first, int32 second,
                        int32 third) {
                        Value.Wide = wide;
                        First = first;
                        Second = second;
                        Third = third;
                        Values = [first, second, third];
                    }
                }
                public Snapshot CreateSnapshot() {
                    return new Snapshot(42, 1, 2, 3);
                }
                """), options);
        Assert.That(typeProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                typeProviderCompilation.Diagnostics));
        var typeProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            typeProviderCompilation.Program!, typeProviderIdentity,
            schemaIdentity, 1, [
                new TrRmSourceTypeExport("", "Snapshot", "SnapshotKey"),
                new TrRmSourceTypeExport("", "RawWord", "RawWordKey"),
            ]);
        var typeProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, typeProviderArtifact);
        var typeProviderReference
            = TrRmExecutableCompiler.CreateStandardReference(
                typeProviderInput, typeProviderIdentity, []);

        var methodProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public Snapshot CreateSnapshotViaProvider() {
                    return CreateSnapshot();
                }
                public Snapshot ReadSnapshot(ptr<Snapshot> source) {
                    unsafe { return *source; }
                }
                """), typeProviderReference, options);
        Assert.That(methodProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                methodProviderCompilation.Diagnostics));
        var methodProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            methodProviderCompilation.Program!, methodProviderIdentity,
            schemaIdentity, moduleTargetRequirement: 1);
        var methodProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, methodProviderArtifact);
        var methodProviderReference
            = TrRmExecutableCompiler.CreateStandardReference(
                methodProviderInput, methodProviderIdentity, [
                    typeProviderInput,
                ]);

        var rootCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public int32 Main() {
                    mut Snapshot value = CreateSnapshotViaProvider();
                    unsafe {
                        ptr<Snapshot> source = &value;
                        ptr<int32> element = &value.Values[1];
                        int32 capturedElement = *element;
                        Snapshot captured = ReadSnapshot(source);
                        value.Value.Wide = 99;
                        value.Values[1] = 99;
                        return int32(captured.Value.Wide)
                            + captured.Values[1] + capturedElement;
                    }
                }
                """), methodProviderReference, options);
        Assert.That(rootCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                rootCompilation.Diagnostics));
        var rootArtifact = TrRmExecutableCompiler.EmitStandard(
            rootCompilation.Program!, rootIdentity, schemaIdentity,
            moduleTargetRequirement: 1);
        var providers = new[] { methodProviderInput, typeProviderInput };
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, providers);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            rootIdentity + "-reemitted", schemaIdentity,
            moduleTargetRequirement: 1);
        var reemittedIdentity = rootIdentity + "-reemitted";
        var reemittedInput = new TrRmStandardArtifactInput(schemaIdentity,
            reemitted);
        Assert.That(NativeModuleCompiler.ExecuteStandard(reemittedInput,
            reemittedIdentity, providers, NativeRuntimePolicy.FailFast),
            Is.EqualTo(46));
        var image = NativeModuleCompiler.EmitStandardExecutable(
            reemittedInput, reemittedIdentity, providers,
            NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1p-imported-jit-snapshot-");
        try
        {
            await AssertNativeImageExitCodeAsync(image,
                Path.Combine(directory, "program.exe"),
                reemittedIdentity, 46).ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public void L1StorageAddressRejectsInvalidElementCount()
    {
        const string moduleIdentity = "l1p-storage-address-extent";
        const string l1pSchemaIdentity
            = "trrm/1-draft-local-aggregate-layout-execution-pointer-loads-1";
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct Snapshot {
                public mut int32 First;
                public mut int32 Second;
                public mut int32 Third;
                public Snapshot(int32 first, int32 second, int32 third) {
                    First = first;
                    Second = second;
                    Third = third;
                }
            }
            Snapshot ReadSnapshot(ptr<Snapshot> source) {
                unsafe { return *source; }
            }
            public int32 Main() {
                mut Snapshot value = new Snapshot(42, 1, 2);
                unsafe {
                    ptr<Snapshot> source = &value;
                    Snapshot captured = ReadSnapshot(source);
                    return captured.First;
                }
            }
            """), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var malformed = MutateL1pStorageAddressElementCount(artifact,
            l1pSchemaIdentity, "Main");
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(l1pSchemaIdentity,
                    malformed), moduleIdentity, []));
    }

    [TestCase(1, true)]
    [TestCase(2, true)]
    [TestCase(1, false)]
    [TestCase(2, false)]
    public void L1StorageAddressRejectsInvalidPointerTypeMapEntry(
        byte sourceKind, bool outOfRangeTypeMapIndex)
    {
        const string moduleIdentity = "l1p-storage-address-type-map";
        const string l1pSchemaIdentity
            = "trrm/1-draft-local-aggregate-layout-execution-pointer-loads-1";
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct Snapshot {
                public mut int32 Value;
                public Snapshot(int32 value) { Value = value; }
            }
            public int32 Main() {
                mut Snapshot snapshot = new Snapshot(42);
                mut [int32; 3] values = [4, 5, 6];
                unsafe {
                    ptr<Snapshot> aggregate = &snapshot;
                    ptr<int32> element = &values[1];
                    Snapshot captured = *aggregate;
                    return captured.Value + *element;
                }
            }
            """), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var invalidTypeMapIndex = outOfRangeTypeMapIndex
            ? ushort.MaxValue
            : FindL1pNonPointerTypeMapIndex(artifact,
                l1pSchemaIdentity, "Main");
        var malformed = MutateL1pStorageAddressTypeMapIndex(artifact,
            l1pSchemaIdentity, "Main", sourceKind,
            invalidTypeMapIndex);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(l1pSchemaIdentity,
                    malformed), moduleIdentity, []));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void L1StorageAddressRejectsOutOfRangeHomeIndex(
        bool resultHomeIndex)
    {
        const string moduleIdentity = "l1p-storage-address-home-index";
        const string l1pSchemaIdentity
            = "trrm/1-draft-local-aggregate-layout-execution-pointer-loads-1";
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct Snapshot {
                public mut int32 Value;
                public Snapshot(int32 value) { Value = value; }
            }
            public int32 Main() {
                mut Snapshot snapshot = new Snapshot(42);
                unsafe {
                    ptr<Snapshot> source = &snapshot;
                    Snapshot captured = *source;
                    return captured.Value;
                }
            }
            """), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var malformed = MutateL1pStorageAddressHomeIndex(artifact,
            l1pSchemaIdentity, "Main", resultHomeIndex);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(l1pSchemaIdentity,
                    malformed), moduleIdentity, []));
    }

    [TestCase(14)]
    [TestCase(15)]
    public void L1ArrayStorageAddressRejectsReservedPlaceRegisters(
        int invalidRegister)
    {
        const string moduleIdentity = "l1p-array-address-reserved-register";
        const string l1pSchemaIdentity
            = "trrm/1-draft-local-aggregate-layout-execution-pointer-loads-1";
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            public int32 Main() {
                mut [int32; 3] values = [4, 5, 6];
                unsafe {
                    ptr<int32> source = &values[1];
                    return *source;
                }
            }
            """), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var malformed = MutateL1pStorageAddressSourceRegister(artifact,
            l1pSchemaIdentity, "Main", checked((ushort)invalidRegister));
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(l1pSchemaIdentity,
                    malformed), moduleIdentity, []));
    }

    [Test]
    public void NativePointerReadSnapshotsIndirectAggregateBeforeMutation()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct Snapshot {
                public mut int32 First;
                public mut int32 Second;
                public mut int32 Third;
                public Snapshot(int32 first, int32 second, int32 third) {
                    First = first;
                    Second = second;
                    Third = third;
                }
            }
            Snapshot ReadSnapshot(ptr<Snapshot> source) {
                unsafe { return *source; }
            }
            public int32 Main() {
                Snapshot value = new Snapshot(42, 1, 2);
                unsafe {
                    Snapshot captured = ReadSnapshot(&value);
                    value.First = 99;
                    return captured.First;
                }
            }
            """), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void NativePointerReadPreservesLocalRawUnionCoverage()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Union)]
            struct RawWord {
                public mut uint32 Narrow;
                public mut uint64 Wide;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct Snapshot {
                public mut RawWord Value;
                public Snapshot(uint64 value) {
                    Value.Wide = value;
                }
            }
            Snapshot MakeSnapshot() {
                return new Snapshot(uint64(7));
            }
            public int32 Main() {
                mut Snapshot value = MakeSnapshot();
                unsafe {
                    Snapshot captured = *(&value);
                    return int32(captured.Value.Wide);
                }
            }
            """), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(7));
    }

    [Test]
    public async Task L1PointerLoadReadsNestedTypedPointerCellsAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string moduleIdentity = "l1p-nested-pointer-cells";
        const string l1pSchemaIdentity
            = "trrm/1-draft-local-aggregate-layout-execution-pointer-loads-1";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Main(ptr<ptr<int32>> source) {
                unsafe {
                    ptr<int32> inner = *source;
                    return *inner;
                }
            }
            """), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var input = new TrRmStandardArtifactInput(l1pSchemaIdentity,
            artifact);
        var image = NativeModuleCompiler.EmitStandardLibrary(input,
            moduleIdentity, [], ["Main"], NativeRuntimePolicy.FailFast);

        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1p-nested-pointer-cells-");
        try
        {
            var imagePath = Path.Combine(directory, "library.dll");
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            var library = NativeLibrary.Load(imagePath);
            try
            {
                var function = Marshal.GetDelegateForFunctionPointer<
                    GetPointerValueDelegate>(NativeLibrary.GetExport(library,
                    "Main"));
                var source = Marshal.AllocHGlobal(IntPtr.Size);
                var value = Marshal.AllocHGlobal(sizeof(int));
                try
                {
                    Marshal.WriteInt32(value, 42);
                    Marshal.WriteIntPtr(source, value);
                    Assert.That(function(source), Is.EqualTo(42));
                }
                finally
                {
                    Marshal.FreeHGlobal(value);
                    Marshal.FreeHGlobal(source);
                }
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
    public async Task L1PointerLoadCapturesOverAlignedAggregateWithArrayChildrenAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string moduleIdentity = "l1p-overaligned-array-snapshot";
        const string l1pSchemaIdentity
            = "trrm/1-draft-local-aggregate-layout-execution-pointer-loads-1";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential, Alignment: 32)]
            struct Inner {
                public mut [int32; 8] Items;
            }
            [Blittable]
            [Layout(LayoutKind.Sequential, Alignment: 8192)]
            struct Snapshot {
                public mut Inner Nested;
            }
            public int32 Main(ptr<Snapshot> source) {
                unsafe {
                    Snapshot captured = *source;
                    return captured.Nested.Items[7];
                }
            }
            """), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var input = new TrRmStandardArtifactInput(l1pSchemaIdentity,
            artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity + "-reemitted", l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var reemittedIdentity = moduleIdentity + "-reemitted";
        var image = NativeModuleCompiler.EmitStandardLibrary(
            new TrRmStandardArtifactInput(l1pSchemaIdentity, reemitted),
            reemittedIdentity, [], ["Main"],
            NativeRuntimePolicy.FailFast);

        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1p-overaligned-array-");
        try
        {
            var imagePath = Path.Combine(directory, "library.dll");
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            var library = NativeLibrary.Load(imagePath);
            try
            {
                var function = Marshal.GetDelegateForFunctionPointer<
                    GetPointerValueDelegate>(NativeLibrary.GetExport(library,
                    "Main"));
                const nuint alignment = 8192;
                var allocation = Marshal.AllocHGlobal(
                    checked((nint)(alignment * 2 - 1)));
                try
                {
                    var rawAddress = (nuint)allocation;
                    var source = checked((nint)((rawAddress + alignment - 1)
                        & ~(alignment - 1)));
                    Assert.That((nuint)source % alignment, Is.Zero);
                    for (var index = 0; index < 8; index++)
                    {
                        Marshal.WriteInt32(source, index * sizeof(int),
                            index == 7 ? 42 : index + 1);
                    }

                    Assert.That(function(source), Is.EqualTo(42));
                }
                finally
                {
                    Marshal.FreeHGlobal(allocation);
                }
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
    public async Task L1PointerLoadSnapshotsLocal8192AlignedHomeAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string moduleIdentity = "l1p-local-8192-aligned-snapshot";
        const string l1pSchemaIdentity
            = "trrm/1-draft-local-aggregate-layout-execution-pointer-loads-1";
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential, Alignment: 8192)]
            struct Snapshot {
                public mut int32 Value;
                public Snapshot(int32 value) { Value = value; }
            }
            public int32 Main() {
                mut Snapshot value = new Snapshot(42);
                unsafe {
                    ptr<Snapshot> source = &value;
                    Snapshot captured = *source;
                    value.Value = 99;
                    return captured.Value;
                }
            }
            """), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var snapshot = compilation.Program!.Structures.Single(
            static structure => structure.Name == "Snapshot");
        Assert.That(snapshot.Size, Is.EqualTo(8192UL));
        Assert.That(snapshot.Alignment, Is.EqualTo(8192));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(l1pSchemaIdentity, artifact),
            moduleIdentity, []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity + "-reemitted", l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var reemittedIdentity = moduleIdentity + "-reemitted";
        var reemittedInput = new TrRmStandardArtifactInput(
            l1pSchemaIdentity, reemitted);
        Assert.That(NativeModuleCompiler.ExecuteStandard(reemittedInput,
            reemittedIdentity, [], NativeRuntimePolicy.FailFast),
            Is.EqualTo(42));
        var image = NativeModuleCompiler.EmitStandardExecutable(
            reemittedInput, reemittedIdentity, [],
            NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1p-local-8192-aligned-snapshot-");
        try
        {
            await AssertNativeImageExitCodeAsync(image,
                Path.Combine(directory, "program.exe"),
                reemittedIdentity, 42).ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [TestCase(14, true)]
    [TestCase(15, true)]
    [TestCase(14, false)]
    [TestCase(15, false)]
    public void L1PointerLoadRejectsReservedRegisters(int invalidRegister,
        bool invalidDestination)
    {
        const string moduleIdentity = "l1p-reserved-register";
        const string l1pSchemaIdentity
            = "trrm/1-draft-local-aggregate-layout-execution-pointer-loads-1";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Read(ptr<int32> source) {
                unsafe { return *source; }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var malformed = MutateL1pPointerLoadRegister(artifact,
            l1pSchemaIdentity, "Read", invalidRegister,
            invalidDestination);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(l1pSchemaIdentity,
                malformed), moduleIdentity, []));
    }

    [Test]
    public void L1PointerLoadRejectsTruncatedOperand()
    {
        const string moduleIdentity = "l1p-truncated-pointer-load";
        const string l1pSchemaIdentity
            = "trrm/1-draft-local-aggregate-layout-execution-pointer-loads-1";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Read(ptr<int32> source) {
                unsafe { return *source; }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var malformed = TruncateL1pPointerLoadOperand(artifact,
            l1pSchemaIdentity, "Read");
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                    new TrRmStandardArtifactInput(l1pSchemaIdentity,
                        malformed), moduleIdentity, []));
    }

    [Test]
    public void L1PointerLoadRejectsMismatchedFormalHomeType()
    {
        const string moduleIdentity = "l1p-formal-home-type";
        const string l1pSchemaIdentity
            = "trrm/1-draft-local-aggregate-layout-execution-pointer-loads-1";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Read(ptr<int32> source) {
                int32 one = 1;
                unsafe { return *source + one; }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var malformed = MutateL1pFormalHomeTypeMap(artifact,
            l1pSchemaIdentity, "Read");
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                        new TrRmStandardArtifactInput(l1pSchemaIdentity,
                            malformed), moduleIdentity, []));
    }

    [Test]
    public void SourceRejectsL1PointerLoadOfUniqueAggregate()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public unique struct UniqueSnapshot {
                    public mut int32 First;
            }
            public int32 Read(ptr<UniqueSnapshot> source) {
                    unsafe { return (*source).First; }
            }
            """), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.True,
            "A pointer load must not copy a unique aggregate.");
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3023"), Is.True);

        var missingUnsafe = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Read(ptr<int32> source) {
                return *source;
            }
            """));
        Assert.That(missingUnsafe.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3028"), Is.True);
    }

    [Test]
    public void SourceRejectsL1PointerLoadOfVoid()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Read(ptr<void> source) {
                unsafe { return int32(*source); }
            }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            "A void pointer has no positive typed load extent.");
    }

    [Test]
    public void L1PointerLoadRequiresRootlessNativeValuesFeature()
    {
        const string moduleIdentity = "l1p-feature-closure";
        const string l1pSchemaIdentity
            = "trrm/1-draft-local-aggregate-layout-execution-pointer-loads-1";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Read(ptr<int32> source) {
                unsafe { return *source; }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var index = new TrRmArtifactIndex(artifact,
            TrRmArtifactSchema.LocalAggregateLayoutDraft,
            resourceLedger: null, usesNativePointerLoads: true,
            standardSchemaIdentity: l1pSchemaIdentity);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                index);
        var (featureFirst, featureCount)
            = tables.Modules.GetFeatureRange(1);
        var retainedFeatures
            = new List<TrRmFeatureRow>(featureCount);
        for (var featureRow = featureFirst;
            featureRow < featureFirst + featureCount; featureRow++)
        {
            var feature = tables.Features.GetFeature(featureRow);
            if (feature.FeatureId
                != TrRmFeatureTableReader.RootlessNativeValuesFeatureId)
            {
                retainedFeatures.Add(feature);
            }
        }
        Assert.That(retainedFeatures.All(static feature =>
            feature.FeatureId
                != TrRmFeatureTableReader.RootlessNativeValuesFeatureId),
            Is.True);

        var modules = GetSectionBytes(index, 0x0001);
        BinaryPrimitives.WriteInt32LittleEndian(modules.AsSpan(24),
            retainedFeatures.Count == 0 ? 0 : 1);
        BinaryPrimitives.WriteInt32LittleEndian(modules.AsSpan(28),
            retainedFeatures.Count);
        var malformed = ReplaceSections(index,
            new Dictionary<ushort, TrRmSection> {
                [0x0001] = new(0x0001, 0, 1, modules),
                [0x0004] = new(0x0004, 0,
                    checked((uint)retainedFeatures.Count),
                    TrRmFeatureTableWriter.Encode(retainedFeatures,
                        TrRmArtifactSchema.LocalAggregateLayoutDraft)),
            });

        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(l1pSchemaIdentity,
                    malformed), moduleIdentity, []));
    }

    [Test]
    public async Task L1PointerLoadPreservesImportedAggregateProviderClosureAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string l1pSchemaIdentity
            = "trrm/1-draft-local-aggregate-layout-execution-pointer-loads-1";
        const string rootIdentity = "l1p-imported-aggregate-root";
        const string methodProviderIdentity
            = "l1p-imported-aggregate-methods";
        const string typeProviderIdentity = "l1p-imported-aggregate-types";
        var (rootArtifact, methodProviderArtifact, expectedTypeArtifact)
            = CreateL1pImportedAggregateMethodArtifacts(rootIdentity,
                methodProviderIdentity, typeProviderIdentity,
                l1pSchemaIdentity);
        var relocatedTypeArtifact = CreateL1pTypeProviderArtifact(
            typeProviderIdentity, l1pSchemaIdentity, relocate: true);
        var rootInput = new TrRmStandardArtifactInput(l1pSchemaIdentity,
            rootArtifact);
        var sourceMethodProviderInput = new TrRmStandardArtifactInput(
            l1pSchemaIdentity, methodProviderArtifact);
        var typeProviderInput = new TrRmStandardArtifactInput(
            l1pSchemaIdentity, relocatedTypeArtifact);
        var loadedMethodProvider = TrRmExecutableCompiler.LoadStandard(
            sourceMethodProviderInput, methodProviderIdentity, [
                typeProviderInput,
            ]);
        var reemittedMethodProvider
            = TrRmExecutableCompiler.EmitStandard(loadedMethodProvider,
                methodProviderIdentity, l1pSchemaIdentity,
                moduleTargetRequirement: 1);
        var methodProviderInput = new TrRmStandardArtifactInput(
            l1pSchemaIdentity, reemittedMethodProvider);
        var providers = new[] { methodProviderInput, typeProviderInput };
        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, providers);
        var reordered = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, [typeProviderInput, methodProviderInput]);
        var reorderedClosure = reordered.StandardMethodProviderClosure
            ?? throw new InvalidOperationException(
                "The L1P root must retain its reordered provider closure.");
        Assert.That(reorderedClosure.MethodEdges.Single().ProducerIdentity,
            Is.EqualTo(methodProviderIdentity));
        Assert.That(reorderedClosure.MethodEdges.Single().ExportKey,
            Is.EqualTo("Actual"));
        var reorderedTypeProvider = reorderedClosure.Modules.Single(module =>
            module.Identity == typeProviderIdentity);
        Assert.That(reorderedClosure.TypeEdges.All(edge =>
            edge.ProviderArtifactIndex
                == reorderedTypeProvider.ArtifactIndex
            && edge.ProviderModuleRow == reorderedTypeProvider.ModuleRow),
            Is.True);
        var closure = loaded.StandardMethodProviderClosure
            ?? throw new InvalidOperationException(
                "The L1P root must retain its method and type provider closure.");
        var methodEdge = closure.MethodEdges.Single();
        Assert.That(methodEdge.ProducerIdentity,
            Is.EqualTo(methodProviderIdentity));
        Assert.That(methodEdge.ExportKey, Is.EqualTo("Actual"));
        var typeProvider = closure.Modules.Single(module =>
            module.Identity == typeProviderIdentity);
        Assert.That(closure.TypeEdges, Has.Length.GreaterThanOrEqualTo(2));
        Assert.That(closure.TypeEdges.Any(edge =>
            edge.ExportKey == "RawWordKey"), Is.True);
        Assert.That(closure.TypeEdges.All(edge =>
            edge.ProviderArtifactIndex == typeProvider.ArtifactIndex
            && edge.ProviderModuleRow == typeProvider.ModuleRow
            && edge.ExpectedDefinitionId != edge.ActualDefinitionId), Is.True);
        var methodProvider = closure.Modules.Single(module =>
            module.Identity == methodProviderIdentity);
        var implementation = methodProvider.Program.Functions[
            methodEdge.Actual.FunctionIndex];
        var pointerType = methodProvider.Program.NativePointerTypes[
            NativePointerTypes.Index(implementation.Variables[0].Type)];
        var importedSnapshot = methodProvider.Program.Structures[
            (int)pointerType.PointeeType - 100];
        Assert.That(importedSnapshot.StandardTypeOrigin!.Provider
            .ModuleIdentity, Is.EqualTo(typeProviderIdentity));
        Assert.That(importedSnapshot.Size, Is.GreaterThan(16));
        Assert.That(expectedTypeArtifact, Is.Not.EqualTo(relocatedTypeArtifact));

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            rootIdentity + "-reemitted", l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var reemittedIdentity = rootIdentity + "-reemitted";
        var reemittedInput = new TrRmStandardArtifactInput(
            l1pSchemaIdentity, reemitted);
        _ = TrRmExecutableCompiler.LoadStandard(reemittedInput,
            reemittedIdentity, providers);
        var image = NativeModuleCompiler.EmitStandardLibrary(
            reemittedInput, reemittedIdentity, providers, ["Main"],
            NativeRuntimePolicy.FailFast);

        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-l1p-imported-aggregate-");
        try
        {
            var imagePath = Path.Combine(directory, "library.dll");
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            var library = NativeLibrary.Load(imagePath);
            try
            {
                var function = Marshal.GetDelegateForFunctionPointer<
                    GetPointerValueDelegate>(NativeLibrary.GetExport(library,
                    "Main"));
                var source = Marshal.AllocHGlobal(32);
                try
                {
                    for (var index = 0; index < 32; index++)
                    {
                        Marshal.WriteByte(source, index, 0);
                    }
                    Marshal.WriteInt64(source, 0, 40);
                    Marshal.WriteInt32(source, 8, 2);
                    Marshal.WriteInt32(source, 12, 1);
                    Marshal.WriteInt32(source, 16, 2);
                    Marshal.WriteInt32(source, 20, 3);
                    Marshal.WriteInt32(source, 24, 4);
                    Marshal.WriteInt32(source, 28, 5);
                    Assert.That(function(source), Is.EqualTo(42));
                }
                finally
                {
                    Marshal.FreeHGlobal(source);
                }
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

        var incompatibleTypeArtifact = CreateL1pTypeProviderArtifact(
            typeProviderIdentity, l1pSchemaIdentity,
            incompatible: true);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity, [
                methodProviderInput,
                new TrRmStandardArtifactInput(l1pSchemaIdentity,
                    incompatibleTypeArtifact),
            ]));
        var incompatibleRawUnionArtifact = CreateL1pTypeProviderArtifact(
            typeProviderIdentity, l1pSchemaIdentity, relocate: true,
            incompatibleRawUnion: true);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity, [
                methodProviderInput,
                new TrRmStandardArtifactInput(l1pSchemaIdentity,
                    incompatibleRawUnionArtifact),
            ]));
        var incompatibleArrayArtifact = CreateL1pTypeProviderArtifact(
            typeProviderIdentity, l1pSchemaIdentity, relocate: true,
            incompatibleArray: true);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity, [
                methodProviderInput,
                new TrRmStandardArtifactInput(l1pSchemaIdentity,
                    incompatibleArrayArtifact),
            ]));
    }

    [Test]
    public void L1PointerLoadProfileDoesNotAliasL1OrAcceptOldProviders()
    {
        const string l1pSchemaIdentity
            = "trrm/1-draft-local-aggregate-layout-execution-pointer-loads-1";
        const string moduleIdentity = "l1p-schema-selection-root";
        const string providerIdentity = "l1p-schema-selection-provider";
        var commonCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public int32 Value() { return int32(42); }"));
        Assert.That(commonCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                commonCompilation.Diagnostics));
        var commonArtifact = TrRmExecutableCompiler.EmitStandard(
            commonCompilation.Program!, moduleIdentity + "-common",
            l1pSchemaIdentity, moduleTargetRequirement: 1);
        var commonInput = new TrRmStandardArtifactInput(l1pSchemaIdentity,
            commonArtifact);
        var commonLoaded = TrRmExecutableCompiler.LoadStandard(commonInput,
            moduleIdentity + "-common", []);
        var commonReemitted = TrRmExecutableCompiler.EmitStandard(
            commonLoaded, moduleIdentity + "-common-reemitted",
            l1pSchemaIdentity, moduleTargetRequirement: 1);
        _ = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(l1pSchemaIdentity,
                commonReemitted),
            moduleIdentity + "-common-reemitted", []);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    commonArtifact), moduleIdentity + "-common", []));

        var rootCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Read(ptr<int32> value)
            {
                unsafe { return *value; }
            }
            """));
        Assert.That(rootCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                rootCompilation.Diagnostics));
        var rootArtifact = TrRmExecutableCompiler.EmitStandard(
            rootCompilation.Program!, moduleIdentity, l1pSchemaIdentity,
            moduleTargetRequirement: 1);
        var rootInput = new TrRmStandardArtifactInput(l1pSchemaIdentity,
            rootArtifact);

        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
                moduleIdentity, []));

        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public int32 Unused() { return 1; }"));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, providerIdentity, SchemaIdentity,
            moduleTargetRequirement: 1);
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, moduleIdentity, [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    providerArtifact),
            ]));
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

    private static (
        byte[] RootArtifact, byte[] MethodProviderArtifact,
        byte[] TypeProviderArtifact)
        CreateL1pImportedAggregateMethodArtifacts(
        string rootIdentity, string methodProviderIdentity,
        string typeProviderIdentity, string schemaIdentity)
    {
        var typeProviderArtifact = CreateL1pTypeProviderArtifact(
            typeProviderIdentity, schemaIdentity);
        var typeProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, typeProviderArtifact);
        var typeReference = TrRmExecutableCompiler.CreateStandardReference(
            typeProviderInput, typeProviderIdentity, [],
            ["SnapshotKey", "InnerKey", "RawWordKey"]);
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            LayoutCoreRegistration =
                LayoutCoreRegistrationTestData.Create(),
        };
        var methodProvider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Actual(ptr<Snapshot> source) {
                unsafe {
                    Snapshot captured = *source;
                    return int32(captured.Value.Wide)
                        + captured.Nested.First;
                }
            }
            internal int32 ActualLocal(ptr<Snapshot> source) {
                unsafe {
                    Snapshot captured = *source;
                    return int32(captured.Value.Wide)
                        + captured.Nested.First;
                }
            }
            """), typeReference, options);
        Assert.That(methodProvider.HasErrors, Is.False,
            string.Join(Environment.NewLine, methodProvider.Diagnostics));
        var methodProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            methodProvider.Program!, methodProviderIdentity,
            schemaIdentity, 1);

        var rootCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                internal int32 Expected(ptr<Snapshot> source) {
                    unsafe {
                        Snapshot captured = *source;
                        return int32(captured.Value.Wide)
                            + captured.Nested.First;
                    }
                }

                public int32 Main(ptr<Snapshot> source) {
                    return Expected(source);
                }
                """), typeReference, options);
        Assert.That(rootCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var rootArtifact = TrRmExecutableCompiler.EmitStandard(
            rootCompilation.Program!, rootIdentity, schemaIdentity, 1);
        rootArtifact = AddL1pMethodImportToRootArtifact(rootArtifact,
            methodProviderIdentity, "Actual", schemaIdentity);
        return (rootArtifact, methodProviderArtifact,
            typeProviderArtifact);
    }

    private static byte[] CreateL1pTypeProviderArtifact(
        string moduleIdentity, string schemaIdentity,
        bool relocate = false, bool incompatible = false,
        bool incompatibleRawUnion = false, bool incompatibleArray = false)
    {
        var prefix = relocate ? """
            [Blittable] [Layout(LayoutKind.Sequential)]
            internal struct Prefix {
                public uint8 Value;
            }
            """ : string.Empty;
        var firstFieldType = incompatible ? "uint32" : "int32";
        var wideFieldType = incompatibleRawUnion ? "uint32" : "uint64";
        var arrayElementType = incompatibleArray ? "uint32" : "int32";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
            {{prefix}}
            [Blittable] [Layout(LayoutKind.Union)]
            public struct RawWord {
                public mut uint32 Narrow;
                public mut {{wideFieldType}} Wide;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Inner {
                public mut {{firstFieldType}} First;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Snapshot {
                public mut RawWord Value;
                public mut Inner Nested;
                public mut int32 Second;
                public mut int32 Third;
                public mut [{{arrayElementType}}; 3] Values;
            }
            """), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            LayoutCoreRegistration =
                LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return TrRmExecutableCompiler.EmitStandard(compilation.Program!,
            moduleIdentity, schemaIdentity, 1, [
                new TrRmSourceTypeExport("", "Snapshot", "SnapshotKey"),
                new TrRmSourceTypeExport("", "Inner", "InnerKey"),
                new TrRmSourceTypeExport("", "RawWord", "RawWordKey"),
            ]);
    }

    private static byte[] MutateL1pPointerLoadRegister(
        byte[] artifact, string schemaIdentity, string methodName,
        int invalidRegister, bool invalidDestination)
    {
        var index = new TrRmArtifactIndex(artifact,
            TrRmArtifactSchema.LocalAggregateLayoutDraft,
            resourceLedger: null, usesNativePointerLoads: true,
            standardSchemaIdentity: schemaIdentity);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                index);
        var methodRow = 0;
        for (var candidate = 1;
            candidate <= tables.Methods.Count; candidate++)
        {
            if (tables.Methods.GetName(candidate) == methodName)
            {
                methodRow = candidate;
                break;
            }
        }
        Assert.That(methodRow, Is.GreaterThan(0));
        var bodyRow = tables.Methods.GetBodyRow(methodRow);
        Assert.That(bodyRow, Is.GreaterThan(0));
        var bodyIndex = new TrRmVariableTableIndex(
            GetSectionBytes(index, 0x0020), tables.Bodies.Count, 1, 1);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var bodyBytes = bodyIndex.GetRow(bodyRow).ToArray();
        var codeOffset = bodyBytes.Length - body.Code.Length;
        if (!bodyBytes.AsSpan(codeOffset).SequenceEqual(body.Code))
        {
            throw new InvalidDataException(
                "The L1P test Body code is not the final variable-row payload.");
        }

        var foundLoad = false;
        for (var position = 0; position < body.Code.Length;)
        {
            var instructionLength
                = TrRmRegisterBodyReader.InstructionLength(
                    body.Code[position], body.Schema,
                    usesNativePointerLoads: true);
            if (body.Code[position] == 0x1B)
            {
                var operandOffset = codeOffset + position + 1;
                var operand = bodyBytes[operandOffset];
                bodyBytes[operandOffset] = invalidDestination
                    ? (byte)((operand & 0xF0) | invalidRegister)
                    : (byte)((operand & 0x0F)
                        | (invalidRegister << 4));
                foundLoad = true;
                break;
            }
            position += instructionLength;
        }
        if (!foundLoad)
        {
            throw new InvalidDataException(
                "The L1P test method has no typed pointer-load instruction.");
        }

        var bodyRows = new ReadOnlyMemory<byte>[tables.Bodies.Count];
        for (var row = 1; row <= bodyRows.Length; row++)
        {
            bodyRows[row - 1] = row == bodyRow
                ? bodyBytes
                : bodyIndex.GetRow(row).ToArray();
        }

        return ReplaceSections(index, new Dictionary<ushort, TrRmSection> {
            [0x0020] = new(0x0020, 0,
                checked((uint)bodyRows.Length),
                TrRmVariableTableWriter.EncodeBodies(bodyRows)),
        });
    }

    private static byte[] TruncateL1pPointerLoadOperand(
        byte[] artifact, string schemaIdentity, string methodName)
    {
        var index = new TrRmArtifactIndex(artifact,
            TrRmArtifactSchema.LocalAggregateLayoutDraft,
            resourceLedger: null, usesNativePointerLoads: true,
            standardSchemaIdentity: schemaIdentity);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                index);
        var methodRow = 0;
        for (var candidate = 1;
            candidate <= tables.Methods.Count; candidate++)
        {
            if (tables.Methods.GetName(candidate) == methodName)
            {
                methodRow = candidate;
                break;
            }
        }
        Assert.That(methodRow, Is.GreaterThan(0));
        var bodyRow = tables.Methods.GetBodyRow(methodRow);
        Assert.That(bodyRow, Is.GreaterThan(0));
        var bodyIndex = new TrRmVariableTableIndex(
            GetSectionBytes(index, 0x0020), tables.Bodies.Count, 1, 1);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        Assert.That(body.BlockCount, Is.EqualTo(1));
        var bodyBytes = bodyIndex.GetRow(bodyRow).ToArray();
        var codeOffset = bodyBytes.Length - body.Code.Length;
        if (!bodyBytes.AsSpan(codeOffset).SequenceEqual(body.Code))
        {
            throw new InvalidDataException(
                "The L1P test Body code is not the final variable-row payload.");
        }

        var loadPosition = -1;
        for (var position = 0; position < body.Code.Length;)
        {
            var instructionLength
                = TrRmRegisterBodyReader.InstructionLength(
                    body.Code[position], body.Schema,
                    usesNativePointerLoads: true);
            if (body.Code[position] == 0x1B)
            {
                loadPosition = position;
                break;
            }
            position += instructionLength;
        }
        Assert.That(loadPosition, Is.GreaterThanOrEqualTo(0));

        var mapCountBytes = 0;
        for (var mask = body.MapMask; mask != 0; mask >>= 1)
        {
            mapCountBytes += mask & 1;
        }
        var mapStart = 5 + (mapCountBytes * sizeof(uint));
        var blockStart = mapStart + (body.HomeCount * 8)
            + (body.TypeCount * sizeof(uint))
            + (body.MethodCount * sizeof(uint))
            + (body.FieldCount * sizeof(uint))
            + (body.ConstantCount * 8);
        var truncatedCodeLength = loadPosition + 1;
        BinaryPrimitives.WriteUInt32LittleEndian(
            bodyBytes.AsSpan(blockStart + 4),
            checked((uint)truncatedCodeLength));
        BinaryPrimitives.WriteUInt32LittleEndian(
            bodyBytes.AsSpan(5 + (mapCountBytes * sizeof(uint)) + 8),
            checked((uint)truncatedCodeLength));
        var truncatedBody = bodyBytes.AsSpan(0,
            codeOffset + truncatedCodeLength).ToArray();

        var bodyRows = new ReadOnlyMemory<byte>[tables.Bodies.Count];
        for (var row = 1; row <= bodyRows.Length; row++)
        {
            bodyRows[row - 1] = row == bodyRow
                ? truncatedBody
                : bodyIndex.GetRow(row).ToArray();
        }

        return ReplaceSections(index, new Dictionary<ushort, TrRmSection> {
            [0x0020] = new(0x0020, 0,
                checked((uint)bodyRows.Length),
                TrRmVariableTableWriter.EncodeBodies(bodyRows)),
        });
    }

    private static byte[] MutateL1pFormalHomeTypeMap(
        byte[] artifact, string schemaIdentity, string methodName)
    {
        var index = new TrRmArtifactIndex(artifact,
            TrRmArtifactSchema.LocalAggregateLayoutDraft,
            resourceLedger: null, usesNativePointerLoads: true,
            standardSchemaIdentity: schemaIdentity);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                index);
        var methodRow = 0;
        for (var candidate = 1;
            candidate <= tables.Methods.Count; candidate++)
        {
            if (tables.Methods.GetName(candidate) == methodName)
            {
                methodRow = candidate;
                break;
            }
        }
        Assert.That(methodRow, Is.GreaterThan(0));
        var bodyRow = tables.Methods.GetBodyRow(methodRow);
        Assert.That(bodyRow, Is.GreaterThan(0));
        var bodyIndex = new TrRmVariableTableIndex(
            GetSectionBytes(index, 0x0020), tables.Bodies.Count, 1, 1);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        Assert.That(body.HomeCount, Is.GreaterThan(0));
        var bodyBytes = bodyIndex.GetRow(bodyRow).ToArray();

        var formalHome = body.HomeCount - 1;
        Assert.That(body.HomeMap[formalHome * 8], Is.EqualTo(2));
        var homeTypeMapIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(
                body.HomeMap[(formalHome * 8 + 2)..]);
        var homeTypeSpecRef = BinaryPrimitives.ReadUInt32LittleEndian(
            body.TypeMap[(homeTypeMapIndex * sizeof(uint))..]);
        var malformedTypeMapIndex = -1;
        for (var typeMapIndex = 0; typeMapIndex < body.TypeCount;
            typeMapIndex++)
        {
            var candidateTypeSpecRef = BinaryPrimitives
                .ReadUInt32LittleEndian(
                    body.TypeMap[(typeMapIndex * sizeof(uint))..]);
            if (!tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                    candidateTypeSpecRef, homeTypeSpecRef))
            {
                malformedTypeMapIndex = typeMapIndex;
                break;
            }
        }
        Assert.That(malformedTypeMapIndex,
            Is.GreaterThanOrEqualTo(0));

        var mapCountBytes = 0;
        for (var mask = body.MapMask; mask != 0; mask >>= 1)
        {
            mapCountBytes += mask & 1;
        }
        var mapStart = 5 + (mapCountBytes * sizeof(uint));
        BinaryPrimitives.WriteUInt16LittleEndian(
            bodyBytes.AsSpan(mapStart + (formalHome * 8) + 2),
            checked((ushort)malformedTypeMapIndex));

        var bodyRows = new ReadOnlyMemory<byte>[tables.Bodies.Count];
        for (var row = 1; row <= bodyRows.Length; row++)
        {
            bodyRows[row - 1] = row == bodyRow
                ? bodyBytes
                : bodyIndex.GetRow(row).ToArray();
        }

        return ReplaceSections(index, new Dictionary<ushort, TrRmSection> {
            [0x0020] = new(0x0020, 0,
                checked((uint)bodyRows.Length),
                TrRmVariableTableWriter.EncodeBodies(bodyRows)),
        });
    }

    private static byte[] MutateL1pStorageAddressElementCount(
        byte[] artifact, string schemaIdentity, string methodName)
        => MutateL1pStorageAddressRecord(artifact, schemaIdentity,
            methodName, 2,
            static (bodyBytes, recordStart) =>
                BinaryPrimitives.WriteUInt32LittleEndian(
                    bodyBytes.AsSpan(recordStart + 12), 2));

    private static byte[] MutateL1pStorageAddressHomeIndex(
        byte[] artifact, string schemaIdentity, string methodName,
        bool resultHomeIndex)
        => MutateL1pStorageAddressRecord(artifact, schemaIdentity,
            methodName, 2,
            (bodyBytes, recordStart) =>
                BinaryPrimitives.WriteUInt16LittleEndian(
                    bodyBytes.AsSpan(recordStart
                        + (resultHomeIndex ? 8 : 6)),
                    ushort.MaxValue));

    private static byte[] MutateL1pStorageAddressTypeMapIndex(
        byte[] artifact, string schemaIdentity, string methodName,
        byte sourceKind, ushort invalidTypeMapIndex)
        => MutateL1pStorageAddressRecord(artifact, schemaIdentity,
            methodName, sourceKind,
            (bodyBytes, recordStart) =>
                BinaryPrimitives.WriteUInt16LittleEndian(
                    bodyBytes.AsSpan(recordStart + 10),
                    invalidTypeMapIndex));

    private static byte[] MutateL1pStorageAddressSourceRegister(
        byte[] artifact, string schemaIdentity, string methodName,
        ushort invalidRegister)
        => MutateL1pStorageAddressRecord(artifact, schemaIdentity,
            methodName, 1,
            (bodyBytes, recordStart) =>
                BinaryPrimitives.WriteUInt16LittleEndian(
                    bodyBytes.AsSpan(recordStart + 6), invalidRegister));

    private static ushort FindL1pNonPointerTypeMapIndex(
        byte[] artifact, string schemaIdentity, string methodName)
    {
        var index = new TrRmArtifactIndex(artifact,
            TrRmArtifactSchema.LocalAggregateLayoutDraft,
            resourceLedger: null, usesNativePointerLoads: true,
            standardSchemaIdentity: schemaIdentity);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                index);
        var methodRow = 0;
        for (var candidate = 1;
            candidate <= tables.Methods.Count; candidate++)
        {
            if (tables.Methods.GetName(candidate) == methodName)
            {
                methodRow = candidate;
                break;
            }
        }
        Assert.That(methodRow, Is.GreaterThan(0));
        var bodyRow = tables.Methods.GetBodyRow(methodRow);
        Assert.That(bodyRow, Is.GreaterThan(0));
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        for (var typeMapIndex = 0;
            typeMapIndex < body.TypeCount; typeMapIndex++)
        {
            var typeSpecRef = BinaryPrimitives.ReadUInt32LittleEndian(
                body.TypeMap[(typeMapIndex * sizeof(uint))..]);
            if (!tables.TypeSpecs.TryGetNativePointerPointeeTypeSpecRef(
                    typeSpecRef, out _))
            {
                return checked((ushort)typeMapIndex);
            }
        }

        throw new InvalidDataException(
            "The L1P test Body has no non-pointer TypeMap entry.");
    }

    private static byte[] MutateL1pStorageAddressRecord(
        byte[] artifact, string schemaIdentity, string methodName,
        byte expectedSourceKind, Action<byte[], int> mutateRecord)
    {
        var index = new TrRmArtifactIndex(artifact,
            TrRmArtifactSchema.LocalAggregateLayoutDraft,
            resourceLedger: null, usesNativePointerLoads: true,
            standardSchemaIdentity: schemaIdentity);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                index);
        var methodRow = 0;
        for (var candidate = 1;
            candidate <= tables.Methods.Count; candidate++)
        {
            if (tables.Methods.GetName(candidate) == methodName)
            {
                methodRow = candidate;
                break;
            }
        }
        Assert.That(methodRow, Is.GreaterThan(0));
        var bodyRow = tables.Methods.GetBodyRow(methodRow);
        Assert.That(bodyRow, Is.GreaterThan(0));
        var bodyIndex = new TrRmVariableTableIndex(
            GetSectionBytes(index, 0x0020), tables.Bodies.Count, 1, 1);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var bodyBytes = bodyIndex.GetRow(bodyRow).ToArray();
        var codeOffset = bodyBytes.Length - body.Code.Length;
        if (!bodyBytes.AsSpan(codeOffset).SequenceEqual(body.Code))
        {
            throw new InvalidDataException(
                "The L1P test Body code is not the final variable-row payload.");
        }

        var foundAddress = false;
        for (var position = 0; position < body.Code.Length;)
        {
            var instructionLength
                = TrRmRegisterBodyReader.InstructionLength(
                    body.Code[position], body.Schema,
                    usesNativePointerLoads: true);
            if (body.Code[position] == 0xEC)
            {
                var recordIndex
                    = BinaryPrimitives.ReadUInt16LittleEndian(
                        body.Code[(position + 2)..]);
                var record = body.GetRecord(recordIndex);
                Assert.That(record[0], Is.EqualTo(0x0E));
                Assert.That(record[4], Is.EqualTo(expectedSourceKind));
                Assert.That(record.Length, Is.EqualTo(20));

                var mapCountBytes = 0;
                for (var mask = body.MapMask; mask != 0; mask >>= 1)
                {
                    mapCountBytes += mask & 1;
                }
                var mapStart = 5 + (mapCountBytes * sizeof(uint));
                var blockStart = mapStart + (body.HomeCount * 8)
                    + (body.TypeCount * sizeof(uint))
                    + (body.MethodCount * sizeof(uint))
                    + (body.FieldCount * sizeof(uint))
                    + (body.ConstantCount * 8);
                var recordOffsetsStart
                    = blockStart + (body.BlockCount * 12);
                var recordDataStart = recordOffsetsStart
                    + ((body.RecordCount + 1) * sizeof(uint));
                var recordOffset = BinaryPrimitives
                    .ReadUInt32LittleEndian(bodyBytes.AsSpan(
                        recordOffsetsStart + (recordIndex * sizeof(uint))));
                var recordStart = recordDataStart
                    + checked((int)recordOffset);
                mutateRecord(bodyBytes, recordStart);
                foundAddress = true;
                break;
            }
            position += instructionLength;
        }
        if (!foundAddress)
        {
            throw new InvalidDataException(
                "The L1P test method has no StorageAddress instruction.");
        }

        var bodyRows = new ReadOnlyMemory<byte>[tables.Bodies.Count];
        for (var row = 1; row <= bodyRows.Length; row++)
        {
            bodyRows[row - 1] = row == bodyRow
                ? bodyBytes
                : bodyIndex.GetRow(row).ToArray();
        }

        return ReplaceSections(index, new Dictionary<ushort, TrRmSection> {
            [0x0020] = new(0x0020, 0,
                checked((uint)bodyRows.Length),
                TrRmVariableTableWriter.EncodeBodies(bodyRows)),
        });
    }

    private static byte[] AddL1pMethodImportToRootArtifact(
        byte[] artifact, string methodProviderIdentity,
        string exportKey, string schemaIdentity)
    {
        var index = new TrRmArtifactIndex(artifact,
            TrRmArtifactSchema.LocalAggregateLayoutDraft,
            resourceLedger: null, usesNativePointerLoads: true,
            standardSchemaIdentity: schemaIdentity);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                index);
        var expectedMethodRow = 0;
        var mainMethodRow = 0;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            switch (tables.Methods.GetName(methodRow))
            {
                case "Expected":
                    expectedMethodRow = methodRow;
                    break;
                case "Main":
                    mainMethodRow = methodRow;
                    break;
            }
        }
        Assert.That(expectedMethodRow, Is.GreaterThan(0));
        Assert.That(mainMethodRow, Is.GreaterThan(0));
        var expectedBodyRow
            = tables.Methods.GetBodyRow(expectedMethodRow);
        Assert.That(expectedBodyRow, Is.GreaterThan(0));
        var importRow = tables.Imports.Count + 1;
        var localMethodToken = 0x1600_0000u
            | checked((uint)expectedMethodRow);
        var importedMethodToken = 0x1C00_0000u
            | checked((uint)importRow);

        var methodRows = GetSectionBytes(index, 0x0016);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodRows.AsSpan(((expectedMethodRow - 1) * 28) + 12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodRows.AsSpan(((expectedMethodRow - 1) * 28) + 24), 0);

        var bodies = new List<ReadOnlyMemory<byte>>();
        var bodyRowByOriginal = new int[tables.Bodies.Count + 1];
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            var originalBodyRow = tables.Methods.GetBodyRow(methodRow);
            if (originalBodyRow == 0
                || originalBodyRow == expectedBodyRow)
            {
                continue;
            }

            var remappedBodyRow = bodyRowByOriginal[originalBodyRow];
            if (remappedBodyRow == 0)
            {
                var body = tables.Bodies.GetRegisterBody(originalBodyRow,
                    tables.Contracts.Count);
                var methodMap = body.MethodMap.ToArray();
                var foundExpectedTarget = false;
                for (var offset = 0; offset < methodMap.Length;
                    offset += sizeof(uint))
                {
                    var target = BinaryPrimitives.ReadUInt32LittleEndian(
                        methodMap.AsSpan(offset));
                    if (target == localMethodToken)
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(
                            methodMap.AsSpan(offset), importedMethodToken);
                        foundExpectedTarget = true;
                    }
                }
                if (methodRow == mainMethodRow && !foundExpectedTarget)
                {
                    throw new InvalidDataException(
                        "The L1P Main Body does not retain its Expected MethodMap target.");
                }

                var blocks = new TrRmRegisterBodyBlock[body.BlockCount];
                for (var blockIndex = 0; blockIndex < body.BlockCount;
                    blockIndex++)
                {
                    var block = body.GetBlock(blockIndex);
                    blocks[blockIndex] = new TrRmRegisterBodyBlock(
                        BinaryPrimitives.ReadUInt32LittleEndian(block),
                        BinaryPrimitives.ReadUInt32LittleEndian(block[4..]),
                        BinaryPrimitives.ReadUInt16LittleEndian(block[8..]));
                }
                var records = new ReadOnlyMemory<byte>[body.RecordCount];
                for (var recordIndex = 0;
                    recordIndex < body.RecordCount; recordIndex++)
                {
                    records[recordIndex]
                        = body.GetRecord(recordIndex).ToArray();
                }
                var encoded = TrRmRegisterBodyWriter.Encode(
                    body.HomeMap.ToArray(), body.TypeMap.ToArray(),
                    methodMap, body.FieldMap.ToArray(),
                    body.ConstantMap.ToArray(), blocks, records,
                    body.Code.ToArray(), tables.Contracts.Count,
                    TrRmArtifactSchema.LocalAggregateLayoutDraft,
                    usesNativePointerLoads: true);
                bodies.Add(encoded);
                remappedBodyRow = bodies.Count;
                bodyRowByOriginal[originalBodyRow] = remappedBodyRow;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(
                methodRows.AsSpan(((methodRow - 1) * 28) + 24),
                checked((uint)remappedBodyRow));
        }

        var strings = new List<(string Text, int Encoding)>(
            tables.Strings.Count + 2);
        var stringRowsByText = new Dictionary<string, int>(
            tables.Strings.Count + 2, StringComparer.Ordinal);
        for (var row = 1; row <= tables.Strings.Count; row++)
        {
            var text = tables.Strings.DecodeNamespace(row);
            strings.Add((text, 1));
            _ = stringRowsByText.TryAdd(text, row);
        }
        int GetStringRow(string value)
        {
            if (!stringRowsByText.TryGetValue(value, out var row))
            {
                row = strings.Count + 1;
                strings.Add((value, 1));
                stringRowsByText.Add(value, row);
            }
            return row;
        }

        var providerIdentityRow = GetStringRow(methodProviderIdentity);
        var exportKeyRow = GetStringRow(exportKey);
        var moduleRefs = GetSectionBytes(index, 0x0010);
        var moduleRefRow = tables.ModuleRefs.Count + 1;
        Array.Resize(ref moduleRefs, checked(moduleRefs.Length + 12));
        var moduleRefOffset = moduleRefs.Length - 12;
        BinaryPrimitives.WriteInt32LittleEndian(
            moduleRefs.AsSpan(moduleRefOffset), providerIdentityRow);
        BinaryPrimitives.WriteUInt32LittleEndian(
            moduleRefs.AsSpan(moduleRefOffset + 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            moduleRefs.AsSpan(moduleRefOffset + 8), 0);

        var imports = GetSectionBytes(index, 0x001C);
        Array.Resize(ref imports, checked(imports.Length + 12));
        var importOffset = imports.Length - 12;
        BinaryPrimitives.WriteInt32LittleEndian(
            imports.AsSpan(importOffset), moduleRefRow);
        BinaryPrimitives.WriteInt32LittleEndian(
            imports.AsSpan(importOffset + 4), exportKeyRow);
        BinaryPrimitives.WriteUInt32LittleEndian(
            imports.AsSpan(importOffset + 8), localMethodToken);

        return ReplaceSections(index, new Dictionary<ushort, TrRmSection> {
            [0x0002] = new(0x0002, 0,
                checked((uint)strings.Count),
                TrRmStringTableWriter.EncodeTextRows(strings)),
            [0x0010] = new(0x0010, 0,
                checked((uint)moduleRefRow), moduleRefs),
            [0x0016] = new(0x0016, 0,
                checked((uint)tables.Methods.Count), methodRows),
            [0x001C] = new(0x001C, 0,
                checked((uint)importRow), imports),
            [0x0020] = new(0x0020, 0,
                checked((uint)bodies.Count),
                TrRmVariableTableWriter.EncodeBodies(bodies)),
        });
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
        Dictionary<ushort, TrRmSection> replacements)
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
            index.Schema,
            standardSchemaIdentity: index.StandardSchemaIdentity);
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
