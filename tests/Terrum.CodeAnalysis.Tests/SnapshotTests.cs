// Copyright (c) Tanner Gooding and Contributors. Licensed under the MIT License.

using System;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class SnapshotTests
{
    [Fact]
    public void AppliesAnAtomicOrderedEditBatchWithoutMutatingOriginal()
    {
        SyntaxTree original = SyntaxTree.Parse("void F() { return; }", "source.terrum");
        SyntaxTree changed = original.WithChanges(original.ContentId,
        [
            new TextChange(new TextSpan(5, 1), "Main"),
            new TextChange(new TextSpan(11, 7), "Terminal.WriteLine(\"hello\");"),
        ]);
        Assert.Equal("void F() { return; }", original.Root.ToFullString());
        Assert.Equal("void Main() { Terminal.WriteLine(\"hello\"); }", changed.Root.ToFullString());
        Assert.Equal(original.Source.Path, changed.Source.Path);
        Assert.False(changed.HasErrors);
        Assert.NotEqual(original.ContentId, changed.ContentId);
    }

    [Fact]
    public void RejectsStaleOverlappingAndOutOfRangeChanges()
    {
        SyntaxTree tree = SyntaxTree.Parse("void F() {}");
        Assert.Throws<InvalidOperationException>(() => tree.WithChanges("stale", []));
        Assert.Throws<ArgumentException>(() => tree.WithChanges(tree.ContentId,
            [new TextChange(new TextSpan(1, 3), ""), new TextChange(new TextSpan(2, 0), "x")]));
        Assert.Throws<ArgumentException>(() => tree.WithChanges(tree.ContentId,
            [new TextChange(new TextSpan(0, 0), "a"), new TextChange(new TextSpan(0, 0), "b")]));
        Assert.Throws<ArgumentException>(() => tree.WithChanges(tree.ContentId, [new TextChange(new TextSpan(50, 0), "")]));
        Assert.Equal("void F() {}", tree.Root.ToFullString());
    }

    [Fact]
    public void ContentIdentityIsDeterministicAndPreservesMalformedCodeUnits()
    {
        Assert.Equal(SyntaxTree.Parse("a").ContentId, SyntaxTree.Parse("a", "other-path").ContentId);
        Assert.NotEqual(SyntaxTree.Parse("\uD800").ContentId, SyntaxTree.Parse("\uD801").ContentId);
        Assert.NotEqual(SyntaxTree.Parse("\uD800").ContentId, SyntaxTree.Parse("\uFFFD").ContentId);
        SyntaxTree tree = SyntaxTree.Parse("void F() {}");
        Assert.Equal(tree.ContentId, tree.WithChanges(tree.ContentId, []).ContentId);
    }
}
