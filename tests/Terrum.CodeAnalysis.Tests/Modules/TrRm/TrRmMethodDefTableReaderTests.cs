// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmMethodDefTableReaderTests
{
    [Test]
    public void ModuleLevelCallableUsesSignatureWithoutNominalTypes()
    {
        Assert.That(GetName(Method(), 1), Is.EqualTo("Run"));
        Assert.Throws<InvalidDataException>(() => GetName(Method(), 0));
        Assert.Throws<InvalidDataException>(() => GetName(Method(), 2));
    }
    [TestCase(0,(byte)(1))]
    [TestCase(4,(byte)(0))]
    [TestCase(8,(byte)(0))]
    [TestCase(8,(byte)(2))]
    [TestCase(12,(byte)(3))]
    [TestCase(16,(byte)(1))]
    [TestCase(20,(byte)(1))]
    [TestCase(24,(byte)(0))]
    [TestCase(24,(byte)(2))]
    public void RejectsBadReferencesVisibilityAndUnownedBodies(int offset, byte replacement)
    {
        var bytes = Method();
        bytes[offset] = replacement;
        Assert.Throws<InvalidDataException>(() => Create(bytes));
    }

    [Test]
    public void RejectsTruncatedRowAndUnownedSignatureReceiver()
    {
        Assert.Throws<InvalidDataException>(() => Create(Method()[..^1]));
        var signature = Signature();
        Write(signature, 4, 1);
        Assert.Throws<InvalidDataException>(() => Create(Method(), signature: signature));
    }

    [Test]
    public void GenericSignatureCannotBeSharedBetweenMethodDefinitions()
    {
        var rows = new byte[56];
        var method = Method();
        Write(method, 24, 0);
        method.CopyTo(rows, 0);
        method.CopyTo(rows, 28);
        var signature = Signature();
        Write(signature, 16, 1);
        Write(signature, 20, 1);
        Assert.Throws<InvalidDataException>(() =>
            Create(rows, methodCount: 2, bodyCount: 0, signature: signature, genericCount: 1));
    }

    [Test]
    public void NominalMethodOwnerMustBelongToTheSameModule()
    {
        CreateTwoModules(wrongOwner: false);
        Assert.Throws<InvalidDataException>(() => CreateTwoModules(wrongOwner: true));
    }

    [Test]
    public void SkipsEmptyModuleRangesWhileValidatingOrderedMethods()
    {
        var names = new TrRmStringTableReader(TrRmStringTableWriter.EncodeTextRows(
            [("One", 0), ("Two", 0), ("Run", 0)]), 3);
        var moduleRows = new byte[88];
        Write(moduleRows, 0, 1);
        Write(moduleRows, 4, 1);
        Write(moduleRows, 28, 1);
        Write(moduleRows, 32, 1);
        Write(moduleRows, 36, 1);
        Write(moduleRows, 40, 1);
        Write(moduleRows, 44, 2);
        Write(moduleRows, 48, 1);
        Write(moduleRows, 72, 1);
        Write(moduleRows, 76, 1);
        Write(moduleRows, 80, 1);
        Write(moduleRows, 84, 1);
        var modules = new TrRmModuleTableReader(moduleRows, 2, names, 0, 0, 1, 0);
        var types = new TrRmTypeDefTableReader([], 0, names, modules, 0, 0, 0, 0);
        var signatures = new TrRmMethodSigTableReader(Signature(), 1, TypeSpecs(), 0, 0);
        var method = new byte[28];
        Write(method, 4, 3);
        Write(method, 8, 1);
        Write(method, 12, 2);
        Write(method, 24, 1);

        var methods = new TrRmMethodDefTableReader(method, 1, names, modules, types,
            signatures, 0, 1);

        Assert.That(methods.GetOwnerModule(1), Is.EqualTo(2));
    }

    [Test]
    public void ValidatesNominalOwnerAfterEmptyMethodRanges()
    {
        var names = new TrRmStringTableReader(TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("One", 0), ("Two", 0), ("Three", 0), ("Widget", 0), ("Run", 0),
        ]), 6);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1),
            new TrRmModuleRow(3, 0, 0, 0, 1, 1, 1, 1),
            new TrRmModuleRow(4, 0, 0, 0, 1, 1, 1, 1),
        ], names, 0, 1, 1, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 3, names, 0, 1, 1, 0);
        var typeBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 5, 4, 0, 0, 0, 0, 0, 0, 0),
        ], names, modules, 0, 0, 0, 0);
        var types = new TrRmTypeDefTableReader(typeBytes, 1, names, modules, 0, 0, 0, 0);
        var signatures = new TrRmMethodSigTableReader(Signature(), 1, TypeSpecs(), 0, 0);
        var method = new byte[28];
        Write(method, 0, 1);
        Write(method, 4, 6);
        Write(method, 8, 1);
        Write(method, 12, 2);

        var methods = new TrRmMethodDefTableReader(method, 1, names, modules, types,
            signatures, 0, 0);

        Assert.That(methods.GetOwnerModule(1), Is.EqualTo(3));
    }

    private static void CreateTwoModules(bool wrongOwner)
    {
        var names = new TrRmStringTableReader(TrRmStringTableWriter.EncodeTextRows(
            [("One", 0), ("Two", 0), ("", 0), ("Widget", 0), ("Run", 0)]), 5);
        var moduleRows = new byte[88];
        Write(moduleRows, 0, 1);
        Write(moduleRows, 4, 1);
        Write(moduleRows, 28, 1);
        Write(moduleRows, 32, 1);
        Write(moduleRows, 36, 1);
        Write(moduleRows, 40, 1);
        Write(moduleRows, 44, 2);
        Write(moduleRows, 48, 1);
        Write(moduleRows, 72, 1);
        Write(moduleRows, 76, 2);
        Write(moduleRows, 80, 2);
        Write(moduleRows, 84, 1);
        var modules = new TrRmModuleTableReader(moduleRows, 2, names, 0, 1, 2, 0);
        var typeRow = new byte[40];
        Write(typeRow, 0, 3);
        Write(typeRow, 4, 4);
        Write(typeRow, 8, 4);
        var types = new TrRmTypeDefTableReader(typeRow, 1, names, modules, 0, 0, 0, 0);
        var typeSpecs = TypeSpecs();
        var signatures = new TrRmMethodSigTableReader(Signature(), 1, typeSpecs, 0, 0);
        var methods = new byte[56];
        Write(methods, 0, wrongOwner ? 0u : 1u);
        Write(methods, 4, 5);
        Write(methods, 8, 1);
        Write(methods, 28, wrongOwner ? 1u : 0u);
        Write(methods, 32, 5);
        Write(methods, 36, 1);
        _ = new TrRmMethodDefTableReader(methods, 2, names, modules, types, signatures, 0, 0);
    }

    private static string GetName(byte[] bytes, int row)
    {
        var names = new TrRmStringTableReader(Names(), 2);
        var modules = new TrRmModuleTableReader(Module(), 1, names, 0, 0, 1, 0);
        var types = new TrRmTypeDefTableReader([], 0, names, modules, 0, 0, 0, 0);
        var typeSpecs = TypeSpecs();
        var signatures = new TrRmMethodSigTableReader(Signature(), 1, typeSpecs, 0, 0);
        var methods = new TrRmMethodDefTableReader(bytes, 1, names, modules, types, signatures, 0, 1);
        return methods.GetName(row);
    }

    private static void Create(byte[] bytes, int methodCount = 1, int bodyCount = 1,
        byte[]? signature = null, int genericCount = 0)
    {
        var names = new TrRmStringTableReader(Names(), 2);
        var modules = new TrRmModuleTableReader(Module(), 1, names, 0, 0, methodCount, 0);
        var types = new TrRmTypeDefTableReader([], 0, names, modules, 0, genericCount, 0, 0);
        var typeSpecs = TypeSpecs();
        var signatures = new TrRmMethodSigTableReader(signature ?? Signature(), 1, typeSpecs, 0, genericCount);
        _ = new TrRmMethodDefTableReader(bytes, methodCount, names, modules, types,
            signatures, 0, bodyCount);
    }

    private static TrRmTypeSpecTableReader TypeSpecs()
    {
        byte[] row = [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, 8];
        return new TrRmTypeSpecTableReader(row, 1, 0, 0, 0, 0);
    }

    private static byte[] Names()
        => TrRmStringTableWriter.EncodeNames([("Module", 0), ("Run", 0)]);

    private static byte[] Module()
    {
        var result = new byte[44];
        Write(result, 0, 1);
        Write(result, 4, 1);
        Write(result, 28, 1);
        Write(result, 32, 1);
        Write(result, 36, 1);
        Write(result, 40, 1);
        return result;
    }

    private static byte[] Signature()
    {
        var result = new byte[24];
        Write(result, 0, 1);
        return result;
    }

    private static byte[] Method()
    {
        var result = new byte[28];
        Write(result, 4, 2);
        Write(result, 8, 1);
        Write(result, 12, 2);
        Write(result, 24, 1);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
