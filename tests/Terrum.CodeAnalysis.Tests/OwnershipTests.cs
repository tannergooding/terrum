// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class OwnershipTests
{
    private const string Owner = """
        unique struct Owner {
            public int32 Id;
            public Owner(int32 id) { Id = id; }
            [NoThrow] [Cleanup] public void Close() {
                Host.Trace[0] = Host.Trace[0] ^ Id;
                Host.Trace[1] = Id;
            }
            [NoThrow] public int32 Peek() { return Id; }
        }
        """;

    [Theory]
    [InlineData("Owner copy = value;", "TRM3023")]
    [InlineData("Owner copy = move value; Owner duplicate = move value;", "TRM3023")]
    [InlineData("Owner copy = move value; return value.Id;", "TRM3023")]
    [InlineData("if (flag) { Owner gone = move value; } return value.Id;", "TRM3023")]
    [InlineData("value.Close();", "TRM3023")]
    [InlineData("return new Owner(42).Id;", "TRM3023")]
    public void UniqueOwnersCannotBeCopiedReadVacantOrManuallyCleaned(string body, string code)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Owner
            + " int32 Main() { bool flag = true; Owner value = new Owner(42); " + body + " return 0; }"),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0, 0], true)]);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("struct Copy { public int32 Id; public Copy() { Id = 1; } [NoThrow] [Cleanup] public void Close() {} }", "TRM3023")]
    [InlineData("unique struct U { public int32 Id; public U() { Id = 1; } [Cleanup] public void Close() {} }", "TRM3023")]
    [InlineData("unique struct U { public int32 Id; public U() { Id = 1; } } struct Copy { public U Value; public Copy() { Value = new U(); } }", "TRM3023")]
    [InlineData("unique struct U { public int32 Id; public U() { Id = 1; U duplicate = this; } }", "TRM3023")]
    public void UnsupportedOwnershipContractsAreDiagnosed(string declarations, string code)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(declarations + " int32 Main() { return 0; }"));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Theory]
    [InlineData("Owner first = new Owner(1); Owner second = new Owner(2);", 3, 1)]
    [InlineData("Owner first = new Owner(1); Owner second = move first;", 1, 1)]
    [InlineData("mut Owner first = new Owner(1); first = new Owner(2);", 3, 2)]
    public async Task NormalCleanupRunsOnceInReverseLexicalOrderAsync(string body, int mask, int last)
    {
        var source = Owner + " int32 Main() { { " + body
            + " } if (Host.Trace[0] == " + mask + " && Host.Trace[1] == " + last + ") { return 42; } return 0; }";
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true));
    }

    [Fact]
    public async Task ReturningScalarSnapshotsBeforeDroppingOwnerAsync()
    {
        var source = Owner + """
            int32 Take() {
                Owner value = new Owner(1);
                return value.Peek();
            }
            int32 Main() {
                int32 result = Take();
                if (result == 1 && Host.Trace[0] == 1 && Host.Trace[1] == 1) {
                    return 42;
                }
                return 0;
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true));
    }

    [Fact]
    public async Task ConstructorDropsLocalOwnerBeforeImplicitReturnAsync()
    {
        var source = Owner + """
            struct Result {
                public int32 Id;
                public Result() {
                    Owner local = new Owner(1);
                    Id = 42;
                }
            }
            int32 Main() {
                Result result = new Result();
                if (result.Id == 42 && Host.Trace[0] == 1) { return 42; }
                return 0;
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true));
    }

    [Fact]
    public async Task IndirectOwnerReturnTransfersCleanupToCallerAsync()
    {
        var source = """
            unique struct Token {
                public int32 First;
                public int32 Second;
                public int32 Third;
                public Token(int32 value) {
                    First = value;
                    Second = value + 1;
                    Third = value + 2;
                }
                [NoThrow] [Cleanup] public void Close() {
                    Host.Trace[0] = Host.Trace[0] ^ First;
                    Host.Trace[1] = Third;
                }
            }
            Token Make() {
                Token value = new Token(40);
                return move value;
            }
            int32 Main() {
                {
                    Token value = Make();
                    if (value.Third != 42 || Host.Trace[0] != 0) { return 0; }
                }
                if (Host.Trace[0] == 40 && Host.Trace[1] == 42) { return 42; }
                return 0;
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BranchMoveRetainsExactlyOneCleanupObligationAsync(bool move)
    {
        var source = Owner + " int32 Main() { bool choose = " + (move ? "true" : "false") + ";"
            + " { Owner value = new Owner(1); if (choose) { Owner destination = move value; } }"
            + " if (Host.Trace[0] == 1 && Host.Trace[1] == 1) { return 42; } return 0; }";
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
    }

    [Fact]
    public async Task LoopInitializerOwnerDropsAfterLastIterationAsync()
    {
        var source = Owner + """
            int32 Main() {
                for (Owner value = new Owner(1); false;) {
                    require false;
                }
                if (Host.Trace[0] == 1) { return 42; }
                return 0;
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true));
    }

    [Fact]
    public async Task CheckedUnwindDropsInnerThenOuterBeforeRecoveryAsync()
    {
        var source = Owner + """
            int32 Main() {
                unsafe {
                    try {
                        Owner outer = new Owner(1);
                        Owner inner = new Owner(2);
                        require inner.Peek() == 0;
                    } catch {
                        if (Host.Trace[0] == 3 && Host.Trace[1] == 1) { return 42; }
                        return 0;
                    }
                }
                return 0;
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
    }

    [Fact]
    public async Task CheckedCrossFunctionUnwindDropsOwnersBeforeCallerRecoveryAsync()
    {
        var source = Owner + """
            void Fail() {
                Owner outer = new Owner(1);
                Owner inner = new Owner(2);
                require false;
            }
            int32 Main() {
                unsafe {
                    try { Fail(); } catch {
                        if (Host.Trace[0] == 3 && Host.Trace[1] == 1) { return 42; }
                        return 0;
                    }
                }
                return 0;
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
    }

    [Fact]
    public async Task FailedReplacementLeavesOriginalOwnerForExceptionalCleanupAsync()
    {
        var source = """
            unique struct Token {
                public int32 Id;
                public Token(int32 id) {
                    require id != 2;
                    Id = id;
                }
                [NoThrow] [Cleanup] public void Close() {
                    Host.Trace[0] = Host.Trace[0] ^ Id;
                    Host.Trace[1] = Id;
                }
            }
            int32 Main() {
                unsafe {
                    try {
                        mut Token value = new Token(1);
                        value = new Token(2);
                    } catch {
                        if (Host.Trace[0] == 1 && Host.Trace[1] == 1) { return 42; }
                    }
                }
                return 0;
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
    }

    [Fact]
    public async Task PassedOwnerDropsInCalleeAcrossSixthStackArgumentAsync()
    {
        var source = Owner + """
            void Take(int32 a, int32 b, int32 c, int32 d, int32 e, Owner value) {
                require value.Id == 42;
            }
            int32 Main() {
                Owner value = new Owner(42);
                Take(1, 2, 3, 4, 5, move value);
                if (Host.Trace[0] == 42 && Host.Trace[1] == 42) { return 42; }
                return 0;
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true));
    }

    [Fact]
    public async Task LaterFailedArgumentDropsEarlierStagedOwnersAsync()
    {
        var source = Owner + """
            void Take(Owner first, Owner second, int32 third) {}
            int32 Main() {
                unsafe {
                    try {
                        Owner named = new Owner(1);
                        Take(move named, new Owner(2), 1 / 0);
                    } catch {
                        if (Host.Trace[0] == 3 && Host.Trace[1] == 1) { return 42; }
                    }
                }
                return 0;
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
    }

    [Fact]
    public async Task ExceptionalCalleeDropsTransferredParameterAsync()
    {
        var source = Owner + """
            void Fail(Owner value) { require false; }
            int32 Main() {
                unsafe {
                    try { Fail(new Owner(42)); }
                    catch {
                        if (Host.Trace[0] == 42 && Host.Trace[1] == 42) { return 42; }
                    }
                }
                return 0;
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
    }

    [Fact]
    public async Task CalleeCanReturnMovedParameterWithoutDroppingItAsync()
    {
        var source = Owner + """
            Owner Relay(Owner input) { return move input; }
            int32 Main() {
                { Owner value = Relay(new Owner(42)); if (Host.Trace[0] != 0) { return 0; } }
                if (Host.Trace[0] == 42) { return 42; }
                return 0;
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true));
    }

    [Fact]
    public void ReceiverLoanCannotBeConsumedByLaterArgument()
    {
        var source = Owner + """
            int32 Main() {
                Owner value = new Owner(42);
                return value.Peek(move value);
            }
            """;
        var modified = source.Replace("public int32 Peek()", "public int32 Peek(Owner consumed)",
            StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(modified),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0, 0], true)]);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public async Task ConditionalOwningOutputDropsFalseAndTransfersTrueAsync()
    {
        var source = Owner + """
            bool Make(bool success, out Owner value) {
                if (success) { value = new Owner(43); }
                else { value = new Owner(7); }
                return success;
            }
            int32 Main() {
                {
                    mut Owner value = new Owner(1);
                    bool rejected = Make(false, out value);
                    if (rejected) { return 10; }
                    if (Host.Trace[0] != 6) { return 11; }
                    if (!Make(true, out value)) { return 12; }
                    if (value.Id != 43) { return 13; }
                    if (Host.Trace[0] != 6) { return 14; }
                }
                if (Host.Trace[0] != 45) { return 100 + Host.Trace[0]; }
                if (Host.Trace[1] != 43) { return 15; }
                return 42;
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
    }

    [Fact]
    public async Task FalseOwningOutputLeavesNoCallerOwnerLiveAsync()
    {
        var source = Owner + """
            bool Make(out Owner value) { value = new Owner(42); return false; }
            int32 Main() {
                {
                    mut Owner value = new Owner(1);
                    bool success = Make(out value);
                    if (success) { return 0; }
                }
                if (Host.Trace[0] == 43) { return 42; }
                return Host.Trace[0];
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
    }

    [Fact]
    public async Task TrueOwningOutputReplacesExistingOwnerAsync()
    {
        var source = Owner + """
            bool Make(bool success, out Owner value) {
                if (success) { value = new Owner(43); }
                else { value = new Owner(42); }
                return success;
            }
            int32 Main() {
                {
                    mut Owner value = new Owner(1);
                    require Make(true, out value);
                    if (value.Id != 43 || Host.Trace[0] != 1) { return 200 + Host.Trace[0]; }
                }
                if (Host.Trace[0] == 42) { return 42; }
                return Host.Trace[0];
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
    }

    [Fact]
    public async Task ExceptionalOwningOutputDropsPartialPublicationAsync()
    {
        var source = Owner + """
            bool Fail(out Owner value) {
                value = new Owner(2);
                require false;
                return true;
            }
            int32 Main() {
                unsafe {
                    try {
                        mut Owner value = new Owner(1);
                        bool success = Fail(out value);
                    } catch {
                        if (Host.Trace[0] == 3 && Host.Trace[1] == 2) { return 42; }
                    }
                }
                return 0;
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
    }

    [Fact]
    public async Task LaterArgumentFailurePreservesExistingOutputUntilScopeUnwindAsync()
    {
        var source = Owner + """
            bool Replace(out Owner value, int32 later) {
                value = new Owner(1);
                return true;
            }
            int32 FailLater() {
                Host.Trace[2] = Host.Trace[0];
                require false;
                return 0;
            }
            int32 Main() {
                unsafe {
                    try {
                        mut Owner value = new Owner(42);
                        require Replace(out value, FailLater());
                    } catch {
                        if (Host.Trace[2] == 0 && Host.Trace[0] == 42) { return 42; }
                    }
                }
                return 0;
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions,
            [0, 0, 0]).ConfigureAwait(true));
    }

    [Theory]
    [InlineData("Make(out owner, out owner);")]
    [InlineData("Take(move owner, move owner);")]
    public void OwningArgumentAliasesDoNotDuplicateObligations(string statement)
    {
        var source = Owner + """
            bool Make(out Owner first, out Owner second) {
                first = new Owner(1);
                second = new Owner(2);
                return true;
            }
            void Take(Owner first, Owner second) {}
            int32 Main() {
                mut Owner owner = new Owner(42);
            """ + statement + " return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0, 0], true)]);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public async Task SixthStackOutputPublishesIndirectOwnerAsync()
    {
        var source = """
            unique struct Token {
                public int32 First;
                public int32 Second;
                public int32 Third;
                public Token(int32 value) {
                    First = value;
                    Second = value + 1;
                    Third = value + 2;
                }
                [NoThrow] [Cleanup] public void Close() {
                    Host.Trace[0] = Third;
                }
            }
            bool Fill(int32 a, int32 b, int32 c, int32 d, int32 e, out Token value) {
                value = new Token(40);
                return true;
            }
            int32 Main() {
                {
                    require Fill(1, 2, 3, 4, 5, out var token);
                    if (token.Third != 42) { return 10; }
                    if (Host.Trace[0] != 0) { return 11; }
                }
                return Host.Trace[0];
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true));
    }

    [Theory]
    [InlineData("output.Id")]
    [InlineData("output.Peek()")]
    public async Task DirectOwningOutputReadsItsValueInsideCalleeAsync(string read)
    {
        var source = Owner + " bool Fill(out Owner output) { output = new Owner(42); return "
            + read + " == 42; } int32 Main() { require Fill(out var value); return value.Id; }";
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
    }

    [Theory]
    [InlineData("output.Third")]
    [InlineData("output.Peek()")]
    public async Task IndirectOwningOutputReadsItsValueInsideCalleeAsync(string read)
    {
        var source = """
            unique struct Token {
                public int32 First;
                public int32 Second;
                public int32 Third;
                public Token(int32 value) {
                    First = value;
                    Second = value + 1;
                    Third = value + 2;
                }
                [NoThrow] [Cleanup] public void Close() {}
                [NoThrow] public int32 Peek() { return Third; }
            }
            bool Fill(out Token output) {
                output = new Token(40);
                return OUTPUT_READ == 42;
            }
            int32 Main() { require Fill(out var value); return value.Third; }
            """.Replace("OUTPUT_READ", read, StringComparison.Ordinal);
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
    }

    [Fact]
    public async Task MovingOwningOutputIntoLocalAllowsRefillAsync()
    {
        var source = Owner + """
            bool Refill(out Owner output) {
                output = new Owner(7);
                Owner moved = move output;
                output = new Owner(42);
                return output.Peek() == 42;
            }
            int32 Main() {
                require Refill(out var value);
                if (Host.Trace[0] != 7) { return 0; }
                return value.Id;
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
    }

    [Fact]
    public async Task MovingIndirectOutputIntoCallThenReturningFalseDropsBothAsync()
    {
        var source = """
            unique struct Token {
                public int32 First;
                public int32 Second;
                public int32 Third;
                public Token(int32 value) {
                    First = value;
                    Second = value + 1;
                    Third = value + 2;
                }
                [NoThrow] [Cleanup] public void Close() {
                    Host.Trace[0] = Host.Trace[0] ^ First;
                }
            }
            void Take(Token value) {}
            bool Reject(out Token output) {
                output = new Token(7);
                Take(move output);
                output = new Token(99);
                return false;
            }
            int32 Main() {
                mut Token old = new Token(1);
                bool success = Reject(out old);
                if (success) { return 0; }
                if (Host.Trace[0] == 101) { return 42; }
                return 0;
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
    }

    [Fact]
    public void FailedOwningOutputCannotBeRead()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Token {
                public int32 Id;
                public Token() { Id = 42; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            bool Try(out Token value) { value = new Token(); return false; }
            int32 Main() {
                bool success = Try(out var result);
                return result.Id;
            }
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void OwningForConditionOutputRequiresLexicalRegion()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Ticket {
                public int32 Id;
                public Ticket() { Id = 42; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            bool Next(out Ticket ticket) { ticket = new Ticket(); return true; }
            int32 Main() {
                for (; Next(out var ticket);) { return ticket.Id; }
                return 0;
            }
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void PublicIrCannotOmitLexicalDropOrForgeOwnershipLoan()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public int32 Id;
                public Owner(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            int32 Main() { Owner owner = new Owner(42); return owner.Id; }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var entry = program.Functions[program.EntryPoint];
        var truncated = entry with { Body = entry.Body with { Statements = entry.Body.Statements.RemoveAt(1) } };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, truncated),
        }));
        var earlyDrop = entry with {
            Body = entry.Body with {
                Statements = entry.Body.Statements.Add(new BoundAssignment(0,
                new BoundCall(0, [new BoundConstant(1, PrimitiveType.Int32, default)],
                    (PrimitiveType)100, default), default)),
            }
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, earlyDrop),
        }));
        var forged = entry with {
            Body = entry.Body with {
                Statements = entry.Body.Statements.SetItem(0, new BoundLocal(0,
                new BoundReceiverLoan(0, (PrimitiveType)100, default), default)),
            }
        };
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, forged),
        }, NativeRuntimePolicy.FailFast));
        var unboundCleanup = program with {
            Structures = program.Structures.SetItem(0, program.Structures[0] with { CleanupMethod = "Owner.Missing" }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(unboundCleanup));
    }

    [Theory]
    [InlineData("then")]
    [InlineData("else")]
    [InlineData("while")]
    public void PublicIrRequiresCleanupForBareUniqueDeclarations(string placement)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public int32 Id;
                public Owner(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            int32 Main() { Owner owner = new Owner(42); return owner.Id; }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var entry = program.Functions[program.EntryPoint];
        var local = Assert.IsType<BoundLocal>(entry.Body.Statements[0]);
        var region = Assert.IsType<BoundTryFinally>(entry.Body.Statements[1]);
        var result = Assert.Single(Assert.IsType<BoundBlock>(region.Body).Statements);
        BoundStatement bare = placement switch {
            "then" => new BoundIf(new BoundConstant(1, PrimitiveType.Bool, default), local, null, default),
            "else" => new BoundIf(new BoundConstant(0, PrimitiveType.Bool, default),
                new BoundReturn(new BoundConstant(0, PrimitiveType.Int32, default), default), local, default),
            "while" => new BoundWhile(new BoundConstant(1, PrimitiveType.Bool, default), local, default),
            _ => throw new InvalidOperationException(),
        };
        var body = entry.Body with { Statements = placement == "while" ? [bare] : [bare, result] };
        var forged = program with {
            Functions = program.Functions.SetItem(program.EntryPoint, entry with { Body = body }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicIrRejectsWritableOrOutUniqueReceiver(bool isOut)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public mut int32 Id;
                public Owner(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {}
                public int32 Peek() { return Id; }
                public bool Ready() { return false; }
            }
            int32 Main() { Owner owner = new Owner(42); return owner.Peek(); }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var index = -1;
        for (var i = 0; i < program.Functions.Length; i++)
        {
            if (program.Functions[i].Name == (isOut ? "Owner.Ready" : "Owner.Peek"))
            {
                index = i;
                break;
            }
        }
        Assert.True(index >= 0);
        var function = program.Functions[index];
        var receiver = function.Variables[0] with { IsMutable = !isOut, IsOut = isOut };
        var body = isOut ? function.Body : function.Body with {
            Statements = [new BoundFieldAssignment(0, 0,
                new BoundConstant(1, PrimitiveType.Int32, default), default), .. function.Body.Statements],
        };
        var forged = program with {
            Functions = program.Functions.SetItem(index, function with {
                Variables = function.Variables.SetItem(0, receiver),
                Body = body,
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicIrCannotOmitOrUnconditionallyDropOwningParameter(bool output)
    {
        var source = Owner + (output
            ? " bool Take(out Owner value) { value = new Owner(42); return true; }"
            : " void Take(Owner value) {}")
            + " int32 Main() { return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0, 0], true)]);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var index = -1;
        for (var i = 0; i < program.Functions.Length; i++)
        {
            if (program.Functions[i].Name == "Take")
            {
                index = i;
                break;
            }
        }
        Assert.True(index >= 0);
        var function = program.Functions[index];
        var region = Assert.IsType<BoundTryFinally>(Assert.Single(function.Body.Statements));
        var unscoped = program with {
            Functions = program.Functions.SetItem(index, function with { Body = region.Body }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(unscoped));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(unscoped, NativeRuntimePolicy.FailFast));

        var drop = Assert.IsType<BoundDrop>(Assert.Single(region.Finally.Statements));
        var invalid = region with {
            Finally = region.Finally with {
                Statements = [drop with { IsOutput = !drop.IsOutput }],
            },
        };
        var forged = program with {
            Functions = program.Functions.SetItem(index, function with {
                Body = function.Body with { Statements = [invalid] },
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
    }

    [Fact]
    public async Task UniqueByValueContractSurvivesSeparateNativeModulesAsync()
    {
        var providerSource = """
            public unique struct Token {
                public int32 Id;
                public Token(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            public Token Make(int32 id) { return new Token(id); }
            public int32 Take(Token token) { return token.Id; }
            """;
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(providerSource));
        Assert.False(providerCompilation.HasErrors, string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(providerCompilation.Program),
            ["Make", "Take", "Token.Close"], "owned_param", NativeRuntimePolicy.FailFast);
        var reference = NativeModuleCompiler.CreateReference(provider);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Token token = Make(42); return Take(move token); }"), reference);
        Assert.False(consumerCompilation.HasErrors, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumerCompilation.Program),
            provider, NativeRuntimePolicy.FailFast);
        Assert.Equal("native-13", NativeModuleCompiler.Inspect(consumer).NativeProfileVersion);
        Assert.Equal(42, await NativeModuleCompiler.RunAsync(consumer, provider, TimeSpan.FromSeconds(10),
            NativeRuntimePolicy.FailFast).ConfigureAwait(true));

        var changedCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(providerSource.Replace(
            "return token.Id;", "return token.Id + 1;", StringComparison.Ordinal)));
        Assert.False(changedCompilation.HasErrors);
        var replacement = NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(changedCompilation.Program),
            ["Make", "Take", "Token.Close"], "owned_param", NativeRuntimePolicy.FailFast);
        Assert.Equal(43, await NativeModuleCompiler.RunAsync(consumer, replacement, TimeSpan.FromSeconds(10),
            NativeRuntimePolicy.FailFast).ConfigureAwait(true));
    }

    [Fact]
    public async Task ConditionalOwningOutputContractSurvivesSeparateModulesAsync()
    {
        const string ProviderSource = """
            public unique struct Token {
                public int32 Id;
                public Token(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            public bool Make(out Token token) { token = new Token(42); return true; }
            """;
        var firstCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(ProviderSource));
        Assert.False(firstCompilation.HasErrors, string.Join(Environment.NewLine, firstCompilation.Diagnostics));
        var first = Assert.IsType<BoundProgram>(firstCompilation.Program);
        var semanticProducer = ModuleCompiler.CreateProducer(first, ["Make", "Token.Close"]);
        var semanticConsumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { require Make(out var ticket); return ticket.Id; }"),
            ModuleCompiler.CreateReference(semanticProducer));
        Assert.False(semanticConsumerCompilation.HasErrors,
            string.Join(Environment.NewLine, semanticConsumerCompilation.Diagnostics));
        var client = Assert.IsType<BoundProgram>(semanticConsumerCompilation.Program);
        var semanticConsumer = ModuleCompiler.CreateConsumer(client, semanticProducer);
        Assert.Equal("native-13", ModuleCompiler.GetNativeProfileVersion(semanticConsumer, true));
        var nativeProducer = NativeModuleCompiler.CreateProducer(first,
            ["Make", "Token.Close"], "owned_output", NativeRuntimePolicy.FailFast);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(client, nativeProducer,
            NativeRuntimePolicy.FailFast);
        Assert.Equal(42, await NativeModuleCompiler.RunAsync(nativeConsumer, nativeProducer,
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast).ConfigureAwait(true));

        var changedCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            ProviderSource.Replace("new Token(42)", "new Token(43)", StringComparison.Ordinal)));
        Assert.False(changedCompilation.HasErrors);
        var changed = Assert.IsType<BoundProgram>(changedCompilation.Program);
        _ = ModuleCompiler.Link(semanticConsumer, ModuleCompiler.CreateProducer(changed,
            ["Make", "Token.Close"]));
        var replacement = NativeModuleCompiler.CreateProducer(changed,
            ["Make", "Token.Close"], "owned_output", NativeRuntimePolicy.FailFast);
        Assert.Equal(43, await NativeModuleCompiler.RunAsync(nativeConsumer, replacement,
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast).ConfigureAwait(true));

        var incompatibleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            ProviderSource.Replace("out Token token) { token = new Token(42); return true;",
                "Token token) { return true;", StringComparison.Ordinal)));
        Assert.False(incompatibleCompilation.HasErrors);
        var incompatible = Assert.IsType<BoundProgram>(incompatibleCompilation.Program);
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(semanticConsumer,
            ModuleCompiler.CreateProducer(incompatible, ["Make", "Token.Close"])));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.Validate(nativeConsumer,
            NativeModuleCompiler.CreateProducer(incompatible, ["Make", "Token.Close"],
                "owned_output", NativeRuntimePolicy.FailFast)));
    }

    [Theory]
    [InlineData(false, BoundRuntimePolicy.FailFast)]
    [InlineData(false, BoundRuntimePolicy.CheckedExceptions)]
    [InlineData(true, BoundRuntimePolicy.CheckedExceptions)]
    public async Task ImportedOwningOutputCleanupCallsProducerAsync(bool exceptional, BoundRuntimePolicy policy)
    {
        var body = exceptional
            ? "output = Make(7); require false; return true;"
            : "output = Make(7); return false;";
        var main = exceptional
            ? "Token guard = Make(41); unsafe { try { Retire(out var absent); } catch { return guard.Id; } } return 0;"
            : "Token guard = Make(41); bool accepted = Retire(out var absent); if (accepted) { return 0; } return guard.Id;";
        await AssertExternalCleanupConsumerAsync(
            "bool Retire(out Token output) { " + body + " } int32 Main() { " + main + " }", policy)
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task ImportedStagedOwnerCleanupCallsProducerAfterLaterFailureAsync()
    {
        const string Source = """
            void Take(Token owner, int32 later) {}
            int32 FailLater() { require false; return 0; }
            int32 Main() {
                Token guard = Make(41);
                unsafe {
                    try {
                        Take(Make(7), FailLater());
                    } catch {
                        return guard.Id;
                    }
                }
                return 0;
            }
            """;
        await AssertExternalCleanupConsumerAsync(Source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true);
    }

    [Fact]
    public void OwnershipTransferProfileRejectsUnknownRuntimePolicy()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Compilation.GetNativeProfileVersion((BoundRuntimePolicy)int.MaxValue,
                uniqueValues: true, ownershipTransfer: true));
    }

    [Fact]
    public void MalformedUnexportedParameterTypeProducesModuleFormatError()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public struct Box {
                public int32 Id;
                public Box(int32 id) { Id = id; }
            }
            int32 Use(Box input) { return input.Id; }
            public int32 Value() { Box box = new Box(42); return Use(box); }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program), ["Value"]);
        var forged = (byte[])artifact.Clone();
        var contractType = forged.AsSpan().IndexOf("in:Box:E"u8);
        Assert.True(contractType >= 0);
        "10000"u8.CopyTo(forged.AsSpan(contractType + 3, 5));
        var variableName = forged.AsSpan().LastIndexOf("input"u8);
        Assert.True(variableName >= 4);
        Assert.Equal(5, BinaryPrimitives.ReadInt32LittleEndian(forged.AsSpan(variableName - 4, 4)));
        var type = variableName + 5;
        Assert.Equal(100, BinaryPrimitives.ReadInt32LittleEndian(forged.AsSpan(type, 4)));
        BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(type, 4), 10000);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forged));
    }

    [Fact]
    public void ConsumerImportCannotDowngradeOwningParameterProfile()
    {
        const string ProviderSource = """
            public unique struct Token {
                public int32 Id;
                public Token(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            public Token Make(int32 id) { return new Token(id); }
            public int32 Take(Token token) { return token.Id; }
            """;
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(ProviderSource));
        Assert.False(providerCompilation.HasErrors, string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(providerCompilation.Program),
            ["Make", "Take", "Token.Close"]);
        var client = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Token token = Make(42); return Take(move token); }"),
            ModuleCompiler.CreateReference(provider));
        Assert.False(client.HasErrors, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(client.Program), provider);
        Assert.Equal("native-13", ModuleCompiler.GetNativeProfileVersion(consumer, true));
        var forged = (byte[])consumer.Clone();
        var importName = forged.AsSpan().IndexOf("Take"u8);
        Assert.True(importName >= 4);
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(forged.AsSpan(importName - 4, 4)));
        var contract = importName + 4;
        var marker = contract + 4 + BinaryPrimitives.ReadInt32LittleEndian(forged.AsSpan(contract, 4)) + 2;
        Assert.Equal(1, forged[marker]);
        forged[marker] = 0;
        var profile = forged.AsSpan().IndexOf("native-13"u8);
        Assert.True(profile >= 0);
        forged[profile + "native-1".Length] = (byte)'2';
        Assert.Equal("native-12", ModuleCompiler.GetNativeProfileVersion(forged, true));
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(forged, provider));
    }

    private static async Task AssertExternalCleanupConsumerAsync(string consumerSource, BoundRuntimePolicy policy)
    {
        const string ProducerSource = """
            public unique struct Token {
                public int32 Id;
                public Token(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            public Token Make(int32 id) { return new Token(id); }
            """;
        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(ProducerSource), policy);
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(library.Program),
            ["Make", "Token.Close"], "external_cleanup", nativePolicy);
        var client = Compilation.Create(SyntaxTree.Parse(consumerSource),
            NativeModuleCompiler.CreateReference(producer), policy);
        Assert.False(client.HasErrors, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(client.Program),
            producer, nativePolicy);
        Assert.Equal(41, await NativeModuleCompiler.RunAsync(consumer, producer,
            TimeSpan.FromSeconds(10), nativePolicy).ConfigureAwait(true));

        var changed = Compilation.CreateLibrary(SyntaxTree.Parse(
            ProducerSource.Replace("new Token(id)", "new Token(id + 1)", StringComparison.Ordinal)), policy);
        Assert.False(changed.HasErrors, string.Join(Environment.NewLine, changed.Diagnostics));
        var replacement = NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(changed.Program),
            ["Make", "Token.Close"], "external_cleanup", nativePolicy);
        Assert.Equal(42, await NativeModuleCompiler.RunAsync(consumer, replacement,
            TimeSpan.FromSeconds(10), nativePolicy).ConfigureAwait(true));
    }

    [Fact]
    public async Task UniqueCleanupContractSurvivesSeparateNativeModulesAsync()
    {
        const string ProviderSource = """
            public unique struct Token {
                public int32 Id;
                public Token(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            public Token Make(int32 id) { return new Token(id); }
            """;
        var producerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(ProviderSource));
        Assert.False(producerCompilation.HasErrors, string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(producerCompilation.Program),
            ["Make", "Token.Close"], "unique_owner", NativeRuntimePolicy.FailFast);
        var reference = NativeModuleCompiler.CreateReference(producer);
        var client = Compilation.Create(SyntaxTree.Parse("int32 Main() { Token token = Make(42); return token.Id; }"), reference);
        Assert.False(client.HasErrors, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(client.Program), producer,
            NativeRuntimePolicy.FailFast);
        Assert.Equal(42, await NativeModuleCompiler.RunAsync(consumer, producer, TimeSpan.FromSeconds(10),
            NativeRuntimePolicy.FailFast).ConfigureAwait(true));
        var replacementCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            ProviderSource.Replace("new Token(id);", "new Token(id + 1);", StringComparison.Ordinal)));
        Assert.False(replacementCompilation.HasErrors);
        var replacement = NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(replacementCompilation.Program),
            ["Make", "Token.Close"], "unique_owner", NativeRuntimePolicy.FailFast);
        Assert.Equal(43, await NativeModuleCompiler.RunAsync(consumer, replacement, TimeSpan.FromSeconds(10),
            NativeRuntimePolicy.FailFast).ConfigureAwait(true));

        var incompatibleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            ProviderSource.Replace("Close()", "Release()", StringComparison.Ordinal)));
        Assert.False(incompatibleCompilation.HasErrors);
        var incompatible = NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(incompatibleCompilation.Program),
            ["Make", "Token.Release"], "unique_owner", NativeRuntimePolicy.FailFast);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.Validate(consumer, incompatible));
    }

    private static async Task<int> RunNativeAsync(string source, BoundRuntimePolicy policy, int[]? trace = null)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            [new HostBuffer("Trace", PrimitiveType.Int32, trace is null ? [0, 0] : [.. trace], true)], policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        var image = NativeEmitter.Emit(Assert.IsType<BoundProgram>(compilation.Program), nativePolicy);
        var path = Path.Combine(Directory.GetCurrentDirectory(), $"terrum-owner-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Native process did not start.");
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
