// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Text;

public sealed class SourceTextTests
{
    [Test]
    public void TracksEveryLineBreakAndUtf16Offset()
    {
        var source = SourceText.From("😀\r\nx\ry\nz\u0085q\u2028r\u2029s", "file.terrum");
        Assert.That(source.Path, Is.EqualTo("file.terrum"));
        Assert.That(source.Lines.Length, Is.EqualTo(7));
        Assert.That(source.Lines[0], Is.EqualTo(new SourceLine(new TextSpan(0, 2), new TextSpan(0, 4))));
        Assert.That(source.Lines[1], Is.EqualTo(new SourceLine(new TextSpan(4, 1), new TextSpan(4, 2))));
        Assert.That(source.GetLinePosition(2), Is.EqualTo(new LinePosition(0, 2)));
        Assert.That(source.GetLinePosition(3), Is.EqualTo(new LinePosition(0, 3)));
        Assert.That(source.GetLinePosition(4), Is.EqualTo(new LinePosition(1, 0)));
        Assert.That(source.GetLinePosition(source.Length), Is.EqualTo(new LinePosition(6, 1)));
        Assert.That(source.GetSubText(new TextSpan(0, 2)), Is.EqualTo("😀"));
    }

    [Test]
    public void TrailingBreakCreatesEmptyFinalLine()
    {
        var source = SourceText.From("a\n");
        Assert.That(source.Lines.Length, Is.EqualTo(2));
        Assert.That(source.Lines[1], Is.EqualTo(new SourceLine(new TextSpan(2, 0), new TextSpan(2, 0))));
        Assert.That(source.GetLinePosition(2), Is.EqualTo(new LinePosition(1, 0)));
    }

    [Test]
    public void CountsLargeCrlfInputsBeforeBuildingLines()
    {
        const int terminatedLineCount = 4096;
        var source = SourceText.From(string.Concat(Enumerable.Repeat("x\r\n", terminatedLineCount)));

        Assert.That(source.Lines.Length, Is.EqualTo(terminatedLineCount + 1));
        Assert.That(source.Lines[^1], Is.EqualTo(new SourceLine(new TextSpan(terminatedLineCount * 3, 0),
                                    new TextSpan(terminatedLineCount * 3, 0))));
    }
    [TestCase("")]
    [TestCase("a\r\nb\rc\nd\u0085e\u2028f\u2029")]
    [TestCase("\r\r\n\n\r")]
    [TestCase("\uD800\r\n\uDC00\0\uD800\uDC00")]
    public void LineOffsetsRoundTripThroughBreaksAndUtf16Units(string text)
    {
        var source = SourceText.From(text);
        for (var position = 0; position <= source.Length; position++)
        {
            var location = source.GetLinePosition(position);
            Assert.That(source.TryGetOffset(location.Line, location.Character, out var offset), Is.True);
            Assert.That(offset, Is.EqualTo(position));
        }

        Assert.That(source.TryGetOffset(-1, 0, out _), Is.False);
        Assert.That(source.TryGetOffset(source.Lines.Length, 0, out _), Is.False);
        Assert.That(source.TryGetOffset(0, -1, out _), Is.False);
        Assert.That(source.TryGetOffset(0, int.MaxValue, out _), Is.False);
        for (var line = 0; line < source.Lines.Length; line++)
        {
            var end = source.Lines[line].SpanIncludingLineBreak.Length;
            Assert.That(source.TryGetOffset(line, end, out var offset), Is.EqualTo(line == source.Lines.Length - 1));
            if (line == source.Lines.Length - 1)
            {
                Assert.That(offset, Is.EqualTo(source.Length));
            }
        }
    }

    [Test]
    public void RetainsUnpairedSurrogates()
    {
        var source = SourceText.From("\uD800\uDC00\uD800");
        Assert.That(source.Length, Is.EqualTo(3));
        Assert.That(source[2], Is.EqualTo('\uD800'));
        Assert.That(source.GetSubText(new TextSpan(2, 1)), Is.EqualTo("\uD800"));
    }

    [Test]
    public void RejectsInvalidArguments()
    {
        _ = Assert.Throws<ArgumentNullException>(() => SourceText.From(null!))!;
        _ = Assert.Throws<ArgumentNullException>(() => SourceText.From("", null!))!;
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new TextSpan(-1, 0))!;
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new TextSpan(0, -1))!;
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new TextSpan(int.MaxValue, 1))!;
        var source = SourceText.From("a");
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => source.GetLinePosition(-1))!;
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => source.GetLinePosition(2))!;
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => source.GetSubText(new TextSpan(1, 1)))!;
    }
}
