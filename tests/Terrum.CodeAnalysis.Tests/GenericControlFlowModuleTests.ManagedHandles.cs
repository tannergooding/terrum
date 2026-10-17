// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class GenericControlFlowModuleTests
{
    private const string ManagedGenericSource = """
        namespace Models;
        mut class Cell {
            public int32 Value;
            public mut write Cell? Next;
            public Cell(int32 value) { Value = value; Next = null; }
            public static int32 Expected() { return 41; }
        }
        class Marker {
            public int32 Value;
            public Marker(int32 value) { Value = value; }
        }
        struct Pair {
            public int64 First;
            public int64 Second;
            public Pair(int64 first, int64 second) { First = first; Second = second; }
        }
        [NoThrow] T Identity<T>(T value) where T : [Copy] { return value; }
        [NoThrow] A Forward<A, B, C>(A first, B second, C third)
            where A : [Copy] where B : [Copy] where C : [Copy] {
            return Identity<A>(first);
        }
        [NoThrow] B Reorder<A, B>(A first, B second) where A : [Copy] where B : [Copy] {
            return Forward<B, A, A>(second, first, first);
        }
        [NoThrow] bool Publish<A, B>(A first, B second, out A left, out B right)
            where A : [Copy] where B : [Copy] {
            left = first;
            right = second;
            return true;
        }
        [NoThrow] bool Absent<A, B>(A value, out B missing)
            where A : [Copy] where B : [Copy] {
            return false;
        }
        T CreateAndRead<T>(T value) where T : [Copy] {
            if (Models.Cell.TryCreate(41, out var fresh)) {
                fresh.Next = fresh;
                ManagedHeap.Collect();
                require fresh.Value == Models.Cell.Expected();
            }
            return value;
        }
        write Cell CollectAndReturn(write Cell value) {
            require ManagedHeap.Collect() >= 0;
            return value;
        }
        public int32 Proof() {
            require Cell.TryCreate(40, out var root);
            root.Next = root;
            {
                require Cell.TryCreate(1, out var dead);
                dead.Next = dead;
            }
            require Marker.TryCreate(2, out var marker);
            var live = Forward<write Models.Cell, write Models.Cell, Marker>(
                Identity<write Cell>(CreateAndRead(root)), CollectAndReturn(root), marker);
            require Publish(live, new Pair(1, 2), out var writable, out var pair);
            Cell read = Identity<Cell>(writable);
            Cell inferredReadonly = Identity(writable);
            Cell? inferredOptional = Identity(writable);
            write Cell? inferredWritableOptional = Identity(writable);
            Cell? optional = Identity<Cell?>(read);
            write Models.Cell? mutableOptional = Identity<write Models.Cell?>(root);
            Cell? inferredReadonlyOptional = Identity<write Cell?>(root);
            write Cell? contextualOptional = Identity(null);
            require inferredReadonly.Value == 40;
            require inferredOptional is some inferred && inferred.Value == 40;
            require inferredWritableOptional is some inferredWritable && inferredWritable.Value == 40;
            require inferredReadonlyOptional is some inferredRead && inferredRead.Value == 40;
            require optional is some present && present.Value == 40;
            require mutableOptional is some editable && editable.Value == 40;
            require contextualOptional is null;
            require Identity<write Cell?>(null) is null;
            require Identity<ManagedNode?>(null) is null;
            require ManagedNode.TryCreate(2, out var node);
            write ManagedNode? optionalNode = Identity<write ManagedNode?>(node);
            require optionalNode is some current && current.Value == 2;
            require !Absent<write Cell, Cell?>(writable, out var vacant);
            mut Cell? known = null;
            require !Absent(root, out known);
            require Reorder<Marker, Cell>(marker, read).Value == 40;
            require Forward<int32, write Cell, Pair>(40, root, pair) == 40;
            require ManagedHeap.Collect() == 0;
            return live.Value + marker.Value;
        }
        int32 Main() { return Proof(); }
        """;
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task QualifiedManagedCopyTuplesPreserveIdentityAndRootsInAotAndJitAsync(
        NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(ManagedGenericSource),
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,static function =>
            function.Name == "Models.Identity<write Models.Cell>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,static function =>
            function.Name == "Models.Identity<Models.Cell>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,static function =>
            function.Name == "Models.Identity<Models.Cell?>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,static function =>
            function.Name == "Models.Identity<write Models.Cell?>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,static function =>
            function.Name == "Models.Identity<ManagedNode?>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,static function =>
            function.Name == "Models.Identity<write ManagedNode?>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,static function =>
            function.Name == "Models.Forward<int32, write Models.Cell, Models.Pair>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,static function =>
            function.Name == "Models.CreateAndRead<write Models.Cell>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,static function => function.IsExternal
            || function.Name.Contains('$', StringComparison.Ordinal)),Is.False);
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy)).ConfigureAwait(true), Is.EqualTo(42));
        var sourcePath = Path.Combine(TestArtifactPaths.DirectoryPath, $"managed-generic-{Guid.NewGuid():N}.terrum");
        try
        {
            await File.WriteAllTextAsync(sourcePath, ManagedGenericSource).ConfigureAwait(true);
            var (exit, error) = await RunCliAsync("jit", sourcePath, "--runtime",
                policy == NativeRuntimePolicy.FailFast ? "fail-fast" : "checked-exceptions").ConfigureAwait(true);
            Assert.That(error, Is.Empty);
            Assert.That(exit, Is.EqualTo(42));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ManagedGenericSpecializationsStayPrivateAcrossProducerReplacementAsync(
        NativeRuntimePolicy policy)
    {
        const string Original = "namespace Models; public int32 Proof() { return 40; }";
        const string Consumer = """
            namespace Models;
            mut class Cell {
                public int32 Value;
                public mut write Cell? Next;
                public Cell(int32 value) { Value = value; Next = null; }
            }
            int32 Main() {
                require Cell.TryCreate(1, out var owner);
                owner.Next = owner;
                int32 result = Proof();
                ManagedHeap.Collect();
                require owner.Value == 1;
                return result;
            }
            """;
        var originalProgram = Compile(Original, policy);
        var original = ModuleCompiler.CreateProducer(originalProgram, ["Models.Proof"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile(Consumer, policy,
            ModuleCompiler.CreateReference(original), library: false), original);
        var hash = SHA256.HashData(consumer);
        var replacementProgram = Compile(ManagedGenericSource, policy);
        var replacement = ModuleCompiler.CreateProducer(replacementProgram, ["Models.Proof"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(replacement.AsSpan(4, 4)), Is.EqualTo(76));
        _ = global::System.Linq.Enumerable.Single(ModuleCompiler.CreateReference(replacement).Functions);
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.That(linked.Classes.Select(static @class => @class.Name), Is.EqualTo(["Models.Cell", "$producer.Models.Cell", "$producer.Models.Marker"]));
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,static function =>
            function.Name == "$producer.Models.Identity<write Models.Cell?>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,static function =>
            function.Name == "$producer.Models.CreateAndRead<write Models.Cell>"),Is.True);
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(ModuleCompiler.Link(consumer, original),
            policy)).ConfigureAwait(true), Is.EqualTo(40));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(consumer), Is.EqualTo(hash));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(originalProgram, ["Models.Proof"], "NodeB", policy);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(replacementProgram,
            ["Models.Proof"], "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(Consumer, policy,
            NativeModuleCompiler.CreateGraphReference([nativeOriginal]), library: false),
            [nativeOriginal], policy);
        var nativeHash = SHA256.HashData(nativeConsumer);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeOriginal],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(40));
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeReplacement],
            Apphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(nativeConsumer), Is.EqualTo(nativeHash));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ManagedGenericSampleRunsAndReplacesUnchangedConsumerAsync(NativeRuntimePolicy policy)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "samples", "native",
            "managed-generic-selection.terrum")).ConfigureAwait(true);
        var compilation = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,static function =>
            function.Name == "RetainReversed<Stamp, write Record>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,static function =>
            function.Name == "RetainReversed<Stamp, Record>"),Is.False);
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy)).ConfigureAwait(true), Is.EqualTo(42));
        var sourcePath = Path.Combine(TestArtifactPaths.DirectoryPath, $"managed-generic-sample-{Guid.NewGuid():N}.terrum");
        try
        {
            await File.WriteAllTextAsync(sourcePath, source).ConfigureAwait(true);
            var (exit, error) = await RunCliAsync("jit", sourcePath, "--runtime",
                policy == NativeRuntimePolicy.FailFast ? "fail-fast" : "checked-exceptions").ConfigureAwait(true);
            Assert.That(error, Is.Empty);
            Assert.That(exit, Is.EqualTo(42));
        }
        finally
        {
            File.Delete(sourcePath);
        }

        var originalProgram = Compile("public int32 ManagedGenericProbe() { return 40; }", policy);
        var original = ModuleCompiler.CreateProducer(originalProgram, ["ManagedGenericProbe"]);
        var replacement = ModuleCompiler.CreateProducer(program, ["ManagedGenericProbe"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile("""
            mut class Record {
                public int32 Value;
                public Record(int32 value) { Value = value; }
            }
            int32 Main() {
                require Record.TryCreate(2, out var record);
                return ManagedGenericProbe() + record.Value - 2;
            }
            """, policy, ModuleCompiler.CreateReference(original), library: false), original);
        var hash = SHA256.HashData(consumer);
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.That(linked.Classes.Select(static item => item.Name), Is.EqualTo(["Record", "$producer.Record"]));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(consumer), Is.EqualTo(hash));

        var nativeOriginal = NativeModuleCompiler.CreateProducer(originalProgram,
            ["ManagedGenericProbe"], "NodeB", policy);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(Compile(source, policy),
            ["ManagedGenericProbe"], "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Compile(
            "int32 Main() { return ManagedGenericProbe(); }", policy,
            NativeModuleCompiler.CreateGraphReference([nativeOriginal]), library: false),
            [nativeOriginal], policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeReplacement],
            Apphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase("return value.Value;")]
    [TestCase("return new T();")]
    [TestCase("if (value is null) { return value; } return value;")]
    [TestCase("if (value is some present) { return present; } return value;")]
    public void CopyOnlyDefinitionCannotBorrowManagedCapabilitiesFromSpecialization(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Cell {
                public int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            T Invalid<T>(T value) where T : [Copy] {
            """ + body + """
            }
            int32 Main() {
                require Cell.TryCreate(42, out var cell);
                return Invalid<Cell>(cell).Value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("write Cell stronger = Identity(read);")]
    [TestCase("write Cell stronger = Identity<write Cell>(read);")]
    [TestCase("Cell required = Identity<Cell>(optional);")]
    [TestCase("var value = Identity<write Frozen>(frozen);")]
    public void ManagedGenericQualifiersDoNotStrengthenTypedArguments(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            mut class Cell {
                public int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            class Frozen {
                public int32 Value;
                public Frozen(int32 value) { Value = value; }
            }
            [NoThrow] T Identity<T>(T value) where T : [Copy] { return value; }
            int32 Main() {
                require Cell.TryCreate(40, out var writable);
                Cell read = writable;
                Cell? optional = read;
                require Frozen.TryCreate(2, out var frozen);
            """ + body + """
                return 42;
            }
            """));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("require Copy(writable, out read);","TRM3007")]
    [TestCase("require Copy<write Cell>(writable, out read);","TRM3011")]
    [TestCase("require Copy<write Cell>(writable, out optional);","TRM3011")]
    public void ManagedGenericOutputsRequireExactQualifiedTypes(string body, string diagnostic)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            mut class Cell {
                public int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            bool Copy<T>(T value, out T result) where T : [Copy] {
                result = value;
                return true;
            }
            int32 Main() {
                require Cell.TryCreate(42, out var writable);
                mut Cell read = writable;
                mut write Cell? optional = null;
            """ + body + """
                return 42;
            }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,item => item.Code == diagnostic),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("T Bad<T>(T value) where T : [Copy] { return value.Value; }")]
    [TestCase("T? Bad<T>(T value) where T : [Copy] { return value; }")]
    [TestCase("T Bad<T>(T value) where T : [Copy] { T? optional = null; return value; }")]
    [TestCase("T Bad<T>(T value) where T : [Copy] { write T stronger = value; return value; }")]
    public void OpaqueGenericFormalCannotBorrowOptionalOrWriteCapabilities(string declaration)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Cell {
                public int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            """ + declaration + """
            int32 Main() {
                require Cell.TryCreate(42, out var cell);
                return Bad<Cell>(cell).Value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("public write Cell Leak() { require Cell.TryCreate(42, out var cell); return Identity(cell); }")]
    [TestCase("public bool Leak(out Cell? result) { result = Identity<Cell?>(null); return true; }")]
    public void ClosedManagedGenericsCannotExportManagedHandles(string declaration)
    {
        var program = Compile("""
            mut class Cell {
                public int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            [NoThrow] T Identity<T>(T value) where T : [Copy] { return value; }
            """ + declaration, NativeRuntimePolicy.FailFast);
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(program, ["Leak"]))!;
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateProducer(program,
            ["Leak"], "NodeB", NativeRuntimePolicy.FailFast))!;
    }

    [Test]
    public void FailedManagedGenericOutputRemainsUnavailable()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            mut class Cell {
                public int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            bool Absent<T>(out T result) where T : [Copy] { return false; }
            int32 Main() {
                if (Absent<write Cell>(out var value)) { return 1; }
                return value.Value;
            }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3016"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public async Task CheckedManagedGenericFailureRetainsCallerRootWithoutPublishingOutputsAsync()
    {
        var source = """
            mut class Cell {
                public int32 Value;
                public mut write Cell? Next;
                public Cell(int32 value) { Value = value; Next = null; }
            }
            bool Fail<T, U>(T first, U second, out T left, out U right)
                where T : [Copy] where U : [Copy] {
                left = first;
                right = second;
                require false;
                return true;
            }
            int32 Main() {
                require Cell.TryCreate(42, out var root);
                root.Next = root;
                unsafe {
                    try {
                        Fail<write Cell, Cell?>(root, root, out var first, out var second);
                        return 99;
                    }
                    catch {
                        require ManagedHeap.Collect() == 0;
                        return root.Value;
                    }
                }
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))(), NativeRuntimePolicy.CheckedExceptions))
            .ConfigureAwait(true), Is.EqualTo(42));
    }
}
