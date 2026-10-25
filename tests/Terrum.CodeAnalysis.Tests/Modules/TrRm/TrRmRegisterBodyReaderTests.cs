// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmRegisterBodyReaderTests
{
    [TestCase(0x00)]
    [TestCase(0x01)]
    [TestCase(0x02)]
    [TestCase(0x03)]
    [TestCase(0x04)]
    [TestCase(0x05)]
    [TestCase(0x06)]
    [TestCase(0x07)]
    [TestCase(0x08)]
    [TestCase(0x09)]
    [TestCase(0x0A)]
    [TestCase(0x0B)]
    [TestCase(0x0C)]
    [TestCase(0x0D)]
    [TestCase(0x0E)]
    [TestCase(0x0F)]
    [TestCase(0x10)]
    [TestCase(0x11)]
    [TestCase(0x12)]
    [TestCase(0x13)]
    [TestCase(0x14)]
    [TestCase(0x15)]
    [TestCase(0x16)]
    [TestCase(0x17)]
    [TestCase(0x18)]
    [TestCase(0x19)]
    [TestCase(0x1A)]
    [TestCase(0x1B)]
    [TestCase(0x1C)]
    [TestCase(0x1D)]
    [TestCase(0x1E)]
    [TestCase(0x1F)]
    public void AcceptsEveryMapMaskIncludingEmptyPresentMaps(byte mapMask)
    {
        var body = CreateBody(mapMask);
        var reader = new TrRmRegisterBodyReader(body);

        Assert.That(reader.MapMask, Is.EqualTo(mapMask));
        Assert.That(reader.BlockCount, Is.EqualTo(1));
        Assert.That(reader.RecordCount, Is.EqualTo(1));
        Assert.That(reader.GetRecord(0).Length, Is.EqualTo(8));
        Assert.That(reader.GetBlockCode(0).ToArray(), Is.EqualTo(new byte[] { 0 }));
    }

    [Test]
    public void AcceptsAllInstructionWidthsAndMaximumMapCount()
    {
        AssertBody(new byte[] { 0x00 });
        AssertBody(new byte[] { 0x18, 0 });
        AssertBody(new byte[] { 0xE3, 0, 0, 0 });
        AssertBody(new byte[] { 0x0A, 0x17, 0 });

        var counts = new uint[5];
        counts[1] = 65_536;
        var body = CreateBody(0x02, counts);
        var reader = new TrRmRegisterBodyReader(body);
        Assert.That(reader.TypeCount, Is.EqualTo(65_536));
        Assert.That(reader.TypeMap.Length, Is.EqualTo(65_536 * sizeof(uint)));
    }

    [Test]
    public void ValidatesShortControlFlowBlockAndRecordReferences()
    {
        var blockState = new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 };
        var edge = new byte[] { 6, 0, 0, 0, 0, 0, 0, 0 };
        var edgeRecords = new[] { blockState, edge };
        _ = new TrRmRegisterBodyReader(
            CreateBodyWithRecords(edgeRecords, code: [0xA0, 0x10]));
        _ = new TrRmRegisterBodyReader(
            CreateBodyWithRecords(edgeRecords, code: [0xB1, 0x10]));

        AssertInvalid(CreateBody(0, code: [0xB0, 1]));
        AssertInvalid(CreateBodyWithRecords(edgeRecords, code: [0xA0, 0x00]));
        AssertInvalid(CreateBodyWithRecords(edgeRecords, code: [0xA0, 0x20]));
        AssertInvalid(CreateBodyWithRecords(edgeRecords, code: [0xB1, 0x00]));

        var switchRecord = new byte[] {
            7, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        };
        var typeCounts = new uint[5];
        typeCounts[1] = 1;
        _ = new TrRmRegisterBodyReader(CreateBodyWithRecords(
            [blockState, switchRecord], 0x02, typeCounts, [0xB2, 1]));
        AssertInvalid(CreateBodyWithRecords(
            edgeRecords, code: [0xB2, 1]));

        var constructorBinding = new byte[] {
            0x0B, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        };
        var requirementFailure = new byte[] { 8, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0 };
        var callCounts = new uint[5];
        callCounts[1] = 1;
        callCounts[2] = 1;
        _ = new TrRmRegisterBodyReader(CreateBodyWithRecords(
            [blockState, constructorBinding, requirementFailure],
            0x06, callCounts, [0xB3, 2, 0x00]));
        AssertInvalid(CreateBodyWithRecords(edgeRecords, code: [0xB3, 1, 0x00]));

        _ = new TrRmRegisterBodyReader(CreateBody(0, code: [0xB5, 0]));
        _ = new TrRmRegisterBodyReader(CreateBody(0, code: [0xB7, 0]));
        AssertInvalid(CreateBody(0, code: [0xB5, 1]));
        AssertInvalid(CreateBody(0, code: [0xB7, 1]));

        var homeCounts = new uint[5];
        homeCounts[0] = 1;
        _ = new TrRmRegisterBodyReader(CreateBody(0x01, homeCounts, [0xB8, 0]));
        AssertInvalid(CreateBody(0x01, homeCounts, [0xB8, 1]));
    }

    [Test]
    public void ValidatesShortInstructionMapAndRecordIndexes()
    {
        var homeCounts = new uint[5];
        homeCounts[0] = 1;
        _ = new TrRmRegisterBodyReader(CreateBody(0x01, homeCounts, [0x05, 0x00]));
        _ = new TrRmRegisterBodyReader(
            CreateBody(0x01, homeCounts, [0x13, 0x00, 0x18, 0x00]));
        _ = new TrRmRegisterBodyReader(
            CreateBody(0x01, homeCounts, [0x14, 0x00, 0x18, 0x00]));
        _ = new TrRmRegisterBodyReader(
            CreateBody(0x01, homeCounts, [0x15, 0x00, 0x18, 0x00]));
        _ = new TrRmRegisterBodyReader(
            CreateBody(0x01, homeCounts, [0x60, 0x00, 0x18, 0x00]));

        var typeCounts = new uint[5];
        typeCounts[1] = 1;
        _ = new TrRmRegisterBodyReader(
            CreateBody(0x02, typeCounts, [0x50, 0x00, 0x18, 0x00]));

        var constantCounts = new uint[5];
        constantCounts[4] = 1;
        _ = new TrRmRegisterBodyReader(
            CreateBody(0x10, constantCounts, [0x09, 0x00]));
        _ = new TrRmRegisterBodyReader(
            CreateBody(0x10, constantCounts, [0x16, 0x00, 0x18, 0x00]));

        var methodCounts = new uint[5];
        methodCounts[2] = 1;
        _ = new TrRmRegisterBodyReader(
            CreateBody(0x04, methodCounts, [0x80, 0x00, 0x18, 0x00]));
        _ = new TrRmRegisterBodyReader(
            CreateBody(0x04, methodCounts, [0x82, 0x00, 0x18, 0x00]));

        var blockState = new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 };
        var placeSelection = new byte[] {
            4, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        };
        var placeCounts = new uint[5];
        placeCounts[0] = 1;
        placeCounts[1] = 1;
        _ = new TrRmRegisterBodyReader(CreateBodyWithRecords(
            [blockState, placeSelection], 0x03, placeCounts,
            [0x61, 0x10, 0x18, 0x00]));
        AssertInvalid(CreateBodyWithRecords(
            [blockState, placeSelection], 0x03, placeCounts,
            [0x61, 0x1E, 0x18, 0x00]));
        _ = new TrRmRegisterBodyReader(CreateBodyWithRecords(
            [blockState, placeSelection], 0x03, placeCounts,
            [0x91, 0x10, 0x18, 0x00]));
        AssertInvalid(CreateBodyWithRecords(
            [blockState, placeSelection], 0x03, placeCounts,
            [0x91, 0x1E, 0x18, 0x00]));

        var callSite = new byte[] {
            2, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        };
        var callSiteCounts = new uint[5];
        callSiteCounts[2] = 1;
        _ = new TrRmRegisterBodyReader(CreateBodyWithRecords(
            [blockState, callSite], 0x04, callSiteCounts,
            [0x90, 1, 0x18, 0x00]));

        var semanticStore = new byte[] {
            5, 0, 0, 0, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        };
        _ = new TrRmRegisterBodyReader(CreateBodyWithRecords(
            [blockState, semanticStore], code: [0x92, 1, 0x18, 0x00]));

        AssertInvalid(CreateBody(0, code: [0x05, 0x00]));
        AssertInvalid(CreateBody(0, code: [0x09, 0x00]));
        AssertInvalid(CreateBody(0, code: [0x13, 0x00, 0x18, 0x00]));
        AssertInvalid(CreateBody(0, code: [0x14, 0x00, 0x18, 0x00]));
        AssertInvalid(CreateBody(0, code: [0x16, 0x00, 0x18, 0x00]));
        AssertInvalid(CreateBody(0, code: [0x50, 0x00, 0x18, 0x00]));
        AssertInvalid(CreateBody(0, code: [0x80, 0x00, 0x18, 0x00]));

        var edge = new byte[] { 6, 0, 0, 0, 0, 0, 0, 0 };
        var wrongRecord = new[] { blockState, edge };
        AssertInvalid(CreateBodyWithRecords(
            wrongRecord, 0x03, placeCounts, [0x61, 0x10, 0x18, 0x00]));
        AssertInvalid(CreateBodyWithRecords(
            wrongRecord, 0x03, placeCounts, [0x91, 0x10, 0x18, 0x00]));
        AssertInvalid(CreateBodyWithRecords(
            wrongRecord, 0x04, callSiteCounts, [0x90, 1, 0x18, 0x00]));
        AssertInvalid(CreateBodyWithRecords(
            wrongRecord, code: [0x92, 1, 0x18, 0x00]));
    }

    [Test]
    public void ValidatesLongInstructionMapAndRecordReferences()
    {
        var blockState = new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 };
        var homeCounts = new uint[5];
        homeCounts[0] = 1;
        _ = new TrRmRegisterBodyReader(
            CreateBody(0x01, homeCounts, [0xD5, 0, 0, 0, 0x18, 0]));
        _ = new TrRmRegisterBodyReader(
            CreateBody(0x01, homeCounts, [0xD6, 0x10, 0, 0, 0x18, 0]));
        _ = new TrRmRegisterBodyReader(
            CreateBody(0x01, homeCounts, [0xD7, 0, 0, 0, 0x18, 0]));
        _ = new TrRmRegisterBodyReader(
            CreateBody(0x01, homeCounts, [0xEB, 0, 0, 0]));

        var typeCounts = new uint[5];
        typeCounts[1] = 1;
        _ = new TrRmRegisterBodyReader(
            CreateBody(0x02, typeCounts, [0xD0, 0, 0, 0, 0x18, 0]));
        _ = new TrRmRegisterBodyReader(
            CreateBody(0x02, typeCounts, [0xDE, 0x10, 0, 0, 0x18, 0]));
        _ = new TrRmRegisterBodyReader(
            CreateBody(0x02, typeCounts, [0xDE, 0x50, 0, 0, 0x18, 0]));

        var constantCounts = new uint[5];
        constantCounts[4] = 1;
        _ = new TrRmRegisterBodyReader(
            CreateBody(0x10, constantCounts, [0xD4, 0, 0, 0, 0x18, 0]));

        var fieldCounts = new uint[5];
        fieldCounts[3] = 1;
        _ = new TrRmRegisterBodyReader(
            CreateBody(0x08, fieldCounts, [0xDE, 0x30, 0, 0, 0x18, 0]));
        _ = new TrRmRegisterBodyReader(
            CreateBody(0x08, fieldCounts, [0xDE, 0x40, 0, 0, 0x18, 0]));

        var methodCounts = new uint[5];
        methodCounts[2] = 1;
        _ = new TrRmRegisterBodyReader(
            CreateBody(0x04, methodCounts, [0xE1, 0, 0, 0, 0x18, 0]));

        var edge = new byte[] { 6, 0, 0, 0, 0, 0, 0, 0 };
        var placeSelection = new byte[] {
            4, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        };
        var placeCounts = new uint[5];
        placeCounts[0] = 1;
        placeCounts[1] = 1;
        _ = new TrRmRegisterBodyReader(CreateBodyWithRecords(
            [blockState, placeSelection], 0x03, placeCounts,
            [0xD8, 0, 1, 0, 0x18, 0]));
        AssertInvalid(CreateBodyWithRecords(
            [blockState, placeSelection], 0x03, placeCounts,
            [0xD8, 0x0E, 1, 0, 0x18, 0]));
        _ = new TrRmRegisterBodyReader(CreateBodyWithRecords(
            [blockState, placeSelection], 0x03, placeCounts,
            [0xDA, 0, 1, 0, 0x18, 0]));
        _ = new TrRmRegisterBodyReader(CreateBodyWithRecords(
            [blockState, placeSelection], 0x03, placeCounts,
            [0xDB, 0, 1, 0, 0x18, 0]));
        _ = new TrRmRegisterBodyReader(CreateBodyWithRecords(
            [blockState, placeSelection], 0x03, placeCounts,
            [0xDD, 0, 1, 0, 0x18, 0]));
        _ = new TrRmRegisterBodyReader(CreateBodyWithRecords(
            [blockState, placeSelection], 0x03, placeCounts,
            [0xDC, 0, 1, 0, 0x18, 0]));
        _ = new TrRmRegisterBodyReader(CreateBodyWithRecords(
            [blockState, edge], code: [0xE4, 0, 1, 0]));
        _ = new TrRmRegisterBodyReader(CreateBodyWithRecords(
            [blockState, edge], code: [0xE5, 0x10, 1, 0]));

        var switchRecord = new byte[] {
            7, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        };
        _ = new TrRmRegisterBodyReader(CreateBodyWithRecords(
            [blockState, switchRecord], 0x02, typeCounts, [0xE6, 0, 1, 0]));

        var callSite = new byte[] {
            2, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        };
        _ = new TrRmRegisterBodyReader(CreateBodyWithRecords(
            [blockState, callSite], 0x04, methodCounts, [0xE0, 0, 1, 0, 0x18, 0]));
        _ = new TrRmRegisterBodyReader(
            CreateBody(0, code: [0xE2, 0, 0, 0, 0x18, 0]));
        _ = new TrRmRegisterBodyReader(
            CreateBody(0, code: [0xE3, 0, 0, 0]));

        var constructorBinding = new byte[] {
            0x0B, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        };
        var requirementFailure = new byte[] { 8, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0 };
        var failureCounts = new uint[5];
        failureCounts[1] = 1;
        failureCounts[2] = 1;
        _ = new TrRmRegisterBodyReader(CreateBodyWithRecords(
            [blockState, constructorBinding, requirementFailure],
            0x06, failureCounts, [0xE7, 0, 2, 0, 0x18, 0]));

        var semanticStore = new byte[] {
            5, 0, 0, 0, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        };
        _ = new TrRmRegisterBodyReader(CreateBodyWithRecords(
            [blockState, semanticStore], code: [0xD9, 0, 1, 0, 0x18, 0]));

        AssertInvalid(CreateBody(0, code: [0xD0, 0, 0, 0, 0x18, 0]));
        AssertInvalid(CreateBody(0, code: [0xD4, 0, 0, 0, 0x18, 0]));
        AssertInvalid(CreateBody(0, code: [0xD5, 0, 0, 0, 0x18, 0]));
        AssertInvalid(CreateBody(0, code: [0xDE, 0x60, 0, 0, 0x18, 0]));
        AssertInvalid(CreateBody(0, code: [0xDE, 0x1E, 0, 0, 0x18, 0]));
        AssertInvalid(CreateBody(0, code: [0xE3, 0, 1, 0, 0x18, 0]));
        AssertInvalid(CreateBody(0, code: [0xE2, 0, 0x10, 0, 0x18, 0]));
        AssertInvalid(CreateBody(0, code: [0xEB, 1, 0, 0, 0x18, 0]));
        AssertInvalid(CreateBodyWithRecords(
            [blockState, edge], code: [0xD9, 0, 1, 0, 0x18, 0]));
        AssertInvalid(CreateBodyWithRecords(
            [blockState, edge], code: [0xE0, 0, 1, 0, 0x18, 0]));
    }

    [Test]
    public void ValidatesSpecialRegisterRoleBounds()
    {
        _ = new TrRmRegisterBodyReader(CreateBody(0, code: [0x12, 0xEC, 0]));
        AssertInvalid(CreateBody(0, code: [0x10, 0x0E, 0]));
        AssertInvalid(CreateBody(0, code: [0x21, 0x0E, 0]));
        AssertInvalid(CreateBody(0, code: [0x12, 0x0D, 0]));
        AssertInvalid(CreateBody(0, code: [0x12, 0xFC, 0]));

        var homeCounts = new uint[5];
        homeCounts[0] = 1;
        AssertInvalid(CreateBody(0x01, homeCounts, [0x60, 0x0E, 0]));
        AssertInvalid(CreateBody(0, code: [0x64, 0x0E, 0]));
        AssertInvalid(CreateBody(0, code: [0x63, 0x0E, 0]));
        AssertInvalid(CreateBody(0, code: [0x65, 0xE0, 0]));
        AssertInvalid(CreateBody(0, code: [0x67, 0xE0, 0]));

        var typeCounts = new uint[5];
        typeCounts[1] = 1;
        AssertInvalid(CreateBody(0x02, typeCounts, [0xD0, 0x0E, 0, 0, 0x18, 0]));
        var methodCounts = new uint[5];
        methodCounts[2] = 1;
        AssertInvalid(CreateBody(0x04, methodCounts, [0x80, 0x0E, 0x18, 0]));
        AssertInvalid(CreateBody(0x04, methodCounts, [0x81, 0x0E, 0x18, 0]));
        AssertInvalid(CreateBody(0x04, methodCounts, [0x81, 0xE0, 0x18, 0]));
        AssertInvalid(CreateBody(0x04, methodCounts, [0xE1, 0xE0, 0, 0, 0x18, 0]));
        AssertInvalid(CreateBody(0x04, methodCounts, [0xE1, 0x0E, 0, 0, 0x18, 0]));
        AssertInvalid(CreateBody(0, code: [0xE2, 0x0E, 0, 0, 0x18, 0]));
        AssertInvalid(CreateBody(0, code: [0xE2, 0, 0x0E, 0, 0x18, 0]));
    }

    [Test]
    public void ValidatesLongOperationControlSelectors()
    {
        foreach (var control in new byte[] {
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
            0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
            0x20, 0x21, 0x22, 0x23, 0x24, 0x25,
        })
        {
            var payload = control is 0x0E or 0x0F
                ? 0x3210
                : control is 0x04 or 0x08 or 0x25
                    ? 0xF0F0
                    : 0x21F0;
            _ = new TrRmRegisterBodyReader(
                CreateBody(0, code: [
                    0xC0, control, (byte)payload, (byte)(payload >> 8), 0,
                ]));
        }

        foreach (var control in new byte[] { 1, 2, 3, 4, 5, 6, 7 })
        {
            var payload = control == 4 ? 0xF0F0 : 0x21F0;
            _ = new TrRmRegisterBodyReader(
                CreateBody(0, code: [
                    0xC1, control, (byte)payload, (byte)(payload >> 8), 0,
                ]));
        }

        foreach (var control in new byte[] { 0, 1, 2 })
        {
            _ = new TrRmRegisterBodyReader(
                CreateBody(0, code: [0xC2, control, 0xF0, 0x21, 0]));
        }

        foreach (var opcode in new byte[] { 0xC3, 0xC4, 0xC5 })
        {
            _ = new TrRmRegisterBodyReader(
                CreateBody(0, code: [opcode, 0, 0, 0, 0]));
            _ = new TrRmRegisterBodyReader(
                CreateBody(0, code: [opcode, 0x0F, 0, 0, 0]));
            AssertInvalid(CreateBody(0, code: [opcode, 0x10, 0, 0, 0]));
        }

        foreach (var control in new byte[] { 0, 0x10, 0x1F, 0x26, 0xFF })
        {
            AssertInvalid(CreateBody(0, code: [0xC0, control, 0, 0, 0]));
        }
        AssertInvalid(CreateBody(0, code: [0xC1, 0, 0, 0, 0]));
        AssertInvalid(CreateBody(0, code: [0xC1, 8, 0, 0, 0]));
        AssertInvalid(CreateBody(0, code: [0xC2, 3, 0, 0, 0]));
    }

    [Test]
    public void ValidatesCommonRegisterFamilyPayloadRoles()
    {
        _ = new TrRmRegisterBodyReader(
            CreateBody(0, code: [0xC0, 0x01, 0xF0, 0x21, 0]));
        _ = new TrRmRegisterBodyReader(
            CreateBody(0, code: [0xC0, 0x04, 0xF0, 0xF0, 0]));
        _ = new TrRmRegisterBodyReader(
            CreateBody(0, code: [0xC0, 0x0E, 0x10, 0x32, 0]));
        _ = new TrRmRegisterBodyReader(
            CreateBody(0, code: [0xC1, 0x01, 0xF0, 0x21, 0]));
        _ = new TrRmRegisterBodyReader(
            CreateBody(0, code: [0xC1, 0x04, 0xF0, 0xF0, 0]));
        _ = new TrRmRegisterBodyReader(
            CreateBody(0, code: [0xC2, 0x00, 0xF0, 0x21, 0]));

        foreach (var opcode in new byte[] { 0xC3, 0xC4, 0xC5, 0xC6, 0xCB, 0xCC })
        {
            _ = new TrRmRegisterBodyReader(
                CreateBody(0, code: [opcode, 0x0F, 0x32, 0x10, 0]));
            AssertInvalid(CreateBody(0, code: [opcode, 0, 0x0E, 0, 0]));
        }

        AssertInvalid(CreateBody(0, code: [0xC0, 0x01, 0, 0, 0]));
        AssertInvalid(CreateBody(0, code: [0xC0, 0x04, 0xF0, 0x00, 0]));
        AssertInvalid(CreateBody(0, code: [0xC0, 0x0E, 0, 0, 0]));
        AssertInvalid(CreateBody(0, code: [0xC1, 0x01, 0, 0, 0]));
        AssertInvalid(CreateBody(0, code: [0xC1, 0x04, 0xF0, 0x00, 0]));
        AssertInvalid(CreateBody(0, code: [0xC2, 0x00, 0, 0, 0]));
    }

    [Test]
    public void RejectsInvalidPrefixCountsAndInstructionExtents()
    {
        AssertInvalid(Mutate(CreateBody(), 0, 0x02));
        AssertInvalid(Mutate(CreateBody(), 1, 0x02));
        AssertInvalid(Mutate(CreateBody(), 2, 0x20));
        AssertInvalid(Mutate(CreateBody(), 3, 1));
        AssertInvalid(Mutate(CreateBody(), 5, 0));
        AssertInvalid(Mutate(CreateBody(), 5, 65_537));
        AssertInvalid(Mutate(CreateBody(), 9, 65_537));
        AssertInvalid(Mutate(CreateBody(), 13, 0));
        AssertInvalid(CreateBody(0, code: [0x10]));
        AssertInvalid(CreateBody(0, code: [0xCC, 0, 0, 0]));
        AssertInvalid(CreateBody(0, code: [0x00, 0]));
        AssertInvalid(CreateBody(0, code: [0xB3, 0]));
        AssertInvalid(MutateByte(CreateBody(), 45, 0x01));
    }

    [Test]
    public void RejectsMalformedBlockAndTypedRecordDirectories()
    {
        var body = CreateBody();
        AssertInvalid(Mutate(body, 17, 1));
        AssertInvalid(Mutate(body, 21, 0));
        AssertInvalid(Mutate(body, 25, 1));
        AssertInvalid(Mutate(body, 29, 1));
        AssertInvalid(Mutate(body, 33, 7));
        AssertInvalid(Mutate(body, 37, 2));
        AssertInvalid(Mutate(body, 38, 1));
        AssertInvalid(Mutate(body, 39, 1));
        AssertInvalid(MutateByte(body, 45, 1));
    }

    [Test]
    public void ValidatesRegisterEntryStateCarriersAndTypeIndexes()
    {
        var counts = new uint[5];
        counts[1] = 1;
        var valueState = new byte[] { 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0 };
        _ = new TrRmRegisterBodyReader(
            CreateBodyWithRecord(valueState, 0x02, counts));

        var identityState = new byte[] { 1, 0, 0, 0, 1, 0, 0, 0, 0, 2, 0, 0, 0 };
        _ = new TrRmRegisterBodyReader(CreateBodyWithRecord(identityState));

        var invalidRegister = (byte[])valueState.Clone();
        invalidRegister[8] = 14;
        AssertInvalid(CreateBodyWithRecord(invalidRegister, 0x02, counts));
        var invalidCarrier = (byte[])valueState.Clone();
        invalidCarrier[9] = 3;
        AssertInvalid(CreateBodyWithRecord(invalidCarrier, 0x02, counts));
        var invalidAccess = (byte[])valueState.Clone();
        invalidAccess[12] = 1;
        AssertInvalid(CreateBodyWithRecord(invalidAccess, 0x02, counts));
        var invalidTypeMapIndex = (byte[])valueState.Clone();
        invalidTypeMapIndex[10] = 1;
        AssertInvalid(CreateBodyWithRecord(invalidTypeMapIndex, 0x02, counts));
    }

    [Test]
    public void ValidatesAssignedTypedRecordLayouts()
    {
        var blockState = new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 };
        AssertValidRecords([blockState]);
        AssertValidRecords([
            blockState,
            [2, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        ], 0x04, [0, 0, 1, 0, 0]);
        AssertValidRecords([
            blockState,
            [3, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
            [6, 0, 0, 0, 0, 0, 0, 0],
            [7, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        ], 0x02, [0, 1, 0, 0, 0]);
        AssertValidRecords([
            blockState,
            [4, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        ], 0x03, [1, 1, 0, 0, 0]);
        AssertValidRecords([
            blockState,
            [5, 0, 0, 0, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        ]);
        AssertValidRecords([
            blockState,
            [0x0B, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
            [8, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0],
        ], 0x06, [0, 1, 1, 0, 0]);
        AssertValidRecords([
            blockState,
            [9, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0],
            [0x0A, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0],
        ]);
    }

    [Test]
    public void RejectsMalformedTypedRecordPayloadsAndLocalReferences()
    {
        var blockState = new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 };
        AssertInvalid(CreateBodyWithRecords([
            blockState,
            [2, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        ]));
        AssertInvalid(CreateBodyWithRecords([
            blockState,
            [6, 0, 0, 0, 0, 0, 1, 0],
        ]));
        AssertInvalid(CreateBodyWithRecords([
            blockState,
            [8, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0],
        ]));
        AssertInvalid(CreateBodyWithRecords([
            blockState,
            [0x0B, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        ]));
        AssertInvalid(CreateBodyWithRecords([
            blockState,
            [0x0A, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0],
        ]));
    }

    [Test]
    public void ValidatesCompactDescriptorPayloadsAndReferenceKinds()
    {
        ValidateImplementation(
            [0x02, 0, 0, 0, 1, 0, 0, 0x14], 0);
        ValidateImplementation(
            [0x03, 0, 0, 0, 1, 0, 0, 0x1C, 0, 0, 2, 0], 0);
        ValidateImplementation(
            [0x04, 0, 0, 0, 1, 0, 0, 0x14, 1, 0, 0, 0], 1);
        ValidateImplementation(
            [0x05, 0, 0, 0, 1, 0, 0, 0x16], 0);

        AssertInvalidImplementation([0x02, 0, 0, 0, 1, 0, 0, 0x16], 0);
        AssertInvalidImplementation([0x03, 0, 0, 0, 1, 0, 0, 0x14, 0, 0, 4, 0], 0);
        AssertInvalidImplementation([0x04, 0, 0, 0, 1, 0, 0, 0x14, 2, 0, 0, 0], 1);
        AssertInvalidImplementation([0x05, 1, 0, 0, 1, 0, 0, 0x16], 0);
    }

    [Test]
    public void StandardStructuralValidationUsesTheRegisterBodyCodec()
    {
        var artifact = CreateStandardArtifact(CreateBody());
        TrRmStructuralMetadataValidator.Validate(artifact);
        Assert.That(GetSelectedRegisterBodyFirstOpcode(artifact), Is.EqualTo((byte)0));

        var malformedBody = CreateBody();
        malformedBody[2] = 0x20;
        var malformedArtifact = CreateStandardArtifact(malformedBody);
        TrRmStructuralMetadataValidator.Validate(malformedArtifact);
        Assert.Throws<InvalidDataException>(() => GetSelectedRegisterBodyFirstOpcode(malformedArtifact));
    }

    [Test]
    public void BindsSelectedHomeTypeAndMethodMapsToTheOwningMethod()
    {
        var counts = new uint[5];
        counts[0] = 1;
        counts[1] = 1;
        counts[2] = 1;
        ValidateSelectedBodyMaps(CreateBody(0x07, counts));

        counts[1] = 2;
        var pseudoType = CreateBody(0x07, counts);
        BinaryPrimitives.WriteUInt32LittleEndian(pseudoType.AsSpan(41), 0xF000_0000);
        AssertInvalidSelectedBodyMaps(pseudoType);

        var reversedPseudoType = CreateBody(0x07, counts);
        BinaryPrimitives.WriteUInt32LittleEndian(reversedPseudoType.AsSpan(41), 0xF100_0000);
        AssertInvalidSelectedBodyMaps(reversedPseudoType);

        var typeSpecRow = CreateBody(0x07, counts);
        BinaryPrimitives.WriteUInt32LittleEndian(typeSpecRow.AsSpan(37), 0x1100_0002);
        ValidateSelectedBodyMaps(typeSpecRow);

        var inlineFamily = CreateBody(0x07, counts);
        BinaryPrimitives.WriteUInt32LittleEndian(inlineFamily.AsSpan(37), 0xF300_8B08);
        ValidateSelectedBodyMaps(inlineFamily);
    }

    [Test]
    public void RequiresReceiverAndFormalHomesToMatchTheirSignatureTypes()
    {
        ValidateSelectedHomeBodyMaps(
            CreateHomeTypeBody(2, 0x1300_0001), 3);
        ValidateSelectedHomeBodyMaps(
            CreateHomeTypeBody(2, 0x1100_0005), 3);
        ValidateSelectedHomeBodyMaps(
            CreateHomeTypeBody(2, 0x1C00_0001), 4);
        ValidateSelectedHomeBodyMaps(
            CreateHomeTypeBody(2, 0x1100_0006), 4);
        ValidateSelectedHomeBodyMaps(
            CreateHomeTypeBody(1, 0x1300_0001), 3, 3);
        ValidateSelectedHomeBodyMaps(
            CreateHomeTypeBody(2, 0xF200_0008), 2);
        ValidateSelectedHomeBodyMaps(
            CreateHomeTypeBody(2, 0x1100_0007), 7);
        ValidateSelectedHomeBodyMaps(
            CreateHomeTypeBody(2, 0xF300_8A08), 8);
        ValidateSelectedHomeBodyMaps(
            CreateHomeTypeBody(2, 0xF300_8B08), 9);
        ValidateSelectedHomeBodyMaps(
            CreateHomeTypeBody(2, 0x1100_0008), 0xF300_8A08);

        AssertInvalidSelectedHomeBodyMaps(
            CreateHomeTypeBody(2, 0xF200_0008), 3);
        AssertInvalidSelectedHomeBodyMaps(
            CreateHomeTypeBody(2, 0x1300_0001), 4);
        AssertInvalidSelectedHomeBodyMaps(
            CreateHomeTypeBody(1, 0xF200_0008), 3, 3);
        AssertInvalidSelectedHomeBodyMaps(
            CreateHomeTypeBody(2, 0x1300_0001), 7);
        AssertInvalidSelectedHomeBodyMaps(
            CreateHomeTypeBody(2, 0xF200_0007), 2);
    }

    [Test]
    public void BindsImportedMethodMapAndRejectsAnImportWithTheWrongTargetKind()
    {
        var counts = new uint[5];
        counts[0] = 1;
        counts[1] = 1;
        counts[2] = 1;
        var body = CreateBody(0x07, counts);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(41), 0x1C00_0001);
        ValidateSelectedImportedMethodBodyMaps(body);

        var wrongKind = (byte[])body.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(wrongKind.AsSpan(37), 0x1C00_0001);
        Assert.Throws<InvalidDataException>(() =>
            ValidateSelectedImportedMethodBodyMaps(wrongKind));
    }

    [Test]
    public void BindsTypeFieldPathAndConstantMapVariants()
    {
        var counts = new uint[5];
        counts[0] = 1;
        counts[1] = 3;
        counts[2] = 1;
        counts[3] = 3;
        counts[4] = 1;
        var body = CreateBody(0x1F, counts);
        var mapStart = 5 + (5 * sizeof(uint)) + (3 * sizeof(uint));
        var typeMapStart = mapStart + 8;
        var methodMapStart = typeMapStart + (3 * sizeof(uint));
        var fieldMapStart = methodMapStart + sizeof(uint);
        var constantMapStart = fieldMapStart + (3 * sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(typeMapStart + 4), 0x1300_0001);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(typeMapStart + 8), 0x1C00_0001);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(fieldMapStart), 0x1400_0001);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(fieldMapStart + 4), 0x1C00_0002);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(fieldMapStart + 8), 0x1D00_0001);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(constantMapStart), 2);

        var artifact = CreateStandardArtifactWithFieldMaps(body);
        var tables = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
            new TrRmArtifactIndex(artifact));
        tables.ValidateRegisterBodyMapsForMethod(1);
    }

    [Test]
    public void BindsConstructedTypeMapArgumentsInTheOwningModule()
    {
        var counts = new uint[5];
        counts[0] = 1;
        counts[1] = 1;
        counts[2] = 1;
        var body = CreateBody(0x07, counts);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(37), 0x1100_0004);

        var artifact = CreateStandardArtifactWithConstructedTypeMap(body);
        var tables = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
            new TrRmArtifactIndex(artifact));
        tables.ValidateRegisterBodyMapsForMethod(1);
    }

    [Test]
    public void RejectsConstructedTypeMapImportsOwnedByAnotherModule()
    {
        var counts = new uint[5];
        counts[0] = 1;
        counts[1] = 1;
        counts[2] = 1;
        var body = CreateBody(0x07, counts);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(37), 0x1100_0004);
        var artifact = CreateStandardArtifactWithConstructedTypeMap(body,
            importedTypeOwnerIsDifferentModule: true);
        TrRmStructuralMetadataValidator.Validate(artifact);

        Assert.Throws<InvalidDataException>(() =>
            ValidateSelectedConstructedTypeMap(body,
                importedTypeOwnerIsDifferentModule: true));
    }

    [Test]
    public void RejectsMethodMapReferencesOwnedByAnotherModule()
    {
        var counts = new uint[5];
        counts[0] = 1;
        counts[1] = 1;
        counts[2] = 1;
        var directBody = CreateBody(0x07, counts);
        BinaryPrimitives.WriteUInt32LittleEndian(directBody.AsSpan(41), 0x1600_0002);
        Assert.Throws<InvalidDataException>(() =>
            ValidateSelectedTwoModuleBodyMaps(directBody));

        var importedBody = CreateBody(0x07, counts);
        BinaryPrimitives.WriteUInt32LittleEndian(importedBody.AsSpan(41), 0x1C00_0001);
        Assert.Throws<InvalidDataException>(() =>
            ValidateSelectedTwoModuleBodyMaps(importedBody));
    }

    [Test]
    public void RejectsSelectedBodyMapsWithInvalidRowsKindsOrLocalIndexes()
    {
        var counts = new uint[5];
        counts[0] = 1;
        counts[1] = 1;
        counts[2] = 1;
        var body = CreateBody(0x07, counts);

        var invalidTypeDef = (byte[])body.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(invalidTypeDef.AsSpan(37), 0x1300_0001);
        AssertInvalidSelectedBodyMaps(invalidTypeDef);

        var invalidImport = (byte[])body.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(invalidImport.AsSpan(37), 0x1C00_0001);
        AssertInvalidSelectedBodyMaps(invalidImport);

        var invalidMethod = (byte[])body.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(invalidMethod.AsSpan(41), 0x1600_0002);
        AssertInvalidSelectedBodyMaps(invalidMethod);

        var invalidHomeType = (byte[])body.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(invalidHomeType.AsSpan(31), 1);
        AssertInvalidSelectedBodyMaps(invalidHomeType);

        var retiredWrapperPseudo = (byte[])body.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(retiredWrapperPseudo.AsSpan(37), 0xF000_0000);
        AssertInvalidSelectedBodyMaps(retiredWrapperPseudo);

        var retiredGenericPseudo = (byte[])body.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(retiredGenericPseudo.AsSpan(37), 0xF100_0000);
        AssertInvalidSelectedBodyMaps(retiredGenericPseudo);

        var fieldCounts = new uint[5];
        fieldCounts[0] = 1;
        fieldCounts[1] = 1;
        fieldCounts[2] = 1;
        fieldCounts[3] = 1;
        var invalidField = CreateBody(0x0F, fieldCounts);
        AssertInvalidSelectedBodyMaps(invalidField);

        var constantCounts = new uint[5];
        constantCounts[0] = 1;
        constantCounts[1] = 1;
        constantCounts[2] = 1;
        constantCounts[4] = 1;
        var invalidConstant = CreateBody(0x17, constantCounts);
        AssertInvalidSelectedBodyMaps(invalidConstant);
    }

    private static void AssertBody(byte[] code)
    {
        var reader = new TrRmRegisterBodyReader(CreateBody(0, code: code));
        Assert.That(reader.GetBlockCode(0).ToArray(), Is.EqualTo(code));
    }

    private static void AssertInvalid(byte[] body)
    {
        Assert.Throws<InvalidDataException>(() => ReadRegisterBody(body));
    }

    private static void ReadRegisterBody(byte[] body)
        => _ = new TrRmRegisterBodyReader(body);

    private static void AssertValidRecords(byte[][] records,
        byte mapMask = 0, uint[]? counts = null)
        => _ = new TrRmRegisterBodyReader(CreateBodyWithRecords(records, mapMask, counts));

    private static void ValidateImplementation(byte[] body, int contractCount)
    {
        var section = TrRmVariableTableWriter.EncodeBodies([body]);
        var bodies = new TrRmBodyTableReader(section, 1);
        bodies.ValidateSelectedImplementation(1, contractCount);
    }

    private static byte GetSelectedRegisterBodyFirstOpcode(byte[] artifact)
    {
        var index = new TrRmArtifactIndex(artifact);
        if (!index.TryGetSection(0x0020, out var section, out var count))
        {
            throw new InvalidDataException("Body section is absent.");
        }

        var bodies = new TrRmBodyTableReader(section, count);
        return bodies.GetRegisterBody(1).GetBlockCode(0)[0];
    }

    private static void ValidateSelectedBodyMaps(byte[] body)
    {
        var artifact = CreateStandardArtifact(body);
        var tables = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
            new TrRmArtifactIndex(artifact));
        tables.ValidateRegisterBodyMapsForMethod(1);
    }

    private static void ValidateSelectedHomeBodyMaps(byte[] body,
        uint formalTypeSpecRef, uint receiverTypeSpecRef = 0)
    {
        var artifact = CreateStandardArtifactWithFieldMaps(body,
            formalTypeSpecRef, receiverTypeSpecRef);
        var tables = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
            new TrRmArtifactIndex(artifact));
        tables.ValidateRegisterBodyMapsForMethod(1);
    }

    private static void AssertInvalidSelectedHomeBodyMaps(byte[] body,
        uint formalTypeSpecRef, uint receiverTypeSpecRef = 0)
        => Assert.Throws<InvalidDataException>(() =>
            ValidateSelectedHomeBodyMaps(body, formalTypeSpecRef, receiverTypeSpecRef));

    private static byte[] CreateHomeTypeBody(byte role, uint typeMapToken)
    {
        var counts = new uint[5];
        counts[0] = 1;
        counts[1] = 1;
        var body = CreateBody(0x03, counts);
        var homeMapStart = 5 + (2 * sizeof(uint)) + (3 * sizeof(uint));
        body[homeMapStart] = role;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(homeMapStart + 8),
            typeMapToken);
        return body;
    }

    private static void ValidateSelectedImportedMethodBodyMaps(byte[] body)
    {
        var artifact = CreateStandardArtifactWithImportedMethod(body);
        var tables = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
            new TrRmArtifactIndex(artifact));
        tables.ValidateRegisterBodyMapsForMethod(1);
    }

    private static void ValidateSelectedTwoModuleBodyMaps(byte[] body)
    {
        var artifact = CreateTwoModuleArtifactWithImportedMethod(body);
        var tables = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
            new TrRmArtifactIndex(artifact));
        tables.ValidateRegisterBodyMapsForMethod(1);
    }

    private static void ValidateSelectedConstructedTypeMap(byte[] body,
        bool importedTypeOwnerIsDifferentModule)
    {
        var artifact = CreateStandardArtifactWithConstructedTypeMap(
            body, importedTypeOwnerIsDifferentModule);
        var tables = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
            new TrRmArtifactIndex(artifact));
        tables.ValidateRegisterBodyMapsForMethod(1);
    }

    private static byte[] CreateStandardArtifactWithFieldMaps(byte[] body,
        uint formalTypeSpecRef = 3, uint receiverTypeSpecRef = 0)
    {
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0), ("main", 0), ("Example", 0),
            ("Owner", 0), ("Value", 0), ("provider", 0),
            ("type-key", 0), ("field-key", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 9);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 1, 1, 1, 1, 1, 1, 1),
        ], strings, 1, 1, 1, 0, 1);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            1, 1, 1, 0, 1);
        var moduleRefBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(7, 0, 0),
        ], strings, modules);
        var featuresBytes = TrRmFeatureTableWriter.Encode([
            new TrRmFeatureRow(11, 1),
        ]);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(0),
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Reference(0x03, 1),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Reference(0x03, 1),
            TrRmTypeSpecTableWriter.Reference(0x02, 1, 1),
            TrRmTypeSpecTableWriter.IndexedFamily(0x8A, 0xF200_0008),
            TrRmTypeSpecTableWriter.InlineFamily(0x8B, 8),
        ], 1, 2, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 9, 1, 2, 0);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(4, 5, 6, 1, 1, 0, 0, 1, 1, 0, 0),
        ], strings, modules, 1, 0, 1, 0);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, 1,
            strings, modules, 1, 0, 1, 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
        ], typeDefs, targetProfiles, 0);
        var fieldBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 6, 2, 10),
        ], strings, typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldBytes, 1,
            strings, typeDefs, typeSpecs);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_0000, receiverTypeSpecRef, 1, 1, 0, 0),
        ], typeSpecs, 1, 0);
        var signatures = new TrRmMethodSigTableReader(
            signatureBytes, 1, typeSpecs, 1, 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(receiverTypeSpecRef == 0 ? 0 : 1,
                3, 1, 0, 0, 0, 1),
        ], strings, modules, typeDefs, signatures, 0, 1);
        var methods = new TrRmMethodDefTableReader(methodBytes, 1,
            strings, modules, typeDefs, signatures, 0, 1);
        var paramBytes = TrRmParamTableWriter.Encode([
            new TrRmParamRow(1, 0, formalTypeSpecRef, 0),
        ], signatures, typeSpecs);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes, 1,
            strings, modules);
        var importRows = new[]
        {
            new TrRmImportRow(1, 8, 0x1300_0001),
            new TrRmImportRow(1, 9, 0x1400_0001),
        };
        var importBytes = TrRmImportTableWriter.Encode(importRows, strings,
            moduleRefs, typeDefs, methods, default, default, default, fields);
        var imports = new TrRmImportTableReader(importBytes, importRows.Length,
            strings, moduleRefs, typeDefs, methods, default, default, default, fields);
        var fieldPathsBytes = TrRmFieldPathTableWriter.Encode([
            new TrRmFieldPathRow(3, [0x1C00_0002]),
        ], typeSpecs, fields, imports);
        var constantSourcesBytes = TrRmConstantSourceTableWriter.Encode([
            new TrRmConstantSourceRow(1, 1),
        ], 1, 0, 0);

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 9, stringsBytes),
            new TrRmSection(0x0004, 0, 1, featuresBytes),
            new TrRmSection(0x0010, 0, 1, moduleRefBytes),
            new TrRmSection(0x0011, 0, 9, typeSpecBytes),
            new TrRmSection(0x0013, 0, 1, typeDefBytes),
            new TrRmSection(0x0014, 0, 1, fieldBytes),
            new TrRmSection(0x0015, 0, 1, layoutBytes),
            new TrRmSection(0x0016, 0, 1, methodBytes),
            new TrRmSection(0x0017, 0, 1, signatureBytes),
            new TrRmSection(0x0018, 0, 1, paramBytes),
            new TrRmSection(0x001C, 0, 2, importBytes),
            new TrRmSection(0x0020, 0, 1,
                TrRmVariableTableWriter.EncodeBodies([body])),
            new TrRmSection(0x0030, 0, 1, constantSourcesBytes),
            new TrRmSection(0x003F, 0, 1, new byte[24]),
            new TrRmSection(0x004B, 0, 1, fieldPathsBytes),
        ]);
    }

    private static byte[] CreateStandardArtifactWithConstructedTypeMap(byte[] body,
        bool importedTypeOwnerIsDifferentModule = false)
    {
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0), ("main", 0), ("Example", 0),
            ("Owner", 0), ("T", 0), ("Imported", 0),
            ("provider", 0), ("type-key", 0), ("consumer", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 10);
        var moduleRows = importedTypeOwnerIsDifferentModule
            ? new[]
            {
                new TrRmModuleRow(2, 0, 0, 1, 1, 1, 1, 1),
                new TrRmModuleRow(10, 0, 0, 0, 1, 2, 2, 1),
            }
            : new[] { new TrRmModuleRow(2, 0, 0, 1, 1, 1, 1, 1) };
        var moduleBytes = TrRmModuleTableWriter.Encode(moduleRows,
            strings, 1, 2, 1, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, moduleRows.Length,
            strings, 1, 2, 1, 0, 0);
        var moduleRefBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(8, 0, 0),
        ], strings, modules);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(0),
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Constructed(3, [5]),
            TrRmTypeSpecTableWriter.Reference(0x03, 1),
        ], 2, 1, 1);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 5, 2, 1, 1);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(4, 5, 6, 0, 0, 1, 1, 1, 1, 0, 0),
            new TrRmTypeDefRow(4, 7, 6, 0, 0, 0, 0, 2, 1, 0, 0),
        ], strings, modules, 0, 1, 2, 0);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, 2,
            strings, modules, 0, 1, 2, 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
            new TrRmLayoutRow(2, 0, 0, 0, 0, 0, 0, 0),
        ], typeDefs, targetProfiles, 0);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_0000, 0, 0, 0, 0, 0),
        ], typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(
            signatureBytes, 1, typeSpecs, 0, 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 3, 1, 0, 0, 0, 1),
        ], strings, modules, typeDefs, signatures, 0, 1);
        var methods = new TrRmMethodDefTableReader(methodBytes, 1,
            strings, modules, typeDefs, signatures, 0, 1);
        var genericParamBytes = TrRmGenericParamTableWriter.Encode([
            new TrRmGenericParamRow(0x1300_0001, 0, 6, 0, 0,
                1, 1, 1, 0, 0),
        ], strings, typeDefs, methods, signatures, typeSpecs, 0);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes, 1,
            strings, modules);
        var importBytes = TrRmImportTableWriter.Encode([
            new TrRmImportRow(1, 9, 0x1300_0002),
        ], strings, moduleRefs, typeDefs, methods, default);

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, (uint)moduleRows.Length, moduleBytes),
            new TrRmSection(0x0002, 0, 10, stringsBytes),
            new TrRmSection(0x0010, 0, 1, moduleRefBytes),
            new TrRmSection(0x0011, 0, 5, typeSpecBytes),
            new TrRmSection(0x0013, 0, 2, typeDefBytes),
            new TrRmSection(0x0015, 0, 2, layoutBytes),
            new TrRmSection(0x0016, 0, 1, methodBytes),
            new TrRmSection(0x0017, 0, 1, signatureBytes),
            new TrRmSection(0x0019, 0, 1, genericParamBytes),
            new TrRmSection(0x001C, 0, 1, importBytes),
            new TrRmSection(0x0020, 0, 1,
                TrRmVariableTableWriter.EncodeBodies([body])),
        ]);
    }

    private static void AssertInvalidSelectedBodyMaps(byte[] body)
        => Assert.Throws<InvalidDataException>(() => ValidateSelectedBodyMaps(body));

    private static void AssertInvalidImplementation(byte[] body, int contractCount)
        => Assert.Throws<InvalidDataException>(() =>
            ValidateImplementation(body, contractCount));

    private static byte[] CreateBody(byte mapMask = 0, uint[]? mapCounts = null,
        byte[]? code = null)
        => CreateBodyWithRecord([0x01, 0, 0, 0, 0, 0, 0, 0],
            mapMask, mapCounts, code);

    private static byte[] CreateBodyWithRecord(byte[] record, byte mapMask = 0,
        uint[]? mapCounts = null, byte[]? code = null)
        => CreateBodyWithRecords([record], mapMask, mapCounts, code);

    private static byte[] CreateBodyWithRecords(byte[][] records, byte mapMask = 0,
        uint[]? mapCounts = null, byte[]? code = null)
    {
        var counts = mapCounts ?? new uint[5];
        if (counts.Length != 5 || records.Length == 0
            || records[0].Length == 0 || records[0][0] != 0x01)
        {
            throw new ArgumentException("Five map counts and a leading entry-state record are required.");
        }

        var instructions = code ?? [0x00];
        var bytes = new List<byte>(128);
        bytes.Add(0x01);
        bytes.Add(0x01);
        bytes.Add(mapMask);
        bytes.Add(0);
        bytes.Add(0);
        for (var map = 0; map < counts.Length; map++)
        {
            if ((mapMask & (1 << map)) != 0)
            {
                Write(bytes, counts[map]);
            }
        }

        Write(bytes, 1);
        Write(bytes, (uint)records.Length);
        Write(bytes, (uint)instructions.Length);
        for (var map = 0; map < counts.Length; map++)
        {
            if ((mapMask & (1 << map)) == 0)
            {
                continue;
            }

            for (var entry = 0; entry < counts[map]; entry++)
            {
                switch (map)
                {
                    case 0:
                        bytes.AddRange([3, 0, 0, 0, 0, 0, 0, 0]);
                        break;
                    case 1:
                        Write(bytes, 0xF2000008);
                        break;
                    case 2:
                        Write(bytes, 0x16000001);
                        break;
                    case 3:
                        Write(bytes, 0x14000001);
                        break;
                    case 4:
                        Write(bytes, 0xF2000008);
                        Write(bytes, 1);
                        break;
                }
            }
        }

        Write(bytes, 0);
        Write(bytes, (uint)instructions.Length);
        Write(bytes, 0);
        var recordOffset = 0u;
        Write(bytes, recordOffset);
        foreach (var record in records)
        {
            recordOffset += (uint)record.Length;
            Write(bytes, recordOffset);
        }
        foreach (var record in records)
        {
            bytes.AddRange(record);
        }

        bytes.AddRange(instructions);
        return bytes.ToArray();
    }

    private static byte[] CreateStandardArtifact(byte[] body)
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0), ("main", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 3);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 1, 1, 1, 1, 1),
        ], strings, 0, 0, 1, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 0, 1, 0, 0);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(0),
            TrRmTypeSpecTableWriter.Builtin(8),
        ], 0, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 2, 0, 0, 0);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF2000000, 0, 0, 0, 0, 0),
        ], typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 1,
            typeSpecs, 0, 0);
        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 3, 1, 0, 0, 0, 1),
        ], strings, modules, typeDefs, signatures, 0, 1);
        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 3, stringBytes),
            new TrRmSection(0x0011, 0, 2, typeSpecBytes),
            new TrRmSection(0x0016, 0, 1, methodBytes),
            new TrRmSection(0x0017, 0, 1, signatureBytes),
            new TrRmSection(0x0020, 0, 1,
                TrRmVariableTableWriter.EncodeBodies([body])),
        ]);
    }

    private static byte[] CreateStandardArtifactWithImportedMethod(byte[] body)
    {
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0), ("main", 0), ("required", 0),
            ("provider", 0), ("required-key", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 6);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 1, 1, 1, 1, 1),
        ], strings, 1, 0, 2, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            1, 0, 2, 0, 0);
        var moduleRefBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(5, 0, 0),
        ], strings, modules);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(0),
            TrRmTypeSpecTableWriter.Builtin(8),
        ], 0, 1, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 2, 0, 1, 0);
        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, 0);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_0000, 0, 0, 0, 0, 0),
        ], typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(
            signatureBytes, 1, typeSpecs, 0, 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 3, 1, 0, 0, 0, 1),
            new TrRmMethodDefRow(0, 4, 1, 0, 0, 0, 0),
        ], strings, modules, typeDefs, signatures, 0, 1);
        var methods = new TrRmMethodDefTableReader(methodBytes, 2,
            strings, modules, typeDefs, signatures, 0, 1);
        var importBytes = TrRmImportTableWriter.Encode([
            new TrRmImportRow(1, 6, 0x1600_0002),
        ], strings, new TrRmModuleRefTableReader(moduleRefBytes, 1, strings, modules),
            typeDefs, methods, default);

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 6, stringsBytes),
            new TrRmSection(0x0010, 0, 1, moduleRefBytes),
            new TrRmSection(0x0011, 0, 2, typeSpecBytes),
            new TrRmSection(0x0016, 0, 2, methodBytes),
            new TrRmSection(0x0017, 0, 1, signatureBytes),
            new TrRmSection(0x001C, 0, 1, importBytes),
            new TrRmSection(0x0020, 0, 1,
                TrRmVariableTableWriter.EncodeBodies([body])),
        ]);
    }

    private static byte[] CreateTwoModuleArtifactWithImportedMethod(byte[] body)
    {
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0), ("main", 0), ("provider", 0),
            ("consumer", 0), ("required", 0), ("required-key", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 7);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 1, 1, 1, 1, 1),
            new TrRmModuleRow(5, 0, 0, 0, 1, 1, 2, 1),
        ], strings, 1, 0, 2, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 2, strings,
            1, 0, 2, 0, 0);
        var moduleRefBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(4, 0, 0),
        ], strings, modules);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(0),
            TrRmTypeSpecTableWriter.Builtin(8),
        ], 0, 1, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 2, 0, 1, 0);
        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, 0);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_0000, 0, 0, 0, 0, 0),
        ], typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(
            signatureBytes, 1, typeSpecs, 0, 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 3, 1, 0, 0, 0, 1),
            new TrRmMethodDefRow(0, 6, 1, 0, 0, 0, 0),
        ], strings, modules, typeDefs, signatures, 0, 1);
        var methods = new TrRmMethodDefTableReader(methodBytes, 2,
            strings, modules, typeDefs, signatures, 0, 1);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes, 1, strings, modules);
        var importBytes = TrRmImportTableWriter.Encode([
            new TrRmImportRow(1, 7, 0x1600_0002),
        ], strings, moduleRefs, typeDefs, methods, default);

        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 2, moduleBytes),
            new TrRmSection(0x0002, 0, 7, stringsBytes),
            new TrRmSection(0x0010, 0, 1, moduleRefBytes),
            new TrRmSection(0x0011, 0, 2, typeSpecBytes),
            new TrRmSection(0x0016, 0, 2, methodBytes),
            new TrRmSection(0x0017, 0, 1, signatureBytes),
            new TrRmSection(0x001C, 0, 1, importBytes),
            new TrRmSection(0x0020, 0, 1,
                TrRmVariableTableWriter.EncodeBodies([body])),
        ]);
    }

    private static byte[] Mutate(byte[] body, int offset, uint value)
    {
        var copy = (byte[])body.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(offset), value);
        return copy;
    }

    private static byte[] MutateByte(byte[] body, int offset, byte value)
    {
        var copy = (byte[])body.Clone();
        copy[offset] = value;
        return copy;
    }

    private static void Write(List<byte> bytes, uint value)
    {
        bytes.Add((byte)(value & 0xFF));
        bytes.Add((byte)((value >> 8) & 0xFF));
        bytes.Add((byte)((value >> 16) & 0xFF));
        bytes.Add((byte)((value >> 24) & 0xFF));
    }
}
