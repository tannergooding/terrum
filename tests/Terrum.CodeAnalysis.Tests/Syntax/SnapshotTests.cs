// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Syntax;

public sealed class SnapshotTests
{
    [Test]
    public void AppliesAnAtomicOrderedEditBatchWithoutMutatingOriginal()
    {
        var original = SyntaxTree.Parse("void F() { return; }", "source.terrum");
        var changed = original.WithChanges(original.ContentId,
        [
            new TextChange(new TextSpan(5, 1), "Main"),
            new TextChange(new TextSpan(11, 7), "Terminal.WriteLine(\"hello\");"),
        ]);
        Assert.That(original.Root.ToFullString(), Is.EqualTo("void F() { return; }"));
        Assert.That(changed.Root.ToFullString(), Is.EqualTo("void Main() { Terminal.WriteLine(\"hello\"); }"));
        Assert.That(changed.Source.Path, Is.EqualTo(original.Source.Path));
        Assert.That(changed.HasErrors, Is.False);
        Assert.That(changed.ContentId, Is.Not.EqualTo(original.ContentId));
    }

    [Test]
    public void RejectsStaleOverlappingAndOutOfRangeChanges()
    {
        var tree = SyntaxTree.Parse("void F() {}");
        _ = Assert.Throws<InvalidOperationException>(() => tree.WithChanges("stale", []))!;
        _ = Assert.Throws<ArgumentException>(() => tree.WithChanges(tree.ContentId,
            [new TextChange(new TextSpan(1, 3), ""), new TextChange(new TextSpan(2, 0), "x")]))!;
        _ = Assert.Throws<ArgumentException>(() => tree.WithChanges(tree.ContentId,
            [new TextChange(new TextSpan(0, 0), "a"), new TextChange(new TextSpan(0, 0), "b")]))!;
        _ = Assert.Throws<ArgumentException>(() => tree.WithChanges(tree.ContentId, [new TextChange(new TextSpan(50, 0), "")]))!;
        Assert.That(tree.Root.ToFullString(), Is.EqualTo("void F() {}"));
    }

    [Test]
    public void ContentIdentityIsDeterministicAndPreservesMalformedCodeUnits()
    {
        Assert.That(SyntaxTree.Parse("a", "other-path").ContentId, Is.EqualTo(SyntaxTree.Parse("a").ContentId));
        Assert.That(SyntaxTree.Parse("\uD801").ContentId, Is.Not.EqualTo(SyntaxTree.Parse("\uD800").ContentId));
        Assert.That(SyntaxTree.Parse("\uFFFD").ContentId, Is.Not.EqualTo(SyntaxTree.Parse("\uD800").ContentId));
        var tree = SyntaxTree.Parse("void F() {}");
        Assert.That(tree.WithChanges(tree.ContentId, []).ContentId, Is.EqualTo(tree.ContentId));
    }
}
