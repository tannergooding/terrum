// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeReadonlyBufferViewTests
{
    private const string Buffer = """
        int32 Main() {
            require OwnedInt32Buffer.TryCreate(3, out var raw);
            mut OwnedInt32Buffer buffer = move raw;
            require buffer.TryAppend(40);
            require buffer.TryAppend(41);
            require buffer.TryAppend(42);
        """;

    private const string ForeignViewIndex = """
        bool Replace(out OwnedInt32Buffer value) { return OwnedInt32Buffer.TryCreate(1, out value); }
        int32 Observe(bool value) { return 0; }
        int32 Main() {
            require OwnedInt32Buffer.TryCreate(1, out var rawA);
            mut OwnedInt32Buffer ownerA = move rawA;
            require ownerA.TryAppend(41);
            require OwnedInt32Buffer.TryCreate(1, out var rawB);
            mut OwnedInt32Buffer ownerB = move rawB;
            require ownerB.TryAppend(0);
            ReadonlyInt32BufferView viewB = ownerB.Slice(0, 1);
            return ownerA.Read(viewB.Read(Observe(Replace(out ownerA))));
        }
        """;

    [Fact]
    public void ForeignViewIndexCannotReplaceOriginalReadonlyReceiver()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(ForeignViewIndex));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void PublicIrRejectsOriginalReceiverReplacementInsideForeignViewIndex()
    {
        var safeSource = ForeignViewIndex.Replace("Observe(Replace(out ownerA))", "Observe(true)",
            StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(safeSource));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var entry = program.Functions[program.EntryPoint];
        var owner = Assert.Single(entry.Variables, static variable => variable.Name == "ownerA");
        var ownerSlot = entry.Variables.IndexOf(owner);
        var replacement = program.Functions.IndexOf(Assert.Single(program.Functions,
            static function => function.Name == "Replace"));
        var changed = false;

        BoundStatement Rewrite(BoundStatement statement)
        {
            return statement switch {
                BoundBlock block => block with { Statements = [.. block.Statements.Select(Rewrite)] },
                BoundTryFinally region => region with { Body = (BoundBlock)Rewrite(region.Body) },
                BoundViewScope scope => scope with { Body = (BoundBlock)Rewrite(scope.Body) },
                BoundReturn { Value: BoundCall call } result => ReplaceIndex(result, call),
                _ => statement,
            };
        }

        BoundStatement ReplaceIndex(BoundReturn result, BoundCall call)
        {
            var view = Assert.IsType<BoundViewRead>(call.Arguments[1]);
            var observe = Assert.IsType<BoundCall>(view.Index);
            var output = new BoundOutArgument(ownerSlot, false, PrimitiveType.OwnedInt32Buffer, observe.Span);
            var invalidating = new BoundCall(replacement, [output], PrimitiveType.Bool, observe.Span);
            changed = true;
            return result with {
                Value = call with {
                    Arguments = call.Arguments.SetItem(1,
                        view with { Index = observe with { Arguments = [invalidating] } }),
                },
            };
        }

        var forged = program with {
            Functions = program.Functions.SetItem(program.EntryPoint,
                entry with { Body = (BoundBlock)Rewrite(entry.Body) }),
        };
        Assert.True(changed);
        var error = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
        Assert.Equal("Ownership", error.Data["Terrum.InvalidEffect"]);
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task RangeViewSurvivesSemanticModuleRoundTripAsync(BoundRuntimePolicy policy)
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Read(OwnedInt32Buffer owner) {
                ReadonlyInt32BufferView view = owner[1..^0];
                return view.Read(0);
            }
            """), policy);
        Assert.False(provider.HasErrors, string.Join(Environment.NewLine, provider.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(provider.Program), ["Read"]);
        var client = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(2, out var raw);
                mut OwnedInt32Buffer value = move raw;
                require value.TryAppend(20);
                require value.TryAppend(22);
                return Read(move value);
            }
            """), ModuleCompiler.CreateReference(producer), runtimePolicy: policy);
        Assert.False(client.HasErrors, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(client.Program), producer);
        Assert.Equal(22, await RunAsync(ModuleCompiler.Link(consumer, producer), policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NestedReadonlySlicesRetainRangeThroughBranchAndLoopAsync(BoundRuntimePolicy policy)
    {
        const string Source = Buffer + """
            ReadonlyInt32BufferView tail = buffer.Slice(1, 2);
            ReadonlyInt32BufferView entire = buffer.Slice(0, buffer.Count);
            tail.Read(0);
            try { if (entire.Length == 0) { return 0; } }
            finally { int32 preservedLength = tail.Length; }
            mut int32 sum = 0;
            for (mut int32 i = 0; i < tail.Length; i = i + 1) {
                if (entire.Read(i) != 40 + i) { return 0; }
                sum = sum + tail.Read(i);
            }
            if (tail.Length == 2 && entire.Length == 3 && buffer.Read(0) == 40) {
                return sum - 41;
            }
            return 0;
        }
        """;
        Assert.Equal(42, await RunAsync(Source, policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task RangeSlicesCreateReadonlyViewsForSupportedBoundsAsync(BoundRuntimePolicy policy)
    {
        const string Source = Buffer + """
            var all = buffer[..];
            ReadonlyInt32BufferView prefix = buffer[..2];
            var middle = buffer[1..2];
            ReadonlyInt32BufferView suffix = buffer[1..];
            ReadonlyInt32BufferView tail = buffer[1..^0];
            ReadonlyInt32BufferView beforeLast = buffer[..^1];
            ReadonlyInt32BufferView separated = buffer[0.. ^1];
            return all.Read(0) + middle.Read(0) + suffix.Read(1) - 81;
        }
        """;
        Assert.Equal(42, await RunAsync(Source, policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SliceBoundsEvaluateOnceInSourceOrderAsync(BoundRuntimePolicy policy)
    {
        const string Source = """
            int32 Start() { Host.Trace[0] = Host.Trace[0] + 1; return 0; }
            int32 Length() { Host.Trace[1] = Host.Trace[0] + Host.Trace[1]; return 1; }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                mut OwnedInt32Buffer buffer = move raw;
                require buffer.TryAppend(40);
                ReadonlyInt32BufferView view = buffer.Slice(Start(), Length());
                return view.Read(0) + Host.Trace[0] + Host.Trace[1];
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0, 0], true)], policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(42, await RunAsync(Assert.IsType<BoundProgram>(compilation.Program), policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ViewAllowsMutationOfDistinctOwnerAsync(BoundRuntimePolicy policy)
    {
        const string Source = """
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                mut OwnedInt32Buffer owner = move raw;
                require owner.TryAppend(40);
                require OwnedInt32Buffer.TryCreate(1, out var otherRaw);
                mut OwnedInt32Buffer other = move otherRaw;
                require other.TryAppend(1);
                ReadonlyInt32BufferView view = owner.Slice(0, 1);
                other.Set(0, 2);
                return view.Read(0) + other.Read(0);
            }
            """;
        Assert.Equal(42, await RunAsync(Source, policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task OwningFieldViewAndEndedScopePreserveOwnerIdentityAsync(BoundRuntimePolicy policy)
    {
        const string Source = """
            unique struct Index {
                private OwnedInt32Buffer _words;
                public Index(OwnedInt32Buffer words) { _words = move words; }
                [NoThrow] [Cleanup] public void Close() {}
                public int32 Find() {
                    ReadonlyInt32BufferView starts = _words.Slice(0, _words.Count);
                    return starts.Read(0) + starts.Read(1);
                }
            }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(2, out var raw);
                mut OwnedInt32Buffer words = move raw;
                require words.TryAppend(20);
                require words.TryAppend(22);
                Index index = new Index(move words);
                return index.Find();
            }
            """;
        Assert.Equal(42, await RunAsync(Source, policy).ConfigureAwait(true));

        const string Ended = Buffer + """
            {
                ReadonlyInt32BufferView first = buffer.Slice(0, 1);
                if (first.Read(0) != 40) { return 0; }
            }
            OwnedInt32Buffer transferred = move buffer;
            return transferred.Read(2);
        }
        """;
        Assert.Equal(42, await RunAsync(Ended, policy).ConfigureAwait(true));
    }

    [Fact]
    public void ReadonlyReceiverFieldViewRetainsPlaceAndRejectsForgedRootReplacement()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Index {
                private OwnedInt32Buffer _starts;
                public Index(OwnedInt32Buffer starts) { _starts = move starts; }
                [NoThrow] [Cleanup] public void Close() {}
                public int32 Find() {
                    ReadonlyInt32BufferView starts = _starts[..];
                    return starts.Read(0);
                }
            }
            int32 Main() { return 42; }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var function = Assert.Single(program.Functions,
            static item => item.Name.EndsWith(".Find", StringComparison.Ordinal));
        var scope = Assert.IsType<BoundViewScope>(Assert.Single(function.Body.Statements));
        var loan = Assert.IsType<BoundFieldLoan>(scope.Owner);
        var field = Assert.IsType<BoundFieldRead>(loan.Place);
        Assert.Equal(0, field.Field);
        Assert.Equal(0, Assert.IsType<BoundRead>(field.Receiver).Variable);
        var replacement = new BoundAssignment(0,
            new BoundRead(0, function.Variables[0].Type, scope.Span), scope.Span);
        var forgedScope = scope with {
            Body = scope.Body with { Statements = [replacement, .. scope.Body.Statements] },
        };
        var forged = program with {
            Functions = program.Functions.SetItem(program.Functions.IndexOf(function),
                function with { Body = function.Body with { Statements = [forgedScope] } }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void NestedOwningFieldViewRejectsWholeOwnerMove()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Index {
                private OwnedInt32Buffer _starts;
                public Index(OwnedInt32Buffer starts) { _starts = move starts; }
                [NoThrow] [Cleanup] public void Close() {}
                public int32 Steal(Index owner) {
                    ReadonlyInt32BufferView starts = owner._starts.Slice(0, 1);
                    Index moved = move owner;
                    return starts.Read(0);
                }
            }
            int32 Main() { return 42; }
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("buffer.Slice(-1, 0)")]
    [InlineData("buffer.Slice(0, -1)")]
    [InlineData("buffer.Slice(3, 1)")]
    [InlineData("buffer.Slice(2, 2)")]
    [InlineData("buffer.Slice(2147483647, 2147483647)")]
    [InlineData("buffer[4..]")]
    [InlineData("buffer[-1..]")]
    [InlineData("buffer[2..1]")]
    [InlineData("buffer[..^4]")]
    public async Task SliceRejectsOutOfPrefixOrOverflowingRangeAsync(string range)
    {
        var source = $$"""
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(4, out var raw);
                mut OwnedInt32Buffer buffer = move raw;
                require buffer.TryAppend(42);
                ReadonlyInt32BufferView view = {{range}};
                return view.Length;
            }
            """;
        Assert.Equal(unchecked((int)0xE0540003), await RunAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true));
    }

    [Fact]
    public void RangeViewsRequireCompileTimeBoundsAndReadonlyLocals()
    {
        var dynamicBound = Compilation.Create(SyntaxTree.Parse("""
            int32 Start() { return 0; }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                OwnedInt32Buffer owner = move raw;
                ReadonlyInt32BufferView view = owner[Start()..];
                return view.Length;
            }
            """));
        Assert.Contains(dynamicBound.Diagnostics, static diagnostic => diagnostic.Code == "TRM3001");
        Assert.Null(dynamicBound.Program);

        var mutableView = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                mut OwnedInt32Buffer owner = move raw;
                mut MutableInt32BufferView view = owner[..];
                return view.Length;
            }
            """));
        Assert.Contains(mutableView.Diagnostics, static diagnostic => diagnostic.Code == "TRM3023");
        Assert.Null(mutableView.Program);

        var endRelativeLower = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                OwnedInt32Buffer owner = move raw;
                ReadonlyInt32BufferView view = owner[^0..];
                return view.Length;
            }
            """));
        Assert.Contains(endRelativeLower.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3001");
        Assert.Null(endRelativeLower.Program);

        var mutation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                mut OwnedInt32Buffer owner = move raw;
                ReadonlyInt32BufferView view = owner[..];
                owner.Set(0, 42);
                return view.Length;
            }
            """));
        Assert.Contains(mutation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3023");
        Assert.Null(mutation.Program);
    }

    [Fact]
    public async Task EmptyEndSliceHasMetadataButNoReadableElementAsync()
    {
        const string Valid = Buffer + """
            ReadonlyInt32BufferView empty = buffer.Slice(buffer.Count, 0);
            if (empty.Length == 0) { return 42; }
            return 0;
        }
        """;
        Assert.Equal(42, await RunAsync(Valid, BoundRuntimePolicy.FailFast).ConfigureAwait(true));
        const string Invalid = Buffer + """
            ReadonlyInt32BufferView empty = buffer.Slice(buffer.Count, 0);
            return empty.Read(0);
        }
        """;
        Assert.Equal(unchecked((int)0xE0540003), await RunAsync(Invalid, BoundRuntimePolicy.FailFast).ConfigureAwait(true));
    }

    [Fact]
    public async Task CheckedViewReadRecoveryLeavesOriginalOwnerLiveAsync()
    {
        const string Source = """
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                OwnedInt32Buffer owner = move raw;
                unsafe {
                    try {
                        ReadonlyInt32BufferView view = owner.Slice(0, 0);
                        return view.Read(0);
                    }
                    catch { return owner.Count + 42; }
                }
            }
            """;
        Assert.Equal(42, await RunAsync(Source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
    }

    [Fact]
    public async Task CheckedRangeViewFailureRecoveryLeavesOriginalOwnerLiveAsync()
    {
        const string Source = """
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                OwnedInt32Buffer owner = move raw;
                unsafe {
                    try {
                        ReadonlyInt32BufferView view = owner[..^1];
                        return view.Length;
                    }
                    catch { return owner.Count + 42; }
                }
            }
            """;
        Assert.Equal(42, await RunAsync(Source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
    }

    [Theory]
    [InlineData("OwnedInt32Buffer consumed = move buffer;")]
    [InlineData("OwnedInt32Buffer.TryCreate(1, out buffer);")]
    [InlineData("buffer.Set(0, 42);")]
    [InlineData("buffer.TryAppend(42);")]
    [InlineData("if (false) { buffer.Set(0, 42); }")]
    [InlineData("while (false) { buffer.Set(0, 42); }")]
    public void ViewRejectsRootReplacementOrMutationOnEveryLexicalPath(string body)
    {
        var source = Buffer + " ReadonlyInt32BufferView view = buffer.Slice(0, 1); "
            + body + " return view.Read(0); }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("ReadonlyInt32BufferView copy = view;")]
    [InlineData("mut ReadonlyInt32BufferView copy = buffer.Slice(0, 1);")]
    [InlineData("return buffer.Slice(0, 1).Read(0);")]
    public void ViewsCannotBeCopiedOrUsedAsTemporaryValues(string expression)
    {
        var source = Buffer + " ReadonlyInt32BufferView view = buffer.Slice(0, 1); "
            + expression + " return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("struct Container { public ReadonlyInt32BufferView View; }")]
    [InlineData("ReadonlyInt32BufferView ReturnView(OwnedInt32Buffer value) { return value.Slice(0, 1); }")]
    [InlineData("int32 Use(ReadonlyInt32BufferView value) { return 0; }")]
    public void ViewFieldsReturnsAndParametersAreRejected(string declaration)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(declaration + " int32 Main() { return 42; }"));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
    }

    [Fact]
    public void VerifierRejectsForgedViewField()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Label { public int32 Value; }
            int32 Main() { return 42; }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var structure = Assert.Single(program.Structures);
        var field = Assert.Single(structure.Fields);
        var forged = program with {
            Structures = [structure with {
                Fields = [field with { Type = PrimitiveType.ReadonlyInt32BufferView, Size = 16 }],
                Size = 16,
            }],
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void SliceBoundsAndReadsCannotForgeNoThrow()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow] int32 Read(OwnedInt32Buffer buffer) {
                ReadonlyInt32BufferView view = buffer.Slice(0, 1);
                return view.Read(0);
            }
            int32 Main() { return 42; }
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3017");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void PublicIrRejectsEscapedViewAndForgedOwnerMutation()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Read(OwnedInt32Buffer owner) {
                ReadonlyInt32BufferView view = owner.Slice(0, owner.Count);
                return view.Read(0);
            }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                mut OwnedInt32Buffer value = move raw;
                require value.TryAppend(42);
                return Read(move value);
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var function = Assert.Single(program.Functions, static item => item.Name == "Read");
        var index = program.Functions.IndexOf(function);
        var owner = Assert.IsType<BoundTryFinally>(Assert.Single(function.Body.Statements));
        var view = Assert.IsType<BoundViewScope>(Assert.Single(owner.Body.Statements));
        var result = Assert.IsType<BoundReturn>(Assert.Single(view.Body.Statements));
        var read = Assert.IsType<BoundViewRead>(result.Value);

        Reject(view with { Start = new BoundMove(0, PrimitiveType.OwnedInt32Buffer, view.Span) });
        Reject(view with {
            Body = view.Body with {
                Statements = [
            new BoundAssignment(0, new BoundRead(0, PrimitiveType.OwnedInt32Buffer, view.Span), view.Span),
            result,
        ]
            }
        });
        Reject(view with {
            Body = view.Body with {
                Statements = [
            result with { Value = read with { Variable = 0 } },
        ]
            }
        });
        Reject(view with {
            Body = view.Body with {
                Statements = [
            result with { Value = new BoundRead(view.Variable, PrimitiveType.ReadonlyInt32BufferView, view.Span) },
        ]
            }
        });
        var endedView = view with { Body = view.Body with { Statements = [] } };
        var escapedOwner = owner with { Body = owner.Body with { Statements = [endedView, result] } };
        var escapedFunction = function with { Body = function.Body with { Statements = [escapedOwner] } };
        var escaped = program with { Functions = program.Functions.SetItem(index, escapedFunction) };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(escaped));

        void Reject(BoundViewScope invalid)
        {
            var forged = program with {
                Functions = program.Functions.SetItem(index, function with {
                    Body = function.Body with {
                        Statements = [
                        owner with { Body = owner.Body with { Statements = [invalid] } },
                    ]
                    },
                }),
            };
            _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
            _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast));
        }
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SemanticModuleRetainsViewScopeAndNativeProfileAsync(BoundRuntimePolicy policy)
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Read(OwnedInt32Buffer owner) {
                ReadonlyInt32BufferView view = owner.Slice(0, owner.Count);
                return view.Read(0);
            }
            """), policy);
        Assert.False(provider.HasErrors, string.Join(Environment.NewLine, provider.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(provider.Program), ["Read"]);
        Assert.Equal("native-16", ModuleCompiler.GetNativeProfileVersion(producer, false));
        var client = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                mut OwnedInt32Buffer value = move raw;
                require value.TryAppend(42);
                {
                    ReadonlyInt32BufferView preview = value.Slice(0, value.Count);
                    if (preview.Read(0) != 42) { return 0; }
                }
                return Read(move value);
            }
            """), ModuleCompiler.CreateReference(producer), runtimePolicy: policy);
        Assert.False(client.HasErrors, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(client.Program), producer);
        Assert.Equal("native-16", ModuleCompiler.GetNativeProfileVersion(consumer, true));
        Assert.Equal(42, await RunAsync(ModuleCompiler.Link(consumer, producer), policy).ConfigureAwait(true));
    }

    private static async Task<int> RunAsync(string source, BoundRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return await RunAsync(Assert.IsType<BoundProgram>(compilation.Program), policy).ConfigureAwait(true);
    }

    private static async Task<int> RunAsync(BoundProgram program, BoundRuntimePolicy policy)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        var image = NativeEmitter.Emit(program, nativePolicy);
        var path = Path.Combine(Directory.GetCurrentDirectory(), $"terrum-view-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Native view test process did not start.");
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
}
