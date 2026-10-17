// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class OwnershipTests
{
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task LifetimeEndDropsEachValueExactlyOnceAtTheStatementAsync(BoundRuntimePolicy policy)
    {
        const string Source = """
            unique struct Owner {
                public int32 Id;
                public Owner(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {
                    if (Host.Trace[0] == 0) { Host.Trace[0] = 1; }
                    else if (Host.Trace[0] == 1) { Host.Trace[0] = 2; }
                    else if (Host.Trace[0] == 2) { Host.Trace[0] = 3; }
                    else if (Host.Trace[0] == 3) { Host.Trace[0] = 4; }
                    else if (Host.Trace[0] == 4) { Host.Trace[0] = 5; }
                    else { Host.Trace[0] = 6; }
                    Host.Trace[1] = Host.Trace[1] ^ Id;
                }
            }
            int32 Main() {
                {
                    Owner immutable = new Owner(1);
                    immutable = _;
                    if (Host.Trace[0] != 1 || Host.Trace[1] != 1) { return 1; }
                    init Owner reusable;
                    reusable = new Owner(2);
                    reusable = _;
                    if (Host.Trace[0] != 2 || Host.Trace[1] != 3) { return 2; }
                    reusable = new Owner(3);
                    mut Owner mutable = new Owner(4);
                    mutable = _;
                    mutable = new Owner(5);
                    mutable = _;
                    if (Host.Trace[0] != 4 || Host.Trace[1] != 2) { return 3; }
                }
                if (Host.Trace[0] != 5 || Host.Trace[1] != 1) { return 4; }
                return 42;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0, 0], true)], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(),
            (NativeRuntimePolicy)policy), Is.EqualTo(42));
        Assert.That(await RunNativeAsync(Source, policy, [0, 0]).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public async Task LifetimeEndCleanupIsNotRepeatedByFinallyOrExceptionalUnwindAsync()
    {
        const string Source = """
            unique struct Owner {
                public int32 Id;
                public Owner(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {
                    if (Host.Trace[0] == 0) { Host.Trace[0] = 1; }
                    else { Host.Trace[0] = 2; }
                    Host.Trace[1] = Host.Trace[1] ^ Id;
                }
            }
            Owner Fail() { require false; return new Owner(99); }
            int32 Main() {
                unsafe {
                    try {
                        init Owner owner;
                        owner = new Owner(1);
                        try { owner = _; }
                        finally { if (Host.Trace[0] != 1) { Host.Trace[1] = 100; } }
                        owner = Fail();
                    }
                    catch {
                        if (Host.Trace[0] != 1 || Host.Trace[1] != 1) { return 1; }
                    }
                }
                if (Host.Trace[0] != 1 || Host.Trace[1] != 1) { return 2; }
                return 42;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0, 0], true)], BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(),
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
        Assert.That(await RunNativeAsync(Source, BoundRuntimePolicy.CheckedExceptions, [0, 0]).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task StandaloneVacantOwnerDropsOnlyItsPublishedValueAsync(BoundRuntimePolicy policy)
    {
        const string Source = """
            unique struct Owner {
                public int32 Number;
                public Owner(int32 number) { Number = number; }
                [NoThrow] [Cleanup] public void Close() {
                    if (Host.Trace[0] == 0) { Host.Trace[0] = 1; }
                    else { Host.Trace[0] = 2; }
                    Host.Trace[1] = Number;
                }
            }
            bool Create(bool publish, init ref<Owner> result) {
                if (!publish) { return false; }
                result = new Owner(42);
                return true;
            }
            int32 Main() {
                {
                    init var owner;
                    if (Create(false, init owner)) { return 1; }
                    if (Host.Trace[0] != 0) { return 2; }
                    require Create(true, init owner);
                    if (owner.Number != 42 || Host.Trace[0] != 0) { return 3; }
                }
                if (Host.Trace[0] != 1 || Host.Trace[1] != 42) { return 4; }
                return 42;
            }
            """;
        Assert.That(await RunNativeAsync(Source, policy, [0, 0]).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task InitReferencePublishesOneOwnerAndDropsOnlyTheCompletedValueAsync(BoundRuntimePolicy policy)
    {
        const string Source = """
            unique struct Owner {
                public int32 Number;
                public Owner(int32 number) { Number = number; }
                [NoThrow] [Cleanup] public void Close() {
                    if (Host.Trace[0] == 0) { Host.Trace[0] = 1; }
                    else { Host.Trace[0] = 2; }
                    Host.Trace[1] = Number;
                }
            }
            bool Create(bool publish, init ref<Owner> result) {
                result = new Owner(42);
                return publish;
            }
            int32 Main() {
                {
                    if (Create(false, init var owner)) { return 1; }
                    if (Host.Trace[0] != 1 || Host.Trace[1] != 42) { return 2; }
                    require Create(true, init owner);
                    if (owner.Number != 42 || Host.Trace[0] != 1) { return 3; }
                }
                if (Host.Trace[0] != 2 || Host.Trace[1] != 42) { return 4; }
                return 42;
            }
            """;
        Assert.That(await RunNativeAsync(Source, policy, [0, 0]).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task DiscardedMoveDropsAtStatementBoundaryExactlyOnceAsync(BoundRuntimePolicy policy)
    {
        const string Source = """
            unique struct Owner {
                public int32 Id;
                public Owner(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {
                    if (Host.Trace[0] == 0) { Host.Trace[0] = 1; }
                    else if (Host.Trace[0] == 1) { Host.Trace[0] = 2; }
                    else { Host.Trace[0] = 3; }
                    Host.Trace[1] = Id;
                }
            }
            int32 Main() {
                {
                    Owner owner = new Owner(40);
                    _ = move owner;
                    if (Host.Trace[0] != 1 || Host.Trace[1] != 40) { return 0; }
                }
                if (Host.Trace[0] != 1 || Host.Trace[1] != 40) { return 0; }
                _ = new Owner(2);
                if (Host.Trace[0] != 2 || Host.Trace[1] != 2) { return 0; }
                return 42;
            }
            """;
        Assert.That(await RunNativeAsync(Source, policy, [0, 0]).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task MutatedBufferFieldRetainsOwnerAndDropsExactlyOnceAsync(BoundRuntimePolicy policy)
    {
        const string Source = """
            unique struct Owner {
                public mut OwnedInt32Buffer Words;
                public mut int32 Latest;
                public Owner(OwnedInt32Buffer words) { Words = move words; Latest = 0; }
                public mut void Update() {
                    Words.Set(0, 42);
                    Latest = 42;
                }
                [NoThrow] [Cleanup] public void Close() {
                    if (Host.Trace[0] == 0) { Host.Trace[0] = 1; }
                    else { Host.Trace[0] = 2; }
                    Host.Trace[1] = Latest;
                    Host.Trace[2] = Words.Count;
                }
            }
            int32 Main() {
                {
                    require OwnedInt32Buffer.TryCreate(1, out var raw);
                    mut Owner owner = new Owner(move raw);
                    require owner.Words.TryAppend(1);
                    owner.Update();
                    if (owner.Words.Read(0) != 42 || Host.Trace[0] != 0) { return 0; }
                }
                if (Host.Trace[0] == 1 && Host.Trace[1] == 42 && Host.Trace[2] == 1) {
                    return 42;
                }
                return 0;
            }
            """;
        Assert.That(await RunNativeAsync(Source, policy, [0, 0, 0]).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public async Task CaughtFailureThroughUniqueChildPreservesWritesAndRecursiveCleanupAsync()
    {
        const string Source = """
            unique struct Child {
                public mut int32 Value;
                public Child(int32 value) { Value = value; }
                public mut void Fail() { Value = 42; require false; }
                [NoThrow] [Cleanup] public void Close() {
                    if (Host.Trace[0] == 0) { Host.Trace[0] = 1; }
                    else { Host.Trace[0] = 2; }
                    Host.Trace[2] = Value;
                }
            }
            unique struct Owner {
                public mut Child Nested;
                public Owner(int32 value) { Nested = new Child(value); }
                [NoThrow] [Cleanup] public void Close() {
                    if (Host.Trace[1] == 0) { Host.Trace[1] = 1; }
                    else { Host.Trace[1] = 2; }
                    Host.Trace[3] = Nested.Value;
                }
            }
            int32 Main() {
                {
                    mut Owner owner = new Owner(1);
                    unsafe {
                        try { owner.Nested.Fail(); }
                        catch { if (owner.Nested.Value != 42) { return 1; } }
                    }
                    if (Host.Trace[0] != 0 || Host.Trace[1] != 0) { return 2; }
                }
                if (Host.Trace[0] == 1 && Host.Trace[1] == 1
                    && Host.Trace[2] == 42 && Host.Trace[3] == 42) { return 42; }
                return 0;
            }
            """;
        Assert.That(await RunNativeAsync(Source, BoundRuntimePolicy.CheckedExceptions,
            [0, 0, 0, 0]).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task MutatedUniqueOwnerCleansUpExactlyOnceWithUpdatedValueAsync(BoundRuntimePolicy policy)
    {
        const string Source = """
            unique struct Owner {
                public mut int32 Value;
                public Owner(int32 value) { Value = value; }
                public mut void Add(int32 amount) { Value = Value + amount; }
                [NoThrow] [Cleanup] public void Close() {
                    if (Host.Trace[0] == 0) { Host.Trace[0] = 1; }
                    else { Host.Trace[0] = 2; }
                    Host.Trace[1] = Value;
                }
            }
            int32 Main() {
                {
                    mut Owner owner = new Owner(40);
                    owner.Add(2);
                    if (Host.Trace[0] != 0) { return 1; }
                }
                if (Host.Trace[0] == 1 && Host.Trace[1] == 42) { return 42; }
                return 0;
            }
            """;
        Assert.That(await RunNativeAsync(Source, policy).ConfigureAwait(true), Is.EqualTo(42));
    }

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
    [TestCase("Owner copy = value;","TRM3023")]
    [TestCase("Owner copy = move value; Owner duplicate = move value;","TRM3023")]
    [TestCase("Owner copy = move value; return value.Id;","TRM3023")]
    [TestCase("if (flag) { Owner gone = move value; } return value.Id;","TRM3023")]
    [TestCase("value.Close();","TRM3023")]
    [TestCase("return new Owner(42).Id;","TRM3023")]
    public void UniqueOwnersCannotBeCopiedReadVacantOrManuallyCleaned(string body, string code)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Owner
            + " int32 Main() { bool flag = true; Owner value = new Owner(42); " + body + " return 0; }"),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0, 0], true)]);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == code),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("struct Copy { public int32 Id; public Copy() { Id = 1; } [NoThrow] [Cleanup] public void Close() {} }","TRM3023")]
    [TestCase("unique struct U { public int32 Id; public U() { Id = 1; } [Cleanup] public void Close() {} }","TRM3023")]
    [TestCase("unique struct U { public int32 Id; public U() { Id = 1; } } struct Copy { public U Value; public Copy() { Value = new U(); } }","TRM3023")]
    [TestCase("unique struct U { public int32 Id; public U() { Id = 1; U duplicate = this; } }","TRM3023")]
    public void UnsupportedOwnershipContractsAreDiagnosed(string declarations, string code)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(declarations + " int32 Main() { return 0; }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == code),Is.True);
    }
    [TestCase("Owner first = new Owner(1); Owner second = new Owner(2);",3,1)]
    [TestCase("Owner first = new Owner(1); Owner second = move first;",1,1)]
    [TestCase("mut Owner first = new Owner(1); first = new Owner(2);",3,2)]
    public async Task NormalCleanupRunsOnceInReverseLexicalOrderAsync(string body, int mask, int last)
    {
        var source = Owner + " int32 Main() { { " + body
            + " } if (Host.Trace[0] == " + mask + " && Host.Trace[1] == " + last + ") { return 42; } return 0; }";
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
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
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
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
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
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
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(true)]
    [TestCase(false)]
    public async Task BranchMoveRetainsExactlyOneCleanupObligationAsync(bool move)
    {
        var source = Owner + " int32 Main() { bool choose = " + (move ? "true" : "false") + ";"
            + " { Owner value = new Owner(1); if (choose) { Owner destination = move value; } }"
            + " if (Host.Trace[0] == 1 && Host.Trace[1] == 1) { return 42; } return 0; }";
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
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
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
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
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
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
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
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
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
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
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
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
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
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
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
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
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3023"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    private const string NestedReceiver = """
        unique struct Leaf {
            public int32 Id;
            public Leaf() { Id = 42; }
            [NoThrow] [Cleanup] public void Close() {}
            public int32 Read(int32 offset) { return Id + offset; }
        }
        unique struct Branch {
            public Leaf Leaf;
            public Branch() { Leaf = new Leaf(); }
            [NoThrow] [Cleanup] public void Close() {}
        }
        unique struct Root {
            public Branch Branch;
            public Root() { Branch = new Branch(); }
            [NoThrow] [Cleanup] public void Close() {}
        }
        bool Replace(out Root replacement) { replacement = new Root(); return true; }
        bool ReplaceMoved(Root old, out Root replacement) {
            replacement = new Root();
            return true;
        }
        int32 Observe(bool value) { if (value) { return 0; } return 0; }
        """;
    [TestCase("root.Branch.Leaf.Read(Observe(Replace(out root)))")]
    [TestCase("root.Branch.Leaf.Read(Observe(ReplaceMoved(move root, out root)))")]
    public void NestedFieldLoanReservesOriginalRootAcrossScalarArguments(string expression)
    {
        var source = NestedReceiver + " int32 Main() { mut Root root = new Root(); return "
            + expression + "; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3023"
            && diagnostic.Span.Start == source.LastIndexOf("Observe(", StringComparison.Ordinal)),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase(false)]
    [TestCase(true)]
    public void PublicIrRejectsNestedFieldLoanRootReplacementDuringScalarArgument(bool moveFirst)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(NestedReceiver
            + " int32 Main() { mut Root root = new Root(); return root.Branch.Leaf.Read(Observe(true)); }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
        var entry = program.Functions[program.EntryPoint];
        var local = ((global::System.Func<BoundLocal>)(() => { var __nunitActual3 = entry.Body.Statements[0]; Assert.That(__nunitActual3, Is.TypeOf<BoundLocal>()); return (BoundLocal)__nunitActual3!; }))();
        var region = ((global::System.Func<BoundTryFinally>)(() => { var __nunitActual4 = entry.Body.Statements[1]; Assert.That(__nunitActual4, Is.TypeOf<BoundTryFinally>()); return (BoundTryFinally)__nunitActual4!; }))();
        var result = ((global::System.Func<BoundReturn>)(() => { var __nunitActual5 = global::System.Linq.Enumerable.Single(region.Body.Statements); Assert.That(__nunitActual5, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual5!; }))();
        var call = ((global::System.Func<BoundCall>)(() => { var __nunitActual6 = result.Value; Assert.That(__nunitActual6, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual6!; }))();
        _ = ((global::System.Func<BoundFieldLoan>)(() => { var __nunitActual7 = call.Arguments[0]; Assert.That(__nunitActual7, Is.TypeOf<BoundFieldLoan>()); return (BoundFieldLoan)__nunitActual7!; }))();
        var scalar = ((global::System.Func<BoundCall>)(() => { var __nunitActual8 = call.Arguments[1]; Assert.That(__nunitActual8, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual8!; }))();
        var replacement = program.Functions.IndexOf(global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == (moveFirst ? "ReplaceMoved" : "Replace")));
        ImmutableArray<BoundExpression> arguments = [new BoundOutArgument(
            local.Variable, false, local.Initializer.Type, scalar.Span)];
        if (moveFirst)
        {
            arguments = [new BoundMove(local.Variable, local.Initializer.Type, scalar.Span), .. arguments];
        }
        var invalidating = new BoundCall(replacement, arguments, PrimitiveType.Bool, scalar.Span);
        var forgedScalar = scalar with { Arguments = [invalidating] };
        var forgedCall = call with { Arguments = call.Arguments.SetItem(1, forgedScalar) };
        var forgedReturn = result with { Value = forgedCall };
        var forgedRegion = region with { Body = region.Body with { Statements = [forgedReturn] } };
        var forged = program with {
            Functions = program.Functions.SetItem(program.EntryPoint, entry with {
                Body = entry.Body with { Statements = entry.Body.Statements.SetItem(1, forgedRegion) },
            }),
        };
        var error = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged))!;
        Assert.That(error.Data["Terrum.InvalidEffect"], Is.EqualTo("Ownership"));
        Assert.That(error.Data["Terrum.SourceSpan"], Is.EqualTo(scalar.Span));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast))!;
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NestedFieldLoanAllowsCompletedReadonlyQueryAsync(BoundRuntimePolicy policy)
    {
        var source = NestedReceiver + """
            int32 Main() {
                Root root = new Root();
                return root.Branch.Leaf.Read(root.Branch.Leaf.Read(0) - 42);
            }
            """;
        Assert.That(await RunNativeAsync(source, policy).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
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
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
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
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
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
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
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
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
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
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions,
            [0, 0, 0]).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase("Make(out owner, out owner);")]
    [TestCase("Take(move owner, move owner);")]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3023"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
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
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase("output.Id")]
    [TestCase("output.Peek()")]
    public async Task DirectOwningOutputReadsItsValueInsideCalleeAsync(string read)
    {
        var source = Owner + " bool Fill(out Owner output) { output = new Owner(42); return "
            + read + " == 42; } int32 Main() { require Fill(out var value); return value.Id; }";
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase("output.Third")]
    [TestCase("output.Peek()")]
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
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
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
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
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
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3023"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3023"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = compilation.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))();
        var entry = program.Functions[program.EntryPoint];
        var truncated = entry with { Body = entry.Body with { Statements = entry.Body.Statements.RemoveAt(1) } };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, truncated),
        }))!;
        var earlyDrop = entry with {
            Body = entry.Body with {
                Statements = entry.Body.Statements.Add(new BoundAssignment(0,
                new BoundCall(0, [new BoundConstant(1, PrimitiveType.Int32, default)],
                    (PrimitiveType)100, default), default)),
            }
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, earlyDrop),
        }))!;
        var forged = entry with {
            Body = entry.Body with {
                Statements = entry.Body.Statements.SetItem(0, new BoundLocal(0,
                new BoundReceiverLoan(0, (PrimitiveType)100, default), default)),
            }
        };
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, forged),
        }, NativeRuntimePolicy.FailFast))!;
        var unboundCleanup = program with {
            Structures = program.Structures.SetItem(0, program.Structures[0] with { CleanupMethod = "Owner.Missing" }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(unboundCleanup))!;
    }
    [TestCase("then")]
    [TestCase("else")]
    [TestCase("while")]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = compilation.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))();
        var entry = program.Functions[program.EntryPoint];
        var local = ((global::System.Func<BoundLocal>)(() => { var __nunitActual11 = entry.Body.Statements[0]; Assert.That(__nunitActual11, Is.TypeOf<BoundLocal>()); return (BoundLocal)__nunitActual11!; }))();
        var region = ((global::System.Func<BoundTryFinally>)(() => { var __nunitActual12 = entry.Body.Statements[1]; Assert.That(__nunitActual12, Is.TypeOf<BoundTryFinally>()); return (BoundTryFinally)__nunitActual12!; }))();
        var result = global::System.Linq.Enumerable.Single(((global::System.Func<BoundBlock>)(() => { var __nunitActual13 = region.Body; Assert.That(__nunitActual13, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual13!; }))().Statements);
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
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast))!;
    }

    [Test]
    public void PublicIrCannotUseAnotherStructuresCleanupMethod()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public int32 Id;
                public Owner(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            unique struct Other {
                public int32 Id;
                public Other(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            int32 Main() { return 0; }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual14 = compilation.Program; Assert.That(__nunitActual14, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual14!; }))();
        var ownerIndex = -1;
        for (var index = 0; index < program.Structures.Length; index++)
        {
            if (program.Structures[index].Name == "Owner")
            {
                ownerIndex = index;
                break;
            }
        }
        Assert.That(ownerIndex >= 0, Is.True);
        var malformed = program with {
            Structures = program.Structures.SetItem(ownerIndex,
                program.Structures[ownerIndex] with { CleanupMethod = "Other.Close" }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(malformed))!;
    }
    [TestCase(false)]
    [TestCase(true)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual15 = compilation.Program; Assert.That(__nunitActual15, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual15!; }))();
        var index = -1;
        for (var i = 0; i < program.Functions.Length; i++)
        {
            if (program.Functions[i].Name == (isOut ? "Owner.Ready" : "Owner.Peek"))
            {
                index = i;
                break;
            }
        }
        Assert.That(index >= 0, Is.True);
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
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast))!;
    }
    [TestCase(false)]
    [TestCase(true)]
    public void PublicIrCannotOmitOrUnconditionallyDropOwningParameter(bool output)
    {
        var source = Owner + (output
            ? " bool Take(out Owner value) { value = new Owner(42); return true; }"
            : " void Take(Owner value) {}")
            + " int32 Main() { return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0, 0], true)]);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual16 = compilation.Program; Assert.That(__nunitActual16, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual16!; }))();
        var index = -1;
        for (var i = 0; i < program.Functions.Length; i++)
        {
            if (program.Functions[i].Name == "Take")
            {
                index = i;
                break;
            }
        }
        Assert.That(index >= 0, Is.True);
        var function = program.Functions[index];
        var region = ((global::System.Func<BoundTryFinally>)(() => { var __nunitActual17 = global::System.Linq.Enumerable.Single(function.Body.Statements); Assert.That(__nunitActual17, Is.TypeOf<BoundTryFinally>()); return (BoundTryFinally)__nunitActual17!; }))();
        var unscoped = program with {
            Functions = program.Functions.SetItem(index, function with { Body = region.Body }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(unscoped))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(unscoped, NativeRuntimePolicy.FailFast))!;

        var drop = ((global::System.Func<BoundDrop>)(() => { var __nunitActual18 = global::System.Linq.Enumerable.Single(region.Finally.Statements); Assert.That(__nunitActual18, Is.TypeOf<BoundDrop>()); return (BoundDrop)__nunitActual18!; }))();
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
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged))!;
    }

    [Test]
    public async Task UniqueByValueContractSurvivesSeparateNativeModulesAsync()
    {
        var providerSource = """
            [Blittable] [Layout(LayoutKind.Sequential)] public unique struct Token {
                public int32 Id;
                public Token(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            public Token Make(int32 id) { return new Token(id); }
            public int32 Take(Token token) { return token.Id; }
            """;
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(providerSource));
        Assert.That(providerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual19 = providerCompilation.Program; Assert.That(__nunitActual19, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual19!; }))(),
            ["Make", "Take", "Token.Close"], "owned_param", NativeRuntimePolicy.FailFast);
        var reference = NativeModuleCompiler.CreateReference(provider);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Token token = Make(42); return Take(move token); }"), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual20 = consumerCompilation.Program; Assert.That(__nunitActual20, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual20!; }))(),
            provider, NativeRuntimePolicy.FailFast);
        Assert.That(NativeModuleCompiler.Inspect(consumer).NativeProfileVersion, Is.EqualTo("native-13"));
        Assert.That(await NativeModuleCompiler.RunAsync(consumer, provider, TimeSpan.FromSeconds(10),
            NativeRuntimePolicy.FailFast).ConfigureAwait(true), Is.EqualTo(42));

        var changedCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(providerSource.Replace(
            "return token.Id;", "return token.Id + 1;", StringComparison.Ordinal)));
        Assert.That(changedCompilation.HasErrors, Is.False);
        var replacement = NativeModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual21 = changedCompilation.Program; Assert.That(__nunitActual21, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual21!; }))(),
            ["Make", "Take", "Token.Close"], "owned_param", NativeRuntimePolicy.FailFast);
        Assert.That(await NativeModuleCompiler.RunAsync(consumer, replacement, TimeSpan.FromSeconds(10),
            NativeRuntimePolicy.FailFast).ConfigureAwait(true), Is.EqualTo(43));
    }

    [Test]
    public async Task ConditionalOwningOutputContractSurvivesSeparateModulesAsync()
    {
        const string ProviderSource = """
            [Blittable] [Layout(LayoutKind.Sequential)] public unique struct Token {
                public int32 Id;
                public Token(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            public bool Make(out Token token) { token = new Token(42); return true; }
            """;
        var firstCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(ProviderSource));
        Assert.That(firstCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, firstCompilation.Diagnostics));
        var first = ((global::System.Func<BoundProgram>)(() => { var __nunitActual22 = firstCompilation.Program; Assert.That(__nunitActual22, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual22!; }))();
        var semanticProducer = ModuleCompiler.CreateProducer(first, ["Make", "Token.Close"]);
        var semanticConsumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { require Make(out var ticket); return ticket.Id; }"),
            ModuleCompiler.CreateReference(semanticProducer));
        Assert.That(semanticConsumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, semanticConsumerCompilation.Diagnostics));
        var client = ((global::System.Func<BoundProgram>)(() => { var __nunitActual23 = semanticConsumerCompilation.Program; Assert.That(__nunitActual23, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual23!; }))();
        var semanticConsumer = ModuleCompiler.CreateConsumer(client, semanticProducer);
        Assert.That(ModuleCompiler.GetNativeProfileVersion(semanticConsumer, true), Is.EqualTo("native-13"));
        var nativeProducer = NativeModuleCompiler.CreateProducer(first,
            ["Make", "Token.Close"], "owned_output", NativeRuntimePolicy.FailFast);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(client, nativeProducer,
            NativeRuntimePolicy.FailFast);
        Assert.That(await NativeModuleCompiler.RunAsync(nativeConsumer, nativeProducer,
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast).ConfigureAwait(true), Is.EqualTo(42));

        var changedCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            ProviderSource.Replace("new Token(42)", "new Token(43)", StringComparison.Ordinal)));
        Assert.That(changedCompilation.HasErrors, Is.False);
        var changed = ((global::System.Func<BoundProgram>)(() => { var __nunitActual24 = changedCompilation.Program; Assert.That(__nunitActual24, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual24!; }))();
        _ = ModuleCompiler.Link(semanticConsumer, ModuleCompiler.CreateProducer(changed,
            ["Make", "Token.Close"]));
        var replacement = NativeModuleCompiler.CreateProducer(changed,
            ["Make", "Token.Close"], "owned_output", NativeRuntimePolicy.FailFast);
        Assert.That(await NativeModuleCompiler.RunAsync(nativeConsumer, replacement,
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast).ConfigureAwait(true), Is.EqualTo(43));

        var incompatibleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            ProviderSource.Replace("out Token token) { token = new Token(42); return true;",
                "Token token) { return true;", StringComparison.Ordinal)));
        Assert.That(incompatibleCompilation.HasErrors, Is.False);
        var incompatible = ((global::System.Func<BoundProgram>)(() => { var __nunitActual25 = incompatibleCompilation.Program; Assert.That(__nunitActual25, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual25!; }))();
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(semanticConsumer,
            ModuleCompiler.CreateProducer(incompatible, ["Make", "Token.Close"])))!;
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.Validate(nativeConsumer,
            NativeModuleCompiler.CreateProducer(incompatible, ["Make", "Token.Close"],
                "owned_output", NativeRuntimePolicy.FailFast)))!;
    }
    [TestCase(false,BoundRuntimePolicy.FailFast)]
    [TestCase(false,BoundRuntimePolicy.CheckedExceptions)]
    [TestCase(true,BoundRuntimePolicy.CheckedExceptions)]
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

    [Test]
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

    [Test]
    public void OwnershipTransferProfileRejectsUnknownRuntimePolicy()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Compilation.GetNativeProfileVersion((BoundRuntimePolicy)int.MaxValue,
                uniqueValues: true, ownershipTransfer: true))!;
    }

    [Test]
    public void MalformedUnexportedParameterTypeProducesModuleFormatError()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Box {
                public int32 Id;
                public Box(int32 id) { Id = id; }
            }
            int32 Use(Box input) { return input.Id; }
            public int32 Value() { Box box = new Box(42); return Use(box); }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual26 = compilation.Program; Assert.That(__nunitActual26, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual26!; }))(), ["Value"]);
        var forged = (byte[])artifact.Clone();
        var contractType = forged.AsSpan().IndexOf("in:Box:E"u8);
        Assert.That(contractType >= 0, Is.True);
        "10000"u8.CopyTo(forged.AsSpan(contractType + 3, 5));
        var variableName = forged.AsSpan().LastIndexOf("input"u8);
        Assert.That(variableName >= 4, Is.True);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(forged.AsSpan(variableName - 4, 4)), Is.EqualTo(5));
        var type = variableName + 5;
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(forged.AsSpan(type, 4)), Is.EqualTo(100));
        BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(type, 4), 10000);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forged))!;
    }

    [Test]
    public void ConsumerImportCannotDowngradeOwningParameterProfile()
    {
        const string ProviderSource = """
            [Blittable] [Layout(LayoutKind.Sequential)] public unique struct Token {
                public int32 Id;
                public Token(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            public Token Make(int32 id) { return new Token(id); }
            public int32 Take(Token token) { return token.Id; }
            """;
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(ProviderSource));
        Assert.That(providerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual27 = providerCompilation.Program; Assert.That(__nunitActual27, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual27!; }))(),
            ["Make", "Take", "Token.Close"]);
        var client = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Token token = Make(42); return Take(move token); }"),
            ModuleCompiler.CreateReference(provider));
        Assert.That(client.HasErrors, Is.False, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual28 = client.Program; Assert.That(__nunitActual28, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual28!; }))(), provider);
        Assert.That(ModuleCompiler.GetNativeProfileVersion(consumer, true), Is.EqualTo("native-13"));
        var forged = (byte[])consumer.Clone();
        var importName = forged.AsSpan().IndexOf("Take"u8);
        Assert.That(importName >= 4, Is.True);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(forged.AsSpan(importName - 4, 4)), Is.EqualTo(4));
        var contract = importName + 4;
        var marker = contract + 4 + BinaryPrimitives.ReadInt32LittleEndian(forged.AsSpan(contract, 4)) + 2;
        Assert.That(forged[marker], Is.EqualTo(1));
        forged[marker] = 0;
        var profile = forged.AsSpan().IndexOf("native-13"u8);
        Assert.That(profile >= 0, Is.True);
        forged[profile + "native-1".Length] = (byte)'2';
        Assert.That(ModuleCompiler.GetNativeProfileVersion(forged, true), Is.EqualTo("native-12"));
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(forged, provider))!;
    }

    private static async Task AssertExternalCleanupConsumerAsync(string consumerSource, BoundRuntimePolicy policy)
    {
        const string ProducerSource = """
            [Blittable] [Layout(LayoutKind.Sequential)] public unique struct Token {
                public int32 Id;
                public Token(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            public Token Make(int32 id) { return new Token(id); }
            """;
        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(ProducerSource), policy);
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = NativeModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual29 = library.Program; Assert.That(__nunitActual29, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual29!; }))(),
            ["Make", "Token.Close"], "external_cleanup", nativePolicy);
        var client = Compilation.Create(SyntaxTree.Parse(consumerSource),
            NativeModuleCompiler.CreateReference(producer), policy);
        Assert.That(client.HasErrors, Is.False, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual30 = client.Program; Assert.That(__nunitActual30, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual30!; }))(),
            producer, nativePolicy);
        Assert.That(await NativeModuleCompiler.RunAsync(consumer, producer,
            TimeSpan.FromSeconds(10), nativePolicy).ConfigureAwait(true), Is.EqualTo(41));

        var changed = Compilation.CreateLibrary(SyntaxTree.Parse(
            ProducerSource.Replace("new Token(id)", "new Token(id + 1)", StringComparison.Ordinal)), policy);
        Assert.That(changed.HasErrors, Is.False, string.Join(Environment.NewLine, changed.Diagnostics));
        var replacement = NativeModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual31 = changed.Program; Assert.That(__nunitActual31, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual31!; }))(),
            ["Make", "Token.Close"], "external_cleanup", nativePolicy);
        Assert.That(await NativeModuleCompiler.RunAsync(consumer, replacement,
            TimeSpan.FromSeconds(10), nativePolicy).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public async Task UniqueCleanupContractSurvivesSeparateNativeModulesAsync()
    {
        const string ProviderSource = """
            [Blittable] [Layout(LayoutKind.Sequential)] public unique struct Token {
                public int32 Id;
                public Token(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            public Token Make(int32 id) { return new Token(id); }
            """;
        var producerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(ProviderSource));
        Assert.That(producerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = NativeModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual32 = producerCompilation.Program; Assert.That(__nunitActual32, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual32!; }))(),
            ["Make", "Token.Close"], "unique_owner", NativeRuntimePolicy.FailFast);
        var reference = NativeModuleCompiler.CreateReference(producer);
        var client = Compilation.Create(SyntaxTree.Parse("int32 Main() { Token token = Make(42); return token.Id; }"), reference);
        Assert.That(client.HasErrors, Is.False, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual33 = client.Program; Assert.That(__nunitActual33, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual33!; }))(), producer,
            NativeRuntimePolicy.FailFast);
        Assert.That(await NativeModuleCompiler.RunAsync(consumer, producer, TimeSpan.FromSeconds(10),
            NativeRuntimePolicy.FailFast).ConfigureAwait(true), Is.EqualTo(42));
        var replacementCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            ProviderSource.Replace("new Token(id);", "new Token(id + 1);", StringComparison.Ordinal)));
        Assert.That(replacementCompilation.HasErrors, Is.False);
        var replacement = NativeModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual34 = replacementCompilation.Program; Assert.That(__nunitActual34, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual34!; }))(),
            ["Make", "Token.Close"], "unique_owner", NativeRuntimePolicy.FailFast);
        Assert.That(await NativeModuleCompiler.RunAsync(consumer, replacement, TimeSpan.FromSeconds(10),
            NativeRuntimePolicy.FailFast).ConfigureAwait(true), Is.EqualTo(43));

        var incompatibleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            ProviderSource.Replace("Close()", "Release()", StringComparison.Ordinal)));
        Assert.That(incompatibleCompilation.HasErrors, Is.False);
        var incompatible = NativeModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual35 = incompatibleCompilation.Program; Assert.That(__nunitActual35, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual35!; }))(),
            ["Make", "Token.Release"], "unique_owner", NativeRuntimePolicy.FailFast);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.Validate(consumer, incompatible))!;
    }

    private const string CompositeOwners = """
        unique struct Token {
            public int32 Id;
            public Token(int32 id) { Id = id; }
            [NoThrow] [Cleanup] public void Close() {
                Host.Trace[1] = Host.Trace[0];
                Host.Trace[0] = Id;
            }
            [NoThrow] public int32 Read() { return Id; }
        }
        unique struct Pair {
            public Token First;
            public Token Second;
            public Pair(bool complete) {
                Second = new Token(2);
                require complete;
                First = new Token(1);
                require First.Read() == 1;
            }
            [NoThrow] [Cleanup] public void Close() {
                Host.Trace[2] = First.Read();
            }
            [NoThrow] public int32 Read() { return First.Read(); }
        }
        """;
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompositeOwnerDropsParentThenReverseDeclaredChildrenAsync(BoundRuntimePolicy policy)
    {
        var source = CompositeOwners + """
            Pair Relay(Pair owner) { return move owner; }
            bool Fill(out Pair owner) { owner = Relay(new Pair(true)); return true; }
            int32 Main() {
                {
                    if (Fill(out var owner)) {
                        if (owner.Read() != 1) { return 0; }
                    }
                }
                if (Host.Trace[0] == 1 && Host.Trace[1] == 2 && Host.Trace[2] == 1) {
                    return 42;
                }
                return 0;
            }
            """;
        Assert.That(await RunNativeAsync(source, policy, [0, 0, 0]).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(BoundRuntimePolicy.FailFast,true)]
    [TestCase(BoundRuntimePolicy.FailFast,false)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions,true)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions,false)]
    public async Task ConstructorCanRequireReadonlyChildCallAfterAcquisitionAsync(
        BoundRuntimePolicy policy, bool completeParent)
    {
        var constructor = completeParent
            ? "Tail = 2; Child = new Ticket(id); require Child.Read() == id;"
            : "Child = new Ticket(id); require Child.Read() == id; Tail = 2;";
        var source = """
            unique struct Ticket {
                public int32 Id;
                [NoThrow] public Ticket(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {}
                [NoThrow] public int32 Read() { return Id; }
            }
            unique struct Bundle {
                public Ticket Child;
                public int32 Tail;
                public Bundle(int32 id) {
            """ + constructor + """
                }
                [NoThrow] public int32 ReadChild() { return Child.Read(); }
            }
            int32 Main() {
                Bundle value = new Bundle(42);
                return value.ReadChild();
            }
            """;
        Assert.That(await RunNativeAsync(source, policy).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompositeOwnersDropOnceAcrossLoopsAndConditionalMovesAsync(BoundRuntimePolicy policy)
    {
        const string Source = """
            int32 Bit(int32 id) {
                if (id == 0) { return 1; }
                if (id == 1) { return 4; }
                return 16;
            }
            unique struct Child {
                public int32 Id;
                public Child(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() { Host.Trace[0] = Host.Trace[0] ^ Id; }
            }
            unique struct Parent {
                public Child First;
                public Child Second;
                public int32 Mask;
                public Parent(int32 id) {
                    Mask = Bit(id);
                    First = new Child(Mask);
                    Second = new Child(Mask * 2);
                }
                [NoThrow] [Cleanup] public void Close() { Host.Trace[1] = Host.Trace[1] ^ Mask; }
            }
            int32 Main() {
                mut int32 iteration = 0;
                while (iteration < 3) {
                    Parent owner = new Parent(iteration);
                    if (iteration == 1) {
                        Parent moved = move owner;
                    }
                    iteration = iteration + 1;
                }
                if (Host.Trace[0] == 63 && Host.Trace[1] == 21) { return 42; }
                return 0;
            }
            """;
        Assert.That(await RunNativeAsync(Source, policy).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public async Task CompositePartialConstructorDropsOnlyAcquiredFieldsOnFailureAsync()
    {
        var source = CompositeOwners + """
            int32 Main() {
                unsafe {
                    try { Pair absent = new Pair(false); }
                    catch {
                        if (Host.Trace[0] == 2 && Host.Trace[1] == 0 && Host.Trace[2] == 0) {
                            return 42;
                        }
                    }
                }
                return 0;
            }
            """;
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions,
            [0, 0, 0]).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public async Task StraightLineUnsafeConstructorFieldAcquisitionStillUnwindsAsync()
    {
        var source = CompositeOwners.Replace("Second = new Token(2);",
            "unsafe { Second = new Token(2); }", StringComparison.Ordinal) + """
            int32 Main() {
                unsafe {
                    try { Pair absent = new Pair(false); }
                    catch {
                        if (Host.Trace[0] == 2 && Host.Trace[1] == 0 && Host.Trace[2] == 0) {
                            return 42;
                        }
                    }
                }
                return 0;
            }
            """;
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions,
            [0, 0, 0]).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public async Task CompositePartialCleanupReversesAcquisitionRatherThanFieldOrderAsync()
    {
        var source = CompositeOwners.Replace("require First.Read() == 1;",
            "require First.Read() == 0;", StringComparison.Ordinal) + """
            int32 Main() {
                unsafe {
                    try { Pair absent = new Pair(true); }
                    catch {
                        if (Host.Trace[0] == 2 && Host.Trace[1] == 1 && Host.Trace[2] == 0) {
                            return 42;
                        }
                    }
                }
                return 0;
            }
            """;
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions,
            [0, 0, 0]).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public async Task CompositeOutputFailureLeavesCallerGuardLiveAsync()
    {
        var source = CompositeOwners + """
            bool Make(bool complete, out Pair result) {
                unsafe {
                    try { result = new Pair(complete); return true; }
                    catch { return false; }
                }
            }
            int32 Main() {
                Pair guard = new Pair(true);
                bool success = Make(false, out var absent);
                if (success || guard.Read() != 1 || Host.Trace[0] != 2
                    || Host.Trace[1] != 0 || Host.Trace[2] != 0) {
                    return 0;
                }
                return 42;
            }
            """;
        Assert.That(await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions,
            [0, 0, 0]).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public async Task UnrelatedFieldWriteDoesNotAcquireParentChildAsync()
    {
        const string Source = """
            struct Scratch {
                public int32 First;
                public mut int32 Second;
                public Scratch() { First = 0; Second = 0; }
            }
            unique struct Token {
                public int32 Id;
                public Token(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() { Host.Trace[0] = 1; }
            }
            unique struct Parent {
                public Token First;
                public Token Second;
                public Parent() {
                    mut Scratch scratch = new Scratch();
                    scratch.Second = 42;
                    require false;
                    First = new Token(1);
                    Second = new Token(2);
                }
                [NoThrow] [Cleanup] public void Close() {}
            }
            int32 Main() {
                unsafe {
                    try { Parent incomplete = new Parent(); }
                    catch { if (Host.Trace[0] == 0) { return 42; } }
                }
                return 0;
            }
            """;
        Assert.That(await RunNativeAsync(Source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase("struct Copy { public Token Child; public Copy() { Child = new Token(1); } }")]
    [TestCase("unique struct Pair { public Token Child; public Pair() { Child = new Token(1); Child = new Token(2); } }")]
    [TestCase("unique struct Pair { public Token Child; public Pair() { if (true) { Child = new Token(1); } } }")]
    [TestCase("unique struct Pair { public Token Child; public Pair() { Child = new Token(1); Token stolen = move Child; } }")]
    public void UnsupportedCompositeOwnershipIsDiagnosed(string declaration)
    {
        var source = """
            unique struct Token {
                public int32 Id;
                public Token(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            """ + declaration + " int32 Main() { return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3023"),Is.True);
    }

    [Test]
    public void PublicIrCannotEraseOrReorderPartialConstructorCleanup()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(CompositeOwners
            + " int32 Main() { Pair pair = new Pair(true); return pair.Read(); }"),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0, 0, 0], true)],
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual36 = compilation.Program; Assert.That(__nunitActual36, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual36!; }))();
        var index = -1;
        for (var i = 0; i < program.Functions.Length; i++)
        {
            if (program.Functions[i].IsConstructor && program.Functions[i].DeclaringStruct == "Pair")
            {
                index = i;
                break;
            }
        }
        Assert.That(index >= 0, Is.True);
        var constructor = program.Functions[index];
        var region = ((global::System.Func<BoundTryFinally>)(() => { var __nunitActual37 = global::System.Linq.Enumerable.Single(constructor.Body.Statements); Assert.That(__nunitActual37, Is.TypeOf<BoundTryFinally>()); return (BoundTryFinally)__nunitActual37!; }))();
        var erased = program with {
            Functions = program.Functions.SetItem(index, constructor with { Body = region.Body }),
        };
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(erased, NativeRuntimePolicy.CheckedExceptions))!;

        var partial = ((global::System.Func<BoundDropConstructedFields>)(() => { var __nunitActual38 = global::System.Linq.Enumerable.Single(region.Finally.Statements); Assert.That(__nunitActual38, Is.TypeOf<BoundDropConstructedFields>()); return (BoundDropConstructedFields)__nunitActual38!; }))();
        var reordered = region with {
            Finally = region.Finally with {
                Statements = [partial with { AcquisitionOrder = [0, 1] }],
            },
        };
        var forged = program with {
            Functions = program.Functions.SetItem(index, constructor with {
                Body = constructor.Body with { Statements = [reordered] },
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(forged, NativeRuntimePolicy.CheckedExceptions))!;
    }

    [Test]
    public void PublicIrRejectsMalformedConstructorRegionsAndOwningFieldLoans()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(CompositeOwners
            + " int32 Main() { Pair pair = new Pair(true); return pair.Read(); }"),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0, 0, 0], true)],
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual39 = compilation.Program; Assert.That(__nunitActual39, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual39!; }))();
        var constructorIndex = -1;
        var methodIndex = -1;
        for (var index = 0; index < program.Functions.Length; index++)
        {
            if (program.Functions[index].IsConstructor && program.Functions[index].DeclaringStruct == "Pair")
            {
                constructorIndex = index;
            }
            if (program.Functions[index].Name == "Pair.Read")
            {
                methodIndex = index;
            }
        }
        Assert.That(constructorIndex >= 0 && methodIndex >= 0, Is.True);
        var constructor = program.Functions[constructorIndex];
        var region = ((global::System.Func<BoundTryFinally>)(() => { var __nunitActual40 = global::System.Linq.Enumerable.Single(constructor.Body.Statements); Assert.That(__nunitActual40, Is.TypeOf<BoundTryFinally>()); return (BoundTryFinally)__nunitActual40!; }))();
        Reject(constructorIndex, constructor with {
            Body = constructor.Body with { Statements = [region with { Body = null! }] },
        });
        Reject(constructorIndex, constructor with {
            Body = constructor.Body with {
                Statements = [region with {
                Body = region.Body with { Statements = default },
            }]
            },
        });

        var method = program.Functions[methodIndex];
        var result = ((global::System.Func<BoundReturn>)(() => { var __nunitActual41 = global::System.Linq.Enumerable.Single(method.Body.Statements); Assert.That(__nunitActual41, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual41!; }))();
        var call = ((global::System.Func<BoundCall>)(() => { var __nunitActual42 = result.Value; Assert.That(__nunitActual42, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual42!; }))();
        var loan = ((global::System.Func<BoundFieldLoan>)(() => { var __nunitActual43 = call.Arguments[0]; Assert.That(__nunitActual43, Is.TypeOf<BoundFieldLoan>()); return (BoundFieldLoan)__nunitActual43!; }))();
        Reject(methodIndex, method with {
            Body = method.Body with {
                Statements = [result with {
                Value = call with { Arguments = call.Arguments.SetItem(0, loan with { Place = null! }) },
            }]
            },
        });
        Reject(methodIndex, method with {
            Body = method.Body with {
                Statements = [result with {
                Value = call with { Arguments = call.Arguments.SetItem(0,
                    loan with { Place = loan.Place with { Receiver = null! } }) },
            }]
            },
        });

        void Reject(int index, BoundFunction function)
        {
            var forged = program with { Functions = program.Functions.SetItem(index, function) };
            _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(
                forged, NativeRuntimePolicy.CheckedExceptions))!;
        }
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CompositeOwnershipContractsSurviveSeparateModulesAsync(NativeRuntimePolicy policy)
    {
        const string Provider = """
            [Blittable] [Layout(LayoutKind.Sequential)] public unique struct Token {
                public int32 Id;
                public Token(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {}
                [NoThrow] public int32 Read() { return Id; }
            }
            [Blittable] [Layout(LayoutKind.Sequential)] public unique struct Pair {
                public Token Child;
                public int32 Tail;
                public Pair(int32 id) {
                    Child = new Token(id);
                    Tail = 2;
                    require Child.Read() == id;
                }
                [NoThrow] [Cleanup] public void Close() {}
                [NoThrow] public int32 Read() { return Child.Read(); }
            }
            public Pair Make(int32 id) { return new Pair(id); }
            public int32 Take(Pair pair) { return pair.Read() + pair.Tail; }
            """;
        var boundPolicy = (BoundRuntimePolicy)policy;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(Provider), boundPolicy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual44 = compilation.Program; Assert.That(__nunitActual44, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual44!; }))();
        var exports = new[] { "Token.Close", "Token.Read", "Pair.Close", "Pair.Read", "Make", "Take" };
        var semanticProducer = ModuleCompiler.CreateProducer(program, exports);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(semanticProducer.AsSpan(4, 4)), Is.EqualTo(78));
        var staleVersion = (byte[])semanticProducer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(staleVersion.AsSpan(4, 4), 13);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(staleVersion))!;
        var reference = ModuleCompiler.CreateReference(semanticProducer);
        var client = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() { Pair pair = Make(40); return Take(move pair); }
            """), reference, boundPolicy);
        Assert.That(client.HasErrors, Is.False, string.Join(Environment.NewLine, client.Diagnostics));
        var consumerProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual45 = client.Program; Assert.That(__nunitActual45, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual45!; }))();
        var semanticConsumer = ModuleCompiler.CreateConsumer(consumerProgram, semanticProducer);
        Assert.That(ModuleCompiler.GetNativeProfileVersion(semanticProducer, false), Is.EqualTo("native-14"));
        Assert.That(ModuleCompiler.GetNativeProfileVersion(semanticConsumer, true), Is.EqualTo("native-14"));
        _ = ModuleCompiler.Link(semanticConsumer, semanticProducer);
        var nativeProducer = NativeModuleCompiler.CreateProducer(program, exports, "composite_pair", policy);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(consumerProgram, nativeProducer, policy);
        Assert.That(await NativeModuleCompiler.RunAsync(nativeConsumer, nativeProducer,
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));

        var replacementSource = Provider.Replace("new Pair(id)", "new Pair(id + 1)", StringComparison.Ordinal);
        var replacementCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(replacementSource), boundPolicy);
        Assert.That(replacementCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, replacementCompilation.Diagnostics));
        var replacement = ((global::System.Func<BoundProgram>)(() => { var __nunitActual46 = replacementCompilation.Program; Assert.That(__nunitActual46, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual46!; }))();
        _ = ModuleCompiler.Link(semanticConsumer, ModuleCompiler.CreateProducer(replacement, exports));
        var nativeReplacement = NativeModuleCompiler.CreateProducer(replacement, exports, "composite_pair", policy);
        Assert.That(await NativeModuleCompiler.RunAsync(nativeConsumer, nativeReplacement,
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(43));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CompositeChildCleanupUsesItsLeafOriginAcrossNativeGraphAsync(NativeRuntimePolicy policy)
    {
        const string Leaf = """
            [Blittable] [Layout(LayoutKind.Sequential)] public unique struct Ticket {
                public int32 Value;
                public Ticket(int32 value) { Value = value; }
                [NoThrow] [Cleanup] public void Close() {}
                [NoThrow] public int32 Read() { return Value; }
            }
            public Ticket Make(int32 value) { return new Ticket(value); }
            """;
        const string Middle = """
            [Blittable] [Layout(LayoutKind.Sequential)] public unique struct Pair {
                public Ticket Child;
                public int32 Tail;
                public Pair(int32 value) { Child = Make(value); Tail = 2; }
                [NoThrow] public int32 Read() { return Child.Read(); }
            }
            public Pair Forward() { return new Pair(40); }
            """;
        var boundPolicy = (BoundRuntimePolicy)policy;
        var leafCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(Leaf), boundPolicy);
        Assert.That(leafCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, leafCompilation.Diagnostics));
        var leaf = NativeModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual47 = leafCompilation.Program; Assert.That(__nunitActual47, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual47!; }))(),
            ["Make", "Ticket.Close", "Ticket.Read"], "CompositeLeaf", policy);
        var middleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(Middle),
            NativeModuleCompiler.CreateGraphReference([leaf]), boundPolicy);
        Assert.That(middleCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, middleCompilation.Diagnostics));
        var middle = NativeModuleCompiler.CreateGraphProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual48 = middleCompilation.Program; Assert.That(__nunitActual48, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual48!; }))(),
            ["Forward", "Pair.Read"], "CompositeMiddle", [leaf], policy);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() { Pair value = Forward(); return value.Read() + value.Tail; }
            """), NativeModuleCompiler.CreateGraphReference([leaf, middle]), boundPolicy);
        Assert.That(rootCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual49 = rootCompilation.Program; Assert.That(__nunitActual49, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual49!; }))(),
            [leaf, middle], policy);
        Assert.That(NativeModuleCompiler.InspectGraphArtifact(root).NativeProfileVersion, Is.EqualTo("native-14"));
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [leaf, middle],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        var replacementCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            Leaf.Replace("new Ticket(value)", "new Ticket(value + 1)", StringComparison.Ordinal)), boundPolicy);
        Assert.That(replacementCompilation.HasErrors, Is.False);
        var replacement = NativeModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual50 = replacementCompilation.Program; Assert.That(__nunitActual50, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual50!; }))(),
            ["Make", "Ticket.Close", "Ticket.Read"], "CompositeLeaf", policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [replacement, middle],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(43));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task PrivateCompositeCleanupTargetsSurviveLibraryPruningAsync(NativeRuntimePolicy policy)
    {
        const string Provider = """
            internal unique struct Child {
                public int32 Value;
                public Child(int32 value) { Value = value; }
                [NoThrow] [Cleanup] public void Close() {}
                [NoThrow] public int32 Read() { return Value; }
            }
            internal unique struct Shell {
                public Child Value;
                public Shell(int32 value) { Value = new Child(value); }
                [NoThrow] public int32 Read() { return Value.Read(); }
            }
            public int32 Count() { Shell local = new Shell(42); return local.Read(); }
            """;
        var compiled = Compilation.CreateLibrary(SyntaxTree.Parse(Provider), (BoundRuntimePolicy)policy);
        Assert.That(compiled.HasErrors, Is.False, string.Join(Environment.NewLine, compiled.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual51 = compiled.Program; Assert.That(__nunitActual51, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual51!; }))();
        var semantic = ModuleCompiler.CreateProducer(program, ["Count"]);
        Assert.That(ModuleCompiler.GetNativeProfileVersion(semantic, false), Is.EqualTo("native-14"));
        var reference = ModuleCompiler.CreateReference(semantic);
        var client = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Count(); }"),
            reference, (BoundRuntimePolicy)policy);
        Assert.That(client.HasErrors, Is.False, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = ((global::System.Func<BoundProgram>)(() => { var __nunitActual52 = client.Program; Assert.That(__nunitActual52, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual52!; }))();
        _ = ModuleCompiler.Link(ModuleCompiler.CreateConsumer(consumer, semantic), semantic);
        var producer = NativeModuleCompiler.CreateProducer(program, ["Count"], "private_shell", policy);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(consumer, producer, policy);
        Assert.That(await NativeModuleCompiler.RunAsync(nativeConsumer, producer,
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
    }

    private static async Task<int> RunNativeAsync(string source, BoundRuntimePolicy policy, int[]? trace = null)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            [new HostBuffer("Trace", PrimitiveType.Int32, trace is null ? [0, 0] : [.. trace], true)], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        var image = NativeEmitter.Emit(((global::System.Func<BoundProgram>)(() => { var __nunitActual53 = compilation.Program; Assert.That(__nunitActual53, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual53!; }))(), nativePolicy);
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-owner-{Guid.NewGuid():N}.exe");
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
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }
}
