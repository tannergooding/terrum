// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionalCycleSampleAgreesBetweenAotAndJitAsync(bool checkedFailures)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "samples", "native",
            "managed-optional-cycle.terrum")).ConfigureAwait(true);
        if (checkedFailures)
        {
            await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
        }
        else
        {
            await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ConstructorCompletesOptionalFieldsBeforeSelfCycleAsync(bool checkedFailures)
    {
        const string Source = """
            mut class Cycle {
                public int32 Value;
                public mut write Cycle? Next;

                public Cycle(int32 value) {
                    Value = value;
                    Next = null;
                    Next = this;
                }
            }
            int32 Main() {
                {
                    require Cycle.TryCreate(42, out var root);
                    if (ManagedHeap.Collect() != 0) { return 1; }
                    require root.Next is some self;
                    if (self.Value != 42) { return 2; }
                }
                if (ManagedHeap.Collect() != 1) { return 3; }
                return 42;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NullableConstructorArgumentSurvivesFactoryAndDetachmentAsync(bool checkedFailures)
    {
        const string Source = """
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            mut class Host {
                public mut Leaf? Edge;
                public Host(Leaf? edge) { Edge = edge; }
            }
            int32 Main() {
                require Host.TryCreate(null, out var empty);
                if (!(empty.Edge is null)) { return 1; }
                {
                    require Leaf.TryCreate(42, out var leaf);
                    require Host.TryCreate(leaf, out var linked);
                    if (linked.Edge is some snapshot) {
                        linked.Edge = null;
                        if (ManagedHeap.Collect() != 0) { return 2; }
                        if (snapshot.Value != 42) { return 3; }
                    } else { return 4; }
                }
                if (ManagedHeap.Collect() != 2) { return 5; }
                return 42;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkedTokenCandidateRejectsInvalidMutationsAndCollectsClearedLinksAsync(bool checkedFailures)
    {
        string[] components = [
            Path.Combine(RepositoryRoot(), "self-host", "SourceRange.terrum"),
            Path.Combine(RepositoryRoot(), "self-host", "Token.terrum"),
            Path.Combine(RepositoryRoot(), "self-host", "ManagedTokenChain.terrum"),
        ];
        const string Source = """
            using Terrum.CodeAnalysis;
            int32 Main() {
                require ManagedTokenChain.TryCreate(out var chain);
                if (chain.GetCount() != 0 || chain.GetVersion() != 0 || chain.TryClear()) { return 1; }
                require chain.TryAppend(1, 0, 3);
                require chain.TryAppend(7, 42, 5);
                if (chain.TryAppend(-1, 43, 1) || chain.TryAppend(9, 41, 1)
                    || chain.TryAppend(9, 43, -1) || chain.TryAppend(9, int32.MaxValue, 1)) { return 2; }
                if (chain.TryGet(-1, out var invalid)) { return 3; }
                require chain.TryGet(1, out var token);
                if (token.Kind != 7 || token.Range.Start != 42 || token.Range.Length != 5
                    || token.Range.GetEnd() != 47 || chain.GetCount() != 2
                    || chain.GetVersion() != 2) { return 4; }
                require chain.TryClear();
                if (chain.GetCount() != 0 || chain.GetVersion() != 3 || chain.TryClear()) { return 5; }
                if (ManagedHeap.Collect() != 2) { return 6; }
                if (token.Kind != 7 || token.Range.Start != 42 || token.Range.Length != 5) { return 10; }
                for (mut int32 i = 0; i < 64; i = i + 1) {
                    require chain.TryAppend(i, i, 1);
                }
                if (chain.TryAppend(64, 64, 1) || chain.GetCount() != 64
                    || chain.GetVersion() != 67) { return 7; }
                require chain.TryGet(63, out var last);
                if (last.Kind != 63 || last.Range.Start != 63 || last.Range.Length != 1) { return 8; }
                require chain.TryClear();
                if (ManagedHeap.Collect() != 64) { return 9; }
                return 42;
            }
            """;
        if (checkedFailures)
        {
            await AssertCheckedExecutionAsync(Source, 42, components).ConfigureAwait(true);
        }
        else
        {
            await AssertFailFastExecutionAsync(Source, 42, components).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkedTokenCopiesMatchHostMetadataAfterCollectionAsync(bool checkedFailures)
    {
        string[] components = [
            Path.Combine(RepositoryRoot(), "self-host", "SourceRange.terrum"),
            Path.Combine(RepositoryRoot(), "self-host", "Token.terrum"),
            Path.Combine(RepositoryRoot(), "self-host", "ManagedTokenChain.terrum"),
        ];
        var lexical = Lexer.Lex(SourceText.From("// header\r\nint32 \U00010400() { return 42; }"));
        Assert.Empty(lexical.Diagnostics);
        var source = new StringBuilder("""
            using Terrum.CodeAnalysis;
            int32 Main() {
                require ManagedTokenChain.TryCreate(out var chain);
            """);
        foreach (var token in lexical.Tokens)
        {
            _ = source.Append(CultureInfo.InvariantCulture,
                $"require chain.TryAppend({(int)token.Kind}, {token.Span.Start}, {token.Span.Length});");
        }
        for (var index = 0; index < lexical.Tokens.Length; index++)
        {
            _ = source.Append(CultureInfo.InvariantCulture, $"require chain.TryGet({index}, out var copy{index});");
        }
        _ = source.Append(CultureInfo.InvariantCulture,
            $"require chain.TryClear(); require ManagedHeap.Collect() == {lexical.Tokens.Length};");
        for (var index = 0; index < lexical.Tokens.Length; index++)
        {
            var token = lexical.Tokens[index];
            _ = source.Append(CultureInfo.InvariantCulture,
                $"require copy{index}.Kind == {(int)token.Kind} && copy{index}.Range.Start == {token.Span.Start} && copy{index}.Range.Length == {token.Span.Length} && copy{index}.Range.GetEnd() == {token.Span.End};");
        }
        _ = source.Append("return 42; }");
        if (checkedFailures)
        {
            await AssertCheckedExecutionAsync(source.ToString(), 42, components).ConfigureAwait(true);
        }
        else
        {
            await AssertFailFastExecutionAsync(source.ToString(), 42, components).ConfigureAwait(true);
        }
    }

    [Fact]
    public Task CheckedFailureAbandonsPartiallyInitializedOptionalEdgesAsync()
    {
        const string Source = """
            class Envelope {
                public ManagedNode? Edge;
                public int32 Value;

                public Envelope(bool fail) {
                    Edge = null;
                    require ManagedNode.TryCreate(7, out var child);
                    Edge = child;
                    Value = 42;
                    require !fail;
                }
            }
            int32 Main() {
                unsafe {
                    try {
                        if (Envelope.TryCreate(true, out var abandoned)) { return 1; }
                        return 2;
                    } catch {
                        if (ManagedHeap.Collect() != 1) { return 3; }
                        require Envelope.TryCreate(false, out var complete);
                        if (complete.Edge is some snapshot) {
                            if (ManagedHeap.Collect() != 0) { return 4; }
                            return snapshot.Value + 35;
                        }
                        return 5;
                    }
                }
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }
}
