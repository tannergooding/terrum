// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class SourceTextTests
{
    [Fact]
    public void TracksEveryLineBreakAndUtf16Offset()
    {
        var source = SourceText.From("😀\r\nx\ry\nz\u0085q\u2028r\u2029s", "file.terrum");
        Assert.Equal("file.terrum", source.Path);
        Assert.Equal(7, source.Lines.Length);
        Assert.Equal(new SourceLine(new TextSpan(0, 2), new TextSpan(0, 4)), source.Lines[0]);
        Assert.Equal(new SourceLine(new TextSpan(4, 1), new TextSpan(4, 2)), source.Lines[1]);
        Assert.Equal(new LinePosition(0, 2), source.GetLinePosition(2));
        Assert.Equal(new LinePosition(0, 3), source.GetLinePosition(3));
        Assert.Equal(new LinePosition(1, 0), source.GetLinePosition(4));
        Assert.Equal(new LinePosition(6, 1), source.GetLinePosition(source.Length));
        Assert.Equal("😀", source.GetSubText(new TextSpan(0, 2)));
    }

    [Fact]
    public void TrailingBreakCreatesEmptyFinalLine()
    {
        var source = SourceText.From("a\n");
        Assert.Equal(2, source.Lines.Length);
        Assert.Equal(new SourceLine(new TextSpan(2, 0), new TextSpan(2, 0)), source.Lines[1]);
        Assert.Equal(new LinePosition(1, 0), source.GetLinePosition(2));
    }

    [Fact]
    public void RetainsUnpairedSurrogates()
    {
        var source = SourceText.From("\uD800\uDC00\uD800");
        Assert.Equal(3, source.Length);
        Assert.Equal('\uD800', source[2]);
        Assert.Equal("\uD800", source.GetSubText(new TextSpan(2, 1)));
    }

    [Fact]
    public void RejectsInvalidArguments()
    {
        _ = Assert.Throws<ArgumentNullException>(() => SourceText.From(null!));
        _ = Assert.Throws<ArgumentNullException>(() => SourceText.From("", null!));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new TextSpan(-1, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new TextSpan(0, -1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new TextSpan(int.MaxValue, 1));
        var source = SourceText.From("a");
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => source.GetLinePosition(-1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => source.GetLinePosition(2));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => source.GetSubText(new TextSpan(1, 1)));
    }
}
