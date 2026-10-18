// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Semantics;

public sealed class CallerUnsafeTests
{
    private const int FixedLayoutModuleVersion = 78;

    private const string PairDeclaration = """
        [Blittable] [Layout(LayoutKind.Sequential)]
        public struct Pair {
            public mut int32 First;
            public mut int32 Second;
            public Pair(int32 first, int32 second) { First = first; Second = second; }
        }
        """;
    private const string TripleDeclaration = """
        [Blittable] [Layout(LayoutKind.Sequential)]
        public struct Triple {
            public mut int32 First;
            public mut int32 Second;
            public mut int32 Third;
            public Triple(int32 first, int32 second, int32 third) {
                First = first;
                Second = second;
                Third = third;
            }
        }
        """;
    private const string EnvelopeDeclaration = """
        [Blittable] [Layout(LayoutKind.Sequential)]
        public struct Envelope {
            public mut Pair Payload;
            public Envelope(Pair payload) { Payload = payload; }
        }
        """;
    private const string FloatBoxDeclaration = """
        [Blittable] [Layout(LayoutKind.Sequential)]
        public struct FloatBox {
            public float64 Value;
            public FloatBox(float64 value) { Value = value; }
        }
        """;
    private const string ValueDeclaration = """
        [Blittable] [Layout(LayoutKind.Sequential)]
        public struct Value {
            public mut int32 Marker;
            public Value(int32 marker) { Marker = marker; }
            public unsafe Pair Echo(Pair input) { return input; }
            public static unsafe Triple Transform(Pair first, Pair second,
                int32 a, int32 b, int32 c, int32 d, int32 e) {
                return new Triple(
                    first.First + second.Second + a * 10000 + b * 1000 + c * 100 + d * 10 + e,
                    first.Second, second.First);
            }
            public static unsafe Envelope Wrap(Envelope input) { return input; }
            public Pair SafeEcho(Pair input) { unsafe { return Echo(input); } }
        }
        """;
    private const string UnsafeFieldModuleSource = """
        [Blittable] [Layout(LayoutKind.Sequential)]
        public struct SafeInner {
            public mut int32 Value;
            public SafeInner(int32 value) { Value = value; }
        }
        [Blittable] [Layout(LayoutKind.Sequential)]
        public struct UnsafeInner {
            public unsafe mut int32 Value;
            public UnsafeInner(int32 value) { unsafe { Value = value; } }
        }
        [Blittable] [Layout(LayoutKind.Sequential)]
        public struct UnsafeOuter {
            public unsafe mut SafeInner Value;
            public UnsafeOuter(SafeInner value) { unsafe { Value = value; } }
        }
        [Blittable] [Layout(LayoutKind.Sequential)]
        public struct UnsafeLeafOuter {
            public mut UnsafeInner Value;
            public UnsafeLeafOuter(UnsafeInner value) { Value = value; }
        }
        public UnsafeOuter MakeOuter(int32 value) {
            return new UnsafeOuter(new SafeInner(value));
        }
        public UnsafeLeafOuter MakeLeafOuter(int32 value) {
            return new UnsafeLeafOuter(new UnsafeInner(value));
        }
        """;
    private const string UnsafeFieldModuleConsumerSource = """
        int32 Main() {
            mut UnsafeOuter outer = MakeOuter(40);
            mut UnsafeLeafOuter leaf = MakeLeafOuter(1);
            unsafe {
                outer.Value.Value = 41;
                outer.Value.Value++;
                leaf.Value.Value = 2;
                leaf.Value.Value += 1;
                return outer.Value.Value + leaf.Value.Value;
            }
        }
        """;
    private const string UnsafeFieldExpressionModuleSource =
        UnsafeFieldModuleSource + "\npublic unsafe int32 ReadValue(SafeInner value) { return value.Value; }\n";
    private const string UnsafeFieldModuleExpressionConsumerSource = """
        int32 Main() {
            UnsafeOuter outer = MakeOuter(40);
            UnsafeLeafOuter leaf = MakeLeafOuter(1);
            int32 ancestor = unsafe(outer.Value.Value);
            int32 leafValue = unsafe(leaf.Value.Value);
            int32 projected = unsafe(outer.Value).Value;
            int32 throughCall = unsafe(ReadValue(outer.Value));
            return ancestor + leafValue + projected + throughCall;
        }
        """;
    private const string UnsafeMutableFieldLoanModuleSource = """
        [Blittable] [Layout(LayoutKind.Sequential)]
        public struct DiscardedBeforeLoanCounter {
            public int32 Value;
        }
        [Blittable] [Layout(LayoutKind.Sequential)]
        public struct LoanCounter {
            public mut int32 Number;
            public LoanCounter(int32 number) { Number = number; }
            public mut int32 Next() { Number = Number + 1; return Number; }
            public mut int32 NextBy(int32 first, int32 second) {
                Number = Number + first * 10 + second;
                return Number;
            }
            public unsafe mut int32 UnsafeNextBy(int32 first, int32 second) {
                Number = Number + first * 10 + second;
                return Number;
            }
        }
        [Blittable] [Layout(LayoutKind.Sequential)]
        public struct UnsafeLeafOwner {
            public unsafe mut LoanCounter Value;
            public UnsafeLeafOwner(LoanCounter value) { unsafe { Value = value; } }
        }
        [Blittable] [Layout(LayoutKind.Sequential)]
        public struct SafeLoanInner {
            public mut LoanCounter Value;
            public SafeLoanInner(LoanCounter value) { Value = value; }
        }
        [Blittable] [Layout(LayoutKind.Sequential)]
        public struct UnsafeAncestorOwner {
            public unsafe mut SafeLoanInner Value;
            public UnsafeAncestorOwner(SafeLoanInner value) { unsafe { Value = value; } }
        }
        public int32 InvokeUnsafeLeaf() {
            mut UnsafeLeafOwner owner = new UnsafeLeafOwner(new LoanCounter(40));
            unsafe { return owner.Value.Next(); }
        }
        public int32 InvokeUnsafeAncestor() {
            mut UnsafeAncestorOwner owner =
                new UnsafeAncestorOwner(new SafeLoanInner(new LoanCounter(0)));
            unsafe { return owner.Value.Value.Next(); }
        }
        public int32 InvokeExpressionScoped() {
            mut UnsafeLeafOwner owner = new UnsafeLeafOwner(new LoanCounter(40));
            return unsafe(owner.Value.NextBy(second: 1, first: 0));
        }
        public int32 InvokeExpressionScopedAncestor() {
            mut UnsafeAncestorOwner owner =
                new UnsafeAncestorOwner(new SafeLoanInner(new LoanCounter(0)));
            return unsafe(owner.Value.Value.NextBy(second: 1, first: 0));
        }
        public int32 InvokeExpressionScopedUnsafeMethod() {
            mut UnsafeLeafOwner owner = new UnsafeLeafOwner(new LoanCounter(40));
            return unsafe(owner.Value.UnsafeNextBy(second: 1, first: 0));
        }
        """;
    private const string ConsumerLocalUnsafeMutableFieldLoanSource = """
        [Blittable] [Layout(LayoutKind.Sequential)]
        public struct ConsumerLoanCounter {
            public mut int32 Number;
            public ConsumerLoanCounter(int32 number) { Number = number; }
            public unsafe mut int32 UnsafeNextBy(int32 first, int32 second) {
                Number = Number + first * 10 + second;
                return Number;
            }
        }
        [Blittable] [Layout(LayoutKind.Sequential)]
        public struct ConsumerUnsafeLeafOwner {
            public unsafe mut ConsumerLoanCounter Value;
            public ConsumerUnsafeLeafOwner(ConsumerLoanCounter value) {
                unsafe { Value = value; }
            }
        }
        """;
    private const string CopyValueMethodSource =
        PairDeclaration + "\n\n" + TripleDeclaration + "\n\n" + EnvelopeDeclaration
            + "\n\n" + ValueDeclaration + """

        public Pair MakePair(int32 first, int32 second) { return new Pair(first, second); }
        public Value MakeValue() { return new Value(0); }
        public Envelope MakeEnvelope() { return new Envelope(new Pair(7, 8)); }
        """;
    private const string ReorderedCopyValueMethodSource =
        TripleDeclaration + "\n\n" + PairDeclaration + "\n\n" + EnvelopeDeclaration
            + "\n\n" + ValueDeclaration + """

        public Pair MakePair(int32 first, int32 second) { return new Pair(first, second); }
        public Value MakeValue() { return new Value(0); }
        public Envelope MakeEnvelope() { return new Envelope(new Pair(7, 8)); }
        """;
    private const string CopyValueConsumerSource = """
        struct Counter {
            public mut int32 Number;
            public Counter(int32 number) { Number = number; }
            public mut int32 Next() { Number = Number + 1; return Number; }
        }
        int32 Main() {
            Value owner = MakeValue();
            Pair first = MakePair(10, 20);
            Pair second = MakePair(30, 40);
            Envelope nested = MakeEnvelope();
            mut Counter counter = new Counter(0);
            Pair safe = owner.SafeEcho(second);
            unsafe {
                Pair echo = owner.Echo(first);
                Envelope wrapped = Value.Wrap(nested);
                Triple result = Value.Transform(e: counter.Next(), first: first,
                    a: counter.Next(), second: second, b: counter.Next(),
                    c: counter.Next(), d: counter.Next());
                return result.First + echo.First + echo.Second + safe.First + safe.Second
                    + wrapped.Payload.First;
            }
        }
        """;
    private const string ExpressionOpsDeclaration = """
        [Blittable] [Layout(LayoutKind.Sequential)]
        public struct ExpressionOps {
            public int32 Marker;
            public ExpressionOps(int32 marker) { Marker = marker; }
            public static unsafe Pair CreatePair(int32 first, int32 second) {
                return new Pair(first, second);
            }
            public static unsafe int32 Sum(Pair value) { return value.First + value.Second; }
            public static unsafe Pair ZeroPair() { return new Pair(11, 12); }
            public static unsafe FloatBox EchoFloat(FloatBox value) { return value; }
        }
        """;
    private const string CopyValueExpressionProviderSource =
        CopyValueMethodSource + "\n\n" + FloatBoxDeclaration + "\n\n" + ExpressionOpsDeclaration
            + "\n\npublic unsafe Pair Identity(Pair input) { return input; }"
            + "\npublic FloatBox MakeFloatBox(float64 value) { return new FloatBox(value); }\n";
    private const string CopyValueExpressionConsumerSource = """
        int32 Main() {
            Pair first = MakePair(10, 20);
            Pair second = MakePair(30, 40);
            Envelope nested = MakeEnvelope();
            Triple transformed = unsafe(Value.Transform(e: 5, first: first,
                a: 1, second: second, b: 2, c: 3, d: 4));
            Envelope wrapped = unsafe(Value.Wrap(nested));
            Pair identity = unsafe(Identity(first));
            Pair created = unsafe(ExpressionOps.CreatePair(1, 2));
            int32 sum = unsafe(ExpressionOps.Sum(created));
            Pair zero = unsafe(ExpressionOps.ZeroPair());
            FloatBox floating = unsafe(ExpressionOps.EchoFloat(MakeFloatBox(float64(7))));
            require transformed.First == 12395 && transformed.Second == 20 && transformed.Third == 30;
            require wrapped.Payload.First == 7 && wrapped.Payload.Second == 8;
            require identity.First == 10 && identity.Second == 20;
            require floating.Value == float64(7);
            return transformed.First + wrapped.Payload.First + identity.Second + sum + zero.First;
        }
        """;
    private const string CopyValueGetterSource =
        PairDeclaration + "\n\n" + TripleDeclaration + """

        [Blittable] [Layout(LayoutKind.Sequential)]
        public struct Odd {
            public char16 First;
            public char16 Second;
            public bool Third;
            public Odd(char16 first, char16 second, bool third) {
                First = first;
                Second = second;
                Third = third;
            }
        }
        [Blittable] [Layout(LayoutKind.Sequential)]
        public struct Nested {
            public Pair Payload;
            public Odd Tail;
            public Nested(Pair payload, Odd tail) { Payload = payload; Tail = tail; }
        }
        [Blittable] [Layout(LayoutKind.Sequential)]
        public struct GetterOwner {
            public int32 Marker;
            public GetterOwner(int32 marker) { Marker = marker; }
            public Pair InstancePair { unsafe get { return new Pair(40, 2); } }
            public Odd InstanceOdd {
                unsafe get {
                    Odd local = new Odd(char16(5), char16(6), true);
                    return local;
                }
            }
            public static Triple StaticTriple {
                unsafe get {
                    Triple local = new Triple(10, 11, 12);
                    return local;
                }
            }
            public static Nested StaticNested {
                unsafe get {
                    return new Nested(new Pair(20, 21),
                        new Odd(char16(7), char16(8), true));
                }
            }
        }
        public GetterOwner MakeGetterOwner() { return new GetterOwner(0); }
        """;
    private const string CopyValueGetterMiddleSource = """
        public Nested ForwardNested() {
            return unsafe(GetterOwner.StaticNested);
        }
        public Odd ForwardOdd(GetterOwner owner) {
            return unsafe(owner.InstanceOdd);
        }
        """;
    private const string CopyValueGetterConsumerSource = """
        int32 Main() {
            GetterOwner owner = MakeGetterOwner();
            Pair pair = unsafe(owner.InstancePair);
            Odd odd = unsafe(owner.InstanceOdd);
            Triple triple = unsafe(GetterOwner.StaticTriple);
            Nested nested = unsafe(GetterOwner.StaticNested);
            Nested forwardedNested = ForwardNested();
            Odd forwardedOdd = ForwardOdd(owner);
            require odd.Third && nested.Tail.Third
                && forwardedNested.Tail.Third && forwardedOdd.Third;
            require pair.First == 40 && pair.Second == 2
                && odd.First == char16(5) && odd.Second == char16(6)
                && triple.First == 10 && triple.Second == 11 && triple.Third == 12
                && (nested.Payload.First == 20 || nested.Payload.First == 22)
                && nested.Payload.Second == 21
                && nested.Tail.First == char16(7) && nested.Tail.Second == char16(8)
                && (forwardedNested.Payload.First == 20 || forwardedNested.Payload.First == 22)
                && forwardedNested.Payload.Second == 21
                && forwardedOdd.First == char16(5) && forwardedOdd.Second == char16(6);
            if (nested.Payload.First == 22) {
                return 211;
            }
            return 209;
        }
        """;

    private static string SafeCopyValueMethodSource => ReorderedCopyValueMethodSource
        .Replace("public unsafe Pair Echo(Pair input) { return input; }",
            "public Pair Echo(Pair input) { return new Pair(input.First + 1, input.Second); }",
            StringComparison.Ordinal)
        .Replace("public static unsafe Triple Transform(", "public static Triple Transform(",
            StringComparison.Ordinal)
        .Replace("first.First + second.Second + a * 10000",
            "first.First + second.Second + 1 + a * 10000", StringComparison.Ordinal)
        .Replace("unsafe { return Echo(input); }", "return Echo(input);", StringComparison.Ordinal);

    private static BoundProgram CompileLibrary(string source,
        BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
    }

    private static byte[] ReplaceModuleString(byte[] module, string oldValue, string newValue)
    {
        var oldBytes = Encoding.UTF8.GetBytes(oldValue);
        var newBytes = Encoding.UTF8.GetBytes(newValue);
        var offsets = new List<int>();
        for (var offset = sizeof(int); offset <= module.Length - oldBytes.Length; offset++)
        {
            if (BinaryPrimitives.ReadInt32LittleEndian(module.AsSpan(offset - sizeof(int))) == oldBytes.Length
                && module.AsSpan(offset, oldBytes.Length).SequenceEqual(oldBytes))
            {
                offsets.Add(offset);
            }
        }
        Assert.That(offsets, Is.Not.Empty);

        var result = module;
        var lengthDelta = newBytes.Length - oldBytes.Length;
        foreach (var offset in offsets.OrderDescending())
        {
            var stringLength = BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(offset - sizeof(int)));
            Assert.That(stringLength, Is.EqualTo(oldBytes.Length));

            var updated = new byte[result.Length + lengthDelta];
            result.AsSpan(0, offset - sizeof(int)).CopyTo(updated);
            BinaryPrimitives.WriteInt32LittleEndian(updated.AsSpan(offset - sizeof(int)), newBytes.Length);
            newBytes.CopyTo(updated.AsSpan(offset));
            result.AsSpan(offset + oldBytes.Length).CopyTo(updated.AsSpan(offset + newBytes.Length));
            result = updated;
        }
        return result;
    }

    private static string GetModuleStringStartingWith(byte[] module, string valuePrefix)
    {
        var prefixBytes = Encoding.UTF8.GetBytes(valuePrefix);
        var valueOffset = module.AsSpan().IndexOf(prefixBytes);
        Assert.That(valueOffset >= sizeof(int), Is.True);
        var valueLength = BinaryPrimitives.ReadInt32LittleEndian(module.AsSpan(valueOffset - sizeof(int)));
        Assert.That(valueLength >= prefixBytes.Length, Is.True);
        Assert.That(valueOffset + valueLength <= module.Length, Is.True);
        var value = Encoding.UTF8.GetString(module, valueOffset, valueLength);
        Assert.That(value, Does.StartWith(valuePrefix));
        return value;
    }

    private static byte[] StripSerializedUnsafeExpression(byte[] module, BoundUnsafeExpression expression)
    {
        Span<byte> wrapper = stackalloc byte[1 + sizeof(int) + (2 * sizeof(int))];
        wrapper[0] = 31;
        BinaryPrimitives.WriteInt32LittleEndian(wrapper[1..], (int)expression.Type);
        BinaryPrimitives.WriteInt32LittleEndian(
            wrapper[(1 + sizeof(int))..], expression.Span.Start);
        BinaryPrimitives.WriteInt32LittleEndian(
            wrapper[(1 + (2 * sizeof(int)))..], expression.Span.Length);
        var offset = module.AsSpan().IndexOf(wrapper);
        Assert.That(offset >= 0, Is.True);
        var stripped = new byte[module.Length - wrapper.Length];
        module.AsSpan(0, offset).CopyTo(stripped);
        module.AsSpan(offset + wrapper.Length).CopyTo(stripped.AsSpan(offset));
        return stripped;
    }

    private static byte[] SetSerializedNominalUpdateOperator(byte[] module,
        BoundNominalUpdate update, TokenKind updateOperator)
    {
        Span<byte> header = stackalloc byte[1 + sizeof(int) + (2 * sizeof(int))];
        header[0] = 25;
        BinaryPrimitives.WriteInt32LittleEndian(header[1..], (int)update.Type);
        BinaryPrimitives.WriteInt32LittleEndian(
            header[(1 + sizeof(int))..], update.Span.Start);
        BinaryPrimitives.WriteInt32LittleEndian(
            header[(1 + (2 * sizeof(int)))..], update.Span.Length);
        var offset = module.AsSpan().IndexOf(header);
        Assert.That(offset >= 0, Is.True);
        var forged = (byte[])module.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(
            forged.AsSpan(offset + header.Length, sizeof(int)), (int)updateOperator);
        return forged;
    }

    private static byte[] WrapSerializedMutableFieldLoan(byte[] module, BoundMutableFieldLoan loan)
    {
        const int expressionHeaderLength = 1 + sizeof(int) + (2 * sizeof(int));
        var offset = -1;
        for (var index = 0; index <= module.Length - expressionHeaderLength; index++)
        {
            var header = module.AsSpan(index, expressionHeaderLength);
            if (header[0] == 20
                && BinaryPrimitives.ReadInt32LittleEndian(header[(1 + sizeof(int))..]) == loan.Span.Start
                && BinaryPrimitives.ReadInt32LittleEndian(header[(1 + (2 * sizeof(int)))..])
                    == loan.Span.Length)
            {
                offset = index;
                break;
            }
        }
        Assert.That(offset >= 0, Is.True);
        var serializedType = BinaryPrimitives.ReadInt32LittleEndian(module.AsSpan(offset + 1, sizeof(int)));

        Span<byte> wrapper = stackalloc byte[expressionHeaderLength];
        wrapper[0] = 31;
        BinaryPrimitives.WriteInt32LittleEndian(wrapper[1..], serializedType);
        BinaryPrimitives.WriteInt32LittleEndian(
            wrapper[(1 + sizeof(int))..], loan.Span.Start);
        BinaryPrimitives.WriteInt32LittleEndian(
            wrapper[(1 + (2 * sizeof(int)))..], loan.Span.Length);
        var wrapped = new byte[module.Length + wrapper.Length];
        module.AsSpan(0, offset).CopyTo(wrapped);
        wrapper.CopyTo(wrapped.AsSpan(offset));
        module.AsSpan(offset).CopyTo(wrapped.AsSpan(offset + wrapper.Length));
        return wrapped;
    }

    private static BoundCall WithArgumentParameterIndices(BoundCall call, ImmutableArray<int> parameterIndices)
    {
        var property = typeof(BoundCall).GetProperty(
            "ArgumentParameterIndices", BindingFlags.Instance | BindingFlags.NonPublic);
        TestAssertions.NotNull(property);
        var clone = call with { };
        property.SetValue(clone, parameterIndices);
        return clone;
    }

    private static byte[] RemapSerializedCallReceiver(byte[] module, string functionName)
    {
        var serializedName = Encoding.UTF8.GetBytes(functionName);
        var nameOffset = -1;
        for (var index = 17; index <= module.Length - serializedName.Length; index++)
        {
            if (module[index - 17] == 5
                && BinaryPrimitives.ReadInt32LittleEndian(module.AsSpan(index - 4, sizeof(int)))
                    == serializedName.Length
                && module.AsSpan(index, serializedName.Length).SequenceEqual(serializedName)
                && BinaryPrimitives.ReadInt32LittleEndian(
                    module.AsSpan(index + serializedName.Length, sizeof(int))) == 3
                && module[index + serializedName.Length + sizeof(int)] == 1)
            {
                nameOffset = index;
                break;
            }
        }
        Assert.That(nameOffset >= 0, Is.True);

        var remapped = (byte[])module.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(
            remapped.AsSpan(nameOffset + serializedName.Length + sizeof(int) + sizeof(byte), sizeof(int)), 1);
        return remapped;
    }

    private static byte[] SetSerializedCallParameterMapping(byte[] module, string functionName,
        int[] parameterMapping)
    {
        var serializedName = Encoding.UTF8.GetBytes(functionName);
        var nameOffset = -1;
        for (var index = 17; index <= module.Length - serializedName.Length; index++)
        {
            if (module[index - 17] == 5
                && BinaryPrimitives.ReadInt32LittleEndian(module.AsSpan(index - 4, sizeof(int)))
                    == serializedName.Length
                && module.AsSpan(index, serializedName.Length).SequenceEqual(serializedName)
                && BinaryPrimitives.ReadInt32LittleEndian(
                    module.AsSpan(index + serializedName.Length, sizeof(int))) == parameterMapping.Length
                && module[index + serializedName.Length + sizeof(int)] is 0 or 1)
            {
                nameOffset = index;
                break;
            }
        }
        Assert.That(nameOffset >= 0, Is.True);

        var mappingFlagOffset = nameOffset + serializedName.Length + sizeof(int);
        var mappingOffset = mappingFlagOffset + sizeof(byte);
        if (module[mappingFlagOffset] == 0)
        {
            var mappingBytes = parameterMapping.Length * sizeof(int);
            var mappedModule = new byte[module.Length + mappingBytes];
            module.AsSpan(0, mappingOffset).CopyTo(mappedModule);
            mappedModule[mappingFlagOffset] = 1;
            for (var index = 0; index < parameterMapping.Length; index++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    mappedModule.AsSpan(mappingOffset + (index * sizeof(int)), sizeof(int)), parameterMapping[index]);
            }
            module.AsSpan(mappingOffset).CopyTo(mappedModule.AsSpan(mappingOffset + mappingBytes));
            return mappedModule;
        }

        var remapped = (byte[])module.Clone();
        for (var index = 0; index < parameterMapping.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                remapped.AsSpan(mappingOffset + (index * sizeof(int)), sizeof(int)), parameterMapping[index]);
        }
        return remapped;
    }

    private static byte[] SetSerializedFunctionRequiresUnsafeCaller(byte[] module, string functionName)
    {
        var serializedName = Encoding.UTF8.GetBytes(functionName);
        var functionNameOffset = -1;
        for (var offset = sizeof(int); offset <= module.Length - serializedName.Length; offset++)
        {
            if (BinaryPrimitives.ReadInt32LittleEndian(module.AsSpan(offset - sizeof(int)))
                != serializedName.Length
                || !module.AsSpan(offset, serializedName.Length).SequenceEqual(serializedName))
            {
                continue;
            }

            var returnTypeOffset = offset + serializedName.Length;
            if (returnTypeOffset > module.Length - (sizeof(int) + 10)
                || BinaryPrimitives.ReadInt32LittleEndian(module.AsSpan(returnTypeOffset, sizeof(int)))
                    != (int)PrimitiveType.Int32)
            {
                continue;
            }
            var flagsOffset = returnTypeOffset + sizeof(int);
            var hasInvalidFlag = false;
            for (var flagIndex = 0; flagIndex < 10; flagIndex++)
            {
                if (module[flagsOffset + flagIndex] > 1)
                {
                    hasInvalidFlag = true;
                    break;
                }
            }
            if (hasInvalidFlag)
            {
                continue;
            }
            functionNameOffset = offset;
            break;
        }
        Assert.That(functionNameOffset >= 0, Is.True);

        var updated = (byte[])module.Clone();
        var requiresUnsafeCallerOffset = functionNameOffset + serializedName.Length + sizeof(int) + 7;
        Assert.That(updated[requiresUnsafeCallerOffset], Is.EqualTo(0));
        updated[requiresUnsafeCallerOffset] = 1;
        return updated;
    }
    [TestCase("unsafe { return Danger(); }")]
    [TestCase("return unsafe(Danger());")]
    [TestCase("unsafe(Danger()); return 42;")]
    public void ExplicitUnsafeScopesAuthorizeOnlyTheirCall(string body)
    {
        var source = $"unsafe int32 Danger() {{ return 42; }} int32 Caller() {{ {body} }}";
        var program = CompileLibrary(source);
        Assert.That(global::System.Linq.Enumerable.Single(program.Functions, static function => function.Name == "Danger")
            .RequiresUnsafeCaller, Is.True);
        Assert.That(global::System.Linq.Enumerable.Single(program.Functions, static function => function.Name == "Caller")
            .RequiresUnsafeCaller, Is.False);
    }

    [Test]
    public void ExpressionScopedVoidCallsPreserveExecution()
    {
        ImmutableArray<HostBuffer> buffers = [new("Output", PrimitiveType.Int32, [0], true)];
        const string returnSource = """
            unsafe void Assign(int32 value) { Host.Output[0] = value; }
            void ReturnCall() { return unsafe(Assign(41)); }
            int32 Main() { ReturnCall(); return Host.Output[0]; }
            """;
        var returnCompilation = Compilation.Create(SyntaxTree.Parse(returnSource), buffers);
        Assert.That(returnCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, returnCompilation.Diagnostics));
        Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = returnCompilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(),
            NativeRuntimePolicy.FailFast), Is.EqualTo(41));

        const string discardSource = """
            unsafe void Assign(int32 value) { Host.Output[0] = value; }
            int32 Main() { unsafe(Assign(42)); return Host.Output[0]; }
            """;
        var discardCompilation = Compilation.Create(SyntaxTree.Parse(discardSource), buffers);
        Assert.That(discardCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, discardCompilation.Diagnostics));
        Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = discardCompilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))(),
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void ExpressionScopedCallsPreserveBooleanAndOutputFlow()
    {
        const string shortCircuitSource = """
            unsafe bool Danger() { require false; return false; }
            int32 Main() {
                if (unsafe(true || Danger())) { return 42; }
                return 0;
            }
            """;
        var shortCircuitCompilation = Compilation.Create(SyntaxTree.Parse(shortCircuitSource));
        Assert.That(shortCircuitCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, shortCircuitCompilation.Diagnostics));
        Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = shortCircuitCompilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))(),
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));

        const string conditionSource = """
            unsafe bool Pass() { return true; }
            int32 Main() { require unsafe(Pass()); return 42; }
            """;
        var conditionCompilation = Compilation.Create(SyntaxTree.Parse(conditionSource));
        Assert.That(conditionCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, conditionCompilation.Diagnostics));
        Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = conditionCompilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))(),
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));

        const string outputSource = """
            unsafe bool Fill(out int32 value) { value = 42; return true; }
            int32 Main() {
                mut int32 value = 0;
                require unsafe(Fill(out value));
                return value;
            }
            """;
        var outputCompilation = Compilation.Create(SyntaxTree.Parse(outputSource));
        Assert.That(outputCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, outputCompilation.Diagnostics));
        Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = outputCompilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))(),
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void UnsafeDeclarationDoesNotAuthorizeItsBodyOrFollowingCalls()
    {
        var bodyCall = Compilation.CreateLibrary(SyntaxTree.Parse(
            "unsafe int32 Danger() { return 42; } unsafe int32 Wrapper() { return Danger(); }"));
        Assert.That(bodyCall.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(bodyCall.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var memberBodyCall = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Value {
                public unsafe int32 Danger() { return 42; }
                public unsafe int32 Wrapper() { return Danger(); }
            }
            """));
        Assert.That(memberBodyCall.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(memberBodyCall.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var leakedScope = Compilation.CreateLibrary(SyntaxTree.Parse(
            "unsafe int32 Danger() { return 42; } int32 Caller() { unsafe(Danger()); return Danger(); }"));
        Assert.That(leakedScope.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(leakedScope.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);
    }

    [Test]
    public void SafeWrapperMayUseUnsafeImplementationWithoutPropagatingTheObligation()
    {
        var program = CompileLibrary("""
            public unsafe int32 Danger() { return 42; }
            public int32 Wrapper() { unsafe { return Danger(); } }
            """);
        Assert.That(global::System.Linq.Enumerable.Single(program.Functions, static function => function.Name == "Danger")
            .RequiresUnsafeCaller, Is.True);
        Assert.That(global::System.Linq.Enumerable.Single(program.Functions, static function => function.Name == "Wrapper")
            .RequiresUnsafeCaller, Is.False);
    }

    [Test]
    public void UnsafeClassConstructorsRequireAuthorizationThroughTheirFactories()
    {
        const string declaration = """
            class Leaf {
                public int32 Value;
                public unsafe Leaf(int32 value) { Value = value; }
            }
            """;

        var safeCall = Compilation.Create(SyntaxTree.Parse(declaration
            + " int32 Main() { require Leaf.TryCreate(42, out var leaf); return leaf.Value; }"));
        Assert.That(safeCall.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(safeCall.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var authorizedCall = Compilation.Create(SyntaxTree.Parse(declaration
            + " int32 Main() { unsafe { require Leaf.TryCreate(42, out var leaf); return leaf.Value; } }"));
        Assert.That(authorizedCall.HasErrors, Is.False, string.Join(Environment.NewLine, authorizedCall.Diagnostics));
    }

    [Test]
    public void UnsafeGetterRequiresAuthorizationAtThePropertyAccess()
    {
        const string source = """
            class Box {
                int32 _value;
                public Box(int32 value) { _value = value; }
                public int32 Value { unsafe get { return _value; } }
            }
            """;

        var safeAccess = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { require Box.TryCreate(42, out var box); return box.Value; }"));
        Assert.That(safeAccess.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(safeAccess.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var authorizedAccess = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { require Box.TryCreate(42, out var box); unsafe { return box.Value; } }"));
        Assert.That(authorizedAccess.HasErrors, Is.False, string.Join(Environment.NewLine, authorizedAccess.Diagnostics));
    }

    [Test]
    public void UnsafeInstanceAndStaticMethodsRequireAuthorizationWithoutTaintingSafeWrappers()
    {
        const string source = """
            class Box {
                public int32 Value;
                public Box(int32 value) { Value = value; }
                public unsafe int32 Read() { return Value; }
                public int32 SafeRead() { unsafe { return Read(); } }
                public static unsafe int32 Twice(int32 value) { return value * 2; }
            }
            """;

        var safeCalls = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { require Box.TryCreate(21, out var box); return box.Read() + Box.Twice(1); }"));
        Assert.That(safeCalls.Program, Is.Null);
        Assert.That(safeCalls.Diagnostics.Count(static diagnostic => diagnostic.Code == "TRM3028"), Is.EqualTo(2));

        var authorizedCalls = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { require Box.TryCreate(21, out var box);"
            + " unsafe { return box.Read() + Box.Twice(1); } }"));
        Assert.That(authorizedCalls.HasErrors, Is.False, string.Join(Environment.NewLine, authorizedCalls.Diagnostics));

        var safeWrapper = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { require Box.TryCreate(21, out var box); return box.SafeRead(); }"));
        Assert.That(safeWrapper.HasErrors, Is.False, string.Join(Environment.NewLine, safeWrapper.Diagnostics));
        Assert.That(global::System.Linq.Enumerable.Single(((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = safeWrapper.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))().Functions, static function => function.Name == "Box.SafeRead").RequiresUnsafeCaller, Is.False);
    }

    [Test]
    public void UnsafeFieldsRequireAuthorizationForReadsAndWrites()
    {
        const string declaration = """
            struct Cell {
                public unsafe mut int32 Value;
                public Cell() { unsafe { Value = 1; } }
            }
            """;

        var safeRead = Compilation.CreateLibrary(SyntaxTree.Parse(declaration
            + " int32 Read() { Cell cell = new Cell(); return cell.Value; }"));
        Assert.That(safeRead.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(safeRead.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var authorizedRead = Compilation.CreateLibrary(SyntaxTree.Parse(declaration
            + " int32 Read() { Cell cell = new Cell(); return unsafe(cell.Value); }"));
        Assert.That(authorizedRead.HasErrors, Is.False, string.Join(Environment.NewLine, authorizedRead.Diagnostics));

        var safeWrite = Compilation.Create(SyntaxTree.Parse(declaration
            + " int32 Main() { mut Cell cell = new Cell(); cell.Value = 42; return 0; }"));
        Assert.That(safeWrite.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(safeWrite.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var authorizedWrite = Compilation.Create(SyntaxTree.Parse(declaration
            + " int32 Main() { mut Cell cell = new Cell(); unsafe { cell.Value = 42; return cell.Value; } }"));
        Assert.That(authorizedWrite.HasErrors, Is.False, string.Join(Environment.NewLine, authorizedWrite.Diagnostics));

        var unscopedInitialization = Compilation.CreateLibrary(SyntaxTree.Parse(
            "struct Cell { public unsafe int32 Value; public Cell() { Value = 1; } }"));
        Assert.That(unscopedInitialization.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(unscopedInitialization.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var duplicateModifier = Compilation.CreateLibrary(SyntaxTree.Parse(
            "struct Cell { public unsafe unsafe int32 Value; public Cell() { unsafe { Value = 1; } } }"));
        Assert.That(duplicateModifier.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(duplicateModifier.Diagnostics,static diagnostic => diagnostic.Code == "TRM3001"),Is.True);
    }

    [Test]
    public void UnsafeFieldObligationsApplyToEveryFieldInANestedPath()
    {
        const string source = """
            struct Inner {
                public int32 Value;
                public Inner() { Value = 42; }
            }
            struct Outer {
                public unsafe mut Inner Value;
                public Outer() { unsafe { Value = new Inner(); } }
            }
            """;

        var safeAccess = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { Outer outer = new Outer(); return outer.Value.Value; }"));
        Assert.That(safeAccess.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(safeAccess.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var authorizedAccess = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { mut Outer outer = new Outer(); unsafe { outer.Value = new Inner();"
            + " return outer.Value.Value; } }"));
        Assert.That(authorizedAccess.HasErrors, Is.False, string.Join(Environment.NewLine, authorizedAccess.Diagnostics));

        var safeWrite = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { mut Outer outer = new Outer(); outer.Value = new Inner(); return 0; }"));
        Assert.That(safeWrite.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(safeWrite.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);
    }

    [Test]
    public void UnsafeManagedClassFieldsRequireAuthorization()
    {
        const string source = """
            class Box {
                public unsafe int32 Value;
                public Box(int32 value) { unsafe { Value = value; } }
            }
            """;

        var safeAccess = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { require Box.TryCreate(42, out var box); return box.Value; }"));
        Assert.That(safeAccess.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(safeAccess.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var authorizedAccess = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { require Box.TryCreate(42, out var box); return unsafe(box.Value); }"));
        Assert.That(authorizedAccess.HasErrors, Is.False, string.Join(Environment.NewLine, authorizedAccess.Diagnostics));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void SemanticModulesRoundTripCallerUnsafeCopyStructFieldPaths(BoundRuntimePolicy policy)
    {
        var producer = ModuleCompiler.CreateProducer(
        CompileLibrary(UnsafeFieldExpressionModuleSource, policy),
        ["MakeOuter", "MakeLeafOuter", "ReadValue"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, sizeof(int))), Is.EqualTo(78));

        var reference = ModuleCompiler.CreateReference(producer);
        foreach (var (owner, fieldName, expected) in new[]
        {
            ("UnsafeOuter", "Value", true),
            ("UnsafeLeafOuter", "Value", false),
            ("SafeInner", "Value", false),
            ("UnsafeInner", "Value", true),
        })
        {
            var structure = global::System.Linq.Enumerable.Single(reference.Structures, item => item.Name == owner);
            Assert.That(global::System.Linq.Enumerable.Single(structure.Fields, field => field.Name == fieldName)
                .RequiresUnsafeCaller, Is.EqualTo(expected));
        }

        var unscoped = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                UnsafeOuter outer = MakeOuter(40);
                return outer.Value.Value;
            }
            """), reference, policy);
        Assert.That(unscoped.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(unscoped.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var expressionScoped = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                UnsafeOuter outer = MakeOuter(40);
                return unsafe(outer.Value.Value);
            }
            """), reference, policy);
        Assert.That(expressionScoped.HasErrors, Is.False, string.Join(Environment.NewLine, expressionScoped.Diagnostics));
        var expressionProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = expressionScoped.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))();
        var expressionMain = expressionProgram.Functions[expressionProgram.EntryPoint];
        var expressionReturn = ((global::System.Func<BoundReturn>)(() => { var __nunitActual8 = expressionMain.Body.Statements[^1]; Assert.That(__nunitActual8, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual8!; }))();
        var unsafeRead = ((global::System.Func<BoundUnsafeExpression>)(() => { var __nunitActual9 = expressionReturn.Value; Assert.That(__nunitActual9, Is.TypeOf<BoundUnsafeExpression>()); return (BoundUnsafeExpression)__nunitActual9!; }))();
        var expressionConsumer = ModuleCompiler.CreateConsumer(expressionProgram, producer);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            expressionConsumer.AsSpan(4, sizeof(int))), Is.EqualTo(78));
        foreach (var unsupportedVersion in new[] { 69, 70, 71, 72, 73, 74, 75 })
        {
            var unsupportedConsumer = (byte[])expressionConsumer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(unsupportedConsumer.AsSpan(4, sizeof(int)),
                unsupportedVersion);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(unsupportedConsumer, producer));
        }
        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        Assert.That(NativeJit.Execute(
            ModuleCompiler.Link(expressionConsumer, producer), runtimePolicy), Is.EqualTo(40));

        var expressionConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse(UnsafeFieldModuleExpressionConsumerSource), reference, policy);
        Assert.That(expressionConsumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, expressionConsumerCompilation.Diagnostics));
        var expressionConsumerProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = expressionConsumerCompilation.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))();
        var expressionConsumerModule = ModuleCompiler.CreateConsumer(expressionConsumerProgram, producer);
        Assert.That(NativeJit.Execute(
            ModuleCompiler.Link(expressionConsumerModule, producer), runtimePolicy), Is.EqualTo(121));

        var strippedMain = expressionMain with {
            Body = expressionMain.Body with {
                Statements = expressionMain.Body.Statements.SetItem(
                    expressionMain.Body.Statements.Length - 1,
                    expressionReturn with { Value = unsafeRead.Expression }),
            },
        };
        var strippedProgram = expressionProgram with {
            Functions = expressionProgram.Functions.SetItem(
                expressionProgram.Functions.IndexOf(expressionMain), strippedMain),
        };
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateConsumer(strippedProgram, producer));

        Span<byte> wrapperPrefix = stackalloc byte[1 + sizeof(int) + (2 * sizeof(int))];
        wrapperPrefix[0] = 31;
        BinaryPrimitives.WriteInt32LittleEndian(wrapperPrefix[1..], (int)unsafeRead.Type);
        BinaryPrimitives.WriteInt32LittleEndian(wrapperPrefix[(1 + sizeof(int))..],
            unsafeRead.Span.Start);
        BinaryPrimitives.WriteInt32LittleEndian(wrapperPrefix[(1 + (2 * sizeof(int)))..],
            unsafeRead.Span.Length);
        var wrapperOffset = expressionConsumer.AsSpan().IndexOf(wrapperPrefix);
        Assert.That(wrapperOffset >= 0, Is.True);
        var strippedConsumer = new byte[expressionConsumer.Length - wrapperPrefix.Length];
        expressionConsumer.AsSpan(0, wrapperOffset).CopyTo(strippedConsumer);
        expressionConsumer.AsSpan(wrapperOffset + wrapperPrefix.Length)
            .CopyTo(strippedConsumer.AsSpan(wrapperOffset));
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(strippedConsumer, producer));

        foreach (var mutation in new[] { "outer.Value.Value++;", "outer.Value.Value += 1;" })
        {
            var mutationCompilation = Compilation.Create(SyntaxTree.Parse($$"""
                int32 Main() {
                    mut UnsafeOuter outer = MakeOuter(40);
                    unsafe { {{mutation}} }
                    return 0;
                }
                """), reference, policy);
            Assert.That(mutationCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, mutationCompilation.Diagnostics));
            var mutationProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual11 = mutationCompilation.Program; Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual11!; }))();
            var mutationMain = mutationProgram.Functions[mutationProgram.EntryPoint];
            var mutationBlock = ((global::System.Func<BoundUnsafe>)(() => { var __nunitActual12 = mutationMain.Body.Statements[1]; Assert.That(__nunitActual12, Is.TypeOf<BoundUnsafe>()); return (BoundUnsafe)__nunitActual12!; }))();
            var mutationStatement = ((global::System.Func<BoundExpressionStatement>)(() => { var __nunitActual13 = mutationBlock.Body.Statements[0]; Assert.That(__nunitActual13, Is.TypeOf<BoundExpressionStatement>()); return (BoundExpressionStatement)__nunitActual13!; }))();
            var scopedMutation = mutationStatement with {
                Expression = new BoundUnsafeExpression(
                    mutationStatement.Expression, mutationStatement.Expression.Span),
            };
            var mutationFunction = mutationMain with {
                Body = mutationMain.Body with {
                    Statements = [
                        mutationMain.Body.Statements[0],
                        scopedMutation,
                        mutationMain.Body.Statements[2],
                    ],
                },
            };
            var forgedMutationProgram = mutationProgram with {
                Functions = mutationProgram.Functions.SetItem(
                    mutationProgram.Functions.IndexOf(mutationMain), mutationFunction),
            };
            var exception = TestExceptionCapture.Capture(() => ModuleCompiler.CreateConsumer(forgedMutationProgram, producer));
            Assert.That(exception is ModuleContractException or ArgumentException, Is.True, exception?.ToString());
        }

        var unrelatedMutationCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                UnsafeOuter outer = MakeOuter(40);
                mut int32 count = 0;
                return unsafe(outer.Value.Value) + count++;
            }
            """), reference, policy);
        Assert.That(unrelatedMutationCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, unrelatedMutationCompilation.Diagnostics));
        var unrelatedMutationConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual14 = unrelatedMutationCompilation.Program; Assert.That(__nunitActual14, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual14!; }))(), producer);
        Assert.That(NativeJit.Execute(
            ModuleCompiler.Link(unrelatedMutationConsumer, producer), runtimePolicy), Is.EqualTo(40));

        var wrappedRightCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut UnsafeOuter outer = MakeOuter(40);
                outer.Value = unsafe(new SafeInner(42));
                return 0;
            }
            """), reference, policy);
        Assert.That(wrappedRightCompilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(wrappedRightCompilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse(UnsafeFieldModuleConsumerSource), reference, policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumerProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual15 = consumerCompilation.Program; Assert.That(__nunitActual15, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual15!; }))();
        var main = consumerProgram.Functions[consumerProgram.EntryPoint];
        var unsafeBlock = ((global::System.Func<BoundUnsafe>)(() => { var __nunitActual16 = main.Body.Statements[^1]; Assert.That(__nunitActual16, Is.TypeOf<BoundUnsafe>()); return (BoundUnsafe)__nunitActual16!; }))();
        var forgedMain = main with {
            Body = main.Body with {
                Statements = main.Body.Statements.SetItem(main.Body.Statements.Length - 1, unsafeBlock.Body),
            },
        };
        var forgedProgram = consumerProgram with {
            Functions = consumerProgram.Functions.SetItem(consumerProgram.EntryPoint, forgedMain),
        };
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateConsumer(forgedProgram, producer));

        var consumer = ModuleCompiler.CreateConsumer(consumerProgram, producer);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(consumer.AsSpan(4, sizeof(int))), Is.EqualTo(78));
        foreach (var unsupportedVersion in new[] { 69, 70, 71, 72, 73, 74, 75 })
        {
            var unsupportedConsumer = (byte[])consumer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(unsupportedConsumer.AsSpan(4, sizeof(int)),
                unsupportedVersion);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(unsupportedConsumer, producer));
        }

        const byte BoundUnsafeStatementTag = 11;
        var unsafeScope = new byte[1 + (2 * sizeof(int))];
        unsafeScope[0] = BoundUnsafeStatementTag;
        BinaryPrimitives.WriteInt32LittleEndian(unsafeScope.AsSpan(1), unsafeBlock.Span.Start);
        BinaryPrimitives.WriteInt32LittleEndian(unsafeScope.AsSpan(1 + sizeof(int)), unsafeBlock.Span.Length);
        var unsafeScopeOffset = consumer.AsSpan().IndexOf(unsafeScope);
        Assert.That(unsafeScopeOffset >= 0, Is.True);
        Assert.That(consumer.AsSpan().LastIndexOf(unsafeScope), Is.EqualTo(unsafeScopeOffset));
        var forgedConsumer = new byte[consumer.Length - unsafeScope.Length];
        consumer.AsSpan(0, unsafeScopeOffset).CopyTo(forgedConsumer);
        consumer.AsSpan(unsafeScopeOffset + unsafeScope.Length)
            .CopyTo(forgedConsumer.AsSpan(unsafeScopeOffset));
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(forgedConsumer, producer));

        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(NativeJit.Execute(linked, runtimePolicy), Is.EqualTo(45));
        Assert.That(NativeEmitter.Emit(linked, runtimePolicy), Is.Not.Empty);

        var safeReplacementSource = UnsafeFieldModuleSource
            .Replace("public unsafe mut", "public mut", StringComparison.Ordinal)
            .Replace("unsafe { Value = value; }", "Value = value;", StringComparison.Ordinal);
        var safeReplacement = ModuleCompiler.CreateProducer(
            CompileLibrary(safeReplacementSource, policy), ["MakeOuter", "MakeLeafOuter"]);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(consumer, safeReplacement));

        var safeConsumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                UnsafeOuter outer = MakeOuter(40);
                UnsafeLeafOuter leaf = MakeLeafOuter(1);
                return outer.Value.Value + leaf.Value.Value;
            }
            """), ModuleCompiler.CreateReference(safeReplacement), policy);
        Assert.That(safeConsumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, safeConsumerCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual17 = safeConsumerCompilation.Program; Assert.That(__nunitActual17, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual17!; }))(), safeReplacement);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NativeModuleSetsAndGraphsRoundTripCallerUnsafeCopyStructFieldExpressionsAsync(
        BoundRuntimePolicy policy)
    {
        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        string[] exports = ["MakeOuter", "MakeLeafOuter", "ReadValue"];
        var provider = NativeModuleCompiler.CreateProducer(
            CompileLibrary(UnsafeFieldExpressionModuleSource, policy), exports,
            "UnsafeFieldExpressionProvider", runtimePolicy);

        var graphCompilation = Compilation.Create(
            SyntaxTree.Parse(UnsafeFieldModuleExpressionConsumerSource),
            NativeModuleCompiler.CreateGraphReference([provider]), policy);
        Assert.That(graphCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual18 = graphCompilation.Program; Assert.That(__nunitActual18, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual18!; }))(), [provider], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [provider]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(
            graphConsumer, [provider], TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(121));

        var helper = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }", policy),
            ["Helper"], "UnsafeFieldExpressionHelper", runtimePolicy);
        var providers = new[] { provider, helper };
        var setCompilation = Compilation.Create(
            SyntaxTree.Parse(UnsafeFieldModuleExpressionConsumerSource),
            NativeModuleCompiler.CreateReferenceSet(providers), policy);
        Assert.That(setCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual19 = setCompilation.Program; Assert.That(__nunitActual19, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual19!; }))(), providers, runtimePolicy);
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, providers);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(
            consumerSet, providers, TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(121));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void SemanticModulesRoundTripCallerUnsafeMutableFieldReceiverLoans(BoundRuntimePolicy policy)
    {
        var program = CompileLibrary(UnsafeMutableFieldLoanModuleSource, policy);
        string[] blockExports = ["InvokeUnsafeLeaf", "InvokeUnsafeAncestor"];
        string[] expressionExports = [
            "InvokeExpressionScoped", "InvokeExpressionScopedAncestor", "InvokeExpressionScopedUnsafeMethod",
        ];
        string[] exports = [.. blockExports, .. expressionExports];
        var producer = ModuleCompiler.CreateProducer(program, exports);
        var callerUnsafeMutableBlockProgram = CompileLibrary(UnsafeMutableFieldLoanModuleSource.Replace(
            "public mut int32 Next()", "public unsafe mut int32 Next()", StringComparison.Ordinal), policy);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(
            callerUnsafeMutableBlockProgram, ["InvokeUnsafeLeaf"]));
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(
            program, ["LoanCounter.UnsafeNextBy"]));

        var expressionFunction = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "InvokeExpressionScoped");
        var expressionFunctionIndex = program.Functions.IndexOf(expressionFunction);
        var expressionReturn = ((global::System.Func<BoundReturn>)(() => { var __nunitActual20 = expressionFunction.Body.Statements[^1]; Assert.That(__nunitActual20, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual20!; }))();
        var expressionScope = ((global::System.Func<BoundUnsafeExpression>)(() => { var __nunitActual21 = expressionReturn.Value; Assert.That(__nunitActual21, Is.TypeOf<BoundUnsafeExpression>()); return (BoundUnsafeExpression)__nunitActual21!; }))();
        var expressionCall = ((global::System.Func<BoundCall>)(() => { var __nunitActual22 = expressionScope.Expression; Assert.That(__nunitActual22, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual22!; }))();
        var receiverLoan = ((global::System.Func<BoundMutableFieldLoan>)(() => { var __nunitActual23 = expressionCall.Arguments[0]; Assert.That(__nunitActual23, Is.TypeOf<BoundMutableFieldLoan>()); return (BoundMutableFieldLoan)__nunitActual23!; }))();

        var wrappedReceiverCall = expressionCall with {
            Arguments = expressionCall.Arguments.SetItem(
                0, new BoundUnsafeExpression(receiverLoan, receiverLoan.Span)),
        };
        var wrappedReceiverFunction = expressionFunction with {
            Body = expressionFunction.Body with {
                Statements = expressionFunction.Body.Statements.SetItem(
                    expressionFunction.Body.Statements.Length - 1,
                    expressionReturn with { Value = expressionScope with { Expression = wrappedReceiverCall } }),
            },
        };
        var wrappedReceiverProgram = program with {
            Functions = program.Functions.SetItem(expressionFunctionIndex, wrappedReceiverFunction),
        };
        var wrappedReceiverException = TestExceptionCapture.Capture(() =>
            ModuleCompiler.CreateProducer(wrappedReceiverProgram, ["InvokeExpressionScoped"]));
        Assert.That(wrappedReceiverException is ModuleContractException or ArgumentException, Is.True, wrappedReceiverException?.ToString());

        var remappedReceiverCall = WithArgumentParameterIndices(expressionCall, [1, 0, 2]);
        var remappedReceiverFunction = expressionFunction with {
            Body = expressionFunction.Body with {
                Statements = expressionFunction.Body.Statements.SetItem(
                    expressionFunction.Body.Statements.Length - 1,
                    expressionReturn with { Value = expressionScope with { Expression = remappedReceiverCall } }),
            },
        };
        var remappedReceiverProgram = program with {
            Functions = program.Functions.SetItem(expressionFunctionIndex, remappedReceiverFunction),
        };
        var remappedReceiverException = TestExceptionCapture.Capture(() =>
            ModuleCompiler.CreateProducer(remappedReceiverProgram, ["InvokeExpressionScoped"]));
        Assert.That(remappedReceiverException is ModuleContractException or ArgumentException, Is.True, remappedReceiverException?.ToString());
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateReference(WrapSerializedMutableFieldLoan(producer, receiverLoan)));
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateReference(RemapSerializedCallReceiver(producer, "LoanCounter.NextBy")));

        var unsafeMethodFunction = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "InvokeExpressionScopedUnsafeMethod");
        var unsafeMethodFunctionIndex = program.Functions.IndexOf(unsafeMethodFunction);
        var unsafeMethodReturn = ((global::System.Func<BoundReturn>)(() => { var __nunitActual24 = unsafeMethodFunction.Body.Statements[^1]; Assert.That(__nunitActual24, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual24!; }))();
        var unsafeMethodScope = ((global::System.Func<BoundUnsafeExpression>)(() => { var __nunitActual25 = unsafeMethodReturn.Value; Assert.That(__nunitActual25, Is.TypeOf<BoundUnsafeExpression>()); return (BoundUnsafeExpression)__nunitActual25!; }))();
        var unsafeMethodCall = ((global::System.Func<BoundCall>)(() => { var __nunitActual26 = unsafeMethodScope.Expression; Assert.That(__nunitActual26, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual26!; }))();
        var unsafeMethodLoan = ((global::System.Func<BoundMutableFieldLoan>)(() => { var __nunitActual27 = unsafeMethodCall.Arguments[0]; Assert.That(__nunitActual27, Is.TypeOf<BoundMutableFieldLoan>()); return (BoundMutableFieldLoan)__nunitActual27!; }))();
        var unscopedUnsafeMethodFunction = unsafeMethodFunction with {
            Body = unsafeMethodFunction.Body with {
                Statements = unsafeMethodFunction.Body.Statements.SetItem(
                    unsafeMethodFunction.Body.Statements.Length - 1,
                    unsafeMethodReturn with { Value = unsafeMethodCall }),
            },
        };
        var unscopedUnsafeMethodProgram = program with {
            Functions = program.Functions.SetItem(unsafeMethodFunctionIndex, unscopedUnsafeMethodFunction),
        };
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(
            unscopedUnsafeMethodProgram, ["InvokeExpressionScopedUnsafeMethod"]));

        var remappedUnsafeMethodCall = WithArgumentParameterIndices(unsafeMethodCall, [1, 0, 2]);
        var remappedUnsafeMethodFunction = unsafeMethodFunction with {
            Body = unsafeMethodFunction.Body with {
                Statements = unsafeMethodFunction.Body.Statements.SetItem(
                    unsafeMethodFunction.Body.Statements.Length - 1,
                    unsafeMethodReturn with {
                        Value = unsafeMethodScope with { Expression = remappedUnsafeMethodCall },
                    }),
            },
        };
        var remappedUnsafeMethodProgram = program with {
            Functions = program.Functions.SetItem(unsafeMethodFunctionIndex, remappedUnsafeMethodFunction),
        };
        var remappedUnsafeMethodException = TestExceptionCapture.Capture(() => ModuleCompiler.CreateProducer(
            remappedUnsafeMethodProgram, ["InvokeExpressionScopedUnsafeMethod"]));
        Assert.That(remappedUnsafeMethodException is ModuleContractException or ArgumentException, Is.True, remappedUnsafeMethodException?.ToString());
        foreach (var invalidMapping in new[] {
            ImmutableArray.Create(0, 0, 1),
            ImmutableArray.Create(0, 1, 3),
        })
        {
            var invalidMappingCall = WithArgumentParameterIndices(unsafeMethodCall, invalidMapping);
            var invalidMappingFunction = unsafeMethodFunction with {
                Body = unsafeMethodFunction.Body with {
                    Statements = unsafeMethodFunction.Body.Statements.SetItem(
                        unsafeMethodFunction.Body.Statements.Length - 1,
                        unsafeMethodReturn with {
                            Value = unsafeMethodScope with { Expression = invalidMappingCall },
                        }),
                },
            };
            var invalidMappingProgram = program with {
                Functions = program.Functions.SetItem(unsafeMethodFunctionIndex, invalidMappingFunction),
            };
            var invalidMappingException = TestExceptionCapture.Capture(() => ModuleCompiler.CreateProducer(
                invalidMappingProgram, ["InvokeExpressionScopedUnsafeMethod"]));
            Assert.That(invalidMappingException is ModuleContractException or ArgumentException, Is.True, invalidMappingException?.ToString());
        }
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateReference(WrapSerializedMutableFieldLoan(producer, unsafeMethodLoan)));
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateReference(RemapSerializedCallReceiver(producer, "LoanCounter.UnsafeNextBy")));
        foreach (var invalidMapping in new[] {
            new[] { 0, 0, 1 },
            new[] { 0, 1, 3 },
        })
        {
            var invalidMappingArtifact = SetSerializedCallParameterMapping(
                producer, "LoanCounter.UnsafeNextBy", invalidMapping);
            var invalidMappingReadException = TestExceptionCapture.Capture(() => ModuleCompiler.CreateReference(invalidMappingArtifact));
            Assert.That(invalidMappingReadException is ModuleContractException or ModuleFormatException, Is.True, invalidMappingReadException?.ToString());
        }
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateReference(StripSerializedUnsafeExpression(producer, unsafeMethodScope)));
        var unsafeMethodContract = GetModuleStringStartingWith(producer, "LoanCounter.UnsafeNextBy|");
        Assert.That(unsafeMethodContract, Does.Contain("|MutReceiver|"));
        var mismatchedReceiverMarkerException = TestExceptionCapture.Capture(() => ModuleCompiler.CreateReference(
            ReplaceModuleString(producer, unsafeMethodContract,
                unsafeMethodContract.Replace("|MutReceiver|", "|", StringComparison.Ordinal))));
        Assert.That(mismatchedReceiverMarkerException is ModuleContractException or ModuleFormatException, Is.True, mismatchedReceiverMarkerException?.ToString());

        var unsafeBlockMethodProgram = CompileLibrary(UnsafeMutableFieldLoanModuleSource + """

            public int32 InvokeUnsafeBlockMethod() {
                mut UnsafeLeafOwner owner = new UnsafeLeafOwner(new LoanCounter(40));
                unsafe { return owner.Value.NextBy(second: 1, first: 0); }
            }
            """, policy);
        var unsafeBlockMethodArtifact = ModuleCompiler.CreateProducer(
            unsafeBlockMethodProgram, ["InvokeUnsafeBlockMethod"]);
        var safeMethodContract = GetModuleStringStartingWith(
            unsafeBlockMethodArtifact, "LoanCounter.NextBy|");
        var forgedUnsafeBlockMethod = ReplaceModuleString(
            unsafeBlockMethodArtifact, safeMethodContract, safeMethodContract + "|CallerUnsafe");
        forgedUnsafeBlockMethod = SetSerializedFunctionRequiresUnsafeCaller(
            forgedUnsafeBlockMethod, "LoanCounter.NextBy");
        Assert.Throws<ModuleContractException>(
            () => ModuleCompiler.CreateReference(forgedUnsafeBlockMethod));

        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                return InvokeUnsafeLeaf() + InvokeUnsafeAncestor()
                    + InvokeExpressionScoped() + InvokeExpressionScopedAncestor()
                    + InvokeExpressionScopedUnsafeMethod();
            }
            """), reference, policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumerProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual28 = consumerCompilation.Program; Assert.That(__nunitActual28, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual28!; }))();
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram, producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        Assert.That(NativeJit.Execute(linked, runtimePolicy), Is.EqualTo(125));

        var receiverConsumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            struct DiscardedBeforeLoanCounter { public int32 Value; }
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct LoanCounter {
                public mut int32 Number;
                public LoanCounter(int32 number) { Number = number; }
                public mut int32 Next() { Number = Number + 1; return Number; }
                public mut int32 NextBy(int32 first, int32 second) {
                    Number = Number + first * 10 + second;
                    return Number;
                }
            }
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct UnsafeLeafOwner {
                public unsafe mut LoanCounter Value;
                public UnsafeLeafOwner(LoanCounter value) { unsafe { Value = value; } }
            }
            int32 Main() {
                mut UnsafeLeafOwner owner = new UnsafeLeafOwner(new LoanCounter(40));
                return unsafe(owner.Value.NextBy(second: 1, first: 0));
            }
            """), policy);
        Assert.That(receiverConsumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, receiverConsumerCompilation.Diagnostics));
        var receiverConsumerProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual29 = receiverConsumerCompilation.Program; Assert.That(__nunitActual29, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual29!; }))();
        var unrelatedProducer = ModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }", policy), ["Helper"]);
        var consumerLocalCompilation = Compilation.Create(SyntaxTree.Parse(
            UnsafeMutableFieldLoanModuleSource + """

            int32 Main() {
                mut UnsafeLeafOwner owner = new UnsafeLeafOwner(new LoanCounter(40));
                return unsafe(owner.Value.UnsafeNextBy(second: 1, first: 0));
            }
            """), ModuleCompiler.CreateReference(unrelatedProducer), policy);
        Assert.That(consumerLocalCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerLocalCompilation.Diagnostics));
        var consumerLocalProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual30 = consumerLocalCompilation.Program; Assert.That(__nunitActual30, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual30!; }))();
        var consumerLocal = ModuleCompiler.CreateConsumer(consumerLocalProgram, unrelatedProducer);
        Assert.That(NativeJit.Execute(
            ModuleCompiler.Link(consumerLocal, unrelatedProducer), runtimePolicy), Is.EqualTo(41));
        var consumerLocalMain = global::System.Linq.Enumerable.Single(consumerLocalProgram.Functions, function => function.Name == "Main");
        var consumerLocalReturn = ((global::System.Func<BoundReturn>)(() => { var __nunitActual31 = consumerLocalMain.Body.Statements[^1]; Assert.That(__nunitActual31, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual31!; }))();
        var consumerLocalScope = ((global::System.Func<BoundUnsafeExpression>)(() => { var __nunitActual32 = consumerLocalReturn.Value; Assert.That(__nunitActual32, Is.TypeOf<BoundUnsafeExpression>()); return (BoundUnsafeExpression)__nunitActual32!; }))();
        var strippedConsumerLocal = StripSerializedUnsafeExpression(consumerLocal, consumerLocalScope);
        var strippedConsumerLocalException = TestExceptionCapture.Capture(() => ModuleCompiler.Link(strippedConsumerLocal, unrelatedProducer));
        Assert.That(strippedConsumerLocalException is ModuleContractException or ModuleFormatException, Is.True, strippedConsumerLocalException?.ToString());
        foreach (var invalidMapping in new[] {
            new[] { 0, 0, 1 },
            new[] { 0, 1, 3 },
        })
        {
            var invalidMappingConsumer = SetSerializedCallParameterMapping(
                consumerLocal, "LoanCounter.UnsafeNextBy", invalidMapping);
            var invalidMappingLinkException = TestExceptionCapture.Capture(() => ModuleCompiler.Link(invalidMappingConsumer, unrelatedProducer));
            Assert.That(invalidMappingLinkException is ModuleContractException or ModuleFormatException, Is.True, invalidMappingLinkException?.ToString());
        }
        var localReceiverCompilation = Compilation.Create(SyntaxTree.Parse(
            UnsafeMutableFieldLoanModuleSource + """

            int32 Main() {
                mut LoanCounter value = new LoanCounter(40);
                return unsafe(value.UnsafeNextBy(second: 1, first: 0));
            }
            """), ModuleCompiler.CreateReference(unrelatedProducer), policy);
        Assert.That(localReceiverCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, localReceiverCompilation.Diagnostics));
        var localReceiverProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual33 = localReceiverCompilation.Program; Assert.That(__nunitActual33, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual33!; }))();
        var localReceiverException = TestExceptionCapture.Capture(() => ModuleCompiler.CreateConsumer(localReceiverProgram, unrelatedProducer));
        Assert.That(localReceiverException is ModuleContractException or ArgumentException, Is.True, localReceiverException?.ToString());
        var receiverConsumer = ModuleCompiler.CreateConsumer(receiverConsumerProgram, unrelatedProducer);
        Assert.That(NativeJit.Execute(
            ModuleCompiler.Link(receiverConsumer, unrelatedProducer), runtimePolicy), Is.EqualTo(41));
        var receiverConsumerMain = global::System.Linq.Enumerable.Single(receiverConsumerProgram.Functions, function => function.Name == "Main");
        var receiverConsumerReturn = ((global::System.Func<BoundReturn>)(() => { var __nunitActual34 = receiverConsumerMain.Body.Statements[^1]; Assert.That(__nunitActual34, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual34!; }))();
        var receiverConsumerScope = ((global::System.Func<BoundUnsafeExpression>)(() => { var __nunitActual35 = receiverConsumerReturn.Value; Assert.That(__nunitActual35, Is.TypeOf<BoundUnsafeExpression>()); return (BoundUnsafeExpression)__nunitActual35!; }))();
        var receiverConsumerCall = ((global::System.Func<BoundCall>)(() => { var __nunitActual36 = receiverConsumerScope.Expression; Assert.That(__nunitActual36, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual36!; }))();
        var receiverConsumerLoan = ((global::System.Func<BoundMutableFieldLoan>)(() => { var __nunitActual37 = receiverConsumerCall.Arguments[0]; Assert.That(__nunitActual37, Is.TypeOf<BoundMutableFieldLoan>()); return (BoundMutableFieldLoan)__nunitActual37!; }))();
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(
            WrapSerializedMutableFieldLoan(receiverConsumer, receiverConsumerLoan), unrelatedProducer));
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(
            RemapSerializedCallReceiver(receiverConsumer, "LoanCounter.NextBy"), unrelatedProducer));

        foreach (var export in blockExports)
        {
            var function = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == export);
            var functionIndex = program.Functions.IndexOf(function);
            var unsafeBlock = ((global::System.Func<BoundUnsafe>)(() => { var __nunitActual38 = function.Body.Statements[^1]; Assert.That(__nunitActual38, Is.TypeOf<BoundUnsafe>()); return (BoundUnsafe)__nunitActual38!; }))();
            var forgedFunction = function with {
                Body = function.Body with {
                    Statements = function.Body.Statements.SetItem(
                        function.Body.Statements.Length - 1, unsafeBlock.Body),
                },
            };
            var forgedProgram = program with {
                Functions = program.Functions.SetItem(functionIndex, forgedFunction),
            };
            var forgedProgramException = TestExceptionCapture.Capture(() => ModuleCompiler.CreateProducer(forgedProgram, [export]));
            Assert.That(forgedProgramException is ModuleContractException or ArgumentException, Is.True, forgedProgramException?.ToString());

            var singleExport = ModuleCompiler.CreateProducer(program, [export]);
            var unsafeScope = new byte[1 + (2 * sizeof(int))];
            unsafeScope[0] = 11;
            BinaryPrimitives.WriteInt32LittleEndian(unsafeScope.AsSpan(1), unsafeBlock.Span.Start);
            BinaryPrimitives.WriteInt32LittleEndian(unsafeScope.AsSpan(1 + sizeof(int)),
                unsafeBlock.Span.Length);
            var unsafeScopeOffset = singleExport.AsSpan().IndexOf(unsafeScope);
            Assert.That(unsafeScopeOffset >= 0, Is.True);
            var forgedArtifact = new byte[singleExport.Length - unsafeScope.Length];
            singleExport.AsSpan(0, unsafeScopeOffset).CopyTo(forgedArtifact);
            singleExport.AsSpan(unsafeScopeOffset + unsafeScope.Length)
                .CopyTo(forgedArtifact.AsSpan(unsafeScopeOffset));
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forgedArtifact));
        }

        foreach (var export in expressionExports)
        {
            var function = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == export);
            var functionIndex = program.Functions.IndexOf(function);
            var returnStatement = ((global::System.Func<BoundReturn>)(() => { var __nunitActual39 = function.Body.Statements[^1]; Assert.That(__nunitActual39, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual39!; }))();
            var unsafeExpression = ((global::System.Func<BoundUnsafeExpression>)(() => { var __nunitActual40 = returnStatement.Value; Assert.That(__nunitActual40, Is.TypeOf<BoundUnsafeExpression>()); return (BoundUnsafeExpression)__nunitActual40!; }))();
            var forgedFunction = function with {
                Body = function.Body with {
                    Statements = function.Body.Statements.SetItem(
                        function.Body.Statements.Length - 1,
                        returnStatement with { Value = unsafeExpression.Expression }),
                },
            };
            var forgedProgram = program with {
                Functions = program.Functions.SetItem(functionIndex, forgedFunction),
            };
            var forgedProgramException = TestExceptionCapture.Capture(() => ModuleCompiler.CreateProducer(forgedProgram, [export]));
            Assert.That(forgedProgramException is ModuleContractException or ArgumentException, Is.True, forgedProgramException?.ToString());

            var singleExport = ModuleCompiler.CreateProducer(program, [export]);
            var forgedArtifact = StripSerializedUnsafeExpression(singleExport, unsafeExpression);
            var forgedArtifactException = TestExceptionCapture.Capture(() => ModuleCompiler.CreateReference(forgedArtifact));
            Assert.That(forgedArtifactException is ModuleFormatException or ModuleContractException, Is.True, forgedArtifactException?.ToString());
        }
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void UnsafeExpressionLoanCannotInvalidateAnAlreadyReservedSibling(BoundRuntimePolicy policy)
    {
        const string Source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct LoanCounter {
                public mut int32 Number;
                public LoanCounter(int32 number) { Number = number; }
                public mut int32 Next() { Number = Number + 1; return Number; }
                public mut int32 Add(int32 amount) { Number = Number + amount; return Number; }
            }
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct UnsafeLoanOwner {
                public mut LoanCounter SafeChild;
                public unsafe mut LoanCounter UnsafeChild;
                public UnsafeLoanOwner(LoanCounter safeChild, LoanCounter unsafeChild) {
                    SafeChild = safeChild;
                    unsafe { UnsafeChild = unsafeChild; }
                }
            }
            int32 Main() {
                mut UnsafeLoanOwner owner =
                    new UnsafeLoanOwner(new LoanCounter(10), new LoanCounter(20));
                return owner.SafeChild.Add(unsafe(owner.UnsafeChild.Next()));
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source), policy);

        Assert.That(compilation.HasErrors, Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NativeModuleSetsAndGraphsRoundTripCallerUnsafeMutableFieldReceiverLoansAsync(
        BoundRuntimePolicy policy)
    {
        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        string[] exports = [
            "InvokeUnsafeLeaf", "InvokeUnsafeAncestor",
            "InvokeExpressionScoped", "InvokeExpressionScopedAncestor", "InvokeExpressionScopedUnsafeMethod",
        ];
        var provider = NativeModuleCompiler.CreateProducer(
            CompileLibrary(UnsafeMutableFieldLoanModuleSource, policy), exports,
            "UnsafeLoanProvider", runtimePolicy);
        var helper = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }", policy),
            ["Helper"], "UnsafeLoanHelper", runtimePolicy);
        byte[][] providers = [provider, helper];

        var setCompilation = Compilation.Create(SyntaxTree.Parse(
            ConsumerLocalUnsafeMutableFieldLoanSource + """
            int32 Main() {
                mut ConsumerUnsafeLeafOwner owner =
                    new ConsumerUnsafeLeafOwner(new ConsumerLoanCounter(40));
                return InvokeUnsafeLeaf() + InvokeUnsafeAncestor()
                    + InvokeExpressionScoped() + InvokeExpressionScopedAncestor()
                    + InvokeExpressionScopedUnsafeMethod()
                    + unsafe(owner.Value.UnsafeNextBy(second: 1, first: 0));
            }
            """), NativeModuleCompiler.CreateReferenceSet(providers), policy);
        Assert.That(setCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual41 = setCompilation.Program; Assert.That(__nunitActual41, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual41!; }))(), providers, runtimePolicy);
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, providers);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(166));

        var middleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            ConsumerLocalUnsafeMutableFieldLoanSource + """
            public int32 Forward() {
                mut ConsumerUnsafeLeafOwner owner =
                    new ConsumerUnsafeLeafOwner(new ConsumerLoanCounter(40));
                return InvokeUnsafeLeaf() + InvokeUnsafeAncestor()
                    + InvokeExpressionScoped() + InvokeExpressionScopedAncestor()
                    + InvokeExpressionScopedUnsafeMethod()
                    + unsafe(owner.Value.UnsafeNextBy(second: 1, first: 0));
            }
            """), NativeModuleCompiler.CreateGraphReference([provider]), policy);
        Assert.That(middleCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, middleCompilation.Diagnostics));
        var middle = NativeModuleCompiler.CreateGraphProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual42 = middleCompilation.Program; Assert.That(__nunitActual42, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual42!; }))(), ["Forward"],
            "UnsafeLoanMiddle", [provider], runtimePolicy);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Forward(); }"),
            NativeModuleCompiler.CreateGraphReference([middle]), policy);
        Assert.That(rootCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual43 = rootCompilation.Program; Assert.That(__nunitActual43, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual43!; }))(), [middle], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(root, [middle, provider]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [provider, middle],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(166));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NativeModuleSetsAndGraphsRoundTripCallerUnsafeCopyStructFieldsAsync(
        BoundRuntimePolicy policy)
    {
        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        var provider = NativeModuleCompiler.CreateProducer(
            CompileLibrary(UnsafeFieldModuleSource, policy), ["MakeOuter", "MakeLeafOuter"],
            "UnsafeFieldProvider", runtimePolicy);
        var helper = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }", policy),
            ["Helper"], "UnsafeFieldHelper", runtimePolicy);
        byte[][] providers = [provider, helper];

        var setCompilation = Compilation.Create(SyntaxTree.Parse(UnsafeFieldModuleConsumerSource),
            NativeModuleCompiler.CreateReferenceSet(providers), policy);
        Assert.That(setCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual44 = setCompilation.Program; Assert.That(__nunitActual44, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual44!; }))(), providers, runtimePolicy);
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, providers);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(45));
        var safeFieldSource = UnsafeFieldModuleSource
            .Replace("public unsafe mut", "public mut", StringComparison.Ordinal)
            .Replace("unsafe { Value = value; }", "Value = value;", StringComparison.Ordinal);
        var safeProvider = NativeModuleCompiler.CreateProducer(
            CompileLibrary(safeFieldSource, policy), ["MakeOuter", "MakeLeafOuter"],
            "UnsafeFieldProvider", runtimePolicy);
        Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateConsumerSet(consumerSet, [safeProvider, helper]));

        var middleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Forward() {
                mut UnsafeOuter outer = MakeOuter(40);
                unsafe {
                    outer.Value.Value = 42;
                    return outer.Value.Value;
                }
            }
            """), NativeModuleCompiler.CreateGraphReference([provider]), policy);
        Assert.That(middleCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, middleCompilation.Diagnostics));
        var middle = NativeModuleCompiler.CreateGraphProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual45 = middleCompilation.Program; Assert.That(__nunitActual45, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual45!; }))(), ["Forward"],
            "UnsafeFieldMiddle", [provider], runtimePolicy);

        var rootCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Forward(); }"),
            NativeModuleCompiler.CreateGraphReference([middle]), policy);
        Assert.That(rootCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual46 = rootCompilation.Program; Assert.That(__nunitActual46, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual46!; }))(), [middle], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(root, [middle, provider]);
        Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateGraph(root, [middle, safeProvider]));
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [provider, middle],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public void GenericSpecializationPreservesCallerUnsafeRequirement()
    {
        const string declaration = """
            unsafe T Identity<T>(T value) where T : [Copy] { return value; }
            """;

        var safeCall = Compilation.Create(SyntaxTree.Parse(declaration
            + " int32 Main() { return Identity<int32>(42); }"));
        Assert.That(safeCall.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(safeCall.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var authorizedCall = Compilation.Create(SyntaxTree.Parse(declaration
            + " int32 Main() { return unsafe(Identity<int32>(42)); }"));
        Assert.That(authorizedCall.HasErrors, Is.False, string.Join(Environment.NewLine, authorizedCall.Diagnostics));
    }

    [Test]
    public void UnsafeStaticOperatorsRequireAuthorizationForBinaryUnaryAndUpdateSyntax()
    {
        const string declaration = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Number {
                public int32 Value;
                public Number(int32 value) { Value = value; }
                public static unsafe Number operator -(Number value) {
                    return new Number(0 - value.Value);
                }
                public static unsafe Number operator +(Number left, Number right) {
                    return new Number(left.Value + right.Value);
                }
                public static unsafe Number operator +(Number value) {
                    return new Number(value.Value);
                }
                public static unsafe Number operator ++(Number value) {
                    return new Number(value.Value + 1);
                }
            }
            """;

        const string body = """
            int32 Main() {
                Number left = new Number(2);
                Number right = new Number(3);
                Number combined = left + right;
                Number unary = +left;
                mut Number updated = left;
                ++updated;
                return combined.Value + unary.Value + updated.Value;
            }
            """;

        var safeUse = Compilation.Create(SyntaxTree.Parse(declaration + body));
        Assert.That(safeUse.Program, Is.Null);
        Assert.That(safeUse.Diagnostics.Count(static diagnostic => diagnostic.Code == "TRM3028"), Is.EqualTo(3));

        var authorizedUse = Compilation.Create(SyntaxTree.Parse(declaration + """
            int32 Main() {
                Number left = new Number(2);
                Number right = new Number(3);
                unsafe {
                    Number combined = left + right;
                    Number unary = +left;
                    mut Number updated = left;
                    ++updated;
                    return combined.Value + unary.Value + updated.Value;
                }
            }
            """));
        Assert.That(authorizedUse.HasErrors, Is.False, string.Join(Environment.NewLine, authorizedUse.Diagnostics));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task StaticOperatorIdentityAndUnsafeEffectSurviveModuleImportAsync(BoundRuntimePolicy policy)
    {
        const string declaration = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Magnitude {
                public int32 Value;
                public Magnitude(int32 value) { Value = value; }
            }
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Number {
                public int32 Value;
                public Number(int32 value) { Value = value; }
                public static Magnitude operator !(Number value) {
                    return new Magnitude(value.Value);
                }
                public static bool operator ==(Number left, Number right) {
                    return left.Value == right.Value;
                }
                public static Number operator +(Number left, Number right) {
                    return new Number(left.Value + right.Value);
                }
                public static unsafe Number operator -(Number left, Number right) {
                    return new Number(left.Value - right.Value);
                }
            }
            """;
        var producer = ModuleCompiler.CreateProducer(CompileLibrary(declaration, policy),
                ["Magnitude.#ctor", "Number.#ctor", "Number.op_LogicalNot", "Number.op_Equality",
                    "Number.op_Addition", "Number.op_Subtraction"]);
        var forgedProducer = (byte[])producer.Clone();
        var operatorName = Encoding.UTF8.GetBytes("Number.op_Addition");
        var functionNameOffset = forgedProducer.AsSpan().LastIndexOf(operatorName);
        Assert.That(functionNameOffset >= 0, Is.True);
        using (var reader = new BinaryReader(new MemoryStream(forgedProducer)))
        {
            reader.BaseStream.Position = functionNameOffset + operatorName.Length;
            _ = reader.ReadInt32();
            for (var flag = 0; flag < 4; flag++)
            {
                _ = reader.ReadBoolean();
            }
            var operatorMarkerOffset = (int)reader.BaseStream.Position;
            Assert.That(reader.ReadBoolean(), Is.True);
            forgedProducer[operatorMarkerOffset] = 0;
        }
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forgedProducer));
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.That(reference.Functions.Single(static function => function.Name == "Number.op_LogicalNot").IsOperator, Is.True);
        Assert.That(reference.Functions.Single(static function => function.Name == "Number.op_Equality").IsOperator, Is.True);
        Assert.That(reference.Functions.Single(static function => function.Name == "Number.op_Addition").IsOperator, Is.True);
        Assert.That(reference.Functions.Single(static function => function.Name == "Number.op_Subtraction").IsOperator, Is.True);
        var unscoped = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                Number left = new Number(40);
                Number right = new Number(2);
                Number difference = left - right;
                return difference.Value;
            }
            """), reference, policy);
        Assert.That(unscoped.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(unscoped.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        const string clientSource = """
            int32 Main() {
                Number left = new Number(40);
                Number right = new Number(2);
                Magnitude inverted = !left;
                Number sum = left + right;
                unsafe {
                    Number difference = left - right;
                    if (inverted.Value != 40 || !(left == new Number(40))
                        || sum.Value != 42 || difference.Value != 38) { return 1; }
                    return 42;
                }
            }
            """;
        var client = Compilation.Create(SyntaxTree.Parse(clientSource), reference, policy);
        Assert.That(client.HasErrors, Is.False, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual47 = client.Program; Assert.That(__nunitActual47, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual47!; }))(), producer);
        var malformedConsumer = (byte[])consumer.Clone();
        var canonicalName = Encoding.UTF8.GetBytes("Number.op_Addition");
        var firstNameOffset = malformedConsumer.AsSpan().IndexOf(canonicalName);
        Assert.That(firstNameOffset >= 0, Is.True);
        var contractNameOffset = firstNameOffset + canonicalName.Length
            + malformedConsumer.AsSpan(firstNameOffset + canonicalName.Length).IndexOf(canonicalName);
        Assert.That(contractNameOffset >= firstNameOffset + canonicalName.Length, Is.True);
        var malformedName = Encoding.UTF8.GetBytes("Number.op_FakeName");
        Assert.That(malformedName.Length, Is.EqualTo(canonicalName.Length));
        malformedName.CopyTo(malformedConsumer.AsSpan(contractNameOffset));
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(malformedConsumer, producer));
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), (NativeRuntimePolicy)policy), Is.EqualTo(42));

        var runtimePolicy = (NativeRuntimePolicy)policy;
        var nativeProducer = NativeModuleCompiler.CreateProducer(CompileLibrary(declaration, policy),
            ["Magnitude.#ctor", "Number.#ctor", "Number.op_LogicalNot", "Number.op_Equality",
                "Number.op_Addition", "Number.op_Subtraction"], "StaticOperators", runtimePolicy);
        var graphReference = NativeModuleCompiler.CreateGraphReference([nativeProducer]);
        var graphCompilation = Compilation.Create(SyntaxTree.Parse(clientSource), graphReference, policy);
        Assert.That(graphCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual48 = graphCompilation.Program; Assert.That(__nunitActual48, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual48!; }))(), [nativeProducer], runtimePolicy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(graphConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(42));

        var helper = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }", policy), ["Helper"],
            "StaticOperatorHelper", runtimePolicy);
        var providers = new[] { nativeProducer, helper };
        var setReference = NativeModuleCompiler.CreateReferenceSet(providers);
        var setCompilation = Compilation.Create(SyntaxTree.Parse(clientSource), setReference, policy);
        Assert.That(setCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual49 = setCompilation.Program; Assert.That(__nunitActual49, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual49!; }))(), providers, runtimePolicy);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(42));

        var ordinaryAddition = declaration.Replace("public static Number operator +(",
            "public static Number op_Addition(", StringComparison.Ordinal);
        var mismatchedProducer = ModuleCompiler.CreateProducer(CompileLibrary(ordinaryAddition, policy),
            ["Magnitude.#ctor", "Number.#ctor", "Number.op_LogicalNot", "Number.op_Equality",
                "Number.op_Addition", "Number.op_Subtraction"]);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(consumer, mismatchedProducer));
        var mismatchedNativeProducer = NativeModuleCompiler.CreateProducer(
            CompileLibrary(ordinaryAddition, policy),
            ["Magnitude.#ctor", "Number.#ctor", "Number.op_LogicalNot", "Number.op_Equality",
                "Number.op_Addition", "Number.op_Subtraction"], "StaticOperators", runtimePolicy);
        Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateGraph(graphConsumer, [mismatchedNativeProducer]));
        Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateConsumerSet(consumerSet, [mismatchedNativeProducer, helper]));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CallerUnsafeBinaryOperatorsRoundTripInUnsafeExpressionsAsync(BoundRuntimePolicy policy)
    {
        const string declaration = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Number {
                public int32 Value;
                public Number(int32 value) { Value = value; }
                public static unsafe Number operator -(Number value) {
                    return new Number(0 - value.Value);
                }
                public static unsafe Number operator +(Number left, Number right) {
                    return new Number(left.Value + right.Value);
                }
                public static unsafe Number operator *(Number left, Number right) {
                    return new Number(left.Value * right.Value);
                }
                public static unsafe bool operator ==(Number left, Number right) {
                    return left.Value == right.Value;
                }
                public static Number Combine(Number left, Number right, Number factor) {
                    return unsafe((left + right) * factor);
                }
                public static Number Negate(Number value) { return unsafe(-value); }
                public static bool Same(Number left, Number right) {
                    return unsafe(left == right);
                }
            }
            """;
        const string clientSource = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct LocalNumber {
                public int32 Value;
                public LocalNumber(int32 value) { Value = value; }
                public static unsafe LocalNumber operator +(LocalNumber left, LocalNumber right) {
                    return new LocalNumber(left.Value + right.Value);
                }
            }
            int32 Main() {
                Number result = Number.Combine(new Number(1), new Number(2), new Number(6));
                LocalNumber local = unsafe(new LocalNumber(20) + new LocalNumber(22));
                return Number.Same(result, new Number(18)) && local.Value == 42 ? 42 : 1;
            }
            """;
        var providerProgram = CompileLibrary(declaration, policy);
        string[] exports = [
            "Number.#ctor", "Number.op_Addition", "Number.op_Multiply", "Number.op_Equality",
            "Number.Combine", "Number.Same",
        ];
        var producer = ModuleCompiler.CreateProducer(providerProgram, exports);
        var reference = ModuleCompiler.CreateReference(producer);

        var unscopedCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                Number left = new Number(1);
                Number right = new Number(2);
                Number result = left + right;
                return result.Value;
            }
            """), reference, policy);
        Assert.That(unscopedCompilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(unscopedCompilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);
        var operandScopedCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                Number left = new Number(1);
                Number right = new Number(2);
                Number result = unsafe(left) + right;
                return result.Value;
            }
            """), reference, policy);
        Assert.That(operandScopedCompilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(operandScopedCompilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var combine = global::System.Linq.Enumerable.Single(providerProgram.Functions, static function => function.Name == "Number.Combine");
        var returned = ((global::System.Func<BoundReturn>)(() => { var __nunitActual50 = combine.Body.Statements[^1]; Assert.That(__nunitActual50, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual50!; }))();
        var scope = ((global::System.Func<BoundUnsafeExpression>)(() => { var __nunitActual51 = returned.Value; Assert.That(__nunitActual51, Is.TypeOf<BoundUnsafeExpression>()); return (BoundUnsafeExpression)__nunitActual51!; }))();
        var multiply = ((global::System.Func<BoundCall>)(() => { var __nunitActual52 = scope.Expression; Assert.That(__nunitActual52, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual52!; }))();
        Assert.That(providerProgram.Functions[multiply.Function].IsOperator, Is.True);
        var addition = ((global::System.Func<BoundCall>)(() => { var __nunitActual53 = multiply.Arguments[0]; Assert.That(__nunitActual53, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual53!; }))();
        Assert.That(providerProgram.Functions[addition.Function].IsOperator, Is.True);

        var unscopedCombine = combine with {
            Body = combine.Body with {
                Statements = combine.Body.Statements.SetItem(
                    combine.Body.Statements.Length - 1, returned with { Value = multiply }),
            },
        };
        var unscopedProgram = providerProgram with {
            Functions = providerProgram.Functions.SetItem(providerProgram.Functions.IndexOf(combine), unscopedCombine),
        };
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(unscopedProgram, exports));

        var remappedMultiply = WithArgumentParameterIndices(multiply, [1, 0]);
        var remappedCombine = combine with {
            Body = combine.Body with {
                Statements = combine.Body.Statements.SetItem(
                    combine.Body.Statements.Length - 1,
                    returned with { Value = scope with { Expression = remappedMultiply } }),
            },
        };
        var remappedProgram = providerProgram with {
            Functions = providerProgram.Functions.SetItem(providerProgram.Functions.IndexOf(combine), remappedCombine),
        };
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(remappedProgram, exports));
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(
            StripSerializedUnsafeExpression(producer, scope)));
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateReference(
            SetSerializedCallParameterMapping(producer, "Number.op_Multiply", [1, 0])));

        var semanticCompilation = Compilation.Create(SyntaxTree.Parse(clientSource), reference, policy);
        Assert.That(semanticCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, semanticCompilation.Diagnostics));
        var semanticConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual54 = semanticCompilation.Program; Assert.That(__nunitActual54, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual54!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(semanticConsumer, producer), (NativeRuntimePolicy)policy), Is.EqualTo(42));

        var runtimePolicy = (NativeRuntimePolicy)policy;
        var nativeProducer = NativeModuleCompiler.CreateProducer(providerProgram, exports,
            "CallerUnsafeBinaryOperators", runtimePolicy);
        var graphCompilation = Compilation.Create(SyntaxTree.Parse(clientSource),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), policy);
        Assert.That(graphCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual55 = graphCompilation.Program; Assert.That(__nunitActual55, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual55!; }))(), [nativeProducer], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [nativeProducer]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(
            graphConsumer, [nativeProducer], TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(42));

        var helper = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }", policy), ["Helper"],
            "CallerUnsafeBinaryOperatorHelper", runtimePolicy);
        var providers = new[] { nativeProducer, helper };
        var setCompilation = Compilation.Create(SyntaxTree.Parse(clientSource),
            NativeModuleCompiler.CreateReferenceSet(providers), policy);
        Assert.That(setCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual56 = setCompilation.Program; Assert.That(__nunitActual56, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual56!; }))(), providers, runtimePolicy);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(
            consumerSet, providers, TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CallerUnsafeOperatorsRoundTripInUnsafeExpressionsAsync(BoundRuntimePolicy policy)
    {
        const string declaration = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Magnitude {
                public int32 Value;
                public Magnitude(int32 value) { Value = value; }
            }
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Number {
                public int32 Value;
                public Number(int32 value) { Value = value; }
                public static unsafe Number operator +(Number value) {
                    return new Number(value.Value);
                }
                public static unsafe Number operator -(Number value) {
                    return new Number(0 - value.Value);
                }
                public static unsafe Magnitude operator !(Number value) {
                    return new Magnitude(value.Value);
                }
                public static unsafe Number operator ~(Number value) {
                    return new Number(~value.Value);
                }
                public static unsafe Number operator ++(Number value) {
                    return new Number(value.Value + 1);
                }
                public static unsafe Number operator --(Number value) {
                    return new Number(value.Value - 1);
                }
                public static Number Plus(Number value) { return unsafe(+value); }
                public static Number Negate(Number value) { return unsafe(-value); }
                public static Magnitude LogicalNot(Number value) { return unsafe(!value); }
                public static Number Complement(Number value) { return unsafe(~value); }
                public static Number Increment(Number value) {
                    mut Number mutable = value;
                    return unsafe(++mutable);
                }
                public static Number Decrement(Number value) {
                    mut Number mutable = value;
                    return unsafe(--mutable);
                }
            }
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct SafeHolder {
                public mut Number Value;
                public SafeHolder(Number value) { Value = value; }
            }
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct UnsafeHolder {
                public unsafe mut Number Value;
                public UnsafeHolder(Number value) { unsafe { Value = value; } }
            }
            public Number IncrementField(SafeHolder input) {
                mut SafeHolder holder = input;
                return unsafe(++holder.Value);
            }
            public Number IncrementUnsafeField(UnsafeHolder input) {
                mut UnsafeHolder holder = input;
                return unsafe(++holder.Value);
            }
            """;
        const string clientSource = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct LocalNumber {
                public int32 Value;
                public LocalNumber(int32 value) { Value = value; }
                public static unsafe bool operator !(LocalNumber value) {
                    return value.Value == 42;
                }
                public static unsafe LocalNumber operator ++(LocalNumber value) {
                    return new LocalNumber(value.Value + 1);
                }
                public static unsafe LocalNumber operator --(LocalNumber value) {
                    return new LocalNumber(value.Value - 1);
                }
            }
            int32 Main() {
                mut Number value = new Number(42);
                Number plus = Number.Plus(value);
                Number negative = Number.Negate(value);
                Magnitude magnitude = Number.LogicalNot(value);
                Number complement = Number.Complement(value);
                Number prefix = unsafe(++value);
                Number postfix = unsafe(value--);
                Number postfixIncrement = unsafe(value++);
                Number prefixDecrement = unsafe(--value);
                Number incremented = Number.Increment(new Number(41));
                Number decremented = Number.Decrement(new Number(43));
                Number safeField = IncrementField(new SafeHolder(new Number(41)));
                mut LocalNumber localValue = new LocalNumber(41);
                LocalNumber localPrefix = unsafe(++localValue);
                LocalNumber localPostfix = unsafe(localValue--);
                bool local = unsafe(!new LocalNumber(42));
                return plus.Value == 42 && negative.Value == -42
                    && magnitude.Value == 42 && complement.Value == ~42
                    && prefix.Value == 43 && postfix.Value == 43 && value.Value == 42
                    && postfixIncrement.Value == 42 && prefixDecrement.Value == 42
                    && incremented.Value == 42 && decremented.Value == 42
                    && safeField.Value == 42
                    && localPrefix.Value == 42 && localPostfix.Value == 42
                    && localValue.Value == 41 && local ? 42 : 1;
            }
            """;
        var providerProgram = CompileLibrary(declaration, policy);
        string[] exports = [
            "Magnitude.#ctor", "Number.#ctor", "Number.op_UnaryPlus",
            "Number.op_UnaryNegation", "Number.op_LogicalNot", "Number.op_OnesComplement",
            "Number.op_Increment", "Number.op_Decrement",
            "Number.Plus", "Number.Negate", "Number.LogicalNot", "Number.Complement",
            "Number.Increment", "Number.Decrement",
            "SafeHolder.#ctor", "IncrementField",
        ];
        var producer = ModuleCompiler.CreateProducer(providerProgram, exports);
        var reference = ModuleCompiler.CreateReference(producer);
        var increment = global::System.Linq.Enumerable.Single(providerProgram.Functions, static function => function.Name == "Number.Increment");
        var incrementReturn = ((global::System.Func<BoundReturn>)(() => { var __nunitActual57 = increment.Body.Statements[^1]; Assert.That(__nunitActual57, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual57!; }))();
        var incrementScope = ((global::System.Func<BoundUnsafeExpression>)(() => { var __nunitActual58 = incrementReturn.Value; Assert.That(__nunitActual58, Is.TypeOf<BoundUnsafeExpression>()); return (BoundUnsafeExpression)__nunitActual58!; }))();
        var incrementUpdate = ((global::System.Func<BoundNominalUpdate>)(() => { var __nunitActual59 = incrementScope.Expression; Assert.That(__nunitActual59, Is.TypeOf<BoundNominalUpdate>()); return (BoundNominalUpdate)__nunitActual59!; }))();
        Assert.That(providerProgram.Functions[incrementUpdate.Function].IsOperator, Is.True);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(
            StripSerializedUnsafeExpression(producer, incrementScope)));
        var unscopedReturn = incrementReturn with { Value = incrementUpdate };
        var unscopedIncrement = increment with {
            Body = increment.Body with {
                Statements = increment.Body.Statements.SetItem(
                    increment.Body.Statements.Length - 1, unscopedReturn),
            },
        };
        var unscopedProgram = providerProgram with {
            Functions = providerProgram.Functions.SetItem(
                providerProgram.Functions.IndexOf(increment), unscopedIncrement),
        };
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(unscopedProgram, exports));
        var forgedUpdateException = TestExceptionCapture.Capture(() => ModuleCompiler.CreateReference(
            SetSerializedNominalUpdateOperator(producer, incrementUpdate, TokenKind.MinusMinus)));
        Assert.That(forgedUpdateException is ModuleContractException or ModuleFormatException, Is.True, forgedUpdateException?.ToString());
        var unsafeFieldUpdateException = TestExceptionCapture.Capture(() => ModuleCompiler.CreateProducer(
            providerProgram, ["UnsafeHolder.#ctor", "Number.op_Increment", "IncrementUnsafeField"]));
        Assert.That(unsafeFieldUpdateException is ModuleContractException or ModuleFormatException, Is.True, unsafeFieldUpdateException?.ToString());

        var operandScopedCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                Number value = new Number(42);
                Number result = -unsafe(value);
                return result.Value;
            }
            """), reference, policy);
        Assert.That(operandScopedCompilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(operandScopedCompilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var unscopedUpdate = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut Number value = new Number(41);
                value++;
                return value.Value;
            }
            """), reference, policy);
        Assert.That(unscopedUpdate.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(unscopedUpdate.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var semanticCompilation = Compilation.Create(SyntaxTree.Parse(clientSource), reference, policy);
        Assert.That(semanticCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, semanticCompilation.Diagnostics));
        var semanticConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual60 = semanticCompilation.Program; Assert.That(__nunitActual60, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual60!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(semanticConsumer, producer), (NativeRuntimePolicy)policy), Is.EqualTo(42));

        var runtimePolicy = (NativeRuntimePolicy)policy;
        var nativeProducer = NativeModuleCompiler.CreateProducer(providerProgram, exports,
            "CallerUnsafeOperators", runtimePolicy);
        var graphCompilation = Compilation.Create(SyntaxTree.Parse(clientSource),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), policy);
        Assert.That(graphCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual61 = graphCompilation.Program; Assert.That(__nunitActual61, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual61!; }))(), [nativeProducer], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [nativeProducer]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(
            graphConsumer, [nativeProducer], TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(42));

        var helper = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }", policy), ["Helper"],
            "CallerUnsafeOperatorHelper", runtimePolicy);
        var providers = new[] { nativeProducer, helper };
        var setCompilation = Compilation.Create(SyntaxTree.Parse(clientSource),
            NativeModuleCompiler.CreateReferenceSet(providers), policy);
        Assert.That(setCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual62 = setCompilation.Program; Assert.That(__nunitActual62, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual62!; }))(), providers, runtimePolicy);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(
            consumerSet, providers, TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CallerUnsafeConstructorsRoundTripInUnsafeExpressionsAsync(BoundRuntimePolicy policy)
    {
        const string declaration = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Number {
                public int32 Value;
                public unsafe Number(int32 left, int32 right) { Value = left + right; }
                public static Number Create(int32 value) { return unsafe(new Number(value, 0)); }
            }
            """;
        const string clientSource = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct LocalNumber {
                public int32 Value;
                public unsafe LocalNumber() { Value = 20; }
            }
            int32 FromUnsafeBlock() {
                unsafe {
                    Number value = new Number(2, 0);
                    return value.Value;
                }
            }
            int32 ProjectConstructed() {
                return unsafe(new Number(right: 5, left: 15)).Value;
            }
            int32 Main() {
                Number imported = unsafe(new Number(right: 5, left: 15));
                LocalNumber local = unsafe(new LocalNumber());
                return ProjectConstructed() == imported.Value
                    && Number.Create(imported.Value).Value + local.Value + FromUnsafeBlock() == 42 ? 42 : 1;
            }
            """;
        var providerProgram = CompileLibrary(declaration, policy);
        string[] exports = ["Number.#ctor", "Number.Create"];
        var producer = ModuleCompiler.CreateProducer(providerProgram, exports);
        var reference = ModuleCompiler.CreateReference(producer);
        var create = global::System.Linq.Enumerable.Single(providerProgram.Functions, static function => function.Name == "Number.Create");
        var createReturn = ((global::System.Func<BoundReturn>)(() => { var __nunitActual63 = create.Body.Statements[^1]; Assert.That(__nunitActual63, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual63!; }))();
        var createScope = ((global::System.Func<BoundUnsafeExpression>)(() => { var __nunitActual64 = createReturn.Value; Assert.That(__nunitActual64, Is.TypeOf<BoundUnsafeExpression>()); return (BoundUnsafeExpression)__nunitActual64!; }))();
        var constructorCall = ((global::System.Func<BoundCall>)(() => { var __nunitActual65 = createScope.Expression; Assert.That(__nunitActual65, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual65!; }))();
        Assert.That(providerProgram.Functions[constructorCall.Function].IsConstructor, Is.True);
        Assert.That(providerProgram.Functions[constructorCall.Function].RequiresUnsafeCaller, Is.True);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(
            StripSerializedUnsafeExpression(producer, createScope)));
        var unscopedReturn = createReturn with { Value = constructorCall };
        var unscopedCreate = create with {
            Body = create.Body with {
                Statements = create.Body.Statements.SetItem(
                    create.Body.Statements.Length - 1, unscopedReturn),
            },
        };
        var unscopedProgram = providerProgram with {
            Functions = providerProgram.Functions.SetItem(
                providerProgram.Functions.IndexOf(create), unscopedCreate),
        };
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(unscopedProgram, exports));

        var unscopedCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                Number value = new Number(40, 0);
                return value.Value;
            }
            """), reference, policy);
        Assert.That(unscopedCompilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(unscopedCompilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var argumentOnlyCompilation = Compilation.Create(SyntaxTree.Parse("""
            unsafe int32 Danger() { return 40; }
            int32 Main() {
                Number value = new Number(unsafe(Danger()), 0);
                return value.Value;
            }
            """), reference, policy);
        Assert.That(argumentOnlyCompilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(argumentOnlyCompilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var compilation = Compilation.Create(SyntaxTree.Parse(clientSource), reference, policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumerProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual66 = compilation.Program; Assert.That(__nunitActual66, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual66!; }))();
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram, producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), (NativeRuntimePolicy)policy), Is.EqualTo(42));

        var safeDeclaration = declaration.Replace("public unsafe Number(",
            "public Number(", StringComparison.Ordinal);
        var safeProducer = ModuleCompiler.CreateProducer(CompileLibrary(safeDeclaration, policy), exports);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, safeProducer), (NativeRuntimePolicy)policy), Is.EqualTo(42));
        var safeReference = ModuleCompiler.CreateReference(safeProducer);
        var safeCompilation = Compilation.Create(SyntaxTree.Parse(clientSource), safeReference, policy);
        Assert.That(safeCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, safeCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual67 = safeCompilation.Program; Assert.That(__nunitActual67, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual67!; }))(), safeProducer);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));

        var runtimePolicy = (NativeRuntimePolicy)policy;
        var nativeProducer = NativeModuleCompiler.CreateProducer(providerProgram, exports,
            "CallerUnsafeConstructors", runtimePolicy);
        var graphCompilation = Compilation.Create(SyntaxTree.Parse(clientSource),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), policy);
        Assert.That(graphCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual68 = graphCompilation.Program; Assert.That(__nunitActual68, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual68!; }))(), [nativeProducer], runtimePolicy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(
            graphConsumer, [nativeProducer], TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(42));

        var helper = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }", policy), ["Helper"],
            "CallerUnsafeConstructorHelper", runtimePolicy);
        var providers = new[] { nativeProducer, helper };
        var setCompilation = Compilation.Create(SyntaxTree.Parse(clientSource),
            NativeModuleCompiler.CreateReferenceSet(providers), policy);
        Assert.That(setCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual69 = setCompilation.Program; Assert.That(__nunitActual69, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual69!; }))(), providers, runtimePolicy);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(
            consumerSet, providers, TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public void SemanticModulesRoundTripTopLevelCallerUnsafeContracts()
    {
        var providerProgram = CompileLibrary("public unsafe int32 Danger() { return 42; }");
        var producer = ModuleCompiler.CreateProducer(providerProgram, ["Danger"]);
        var reference = ModuleCompiler.CreateReference(producer);

        var safeCall = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Danger(); }"), reference);
        Assert.That(safeCall.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(safeCall.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var scopedCall = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { unsafe { return Danger(); } }"), reference);
        Assert.That(scopedCall.HasErrors, Is.False, string.Join(Environment.NewLine, scopedCall.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual70 = scopedCall.Program; Assert.That(__nunitActual70, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual70!; }))(), producer);
        foreach (var unsupportedVersion in new[] { 69, 70, 71, 72, 73, 74, 75 })
        {
            var unsupportedConsumer = (byte[])consumer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(unsupportedConsumer.AsSpan(4, sizeof(int)),
                unsupportedVersion);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(unsupportedConsumer, producer));
        }
        var linked = ModuleCompiler.Link(consumer, producer);
        SemanticVerifier.Verify(linked);
        Assert.That(NativeJit.Execute(linked, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        var nativeProducer = NativeModuleCompiler.CreateProducer(providerProgram, ["Danger"],
            "UnsafeProvider", NativeRuntimePolicy.FailFast);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual71 = scopedCall.Program; Assert.That(__nunitActual71, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual71!; }))(), nativeProducer, NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.Validate(nativeConsumer, nativeProducer);

        var expressionScopedCall = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return unsafe(Danger() + 1); }"), reference);
        Assert.That(expressionScopedCall.HasErrors, Is.False, string.Join(Environment.NewLine, expressionScopedCall.Diagnostics));
        var expressionConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual72 = expressionScopedCall.Program; Assert.That(__nunitActual72, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual72!; }))(), producer);
        var expressionLinked = ModuleCompiler.Link(expressionConsumer, producer);
        SemanticVerifier.Verify(expressionLinked);
        Assert.That(NativeJit.Execute(expressionLinked, NativeRuntimePolicy.FailFast), Is.EqualTo(43));

        var nestedExpressionScope = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return unsafe(Danger()) + 1; }"), reference);
        Assert.That(nestedExpressionScope.HasErrors, Is.False, string.Join(Environment.NewLine, nestedExpressionScope.Diagnostics));
        var nestedExpressionConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual73 = nestedExpressionScope.Program; Assert.That(__nunitActual73, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual73!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(nestedExpressionConsumer, producer),
            NativeRuntimePolicy.FailFast), Is.EqualTo(43));

        var leakedExpressionScope = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { int32 value = unsafe(Danger()); return Danger(); }"), reference);
        Assert.That(leakedExpressionScope.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(leakedExpressionScope.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var expressionNativeConsumer = NativeModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual74 = expressionScopedCall.Program; Assert.That(__nunitActual74, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual74!; }))(), nativeProducer, NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.Validate(expressionNativeConsumer, nativeProducer);

        var safeProducer = ModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Danger() { return 42; }"), ["Danger"]);
        var safeReference = ModuleCompiler.CreateReference(safeProducer);
        var safeConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Danger(); }"), safeReference);
        Assert.That(safeConsumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, safeConsumerCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual75 = safeConsumerCompilation.Program; Assert.That(__nunitActual75, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual75!; }))(), safeProducer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(safeConsumer, safeProducer),
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, safeProducer),
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));

        var changedUnsafeProducer = ModuleCompiler.CreateProducer(
            CompileLibrary("public unsafe int32 Danger() { return 7; }"), ["Danger"]);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, changedUnsafeProducer),
            NativeRuntimePolicy.FailFast), Is.EqualTo(7));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void SemanticModulesRoundTripCallerUnsafeReadonlyCopyMethods(BoundRuntimePolicy policy)
    {
        const string Source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Value {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public unsafe int32 Read(int32 offset) { return Number + offset; }
                public int32 SafeRead() { unsafe { return Read(0); } }
            }
            """;
        const string ConsumerSource = """
            int32 Main() {
                Value value = new Value(42);
                int32 safeValue = value.SafeRead();
                unsafe { return value.Read(1) + safeValue; }
            }
            """;
        var providerProgram = CompileLibrary(Source, policy);
        string[] exports = ["Value.#ctor", "Value.Read", "Value.SafeRead"];
        var producer = ModuleCompiler.CreateProducer(providerProgram, exports);
        var reference = ModuleCompiler.CreateReference(producer);
        var read = global::System.Linq.Enumerable.Single(reference.Functions, static function => function.Name == "Value.Read");
        Assert.That(read.RequiresUnsafeCaller, Is.True);
        Assert.That(read.IsNoThrow, Is.False);
        Assert.That(global::System.Linq.Enumerable.Single(reference.Functions, static function => function.Name == "Value.SafeRead").RequiresUnsafeCaller, Is.False);

        var unscoped = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Value value = new Value(42); return value.Read(); }"),
            reference, policy);
        Assert.That(unscoped.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(unscoped.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), reference, policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual76 = consumerCompilation.Program; Assert.That(__nunitActual76, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual76!; }))(), producer);
        foreach (var unsupportedVersion in new[] { 69, 70, 71, 72, 73, 74, 75 })
        {
            var unsupportedConsumer = (byte[])consumer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(unsupportedConsumer.AsSpan(4, sizeof(int)),
                unsupportedVersion);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(unsupportedConsumer, producer));
        }

        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(NativeJit.Execute(linked, runtimePolicy), Is.EqualTo(85));

        var safeReplacementSource = Source
            .Replace("public unsafe int32 Read(int32 offset)", "public int32 Read(int32 offset)",
                StringComparison.Ordinal)
            .Replace("return Number + offset;", "return Number + offset + 1;", StringComparison.Ordinal)
            .Replace("unsafe { return Read(0); }", "return Read(0);", StringComparison.Ordinal);
        var safeReplacement = ModuleCompiler.CreateProducer(
            CompileLibrary(safeReplacementSource, policy), exports);
        var compatible = ModuleCompiler.Link(consumer, safeReplacement);
        Assert.That(NativeJit.Execute(compatible, runtimePolicy), Is.EqualTo(87));

        var safeProvider = ModuleCompiler.CreateProducer(
            CompileLibrary(safeReplacementSource, policy), exports);
        var safeConsumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Value value = new Value(42); return value.Read(1); }"),
            ModuleCompiler.CreateReference(safeProvider), policy);
        Assert.That(safeConsumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, safeConsumerCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual77 = safeConsumerCompilation.Program; Assert.That(__nunitActual77, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual77!; }))(), safeProvider);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void SemanticModulesRoundTripCallerUnsafeReadonlyCopyInstanceMethodExpressions(
        BoundRuntimePolicy policy)
    {
        const string Source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Pair {
                public int32 Left;
                public int32 Right;
                public Pair(int32 left, int32 right) { Left = left; Right = right; }
            }
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Value {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public unsafe int32 Compute(int32 first, int32 second) {
                    return Number * 100 + first * 10 + second;
                }
                public unsafe Pair PairWith(Pair value) {
                    return new Pair(Number + value.Left, value.Right);
                }
                public unsafe int32 Ω(int32 offset) { return Number + offset; }
                public unsafe int32 _Offset(int32 offset) { return Number + offset; }
                public int32 SafeCompute(int32 first, int32 second) {
                    unsafe { return Compute(first, second); }
                }
            }
            """;
        const string ConsumerSource = """
            int32 Main() {
                Value value = new Value(41);
                Pair input = new Pair(1, 2);
                Value pairValue = new Value(5);
                Value unicodeValue = new Value(9);
                Value underscoreValue = new Value(41);
                Value forwardingValue = new Value(4);
                int32 scalar = unsafe(value.Compute(second: 42, first: 43));
                Pair pair = unsafe(pairValue.PairWith(input));
                int32 forwarded = forwardingValue.SafeCompute(3, 2);
                int32 unicode = unsafe(unicodeValue.Ω(3));
                int32 underscore = unsafe(underscoreValue._Offset(1));
                return scalar + pair.Left * 100 + pair.Right + forwarded + unicode + underscore;
            }
            """;
        string[] exports = [
            "Pair.#ctor", "Value.#ctor", "Value.Compute", "Value.PairWith", "Value.Ω", "Value._Offset",
            "Value.SafeCompute",
        ];
        var providerProgram = CompileLibrary(Source, policy);
        var producer = ModuleCompiler.CreateProducer(providerProgram, exports);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, sizeof(int))), Is.EqualTo(78));
        var computeContract = GetModuleStringStartingWith(producer, "Value.Compute|");
        var forgedProducer = ReplaceModuleString(producer, "Value.Compute", "Value.$producer.Compute");
        forgedProducer = ReplaceModuleString(forgedProducer, computeContract,
            computeContract.Replace("Value.Compute", "Value.$producer.Compute", StringComparison.Ordinal));
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forgedProducer));

        var reference = ModuleCompiler.CreateReference(producer);
        var compute = global::System.Linq.Enumerable.Single(reference.Functions, static function => function.Name == "Value.Compute");
        Assert.That(compute.RequiresUnsafeCaller, Is.True);
        Assert.That(compute.IsStatic, Is.False);
        Assert.That(compute.Variables[0].IsMutable, Is.False);
        Assert.That(compute.ReturnType, Is.EqualTo(PrimitiveType.Int32));

        var unscoped = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Value value = new Value(1); return value.Compute(2, 3); }"),
            reference, policy);
        Assert.That(unscoped.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(unscoped.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);
        var scopedOnly = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Value value = new Value(1); int32 scoped = unsafe(value.Compute(2, 3)); return value.Compute(2, 3); }"),
            reference, policy);
        Assert.That(scopedOnly.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(scopedOnly.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), reference, policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumerProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual78 = consumerCompilation.Program; Assert.That(__nunitActual78, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual78!; }))();
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram, producer);
        foreach (var unsupportedVersion in new[] { 69, 70, 71, 72, 73, 74, 75 })
        {
            var unsupportedConsumer = (byte[])consumer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(unsupportedConsumer.AsSpan(4, sizeof(int)),
                unsupportedVersion);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(unsupportedConsumer, producer));
        }

        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(NativeJit.Execute(linked, runtimePolicy), Is.EqualTo(5660));
        Assert.That(NativeEmitter.Emit(linked, runtimePolicy), Is.Not.Empty);

        var identityCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Value value = new Value(1); return unsafe(value.Compute(second: 3, first: 2)); }"),
            reference, policy);
        Assert.That(identityCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, identityCompilation.Diagnostics));
        var identityProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual79 = identityCompilation.Program; Assert.That(__nunitActual79, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual79!; }))();
        var identityMain = global::System.Linq.Enumerable.Single(identityProgram.Functions, static function => function.Name == "Main");
        var identityReturn = ((global::System.Func<BoundReturn>)(() => { var __nunitActual80 = identityMain.Body.Statements[^1]; Assert.That(__nunitActual80, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual80!; }))();
        var identityWrapper = ((global::System.Func<BoundUnsafeExpression>)(() => { var __nunitActual81 = identityReturn.Value; Assert.That(__nunitActual81, Is.TypeOf<BoundUnsafeExpression>()); return (BoundUnsafeExpression)__nunitActual81!; }))();
        var identityCall = ((global::System.Func<BoundCall>)(() => { var __nunitActual82 = identityWrapper.Expression; Assert.That(__nunitActual82, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual82!; }))();
        Assert.That(identityCall.Arguments.Length, Is.EqualTo(3));
        var computeFunctionIndex = providerProgram.Functions.IndexOf(
            global::System.Linq.Enumerable.Single(providerProgram.Functions, static function => function.Name == "Value.Compute"));
        foreach (var malformedName in new[]
        {
            "Value.Read.More",
            "Value.$producer.Compute",
            "Value.$producer.$producer.Compute",
            "Value._",
            "Value." + '\uD800',
        })
        {
            var malformedFunctions = providerProgram.Functions.SetItem(computeFunctionIndex,
                providerProgram.Functions[computeFunctionIndex] with { Name = malformedName });
            var malformedProgram = providerProgram with { Functions = malformedFunctions };
            var malformedExports = exports.Select(export =>
                export == "Value.Compute" ? malformedName : export).ToArray();
            Assert.Throws<ModuleContractException>(() =>
                ModuleCompiler.CreateProducer(malformedProgram, malformedExports));
        }

        var strippedMain = identityMain with {
            Body = identityMain.Body with {
                Statements = identityMain.Body.Statements.SetItem(
                    identityMain.Body.Statements.Length - 1,
                    identityReturn with { Value = identityWrapper.Expression }),
            },
        };
        var strippedProgram = identityProgram with {
            Functions = identityProgram.Functions.SetItem(
                identityProgram.Functions.IndexOf(identityMain), strippedMain),
        };
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateConsumer(strippedProgram, producer));

        var identityConsumer = ModuleCompiler.CreateConsumer(identityProgram, producer);
        var strippedConsumer = StripSerializedUnsafeExpression(identityConsumer, identityWrapper);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(strippedConsumer, producer));

        var safeReplacementSource = Source
            .Replace("public unsafe int32 Compute", "public int32 Compute", StringComparison.Ordinal)
            .Replace("public unsafe Pair PairWith", "public Pair PairWith", StringComparison.Ordinal)
            .Replace("public unsafe int32 Ω", "public int32 Ω", StringComparison.Ordinal)
            .Replace("public unsafe int32 _Offset", "public int32 _Offset", StringComparison.Ordinal)
            .Replace("return Number * 100 + first * 10 + second;",
                "return Number * 100 + first * 10 + second + 1;", StringComparison.Ordinal)
            .Replace("return new Pair(Number + value.Left, value.Right);",
                "return new Pair(Number + value.Left + 1, value.Right);", StringComparison.Ordinal);
        var safeReplacement = ModuleCompiler.CreateProducer(
            CompileLibrary(safeReplacementSource, policy), exports);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, safeReplacement), runtimePolicy), Is.EqualTo(5762));

        var safeConsumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Value value = new Value(1); return value.Compute(2, 3); }"),
            ModuleCompiler.CreateReference(safeReplacement), policy);
        Assert.That(safeConsumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, safeConsumerCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual83 = safeConsumerCompilation.Program; Assert.That(__nunitActual83, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual83!; }))(), safeReplacement);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void SemanticModulesRoundTripCallerUnsafeReadonlyCopyGetters(BoundRuntimePolicy policy)
    {
        const string Source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Value {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public int32 Read { [NoThrow] unsafe get { return Number; } }
                public int32 SafeRead() { unsafe { return Read; } }
                public int32 get_Read() { return 7; }
            }
            public unsafe int32 Offset() { return 1; }
            """;
        const string ConsumerSource = """
            struct Counter {
                public mut int32 Number;
                public Counter(int32 number) { Number = number; }
                public mut Value Next() { Number = Number + 1; return new Value(Number); }
            }
            int32 Main() {
                Value value = new Value(42);
                mut Counter counter = new Counter(41);
                int32 safeValue = value.SafeRead();
                int32 expressionValue = unsafe(value.Read + Offset());
                unsafe { return counter.Next().Read + counter.Number + safeValue + expressionValue; }
            }
            """;
        string[] exports = [
            "Value.#ctor", "Value.#get:Read", "Value.SafeRead", "Value.get_Read", "Offset",
        ];
        var providerProgram = CompileLibrary(Source, policy);
        var producer = ModuleCompiler.CreateProducer(providerProgram, exports);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, sizeof(int))), Is.EqualTo(78));

        var reference = ModuleCompiler.CreateReference(producer);
        var getter = global::System.Linq.Enumerable.Single(reference.Functions, static function => function.Name == "Value.#get:Read");
        Assert.That(getter.PropertyName, Is.EqualTo("Read"));
        Assert.That(getter.RequiresUnsafeCaller, Is.True);
        Assert.That(getter.IsNoThrow, Is.True);
        Assert.That(global::System.Linq.Enumerable.Single(reference.Functions, static function => function.Name == "Value.get_Read").PropertyName, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Single(reference.Functions, static function => function.Name == "Value.SafeRead").RequiresUnsafeCaller, Is.False);

        var unscoped = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Value value = new Value(42); return value.Read; }"), reference, policy);
        Assert.That(unscoped.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(unscoped.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var scopedOnly = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Value value = new Value(42); int32 scoped = unsafe(value.Read); return value.Read; }"),
            reference, policy);
        Assert.That(scopedOnly.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(scopedOnly.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), reference, policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual84 = consumerCompilation.Program; Assert.That(__nunitActual84, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual84!; }))(), producer);

        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(NativeJit.Execute(linked, runtimePolicy), Is.EqualTo(169));
        Assert.That(NativeEmitter.Emit(linked, runtimePolicy), Is.Not.Empty);

        var getterOnlyCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Value value = new Value(42); return unsafe(value.Read); }"),
            reference, policy);
        Assert.That(getterOnlyCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, getterOnlyCompilation.Diagnostics));
        var getterOnlyProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual85 = getterOnlyCompilation.Program; Assert.That(__nunitActual85, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual85!; }))();
        var getterOnlyMain = global::System.Linq.Enumerable.Single(getterOnlyProgram.Functions, static function => function.Name == "Main");
        var getterOnlyReturn = ((global::System.Func<BoundReturn>)(() => { var __nunitActual86 = getterOnlyMain.Body.Statements[getterOnlyMain.Body.Statements.Length - 1]; Assert.That(__nunitActual86, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual86!; }))();
        var getterOnlyWrapper = ((global::System.Func<BoundUnsafeExpression>)(() => { var __nunitActual87 = getterOnlyReturn.Value; Assert.That(__nunitActual87, Is.TypeOf<BoundUnsafeExpression>()); return (BoundUnsafeExpression)__nunitActual87!; }))();
        var getterOnlyConsumer = ModuleCompiler.CreateConsumer(getterOnlyProgram, producer);
        Assert.That(NativeJit.Execute(
            ModuleCompiler.Link(getterOnlyConsumer, producer), runtimePolicy), Is.EqualTo(42));

        var returnIndex = getterOnlyMain.Body.Statements.IndexOf(getterOnlyReturn);
        var strippedGetterMain = getterOnlyMain with {
            Body = getterOnlyMain.Body with {
                Statements = getterOnlyMain.Body.Statements.SetItem(
                    returnIndex, getterOnlyReturn with { Value = getterOnlyWrapper.Expression }),
            },
        };
        var strippedGetterProgram = getterOnlyProgram with {
            Functions = getterOnlyProgram.Functions.SetItem(
                getterOnlyProgram.Functions.IndexOf(getterOnlyMain), strippedGetterMain),
        };
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateConsumer(strippedGetterProgram, producer));

        Span<byte> getterWrapperPrefix = stackalloc byte[1 + sizeof(int) + (2 * sizeof(int))];
        getterWrapperPrefix[0] = 31;
        BinaryPrimitives.WriteInt32LittleEndian(getterWrapperPrefix[1..], (int)getterOnlyWrapper.Type);
        BinaryPrimitives.WriteInt32LittleEndian(
            getterWrapperPrefix[(1 + sizeof(int))..], getterOnlyWrapper.Span.Start);
        BinaryPrimitives.WriteInt32LittleEndian(
            getterWrapperPrefix[(1 + (2 * sizeof(int)))..], getterOnlyWrapper.Span.Length);
        var getterWrapperOffset = getterOnlyConsumer.AsSpan().IndexOf(getterWrapperPrefix);
        Assert.That(getterWrapperOffset >= 0, Is.True);
        var strippedGetterConsumer = new byte[getterOnlyConsumer.Length - getterWrapperPrefix.Length];
        getterOnlyConsumer.AsSpan(0, getterWrapperOffset).CopyTo(strippedGetterConsumer);
        getterOnlyConsumer.AsSpan(getterWrapperOffset + getterWrapperPrefix.Length)
            .CopyTo(strippedGetterConsumer.AsSpan(getterWrapperOffset));
        Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.Link(strippedGetterConsumer, producer));

        var safeReplacementSource = Source
            .Replace("[NoThrow] unsafe get", "[NoThrow] get", StringComparison.Ordinal)
            .Replace("return Number;", "return 43;", StringComparison.Ordinal)
            .Replace("unsafe { return Read; }", "return Read;", StringComparison.Ordinal);
        var safeReplacement = ModuleCompiler.CreateProducer(
            CompileLibrary(safeReplacementSource, policy), exports);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, safeReplacement), runtimePolicy), Is.EqualTo(172));

        var safeConsumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Value value = new Value(42); return value.Read; }"),
            ModuleCompiler.CreateReference(safeReplacement), policy);
        Assert.That(safeConsumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, safeConsumerCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual88 = safeConsumerCompilation.Program; Assert.That(__nunitActual88, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual88!; }))(), safeReplacement);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void SemanticModulesRoundTripCallerUnsafeStaticCopyGetters(BoundRuntimePolicy policy)
    {
        const string Source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Value {
                public int32 Placeholder;
                public static int32 Read {
                    [NoThrow] unsafe get {
                        int32 local = 42;
                        return local;
                    }
                }
                public static int32 SafeRead() { unsafe { return Read; } }
            }
            """;
        const string ConsumerSource = """
            int32 Main() {
                int32 safeValue = Value.SafeRead();
                int32 expressionValue = unsafe(Value.Read);
                unsafe { return Value.Read + safeValue + expressionValue; }
            }
            """;
        string[] exports = ["Value.#get:Read", "Value.SafeRead"];
        var providerProgram = CompileLibrary(Source, policy);
        var producer = ModuleCompiler.CreateProducer(providerProgram, exports);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, sizeof(int))), Is.EqualTo(78));

        var reference = ModuleCompiler.CreateReference(producer);
        var getter = global::System.Linq.Enumerable.Single(reference.Functions, static function => function.Name == "Value.#get:Read");
        Assert.That(getter.PropertyName, Is.EqualTo("Read"));
        Assert.That(getter.IsStatic, Is.True);
        Assert.That(getter.ParameterCount, Is.EqualTo(0));
        Assert.That(getter.RequiresUnsafeCaller, Is.True);
        Assert.That(getter.IsNoThrow, Is.True);

        var unscoped = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Value.Read; }"),
            reference, policy);
        Assert.That(unscoped.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(unscoped.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), reference, policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual89 = consumerCompilation.Program; Assert.That(__nunitActual89, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual89!; }))(), producer);
        foreach (var unsupportedVersion in new[] { 69, 70, 71, 72, 73, 74, 75 })
        {
            var unsupportedConsumer = (byte[])consumer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(unsupportedConsumer.AsSpan(4, sizeof(int)),
                unsupportedVersion);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(unsupportedConsumer, producer));
        }

        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(NativeJit.Execute(linked, runtimePolicy), Is.EqualTo(126));
        Assert.That(NativeEmitter.Emit(linked, runtimePolicy), Is.Not.Empty);

        var safeReplacementSource = Source
            .Replace("[NoThrow] unsafe get", "[NoThrow] get", StringComparison.Ordinal)
            .Replace("local = 42;", "local = 43;", StringComparison.Ordinal)
            .Replace("unsafe { return Read; }", "return Read;", StringComparison.Ordinal);
        var safeReplacement = ModuleCompiler.CreateProducer(
            CompileLibrary(safeReplacementSource, policy), exports);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, safeReplacement), runtimePolicy), Is.EqualTo(129));

        var safeConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Value.Read; }"),
            ModuleCompiler.CreateReference(safeReplacement), policy);
        Assert.That(safeConsumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, safeConsumerCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual90 = safeConsumerCompilation.Program; Assert.That(__nunitActual90, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual90!; }))(), safeReplacement);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void SemanticModulesRoundTripCallerUnsafeStaticMethodExpressions(BoundRuntimePolicy policy)
    {
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct ScalarOps {
                public int32 Marker;
                public ScalarOps(int32 marker) { Marker = marker; }
                public static unsafe int32 Add(int32 left, int32 right) { return left + right; }
                public static unsafe int32 Zero() { return 0; }
                public static unsafe float64 Echo(float64 value) { return value; }
                public static int32 Read {
                    [NoThrow] unsafe get { return 0; }
                }
                public unsafe int32 ReadInstance(int32 value) { return value; }
            }
            public unsafe int32 Top(int32 value) { return value; }
            public int32 Forward(int32 left, int32 right) {
                return unsafe(ScalarOps.Add(left, right))
                    + unsafe(ScalarOps.Zero());
            }
            public ScalarOps MakeScalarOps() { return new ScalarOps(0); }
            public float64 EchoForward(float64 value) { return unsafe(ScalarOps.Echo(value)); }
            """;
        string[] exports = [
            "ScalarOps.Add", "ScalarOps.Zero", "ScalarOps.Echo", "ScalarOps.#get:Read",
            "ScalarOps.ReadInstance", "Top", "Forward", "MakeScalarOps", "EchoForward",
        ];
        var producer = ModuleCompiler.CreateProducer(CompileLibrary(source, policy), exports);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, sizeof(int))), Is.EqualTo(78));

        var reference = ModuleCompiler.CreateReference(producer);
        foreach (var name in exports[..3])
        {
            var method = global::System.Linq.Enumerable.Single(reference.Functions, function => function.Name == name);
            Assert.That(method.IsStatic, Is.True);
            Assert.That(method.RequiresUnsafeCaller, Is.True);
            Assert.That(method.PropertyName, Is.Null);
            Assert.That(method.IsOperator, Is.False);
        }

        const string consumerSource = """
            int32 Main() {
                require unsafe(ScalarOps.Echo(0.0)) == float64(0);
                return unsafe(Top(0) + ScalarOps.Add(right: 2, left: 40));
            }
            """;
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(consumerSource), reference, policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumerProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual91 = consumerCompilation.Program; Assert.That(__nunitActual91, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual91!; }))();
        var main = global::System.Linq.Enumerable.Single(consumerProgram.Functions, static function => function.Name == "Main");
        var returned = ((global::System.Func<BoundReturn>)(() => { var __nunitActual92 = main.Body.Statements[1]; Assert.That(__nunitActual92, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual92!; }))();
        var unsafeExpression = ((global::System.Func<BoundUnsafeExpression>)(() => { var __nunitActual93 = returned.Value; Assert.That(__nunitActual93, Is.TypeOf<BoundUnsafeExpression>()); return (BoundUnsafeExpression)__nunitActual93!; }))();
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram, producer);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(consumer.AsSpan(4, sizeof(int))), Is.EqualTo(78));
        foreach (var unsupportedVersion in new[] { 69, 70, 71, 72, 73, 74, 75 })
        {
            var unsupportedConsumer = (byte[])consumer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(unsupportedConsumer.AsSpan(4, sizeof(int)),
                unsupportedVersion);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(unsupportedConsumer, producer));
        }

        var strippedFunction = main with {
            Body = main.Body with {
                Statements = main.Body.Statements.SetItem(1, returned with { Value = unsafeExpression.Expression }),
            },
        };
        var strippedProgram = consumerProgram with {
            Functions = consumerProgram.Functions.SetItem(
                consumerProgram.Functions.IndexOf(main), strippedFunction),
        };
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateConsumer(strippedProgram, producer));

        Span<byte> wrapperPrefix = stackalloc byte[1 + sizeof(int) + (2 * sizeof(int))];
        wrapperPrefix[0] = 31;
        BinaryPrimitives.WriteInt32LittleEndian(wrapperPrefix[1..], (int)unsafeExpression.Type);
        BinaryPrimitives.WriteInt32LittleEndian(wrapperPrefix[(1 + sizeof(int))..],
            unsafeExpression.Span.Start);
        BinaryPrimitives.WriteInt32LittleEndian(wrapperPrefix[(1 + (2 * sizeof(int)))..],
            unsafeExpression.Span.Length);
        var wrapperOffset = consumer.AsSpan().IndexOf(wrapperPrefix);
        Assert.That(wrapperOffset >= 0, Is.True);
        var strippedConsumer = new byte[consumer.Length - wrapperPrefix.Length];
        consumer.AsSpan(0, wrapperOffset).CopyTo(strippedConsumer);
        consumer.AsSpan(wrapperOffset + wrapperPrefix.Length)
            .CopyTo(strippedConsumer.AsSpan(wrapperOffset));
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(strippedConsumer, producer));

        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), runtimePolicy), Is.EqualTo(42));
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(
            ModuleCompiler.CreateConsumer(
                ((global::System.Func<BoundProgram>)(() => {
                    var __nunitActual94 = Compilation.Create(SyntaxTree.Parse(
                    "int32 Main() { return Forward(40, 2); }"), reference, policy).Program;
                    Assert.That(__nunitActual94, Is.TypeOf<BoundProgram>());
                    return (BoundProgram)__nunitActual94!;
                }))(), producer),
            producer), runtimePolicy), Is.EqualTo(42));

        var safeSource = source
            .Replace("static unsafe int32 Add", "static int32 Add", StringComparison.Ordinal)
            .Replace("static unsafe int32 Zero", "static int32 Zero", StringComparison.Ordinal)
            .Replace("static unsafe float64 Echo", "static float64 Echo", StringComparison.Ordinal)
            .Replace("[NoThrow] unsafe get", "[NoThrow] get", StringComparison.Ordinal)
            .Replace("public unsafe int32 ReadInstance", "public int32 ReadInstance", StringComparison.Ordinal)
            .Replace("public unsafe int32 Top", "public int32 Top", StringComparison.Ordinal);
        var safeProducer = ModuleCompiler.CreateProducer(CompileLibrary(safeSource, policy), exports);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, safeProducer), runtimePolicy), Is.EqualTo(42));
        var safeConsumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return ScalarOps.Add(40, 2); }"),
            ModuleCompiler.CreateReference(safeProducer), policy);
        Assert.That(safeConsumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, safeConsumerCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual95 = safeConsumerCompilation.Program; Assert.That(__nunitActual95, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual95!; }))(), safeProducer);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void SemanticModulesRoundTripCallerUnsafeCopyValueExpressions(BoundRuntimePolicy policy)
    {
        string[] exports = [
            "MakePair", "MakeEnvelope", "MakeFloatBox", "Value.Transform", "Value.Wrap", "ExpressionOps.CreatePair",
            "ExpressionOps.Sum", "ExpressionOps.ZeroPair", "ExpressionOps.EchoFloat", "Identity",
        ];
        var producer = ModuleCompiler.CreateProducer(
            CompileLibrary(CopyValueExpressionProviderSource, policy), exports);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, sizeof(int))), Is.EqualTo(78));
        var reference = ModuleCompiler.CreateReference(producer);
        var compilation = Compilation.Create(
            SyntaxTree.Parse(CopyValueExpressionConsumerSource), reference, policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumerProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual96 = compilation.Program; Assert.That(__nunitActual96, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual96!; }))();
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram, producer);
        foreach (var unsupportedVersion in new[] { 69, 70, 71, 72, 73, 74, 75 })
        {
            var unsupportedConsumer = (byte[])consumer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(unsupportedConsumer.AsSpan(4, sizeof(int)),
                unsupportedVersion);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(unsupportedConsumer, producer));
        }

        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), runtimePolicy), Is.EqualTo(12436));

        var safeSource = CopyValueExpressionProviderSource.Replace("unsafe ", "", StringComparison.Ordinal);
        var safeReplacement = ModuleCompiler.CreateProducer(CompileLibrary(safeSource, policy), exports);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, safeReplacement), runtimePolicy), Is.EqualTo(12436));
        var safeCompilation = Compilation.Create(
            SyntaxTree.Parse(CopyValueExpressionConsumerSource),
            ModuleCompiler.CreateReference(safeReplacement), policy);
        Assert.That(safeCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, safeCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual97 = safeCompilation.Program; Assert.That(__nunitActual97, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual97!; }))(), safeReplacement);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NativeModuleSetsAndGraphsRoundTripCallerUnsafeStaticMethodExpressionsAsync(
        BoundRuntimePolicy policy)
    {
        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct ScalarOps {
                public int32 Marker;
                public ScalarOps(int32 marker) { Marker = marker; }
                public static unsafe int32 Add(int32 left, int32 right) { return left + right; }
            }
            """;
        var provider = NativeModuleCompiler.CreateProducer(
            CompileLibrary(source, policy), ["ScalarOps.Add"], "UnsafeStaticProvider", runtimePolicy);
        var helper = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }", policy),
            ["Helper"], "UnsafeStaticHelper", runtimePolicy);
        byte[][] providers = [provider, helper];

        var setCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return unsafe(ScalarOps.Add(40, 2)); }"),
            NativeModuleCompiler.CreateReferenceSet(providers), policy);
        Assert.That(setCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual98 = setCompilation.Program; Assert.That(__nunitActual98, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual98!; }))(), providers, runtimePolicy);
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, providers);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(42));

        var middleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Forward() { return unsafe(ScalarOps.Add(40, 2)); }
            """), NativeModuleCompiler.CreateGraphReference([provider]), policy);
        Assert.That(middleCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, middleCompilation.Diagnostics));
        var middle = NativeModuleCompiler.CreateGraphProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual99 = middleCompilation.Program; Assert.That(__nunitActual99, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual99!; }))(), ["Forward"],
            "UnsafeStaticMiddle", [provider], runtimePolicy);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Forward(); }"),
            NativeModuleCompiler.CreateGraphReference([middle]), policy);
        Assert.That(rootCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual100 = rootCompilation.Program; Assert.That(__nunitActual100, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual100!; }))(), [middle], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(root, [middle, provider]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [provider, middle],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NativeModuleSetsAndGraphsRoundTripCallerUnsafeCopyValueExpressionsAsync(
        BoundRuntimePolicy policy)
    {
        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        string[] exports = [
            "MakePair", "MakeEnvelope", "MakeFloatBox", "Value.Transform", "Value.Wrap", "ExpressionOps.CreatePair",
            "ExpressionOps.Sum", "ExpressionOps.ZeroPair", "ExpressionOps.EchoFloat", "Identity",
        ];
        var provider = NativeModuleCompiler.CreateProducer(
            CompileLibrary(CopyValueExpressionProviderSource, policy), exports,
            "CopyValueExpressionProvider", runtimePolicy);
        var graphCompilation = Compilation.Create(
            SyntaxTree.Parse(CopyValueExpressionConsumerSource),
            NativeModuleCompiler.CreateGraphReference([provider]), policy);
        Assert.That(graphCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual101 = graphCompilation.Program; Assert.That(__nunitActual101, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual101!; }))(), [provider], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [provider]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(graphConsumer, [provider],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(12436));

        var helper = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }", policy),
            ["Helper"], "CopyValueExpressionHelper", runtimePolicy);
        var providers = new[] { provider, helper };
        var setCompilation = Compilation.Create(
            SyntaxTree.Parse(CopyValueExpressionConsumerSource),
            NativeModuleCompiler.CreateReferenceSet(providers), policy);
        Assert.That(setCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual102 = setCompilation.Program; Assert.That(__nunitActual102, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual102!; }))(), providers, runtimePolicy);
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, providers);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(12436));

        var safeSource = CopyValueExpressionProviderSource.Replace("unsafe ", "", StringComparison.Ordinal);
        var safeReplacement = NativeModuleCompiler.CreateProducer(
            CompileLibrary(safeSource, policy), exports, "CopyValueExpressionProvider", runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [safeReplacement]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(graphConsumer, [safeReplacement],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(12436));
        var replacementProviders = new[] { safeReplacement, helper };
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, replacementProviders);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, replacementProviders,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(12436));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void SemanticModulesRoundTripCallerUnsafeAggregateGetters(BoundRuntimePolicy policy)
    {
        const string ConsumerSource = """
            int32 Main() {
                GetterOwner owner = MakeGetterOwner();
                Pair pair = unsafe(owner.InstancePair);
                Odd odd = unsafe(owner.InstanceOdd);
                Triple triple = unsafe(GetterOwner.StaticTriple);
                Nested nested = unsafe(GetterOwner.StaticNested);
                require odd.Third && nested.Tail.Third;
                return pair.First + pair.Second + int32(odd.First) + int32(odd.Second)
                    + triple.First + triple.Second + triple.Third
                    + nested.Payload.First + nested.Payload.Second
                    + int32(nested.Tail.First) + int32(nested.Tail.Second);
            }
            """;
        string[] exports = [
            "MakeGetterOwner",
            "GetterOwner.#get:InstancePair",
            "GetterOwner.#get:InstanceOdd",
            "GetterOwner.#get:StaticTriple",
            "GetterOwner.#get:StaticNested",
        ];
        var producer = ModuleCompiler.CreateProducer(CompileLibrary(CopyValueGetterSource, policy), exports);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, sizeof(int))), Is.EqualTo(78));
        var reference = ModuleCompiler.CreateReference(producer);

        foreach (var (name, isStatic, parameterCount) in new[]
        {
            ("GetterOwner.#get:InstancePair", false, 1),
            ("GetterOwner.#get:InstanceOdd", false, 1),
            ("GetterOwner.#get:StaticTriple", true, 0),
            ("GetterOwner.#get:StaticNested", true, 0),
        })
        {
            var getter = global::System.Linq.Enumerable.Single(reference.Functions, function => function.Name == name);
            Assert.That(getter.PropertyName, Is.EqualTo(name[(name.IndexOf("#get:", StringComparison.Ordinal) + 5)..]));
            Assert.That(getter.IsStatic, Is.EqualTo(isStatic));
            Assert.That(getter.ParameterCount, Is.EqualTo(parameterCount));
            Assert.That(getter.RequiresUnsafeCaller, Is.True, name);
            Assert.That(getter.IsNoThrow, Is.False);
        }

        foreach (var source in new[]
        {
            "int32 Main() { return GetterOwner.StaticTriple.First; }",
            "int32 Main() { GetterOwner owner = MakeGetterOwner(); return owner.InstanceOdd.First; }",
        })
        {
            var unscoped = Compilation.Create(SyntaxTree.Parse(source), reference, policy);
            Assert.That(unscoped.Program, Is.Null);
            Assert.That(global::System.Linq.Enumerable.Any(unscoped.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);
        }

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), reference, policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual103 = consumerCompilation.Program; Assert.That(__nunitActual103, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual103!; }))(), producer);
        foreach (var unsupportedVersion in new[] { 69, 70, 71, 72, 73, 74, 75 })
        {
            var unsupportedConsumer = (byte[])consumer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(unsupportedConsumer.AsSpan(4, sizeof(int)),
                unsupportedVersion);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(unsupportedConsumer, producer));
        }

        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), runtimePolicy), Is.EqualTo(142));

        var safeReplacementSource = CopyValueGetterSource
            .Replace("unsafe get", "get", StringComparison.Ordinal)
            .Replace("new Pair(20, 21)", "new Pair(22, 21)", StringComparison.Ordinal);
        var safeReplacement = ModuleCompiler.CreateProducer(
            CompileLibrary(safeReplacementSource, policy), exports);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, safeReplacement), runtimePolicy), Is.EqualTo(144));

        var safeConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return GetterOwner.StaticNested.Payload.First; }"),
            ModuleCompiler.CreateReference(safeReplacement), policy);
        Assert.That(safeConsumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, safeConsumerCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual104 = safeConsumerCompilation.Program; Assert.That(__nunitActual104, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual104!; }))(), safeReplacement);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));
    }

    [Test]
    public void CallerUnsafeGetterPersistenceRejectsNonExportableCopyResultLayouts()
    {
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Hidden {
                int32 Secret;
                public Hidden(int32 secret) { Secret = secret; }
            }
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Value {
                public int32 Placeholder;
                public Hidden Read { unsafe get { return new Hidden(42); } }
            }
            """;
        var program = CompileLibrary(source);

        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(program, ["Value.#get:Read"]))!;
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void SemanticModulesRoundTripCallerUnsafeStaticCopyMethods(BoundRuntimePolicy policy)
    {
        const string Source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Value {
                public int32 Placeholder;
                public static unsafe int32 Read() { return 42; }
                public static unsafe int32 Add(int32 value) { return value + 1; }
                public static int32 SafeAdd(int32 value) { unsafe { return Add(value); } }
            }
            """;
        const string ConsumerSource = """
            int32 Main() {
                int32 safeValue = Value.SafeAdd(40);
                unsafe { return Value.Read() + Value.Add(1) + safeValue; }
            }
            """;
        var providerProgram = CompileLibrary(Source, policy);
        string[] exports = ["Value.Read", "Value.Add", "Value.SafeAdd"];
        var producer = ModuleCompiler.CreateProducer(providerProgram, exports);
        var reference = ModuleCompiler.CreateReference(producer);
        var read = global::System.Linq.Enumerable.Single(reference.Functions, static function => function.Name == "Value.Read");
        var add = global::System.Linq.Enumerable.Single(reference.Functions, static function => function.Name == "Value.Add");
        Assert.That(read.IsStatic, Is.True);
        Assert.That(read.ParameterCount, Is.EqualTo(0));
        Assert.That(read.RequiresUnsafeCaller, Is.True);
        Assert.That(add.IsStatic, Is.True);
        Assert.That(add.ParameterCount, Is.EqualTo(1));
        Assert.That(add.RequiresUnsafeCaller, Is.True);
        Assert.That(global::System.Linq.Enumerable.Single(reference.Functions, static function => function.Name == "Value.SafeAdd").RequiresUnsafeCaller, Is.False);

        var unscoped = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Value.Read(); }"), reference, policy);
        Assert.That(unscoped.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(unscoped.Diagnostics,static diagnostic => diagnostic.Code == "TRM3028"),Is.True);

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), reference, policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual105 = consumerCompilation.Program; Assert.That(__nunitActual105, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual105!; }))(), producer);
        foreach (var unsupportedVersion in new[] { 69, 70, 71, 72, 73, 74, 75 })
        {
            var unsupportedConsumer = (byte[])consumer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(unsupportedConsumer.AsSpan(4, sizeof(int)),
                unsupportedVersion);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(unsupportedConsumer, producer));
        }

        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(NativeJit.Execute(linked, runtimePolicy), Is.EqualTo(85));

        var safeReplacementSource = Source
            .Replace("public static unsafe int32 Read()", "public static int32 Read()",
                StringComparison.Ordinal)
            .Replace("public static unsafe int32 Add(int32 value)", "public static int32 Add(int32 value)",
                StringComparison.Ordinal)
            .Replace("return 42;", "return 43;", StringComparison.Ordinal)
            .Replace("return value + 1;", "return value + 2;", StringComparison.Ordinal)
            .Replace("unsafe { return Add(value); }", "return Add(value);", StringComparison.Ordinal);
        var safeReplacement = ModuleCompiler.CreateProducer(
            CompileLibrary(safeReplacementSource, policy), exports);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, safeReplacement), runtimePolicy), Is.EqualTo(88));

        var safeConsumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Value.Read() + Value.Add(1); }"),
            ModuleCompiler.CreateReference(safeReplacement), policy);
        Assert.That(safeConsumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, safeConsumerCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual106 = safeConsumerCompilation.Program; Assert.That(__nunitActual106, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual106!; }))(), safeReplacement);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void SemanticModulesRoundTripCallerUnsafeCopyValueMethods(BoundRuntimePolicy policy)
    {
        string[] exports = ["MakePair", "MakeValue", "MakeEnvelope", "Value.Echo",
            "Value.Transform", "Value.Wrap", "Value.SafeEcho"];
        var producer = ModuleCompiler.CreateProducer(CompileLibrary(CopyValueMethodSource, policy), exports);
        var reference = ModuleCompiler.CreateReference(producer);
        var echo = global::System.Linq.Enumerable.Single(reference.Functions, static function => function.Name == "Value.Echo");
        Assert.That(echo.IsStatic, Is.False);
        Assert.That(echo.ParameterCount, Is.EqualTo(2));
        Assert.That(echo.Variables[1].Type, Is.EqualTo(echo.ReturnType));
        Assert.That(echo.RequiresUnsafeCaller, Is.True);
        var transform = global::System.Linq.Enumerable.Single(reference.Functions, static function => function.Name == "Value.Transform");
        Assert.That(transform.IsStatic, Is.True);
        Assert.That(transform.ParameterCount, Is.EqualTo(7));
        Assert.That((int)transform.ReturnType >= 100, Is.True);
        Assert.That((int)transform.Variables[0].Type >= 100, Is.True);
        Assert.That(transform.RequiresUnsafeCaller, Is.True);
        Assert.That(global::System.Linq.Enumerable.Single(reference.Functions, static function => function.Name == "Value.SafeEcho").RequiresUnsafeCaller, Is.False);

        var compilation = Compilation.Create(SyntaxTree.Parse(CopyValueConsumerSource), reference, policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual107 = compilation.Program; Assert.That(__nunitActual107, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual107!; }))(), producer);
        foreach (var unsupportedVersion in new[] { 69, 70, 71, 72, 73, 74, 75 })
        {
            var unsupportedConsumer = (byte[])consumer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(unsupportedConsumer.AsSpan(4, sizeof(int)),
                unsupportedVersion);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(unsupportedConsumer, producer));
        }

        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), runtimePolicy), Is.EqualTo(23608));

        var safeReplacement = ModuleCompiler.CreateProducer(
            CompileLibrary(SafeCopyValueMethodSource, policy), exports);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, safeReplacement), runtimePolicy), Is.EqualTo(23611));

        var safeConsumerCompilation = Compilation.Create(SyntaxTree.Parse(CopyValueConsumerSource),
            ModuleCompiler.CreateReference(safeReplacement), policy);
        Assert.That(safeConsumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, safeConsumerCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual108 = safeConsumerCompilation.Program; Assert.That(__nunitActual108, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual108!; }))(), safeReplacement);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NativeModuleSetsAndGraphsRoundTripCallerUnsafeReadonlyCopyMethodsAsync(
        BoundRuntimePolicy policy)
    {
        const string Source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Value {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public unsafe int32 Read(int32 offset) { return Number + offset; }
            }
            """;
        const string ConsumerSource = """
            int32 Main() {
                Value value = new Value(42);
                return unsafe(value.Read(0));
            }
            """;
        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        var producer = NativeModuleCompiler.CreateProducer(
            CompileLibrary(Source, policy), ["Value.#ctor", "Value.Read"], "UnsafeValue", runtimePolicy);
        var helper = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }", policy),
            ["Helper"], "UnsafeMethodHelper", runtimePolicy);
        var providers = new[] { producer, helper };

        var graphReference = NativeModuleCompiler.CreateGraphReference([producer]);
        var graphCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), graphReference, policy);
        Assert.That(graphCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual109 = graphCompilation.Program; Assert.That(__nunitActual109, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual109!; }))(), [producer], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [producer]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(graphConsumer, [producer],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(42));

        var safeReplacementSource = Source
            .Replace("public unsafe int32 Read(int32 offset)", "public int32 Read(int32 offset)",
                StringComparison.Ordinal)
            .Replace("return Number + offset;", "return Number + offset + 1;", StringComparison.Ordinal);
        var safeReplacement = NativeModuleCompiler.CreateProducer(
            CompileLibrary(safeReplacementSource, policy), ["Value.#ctor", "Value.Read"],
            "UnsafeValue", runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [safeReplacement]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(graphConsumer, [safeReplacement],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(43));

        var setReference = NativeModuleCompiler.CreateReferenceSet(providers);
        var setCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), setReference, policy);
        Assert.That(setCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual110 = setCompilation.Program; Assert.That(__nunitActual110, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual110!; }))(), providers, runtimePolicy);
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, providers);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(42));
        var replacementProviders = new[] { safeReplacement, helper };
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, replacementProviders);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, replacementProviders,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(43));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NativeModuleSetsAndGraphsRoundTripCallerUnsafeReadonlyCopyGettersAsync(
        BoundRuntimePolicy policy)
    {
        const string Source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Value {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public int32 Read { unsafe get { return Number; } }
                public int32 SafeRead() { unsafe { return Read; } }
            }
            """;
        const string ConsumerSource = """
            struct Counter {
                public mut int32 Number;
                public Counter(int32 number) { Number = number; }
                public mut Value Next() { Number = Number + 1; return new Value(Number); }
            }
            int32 Main() {
                Value value = new Value(42);
                mut Counter counter = new Counter(41);
                int32 safeValue = value.SafeRead();
                int32 expressionValue = unsafe(value.Read);
                unsafe { return counter.Next().Read + counter.Number + safeValue + expressionValue; }
            }
            """;
        string[] exports = ["Value.#ctor", "Value.#get:Read", "Value.SafeRead"];
        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        var producer = NativeModuleCompiler.CreateProducer(
            CompileLibrary(Source, policy), exports, "UnsafeGetterValue", runtimePolicy);
        var helper = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }", policy),
            ["Helper"], "UnsafeGetterHelper", runtimePolicy);
        var providers = new[] { producer, helper };

        var graphReference = NativeModuleCompiler.CreateGraphReference([producer]);
        var graphCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), graphReference, policy);
        Assert.That(graphCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual111 = graphCompilation.Program; Assert.That(__nunitActual111, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual111!; }))(), [producer], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [producer]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(graphConsumer, [producer],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(168));

        var safeReplacementSource = Source.Replace("unsafe get", "get", StringComparison.Ordinal)
            .Replace("unsafe { return Read; }", "return Read;", StringComparison.Ordinal)
            .Replace("return Number;", "return Number + 1;", StringComparison.Ordinal);
        var safeReplacement = NativeModuleCompiler.CreateProducer(
            CompileLibrary(safeReplacementSource, policy), exports, "UnsafeGetterValue", runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [safeReplacement]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(graphConsumer, [safeReplacement],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(171));

        var setReference = NativeModuleCompiler.CreateReferenceSet(providers);
        var setCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), setReference, policy);
        Assert.That(setCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual112 = setCompilation.Program; Assert.That(__nunitActual112, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual112!; }))(), providers, runtimePolicy);
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, providers);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(168));

        var replacementProviders = new[] { safeReplacement, helper };
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, replacementProviders);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, replacementProviders,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(171));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NativeModuleSetsAndGraphsRoundTripCallerUnsafeStaticCopyGettersAsync(
        BoundRuntimePolicy policy)
    {
        const string Source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Value {
                public int32 Placeholder;
                public static int32 Read { [NoThrow] unsafe get { return 42; } }
                public static int32 SafeRead() { unsafe { return Read; } }
            }
            """;
        const string ConsumerSource = """
            int32 Main() {
                int32 safeValue = Value.SafeRead();
                int32 expressionValue = unsafe(Value.Read);
                unsafe { return Value.Read + safeValue + expressionValue; }
            }
            """;
        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        var exports = new[] { "Value.#get:Read", "Value.SafeRead" };
        var producer = NativeModuleCompiler.CreateProducer(
            CompileLibrary(Source, policy), exports, "UnsafeStaticGetterValue", runtimePolicy);
        var helper = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }", policy),
            ["Helper"], "UnsafeStaticGetterHelper", runtimePolicy);
        var providers = new[] { producer, helper };

        var graphReference = NativeModuleCompiler.CreateGraphReference([producer]);
        var graphCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), graphReference, policy);
        Assert.That(graphCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual113 = graphCompilation.Program; Assert.That(__nunitActual113, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual113!; }))(), [producer], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [producer]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(graphConsumer, [producer],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(126));

        var safeReplacementSource = Source.Replace("unsafe get", "get", StringComparison.Ordinal)
            .Replace("unsafe { return Read; }", "return Read;", StringComparison.Ordinal)
            .Replace("return 42;", "return 43;", StringComparison.Ordinal);
        var safeReplacement = NativeModuleCompiler.CreateProducer(
            CompileLibrary(safeReplacementSource, policy), exports, "UnsafeStaticGetterValue", runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [safeReplacement]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(graphConsumer, [safeReplacement],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(129));

        var setReference = NativeModuleCompiler.CreateReferenceSet(providers);
        var setCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), setReference, policy);
        Assert.That(setCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual114 = setCompilation.Program; Assert.That(__nunitActual114, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual114!; }))(), providers, runtimePolicy);
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, providers);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(126));

        var replacementProviders = new[] { safeReplacement, helper };
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, replacementProviders);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, replacementProviders,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(129));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NativeModuleSetsAndGraphsForwardCallerUnsafeAggregateGettersAsync(
        BoundRuntimePolicy policy)
    {
        const string SetConsumerSource = """
            int32 Main() {
                GetterOwner owner = MakeGetterOwner();
                Pair pair = unsafe(owner.InstancePair);
                Odd odd = unsafe(owner.InstanceOdd);
                Triple triple = unsafe(GetterOwner.StaticTriple);
                Nested nested = unsafe(GetterOwner.StaticNested);
                unsafe {
                    require odd.Third && nested.Tail.Third;
                    return pair.First + pair.Second + int32(odd.First) + int32(odd.Second)
                        + triple.First + triple.Second + triple.Third
                        + nested.Payload.First + nested.Payload.Second
                        + int32(nested.Tail.First) + int32(nested.Tail.Second) + Helper();
                }
            }
            """;
        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        var provider = NativeModuleCompiler.CreateProducer(
            CompileLibrary(CopyValueGetterSource, policy),
            [
                "MakeGetterOwner",
                "GetterOwner.#get:InstancePair",
                "GetterOwner.#get:InstanceOdd",
                "GetterOwner.#get:StaticTriple",
                "GetterOwner.#get:StaticNested",
            ],
            "CopyGetterProvider", runtimePolicy);

        var middleCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(CopyValueGetterMiddleSource),
            NativeModuleCompiler.CreateGraphReference([provider]), policy);
        Assert.That(middleCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, middleCompilation.Diagnostics));
        var middle = NativeModuleCompiler.CreateGraphProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual115 = middleCompilation.Program; Assert.That(__nunitActual115, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual115!; }))(),
            ["ForwardNested", "ForwardOdd"], "CopyGetterMiddle", [provider], runtimePolicy);
        var providers = new[] { provider, middle };

        var graphCompilation = Compilation.Create(SyntaxTree.Parse(CopyValueGetterConsumerSource),
            NativeModuleCompiler.CreateGraphReference(providers), policy);
        Assert.That(graphCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual116 = graphCompilation.Program; Assert.That(__nunitActual116, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual116!; }))(), providers, runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, providers);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(graphConsumer, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(209));

        var helper = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }", policy),
            ["Helper"], "CopyGetterSetHelper", runtimePolicy);
        var setProviders = new[] { provider, helper };
        var setReference = NativeModuleCompiler.CreateReferenceSet(setProviders);
        var setCompilation = Compilation.Create(SyntaxTree.Parse(SetConsumerSource),
            setReference, policy);
        Assert.That(setCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual117 = setCompilation.Program; Assert.That(__nunitActual117, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual117!; }))(), setProviders, runtimePolicy);
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, setProviders);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, setProviders,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(142));

        var safeReplacementSource = CopyValueGetterSource
            .Replace("unsafe get", "get", StringComparison.Ordinal)
            .Replace("new Pair(20, 21)", "new Pair(22, 21)", StringComparison.Ordinal);
        var safeReplacement = NativeModuleCompiler.CreateProducer(
            CompileLibrary(safeReplacementSource, policy),
            [
                "MakeGetterOwner",
                "GetterOwner.#get:InstancePair",
                "GetterOwner.#get:InstanceOdd",
                "GetterOwner.#get:StaticTriple",
                "GetterOwner.#get:StaticNested",
            ],
            "CopyGetterProvider", runtimePolicy);
        var directGraphCompilation = Compilation.Create(SyntaxTree.Parse(SetConsumerSource),
            NativeModuleCompiler.CreateGraphReference(setProviders), policy);
        Assert.That(directGraphCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, directGraphCompilation.Diagnostics));
        var directGraphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual118 = directGraphCompilation.Program; Assert.That(__nunitActual118, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual118!; }))(), setProviders, runtimePolicy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(directGraphConsumer, setProviders,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(142));
        NativeModuleCompiler.ValidateGraph(directGraphConsumer, [safeReplacement, helper]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(directGraphConsumer,
            [safeReplacement, helper], TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(144));

        var replacementGraphProviders = new[] { safeReplacement, middle };
        NativeModuleCompiler.ValidateGraph(graphConsumer, replacementGraphProviders);
        var nestedForwardCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    Nested value = ForwardNested();
                    return value.Payload.First;
                }
            }
            """), NativeModuleCompiler.CreateGraphReference(providers), policy);
        Assert.That(nestedForwardCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, nestedForwardCompilation.Diagnostics));
        var nestedForwardConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual119 = nestedForwardCompilation.Program; Assert.That(__nunitActual119, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual119!; }))(), providers, runtimePolicy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nestedForwardConsumer, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(20));
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nestedForwardConsumer,
            replacementGraphProviders, TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(22));

        var oddForwardCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                GetterOwner owner = MakeGetterOwner();
                unsafe {
                    Odd value = ForwardOdd(owner);
                    return int32(value.First);
                }
            }
            """), NativeModuleCompiler.CreateGraphReference(providers), policy);
        Assert.That(oddForwardCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, oddForwardCompilation.Diagnostics));
        var oddForwardConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual120 = oddForwardCompilation.Program; Assert.That(__nunitActual120, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual120!; }))(), providers, runtimePolicy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(oddForwardConsumer, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(5));
        Assert.That(await NativeModuleCompiler.RunGraphAsync(oddForwardConsumer,
            replacementGraphProviders, TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(5));

        Assert.That(await NativeModuleCompiler.RunGraphAsync(graphConsumer, replacementGraphProviders,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(211));
        var replacementSetProviders = new[] { safeReplacement, helper };
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, replacementSetProviders);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, replacementSetProviders,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(144));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NativeModuleSetsAndGraphsRoundTripCallerUnsafeStaticCopyMethodsAsync(
        BoundRuntimePolicy policy)
    {
        const string Source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Value {
                public int32 Placeholder;
                public static unsafe int32 Read() { return 42; }
            }
            """;
        const string ConsumerSource = """
            int32 Main() {
                unsafe { return Value.Read(); }
            }
            """;
        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        var producer = NativeModuleCompiler.CreateProducer(
            CompileLibrary(Source, policy), ["Value.Read"], "UnsafeStaticValue", runtimePolicy);
        var helper = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }", policy),
            ["Helper"], "UnsafeStaticMethodHelper", runtimePolicy);
        var providers = new[] { producer, helper };

        var graphReference = NativeModuleCompiler.CreateGraphReference([producer]);
        var graphCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), graphReference, policy);
        Assert.That(graphCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual121 = graphCompilation.Program; Assert.That(__nunitActual121, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual121!; }))(), [producer], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [producer]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(graphConsumer, [producer],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(42));

        var safeReplacement = NativeModuleCompiler.CreateProducer(
            CompileLibrary(Source.Replace("public static unsafe int32 Read()",
                "public static int32 Read()", StringComparison.Ordinal)
                .Replace("return 42;", "return 43;", StringComparison.Ordinal), policy),
            ["Value.Read"], "UnsafeStaticValue", runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [safeReplacement]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(graphConsumer, [safeReplacement],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(43));

        var setReference = NativeModuleCompiler.CreateReferenceSet(providers);
        var setCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), setReference, policy);
        Assert.That(setCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual122 = setCompilation.Program; Assert.That(__nunitActual122, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual122!; }))(), providers, runtimePolicy);
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, providers);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(42));
        var replacementProviders = new[] { safeReplacement, helper };
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, replacementProviders);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, replacementProviders,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(43));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NativeModuleSetsAndGraphsRoundTripCallerUnsafeCopyValueMethodsAsync(
        BoundRuntimePolicy policy)
    {
        string[] exports = ["MakePair", "MakeValue", "MakeEnvelope", "Value.Echo",
            "Value.Transform", "Value.Wrap", "Value.SafeEcho"];
        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        var producer = NativeModuleCompiler.CreateProducer(
            CompileLibrary(CopyValueMethodSource, policy), exports, "UnsafeCopyValue", runtimePolicy);
        var helper = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }", policy),
            ["Helper"], "CopyValueMethodHelper", runtimePolicy);
        var providers = new[] { producer, helper };

        var graphReference = NativeModuleCompiler.CreateGraphReference([producer]);
        var graphCompilation = Compilation.Create(
            SyntaxTree.Parse(CopyValueConsumerSource), graphReference, policy);
        Assert.That(graphCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual123 = graphCompilation.Program; Assert.That(__nunitActual123, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual123!; }))(), [producer], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [producer]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(graphConsumer, [producer],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(23608));

        var safeReplacement = NativeModuleCompiler.CreateProducer(
            CompileLibrary(SafeCopyValueMethodSource, policy), exports, "UnsafeCopyValue", runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [safeReplacement]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(graphConsumer, [safeReplacement],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(23611));

        var setReference = NativeModuleCompiler.CreateReferenceSet(providers);
        var setCompilation = Compilation.Create(
            SyntaxTree.Parse(CopyValueConsumerSource), setReference, policy);
        Assert.That(setCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual124 = setCompilation.Program; Assert.That(__nunitActual124, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual124!; }))(), providers, runtimePolicy);
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, providers);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(23608));
        var replacementProviders = new[] { safeReplacement, helper };
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, replacementProviders);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, replacementProviders,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true), Is.EqualTo(23611));
    }

    [Test]
    public void PersistedWritersRejectUnsupportedUnsafeMemberKinds()
    {
        var privateLayoutStaticMethod = CompileLibrary("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Value {
                private int32 Number;
                public static unsafe int32 Read() { return 42; }
            }
            """);
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(privateLayoutStaticMethod, ["Value.Read"]));

        var uniqueOwnerStaticMethod = CompileLibrary("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public unique struct Value {
                public int32 Placeholder;
                public static unsafe int32 Read() { return 42; }
            }
            """);
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(uniqueOwnerStaticMethod, ["Value.Read"]));

        var mutableMethod = CompileLibrary("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Value {
                public mut int32 Number;
                public Value(int32 number) { Number = number; }
                public mut unsafe int32 Read() { return Number; }
            }
            """);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(mutableMethod, ["Value.Read"]));

        var classMethod = CompileLibrary("""
            public class Box {
                public int32 Number;
                public Box(int32 number) { Number = number; }
                public unsafe int32 Read() { return Number; }
            }
            """);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(classMethod, ["Box.Read"]));

        var classGetter = CompileLibrary("""
            public class Box {
                public int32 Number;
                public Box(int32 number) { Number = number; }
                public int32 Read { unsafe get { return Number; } }
            }
            """);
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(classGetter, ["Box.#get:Read"]));

        var unsupportedValueTypes = new[]
        {
            """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Hidden {
                private mut int32 Number;
                public Hidden(int32 number) { Number = number; }
            }
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Value {
                public int32 Marker;
                public static unsafe int32 Read(Hidden value) { return 42; }
            }
            """,
            """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public unique struct Owned {
                public mut int32 Number;
                public Owned(int32 number) { Number = number; }
            }
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Value {
                public int32 Marker;
                public static unsafe int32 Read(Owned value) { return value.Number; }
            }
            """,
            """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Value {
                public int32 Marker;
                public static unsafe int32 Read(OwnedInt32Buffer buffer) { return 42; }
            }
            """,
            """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Value {
                public int32 Marker;
                public static unsafe int32 Read(ManagedNode node) { return 42; }
            }
            """,
        };
        foreach (var source in unsupportedValueTypes)
        {
            var program = CompileLibrary(source);
            Assert.Throws<ModuleContractException>(() =>
                ModuleCompiler.CreateProducer(program, ["Value.Read"]));
        }
    }

    [Test]
    public void SemanticModuleReadersReconstructCallerUnsafeAuthorizationFromUnsafeBlocks()
    {
        const string source = "int32 Main() { unsafe { return Danger(); } }";
        var producer = ModuleCompiler.CreateProducer(
            CompileLibrary("public unsafe int32 Danger() { return 42; }"), ["Danger"]);
        var compilation = Compilation.Create(SyntaxTree.Parse(source), ModuleCompiler.CreateReference(producer));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual125 = compilation.Program; Assert.That(__nunitActual125, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual125!; }))();
        var main = program.Functions[program.EntryPoint];
        var unsafeBlock = ((global::System.Func<BoundUnsafe>)(() => { var __nunitActual126 = global::System.Linq.Enumerable.Single(main.Body.Statements); Assert.That(__nunitActual126, Is.TypeOf<BoundUnsafe>()); return (BoundUnsafe)__nunitActual126!; }))();

        var forgedMain = main with {
            Body = main.Body with { Statements = [unsafeBlock.Body] },
        };
        var forgedProgram = program with {
            Functions = program.Functions.SetItem(program.EntryPoint, forgedMain),
        };
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateConsumer(forgedProgram, producer));

        var consumer = ModuleCompiler.CreateConsumer(program, producer);
        const byte BoundUnsafeStatementTag = 11;
        var unsafeScope = new byte[1 + (2 * sizeof(int))];
        unsafeScope[0] = BoundUnsafeStatementTag;
        BinaryPrimitives.WriteInt32LittleEndian(unsafeScope.AsSpan(1), unsafeBlock.Span.Start);
        BinaryPrimitives.WriteInt32LittleEndian(unsafeScope.AsSpan(1 + sizeof(int)), unsafeBlock.Span.Length);
        var unsafeScopeOffset = consumer.AsSpan().IndexOf(unsafeScope);
        Assert.That(unsafeScopeOffset >= 0, Is.True);
        Assert.That(consumer.AsSpan().LastIndexOf(unsafeScope), Is.EqualTo(unsafeScopeOffset));

        var forgedConsumer = new byte[consumer.Length - unsafeScope.Length];
        consumer.AsSpan(0, unsafeScopeOffset).CopyTo(forgedConsumer);
        consumer.AsSpan(unsafeScopeOffset + unsafeScope.Length)
            .CopyTo(forgedConsumer.AsSpan(unsafeScopeOffset));
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(forgedConsumer, producer));
    }

    [Test]
    public async Task SemanticModuleGraphPersistsUnsafeImportsWithinUnsafeBlocksAsync()
    {
        var policy = NativeRuntimePolicy.FailFast;
        var leaf = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public unsafe int32 Danger() { return 42; }"), ["Danger"], "UnsafeLeaf", policy);
        var middleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 SafeWrapper() { unsafe { return Danger(); } }"),
            NativeModuleCompiler.CreateGraphReference([leaf]), (BoundRuntimePolicy)policy);
        Assert.That(middleCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, middleCompilation.Diagnostics));
        var middle = NativeModuleCompiler.CreateGraphProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual127 = middleCompilation.Program; Assert.That(__nunitActual127, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual127!; }))(), ["SafeWrapper"], "SafeMiddle", [leaf], policy);

        var rootCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return SafeWrapper(); }"),
            NativeModuleCompiler.CreateGraphReference([middle]), (BoundRuntimePolicy)policy);
        Assert.That(rootCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual128 = rootCompilation.Program; Assert.That(__nunitActual128, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual128!; }))(), [middle], policy);
        NativeModuleCompiler.ValidateGraph(root, [middle, leaf]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [leaf, middle],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public async Task SemanticModuleSetsPersistCallerUnsafeImportsWithinUnsafeBlocksAsync()
    {
        var policy = NativeRuntimePolicy.FailFast;
        var provider = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public unsafe int32 Danger() { return 42; }"),
            ["Danger"], "UnsafeSetProvider", policy);
        var unusedProvider = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }"),
            ["Helper"], "UnusedSetProvider", policy);
        byte[][] providers = [provider, unusedProvider];
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { unsafe { return Danger(); } }"),
            NativeModuleCompiler.CreateReferenceSet(providers), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));

        var consumer = NativeModuleCompiler.CreateConsumerSet(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual129 = compilation.Program; Assert.That(__nunitActual129, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual129!; }))(), providers, policy);
        NativeModuleCompiler.ValidateConsumerSet(consumer, providers);
        Assert.That(NativeModuleCompiler.InspectConsumerSet(consumer).Dependencies, Does.Contain("UnsafeSetProvider"));
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumer, providers,
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public void GraphReadersRejectCallerUnsafeMemberContracts()
    {
        var policy = NativeRuntimePolicy.FailFast;
        var dependency = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }"), ["Helper"], "SafeDependency", policy);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Box {
                public int32 Placeholder;
                public static bool Danger(out int32 value) { value = 42; return true; }
            }
            """), NativeModuleCompiler.CreateGraphReference([dependency]), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var graph = NativeModuleCompiler.CreateGraphProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual130 = compilation.Program; Assert.That(__nunitActual130, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual130!; }))(), ["Box.Danger"], "GraphProvider", [dependency], policy);
        var forged = ForgeCallerUnsafeMemberGraphPackage(graph, "Box.Danger");

        Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.CreateGraphReference([forged]));
        Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.InspectGraphArtifact(forged));

        var uniqueCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public unique struct Owned {
                public mut int32 Number;
                public Owned(int32 number) { Number = number; }
            }
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Box {
                public int32 Placeholder;
                public static int32 Danger(int32 value) {
                    Owned owned = new Owned(value);
                    return owned.Number;
                }
            }
            """), NativeModuleCompiler.CreateGraphReference([dependency]), (BoundRuntimePolicy)policy);
        Assert.That(uniqueCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, uniqueCompilation.Diagnostics));
        var uniqueGraph = NativeModuleCompiler.CreateGraphProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual131 = uniqueCompilation.Program; Assert.That(__nunitActual131, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual131!; }))(), ["Box.Danger"],
            "UniqueContractProvider", [dependency], policy);
        var forgedUnique = ForgeCallerUnsafeMemberGraphPackage(uniqueGraph, "Box.Danger",
            static contract => contract.Replace("|in:2|", "|in:Owned:E:Unique:-|",
                StringComparison.Ordinal));

        Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.CreateGraphReference([forgedUnique]));
        Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.InspectGraphArtifact(forgedUnique));

        var contractLength = BinaryPrimitives.ReadInt32LittleEndian(uniqueGraph.AsSpan(11, sizeof(int)));
        var imageLength = BinaryPrimitives.ReadInt32LittleEndian(uniqueGraph.AsSpan(15, sizeof(int)));
        var contractStart = uniqueGraph.Length - SHA256.HashSizeInBytes - imageLength - contractLength;
        var contractBytes = uniqueGraph.AsSpan(contractStart, contractLength).ToArray();
        var unsafeFlagOffset = FindModuleFieldUnsafeFlagOffset(contractBytes, "Owned", "Number");
        var forgedUnsafeField = (byte[])uniqueGraph.Clone();
        forgedUnsafeField[contractStart + unsafeFlagOffset] = 1;
        SHA256.HashData(forgedUnsafeField.AsSpan(0, forgedUnsafeField.Length - SHA256.HashSizeInBytes))
            .CopyTo(forgedUnsafeField, forgedUnsafeField.Length - SHA256.HashSizeInBytes);
        Assert.Throws<ModuleFormatException>(() =>
            NativeModuleCompiler.CreateGraphReference([forgedUnsafeField]));
        Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.InspectGraphArtifact(forgedUnsafeField));
    }

    [Test]
    public void SemanticReadersBoundRepeatedNestedCopySignatureValidation()
    {
        const int MaximumLayer = 24;
        var layerNames = Enumerable.Range(0, MaximumLayer + 1)
            .Select(static index => "CopyLayer" + index).ToArray();
        var structures = layerNames.Select((name, index) => index == MaximumLayer
            ? $"[Blittable] [Layout(LayoutKind.Sequential)] public struct {name} {{ public mut int32 Left; public mut int32 Right; "
                + $"public {name}(int32 left, int32 right) {{ Left = left; Right = right; }} }}"
            : $"[Blittable] [Layout(LayoutKind.Sequential)] public struct {name} {{ public int32 Left; public int32 Right; }}");
        var touchFunctions = layerNames.Select((name, index) =>
            $"public int32 Touch{index}({name} value) {{ return 0; }}").ToArray();
        var source = string.Join(Environment.NewLine, structures.Concat(touchFunctions)) + """

            public CopyLayer24 MakeLayer() { return new CopyLayer24(1, 2); }

            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Box {
                public int32 Marker;
                public static unsafe int32 Danger(CopyLayer24 value) { return 42; }
            }
            """;
        var program = CompileLibrary(source);
        var exports = layerNames.Select((_, index) => "Touch" + index)
            .Append("MakeLayer").Append("Box.Danger").ToArray();
        var producer = ModuleCompiler.CreateProducer(program, exports);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                CopyLayer24 value = MakeLayer();
                unsafe { return Box.Danger(value); }
            }
            """), ModuleCompiler.CreateReference(producer));
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual132 = consumerCompilation.Program; Assert.That(__nunitActual132, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual132!; }))(), producer);
        var forged = ForgeRepeatedNestedCopyModule(consumer, layerNames);

        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(forged, producer));
        var repeatedSupportedCopyLayout = ForgeRepeatedNestedCopyModule(
            consumer, layerNames, makeLeafPrivate: false);
        Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.Link(repeatedSupportedCopyLayout, producer));
    }

    private static byte[] ForgeRepeatedNestedCopyModule(byte[] module, string[] layerNames,
        bool makeLeafPrivate = true)
    {
        var forged = (byte[])module.Clone();
        var version = BinaryPrimitives.ReadInt32LittleEndian(forged.AsSpan(sizeof(int), sizeof(int)));
        var offset = sizeof(int) * 2;
        SkipModuleString(forged, ref offset);
        offset += sizeof(byte) * 3;
        SkipModuleString(forged, ref offset);
        var invocationBufferCount = BinaryPrimitives.ReadInt32LittleEndian(forged.AsSpan(offset));
        offset += sizeof(int);
        for (var index = 0; index < invocationBufferCount; index++)
        {
            SkipModuleString(forged, ref offset);
            offset += sizeof(int) + sizeof(byte);
        }
        offset += sizeof(int);
        var structureCount = BinaryPrimitives.ReadInt32LittleEndian(forged.AsSpan(offset));
        offset += sizeof(int);
        var structureIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var fieldTypeOffsets = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var firstFieldPrivateOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < structureCount; index++)
        {
            var name = ReadModuleString(forged, ref offset);
            structureIndexes.Add(name, index);
            if (version is 77 or 79 or 80)
            {
                offset += sizeof(byte);
            }
            offset += sizeof(int) + (sizeof(byte) * 4);
            if (version >= FixedLayoutModuleVersion)
            {
                offset += sizeof(int) + sizeof(byte);
            }
            var hasCleanup = forged[offset++] != 0;
            if (hasCleanup)
            {
                SkipModuleString(forged, ref offset);
            }
            var fieldCount = BinaryPrimitives.ReadInt32LittleEndian(forged.AsSpan(offset));
            offset += sizeof(int);
            var typeOffsets = new List<int>(fieldCount);
            for (var field = 0; field < fieldCount; field++)
            {
                SkipModuleString(forged, ref offset);
                typeOffsets.Add(offset);
                offset += sizeof(int) * 3;
                if (field == 0)
                {
                    firstFieldPrivateOffsets.Add(name, offset);
                }
                offset += sizeof(byte) * 4;
            }
            fieldTypeOffsets.Add(name, typeOffsets);
        }

        for (var index = 1; index < layerNames.Length; index++)
        {
            foreach (var typeOffset in fieldTypeOffsets[layerNames[index]])
            {
                BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(typeOffset),
                    100 + structureIndexes[layerNames[index - 1]]);
            }
        }
        if (makeLeafPrivate)
        {
            forged[firstFieldPrivateOffsets[layerNames[0]]] = 1;
        }
        return forged;
    }

    private static void SkipModuleString(byte[] bytes, ref int offset)
    {
        var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
        offset += sizeof(int) + length;
    }

    private static string ReadModuleString(byte[] bytes, ref int offset)
    {
        var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
        offset += sizeof(int);
        var value = Encoding.UTF8.GetString(bytes, offset, length);
        offset += length;
        return value;
    }

    [Test]
    public void TrRmPreservesCallerUnsafeObligationsAndRejectsCalls()
    {
        var declaration = CompileLibrary(
            "[NoThrow] public unsafe int32 Danger() { return 42; }"
            + " [DoesNotReturn] public unsafe void Never() { while (true) { } }");
        var loadedUnsafe = TrRmExecutableCompiler.Load(
            TrRmExecutableCompiler.Emit(declaration, "unsafe-contract"));
        var unsafeFunction = global::System.Linq.Enumerable.Single(loadedUnsafe.Functions, static function => function.Name == "Danger");
        Assert.That(unsafeFunction.IsNoThrow, Is.True);
        Assert.That(unsafeFunction.DoesNotReturn, Is.False);
        Assert.That(unsafeFunction.RequiresUnsafeCaller, Is.True);
        var nonReturningFunction = global::System.Linq.Enumerable.Single(loadedUnsafe.Functions, static function => function.Name == "Never");
        Assert.That(nonReturningFunction.IsNoThrow, Is.False);
        Assert.That(nonReturningFunction.DoesNotReturn, Is.True);
        Assert.That(nonReturningFunction.RequiresUnsafeCaller, Is.True);

        var safeDeclaration = CompileLibrary("[NoThrow] public int32 Danger() { return 42; }");
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(safeDeclaration, "safe-contract"));
        var function = global::System.Linq.Enumerable.Single(loaded.Functions);
        Assert.That(function.IsNoThrow, Is.True);
        Assert.That(function.RequiresUnsafeCaller, Is.False);

        var caller = Compilation.Create(SyntaxTree.Parse(
            "unsafe int32 Danger() { return 42; } int32 Main() { return unsafe(Danger()); }"));
        Assert.That(caller.HasErrors, Is.False, string.Join(Environment.NewLine, caller.Diagnostics));
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual133 = caller.Program; Assert.That(__nunitActual133, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual133!; }))(), "unsafe-call"));

        var scopedCaller = Compilation.Create(SyntaxTree.Parse(
            "unsafe int32 Danger() { return 42; }"
            + " int32 Main() { unsafe { return Danger(); } }"));
        Assert.That(scopedCaller.HasErrors, Is.False, string.Join(Environment.NewLine, scopedCaller.Diagnostics));
        var executable = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual134 = scopedCaller.Program; Assert.That(__nunitActual134, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual134!; }))(), "unsafe-block-call"));
        Assert.That(NativeJit.Execute(executable, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void TrRmUnsafeRegionsNestWithLoopsAndCarryOuterScalarValues()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unsafe int32 Danger() { return 42; }
            int32 Main() {
                mut int32 value = 0;
                while (value < 1) {
                    unsafe { unsafe { value = Danger(); } }
                }
                return value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual135 = compilation.Program; Assert.That(__nunitActual135, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual135!; }))(), "nested-unsafe-regions");
        Assert.That(NativeJit.Execute(TrRmExecutableCompiler.Load(artifact),
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void TrRmLoopTransfersRemainValidWhenNestedWithinUnsafeScope()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut int32 value = 0;
                unsafe {
                    outer: while (value < 3) {
                        while (value < 3) {
                            value++;
                            if (value == 1) { continue outer; }
                            if (value == 2) { continue; }
                            break outer;
                        }
                    }
                }
                return value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual136 = compilation.Program; Assert.That(__nunitActual136, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual136!; }))(), "unsafe-nested-loop-transfers");
        Assert.That(NativeJit.Execute(TrRmExecutableCompiler.Load(artifact),
            NativeRuntimePolicy.FailFast), Is.EqualTo(3));
    }

    [Test]
    public void TrRmTerminalAssignmentPrefixKeepsEnclosingUnsafeAuthorization()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unsafe int32 Danger() { return 42; }
            int32 Main() {
                mut int32 value = 0;
                unsafe {
                    do {
                        value = Danger();
                        break;
                    } while (false);
                }
                return value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual137 = compilation.Program; Assert.That(__nunitActual137, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual137!; }))(), "unsafe-terminal-assignment-prefix");
        Assert.That(NativeJit.Execute(TrRmExecutableCompiler.Load(artifact),
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }
    [TestCase("break;")]
    [TestCase("continue;")]
    [TestCase("break outer;")]
    [TestCase("continue outer;")]
    public void TrRmRejectsLoopTransfersThatLeaveUnsafeScope(string transfer)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { mut int32 value = 0;"
            + " outer: while (value < 1) { while (value < 1) { unsafe { "
            + transfer + " } } } return 42; }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual138 = compilation.Program; Assert.That(__nunitActual138, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual138!; }))(), "unsafe-loop-transfer"));
    }

    [Test]
    public void TrRmUnsafeRegionPreservesReservedBodyLayoutBits()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "unsafe int32 Danger() { return 42; }"
            + " int32 Main() { unsafe { return Danger(); } }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual139 = compilation.Program; Assert.That(__nunitActual139, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual139!; }))(), "unsafe-region-layout");
        var bodyOffset = BodyRowOffset(artifact, 2);
        Assert.That(artifact[bodyOffset] & 0xFC, Is.EqualTo(0x08));

        var invalidForm = (byte[])artifact.Clone();
        invalidForm[bodyOffset] |= 0x04;
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(invalidForm));

        var reservedBit = (byte[])artifact.Clone();
        reservedBit[bodyOffset] |= 0x10;
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(reservedBit));
    }

    [Test]
    public void TrRmAuditsUnscopedCallerUnsafeCallsInUncalledIndexedFunctions()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow] int32 Danger() { return 42; }
            int32 Caller() {
                mut int32 value = 0;
                while (value < 1) { value = Danger(); }
                return value;
            }
            int32 Main() { return 0; }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual140 = compilation.Program; Assert.That(__nunitActual140, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual140!; }))(), "unsafe-unscoped-uncalled-indexed");
        Assert.That(artifact[BodyRowOffset(artifact, 2)] & 0xFC, Is.EqualTo(0x08));
        _ = TrRmExecutableCompiler.Load(artifact);

        var (methodOffset, methodCount) = FindSection(artifact, 0x16);
        var contractRow = 0;
        for (var method = 0; method < methodCount; method++)
        {
            var methodRow = methodOffset + (method * 28);
            if (BinaryPrimitives.ReadUInt32LittleEndian(artifact.AsSpan(methodRow + 20)) != 0)
            {
                contractRow = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    artifact.AsSpan(methodRow + 16)));
                break;
            }
        }
        Assert.That(contractRow, Is.Not.EqualTo(0));

        var (contractOffset, contractCount) = FindSection(artifact, 0x1B);
        var contractData = contractOffset + ((contractCount + 1) * sizeof(uint));
        var contractRowOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            artifact.AsSpan(contractOffset + ((contractRow - 1) * sizeof(uint)))));
        var effectsMaskOffset = contractData + contractRowOffset + 8;
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            artifact.AsSpan(effectsMaskOffset)), Is.EqualTo(1u));
        BinaryPrimitives.WriteUInt32LittleEndian(artifact.AsSpan(effectsMaskOffset), 5);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }

    [Test]
    public void TrRmUnsafeRegionRequiresAValidExitBlock()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "unsafe int32 Danger() { return 42; }"
            + " int32 Main() { unsafe { return Danger(); } }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual141 = compilation.Program; Assert.That(__nunitActual141, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual141!; }))(), "unsafe-region-exit");
        var malformed = (byte[])artifact.Clone();
        var regionOffset = RegionTableOffset(malformed, 2);
        var width = 1 << (malformed[BodyRowOffset(malformed, 2)] & 3);
        var header = ReadCompactCount(malformed, regionOffset + 1 + width, width);
        WriteCompactCount(malformed, regionOffset + 1 + (2 * width), width, header);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    [Test]
    public void TrRmUnsafeRegionRequiresEveryExternalEdgeToUseItsEntry()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unsafe int32 Danger() { return 42; }
            int32 Main() {
                mut int32 value = 0;
                unsafe {
                    if (value == 0) { value = Danger(); }
                    else { value = 1; }
                }
                return value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual142 = compilation.Program; Assert.That(__nunitActual142, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual142!; }))(), "unsafe-region-entry");
        var malformed = (byte[])artifact.Clone();
        var bodyRow = BodyRowOffset(malformed, 2);
        var regionOffset = RegionTableOffset(malformed, 2);
        var width = 1 << (malformed[bodyRow] & 3);
        var regionHeaderOffset = regionOffset + 1 + width;
        var regionHeader = ReadCompactCount(malformed, regionHeaderOffset, width);
        var alternateHeader = FindOtherBlockInRegion(malformed, 2, 1, regionHeader);
        WriteCompactCount(malformed, regionHeaderOffset, width, alternateHeader);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    [Test]
    public void TrRmUnsafeRegionRejectsEdgesThatBypassItsDesignatedExit()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { mut int32 value = 0; unsafe { value = 1; } return value; }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual143 = compilation.Program; Assert.That(__nunitActual143, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual143!; }))(), "unsafe-region-designated-exit");
        var malformed = (byte[])artifact.Clone();
        var bodyOffset = BodyRowOffset(malformed, 1);
        var regionOffset = RegionTableOffset(malformed, 1);
        var width = 1 << (malformed[bodyOffset] & 3);
        WriteCompactCount(malformed, regionOffset + 1 + (2 * width), width, 1);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    [Test]
    public void TrRmNestedUnsafeRegionCannotExitItsParentLoop()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unsafe int32 Danger() { return 42; }
            int32 Main() {
                mut int32 value = 0;
                while (value < 1) {
                    unsafe { unsafe { value = Danger(); } }
                }
                return value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual144 = compilation.Program; Assert.That(__nunitActual144, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual144!; }))(), "unsafe-region-parent-exit");
        var malformed = (byte[])artifact.Clone();
        var bodyOffset = BodyRowOffset(malformed, 2);
        var regionOffset = RegionTableOffset(malformed, 2);
        var width = 1 << (malformed[bodyOffset] & 3);
        var nestedUnsafeRegion = regionOffset + 1 + (4 * width);
        Assert.That(malformed[nestedUnsafeRegion], Is.EqualTo(6));
        WriteCompactCount(malformed, nestedUnsafeRegion + 1 + (2 * width), width, 1);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    private static int BodyRowOffset(byte[] artifact, int row)
    {
        var (dataOffset, bodyCount) = FindSection(artifact, 0x20);
        if (row < 1 || row > bodyCount)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        var rowOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(
            artifact.AsSpan(dataOffset + ((row - 1) * sizeof(uint))));
        return dataOffset + ((bodyCount + 1) * sizeof(uint)) + rowOffset;
    }

    private static (int Offset, int Count) FindSection(byte[] artifact, ushort kind)
    {
        var sectionCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(artifact.AsSpan(12));
        for (var section = 0; section < sectionCount; section++)
        {
            var entryOffset = 32 + section * 16;
            if (BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(entryOffset)) == kind)
            {
                return (
                    (int)BinaryPrimitives.ReadUInt32LittleEndian(artifact.AsSpan(entryOffset + 8)),
                    (int)BinaryPrimitives.ReadUInt32LittleEndian(artifact.AsSpan(entryOffset + 4)));
            }
        }

        throw new InvalidDataException("Executable artifact is missing a required section.");
    }

    private static int RegionTableOffset(byte[] artifact, int bodyRow)
    {
        var bodyOffset = BodyRowOffset(artifact, bodyRow);
        var width = 1 << (artifact[bodyOffset] & 3);
        var regionCount = ReadCompactCount(artifact, bodyOffset + 1 + (2 * width), width);
        var typeCount = ReadCompactCount(artifact, bodyOffset + 1 + (3 * width), width);
        if (regionCount == 0)
        {
            throw new InvalidDataException("Indexed Body has no region table.");
        }

        return bodyOffset + 1 + (4 * width) + (4 * typeCount);
    }

    private static int FindOtherBlockInRegion(byte[] artifact, int bodyRow, int regionId, int excludedBlock)
    {
        var bodyOffset = BodyRowOffset(artifact, bodyRow);
        var width = 1 << (artifact[bodyOffset] & 3);
        var blockCount = ReadCompactCount(artifact, bodyOffset + 1, width);
        var valueCount = ReadCompactCount(artifact, bodyOffset + 1 + width, width);
        var regionCount = ReadCompactCount(artifact, bodyOffset + 1 + (2 * width), width);
        var typeCount = ReadCompactCount(artifact, bodyOffset + 1 + (3 * width), width);
        var blockOffsetsOffset = bodyOffset + 1 + (4 * width) + (4 * typeCount)
            + (regionCount * (1 + (4 * width)))
            + (2 * valueCount * width)
            + ((blockCount + 1) * width)
            + (valueCount * width);
        var blockDataOffset = blockOffsetsOffset + ((2 * blockCount + 1) * width);
        for (var block = 1; block <= blockCount; block++)
        {
            if (block == excludedBlock)
            {
                continue;
            }

            var blockDataStart = blockDataOffset + ReadCompactCount(artifact,
                blockOffsetsOffset + ((block - 1) * width), width);
            var presence = artifact[blockDataStart];
            if ((presence & 1) != 0
                && ReadCompactCount(artifact, blockDataStart + 1, width) == regionId)
            {
                return block;
            }
        }

        throw new InvalidDataException("Indexed Body has no alternate block in the requested region.");
    }

    private static int ReadCompactCount(byte[] bytes, int offset, int width) =>
        width switch {
            1 => bytes[offset],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset)),
            4 => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset))),
            _ => throw new InvalidDataException("Invalid compact Body width."),
        };

    private static void WriteCompactCount(byte[] bytes, int offset, int width, int value)
    {
        switch (width)
        {
            case 1:
                bytes[offset] = checked((byte)value);
                break;
            case 2:
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), checked((ushort)value));
                break;
            case 4:
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), checked((uint)value));
                break;
            default:
                throw new InvalidDataException("Invalid compact Body width.");
        }
    }

    [Test]
    public void TrRmAndSemanticModulesRejectUnsupportedUnsafeFieldContracts()
    {
        var program = CompileLibrary("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Cell {
                public unsafe int32 Value;
                public Cell() { unsafe { Value = 42; } }
            }
            public int32 Read() { Cell cell = new Cell(); return unsafe(cell.Value); }
            """);

        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(program, "unsafe-field"));

        var classProgram = CompileLibrary("""
            public class Box {
                public unsafe int32 Value;
                public Box(int32 value) { unsafe { Value = value; } }
            }
            public int32 Read() {
                require Box.TryCreate(42, out var box);
                return unsafe(box.Value);
            }
            """);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(classProgram, ["Read"]));
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(classProgram, "unsafe-class-field"));

        var fieldLoanProgram = CompileLibrary("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Counter {
                public mut int32 Number;
                public Counter(int32 number) { Number = number; }
                public mut int32 Next() { Number = Number + 1; return Number; }
            }
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Owner {
                public unsafe mut Counter Value;
                public Owner(Counter value) { unsafe { Value = value; } }
            }
            public int32 Invoke() {
                mut Owner owner = new Owner(new Counter(41));
                unsafe { return owner.Value.Next(); }
            }
            """);
        Assert.That(ModuleCompiler.CreateProducer(fieldLoanProgram, ["Invoke"]), Is.Not.Empty);

        var safeFieldLoanProgram = CompileLibrary("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Counter {
                public mut int32 Number;
                public Counter(int32 number) { Number = number; }
                public mut int32 Next() { Number = Number + 1; return Number; }
            }
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Owner {
                public mut Counter Value;
                public Owner(Counter value) { Value = value; }
            }
            public int32 Invoke() {
                mut Owner owner = new Owner(new Counter(41));
                return owner.Value.Next();
            }
            """);
        var safeFieldLoanModule = ModuleCompiler.CreateProducer(safeFieldLoanProgram, ["Invoke"]);
        var forgedFieldLoanModule = (byte[])safeFieldLoanModule.Clone();
        forgedFieldLoanModule[FindModuleFieldUnsafeFlagOffset(forgedFieldLoanModule, "Owner", "Value")] = 1;
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forgedFieldLoanModule));

        const string uniqueOwnerFieldLoanSource = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Counter {
                public mut int32 Number;
                public Counter(int32 number) { Number = number; }
                public mut int32 Next() { Number = Number + 1; return Number; }
            }
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Inner {
                public mut Counter Value;
                public Inner(Counter value) { unsafe { Value = value; } }
            }
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public unique struct Owner {
                public mut Inner Nested;
                public Owner(Inner nested) { Nested = nested; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            public int32 Invoke() {
                mut Owner owner = new Owner(new Inner(new Counter(41)));
                unsafe { return owner.Nested.Value.Next(); }
            }
            """;
        var unsafeUniqueOwnerProgram = CompileLibrary(uniqueOwnerFieldLoanSource
            .Replace("public mut Counter Value;", "public unsafe mut Counter Value;",
                StringComparison.Ordinal));
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(unsafeUniqueOwnerProgram, ["Invoke"]));
        var unsafeUniqueExpressionLoanProgram = CompileLibrary(uniqueOwnerFieldLoanSource
            .Replace("public mut Counter Value;", "public unsafe mut Counter Value;",
                StringComparison.Ordinal)
            .Replace("unsafe { return owner.Nested.Value.Next(); }",
                "return unsafe(owner.Nested.Value.Next());", StringComparison.Ordinal));
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(unsafeUniqueExpressionLoanProgram, ["Invoke"]));

        var safeUniqueOwnerModule = ModuleCompiler.CreateProducer(
            CompileLibrary(uniqueOwnerFieldLoanSource), ["Invoke"]);
        var forgedUnsafeUniqueOwnerModule = (byte[])safeUniqueOwnerModule.Clone();
        forgedUnsafeUniqueOwnerModule[FindModuleFieldUnsafeFlagOffset(
            forgedUnsafeUniqueOwnerModule, "Inner", "Value")] = 1;
        Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(forgedUnsafeUniqueOwnerModule));
    }

    private static int FindModuleFieldUnsafeFlagOffset(byte[] module, string structureName, string fieldName)
    {
        var version = BinaryPrimitives.ReadInt32LittleEndian(module.AsSpan(sizeof(int), sizeof(int)));
        var offset = sizeof(int) * 2;
        SkipModuleString(module, ref offset);
        offset += sizeof(byte) * 3;
        SkipModuleString(module, ref offset);
        var invocationBufferCount = BinaryPrimitives.ReadInt32LittleEndian(module.AsSpan(offset));
        offset += sizeof(int);
        for (var index = 0; index < invocationBufferCount; index++)
        {
            SkipModuleString(module, ref offset);
            offset += sizeof(int) + sizeof(byte);
        }
        offset += sizeof(int);
        var structureCount = BinaryPrimitives.ReadInt32LittleEndian(module.AsSpan(offset));
        offset += sizeof(int);
        for (var structure = 0; structure < structureCount; structure++)
        {
            var name = ReadModuleString(module, ref offset);
            if (version is 77 or 79 or 80)
            {
                offset += sizeof(byte);
            }
            offset += sizeof(int) + (sizeof(byte) * 4);
            if (version >= FixedLayoutModuleVersion)
            {
                offset += sizeof(int) + sizeof(byte);
            }
            if (module[offset++] != 0)
            {
                SkipModuleString(module, ref offset);
            }
            var fieldCount = BinaryPrimitives.ReadInt32LittleEndian(module.AsSpan(offset));
            offset += sizeof(int);
            for (var field = 0; field < fieldCount; field++)
            {
                var currentField = ReadModuleString(module, ref offset);
                offset += sizeof(int) * 3;
                var unsafeFlagOffset = offset + (sizeof(byte) * 3);
                offset += sizeof(byte) * 4;
                if (name == structureName && currentField == fieldName)
                {
                    return unsafeFlagOffset;
                }
            }
        }
        throw new InvalidDataException("Module does not contain the requested field declaration.");
    }

    private static byte[] ForgeCallerUnsafeMemberGraphPackage(byte[] package, string functionName,
        Func<string, string>? mutateContract = null)
    {
        var contractLength = BinaryPrimitives.ReadInt32LittleEndian(package.AsSpan(11, 4));
        var imageLength = BinaryPrimitives.ReadInt32LittleEndian(package.AsSpan(15, 4));
        var contractStart = package.Length - 32 - imageLength - contractLength;
        var contract = package.AsSpan(contractStart, contractLength).ToArray();
        var name = Encoding.UTF8.GetBytes(functionName);
        byte[] contractMarker = [.. name, (byte)'|'];
        var contractTextStart = contract.AsSpan().IndexOf(contractMarker);
        Assert.That(contractTextStart >= sizeof(int), Is.True);
        var contractLengthOffset = contractTextStart - sizeof(int);
        var contractTextLength = BinaryPrimitives.ReadInt32LittleEndian(
            contract.AsSpan(contractLengthOffset, sizeof(int)));
        var contractText = Encoding.UTF8.GetString(contract, contractTextStart, contractTextLength);
        if (mutateContract is not null)
        {
            var mutatedContract = mutateContract(contractText);
            Assert.That(mutatedContract, Is.Not.EqualTo(contractText));
            contractText = mutatedContract;
        }
        var unsafeContractText = Encoding.UTF8.GetBytes(contractText + "|CallerUnsafe");
        var growth = unsafeContractText.Length - contractTextLength;
        var bodyNameStart = contract.AsSpan().LastIndexOf(name);
        Assert.That(bodyNameStart > contractTextStart, Is.True);
        var forgedContract = new byte[contract.Length + growth];
        contract.AsSpan(0, contractLengthOffset).CopyTo(forgedContract);
        BinaryPrimitives.WriteInt32LittleEndian(forgedContract.AsSpan(contractLengthOffset, sizeof(int)),
            unsafeContractText.Length);
        unsafeContractText.CopyTo(forgedContract, contractLengthOffset + sizeof(int));
        contract.AsSpan(contractLengthOffset + sizeof(int) + contractTextLength)
            .CopyTo(forgedContract.AsSpan(contractLengthOffset + sizeof(int) + unsafeContractText.Length));
        var requiresUnsafeCallerOffset = bodyNameStart + growth + name.Length + sizeof(int) + 6;
        forgedContract[requiresUnsafeCallerOffset] = 1;

        var forgedPackage = new byte[package.Length + growth];
        package.AsSpan(0, contractStart).CopyTo(forgedPackage);
        BinaryPrimitives.WriteInt32LittleEndian(forgedPackage.AsSpan(11, sizeof(int)),
            forgedContract.Length);
        forgedContract.CopyTo(forgedPackage, contractStart);
        package.AsSpan(contractStart + contractLength, imageLength)
            .CopyTo(forgedPackage.AsSpan(contractStart + forgedContract.Length));
        SHA256.HashData(forgedPackage.AsSpan(0, forgedPackage.Length - 32))
            .CopyTo(forgedPackage, forgedPackage.Length - 32);
        return forgedPackage;
    }
}
