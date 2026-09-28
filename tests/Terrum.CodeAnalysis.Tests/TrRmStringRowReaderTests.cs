// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.IO;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmStringRowReaderTests
{
    [Theory]
    [InlineData(new byte[] { 0, 6, 0, 0, 65, 110, 115, 119, 101, 114, 0, 0 }, "Answer")]
    [InlineData(new byte[] { 0, 1, 0, 0, 65, 0, 0, 0 }, "A")]
    [InlineData(new byte[] { 1, 2, 0, 0, 195, 169, 0, 0 }, "\u00E9")]
    [InlineData(new byte[] { 2, 2, 0, 0, 0x3D, 0xD8, 0, 0xDE, 0, 0, 0, 0 }, "\uD83D\uDE00")]
    [InlineData(new byte[] { 3, 1, 0, 0, 233, 0, 0, 0, 0, 0, 0, 0 }, "\u00E9")]
    public void DecodesValidatedNameRows(byte[] row, string expected)
    {
        Assert.Equal(expected, TrRmStringRowReader.DecodeName(row));
    }

    [Fact]
    public void NameIdentityIsIndependentOfRowEncoding()
    {
        var ascii = TrRmStringRowReader.DecodeName([0, 1, 0, 0, 65, 0, 0, 0]);
        var utf8 = TrRmStringRowReader.DecodeName([1, 1, 0, 0, 65, 0, 0, 0]);
        var utf16 = TrRmStringRowReader.DecodeName([2, 1, 0, 0, 65, 0, 0, 0]);
        var utf32 = TrRmStringRowReader.DecodeName([3, 1, 0, 0, 65, 0, 0, 0, 0, 0, 0, 0]);
        Assert.Equal(ascii, utf8);
        Assert.Equal(ascii, utf16);
        Assert.Equal(ascii, utf32);
    }

    [Fact]
    public void EmptyNamespaceRemainsDistinctFromARequiredName()
    {
        byte[] empty = [0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Equal("", TrRmStringRowReader.DecodeNamespace(empty));
        Assert.Throws<InvalidDataException>(() => TrRmStringRowReader.DecodeName(empty));
        Assert.Throws<InvalidDataException>(() =>
            TrRmStringRowReader.DecodeNamespace([0, 1, 0, 0, 0, 0, 0, 0]));
    }

    [Theory]
    [InlineData(new byte[] { 0, 1, 0, 0, 65, 0, 0 })]
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 4, 1, 0, 0, 65, 0, 0, 0 })]
    [InlineData(new byte[] { 0, 255, 255, 255, 65, 0, 0, 0 })]
    [InlineData(new byte[] { 0, 1, 0, 0, 65, 0, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0, 1, 0, 0, 65, 1, 0, 0 })]
    [InlineData(new byte[] { 0, 1, 0, 0, 65, 0, 1, 0 })]
    [InlineData(new byte[] { 0, 1, 0, 0, 128, 0, 0, 0 })]
    [InlineData(new byte[] { 0, 1, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 1, 2, 0, 0, 0xC0, 0x80, 0, 0 })]
    [InlineData(new byte[] { 2, 1, 0, 0, 0, 0xD8, 0, 0 })]
    [InlineData(new byte[] { 3, 1, 0, 0, 0, 0, 0x11, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 3, 1, 0, 0, 0, 0xD8, 0, 0, 0, 0, 0 })]
    public void RejectsMalformedAndNonNameRows(byte[] row)
    {
        Assert.Throws<InvalidDataException>(() => TrRmStringRowReader.DecodeName(row));
    }
}
