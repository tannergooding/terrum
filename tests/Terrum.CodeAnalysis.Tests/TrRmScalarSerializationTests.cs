// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmScalarSerializationTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong ScalarUInt64(ulong value);

    public static IEnumerable<object[]> Scalars
    {
        get
        {
            (string Type, string Literal, string Hex, int TypeId)[] values = [
                ("bool", "false", "00", 1), ("bool", "true", "01", 1),
                ("char16", "255", "FF", 2), ("char16", "256", "0001", 2),
                ("char16", "65535", "FFFF", 2),
                ("int32", "255", "FF", 3), ("int32", "256", "0001", 3),
                ("int32", "65535", "FFFF", 3), ("int32", "65536", "00000100", 3),
                ("int32", "2147483647", "FFFFFF7F", 3),
                ("int32", "-2147483648", "00000080", 3), ("int32", "-1", "FFFFFFFF", 3),
                ("uint32", "255", "FF", 4), ("uint32", "256", "0001", 4),
                ("uint32", "65535", "FFFF", 4), ("uint32", "65536", "00000100", 4),
                ("uint32", "2147483648", "00000080", 4),
                ("uint32", "4294967295", "FFFFFFFF", 4),
                ("int64", "4294967295", "FFFFFFFF", 5),
                ("int64", "4294967296", "0000000001000000", 5),
                ("int64", "9223372036854775807", "FFFFFFFFFFFFFF7F", 5),
                ("int64", "-9223372036854775808", "0000000000000080", 5),
                ("int64", "-1", "FFFFFFFFFFFFFFFF", 5),
                ("uint64", "4294967295", "FFFFFFFF", 6),
                ("uint64", "4294967296", "0000000001000000", 6),
                ("uint64", "9223372036854775808", "0000000000000080", 6),
                ("uint64", "18446744073709551615", "FFFFFFFFFFFFFFFF", 6),
                ("uint64", "0x0123456789ABCDEF", "EFCDAB8967452301", 6),
            ];
            foreach (var (type, literal, hex, typeId) in values)
            {
                yield return [type, literal, hex, typeId, false];
                yield return [type, literal, hex, typeId, true];
            }
        }
    }
    [TestCaseSource(nameof(Scalars))]
    public void CanonicalScalarBytesRoundTripThroughLinearAndIndexedGraphs(
        string type, string literal, string hex, int typeId, bool indexed)
    {
        var function = indexed
            ? $"public {type} Value(bool choose) {{ if (choose) {{ return {literal}; }} else {{ return {literal}; }} }}"
            : $"public {type} Value() {{ return {literal}; }}";
        var expectedValue = type == "bool" ? literal : $"{type}({literal})";
        var source = function + $" int32 Main() {{ return Value({(indexed ? "true" : "")}) == {expectedValue} ? 42 : 0; }}";
        var original = Compile(source, BoundRuntimePolicy.FailFast);
        var artifact = TrRmExecutableCompiler.Emit(original, "full-width");
        TrRmStructuralMetadataValidator.ValidateCompact(artifact);
        var body = Body(artifact, 1);
        var payload = Convert.FromHexString(hex);
        if (!indexed)
        {
            var expected = new List<byte> { 4, 6 };
            foreach (var row in new[] { 2, 3, 4, 5, 6, 7 })
            {
                var bytes = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(bytes, row);
                expected.AddRange(bytes);
            }
            expected.Add(1);
            if (payload.Length != 1)
            {
                expected.AddRange([0xF0, Layout(payload.Length)]);
            }
            expected.AddRange([1, 1, (byte)typeId]);
            expected.AddRange(payload);
            expected.AddRange([0x83, 1]);
            Assert.That(body, Is.EqualTo(expected.ToArray()));
        }
        else
        {
            AssertIndexedConstants(body, payload, typeId);
        }
        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
        var checkedSource = Compile(source, BoundRuntimePolicy.CheckedExceptions);
        Assert.That(NativeJit.Execute(checkedSource, NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
        // The persisted scalar contract remains fail-fast; serialization does not add a policy.
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(checkedSource, "checked"));
    }
    [TestCase(1,"EF")]
    [TestCase(2,"EFCD")]
    [TestCase(4,"EFCDAB89")]
    [TestCase(8,"EFCDAB8967452301")]
    public void InternalValidatedWidthSelectsLowOctetsWithoutUnsignedRangeValidation(int width, string hex)
    {
        var write = ((global::System.Func<MethodInfo>)(() => {
            var __nunitActual0 = typeof(TrRmExecutableCompiler).GetMethod("Write",
            BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(__nunitActual0, Is.InstanceOf<MethodInfo>());
            return (MethodInfo)__nunitActual0!;
        }))();
        var bytes = new List<byte>();
        write.Invoke(null, [bytes, 0x0123456789ABCDEFUL, width]);
        Assert.That(bytes.ToArray(), Is.EqualTo(Convert.FromHexString(hex)));
        bytes.Clear();
        write.Invoke(null, [bytes, ulong.MaxValue, width]);
        Assert.That(bytes, Is.EqualTo(Enumerable.Repeat(byte.MaxValue, width)));
    }
    [TestCase(-1L,1,"FF")]
    [TestCase(-128L,1,"80")]
    [TestCase(-129L,2,"7FFF")]
    [TestCase(-32768L,2,"0080")]
    [TestCase(-32769L,4,"FF7FFFFF")]
    [TestCase(-2147483648L,4,"00000080")]
    [TestCase(long.MinValue,8,"0000000000000080")]
    public void SignedNarrowFieldsRetainCanonicalTwosComplement(long value, int width, string hex)
    {
        var write = ((global::System.Func<MethodInfo>)(() => {
            var __nunitActual1 = typeof(TrRmExecutableCompiler).GetMethod("Write",
            BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(__nunitActual1, Is.InstanceOf<MethodInfo>());
            return (MethodInfo)__nunitActual1!;
        }))();
        var bytes = new List<byte> { 4, 1, (byte)(width == 8 ? 5 : 1), 0, 0, 0, 1 };
        if (width != 1)
        {
            bytes.AddRange([0xF0, Layout(width)]);
        }
        bytes.AddRange([2, 1, 1]);
        var start = bytes.Count;
        write.Invoke(null, [bytes, unchecked((ulong)value), width]);
        Assert.That(bytes.Skip(start).ToArray(), Is.EqualTo(Convert.FromHexString(hex)));
        bytes.AddRange([0x83, 1]);
        var artifact = TrRmExecutionAuditTests.Artifact(bytes.ToArray(),
            resultRow: width == 8 ? 5 : 1, entry: width != 8, exported: width == 8);
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        var loaded = TrRmExecutableCompiler.Load(artifact);
        var local = ((global::System.Func<BoundLocal>)(() => { var __nunitActual2 = loaded.Functions[0].Body.Statements[0]; Assert.That(__nunitActual2, Is.TypeOf<BoundLocal>()); return (BoundLocal)__nunitActual2!; }))();
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual3 = local.Initializer; Assert.That(__nunitActual3, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual3!; }))().Value, Is.EqualTo(value));
        if (width != 8)
        {
            Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo((int)value));
        }
        else
        {
            Assert.That(NativeEmitter.EmitLibrary(loaded, ["Main"], NativeRuntimePolicy.FailFast), Is.Not.Empty);
        }
    }
    [TestCase(127,true)]
    [TestCase(128,false)]
    public void IndexedValue256WidensReferencesWithoutChangingTheResourceBoundary(int updates, bool supported)
    {
        var source = "int32 Main() { mut int32 x = 0; "
            + string.Concat(Enumerable.Repeat("x++; ", updates)) + "return +x; }";
        var original = Compile(source, BoundRuntimePolicy.FailFast);
        if (!supported)
        {
            Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(original, "limit"));
            return;
        }
        var artifact = TrRmExecutableCompiler.Emit(original, "limit");
        var body = Body(artifact, 1);
        Assert.That(body[0], Is.EqualTo(9));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(3)), Is.EqualTo(256));
        Assert.That(body[^5..], Is.EqualTo(new byte[] { 0xF0, 1, 0x83, 0, 1 }));
        TrRmStructuralMetadataValidator.ValidateCompact(artifact);
        Assert.That(NativeJit.Execute(TrRmExecutableCompiler.Load(artifact), NativeRuntimePolicy.FailFast), Is.EqualTo(updates));
        Assert.That(NativeJit.Execute(Compile(source, BoundRuntimePolicy.CheckedExceptions),
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(updates));
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task PersistedFullWidthScalarLibraryServicesNativeExportsAsync(bool indexed)
    {
        var source = indexed
            ? "public uint64 Value(uint64 x) { if (x == 0) { return 18446744073709551615; } else { return x ^ 18446744073709551615; } }"
            : "public uint64 Value(uint64 x) { return x ^ 18446744073709551615; }";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))();
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(program, "scalar-service"));
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-scalar-{Guid.NewGuid():N}.dll");
        try
        {
            foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
            {
                BoundProgram executable;
                if (policy == NativeRuntimePolicy.FailFast)
                {
                    executable = loaded;
                }
                else
                {
                    var peer = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
                    Assert.That(peer.HasErrors, Is.False, string.Join(Environment.NewLine, peer.Diagnostics));
                    executable = ((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = peer.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))();
                    Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(executable, "checked"));
                }
                await File.WriteAllBytesAsync(path, NativeEmitter.EmitLibrary(executable, ["Value"], policy));
                var handle = NativeLibrary.Load(path);
                try
                {
                    var invoke = Marshal.GetDelegateForFunctionPointer<ScalarUInt64>(NativeLibrary.GetExport(handle, "Value"));
                    Assert.That(invoke(0), Is.EqualTo(ulong.MaxValue));
                    Assert.That(invoke(ulong.MaxValue), Is.EqualTo(0UL));
                    Assert.That(invoke(0x0123456789ABCDEF), Is.EqualTo(0xFEDCBA9876543210UL));
                }
                finally
                {
                    NativeLibrary.Free(handle);
                }
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
    [TestCase(new byte[] { 4, 1, 1, 0, 0, 0, 1, 0xF0, 0x30, 1, 1, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0x83, 1 })]
    [TestCase(new byte[] { 4, 1, 2, 0, 0, 0, 1, 1, 1, 1, 2, 0x83, 1 })]
    [TestCase(new byte[] { 4, 1, 3, 0, 0, 0, 1, 0xF0, 0x20, 1, 1, 1, 0, 0, 1, 0, 0x83, 1 })]
    [TestCase(new byte[] { 4, 1, 1, 0, 0, 0, 1, 0xF0, 0x20, 1, 1, 1, 0xFF, 0xFF })]
    [TestCase(new byte[] { 4, 1, 1, 0, 0, 0, 1, 0xF0, 0x10, 1, 1, 1, 42, 0, 0x83, 1 })]
    public void FullWidthWriteDoesNotRelaxPublicMagnitudeTruncationOrCanonicalWidthAudits(byte[] body)
    {
        var resultRow = body[2];
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(body, resultRow: resultRow,
                entry: resultRow == 1, exported: resultRow != 1)));
    }

    private static byte Layout(int width) => width switch {
        2 => 0x10,
        4 => 0x20,
        8 => 0x30,
        _ => throw new InvalidOperationException(),
    };

    private static BoundProgram Compile(string source, BoundRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = compilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))();
    }

    private static byte[] Body(byte[] artifact, int method)
    {
        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.TryGetSection(0x20, out var bytes, out var count), Is.True);
        return new TrRmBodyTableReader(bytes, count).GetRow(method).ToArray();
    }

    private static void AssertIndexedConstants(byte[] body, byte[] payload, int typeId)
    {
        Assert.That(body[0] & 0xFC, Is.EqualTo(8));
        var width = 1 << (body[0] & 3);
        var at = 1;
        var blocks = Read();
        var values = Read();
        var regions = Read();
        var types = Read();
        Assert.That(regions, Is.EqualTo(0));
        at += types * 4 + values * 2 * width + (blocks + 1 + values) * width;
        var offsets = new int[blocks + 1];
        for (var block = 0; block <= blocks; block++)
        {
            offsets[block] = Read();
        }
        at += blocks * width;
        var start = at;
        var constants = 0;
        for (var block = 0; block < blocks; block++)
        {
            at = start + offsets[block];
            var presence = body[at++];
            if ((presence & 1) != 0)
            { _ = Read(); }
            foreach (var bit in new[] { 2, 4, 8, 16 })
            {
                if ((presence & bit) != 0)
                {
                    var count = Read();
                    at += count * width * (bit == 16 ? 2 : 1);
                }
            }
            var operations = Read();
            if (operations == 0)
            { continue; }
            Assert.That(operations, Is.EqualTo(1));
            if (payload.Length != 1)
            {
                Assert.That(body[at++], Is.EqualTo(0xF0));
                Assert.That(body[at++], Is.EqualTo(Layout(payload.Length)));
            }
            Assert.That(body[at++], Is.EqualTo(1));
            Assert.That(body[at++], Is.InRange(2, 3));
            Assert.That(body[at++], Is.EqualTo(typeId));
            Assert.That(body.AsSpan(at, payload.Length).ToArray(), Is.EqualTo(payload));
            constants++;
        }
        Assert.That(constants, Is.EqualTo(2));

        int Read()
        {
            var value = width switch {
                1 => body[at],
                2 => BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(at)),
                4 => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(at))),
                _ => throw new InvalidOperationException(),
            };
            at += width;
            return value;
        }
    }
}
