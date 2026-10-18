// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmStringRowReaderTests
{
    [TestCase(new byte[] { 0, 6, 0, 0, 65, 110, 115, 119, 101, 114, 0, 0 },"Answer")]
    [TestCase(new byte[] { 0, 1, 0, 0, 65, 0, 0, 0 },"A")]
    [TestCase(new byte[] { 1, 2, 0, 0, 195, 169, 0, 0 },"\u00E9")]
    [TestCase(new byte[] { 2, 2, 0, 0, 0x3D, 0xD8, 0, 0xDE, 0, 0, 0, 0 },"\uD83D\uDE00")]
    [TestCase(new byte[] { 3, 1, 0, 0, 233, 0, 0, 0, 0, 0, 0, 0 },"\u00E9")]
    public void DecodesValidatedNameRows(byte[] row, string expected)
    {
        Assert.That(TrRmStringRowReader.DecodeName(row), Is.EqualTo(expected));
    }

    [Test]
    public void NameIdentityIsIndependentOfRowEncoding()
    {
        var ascii = TrRmStringRowReader.DecodeName([0, 1, 0, 0, 65, 0, 0, 0]);
        var utf8 = TrRmStringRowReader.DecodeName([1, 1, 0, 0, 65, 0, 0, 0]);
        var utf16 = TrRmStringRowReader.DecodeName([2, 1, 0, 0, 65, 0, 0, 0]);
        var utf32 = TrRmStringRowReader.DecodeName([3, 1, 0, 0, 65, 0, 0, 0, 0, 0, 0, 0]);
        Assert.That(utf8, Is.EqualTo(ascii));
        Assert.That(utf16, Is.EqualTo(ascii));
        Assert.That(utf32, Is.EqualTo(ascii));
    }

    [Test]
    public void EmptyNamespaceRemainsDistinctFromARequiredName()
    {
        byte[] empty = [0, 0, 0, 0, 0, 0, 0, 0];
        Assert.That(TrRmStringRowReader.DecodeNamespace(empty), Is.EqualTo(""));
        Assert.Throws<InvalidDataException>(() => TrRmStringRowReader.DecodeName(empty));
        Assert.Throws<InvalidDataException>(() =>
            TrRmStringRowReader.DecodeNamespace([0, 1, 0, 0, 0, 0, 0, 0]));
    }
    [TestCase(new byte[] { 0, 1, 0, 0, 65, 0, 0 })]
    [TestCase(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 })]
    [TestCase(new byte[] { 4, 1, 0, 0, 65, 0, 0, 0 })]
    [TestCase(new byte[] { 0, 255, 255, 255, 65, 0, 0, 0 })]
    [TestCase(new byte[] { 0, 1, 0, 0, 65, 0, 0, 0, 0, 0, 0, 0 })]
    [TestCase(new byte[] { 0, 1, 0, 0, 65, 1, 0, 0 })]
    [TestCase(new byte[] { 0, 1, 0, 0, 65, 0, 1, 0 })]
    [TestCase(new byte[] { 0, 1, 0, 0, 128, 0, 0, 0 })]
    [TestCase(new byte[] { 0, 1, 0, 0, 0, 0, 0, 0 })]
    [TestCase(new byte[] { 1, 2, 0, 0, 0xC0, 0x80, 0, 0 })]
    [TestCase(new byte[] { 2, 1, 0, 0, 0, 0xD8, 0, 0 })]
    [TestCase(new byte[] { 3, 1, 0, 0, 0, 0, 0x11, 0, 0, 0, 0, 0 })]
    [TestCase(new byte[] { 3, 1, 0, 0, 0, 0xD8, 0, 0, 0, 0, 0 })]
    public void RejectsMalformedAndNonNameRows(byte[] row)
    {
        Assert.Throws<InvalidDataException>(() => TrRmStringRowReader.DecodeName(row));
    }
}
