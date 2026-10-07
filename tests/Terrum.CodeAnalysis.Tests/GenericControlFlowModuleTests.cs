// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class GenericControlFlowModuleTests
{
    private const string Stub = "public int32 PowerProbe() { return 40; }";
    private const string Middle = "public int32 Middle() { return PowerProbe() + 1; }";
    private const string Root = "int32 Main() { Middle(); return Middle() + 1; }";
    private const string ConcreteHelperReplacement = """
        struct Value {
            public int32 Number;
            public Value(int32 number) { Number = number; }
        }
        T Forward<T>(T value) where T : [Copy] {
            return Second<Value, T>(new Value(1), value);
        }
        U Second<A, U>(A first, U second)
            where A : [Copy] where U : [Copy] { return second; }
        public int32 Read() { return Forward(int32(42)); }
        """;
    private const string GenericInitLibrary = """
        struct Pair {
            public int64 First;
            public int64 Second;
            public Pair(int64 first, int64 second) { First = first; Second = second; }
        }
        [NoThrow] bool Copy<T>(bool publish, T value, init ref<T> result) where T : [Copy] {
            if (!publish) { return false; }
            result = value;
            return true;
        }
        [NoThrow] bool Forward<T>(T value, init ref<T> result) where T : [Copy] {
            return Copy(true, value, init result);
        }
        [NoThrow] bool None<T>(init ref<T> result) where T : [Copy] { return false; }
        public int32 Read() {
            if (Copy(false, new Pair(1, 2), init var pair)) { return 1; }
            if (None(init pair)) { return 3; }
            require Forward(new Pair(40, 1), init pair);
            require Copy(true, int32(2), init var scalar);
            if (pair.First != 40 || pair.Second != 1) { return 2; }
            return int32(pair.First) + scalar;
        }
        """;

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task GenericInitOutputsStayPrivateAcrossIndependentConsumersAsync(NativeRuntimePolicy policy)
    {
        var producer = ModuleCompiler.CreateProducer(Compile(GenericInitLibrary, policy), ["Read"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile("int32 Main() { return Read(); }",
            policy, ModuleCompiler.CreateReference(producer), library: false), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Contains(linked.Functions, function => function.Name == "$producer.Forward<Pair>"
            && function.Variables[1].IsInit);
        Assert.Contains(linked.Functions, function => function.Name == "$producer.Copy<int32>"
            && function.Variables[2].IsInit);
        Assert.Contains(linked.Functions, function => function.Name == "$producer.None<Pair>"
            && function.Variables[0].IsInit);
        Assert.Equal(42, NativeJit.Execute(linked, policy));
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));

        var nativeProducer = NativeModuleCompiler.CreateProducer(Compile(GenericInitLibrary, policy), ["Read"],
            "Leaf", policy);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(Compile("int32 Main() { return Read(); }",
            policy, NativeModuleCompiler.CreateReference(nativeProducer), library: false), nativeProducer, policy);
        Assert.Equal(42, await NativeModuleCompiler.RunAsync(nativeConsumer, nativeProducer,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true));
    }

    [Fact]
    public async Task GenericInitDoesNotPublishAfterFailureAcrossIndependentModuleReplacementAsync()
    {
        const string ReplacementSource = """
            bool FailAfterWrite<T>(T value, init ref<T> result) where T : [Copy] {
                result = value;
                require false;
                return true;
            }
            [NoThrow] bool Fill<T>(T value, init ref<T> result) where T : [Copy] {
                result = value;
                return true;
            }
            [NoThrow] bool None<T>(init ref<T> result) where T : [Copy] { return false; }
            public int32 PowerProbe() {
                if (None<int32>(init var target)) { return 1; }
                unsafe {
                    try {
                        FailAfterWrite<int32>(41, init target);
                        return 2;
                    } catch { }
                }
                require Fill(42, init target);
                return target;
            }
            """;
        const NativeRuntimePolicy policy = NativeRuntimePolicy.CheckedExceptions;
        var stub = ModuleCompiler.CreateProducer(Compile(Stub, policy), ["PowerProbe"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile("int32 Main() { return PowerProbe(); }",
            policy, ModuleCompiler.CreateReference(stub), library: false), stub);
        var consumerHash = SHA256.HashData(consumer);
        var replacement = ModuleCompiler.CreateProducer(Compile(ReplacementSource, policy), ["PowerProbe"]);
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.Contains(linked.Functions, static function =>
            function.Name == "$producer.FailAfterWrite<int32>" && function.Variables[1].IsInit);
        Assert.Contains(linked.Functions, static function =>
            function.Name == "$producer.Fill<int32>" && function.Variables[1].IsInit);
        Assert.Equal(42, NativeJit.Execute(linked, policy));
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));
        Assert.Equal(consumerHash, SHA256.HashData(consumer));

        var nativeStub = NativeModuleCompiler.CreateProducer(Compile(Stub, policy),
            ["PowerProbe"], "Leaf", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(
            "int32 Main() { return PowerProbe(); }", policy,
            NativeModuleCompiler.CreateGraphReference([nativeStub]), library: false),
            [nativeStub], policy);
        var nativeConsumerHash = SHA256.HashData(nativeConsumer);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(Compile(ReplacementSource, policy),
            ["PowerProbe"], "Leaf", policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(42, await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeReplacement],
            Apphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(nativeConsumerHash, SHA256.HashData(nativeConsumer));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ConcreteHelperArgumentsStayPrivateAcrossCompatibleProducerReplacementAsync(
        NativeRuntimePolicy policy)
    {
        const string Original = "public int32 Read() { return 40; }";
        const string Source = """
            struct Value {
                public int32 Number;
                public Value(int32 number) { Number = number; }
            }
            int32 Main() { return Read(); }
            """;
        var stub = ModuleCompiler.CreateProducer(Compile(Original, policy), ["Read"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile(Source, policy,
            ModuleCompiler.CreateReference(stub), library: false), stub);
        var replacement = ModuleCompiler.CreateProducer(Compile(ConcreteHelperReplacement, policy), ["Read"]);
        Assert.Equal("Read", Assert.Single(ModuleCompiler.CreateReference(replacement).Functions).Name);
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.Contains(linked.Structures, structure => structure.Name == "$producer.Value");
        Assert.Contains(linked.Functions, function => function.Name == "$producer.Second<Value, int32>");
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));
        Assert.Equal(42, NativeJit.Execute(linked, policy));

        var nativeStub = NativeModuleCompiler.CreateProducer(Compile(Original, policy), ["Read"], "Leaf", policy);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(Compile(Source, policy,
            NativeModuleCompiler.CreateReference(nativeStub), library: false), nativeStub, policy);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(Compile(ConcreteHelperReplacement, policy),
            ["Read"], "Leaf", policy);
        Assert.Equal(40, await NativeModuleCompiler.RunAsync(nativeConsumer, nativeStub,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true));
        Assert.Equal(42, await NativeModuleCompiler.RunAsync(nativeConsumer, nativeReplacement,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task GenericProofUsesImportedConcreteCopyDeclarationsAsync(NativeRuntimePolicy policy)
    {
        var producer = ModuleCompiler.CreateProducer(Compile("""
            public struct Box {
                public int32 Number;
                public Box(int32 number) { Number = number; }
            }
            """, policy), ["Box.#ctor"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile("""
            T Forward<T>(T value) where T : [Copy] {
                return Second<Box, T>(new Box(1), value);
            }
            U Second<A, U>(A first, U second)
                where A : [Copy] where U : [Copy] { return second; }
            int32 Main() { return Forward(int32(42)); }
            """, policy, ModuleCompiler.CreateReference(producer), library: false), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));
        Assert.Equal(42, NativeJit.Execute(linked, policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task GenericProofInfersImportedFactoryReturnWithoutTypeSyntaxAsync(NativeRuntimePolicy policy)
    {
        var producer = ModuleCompiler.CreateProducer(Compile("""
            public struct Box {
                public int32 Number;
                public Box(int32 number) { Number = number; }
                public static Box Create() { return new Box(1); }
            }
            """, policy), ["Box.Create"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile("""
            T Forward<T>(T value) where T : [Copy] {
                return Second(Box.Create(), value);
            }
            U Second<A, U>(A first, U second)
                where A : [Copy] where U : [Copy] { return second; }
            int32 Main() { return Forward(int32(42)); }
            """, policy, ModuleCompiler.CreateReference(producer), library: false), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));
        Assert.Equal(42, NativeJit.Execute(linked, policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ClosedPowerSpecializationsRemainPrivateAfterSemanticReplacementAsync(
        NativeRuntimePolicy policy)
    {
        var stub = ModuleCompiler.CreateProducer(Compile(Stub, policy), ["PowerProbe"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile("""
            struct Int32Value {
                public int64 Unrelated;
                public Int32Value(int64 value) { Unrelated = value; }
            }
            struct Int64Value {
                public int32 Unrelated;
                public Int64Value(int32 value) { Unrelated = value; }
            }
            int32 Main() {
                Int32Value narrow = new Int32Value(1);
                Int64Value wide = new Int64Value(2);
                return PowerProbe() + int32(narrow.Unrelated) + wide.Unrelated - 3;
            }
            """, policy, ModuleCompiler.CreateReference(stub), library: false), stub);
        var consumerHash = SHA256.HashData(consumer);
        Assert.Equal(67,BinaryPrimitives.ReadInt32LittleEndian(consumer.AsSpan(4, 4)));

        var replacement = ModuleCompiler.CreateProducer(Compile(await PowerSourceAsync().ConfigureAwait(true), policy),
            ["PowerProbe"]);
        Assert.Equal(67,BinaryPrimitives.ReadInt32LittleEndian(replacement.AsSpan(4, 4)));
        Assert.Equal(policy == NativeRuntimePolicy.FailFast ? "native-10" : "native-11",
            ModuleCompiler.GetNativeProfileVersion(replacement, false));
        var reference = ModuleCompiler.CreateReference(replacement);
        Assert.Empty(reference.Structures);
        Assert.Equal("PowerProbe", Assert.Single(reference.Functions).Name);

        var original = ModuleCompiler.Link(consumer, stub);
        Assert.Equal(40, await ExecuteAsync(NativeEmitter.Emit(original, policy)).ConfigureAwait(true));
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.Equal(["Int32Value", "Int64Value", "$producer.Int32Value", "$producer.Int64Value"],
            linked.Structures.Select(static item => item.Name));
        Assert.Equal(2, linked.Functions.Count(static function =>
            function.Name.StartsWith("$producer.Power<", StringComparison.Ordinal)));
        Assert.Contains(linked.Functions, static function => function.Name == "$producer.Power<Int32Value>");
        Assert.Contains(linked.Functions, static function => function.Name == "$producer.Power<Int64Value>");
        Assert.Equal(2, linked.Functions.Count(static function =>
            function.Name.StartsWith("$producer.Square<", StringComparison.Ordinal)));
        Assert.Contains(linked.Functions, static function => function.Name == "$producer.Square<Int32Value>");
        Assert.Contains(linked.Functions, static function => function.Name == "$producer.Square<Int64Value>");
        Assert.Contains(linked.Functions, static function => function.Name == "$producer.Int32Value.op_Multiply");
        Assert.Contains(linked.Functions, static function => function.Name == "$producer.Int64Value.op_Multiply");
        Assert.DoesNotContain(linked.Functions, static function => function.Name == "$producer.Power");
        Assert.DoesNotContain(reference.Functions, static function =>
            function.Name.Contains("Power<", StringComparison.Ordinal));
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));
        var consumerPath = Path.Combine(AppContext.BaseDirectory, $"power-{Guid.NewGuid():N}.consumer.trm");
        var producerPath = Path.ChangeExtension(consumerPath, ".producer.trm");
        try
        {
            await File.WriteAllBytesAsync(consumerPath, consumer).ConfigureAwait(true);
            await File.WriteAllBytesAsync(producerPath, stub).ConfigureAwait(true);
            var (originalExit, originalError) = await RunCliAsync("run-module", consumerPath, producerPath,
                "--runtime", policy == NativeRuntimePolicy.FailFast ? "fail-fast" : "checked-exceptions")
                .ConfigureAwait(true);
            Assert.Empty(originalError);
            Assert.Equal(40, originalExit);
            await File.WriteAllBytesAsync(producerPath, replacement).ConfigureAwait(true);
            var (exit, error) = await RunCliAsync("run-module", consumerPath, producerPath, "--runtime",
                policy == NativeRuntimePolicy.FailFast ? "fail-fast" : "checked-exceptions").ConfigureAwait(true);
            Assert.Empty(error);
            Assert.Equal(42, exit);
            Assert.Equal(consumer, await File.ReadAllBytesAsync(consumerPath).ConfigureAwait(true));
        }
        finally
        {
            File.Delete(consumerPath);
            File.Delete(producerPath);
        }
        Assert.Equal(consumerHash, SHA256.HashData(consumer));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ClosedPowerSpecializationsReplaceUnchangedNativeGraphAsync(NativeRuntimePolicy policy)
    {
        var original = NativeModuleCompiler.CreateProducer(Compile(Stub, policy), ["PowerProbe"],
            "NodeB", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile(Middle, policy,
            NativeModuleCompiler.CreateGraphReference([original])), ["Middle"], "NodeA", [original], policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile(Root, policy,
            NativeModuleCompiler.CreateGraphReference([middle]), library: false), [middle], policy);
        var rootHash = SHA256.HashData(root);
        var middleHash = SHA256.HashData(middle);

        var replacement = NativeModuleCompiler.CreateProducer(
            Compile(await PowerSourceAsync().ConfigureAwait(true), policy),
            ["PowerProbe"], "NodeB", policy);
        Assert.Equal(policy == NativeRuntimePolicy.FailFast ? "native-10" : "native-11",
            NativeModuleCompiler.Inspect(replacement).NativeProfileVersion);
        Assert.Equal("PowerProbe", Assert.Single(NativeModuleCompiler.CreateReference(replacement).Functions).Name);
        NativeModuleCompiler.ValidateGraph(root, [middle, original]);
        NativeModuleCompiler.ValidateGraph(root, [replacement, middle]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [original, middle],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(44, await NativeModuleCompiler.RunGraphAsync(root, [middle, replacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(44, await NativeModuleCompiler.RunGraphJitAsync(root, [replacement, middle],
            Apphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(rootHash, SHA256.HashData(root));
        Assert.Equal(middleHash, SHA256.HashData(middle));
    }

    private static async Task<string> PowerSourceAsync() =>
        await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "samples", "native",
            "static-protocol-power.terrum")).ConfigureAwait(true);

    private static BoundProgram Compile(string source, NativeRuntimePolicy policy,
        CompilationReference? reference = null, bool library = true)
    {
        var syntax = SyntaxTree.Parse(source);
        var boundPolicy = (BoundRuntimePolicy)policy;
        var compilation = library
            ? reference is null ? Compilation.CreateLibrary(syntax, boundPolicy)
                : Compilation.CreateLibrary(syntax, reference, boundPolicy)
            : Compilation.Create(syntax, Assert.IsType<CompilationReference>(reference), boundPolicy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    private static async Task<int> ExecuteAsync(byte[] image)
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"generic-module-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true })
                ?? throw new InvalidOperationException("Failed to start linked native program.");
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
            File.Delete(path);
        }
    }

    private static async Task<(int ExitCode, string Error)> RunCliAsync(params string[] arguments)
    {
        var cli = Apphost();
        Assert.True(File.Exists(cli), $"CLI was not built: {cli}");
        var start = new ProcessStartInfo(cli) {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start module JIT worker.");
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEndAsync();
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
        _ = await stdout.ConfigureAwait(true);
        return (process.ExitCode, await stderr.ConfigureAwait(true));
    }

    private static string Apphost()
    {
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        return Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, frameworkDirectory.Name, "terrumc.exe");
    }

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", ".."));
}
