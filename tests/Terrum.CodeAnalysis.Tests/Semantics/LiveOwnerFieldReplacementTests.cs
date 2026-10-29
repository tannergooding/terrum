// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Semantics;

public sealed class LiveOwnerFieldReplacementTests
{
    private const string Token = """
        unique struct Token {
            public int32 Id;
            public Token(int32 id) { Id = id; }
            [Cleanup] public void Close() { Host.Trace[0] = Id; }
        }
        """;

    private const string Owner = """
        unique struct Owner {
            public mut Token Current;
            public mut int32 Sibling;
            public Owner(Token current, int32 sibling) {
                Current = move current;
                Sibling = sibling;
            }
        }
        """;

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public Task ReplacesLiveFieldAndPreservesSiblingReferenceAsync(BoundRuntimePolicy policy) =>
        RunAsync(Token + Owner + """
            int32 Main() {
                mut Owner owner = new Owner(new Token(40), int32(7));
                mut int32 siblingValue = int32(0);
                {
                    ref<int32> sibling = ref owner.Sibling;
                    owner.Current = new Token(42);
                    siblingValue = *sibling;
                    sibling = _;
                }
                owner = _;
                if (siblingValue != 7 || Host.Trace[0] != 42) { return 2; }
                return 42;
            }
            """, policy, 42);

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public Task ReplacesFieldWithAuthoredParentCleanupAsync(BoundRuntimePolicy policy) =>
        RunAsync(Token + """
            unique struct Owner {
                public mut Token Current;
                public Owner(Token current) { Current = move current; }
                [Cleanup] public void Close() { Host.Trace[1] = 1; }
            }
            int32 Main() {
                mut Owner owner = new Owner(new Token(40));
                owner.Current = new Token(42);
                if (Host.Trace[0] != 40) { return 1; }
                owner = _;
                if (Host.Trace[0] != 42 || Host.Trace[1] != 1) { return 2; }
                return 42;
            }
            """, policy, 42);

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public Task ReplacesLiveFieldDuringConstructionAsync(BoundRuntimePolicy policy) =>
        RunAsync(Token + """
            unique struct Owner {
                public mut Token Current;
                public Owner(Token initial, Token replacement) {
                    Current = move initial;
                    Current = move replacement;
                }
            }
            int32 Main() {
                mut Owner owner = new Owner(new Token(40), new Token(42));
                if (Host.Trace[0] != 40 || owner.Current.Id != 42) { return 1; }
                owner = _;
                if (Host.Trace[0] != 42) { return 2; }
                return 42;
            }
            """, policy, 42);

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public Task RechecksMaybeLiveFieldStateAtCommitAsync(BoundRuntimePolicy policy) =>
        RunAsync(Token + Owner + """
            int32 Replace(bool moveFirst) {
                mut Owner owner = new Owner(new Token(40), int32(7));
                if (moveFirst) {
                    Token extracted = move owner.Current;
                }
                owner.Current = new Token(42);
                int32 result = owner.Current.Id;
                owner = _;
                return result;
            }
            int32 Main() { return Replace(false) + Replace(true); }
            """, policy, 84);

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public Task ReplacesUniqueFieldThroughWritableReferenceAsync(
        BoundRuntimePolicy policy) =>
        RunAsync(Token + """
            unique struct Owner {
                public mut Token Current;
                public Owner(Token current) { Current = move current; }
            }
            int32 Main() {
                mut Owner owner = new Owner(new Token(1));
                {
                    write ref<Token> current = ref owner.Current;
                    Token replacement = new Token(2);
                    *current = move replacement;
                    if (Host.Trace[0] != 1) { return 1; }
                    Token next = new Token(3);
                    *current = move next;
                    if (Host.Trace[0] != 2) { return 1; }
                }
                return 42;
            }
            """, policy, 42);

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public Task ReplacesWholeOwnerThroughWritableReferenceAsync(
        BoundRuntimePolicy policy) =>
        RunAsync(Token + """
            unique struct Owner {
                public mut Token Current;
                public Owner(Token current) { Current = move current; }
            }
            int32 Main() {
                mut Owner owner = new Owner(new Token(1));
                {
                    write ref<Owner> destination = ref owner;
                    *destination = new Owner(new Token(2));
                    if (Host.Trace[0] != 1) { return 1; }
                    *destination = new Owner(new Token(3));
                    if (Host.Trace[0] != 2) { return 2; }
                    destination = _;
                }
                if (owner.Current.Id != 3) { return 3; }
                owner = _;
                return Host.Trace[0] == 3 ? 42 : 0;
            }
            """, policy, 42);

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public Task ReplacesUniqueFieldThroughWritableFormalAsync(
        BoundRuntimePolicy policy) =>
        RunAsync(Token + """
            unique struct Owner {
                public mut Token Current;
                public Owner(Token current) { Current = move current; }
            }
            void Replace(write ref<Token> destination, Token replacement) {
                *destination = move replacement;
            }
            int32 Main() {
                mut Owner owner = new Owner(new Token(1));
                write ref<Token> currentField = ref owner.Current;
                Replace(ref owner.Current, new Token(2));
                if (Host.Trace[0] != 1 || owner.Current.Id != 2) { return 1; }
                *currentField = new Token(3);
                if (Host.Trace[0] != 2 || owner.Current.Id != 3) { return 2; }
                currentField = _;
                owner = _;
                return Host.Trace[0] == 3 ? 42 : 0;
            }
            """, policy, 42);

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public Task ReplacesLiveFieldThroughReferenceInPartialOwnerAsync(
        BoundRuntimePolicy policy) =>
        RunAsync(Token + """
            unique struct Owner {
                public mut Token First;
                public mut Token Second;
                public Owner(Token first, Token second) {
                    First = move first;
                    Second = move second;
                }
            }
            int32 Main() {
                mut Owner owner = new Owner(new Token(1), new Token(2));
                Token extracted = move owner.First;
                {
                    write ref<Token> second = ref owner.Second;
                    *second = new Token(3);
                    if (Host.Trace[0] != 2 || owner.Second.Id != 3) { return 1; }
                    second = _;
                }
                owner = _;
                return Host.Trace[0] == 3 ? 42 : 0;
            }
            """, policy, 42);

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public Task ReplacesWholeOwnerThroughWritableFormalAsync(
        BoundRuntimePolicy policy) =>
        RunAsync(Token + """
            void Replace(write ref<Token> destination, Token replacement) {
                *destination = move replacement;
            }
            int32 Main() {
                mut Token owner = new Token(1);
                Replace(ref owner, new Token(2));
                if (Host.Trace[0] != 1 || owner.Id != 2) { return 1; }
                owner = _;
                return Host.Trace[0] == 2 ? 42 : 0;
            }
            """, policy, 42);

    [Test]
    public Task RightHandSideFailurePreservesLiveFieldAsync() =>
        RunAsync(Token + Owner + """
            Token Create(bool fail) {
                if (fail) { require false; }
                return new Token(42);
            }
            int32 Main() {
                mut Owner owner = new Owner(new Token(40), int32(7));
                unsafe {
                    try {
                        owner.Current = Create(true);
                    } catch {
                        if (owner.Current.Id == 40 && Host.Trace[0] == 0) { return 42; }
                    }
                }
                return 0;
            }
            """, BoundRuntimePolicy.CheckedExceptions, 42);

    [Test]
    public Task RightHandSideFailurePreservesFieldThroughWritableReferenceAsync() =>
        RunAsync(Token + Owner + """
            Token Create(bool fail) {
                if (fail) { require false; }
                return new Token(42);
            }
            int32 Main() {
                mut Owner owner = new Owner(new Token(40), int32(7));
                unsafe {
                    try {
                        write ref<Token> current = ref owner.Current;
                        *current = Create(true);
                    } catch {
                        if (owner.Current.Id == 40 && Host.Trace[0] == 0) { return 42; }
                    }
                }
                return 0;
            }
            """, BoundRuntimePolicy.CheckedExceptions, 42);

    [Test]
    public Task ReferenceArgumentFailurePreservesOwnerBeforeStoreAsync() =>
        RunAsync(Token + Owner + """
            Token Create(bool fail) {
                if (fail) { require false; }
                return new Token(42);
            }
            void Replace(write ref<Token> destination, Token replacement) {
                *destination = move replacement;
            }
            int32 Main() {
                mut Owner owner = new Owner(new Token(40), int32(7));
                unsafe {
                    try {
                        Replace(ref owner.Current, Create(true));
                    } catch {
                        if (owner.Current.Id == 40 && Host.Trace[0] == 0) { return 42; }
                    }
                }
                return 0;
            }
            """, BoundRuntimePolicy.CheckedExceptions, 42);

    [Test]
    public Task FailureAfterReferenceStoreKeepsReplacementLiveAsync() =>
        RunAsync(Token + Owner + """
            void ReplaceThenFail(write ref<Token> destination, Token replacement) {
                *destination = move replacement;
                require false;
            }
            int32 Main() {
                mut Owner owner = new Owner(new Token(40), int32(7));
                unsafe {
                    try {
                        ReplaceThenFail(ref owner.Current, new Token(42));
                    } catch {
                        if (owner.Current.Id == 42 && Host.Trace[0] == 40) { return 42; }
                    }
                }
                return 0;
            }
            """, BoundRuntimePolicy.CheckedExceptions, 42);

    [Test]
    public void AdmitsReplacementWhenCleanupBodyIsClosedAndEmpty()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Token {
                public int32 Id;
                public Token(int32 id) { Id = id; }
                [Cleanup] public void Close() {}
            }
            unique struct Owner {
                public mut Token Current;
                public Owner(Token current) { Current = move current; }
            }
            int32 Main() {
                mut Owner owner = new Owner(new Token(40));
                owner.Current = new Token(42);
                return owner.Current.Id;
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [TestCase("""
        [Cleanup] public void Close() { require false; }
        """)]
    [TestCase("""
        [Cleanup] public void Close() { Notify(); }
        """)]
    public void RejectsUnprovedOrReentrantOldValueCleanup(string cleanup)
    {
        var source = $$"""
            unique struct Token {
                public int32 Id;
                public Token(int32 id) { Id = id; }
                {{cleanup}}
            }
            unique struct Owner {
                public mut Token Current;
                public Owner(Token current) { Current = move current; }
            }
            void Notify() {}
            int32 Main() {
                mut Owner owner = new Owner(new Token(40));
                owner.Current = new Token(42);
                return owner.Current.Id;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0, 0], true)]);

        Assert.That(compilation.HasErrors, Is.True);
    }

    [TestCase("Token replacement")]
    [TestCase("move owner.Current")]
    public void RejectsCopyOrSelfMoveThroughWritableOwnerReference(string value)
    {
        var source = Token + """
            unique struct Owner {
                public mut Token Current;
                public Owner(Token current) { Current = move current; }
            }
            int32 Main() {
                mut Owner owner = new Owner(new Token(40));
                write ref<Token> current = ref owner.Current;
                Token replacement = new Token(42);
                *current = VALUE;
                return 0;
            }
            """.Replace("VALUE", value, StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void RejectsOwnerStoreWhenOldCleanupMayFail()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Token {
                public int32 Id;
                public Token(int32 id) { Id = id; }
                [Cleanup] public void Close() { require false; }
            }
            unique struct Owner {
                public mut Token Current;
                public Owner(Token current) { Current = move current; }
            }
            int32 Main() {
                mut Owner owner = new Owner(new Token(40));
                write ref<Token> current = ref owner.Current;
                *current = new Token(42);
                return 0;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsOwnerStoreCallWhileNestedReferenceIsLive()
    {
        var source = Token + Owner + """
            void Replace(write ref<Token> destination, Token replacement) {
                *destination = move replacement;
            }
            int32 Main() {
                mut Owner owner = new Owner(new Token(40), int32(7));
                ref<int32> nested = ref owner.Current.Id;
                Replace(ref owner.Current, new Token(42));
                return *nested;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0, 0], true)]);

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsOwnerStoreCallWhenAnotherArgumentBorrowsItsNestedValue()
    {
        var source = Token + Owner + """
            void Replace(write ref<Owner> destination,
                write ref<Token> nested, Owner replacement) {
                *destination = move replacement;
            }
            int32 Main() {
                mut Owner owner = new Owner(new Token(40), int32(7));
                Replace(ref owner, ref owner.Current, new Owner(new Token(42), int32(8)));
                return 0;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0, 0], true)]);

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsReferenceFormationForVacantUniqueField()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Token + """
            unique struct Owner {
                public mut Token First;
                public mut Token Second;
                public Owner(Token first, Token second) {
                    First = move first;
                    Second = move second;
                }
            }
            int32 Main() {
                mut Owner owner = new Owner(new Token(1), new Token(2));
                Token extracted = move owner.First;
                write ref<Token> vacant = ref owner.First;
                return 0;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsWholeOwnerReferenceWhenOwnerIsPartial()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Token + """
            unique struct Owner {
                public mut Token First;
                public mut Token Second;
                public Owner(Token first, Token second) {
                    First = move first;
                    Second = move second;
                }
            }
            int32 Main() {
                mut Owner owner = new Owner(new Token(1), new Token(2));
                Token extracted = move owner.First;
                write ref<Owner> whole = ref owner;
                return 0;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public Task RetiresNestedOldFieldsAfterCleanupBodyAsync() =>
        RunAsync("""
            unique struct Token {
                public int32 Id;
                public Token(int32 id) { Id = id; }
                [Cleanup] public void Close() { Host.Trace[0] = Id; }
            }
            unique struct Nested {
                public mut Token Leaf;
                public Nested(Token leaf) { Leaf = move leaf; }
                [Cleanup] public void Close() {
                    if (Host.Trace[0] != 0) { Host.Trace[1] = 100; }
                }
            }
            unique struct Owner {
                public mut Nested Current;
                public Owner(Nested current) { Current = move current; }
            }
            int32 Main() {
                mut Owner owner = new Owner(new Nested(new Token(40)));
                owner.Current = new Nested(new Token(42));
                if (Host.Trace[0] != 40 || Host.Trace[1] != 0) { return 1; }
                Host.Trace[0] = 0;
                owner = _;
                if (Host.Trace[0] != 42 || Host.Trace[1] != 0) { return 2; }
                return 42;
            }
            """, BoundRuntimePolicy.CheckedExceptions, 42);

    private static async Task RunAsync(
        string source, BoundRuntimePolicy policy, int expected)
    {
        Assert.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0, 0], true)], policy);
        var diagnosticSource = compilation.Diagnostics.IsEmpty ? string.Empty
            : source.Substring(compilation.Diagnostics[0].Span.Start,
                compilation.Diagnostics[0].Span.Length);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics)
                + Environment.NewLine + diagnosticSource);
        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        var image = NativeEmitter.Emit(compilation.Program!, nativePolicy);
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"terrum-live-field-replacement-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Native process did not start.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(true);
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
            Assert.That(process.ExitCode, Is.EqualTo(expected));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }
}
