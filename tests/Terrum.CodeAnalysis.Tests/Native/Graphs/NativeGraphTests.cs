// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Native.Graphs;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class NativeGraphTests
{
    private const string CounterOutputImport = """
        [Blittable]
        [Layout(LayoutKind.Sequential)]
        public struct Counter { public int64 Value; }
        [Terrum.Interop.NativeImport("kernel32.dll", "QueryPerformanceCounter")]
        public extern int32 ReadCounter(
            [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
            write ptr<Counter> counter);
        """;

    private static string CompilerApphost()
    {
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        return Path.Combine(root, "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
    }

    private static byte[] Leaf(string source, NativeRuntimePolicy policy = NativeRuntimePolicy.FailFast)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            LayoutOptions(policy));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual0 = compilation.Program;
            Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual0!;
        }))();
        return NativeModuleCompiler.CreateProducer(program,
            ["Leaf"], "NodeB", policy);
    }

    private static byte[] Middle(string source, byte[] leaf,
        NativeRuntimePolicy policy = NativeRuntimePolicy.FailFast)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            NativeModuleCompiler.CreateGraphReference([leaf]),
            LayoutOptions(policy));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual1 = compilation.Program;
            Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual1!;
        }))();
        return NativeModuleCompiler.CreateGraphProducer(program,
            ["Middle"], "NodeA", [leaf], policy);
    }

    private static byte[] Consumer(byte[] middle, NativeRuntimePolicy policy = NativeRuntimePolicy.FailFast)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Middle() + 1; }"),
            NativeModuleCompiler.CreateGraphReference([middle]), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual2 = compilation.Program;
            Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual2!;
        }))();
        return NativeModuleCompiler.CreateGraphConsumer(program,
            [middle], policy);
    }

    private static CompilationOptions LayoutOptions(
        NativeRuntimePolicy policy)
    {
        var nativeRegistration
            = NativeCoreRegistrationTests.CreateRegistration();
        return new() {
            RuntimePolicy = (BoundRuntimePolicy)policy,
            LayoutCoreRegistration = NativeCoreRegistrationTests
                .CreateLayoutRegistration(nativeRegistration),
            NativeCoreRegistration = nativeRegistration,
        };
    }

    private static byte[] WeakenRecordedImport(byte[] artifact, string name, string oldSuffix, string newSuffix)
    {
        var anchor = Encoding.ASCII.GetBytes(name + "|");
        var at = artifact.AsSpan().IndexOf(anchor);
        Assert.That(at >= 4, Is.True);
        Assert.That(artifact.AsSpan(at + 1).IndexOf(anchor), Is.EqualTo(-1));
        var length = BinaryPrimitives.ReadInt32LittleEndian(artifact.AsSpan(at - 4));
        var saved = Encoding.ASCII.GetString(artifact, at, length);
        Assert.That(saved, Does.EndWith(oldSuffix));
        var suffixStart = at + length - oldSuffix.Length;
        var difference = newSuffix.Length - oldSuffix.Length;
        var result = new byte[artifact.Length + difference];
        artifact.AsSpan(0, suffixStart).CopyTo(result);
        Encoding.ASCII.GetBytes(newSuffix).CopyTo(result, suffixStart);
        artifact.AsSpan(at + length).CopyTo(result.AsSpan(at + length + difference));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(at - 4), checked(length + difference));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(11),
            checked(BinaryPrimitives.ReadInt32LittleEndian(artifact.AsSpan(11)) + difference));
        SHA256.HashData(result.AsSpan(0, result.Length - 32)).CopyTo(result, result.Length - 32);
        return result;
    }

    private static byte[] ReplaceRecordedGraphImport(byte[] artifact, string dependencyIdentity,
        string oldName, string newName)
    {
        Assert.That(newName.Length, Is.EqualTo(oldName.Length));
        var result = (byte[])artifact.Clone();
        var at = 19 + result[7];
        var replaced = false;
        for (var i = 0; i < result[8]; i++)
        {
            var identityLength = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(at));
            at += 2;
            var identity = Encoding.UTF8.GetString(result, at, identityLength);
            at += identityLength;
            var declarationCount = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(at));
            at += 2;
            for (var j = 0; j < declarationCount; j++)
            {
                var length = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(at));
                at += 2 + length;
            }
            var importCount = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(at));
            at += 2;
            for (var j = 0; j < importCount; j++)
            {
                var length = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(at));
                at += 2;
                if (identity == dependencyIdentity
                    && Encoding.UTF8.GetString(result, at, length) == oldName)
                {
                    Assert.That(replaced, Is.False);
                    Encoding.UTF8.GetBytes(newName).CopyTo(result, at);
                    replaced = true;
                }
                at += length;
            }
        }
        Assert.That(replaced, Is.True);
        SHA256.HashData(result.AsSpan(0, result.Length - 32)).CopyTo(result, result.Length - 32);
        return result;
    }

    private static int GraphContractOffset(byte[] artifact)
    {
        var at = 19 + artifact[7];
        for (var dependency = 0; dependency < artifact[8]; dependency++)
        {
            SkipGraphString(artifact, ref at);
            var declarationCount = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
            at += 2;
            for (var declaration = 0; declaration < declarationCount; declaration++)
            {
                SkipGraphString(artifact, ref at);
            }

            var importCount = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
            at += 2;
            for (var import = 0; import < importCount; import++)
            {
                SkipGraphString(artifact, ref at);
            }
        }

        var exportCount = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(9, 2));
        for (var export = 0; export < exportCount; export++)
        {
            SkipGraphString(artifact, ref at);
        }

        var originCount = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
        at += 2;
        for (var origin = 0; origin < originCount; origin++)
        {
            SkipGraphString(artifact, ref at);
            SkipGraphString(artifact, ref at);
            var cleanupLength = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
            at += 2 + cleanupLength;
        }
        var expectationCount = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
        at += 2;
        for (var expectation = 0; expectation < expectationCount; expectation++)
        {
            for (var field = 0; field < 5; field++)
            {
                SkipGraphString(artifact, ref at);
            }
            var parameterCount = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
            at += 2 + (parameterCount * 14);
        }

        var contractLength = BinaryPrimitives.ReadInt32LittleEndian(artifact.AsSpan(11, 4));
        Assert.That(at + contractLength, Is.LessThanOrEqualTo(artifact.Length - 32));
        return at;
    }

    private static int GraphDirectExpectationStringOffset(byte[] artifact,
        string functionName, int fieldIndex)
    {
        var at = 19 + artifact[7];
        for (var dependency = 0; dependency < artifact[8]; dependency++)
        {
            SkipGraphString(artifact, ref at);
            var declarationCount = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
            at += 2;
            for (var declaration = 0; declaration < declarationCount; declaration++)
            {
                SkipGraphString(artifact, ref at);
            }
            var importCount = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
            at += 2;
            for (var import = 0; import < importCount; import++)
            {
                SkipGraphString(artifact, ref at);
            }
        }
        var exportCount = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(9, 2));
        for (var export = 0; export < exportCount; export++)
        {
            SkipGraphString(artifact, ref at);
        }
        var originCount = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
        at += 2;
        for (var origin = 0; origin < originCount; origin++)
        {
            SkipGraphString(artifact, ref at);
            SkipGraphString(artifact, ref at);
            var cleanupLength = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
            at += 2 + cleanupLength;
        }
        var expectationCount = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
        at += 2;
        Assert.That(expectationCount, Is.GreaterThan(0));
        for (var expectation = 0; expectation < expectationCount; expectation++)
        {
            var localName = string.Empty;
            for (var field = 0; field < 5; field++)
            {
                var length = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
                var valueOffset = at + 2;
                var value = Encoding.UTF8.GetString(artifact, valueOffset, length);
                if (field == 0)
                {
                    localName = value;
                }
                if (localName == functionName && field == fieldIndex)
                {
                    return valueOffset;
                }
                at = valueOffset + length;
            }
            var parameterCount = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
            at += 2 + (parameterCount * 14);
        }
        throw new InvalidOperationException(
            $"Native graph has no direct expectation field {fieldIndex} for '{functionName}'.");
    }

    private static int GraphMachineImportCount(byte[] artifact)
    {
        var at = 19 + artifact[7];
        var total = 0;
        for (var dependency = 0; dependency < artifact[8]; dependency++)
        {
            SkipGraphString(artifact, ref at);
            var declarationCount = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
            at += 2;
            for (var declaration = 0; declaration < declarationCount; declaration++)
            {
                SkipGraphString(artifact, ref at);
            }
            var importCount = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
            total += importCount;
            at += 2;
            for (var import = 0; import < importCount; import++)
            {
                SkipGraphString(artifact, ref at);
            }
        }
        return total;
    }

    private static int NativeOutputSelectorOffset(byte[] artifact)
    {
        var contractOffset = GraphContractOffset(artifact);
        var contractLength = BinaryPrimitives.ReadInt32LittleEndian(artifact.AsSpan(11, 4));
        var name = Encoding.UTF8.GetBytes("QueryPerformanceCounter");
        var nameRecord = new byte[sizeof(int) + name.Length];
        BinaryPrimitives.WriteInt32LittleEndian(nameRecord, name.Length);
        name.CopyTo(nameRecord, sizeof(int));
        var relativeFunctionOffset = artifact.AsSpan(contractOffset, contractLength)
            .IndexOf(nameRecord);
        Assert.That(relativeFunctionOffset, Is.GreaterThanOrEqualTo(0));
        var contractLengthOffset = contractOffset + relativeFunctionOffset + nameRecord.Length;
        var functionContractLength = BinaryPrimitives.ReadInt32LittleEndian(
            artifact.AsSpan(contractLengthOffset, sizeof(int)));
        var outputCountOffset = contractLengthOffset + sizeof(int) + functionContractLength + 3;
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            artifact.AsSpan(outputCountOffset, sizeof(int))), Is.EqualTo(1));
        return outputCountOffset + 18;
    }

    private static void SkipGraphString(byte[] artifact, ref int offset)
    {
        var length = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(offset, 2));
        offset += 2 + length;
    }

    [Test]
    public void GraphPackagesUseCurrentVersionAndRejectPreviousVersion()
    {
        var leaf = Leaf(" public int32 Leaf() { return 40; }");
        var middle = Middle("public int32 Middle() { return Leaf() + 1; }", leaf);
        var root = Consumer(middle);
        Assert.That(middle[4], Is.EqualTo(3));
        Assert.That(root[4], Is.EqualTo(3));

        foreach (var artifact in new[] { middle, root })
        {
            var previousVersion = (byte[])artifact.Clone();
            previousVersion[4] = 2;
            SHA256.HashData(previousVersion.AsSpan(0, previousVersion.Length - 32))
                .CopyTo(previousVersion, previousVersion.Length - 32);
            _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.InspectGraphArtifact(previousVersion))!;
        }
    }

    [Test]
    public void InspectingLeafGraphArtifactPreservesNativeProfile()
    {
        var leaf = Leaf(" public int32 Leaf() { return 40; }");

        Assert.That(NativeModuleCompiler.InspectGraphArtifact(leaf).NativeProfileVersion, Is.EqualTo(NativeModuleCompiler.Inspect(leaf).NativeProfileVersion));
    }

    [Test]
    public void GraphProvidersAreOrderedByIdentity()
    {
        static byte[] CreateStructuredProvider(string structure, string identity)
        {
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
                $"[Blittable] [Layout(LayoutKind.Sequential)] public struct {structure} {{ public int32 Value; }} "
                + $"public int32 Read{structure}({structure} value) {{ return value.Value; }}"),
                LayoutOptions(NativeRuntimePolicy.FailFast));
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            return NativeModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => {
                var __nunitActual3 = compilation.Program;
                Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>());
                return (BoundProgram)__nunitActual3!;
            }))(),
                [$"Read{structure}"], identity, NativeRuntimePolicy.FailFast);
        }

        var reference = NativeModuleCompiler.CreateGraphReference([
            CreateStructuredProvider("Zulu", "NodeZ"),
            CreateStructuredProvider("Alpha", "NodeA"),
        ]);

        Assert.That(reference.Structures.Select(static item => item.Name), Is.EqualTo(["Alpha", "Zulu"]));
    }

    [Test]
    public void ProducerValidationAcceptsUnsortedExportNames()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Zebra() { return 2; }
            public int32 Alpha() { return 1; }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = NativeModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual4 = compilation.Program;
            Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual4!;
        }))(),
            ["Zebra", "Alpha"], "UnsortedExports", NativeRuntimePolicy.FailFast);

        Assert.That(NativeModuleCompiler.Inspect(producer).IsConsumer, Is.False);
    }

    [Test]
    public void ConsumerValidationAcceptsMultipleImports()
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Zebra() { return 2; }
            public int32 Alpha() { return 1; }
            """));
        Assert.That(providerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => {
                var __nunitActual5 = providerCompilation.Program;
                Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>());
                return (BoundProgram)__nunitActual5!;
            }))(), ["Zebra", "Alpha"],
            "MultipleImports", NativeRuntimePolicy.FailFast);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() { return Zebra() + Alpha(); }
            """), NativeModuleCompiler.CreateReference(provider));
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));

        var consumer = NativeModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => {
                var __nunitActual6 = consumerCompilation.Program;
                Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>());
                return (BoundProgram)__nunitActual6!;
            }))(), provider, NativeRuntimePolicy.FailFast);

        Assert.That(consumer, Is.Not.Empty);
    }

    [Test]
    public void ConsumerValidationAcceptsNoImports()
    {
        var provider = Leaf("public int32 Leaf() { return 1; }");
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return 42; }"),
            NativeModuleCompiler.CreateReference(provider));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));

        var consumer = NativeModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => {
                var __nunitActual7 = compilation.Program;
                Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>());
                return (BoundProgram)__nunitActual7!;
            }))(), provider, NativeRuntimePolicy.FailFast);

        Assert.That(consumer, Is.Not.Empty);
    }

    [Test]
    public void GraphProviderValidationAcceptsNoImportsWithDependencies()
    {
        var dependencyCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Node { public int32 Value; }
            public Node GetNode(Node value) { return value; }
            """), LayoutOptions(NativeRuntimePolicy.FailFast));
        Assert.That(dependencyCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, dependencyCompilation.Diagnostics));
        var dependency = NativeModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => {
                var __nunitActual8 = dependencyCompilation.Program;
                Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>());
                return (BoundProgram)__nunitActual8!;
            }))(), ["GetNode"], "NodeB",
            NativeRuntimePolicy.FailFast);
        var dependencyReference = NativeModuleCompiler.CreateGraphReference([dependency]);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public Node Identity(Node value) { return value; }"), dependencyReference);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateGraphProducer(
            ((global::System.Func<BoundProgram>)(() => {
                var __nunitActual9 = compilation.Program;
                Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>());
                return (BoundProgram)__nunitActual9!;
            }))(), ["Identity"], "NodeA", [dependency],
            NativeRuntimePolicy.FailFast);

        Assert.That(provider, Is.Not.Empty);
    }

    [Test]
    public void GraphProviderKeepsDirectNativeImportsSeparateFromGraphDependencies()
    {
        var leaf = Leaf("public int32 Leaf() { return 40; }");
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Packet { public uint32 Value; }
            [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
            extern int32 ReadValue(
                [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<Packet> value);
            public int32 ReadAndReturn() {
                init Packet value;
                unsafe {
                    if (ReadValue(&init value) != 0) {
                        return Leaf() + int32(value.Value);
                    }
                }
                return -1;
            }
            """), NativeModuleCompiler.CreateGraphReference([leaf]),
            LayoutOptions(NativeRuntimePolicy.FailFast));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var provider = NativeModuleCompiler.CreateGraphProducer(compilation.Program!,
            ["ReadAndReturn"], "NodeA", [leaf], NativeRuntimePolicy.FailFast);
        var info = NativeModuleCompiler.InspectGraphArtifact(provider);

        Assert.That(info.Dependencies, Is.EqualTo(["NodeB"]));
        Assert.That(info.Exports, Is.EqualTo(["ReadAndReturn"]));

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Packet { public uint32 Value; }
            [Terrum.Interop.NativeImport("example.dll", "Adjust")]
            extern int32 Adjust(
                [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<Packet> value);

            int32 Main() {
                init Packet value;
                unsafe {
                    if (Adjust(&init value) != 0) {
                        return ReadAndReturn() + int32(value.Value);
                    }
                }
                return -1;
            }
            """),
            NativeModuleCompiler.CreateGraphReference([provider]),
            LayoutOptions(NativeRuntimePolicy.FailFast));
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateGraphConsumer(consumerCompilation.Program!,
            [provider], NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.ValidateGraph(consumer, [leaf, provider]);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task AggregateNativeOutputRunsThroughGraphAotAndRootJitAsync(
        NativeRuntimePolicy policy)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string MiddleSource = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Counter { public int64 Value; }

            [Terrum.Interop.NativeImport("kernel32.dll", "QueryPerformanceCounter")]
            extern int32 QueryPerformanceCounter(
                [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<Counter> counter);

            public int32 Middle() {
                init Counter counter;
                unsafe {
                    if (QueryPerformanceCounter(&init counter) == 0) { return -1; }
                }
                if (counter.Value < 0) { return -2; }
                return 42 + Leaf();
            }
            """;
        var boundPolicy = (BoundRuntimePolicy)policy;
        var leaf = Leaf("public int32 Leaf() { return 40; }", policy);
        var middleCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(MiddleSource),
            NativeModuleCompiler.CreateGraphReference([leaf]),
            LayoutOptions(policy));
        Assert.That(middleCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, middleCompilation.Diagnostics));
        var middle = NativeModuleCompiler.CreateGraphProducer(
            middleCompilation.Program!, ["Middle"], "NodeA", [leaf], policy);
        var rootCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Middle(); }"),
            NativeModuleCompiler.CreateGraphReference([middle]), boundPolicy);
        Assert.That(rootCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(
            rootCompilation.Program!, [middle], policy);

        Assert.That(NativeModuleCompiler.InspectGraphArtifact(middle).Dependencies,
            Is.EqualTo(["NodeB"]));
        NativeModuleCompiler.ValidateGraph(root, [leaf, middle]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [middle, leaf],
            TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(82));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(root, [leaf, middle],
            CompilerApphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true),
            Is.EqualTo(82));

        var malformedMiddle = (byte[])middle.Clone();
        malformedMiddle[NativeOutputSelectorOffset(malformedMiddle)] = byte.MaxValue;
        SHA256.HashData(malformedMiddle.AsSpan(0, malformedMiddle.Length - 32))
            .CopyTo(malformedMiddle, malformedMiddle.Length - 32);
        _ = Assert.Throws<ModuleFormatException>(
            () => NativeModuleCompiler.InspectGraphArtifact(malformedMiddle));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task GraphIntermediateBindsDirectOutputToSelectedProviderAsync(
        NativeRuntimePolicy policy)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var counterCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(CounterOutputImport),
            LayoutOptions(policy));
        Assert.That(counterCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, counterCompilation.Diagnostics));
        var counterProvider = NativeModuleCompiler.CreateProducer(counterCompilation.Program!,
            ["ReadCounter"], "CounterProvider", policy);
        var leaf = Leaf("public int32 Leaf() { return 40; }", policy);
        var middleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Middle() {
                init Counter counter;
                unsafe {
                    if (ReadCounter(&init counter) == 0) { return -1; }
                }
                if (counter.Value < 0) { return -2; }
                return Leaf();
            }
            """), NativeModuleCompiler.CreateGraphReference([counterProvider, leaf]),
            LayoutOptions(policy));
        Assert.That(middleCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, middleCompilation.Diagnostics));
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.CreateGraphProducer(middleCompilation.Program!,
                ["ReadCounter"], "NoDirectImportProxy", [counterProvider, leaf], policy));
        var middle = NativeModuleCompiler.CreateGraphProducer(middleCompilation.Program!,
            ["Middle"], "NodeA", [counterProvider, leaf], policy);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                init Counter counter;
                unsafe {
                    if (ReadCounter(&init counter) == 0) { return -1; }
                }
                if (counter.Value <= 0) { return -2; }
                return Middle();
            }
            """), NativeModuleCompiler.CreateGraphReference([middle, counterProvider]),
            LayoutOptions(policy));
        Assert.That(rootCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(rootCompilation.Program!,
            [middle, counterProvider], policy);

        NativeModuleCompiler.ValidateGraph(root, [middle, counterProvider, leaf]);
        var malformedRoot = (byte[])root.Clone();
        malformedRoot[GraphDirectExpectationStringOffset(malformedRoot,
            "ReadCounter", 3)] = (byte)'/';
        SHA256.HashData(malformedRoot.AsSpan(0, malformedRoot.Length - 32))
            .CopyTo(malformedRoot, malformedRoot.Length - 32);
        _ = Assert.Throws<ModuleFormatException>(
            () => NativeModuleCompiler.InspectGraphArtifact(malformedRoot));
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root,
            [middle, counterProvider, leaf], TimeSpan.FromSeconds(30), policy)
            .ConfigureAwait(true), Is.EqualTo(40));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(root,
            [middle, counterProvider, leaf], CompilerApphost(), TimeSpan.FromSeconds(30), policy)
            .ConfigureAwait(true), Is.EqualTo(40));

        var changedCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            CounterOutputImport.Replace("QueryPerformanceCounter",
                "QueryPerformanceFrequency", StringComparison.Ordinal)),
            LayoutOptions(policy));
        Assert.That(changedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, changedCompilation.Diagnostics));
        var changedProvider = NativeModuleCompiler.CreateProducer(changedCompilation.Program!,
            ["ReadCounter"], "CounterProvider", policy);
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateGraph(root, [middle, changedProvider, leaf]));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task GraphProviderExportsOwnedDirectOutputThroughLeafThunkAsync(
        NativeRuntimePolicy policy)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var leaf = Leaf("public int32 Leaf() { return 40; }", policy);
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(CounterOutputImport + """
                public int32 FromLeaf() { return Leaf(); }
                """), NativeModuleCompiler.CreateGraphReference([leaf]),
            LayoutOptions(policy));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateGraphProducer(providerCompilation.Program!,
            ["ReadCounter", "FromLeaf"], "CounterGraphProvider", [leaf], policy);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                init Counter counter;
                unsafe {
                    if (ReadCounter(&init counter) == 0) { return -1; }
                }
                if (counter.Value <= 0) { return -2; }
                return FromLeaf();
            }
            """), NativeModuleCompiler.CreateGraphReference([provider]),
            (BoundRuntimePolicy)policy);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateGraphConsumer(
            consumerCompilation.Program!, [provider], policy);

        NativeModuleCompiler.ValidateGraph(consumer, [provider, leaf]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(consumer, [provider, leaf],
            TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(40));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(consumer, [provider, leaf],
            CompilerApphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true),
            Is.EqualTo(40));

        var changedCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            CounterOutputImport.Replace("QueryPerformanceCounter",
                "QueryPerformanceFrequency", StringComparison.Ordinal)
            + "public int32 FromLeaf() { return Leaf(); }"),
            NativeModuleCompiler.CreateGraphReference([leaf]),
            LayoutOptions(policy));
        Assert.That(changedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, changedCompilation.Diagnostics));
        var changedProvider = NativeModuleCompiler.CreateGraphProducer(
            changedCompilation.Program!, ["ReadCounter", "FromLeaf"],
            "CounterGraphProvider", [leaf], policy);
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateGraph(consumer, [changedProvider, leaf]));

        var uncalledCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return 0; }"),
            NativeModuleCompiler.CreateGraphReference([provider]), (BoundRuntimePolicy)policy);
        Assert.That(uncalledCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, uncalledCompilation.Diagnostics));
        var uncalled = NativeModuleCompiler.CreateGraphConsumer(
            uncalledCompilation.Program!, [provider], policy);
        Assert.That(GraphMachineImportCount(uncalled), Is.Zero);
        NativeModuleCompiler.ValidateGraph(uncalled, [provider, leaf]);
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateGraph(uncalled, [changedProvider, leaf]));
    }

    [Test]
    public void GraphProvidersRejectCaseOnlyIdentityDuplicates()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            " public int32 Leaf() { return 40; }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual10 = compilation.Program;
            Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual10!;
        }))();
        var provider = NativeModuleCompiler.CreateProducer(program, ["Leaf"], "NodeB",
            NativeRuntimePolicy.FailFast);
        var caseVariant = NativeModuleCompiler.CreateProducer(program, ["Leaf"], "nodeb",
            NativeRuntimePolicy.FailFast);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Leaf() + 1; }"), NativeModuleCompiler.CreateGraphReference([provider]));
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => {
                var __nunitActual11 = consumerCompilation.Program;
                Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>());
                return (BoundProgram)__nunitActual11!;
            }))(), [provider], NativeRuntimePolicy.FailFast);

        var error = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateGraph(consumer, [provider, caseVariant]))!;

        Assert.That(error.Message, Is.EqualTo("Duplicate, ambiguous or consumer graph dependency."));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task UnchangedRootAndMiddleExecuteCompatibleLeafReplacementAsync(NativeRuntimePolicy policy)
    {
        var original = Leaf(" public int32 Leaf() { return 40; }", policy);
        var middle = Middle("public int32 Middle() { return Leaf() + 1; }", original, policy);
        var root = Consumer(middle, policy);
        var originalRoot = root.ToArray();
        var originalMiddle = middle.ToArray();
        NativeModuleCompiler.ValidateGraph(root, [original, middle]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [middle, original],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(false), Is.EqualTo(42));
        var replacement = Leaf(" public int32 Leaf() { return 41; }", policy);
        NativeModuleCompiler.ValidateGraph(root, [replacement, middle]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [replacement, middle],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(false), Is.EqualTo(43));
        Assert.That(root, Is.EqualTo(originalRoot));
        Assert.That(middle, Is.EqualTo(originalMiddle));
        Assert.That(NativeModuleCompiler.InspectGraphArtifact(root).Dependencies, Is.EqualTo(["NodeA"]));
        Assert.That(NativeModuleCompiler.InspectGraphArtifact(middle).Dependencies, Is.EqualTo(["NodeB"]));
    }

    [Test]
    public void RejectsMissingExtraIncompatiblePolicyAndCyclicGraphs()
    {
        var leaf = Leaf(" public int32 Leaf() { return 40; }");
        var middle = Middle("public int32 Middle() { return Leaf() + 1; }", leaf);
        var root = Consumer(middle);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [middle]))!;
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [leaf, middle, leaf]))!;
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [
            leaf, middle, NativeModuleCompiler.CreateProducer(
                ((global::System.Func<BoundProgram>)(() =>
                {
                    var __nunitActual12 = Compilation.CreateLibrary(
                        SyntaxTree.Parse("public int32 Extra() { return 1; }")).Program;
                    Assert.That(__nunitActual12, Is.TypeOf<BoundProgram>());
                    return (BoundProgram)__nunitActual12!;
                }))(),
                ["Extra"], "Extra", NativeRuntimePolicy.FailFast)]))!;
        NativeModuleCompiler.ValidateGraph(root, [
            Leaf("public int32 Leaf() { return 40; }"), middle]);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [
            Leaf(" public int32 Leaf() { return 40; }", NativeRuntimePolicy.CheckedExceptions), middle]))!;
        var damaged = middle.ToArray();
        damaged[^1] ^= 1;
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root, [leaf, damaged]))!;
        var tamperedImport = middle.ToArray();
        "NodeC"u8.CopyTo(tamperedImport.AsSpan(19 + "NodeA".Length + 2));
        SHA256.HashData(tamperedImport.AsSpan(0, tamperedImport.Length - 32))
            .CopyTo(tamperedImport, tamperedImport.Length - 32);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root,
            [leaf, tamperedImport]))!;
        var badCount = middle.ToArray();
        badCount[8] = 0;
        SHA256.HashData(badCount.AsSpan(0, badCount.Length - 32))
            .CopyTo(badCount, badCount.Length - 32);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root, [leaf, badCount]))!;

        var cycle = middle.ToArray();
        "NodeB"u8.CopyTo(cycle.AsSpan(19));
        SHA256.HashData(cycle.AsSpan(0, cycle.Length - 32)).CopyTo(cycle, cycle.Length - 32);
        var error = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root,
            [middle, cycle]))!;
        Assert.That(error.Message, Does.Contain("cycle").IgnoreCase);
    }

    [Test]
    public async Task OwnedTransferAcrossIntermediatePreservesReplacementContractsAsync()
    {
        const string LeafSource = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public unique struct Token {
                public int32 Value;
                public Token(int32 value) { Value = value; }
                 [Cleanup] public void Close() { }
            }
            public Token New() { return new Token(20); }
             public int32 Take(Token value) { return value.Value; }
            """;
        static byte[] CompileLeaf(string source)
        {
            var compilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(source),
                LayoutOptions(NativeRuntimePolicy.FailFast));
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = ((global::System.Func<BoundProgram>)(() => {
                var __nunitActual13 = compilation.Program;
                Assert.That(__nunitActual13, Is.TypeOf<BoundProgram>());
                return (BoundProgram)__nunitActual13!;
            }))();
            return NativeModuleCompiler.CreateProducer(program,
                ["New", "Take", "Token.Close"], "NodeB", NativeRuntimePolicy.FailFast);
        }
        var leaf = CompileLeaf(LeafSource);
        var middleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Middle() {
                Token token = New();
                return Take(move token) + 22;
            }
            """), NativeModuleCompiler.CreateGraphReference([leaf]));
        Assert.That(middleCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, middleCompilation.Diagnostics));
        var middle = NativeModuleCompiler.CreateGraphProducer(
            ((global::System.Func<BoundProgram>)(() => {
                var __nunitActual14 = middleCompilation.Program;
                Assert.That(__nunitActual14, Is.TypeOf<BoundProgram>());
                return (BoundProgram)__nunitActual14!;
            }))(), ["Middle"], "NodeA",
            [leaf], NativeRuntimePolicy.FailFast);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Middle(); }"),
            NativeModuleCompiler.CreateGraphReference([middle]));
        Assert.That(rootCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual15 = rootCompilation.Program;
            Assert.That(__nunitActual15, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual15!;
        }))(),
            [middle], NativeRuntimePolicy.FailFast);
        Assert.That(NativeModuleCompiler.InspectGraphArtifact(middle).NativeProfileVersion, Is.EqualTo("native-13"));
        var replacement = CompileLeaf(LeafSource.Replace("new Token(20)", "new Token(21)", StringComparison.Ordinal));
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [middle, leaf],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast).ConfigureAwait(false), Is.EqualTo(42));
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [replacement, middle],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast).ConfigureAwait(false), Is.EqualTo(43));
        NativeModuleCompiler.ValidateGraph(root, [CompileLeaf(LeafSource), middle]);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [
            CompileLeaf(LeafSource.Replace("public int32 Value;", "public int32 Other;", StringComparison.Ordinal)
                .Replace("Value = value;", "Other = value;", StringComparison.Ordinal)
                .Replace("value.Value", "value.Other", StringComparison.Ordinal)), middle]))!;
    }
    [TestCase(NativeRuntimePolicy.FailFast,false)]
    [TestCase(NativeRuntimePolicy.FailFast,true)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,false)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,true)]
    public async Task ScalarOutputExecutesAcrossGraphAndRejectsModeReplacementAsync(
        NativeRuntimePolicy policy, bool initialUsesInit)
    {
        var initialSource = initialUsesInit
            ? "public void Leaf(init ref<int32> result) { result = 42; }"
            : "public void Leaf(out int32 result) { result = 42; }";
        var replacementSource = initialUsesInit
            ? "public void Leaf(out int32 result) { result = 43; }"
            : "public void Leaf(init ref<int32> result) { result = 43; }";
        var consumerSource = initialUsesInit
            ? "int32 Main() { init int32 result; Leaf(init result); return result; }"
            : "int32 Main() { Leaf(out var result); return result; }";

        var leaf = Leaf(initialSource, policy);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse(consumerSource),
            NativeModuleCompiler.CreateGraphReference([leaf]), (BoundRuntimePolicy)policy);
        Assert.That(rootCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual16 = rootCompilation.Program;
            Assert.That(__nunitActual16, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual16!;
        }))(),
            [leaf], policy);

        NativeModuleCompiler.ValidateGraph(root, [leaf]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [leaf],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(root, [leaf],
            CompilerApphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(42));

        var replacement = Leaf(replacementSource, policy);
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateGraph(root, [replacement]))!;
    }
    [TestCase(NativeRuntimePolicy.FailFast,"float32","float64")]
    [TestCase(NativeRuntimePolicy.FailFast,"float64","float32")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"float32","float64")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"float64","float32")]
    public async Task FloatingScalarOutputExecutesAcrossGraphAsync(
        NativeRuntimePolicy policy, string outputType, string replacementType)
    {
        var leafSource = $"public bool Leaf(bool publish, out {outputType} result) "
            + "{ if (!publish) { return false; } result = 7.75; return true; }";
        var replacementSource = $"public bool Leaf(bool publish, out {replacementType} result) "
            + "{ if (!publish) { return false; } result = 7.75; return true; }";
        var consumerSource = $"int32 Main() {{ return Leaf(true, out var result) "
            + "&& result == 7.75 ? 42 : 0; }";

        var leaf = Leaf(leafSource, policy);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse(consumerSource),
            NativeModuleCompiler.CreateGraphReference([leaf]), (BoundRuntimePolicy)policy);
        Assert.That(rootCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual17 = rootCompilation.Program;
            Assert.That(__nunitActual17, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual17!;
        }))(),
            [leaf], policy);

        NativeModuleCompiler.ValidateGraph(root, [leaf]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [leaf],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(root, [leaf],
            CompilerApphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(42));

        var replacement = Leaf(replacementSource, policy);
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateGraph(root, [replacement]))!;
    }
    [TestCase(NativeRuntimePolicy.FailFast,"float32","float64")]
    [TestCase(NativeRuntimePolicy.FailFast,"float64","float32")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"float32","float64")]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,"float64","float32")]
    public async Task FloatingInitReferenceExecutesAcrossGraphAsync(
        NativeRuntimePolicy policy, string outputType, string replacementType)
    {
        var leafSource = $"public bool Leaf(bool publish, init ref<{outputType}> result) "
            + "{ if (!publish) { return false; } result = 7.75; return true; }";
        var replacementSource = $"public bool Leaf(bool publish, init ref<{replacementType}> result) "
            + "{ if (!publish) { return false; } result = 7.75; return true; }";
        var consumerSource = $"int32 Main() {{ return Leaf(true, init var result) "
            + "&& result == 7.75 ? 42 : 0; }";

        var leaf = Leaf(leafSource, policy);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse(consumerSource),
            NativeModuleCompiler.CreateGraphReference([leaf]), (BoundRuntimePolicy)policy);
        Assert.That(rootCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual18 = rootCompilation.Program;
            Assert.That(__nunitActual18, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual18!;
        }))(),
            [leaf], policy);

        NativeModuleCompiler.ValidateGraph(root, [leaf]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [leaf],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(root, [leaf],
            CompilerApphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(42));

        var replacement = Leaf(replacementSource, policy);
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateGraph(root, [replacement]))!;
    }
    [TestCase(NativeRuntimePolicy.FailFast,false)]
    [TestCase(NativeRuntimePolicy.FailFast,true)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,false)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,true)]
    public async Task CopyStructOutputExecutesAcrossGraphAsync(
        NativeRuntimePolicy policy, bool usesInit)
    {
        var outputMode = usesInit ? "init ref<Pair>" : "out Pair";
        const string ProviderTemplate = """
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Pair {
                public int64 First;
                public int64 Second;
                public Pair(int64 first, int64 second) { First = first; Second = second; }
            }
            """;
        var providerSource = $"{ProviderTemplate}public void Leaf({outputMode} result) "
            + "{ result = new Pair(20, 22); }";
        var consumerSource = usesInit
            ? """
                int32 Main() {
                    init Pair result;
                    Leaf(init result);
                    return int32(result.First + result.Second);
                }
                """
            : """
            int32 Main() {
                Leaf(out var result);
                return int32(result.First + result.Second);
            }
            """;

        var leaf = Leaf(providerSource, policy);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse(consumerSource),
            NativeModuleCompiler.CreateGraphReference([leaf]), (BoundRuntimePolicy)policy);
        Assert.That(rootCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual19 = rootCompilation.Program;
            Assert.That(__nunitActual19, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual19!;
        }))(),
            [leaf], policy);

        NativeModuleCompiler.ValidateGraph(root, [leaf]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [leaf],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(root, [leaf],
            CompilerApphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(42));

        var replacementSource = $"{ProviderTemplate}public void Leaf({outputMode} result) "
            + "{ result = new Pair(21, 22); }";
        var replacement = Leaf(replacementSource, policy);
        NativeModuleCompiler.ValidateGraph(root, [replacement]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [replacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(43));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(root, [replacement],
            CompilerApphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(43));

        var incompatibleMode = usesInit ? "out Pair" : "init ref<Pair>";
        var incompatible = Leaf($"{ProviderTemplate}public void Leaf({incompatibleMode} result) "
            + "{ result = new Pair(21, 22); }", policy);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [incompatible]))!;
    }

    [Test]
    public async Task CheckedGraphFailureLeavesInitRefOutputVacantForRetryAsync()
    {
        const NativeRuntimePolicy policy = NativeRuntimePolicy.CheckedExceptions;
        const string ProviderSource = """
            public void Fail(init ref<int32> result) { result = 42; require false; }
            public void Complete(init ref<int32> result) { result = 43; }
            """;
        const string ConsumerSource = """
            int32 Main() {
                init int32 result;
                unsafe {
                    try {
                        Fail(init result);
                        return 1;
                    } catch { }
                }
                Complete(init result);
                return result;
            }
            """;

        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(ProviderSource),
            (BoundRuntimePolicy)policy);
        Assert.That(providerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => {
                var __nunitActual20 = providerCompilation.Program;
                Assert.That(__nunitActual20, Is.TypeOf<BoundProgram>());
                return (BoundProgram)__nunitActual20!;
            }))(), ["Fail", "Complete"], "NodeB", policy);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource),
            NativeModuleCompiler.CreateGraphReference([provider]), (BoundRuntimePolicy)policy);
        Assert.That(rootCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual21 = rootCompilation.Program;
            Assert.That(__nunitActual21, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual21!;
        }))(),
            [provider], policy);

        NativeModuleCompiler.ValidateGraph(root, [provider]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [provider],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(43));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(root, [provider],
            CompilerApphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(43));
    }
    [TestCase(NativeRuntimePolicy.FailFast,false)]
    [TestCase(NativeRuntimePolicy.FailFast,true)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,false)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,true)]
    public async Task ConditionalInitRefOutputPublishesAcrossGraphOnlyOnTrueAsync(
        NativeRuntimePolicy policy, bool usesCopyStruct)
    {
        var providerSource = usesCopyStruct
            ? """
                [Blittable] [Layout(LayoutKind.Sequential)] public struct Pair {
                    public int64 First;
                    public int64 Second;
                    public Pair(int64 first, int64 second) { First = first; Second = second; }
                }
                public bool TryPublish(bool publish, init ref<Pair> result) {
                    result = publish ? new Pair(20, 22) : new Pair(20, 21);
                    return publish;
                }
                """
            : """
            public bool TryPublish(bool publish, init ref<int32> result) {
                result = publish ? 42 : 41;
                return publish;
            }
            """;
        var consumerSource = usesCopyStruct
            ? """
                int32 Main() {
                    init Pair result;
                    if (TryPublish(false, init result)) { return 1; }
                    if (!TryPublish(true, init result)) { return 2; }
                    return int32(result.First + result.Second);
                }
                """
            : """
            int32 Main() {
                init int32 result;
                if (TryPublish(false, init result)) { return 1; }
                if (!TryPublish(true, init result)) { return 2; }
                return result;
            }
            """;

        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource), LayoutOptions(policy));
        Assert.That(providerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => {
                var __nunitActual22 = providerCompilation.Program;
                Assert.That(__nunitActual22, Is.TypeOf<BoundProgram>());
                return (BoundProgram)__nunitActual22!;
            }))(), ["TryPublish"], "NodeB", policy);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse(consumerSource),
            NativeModuleCompiler.CreateGraphReference([provider]), (BoundRuntimePolicy)policy);
        Assert.That(rootCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual23 = rootCompilation.Program;
            Assert.That(__nunitActual23, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual23!;
        }))(),
            [provider], policy);

        NativeModuleCompiler.ValidateGraph(root, [provider]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [provider],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(root, [provider],
            CompilerApphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(NativeRuntimePolicy.FailFast,false)]
    [TestCase(NativeRuntimePolicy.FailFast,true)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,false)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,true)]
    public async Task ConditionalOutOutputRemainsVacantOnFalseAcrossGraphAsync(
        NativeRuntimePolicy policy, bool usesCopyStruct)
    {
        var providerSource = usesCopyStruct
            ? """
                [Blittable] [Layout(LayoutKind.Sequential)] public struct Pair {
                    public int64 First;
                    public int64 Second;
                    public Pair(int64 first, int64 second) { First = first; Second = second; }
                }
                public bool TryPublish(bool publish, out Pair result) {
                    result = publish ? new Pair(20, 22) : new Pair(20, 21);
                    return publish;
                }
                public void Complete(init ref<Pair> result) { result = new Pair(30, 13); }
                """
            : """
            public bool TryPublish(bool publish, out int32 result) {
                result = publish ? 42 : 41;
                return publish;
            }
            public void Complete(init ref<int32> result) { result = 43; }
            """;
        var consumerSource = usesCopyStruct
            ? """
                int32 Main() {
                    if (TryPublish(false, out var failed)) { return 1; }
                    Complete(init failed);
                    if (failed.First != 30 || failed.Second != 13) { return 2; }
                    if (!TryPublish(true, out var published)) { return 3; }
                    return int32(published.First + published.Second);
                }
                """
            : """
            int32 Main() {
                if (TryPublish(false, out var failed)) { return 1; }
                Complete(init failed);
                if (failed != 43) { return 2; }
                if (!TryPublish(true, out var published)) { return 3; }
                return published;
            }
            """;

        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource), LayoutOptions(policy));
        Assert.That(providerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => {
                var __nunitActual24 = providerCompilation.Program;
                Assert.That(__nunitActual24, Is.TypeOf<BoundProgram>());
                return (BoundProgram)__nunitActual24!;
            }))(), ["TryPublish", "Complete"],
            "NodeB", policy);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse(consumerSource),
            NativeModuleCompiler.CreateGraphReference([provider]), (BoundRuntimePolicy)policy);
        Assert.That(rootCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual25 = rootCompilation.Program;
            Assert.That(__nunitActual25, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual25!;
        }))(),
            [provider], policy);

        NativeModuleCompiler.ValidateGraph(root, [provider]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [provider],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(root, [provider],
            CompilerApphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public async Task SharedLeafDiamondIsAcyclicAndBindsEveryImportToItsOwnerAsync()
    {
        var leaf = Leaf("public int32 Leaf() { return 40; }");
        var middle = Middle("public int32 Middle() { return Leaf() + 1; }", leaf);
        var topCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Top() { return Middle() + Leaf(); }"),
            NativeModuleCompiler.CreateGraphReference([middle, leaf]));
        Assert.That(topCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, topCompilation.Diagnostics));
        var top = NativeModuleCompiler.CreateGraphProducer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual26 = topCompilation.Program;
            Assert.That(__nunitActual26, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual26!;
        }))(),
            ["Top"], "NodeC", [middle, leaf], NativeRuntimePolicy.FailFast);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Top(); }"),
            NativeModuleCompiler.CreateGraphReference([top]));
        Assert.That(rootCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual27 = rootCompilation.Program;
            Assert.That(__nunitActual27, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual27!;
        }))(),
            [top], NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.ValidateGraph(root, [top, leaf, middle]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [middle, leaf, top],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast).ConfigureAwait(false), Is.EqualTo(81));
        var replacement = Leaf("public int32 Leaf() { return 41; }");
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [replacement, top, middle],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast).ConfigureAwait(false), Is.EqualTo(83));
    }

    [Test]
    public void GraphImportsMustBeDeclaredByTheirOwnDependency()
    {
        var leafCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Bravo() { return 40; }"));
        Assert.That(leafCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, leafCompilation.Diagnostics));
        var leaf = NativeModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual28 = leafCompilation.Program;
            Assert.That(__nunitActual28, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual28!;
        }))(),
            ["Bravo"], "NodeB", NativeRuntimePolicy.FailFast);
        var middleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Alpha() { return Bravo(); }"), NativeModuleCompiler.CreateGraphReference([leaf]));
        Assert.That(middleCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, middleCompilation.Diagnostics));
        var middle = NativeModuleCompiler.CreateGraphProducer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual29 = middleCompilation.Program;
            Assert.That(__nunitActual29, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual29!;
        }))(),
            ["Alpha"], "NodeA", [leaf], NativeRuntimePolicy.FailFast);
        var topCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Top() { return Alpha() + Bravo(); }"),
            NativeModuleCompiler.CreateGraphReference([leaf, middle]));
        Assert.That(topCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, topCompilation.Diagnostics));
        var top = NativeModuleCompiler.CreateGraphProducer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual30 = topCompilation.Program;
            Assert.That(__nunitActual30, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual30!;
        }))(),
            ["Top"], "NodeC", [middle, leaf], NativeRuntimePolicy.FailFast);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Top(); }"),
            NativeModuleCompiler.CreateGraphReference([top]));
        Assert.That(rootCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual31 = rootCompilation.Program;
            Assert.That(__nunitActual31, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual31!;
        }))(),
            [top], NativeRuntimePolicy.FailFast);

        NativeModuleCompiler.ValidateGraph(root, [top, middle, leaf]);
        var crossDependencyImport = ReplaceRecordedGraphImport(top, "NodeA", "Alpha", "Bravo");
        _ = Assert.Throws<ModuleFormatException>(() =>
            NativeModuleCompiler.ValidateGraph(root, [crossDependencyImport, middle, leaf]))!;
    }

    [Test]
    public void PersistedGraphContractsOmitNoThrowAndAllowBodyReplacement()
    {
        var leaf = Leaf("public int32 Leaf() { return 40; }");
        var middle = Middle("public int32 Middle() { return Leaf(); }", leaf);
        var root = Consumer(middle);
        NativeModuleCompiler.ValidateGraph(root, [leaf, middle]);
        Assert.That(Encoding.UTF8.GetString(middle), Does.Not.Contain("|NoThrow"));
        Assert.That(Encoding.UTF8.GetString(middle), Does.Not.Contain("|MayThrow"));
        var replacement = Leaf("public int32 Leaf() { return 41; }");
        NativeModuleCompiler.ValidateGraph(root, [replacement, middle]);
    }

    [Test]
    public void PersistedLibraryCannotWeakenRequiredNoReturnPromise()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[DoesNotReturn] public void Halt() { while (true) { } }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var leaf = NativeModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual33 = compilation.Program;
            Assert.That(__nunitActual33, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual33!;
        }))(),
            ["Halt"], "NodeB", NativeRuntimePolicy.FailFast);
        var middleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Middle() { Halt(); }"), NativeModuleCompiler.CreateGraphReference([leaf]));
        Assert.That(middleCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, middleCompilation.Diagnostics));
        var middle = NativeModuleCompiler.CreateGraphProducer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual34 = middleCompilation.Program;
            Assert.That(__nunitActual34, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual34!;
        }))(),
            ["Middle"], "NodeA", [leaf], NativeRuntimePolicy.FailFast);
        var root = Consumer(middle);
        NativeModuleCompiler.ValidateGraph(root, [leaf, middle]);
        var weakened = WeakenRecordedImport(middle, "Halt", "|DoesNotReturn", "|MayReturn");
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.ValidateGraph(root,
            [leaf, weakened]))!;
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void GraphReplacementCannotWeakenRequiredNoReturnPromise(NativeRuntimePolicy policy)
    {
        var leaf = Leaf("[DoesNotReturn] public void Leaf() { while (true) { } }", policy);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Leaf(); return 42; }"),
            NativeModuleCompiler.CreateGraphReference([leaf]), (BoundRuntimePolicy)policy);
        Assert.That(rootCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual35 = rootCompilation.Program;
            Assert.That(__nunitActual35, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual35!;
        }))(),
            [leaf], policy);

        NativeModuleCompiler.ValidateGraph(root, [leaf]);
        var replacement = Leaf("public void Leaf() { }", policy);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [replacement]))!;
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void GraphReplacementIgnoresRetiredNoThrowDimension(NativeRuntimePolicy policy)
    {
        var leaf = Leaf(" public int32 Leaf() { return 40; }", policy);
        var middle = Middle("public int32 Middle() { return Leaf() + 1; }", leaf, policy);
        var root = Consumer(middle, policy);

        NativeModuleCompiler.ValidateGraph(root, [leaf, middle]);
        var replacement = Leaf("public int32 Leaf() { return 41; }", policy);
        NativeModuleCompiler.ValidateGraph(root, [replacement, middle]);
    }

    [Test]
    public void LibraryMayExportOrdinaryMainWithoutCreatingAnEntryPoint()
    {
        var leaf = Leaf("public int32 Leaf() { return 40; }");
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Main() { return Leaf(); }"),
            NativeModuleCompiler.CreateGraphReference([leaf]));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual36 = compilation.Program;
            Assert.That(__nunitActual36, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual36!;
        }))();
        Assert.That(program.EntryPoint, Is.EqualTo(-1));
        var graph = NativeModuleCompiler.CreateGraphProducer(program, ["Main"], "NodeA",
            [leaf], NativeRuntimePolicy.FailFast);
        Assert.That(NativeModuleCompiler.InspectGraphArtifact(graph).Exports, Is.EqualTo(["Main"]));
    }
}
