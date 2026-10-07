// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmScalarInitOutputBodyReaderTests
{
    // A typed delegate keeps the provisional API internal without boxing its ref-struct inputs.
    private delegate object ReadBody(TrRmBodyTableReader bodies, int method,
        TrRmModuleTableReader modules, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types, ReadOnlySpan<byte> contracts);

    private static readonly ReadBody s_read = (typeof(TrRmBodyTableReader).Assembly
        .GetType("Terrum.CodeAnalysis.Modules.TrRmScalarInitOutputBodyReader", throwOnError: true)
        ?.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("The provisional Body reader API was not found."))
        .CreateDelegate<ReadBody>();

    [Fact]
    public void RetirementPreservesFormalPublication()
    {
        var fixture = InitializedOutput();
        var body = Inspect(fixture.Bytes, [2]);
        var place = Items(body, "Places").Single();
        Assert.True(Property<bool>(place, "Initialized"));
        Assert.Equal(0, Property<int>(place, "ParameterOrdinal"));
        Assert.Equal(3, Property<int>(place, "LastUseOrdinal"));
        var values = Items(body, "Values");
        Assert.Equal(1, Property<byte>(values[0], "Kind"));
        Assert.Equal(1, Property<int>(values[0], "PlaceId"));
        Assert.Equal(0, Property<int>(values[1], "PlaceId"));
        Assert.Equal(3, Items(body, "Operations").Length);
    }

    [Fact]
    public void RetiringVacantFormalDoesNotDischargePublication()
    {
        var fixture = Encode([[0x0B, 1, 1]], [1], [-1], [1], terminator: [0x83, 0]);
        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [2]));
    }

    [Fact]
    public void FailHasNoFormalPublicationGuarantee()
    {
        var fixture = Encode([[0x0B, 1, 1]], [1], [-1], [0],
            ending: [], terminator: [0x87, 1]);
        var place = Items(Inspect(fixture.Bytes, [2]), "Places").Single();
        Assert.False(Property<bool>(place, "Initialized"));
        Assert.Equal(0, Property<int>(place, "LastUseOrdinal"));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void FailureReasonsWithoutFaithfulNativeLoweringReject(byte reason)
    {
        var fixture = Encode([[0x0B, 1, 1]], [1], [-1], [0],
            ending: [], terminator: [0x87, reason]);
        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [2]));
    }

    [Fact]
    public void EntryOnlyPlaceHasOrdinalZeroOnZeroOperationFail()
    {
        var fixture = Encode([], [], [], [0], ending: [], terminator: [0x87, 1]);
        var body = Inspect(fixture.Bytes, [2]);
        var place = Items(body, "Places").Single();
        Assert.Equal(0, Property<int>(place, "LastUseOrdinal"));
        Assert.False(Property<bool>(place, "Initialized"));
        Assert.Equal(1, fixture.Bytes[fixture.PlaceUses]);
        Assert.Empty(Items(body, "Operations"));
        Assert.Empty(Items(body, "EndingPlaces"));

        fixture.Bytes[fixture.PlaceUses] = 0;
        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [2]));
    }

    [Fact]
    public void ZeroOperationReturnStillRequiresFormalPublicationAfterRetirement()
    {
        var fixture = Encode([], [], [], [0], terminator: [0x83, 0]);
        var local = Items(Inspect(fixture.Bytes, []), "Places").Single();
        Assert.Equal(0, Property<int>(local, "LastUseOrdinal"));
        Assert.False(Property<bool>(local, "Initialized"));
        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [2]));
    }

    [Fact]
    public void ValueAndPlaceFreeFailUsesEmptyTypeMap()
    {
        byte[] body =
        [
            0, 1, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 12, 0, 0, 0, 10, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x87, 1,
        ];
        var decoded = Inspect(body, []);
        Assert.Empty(Items(decoded, "Types"));
        Assert.Empty(Items(decoded, "Places"));
        Assert.Empty(Items(decoded, "Values"));
        Assert.Throws<InvalidDataException>(() => Inspect(
            Encode([], [], [], [], terminator: [0x87, 1]).Bytes, []));
    }

    [Fact]
    public void ByValueAndOutputFormalsUseSeparateOrdinalSpaces()
    {
        var fixture = Encode([[0x0B, 2, 1], [0x0E, 2, 1]], [0, 1], [1, 1], [2],
            entry: [1], terminator: [0x83, 0]);
        var body = Inspect(fixture.Bytes, [0, 2]);
        Assert.Equal(0, Property<int>(Items(body, "Values")[0], "ParameterOrdinal"));
        Assert.Equal(1, Property<int>(Items(body, "Places")[0], "ParameterOrdinal"));
        Assert.Equal(-1, Property<int>(Items(body, "Values")[0], "DefinitionOffset"));
    }

    [Fact]
    public void CheckedDivRemAccountsForBothDefinitionsIncludingUnusedQuotient()
    {
        var fixture = Encode([[0x18, 3, 4, 1, 2]], [0, 0, 0, 0], [0, 0, -1, 1], [],
            entry: [1, 2], terminator: [0x83, 4]);
        var values = Items(Inspect(fixture.Bytes, [0, 0], voidResult: false), "Values");
        Assert.Equal(Property<int>(values[2], "DefinitionOffset"),
            Property<int>(values[3], "DefinitionOffset"));
        Assert.Equal(-1, Property<int>(values[2], "LastUseOrdinal"));
    }

    [Fact]
    public void SignedConstantDataIsNormalizedWithoutNeedingOriginalEnvelopeWidth()
    {
        var fixture = Encode([[0x02, 1, 1, 0xFF]], [0], [1], [], terminator: [0x83, 1]);
        var operation = Items(Inspect(fixture.Bytes, [], voidResult: false), "Operations").Single();
        Assert.Equal(ulong.MaxValue, Property<ulong>(operation, "Data"));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void NonminimalStructuralWidthsReject(int width)
    {
        var fixture = Encode([[0x02, 1, 1, 42]], [0], [1], [],
            terminator: [0x83, 1], width: width);
        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [], voidResult: false));
    }

    [Theory]
    [InlineData(255, 1)]
    [InlineData(256, 2)]
    public void DescriptorAndOperationCountsSelectMinimalWidth(int count, int width)
    {
        var operations = Enumerable.Range(1, count).Select(value => value <= 255
            ? new byte[] { 2, (byte)value, 1, 42 }
            : new byte[] { 0xF0, 1, 2, (byte)(value & 255), (byte)(value >> 8), 1, 0, 42 })
            .ToArray();
        var fixture = Encode(operations, new byte[count], Enumerable.Repeat(-1, count).ToArray(),
            [], terminator: [0x83, 0], width: width);
        var body = Inspect(fixture.Bytes, []);
        Assert.Equal(count, Items(body, "Values").Length);
        Assert.Equal(count, Items(body, "Operations").Length);
    }

    [Fact]
    public void ExistingEffectsClauseIsPreservedForSemanticVerification()
        => Assert.Equal(1u, Property<uint>(Inspect(InitializedOutput().Bytes, [2],
            contractKind: 1), "Effects"));

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void NonEffectsContractsReject(byte kind)
        => Assert.Throws<InvalidDataException>(() => Inspect(InitializedOutput().Bytes, [2],
            contractKind: kind));

    [Fact]
    public void NormalCallInitializesVacantLocalBeforeLoad()
    {
        var fixture = Encode([[0x0B, 1, 1], [0x12, 0, 2, 1, 1], [0x0D, 2, 1]],
            [1, 0], [2, 3], [3], terminator: [0x83, 2]);
        var body = Inspect(fixture.Bytes, [], voidResult: false, targetModes: [2]);
        Assert.True(Property<bool>(Items(body, "Places")[0], "Initialized"));
        Assert.Equal(-1, Property<int>(Items(body, "Places")[0], "ParameterOrdinal"));
        Assert.Equal(2, Property<int>(Items(body, "Operations")[1], "MethodRow"));
    }

    [Fact]
    public void CallCanPublishMultipleDistinctLocals()
    {
        var fixture = Encode([[0x0B, 1, 1], [0x0B, 2, 2], [0x12, 0, 2, 2, 1, 2]],
            [1, 1], [2, 2], [3, 3], terminator: [0x83, 0]);
        var places = Items(Inspect(fixture.Bytes, [], targetModes: [2, 2]), "Places");
        Assert.All(places, place => Assert.True(Property<bool>(place, "Initialized")));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void OutputCallUsesHandleValueIdRatherThanPlaceDescriptorId(byte argument, bool valid)
    {
        var fixture = Encode([[0x02, 1, 1, 42], [0x0B, 2, 1], [0x12, 0, 2, 1, argument]],
            [0, 1], [-1, 2], [3], terminator: [0x83, 0]);
        if (!valid)
        {
            Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [], targetModes: [2]));
            return;
        }

        var body = Inspect(fixture.Bytes, [], targetModes: [2]);
        var handle = Items(body, "Values")[1];
        Assert.Equal(1, Property<int>(handle, "PlaceId"));
        Assert.Equal(new[] { 2 }, Items(Items(body, "Operations")[2], "Operands").Cast<int>());
        Assert.True(Property<bool>(Items(body, "Places").Single(), "Initialized"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CallArgumentsFollowOrderedParameterModes(bool valueFirst)
    {
        byte[] arguments = valueFirst ? [1, 2] : [2, 1];
        byte[] targetModes = valueFirst ? [0, 2] : [2, 0];
        var fixture = Encode([[0x0B, 2, 1], [0x12, 0, 2, 2, .. arguments]],
            [0, 1], [1, 1], [2], entry: [1], terminator: [0x83, 0]);
        var body = Inspect(fixture.Bytes, [0], targetModes: targetModes);
        var call = Items(body, "Operations")[1];
        Assert.Equal(arguments.Select(id => (int)id), Items(call, "Operands").Cast<int>());
        Assert.True(Property<bool>(Items(body, "Places").Single(), "Initialized"));
    }

    [Theory]
    [InlineData(true, 0, 2)]
    [InlineData(true, 1, 0)]
    [InlineData(true, 2, 1)]
    [InlineData(false, 0, 1)]
    [InlineData(false, 2, 0)]
    [InlineData(false, 1, 2)]
    public void CallRejectsZeroAndWrongDescriptorKindAtEachModePosition(
        bool valueFirst, byte firstArgument, byte secondArgument)
    {
        byte[] targetModes = valueFirst ? [0, 2] : [2, 0];
        var fixture = Encode([[0x0B, 2, 1], [0x12, 0, 2, 2, firstArgument, secondArgument]],
            [0, 1], [1, 1], [2], entry: [1], terminator: [0x83, 0]);
        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [0],
            targetModes: targetModes));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void CallRequiresExactSignatureArgumentCount(byte argumentCount)
    {
        var fixture = Encode([[0x0B, 2, 1], [0x12, 0, 2, argumentCount, 1, 2]],
            [0, 1], [1, 1], [2], entry: [1], terminator: [0x83, 0]);
        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [0],
            targetModes: [0, 2]));
    }

    [Theory]
    [InlineData(0x04)]
    [InlineData(0x08)]
    public void OutputBearingCallsCannotUseCompactLayouts(byte layout)
    {
        var fixture = Encode([[0x0B, 2, 1], [0x12, 0, 2, 2, 1, 2]],
            [0, 1], [1, 1], [2], entry: [1], terminator: [0x83, 0]);
        fixture.Bytes[0] = layout;
        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [0],
            targetModes: [0, 2]));
    }

    [Fact]
    public void DuplicateDestinationsRejectEvenWithDifferentHandleIds()
    {
        var fixture = Encode([[0x0B, 1, 1], [0x0B, 2, 1], [0x12, 0, 2, 2, 1, 2]],
            [1, 1], [2, 2], [3], terminator: [0x83, 0]);
        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [], targetModes: [2, 2]));
    }

    [Fact]
    public void OutputFormalCannotBeForwarded()
    {
        var fixture = Encode([[0x0B, 1, 1], [0x12, 0, 2, 1, 1]],
            [1], [1], [2], terminator: [0x83, 0]);
        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [2], targetModes: [2]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitializedDestinationCannotBeInitializedAgain(bool throughCall)
    {
        byte[] repeated = throughCall ? [0x12, 0, 2, 1, 1] : [0x0E, 1, 2];
        var fixture = Encode([[0x0B, 1, 1], [0x02, 2, 1, 42], [0x0E, 1, 2], repeated],
            [1, 0], [3, throughCall ? 2 : 3], [4], terminator: [0x83, 0]);
        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [], targetModes: [2]));
    }

    [Theory]
    [InlineData(0x03)]
    [InlineData(0x0D)]
    public void HandleCopyAndVacantLoadReject(byte opcode)
    {
        var fixture = Encode([[0x0B, 1, 1], [opcode, 2, 1]], [1, 0],
            [1, -1], [2], terminator: [0x83, 0]);
        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, []));
    }

    [Theory]
    [InlineData(0x0C)]
    [InlineData(0x0F)]
    [InlineData(0x10)]
    [InlineData(0x11)]
    [InlineData(0x13)]
    [InlineData(0x14)]
    public void UnsupportedPlaceAndAggregateOperationsReject(byte opcode)
    {
        var fixture = InitializedOutput();
        fixture.Bytes[fixture.OperationStart] = opcode;
        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [2]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    public void OtherParameterModesReject(byte mode)
        => Assert.Throws<InvalidDataException>(() => Inspect(InitializedOutput().Bytes, [mode]));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CallModeAndArityMismatchesReject(byte variant)
    {
        byte[] call = variant switch
        {
            0 => [0x12, 0, 2, 1, 1],
            1 => [0x12, 0, 2, 0],
            _ => [0x12, 0, 0, 1, 1],
        };
        var fixture = Encode([[0x0B, 1, 1], call], [1], [1], [2], terminator: [0x83, 0]);
        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [],
            targetModes: variant == 0 ? [0] : [2]));
    }

    [Theory]
    [InlineData(0x04)]
    [InlineData(0x08)]
    [InlineData(0x03)]
    [InlineData(0x10)]
    public void OtherBodyLayoutsReject(byte layout)
    {
        var fixture = InitializedOutput();
        fixture.Bytes[0] = layout;
        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [2]));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 255)]
    [InlineData(3, 255)]
    [InlineData(4, 1)]
    [InlineData(5, 255)]
    public void UnsupportedAndUnboundedHeaderCountsReject(int offset, byte value)
    {
        var fixture = InitializedOutput();
        fixture.Bytes[offset] = value;
        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [2]));
    }

    [Fact]
    public void IndexesCannotPointIntoOperandsOrHideSuccessors()
    {
        var fixture = InitializedOutput();
        fixture.Bytes[fixture.Definition]++;
        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [2]));

        fixture = InitializedOutput();
        fixture.Bytes[fixture.TerminatorOffset]++;
        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [2]));

        fixture = InitializedOutput();
        fixture.Bytes[^2] = 0x80;
        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [2]));
    }

    [Fact]
    public void OutputCallTargetCannotUseCompactBodyOrPublicVisibility()
    {
        var fixture = Encode([[0x0B, 1, 1], [0x12, 0, 2, 1, 1]],
            [1], [1], [2], terminator: [0x83, 0]);
        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [],
            targetModes: [2], targetLayout: 4));
        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [],
            targetModes: [2], targetFlags: 2));
    }

    [Theory]
    [InlineData("PlaceFlags", 0)]
    [InlineData("PlaceFlags", 5)]
    [InlineData("PlaceRegion", 1)]
    [InlineData("PlaceUses", 0)]
    [InlineData("ValueKind", 0)]
    [InlineData("ValueUses", 0)]
    [InlineData("ValueLastUse", 1)]
    [InlineData("PlaceLastUse", 2)]
    [InlineData("Definition", 0)]
    [InlineData("TerminatorOffset", 0)]
    public void ForgedInventoriesAndIndexesReject(string field, byte value)
    {
        var fixture = InitializedOutput();
        fixture.Bytes[Property<int>(fixture, field)] = value;
        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [2]));
    }

    [Fact]
    public void EveryTruncatedExtentAndTrailingDataReject()
    {
        var fixture = InitializedOutput();
        for (var length = 0; length < fixture.Bytes.Length; length++)
        {
            var truncated = fixture.Bytes[..length];
            Assert.Throws<InvalidDataException>(() => Inspect(truncated, [2]));
        }

        Assert.Throws<InvalidDataException>(() => Inspect([.. fixture.Bytes, 0], [2]));
    }

    [Fact]
    public void ReturnRequiresRetirementAndExactResultType()
    {
        var fixture = Encode([[0x0B, 1, 1], [0x02, 2, 1, 42], [0x0E, 1, 2]],
            [1, 0], [2, 2], [2], ending: [], terminator: [0x83, 0]);
        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [2]));
        Assert.Throws<InvalidDataException>(() => Inspect(InitializedOutput().Bytes, [2],
            voidResult: false));
    }

    [Fact]
    public void GeneralPlaceBodyWithoutOutputFormalsStillRequiresVoidOrInt32Result()
    {
        var fixture = Encode([[0x0B, 2, 1]], [0, 1], [1, -1], [1],
            entry: [1], terminator: [0x83, 1], valueTypeIds: [2, 1],
            typeMapRows: [1, 3]);

        Assert.Throws<InvalidDataException>(() => Inspect(fixture.Bytes, [0],
            resultTypeSpecRow: 3, paramTypeSpecRows: [3]));
    }

    private sealed record Fixture(byte[] Bytes, int OperationStart, int PlaceFlags,
        int PlaceRegion, int PlaceUses, int ValueKind, int ValueUses, int ValueLastUse,
        int PlaceLastUse, int Definition, int TerminatorOffset);

    private static Fixture InitializedOutput()
        => Encode([[0x0B, 1, 1], [0x02, 2, 1, 42], [0x0E, 1, 2]],
            [1, 0], [2, 2], [3], terminator: [0x83, 0]);

    private static Fixture Encode(byte[][] operations, byte[] kinds, int[] valueLastUses,
        int[] placeLastUses, byte[] terminator, int[]? entry = null, int[]? ending = null,
        int width = 1, int[]? valueTypeIds = null, int[]? placeTypeIds = null,
        int[]? typeMapRows = null)
    {
        entry ??= [];
        ending ??= Enumerable.Range(1, placeLastUses.Length).ToArray();
        valueTypeIds ??= Enumerable.Repeat(1, kinds.Length).ToArray();
        placeTypeIds ??= Enumerable.Repeat(1, placeLastUses.Length).ToArray();
        typeMapRows ??= [1];
        using var blockStream = new MemoryStream();
        using var block = new BinaryWriter(blockStream);
        Local(block, 0, width);
        List(block, entry, width);
        block.Write(new byte[3 * width]);
        List(block, Enumerable.Range(1, placeLastUses.Length).ToArray(), width);
        List(block, ending, width);
        var valueLastUse = Uses(block, valueLastUses, width);
        var placeLastUse = Uses(block, placeLastUses, width);
        Local(block, operations.Length, width);
        var operationStart = (int)blockStream.Position;
        var definitions = Enumerable.Repeat(uint.MaxValue, kinds.Length).ToArray();
        foreach (var operation in operations)
        {
            var opcode = operation[0];
            var operand = 1;
            var referenceWidth = 1;
            if (opcode == 0xF0)
            {
                opcode = operation[2];
                operand = 3;
                referenceWidth = 1 << (operation[1] & 3);
            }

            var result = referenceWidth == 1 ? operation[operand]
                : BinaryPrimitives.ReadUInt16LittleEndian(operation.AsSpan(operand));
            if (opcode != 0x0E && result != 0)
            {
                definitions[result - 1] = (uint)blockStream.Position;
                if (opcode == 0x18)
                {
                    definitions[operation[operand + referenceWidth] - 1] = (uint)blockStream.Position;
                }
            }

            block.Write(operation);
        }

        var terminatorOffset = (uint)blockStream.Position;
        block.Write(terminator);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)(width == 1 ? 0 : width == 2 ? 1 : 2));
        foreach (var count in new[] { 1, kinds.Length, placeLastUses.Length, 0, typeMapRows.Length })
        {
            Local(writer, count, width);
        }

        foreach (var row in typeMapRows)
        {
            writer.Write((uint)row);
        }
        var placeRegion = (int)stream.Position + width;
        var placeFlags = (int)stream.Position + 2 * width;
        var placeUses = (int)stream.Position + 2 * width + 1;
        for (var place = 0; place < placeLastUses.Length; place++)
        {
            Local(writer, placeTypeIds[place], width);
            Local(writer, 0, width);
            writer.Write((byte)4);
            Local(writer, placeLastUses[place] < 0 ? 0 : 1, width);
        }

        var valueKind = (int)stream.Position + width;
        var valueUses = (int)stream.Position + width + 1;
        for (var value = 0; value < kinds.Length; value++)
        {
            Local(writer, valueTypeIds[value], width);
            writer.Write(kinds[value]);
            Local(writer, valueLastUses[value] < 0 ? 0 : 1, width);
        }

        Local(writer, 0, width);
        Local(writer, kinds.Length, width);
        var definition = (int)stream.Position;
        foreach (var offset in definitions)
        {
            writer.Write(offset);
        }

        writer.Write(0u);
        writer.Write((uint)blockStream.Length);
        var terminatorIndex = (int)stream.Position;
        writer.Write(terminatorOffset);
        var blockStart = (int)stream.Position;
        writer.Write(blockStream.ToArray());
        return new Fixture(stream.ToArray(), blockStart + operationStart, placeFlags,
            placeRegion, placeUses, valueKind, valueUses, blockStart + valueLastUse,
            blockStart + placeLastUse, definition, terminatorIndex);
    }

    private static void List(BinaryWriter writer, int[] ids, int width)
    {
        Local(writer, ids.Length, width);
        foreach (var id in ids)
        {
            Local(writer, id, width);
        }
    }

    private static int Uses(BinaryWriter writer, int[] uses, int width)
    {
        Local(writer, uses.Count(use => use >= 0), width);
        var firstOrdinal = (int)writer.BaseStream.Position + width;
        for (var index = 0; index < uses.Length; index++)
        {
            if (uses[index] >= 0)
            {
                Local(writer, index + 1, width);
                Local(writer, uses[index], width);
            }
        }

        return firstOrdinal;
    }

    private static void Local(BinaryWriter writer, int value, int width)
    {
        if (width == 1)
        {
            writer.Write((byte)value);
        }
        else if (width == 2)
        {
            writer.Write((ushort)value);
        }
        else
        {
            writer.Write((uint)value);
        }
    }

    private static object Inspect(byte[] body, byte[] modes, bool voidResult = true,
        byte[]? targetModes = null, byte targetFlags = 0, byte targetLayout = 0,
        byte? contractKind = null, int resultTypeSpecRow = 0,
        byte[]? paramTypeSpecRows = null)
    {
        var methodCount = targetModes is null ? 1 : 2;
        var parameterCount = modes.Length + (targetModes?.Length ?? 0);
        var names = new TrRmStringTableReader(
            TrRmStringTableWriter.EncodeTextRows([("Module", 0), ("Run", 0)]), 2);
        var moduleBytes = new byte[44];
        Write(moduleBytes, 0, 1);
        foreach (var offset in new[] { 28, 32, 36, 40 })
        {
            Write(moduleBytes, offset, 1);
        }

        var modules = new TrRmModuleTableReader(moduleBytes, 1, names, 0, 0, methodCount, 0);
        var typeDefinitions = new TrRmTypeDefTableReader([], 0, names, modules,
            0, 0, 0, contractKind is null ? 0 : 1);
        var types = new TrRmTypeSpecTableReader(
            TrRmVariableTableWriter.EncodeBlobs([
                new byte[] { 1, 0, 8 }, new byte[] { 1, 0, 0 }, new byte[] { 1, 0, 1 },
            ]), 3, 0, 0, 0, 0);
        var signatureBytes = new byte[methodCount * 24];
        Signature(signatureBytes, 0,
            (uint)(resultTypeSpecRow != 0 ? resultTypeSpecRow : voidResult ? 2 : 1),
            modes.Length == 0 ? 0 : 1, modes.Length);
        if (targetModes is not null)
        {
            Signature(signatureBytes, 24, 2, targetModes.Length == 0 ? 0 : modes.Length + 1,
                targetModes.Length);
        }

        var signatures = new TrRmMethodSigTableReader(signatureBytes, methodCount, types,
            parameterCount, 0);
        var parameterBytes = new byte[parameterCount * 16];
        Params(parameterBytes, 0, 1, modes, paramTypeSpecRows);
        if (targetModes is not null)
        {
            Params(parameterBytes, modes.Length, 2, targetModes);
        }

        var parameters = parameterCount == 0 ? default
            : new TrRmParamTableReader(parameterBytes, parameterCount, signatures, types);
        var methodBytes = new byte[methodCount * 28];
        for (var method = 0; method < methodCount; method++)
        {
            Write(methodBytes, method * 28 + 4, 2);
            Write(methodBytes, method * 28 + 8, (uint)(method + 1));
            Write(methodBytes, method * 28 + 24, (uint)(method + 1));
        }

        if (methodCount == 2)
        {
            Write(methodBytes, 40, targetFlags);
        }

        byte[] contractSection = [];
        if (contractKind is byte kind)
        {
            Write(methodBytes, 16, 1);
            Write(methodBytes, 20, 1);
            var clause = new byte[12];
            Write(clause, 0, 0x1600_0001);
            clause[4] = kind;
            Write(clause, 8, 1);
            contractSection = TrRmVariableTableWriter.EncodeBlobs([clause]);
        }

        var methods = new TrRmMethodDefTableReader(methodBytes, methodCount, names, modules,
            typeDefinitions, signatures, contractKind is null ? 0 : 1, methodCount);
        byte[] target = targetModes is [2, 2]
            ? Encode([[0x0B, 1, 1], [0x0B, 2, 2], [0x02, 3, 1, 42],
                [0x0E, 1, 3], [0x0E, 2, 3]], [1, 1, 0], [3, 4, 4], [5, 5],
                terminator: [0x83, 0]).Bytes
            : targetModes is [0, 2] or [2, 0]
                ? Encode([[0x0B, 2, 1], [0x0E, 2, 1]], [0, 1], [1, 1], [2],
                    entry: [1], terminator: [0x83, 0]).Bytes
            : targetModes is [0]
                ? Encode([], [0], [-1], [], entry: [1], terminator: [0x83, 0]).Bytes
                : InitializedOutput().Bytes;
        target[0] = targetLayout;
        var bodyBytes = TrRmVariableTableWriter.EncodeBlobs(
            targetModes is null ? [body] : [body, target]);
        var bodies = new TrRmBodyTableReader(bodyBytes, methodCount);
        return s_read(bodies, 1, modules, methods, signatures, parameters, types, contractSection);
    }

    private static void Signature(byte[] bytes, int offset, uint result, int first, int count)
    {
        Write(bytes, offset, result);
        Write(bytes, offset + 8, (uint)first);
        Write(bytes, offset + 12, (uint)count);
    }

    private static void Params(byte[] bytes, int first, int owner, byte[] modes,
        byte[]? typeSpecRows = null)
    {
        for (var index = 0; index < modes.Length; index++)
        {
            var offset = (first + index) * 16;
            Write(bytes, offset, (uint)owner);
            Write(bytes, offset + 4, (uint)index);
            Write(bytes, offset + 8, typeSpecRows is null ? 1u : typeSpecRows[index]);
            bytes[offset + 12] = modes[index];
        }
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static object[] Items(object value, string name)
        => Property<IEnumerable>(value, name).Cast<object>().ToArray();

    private static T Property<T>(object value, string name)
        => (T)(value.GetType().GetProperty(name)?.GetValue(value)
            ?? throw new InvalidOperationException($"Missing {name} on {value.GetType().Name}."));
}
