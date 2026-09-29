// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmGenericParamTableWriterTests
{
    private static readonly TrRmGenericParamRow TypeFormal = new(0x1300_0001, 0, 5, 1, 1);
    private static readonly TrRmGenericParamRow MethodFormal = new(0x1600_0001, 0, 6, 0, 0);
    private static readonly TrRmConstraintRow Constraint = new(1, ushort.MaxValue, 1, 0x1600_0001);

    [Fact]
    public void EmitsDistinctTypeAndMethodFormalsWithOwnedConstraint()
    {
        var names = Names();
        var strings = new TrRmStringTableReader(names, 6);
        var moduleBytes = Module();
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings, 0, 1, 1, 0);
        var typeBytes = Type();
        var types = new TrRmTypeDefTableReader(typeBytes, 1, strings, modules, 0, 2, 0, 0);
        var specBytes = Spec();
        var specs = new TrRmTypeSpecTableReader(specBytes, 1, 1, 0, 2, 0);
        var sigBytes = Signature();
        var signatures = new TrRmMethodSigTableReader(sigBytes, 1, specs, 0, 2);
        var methodBytes = Method();
        var methods = new TrRmMethodDefTableReader(methodBytes, 1, strings, modules,
            types, signatures, 0, 0);
        var formalRows = new[] { TypeFormal, MethodFormal };
        var formalBytes = TrRmGenericParamTableWriter.Encode(
            formalRows, strings, types, methods, signatures, 1);
        var formals = new TrRmGenericParamTableReader(formalBytes, 2,
            strings, types, methods, signatures, 1);
        var constraintRows = new[] { Constraint };
        var constraintBytes = TrRmConstraintTableWriter.Encode(constraintRows, formals, specs);
        Assert.Equal(40, formalBytes.Length);
        Assert.Equal(16, constraintBytes.Length);
        Assert.Equal(1, formals.GetConstraintOwner(1));
        Assert.Equal(ushort.MaxValue, BinaryPrimitives.ReadUInt16LittleEndian(constraintBytes.AsSpan(4)));
        Assert.Equal(Constraint.WitnessRef, BinaryPrimitives.ReadUInt32LittleEndian(constraintBytes.AsSpan(12)));

        formalRows[0] = TypeFormal with { NameStringRow = 0 };
        constraintRows[0] = Constraint with { TargetTypeSpecRow = 0 };
        Assert.Equal(5u, BinaryPrimitives.ReadUInt32LittleEndian(formalBytes.AsSpan(8)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(constraintBytes.AsSpan(8)));

        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 6, names),
            new TrRmSection(0x0011, 0, 1, specBytes),
            new TrRmSection(0x0013, 0, 1, typeBytes),
            new TrRmSection(0x0016, 0, 1, methodBytes),
            new TrRmSection(0x0017, 0, 1, sigBytes),
            new TrRmSection(0x0019, 0, 2, formalBytes),
            new TrRmSection(0x001A, 0, 1, constraintBytes),
        ]);
        TrRmStructuralMetadataValidator.Validate(artifact);
    }

    [Fact]
    public void RejectsUnownedOrMalformedFormalsAndConstraints()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EncodeFormals([]));
        Assert.Throws<InvalidDataException>(() => EncodeFormals([TypeFormal with { OwnerDefId = 0 }]));
        Assert.Throws<InvalidDataException>(() => EncodeFormals([
            TypeFormal with { Ordinal = 1 }, MethodFormal,
        ]));
        Assert.Throws<InvalidDataException>(() => EncodeFormals([
            TypeFormal with { NameStringRow = 0 }, MethodFormal,
        ]));
        Assert.Throws<InvalidDataException>(() => EncodeFormals([
            TypeFormal with { ConstraintFirst = 0 }, MethodFormal,
        ]));
        Assert.Throws<InvalidDataException>(() => EncodeFormals([
            TypeFormal with { ConstraintCount = 0 }, MethodFormal,
        ]));
        Assert.Throws<InvalidDataException>(() => EncodeFormals([
            TypeFormal, MethodFormal with { OwnerDefId = 0x1300_0001 },
        ]));
        Assert.Throws<ArgumentOutOfRangeException>(() => EncodeConstraints([]));
        Assert.Throws<InvalidDataException>(() => EncodeConstraints([
            Constraint with { OwnerGenericParamRow = 2 },
        ]));
        Assert.Throws<InvalidDataException>(() => EncodeConstraints([
            Constraint with { TargetTypeSpecRow = 0 },
        ]));
    }

    private static byte[] EncodeFormals(TrRmGenericParamRow[] rows)
    {
        var strings = new TrRmStringTableReader(Names(), 6);
        var modules = new TrRmModuleTableReader(Module(), 1, strings, 0, 1, 1, 0);
        var types = new TrRmTypeDefTableReader(Type(), 1, strings, modules, 0, 2, 0, 0);
        var specs = new TrRmTypeSpecTableReader(Spec(), 1, 1, 0, 2, 0);
        var signatures = new TrRmMethodSigTableReader(Signature(), 1, specs, 0, 2);
        var methods = new TrRmMethodDefTableReader(Method(), 1, strings, modules,
            types, signatures, 0, 0);
        return TrRmGenericParamTableWriter.Encode(rows, strings, types, methods, signatures, 1);
    }

    private static byte[] EncodeConstraints(TrRmConstraintRow[] rows)
    {
        var strings = new TrRmStringTableReader(Names(), 6);
        var modules = new TrRmModuleTableReader(Module(), 1, strings, 0, 1, 1, 0);
        var types = new TrRmTypeDefTableReader(Type(), 1, strings, modules, 0, 2, 0, 0);
        var specs = new TrRmTypeSpecTableReader(Spec(), 1, 1, 0, 2, 0);
        var signatures = new TrRmMethodSigTableReader(Signature(), 1, specs, 0, 2);
        var methods = new TrRmMethodDefTableReader(Method(), 1, strings, modules,
            types, signatures, 0, 0);
        var formals = new TrRmGenericParamTableReader(
            EncodeFormals([TypeFormal, MethodFormal]), 2,
            strings, types, methods, signatures, 1);
        return TrRmConstraintTableWriter.Encode(rows, formals, specs);
    }

    private static byte[] Names()
        => TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Widget", 0), ("Build", 0), ("T", 0), ("U", 0),
        ]);

    private static byte[] Module()
    {
        var row = new byte[44];
        Write(row, 0, 2);
        Write(row, 4, 1);
        Write(row, 28, 1);
        Write(row, 32, 1);
        Write(row, 36, 1);
        Write(row, 40, 1);
        return row;
    }

    private static byte[] Type()
    {
        var row = new byte[40];
        Write(row, 0, 1);
        Write(row, 4, 3);
        Write(row, 8, 4);
        Write(row, 20, 1);
        Write(row, 24, 1);
        return row;
    }

    private static byte[] Signature()
    {
        var row = new byte[24];
        Write(row, 0, 1);
        Write(row, 16, 2);
        Write(row, 20, 1);
        return row;
    }

    private static byte[] Method()
    {
        var row = new byte[28];
        Write(row, 4, 4);
        Write(row, 8, 1);
        return row;
    }

    private static byte[] Spec()
        => [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, 8];

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
