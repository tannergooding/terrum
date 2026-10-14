// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class LifetimeEndTests
{
    [TestCase("int32 value = 1; value = _; return 42;")]
    [TestCase("uint64 value = 18446744073709551615; value = _; return 42;")]
    [TestCase("bool value = true; value = _; return 42;")]
    [TestCase("char16 value = char16(97); value = _; return 42;")]
    [TestCase("init int32 value; value = 1; value = _; value = 42; return value;")]
    [TestCase("init var value; value = uint64(1); value = _; value = uint64(42); return int32(value);")]
    [TestCase("mut int32 value = 1; value = _; value = 42; return value;")]
    [TestCase("mut int32 value = 1; if (Pick()) { value = _; } else { value = _; } value = 42; return value;")]
    [TestCase("mut int32 value = 1; while (Pick()) { value = _; value = 2; } return 42;")]
    [TestCase("mut int32 value = 1; do { value = _; value = 42; } while (Pick()); return value;")]
    [TestCase("mut int32 value = 1; for (mut int32 i = 0; i < 2; i++) { value = _; value = 42; } return value;")]
    [TestCase("mut int32 value = 1; try { value = _; } finally { value = 42; } return value;")]
    [TestCase("init int32 value; value = 1; value = _; require Fill(init value); return value;")]
    [TestCase("mut int32 value = 1; value = _; require Fill(init value); return value;")]
    [TestCase("mut int32 value = 1; if (Pick()) { value = _; return 42; } return value + 41;")]
    public void EndLeavesAFixedBindingAndPreservesExistingInitializationAuthority(string body)
    {
        var program = Bind($$"""
            bool Pick() { return false; }
            bool Fill(init ref<int32> value) { value = 42; return true; }
            int32 Main() { {{body}} }
            """);
        SemanticVerifier.Verify(program);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }
    [TestCase("int32 value = 1; value = _; return value;")]
    [TestCase("int32 value = 1; value = _; value = _; return 42;")]
    [TestCase("int32 value = 1; value = _; value = 42; return 42;")]
    [TestCase("int32 value = 1; value = _; int32 value = 42; return 42;")]
    [TestCase("int32 value = 1; value = _; { int32 value = 42; return value; }")]
    [TestCase("init int32 value; value = _; return 42;")]
    [TestCase("init var value; value = _; value = 42; return 42;")]
    [TestCase("init var value; value = int32(1); value = _; value = int64(42); return 42;")]
    [TestCase("int32 value = 1; if (Pick()) { value = _; } return value;")]
    [TestCase("int32 value = 1; if (Pick()) { value = _; } value = _; return 42;")]
    [TestCase("int32 value = 1; while (Pick()) { value = _; } return 42;")]
    [TestCase("mut int32 value = 1; while (Pick()) { value = _; continue; } return value;")]
    [TestCase("mut int32 value = 1; while (Pick()) { value = _; break; } return value;")]
    [TestCase("int32 value = 1; try { value = _; } finally { int32 copy = value; } return 42;")]
    [TestCase("const int32 value = 1; value = _; return 42;")]
    [TestCase("(int32(1)) = _; return 42;")]
    [TestCase("unknown = _; return 42;")]
    public void EndRequiresDefiniteLivenessAndCannotForgeWriteOrDeclarationAuthority(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            $"bool Pick() {{ return false; }} int32 Main() {{ {body} }}"));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("void End(int32 value) { value = _; }")]
    [TestCase("bool Fill(init ref<int32> value) { value = 42; return true; } void End() { int32 value = 1; value = _; require Fill(init value); }")]
    [TestCase("void End(init ref<int32> value) { value = _; }")]
    [TestCase("struct Pair { public int32 Value; public Pair() { Value = 1; } public void End() { this = _; } }")]
    [TestCase("struct Pair { public int32 Value; public Pair() { Value = 1; } public void End() { Value = _; } }")]
    [TestCase("struct Pair { public int32 Value; public Pair() { Value = 1; } } void End() { mut Pair pair = new Pair(); pair.Value = _; }")]
    [TestCase("void End() { Host.Trace[0] = _; }")]
    [TestCase("enum Number { One } void End() { Number value = Number.One; value = _; }")]
    [TestCase("void End() { int32 value = 1; ref<int32> borrowed = ref value; borrowed = _; }")]
    [TestCase("void End() { int32 value = 1; pin ref<int32> borrowed = ref value; borrowed = _; }")]
    public void EndRejectsAliasesAndDoesNotAdoptUnsupportedTypeFamilies(string declarations)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(declarations + " int32 Main() { return 42; }"),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0], true)]);
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase(false)]
    [TestCase(true)]
    public void InlineInitCanRetryBeforePublicationButCannotReviveAnEndedReadonlyValue(bool endBeforeRetry)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            bool Fill(bool publish, init ref<int32> value) {
                if (!publish) { return false; }
                value = 42;
                return true;
            }
            int32 Main() {
                if (Fill(false, init var value)) { return 1; }
                require Fill(true, init value);
                {{(endBeforeRetry ? "value = _; require Fill(true, init value);" : "")}}
                return value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.EqualTo(endBeforeRetry));
        if (!endBeforeRetry)
        {
            Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(),
                NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        }
    }

    [Test]
    public void NamedMoveCannotGiveAnOrdinaryImmutableOwnerNewInitCallAuthority()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public int32 Value;
                public Owner() { Value = 42; }
                [NoThrow] [Cleanup] public void Close() { }
            }
            bool Fill(init ref<Owner> value) { value = new Owner(); return true; }
            int32 Main() {
                Owner owner = new Owner();
                Owner moved = move owner;
                require Fill(init owner);
                return moved.Value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase(false)]
    [TestCase(true)]
    public void FieldViewsRetainTheContainingOwnersBackingUntilTheirLoanEnds(bool retireView)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            unique struct Owner {
                public mut OwnedInt32Buffer Words;
                public Owner(OwnedInt32Buffer words) { Words = move words; }
            }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                mut Owner owner = new Owner(move raw);
                mut MutableInt32BufferView view = owner.Words.SliceMutable(0, 0);
                {{(retireView ? "view = _;" : "")}}
                owner = _;
                return 42;
            }
            """));
        Assert.That(compilation.HasErrors, Is.EqualTo(!retireView));
        if (retireView)
        {
            Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(),
                NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        }
        else
        {
            Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3023"),Is.True);
        }
    }

    private const string Buffer = """
        bool Pick() { return false; }
        int32 Main() {
            require OwnedInt32Buffer.TryCreate(2, out var raw);
            mut OwnedInt32Buffer owner = move raw;
            require owner.TryAppend(42);
        """;
    [TestCase("ReadonlyInt32BufferView view = owner.Slice(0, 1); view = _; owner = _; return 42;")]
    [TestCase("mut MutableInt32BufferView view = owner.SliceMutable(0, 1); view = _; owner = _; return 42;")]
    [TestCase("ReadonlyInt32BufferView view = owner.Slice(0, 1); if (Pick()) { view = _; } else { view = _; } owner = _; return 42;")]
    [TestCase("ReadonlyInt32BufferView view = owner.Slice(0, 1); try { view = _; } finally { owner = _; } return 42;")]
    [TestCase("ReadonlyInt32BufferView view = owner.Slice(0, 1); view = _; require owner.TryAppend(0); return owner.Read(0);")]
    [TestCase("ReadonlyInt32BufferView first = owner.Slice(0, 1); ReadonlyInt32BufferView second = owner.Slice(0, 1); first = _; int32 result = second.Read(0); second = _; owner = _; return result;")]
    public void EndingAViewRetiresItsLoanWithoutEndingTheBackingValue(string body)
    {
        var program = Bind(Buffer + body + "}");
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }
    [TestCase("ReadonlyInt32BufferView view = owner.Slice(0, 1); owner = _; return 42;")]
    [TestCase("ReadonlyInt32BufferView view = owner.Slice(0, 1); if (Pick()) { view = _; } owner = _; return 42;")]
    [TestCase("ReadonlyInt32BufferView first = owner.Slice(0, 1); ReadonlyInt32BufferView second = owner.Slice(0, 1); first = _; owner = _; return 42;")]
    [TestCase("ReadonlyInt32BufferView view = owner.Slice(0, 1); view = _; return view.Length;")]
    [TestCase("ReadonlyInt32BufferView view = owner.Slice(0, 1); view = _; return view.Read(0);")]
    [TestCase("ReadonlyInt32BufferView view = owner.Slice(0, 1); view = _; view = _; return 42;")]
    public void EarlyEndCannotInvalidateADependentLiveView(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Buffer + body + "}"));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task LifetimeEndRoundTripsAcrossSemanticAndPrivateNativeGraphsAsync(bool checkedFailures)
    {
        var policy = checkedFailures ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        const string Source = """
            unique struct Owner {
                public int32 Value;
                public Owner() { Value = 1; }
                [NoThrow] [Cleanup] public void Close() { }
            }
            public int32 Read() {
                init Owner owner;
                owner = new Owner();
                owner = _;
                owner = new Owner();
                owner = _;
                return 42;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(Source), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
        var producer = ModuleCompiler.CreateProducer(program, ["Read"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)), Is.EqualTo(76));
        var retiredRevision = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(retiredRevision.AsSpan(4, 4), 46);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(retiredRevision))!;
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Read(); }"),
            ModuleCompiler.CreateReference(producer), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = consumerCompilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(NativeJit.Execute(linked, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(linked, policy), Is.Not.Empty);

        var nativeProducer = NativeModuleCompiler.CreateProducer(program, ["Read"], "LifetimeEnd", policy);
        var nativeCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Read(); }"),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.That(nativeCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, nativeCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = nativeCompilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))(), [nativeProducer], policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var apphost = Path.Combine(root, "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeProducer],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public void CurrentSemanticRevisionDoesNotDependOnLifetimeEndNodes()
    {
        var scalar = Compilation.CreateLibrary(SyntaxTree.Parse("""
            int32 Unused() { int32 value = 1; value = _; return 1; }
            public int32 Read() { int32 value = 1; return 42; }
            """));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = scalar.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))(), ["Read"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)), Is.EqualTo(76));
        _ = ModuleCompiler.CreateReference(producer);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ManagedEndRootsRoundTripAcrossPrivateGraphParticipantsAsync(NativeRuntimePolicy policy)
    {
        var producerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Read() {
                require ManagedNode.TryCreate(1, out var node);
                node = _;
                if (ManagedHeap.Collect() != 1) { return 0; }
                return 41;
            }
            """), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.That(producerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = producerCompilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))();
        const string Client = """
            int32 Main() {
                int32 result = Read();
                require ManagedNode.TryCreate(1, out var node);
                node = _;
                return result + ManagedHeap.Collect();
            }
            """;
        var producer = ModuleCompiler.CreateProducer(program, ["Read"]);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(Client),
            ModuleCompiler.CreateReference(producer), (BoundRuntimePolicy)policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = consumerCompilation.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))(), producer);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(consumer.AsSpan(4, 4)), Is.EqualTo(76));
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(42));
        BinaryPrimitives.WriteInt32LittleEndian(consumer.AsSpan(4, 4), 46);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(consumer, producer))!;

        var nativeProducer = NativeModuleCompiler.CreateProducer(program, ["Read"], "ManagedEnd", policy);
        var nativeCompilation = Compilation.Create(SyntaxTree.Parse(Client),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), (BoundRuntimePolicy)policy);
        Assert.That(nativeCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, nativeCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = nativeCompilation.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))(), [nativeProducer], policy);
        NativeModuleCompiler.ValidateGraph(nativeConsumer, [nativeProducer]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public void VerifierRejectsParameterEndAndImmutableInitCallAfterEnd()
    {
        var program = Bind("""
            bool Fill(init ref<int32> result) { result = 42; return true; }
            int32 End(int32 parameter) { int32 local = 1; local = _; return 42; }
            int32 Main() { return End(1); }
            """);
        var functionIndex = program.Functions.IndexOf(global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "End"));
        var function = program.Functions[functionIndex];
        var end = ((global::System.Func<BoundLifetimeEnd>)(() => { var __nunitActual9 = function.Body.Statements[1]; Assert.That(__nunitActual9, Is.TypeOf<BoundLifetimeEnd>()); return (BoundLifetimeEnd)__nunitActual9!; }))();
        var fill = program.Functions.IndexOf(global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "Fill"));
        var initialized = new BoundCall(fill,
            [new BoundOutArgument(end.Variable, false, PrimitiveType.Int32, end.Span) { IsInit = true }],
            PrimitiveType.Bool, end.Span);
        foreach (var statements in new[] {
            function.Body.Statements.SetItem(1, end with { Variable = 0 }),
            function.Body.Statements.Insert(2, new BoundRequire(initialized, end.Span)),
        })
        {
            var forged = program with {
                Functions = program.Functions.SetItem(functionIndex,
                    function with { Body = function.Body with { Statements = statements } }),
            };
            _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged))!;
        }
    }

    [Test]
    public void VerifierRejectsForgedTargetsLivenessCleanupAndReinitializationAuthority()
    {
        var program = Bind("int32 Main() { int32 value = 1; value = _; return 42; }");
        var function = program.Functions[program.EntryPoint];
        var end = ((global::System.Func<BoundLifetimeEnd>)(() => { var __nunitActual10 = function.Body.Statements[1]; Assert.That(__nunitActual10, Is.TypeOf<BoundLifetimeEnd>()); return (BoundLifetimeEnd)__nunitActual10!; }))();
        foreach (var forged in new BoundStatement[] {
            end with { Variable = -1 },
            end with { Variable = function.Variables.Length },
            end with { CleanupFunction = program.EntryPoint },
            new BoundAssignment(end.Variable, new BoundConstant(42, PrimitiveType.Int32, end.Span), end.Span),
        })
        {
            var statements = forged is BoundAssignment
                ? function.Body.Statements.Insert(2, forged)
                : function.Body.Statements.SetItem(1, forged);
            var invalid = program with {
                Functions = program.Functions.SetItem(program.EntryPoint,
                    function with { Body = function.Body with { Statements = statements } }),
            };
            _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(invalid))!;
            _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(invalid, NativeRuntimePolicy.FailFast))!;
        }
        var repeated = program with {
            Functions = program.Functions.SetItem(program.EntryPoint,
                function with { Body = function.Body with { Statements = function.Body.Statements.Insert(2, end) } }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(repeated))!;
    }

    [Test]
    public void ProducerReaderRejectsForgedLifetimeEndStorage()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Read() { int32 value = 1; value = _; return 42; }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual11 = compilation.Program; Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual11!; }))();
        var function = global::System.Linq.Enumerable.Single(program.Functions);
        var end = ((global::System.Func<BoundLifetimeEnd>)(() => { var __nunitActual12 = function.Body.Statements[1]; Assert.That(__nunitActual12, Is.TypeOf<BoundLifetimeEnd>()); return (BoundLifetimeEnd)__nunitActual12!; }))();
        var producer = ModuleCompiler.CreateProducer(program, ["Read"]);
        var marker = new byte[14];
        marker[0] = 26;
        BinaryPrimitives.WriteInt32LittleEndian(marker.AsSpan(1, 4), end.Span.Start);
        BinaryPrimitives.WriteInt32LittleEndian(marker.AsSpan(5, 4), end.Span.Length);
        BinaryPrimitives.WriteInt32LittleEndian(marker.AsSpan(9, 4), end.Variable);
        var offset = Enumerable.Range(0, producer.Length - marker.Length + 1)
            .Single(index => producer.AsSpan(index, marker.Length).SequenceEqual(marker));
        BinaryPrimitives.WriteInt32LittleEndian(producer.AsSpan(offset + 9, 4), -1);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(producer))!;
    }

    [Test]
    public void VerifierCannotEraseDeclaredUniqueCleanupFromAnEnd()
    {
        var program = Bind("""
            unique struct Owner {
                public int32 Id;
                public Owner() { Id = 1; }
                [NoThrow] [Cleanup] public void Close() { }
            }
            int32 Main() { Owner owner = new Owner(); owner = _; return 42; }
            """);
        var function = program.Functions[program.EntryPoint];
        var cleanup = ((global::System.Func<BoundTryFinally>)(() => { var __nunitActual13 = function.Body.Statements[1]; Assert.That(__nunitActual13, Is.TypeOf<BoundTryFinally>()); return (BoundTryFinally)__nunitActual13!; }))();
        var end = ((global::System.Func<BoundLifetimeEnd>)(() => { var __nunitActual14 = cleanup.Body.Statements[0]; Assert.That(__nunitActual14, Is.TypeOf<BoundLifetimeEnd>()); return (BoundLifetimeEnd)__nunitActual14!; }))();
        Assert.That(end.CleanupFunction >= 0, Is.True);
        var forged = program with {
            Functions = program.Functions.SetItem(program.EntryPoint,
                function with {
                    Body = function.Body with {
                        Statements = function.Body.Statements.SetItem(1, cleanup with {
                            Body = cleanup.Body with {
                                Statements = cleanup.Body.Statements.SetItem(0, end with { CleanupFunction = -1 }),
                            },
                        }),
                    },
                }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged))!;
        _ = Assert.Throws<ArgumentException>(() => NativeJit.Execute(forged, NativeRuntimePolicy.FailFast))!;
    }

    [Test]
    public void SourceFreeTrRmDoesNotSilentlyEraseLifetimeEnding()
    {
        var program = Bind("int32 Main() { int32 value = 1; value = _; return 42; }");
        _ = Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(program, "LifetimeEnd"))!;
    }

    private static BoundProgram Bind(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual15 = compilation.Program; Assert.That(__nunitActual15, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual15!; }))();
    }
}
