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
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class CallerUnsafeTests
{
    private const string PairDeclaration = """
        public struct Pair {
            public mut int32 First;
            public mut int32 Second;
            public Pair(int32 first, int32 second) { First = first; Second = second; }
        }
        """;
    private const string TripleDeclaration = """
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
        public struct Envelope {
            public mut Pair Payload;
            public Envelope(Pair payload) { Payload = payload; }
        }
        """;
    private const string FloatBoxDeclaration = """
        public struct FloatBox {
            public float64 Value;
            public FloatBox(float64 value) { Value = value; }
        }
        """;
    private const string ValueDeclaration = """
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
        public struct SafeInner {
            public mut int32 Value;
            public SafeInner(int32 value) { Value = value; }
        }
        public struct UnsafeInner {
            public unsafe mut int32 Value;
            public UnsafeInner(int32 value) { unsafe { Value = value; } }
        }
        public struct UnsafeOuter {
            public unsafe mut SafeInner Value;
            public UnsafeOuter(SafeInner value) { unsafe { Value = value; } }
        }
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
        public struct DiscardedBeforeLoanCounter {
            public int32 Value;
        }
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
        public struct UnsafeLeafOwner {
            public unsafe mut LoanCounter Value;
            public UnsafeLeafOwner(LoanCounter value) { unsafe { Value = value; } }
        }
        public struct SafeLoanInner {
            public mut LoanCounter Value;
            public SafeLoanInner(LoanCounter value) { Value = value; }
        }
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
        public struct ConsumerLoanCounter {
            public mut int32 Number;
            public ConsumerLoanCounter(int32 number) { Number = number; }
            public unsafe mut int32 UnsafeNextBy(int32 first, int32 second) {
                Number = Number + first * 10 + second;
                return Number;
            }
        }
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
        public struct Nested {
            public Pair Payload;
            public Odd Tail;
            public Nested(Pair payload, Odd tail) { Payload = payload; Tail = tail; }
        }
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
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
        Assert.NotEmpty(offsets);

        var result = module;
        var lengthDelta = newBytes.Length - oldBytes.Length;
        foreach (var offset in offsets.OrderDescending())
        {
            var stringLength = BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(offset - sizeof(int)));
            Assert.Equal(oldBytes.Length, stringLength);

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
        Assert.True(valueOffset >= sizeof(int));
        var valueLength = BinaryPrimitives.ReadInt32LittleEndian(module.AsSpan(valueOffset - sizeof(int)));
        Assert.True(valueLength >= prefixBytes.Length);
        Assert.True(valueOffset + valueLength <= module.Length);
        var value = Encoding.UTF8.GetString(module, valueOffset, valueLength);
        Assert.StartsWith(valuePrefix, value, StringComparison.Ordinal);
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
        Assert.True(offset >= 0);
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
        Assert.True(offset >= 0);
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
        Assert.True(offset >= 0);
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
        Assert.NotNull(property);
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
        Assert.True(nameOffset >= 0);

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
        Assert.True(nameOffset >= 0);

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
        Assert.True(functionNameOffset >= 0);

        var updated = (byte[])module.Clone();
        var requiresUnsafeCallerOffset = functionNameOffset + serializedName.Length + sizeof(int) + 7;
        Assert.Equal(0, updated[requiresUnsafeCallerOffset]);
        updated[requiresUnsafeCallerOffset] = 1;
        return updated;
    }

    [Theory]
    [InlineData("unsafe { return Danger(); }")]
    [InlineData("return unsafe(Danger());")]
    [InlineData("unsafe(Danger()); return 42;")]
    public void ExplicitUnsafeScopesAuthorizeOnlyTheirCall(string body)
    {
        var source = $"unsafe int32 Danger() {{ return 42; }} int32 Caller() {{ {body} }}";
        var program = CompileLibrary(source);
        Assert.True(Assert.Single(program.Functions, static function => function.Name == "Danger")
            .RequiresUnsafeCaller);
        Assert.False(Assert.Single(program.Functions, static function => function.Name == "Caller")
            .RequiresUnsafeCaller);
    }

    [Fact]
    public void ExpressionScopedVoidCallsPreserveExecution()
    {
        ImmutableArray<HostBuffer> buffers = [new("Output", PrimitiveType.Int32, [0], true)];
        const string returnSource = """
            unsafe void Assign(int32 value) { Host.Output[0] = value; }
            void ReturnCall() { return unsafe(Assign(41)); }
            int32 Main() { ReturnCall(); return Host.Output[0]; }
            """;
        var returnCompilation = Compilation.Create(SyntaxTree.Parse(returnSource), buffers);
        Assert.False(returnCompilation.HasErrors,
            string.Join(Environment.NewLine, returnCompilation.Diagnostics));
        Assert.Equal(41, NativeJit.Execute(Assert.IsType<BoundProgram>(returnCompilation.Program),
            NativeRuntimePolicy.FailFast));

        const string discardSource = """
            unsafe void Assign(int32 value) { Host.Output[0] = value; }
            int32 Main() { unsafe(Assign(42)); return Host.Output[0]; }
            """;
        var discardCompilation = Compilation.Create(SyntaxTree.Parse(discardSource), buffers);
        Assert.False(discardCompilation.HasErrors,
            string.Join(Environment.NewLine, discardCompilation.Diagnostics));
        Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(discardCompilation.Program),
            NativeRuntimePolicy.FailFast));
    }

    [Fact]
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
        Assert.False(shortCircuitCompilation.HasErrors,
            string.Join(Environment.NewLine, shortCircuitCompilation.Diagnostics));
        Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(shortCircuitCompilation.Program),
            NativeRuntimePolicy.FailFast));

        const string conditionSource = """
            unsafe bool Pass() { return true; }
            int32 Main() { require unsafe(Pass()); return 42; }
            """;
        var conditionCompilation = Compilation.Create(SyntaxTree.Parse(conditionSource));
        Assert.False(conditionCompilation.HasErrors,
            string.Join(Environment.NewLine, conditionCompilation.Diagnostics));
        Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(conditionCompilation.Program),
            NativeRuntimePolicy.FailFast));

        const string outputSource = """
            unsafe bool Fill(out int32 value) { value = 42; return true; }
            int32 Main() {
                mut int32 value = 0;
                require unsafe(Fill(out value));
                return value;
            }
            """;
        var outputCompilation = Compilation.Create(SyntaxTree.Parse(outputSource));
        Assert.False(outputCompilation.HasErrors,
            string.Join(Environment.NewLine, outputCompilation.Diagnostics));
        Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(outputCompilation.Program),
            NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void UnsafeDeclarationDoesNotAuthorizeItsBodyOrFollowingCalls()
    {
        var bodyCall = Compilation.CreateLibrary(SyntaxTree.Parse(
            "unsafe int32 Danger() { return 42; } unsafe int32 Wrapper() { return Danger(); }"));
        Assert.Null(bodyCall.Program);
        Assert.Contains(bodyCall.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var memberBodyCall = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Value {
                public unsafe int32 Danger() { return 42; }
                public unsafe int32 Wrapper() { return Danger(); }
            }
            """));
        Assert.Null(memberBodyCall.Program);
        Assert.Contains(memberBodyCall.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var leakedScope = Compilation.CreateLibrary(SyntaxTree.Parse(
            "unsafe int32 Danger() { return 42; } int32 Caller() { unsafe(Danger()); return Danger(); }"));
        Assert.Null(leakedScope.Program);
        Assert.Contains(leakedScope.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");
    }

    [Fact]
    public void SafeWrapperMayUseUnsafeImplementationWithoutPropagatingTheObligation()
    {
        var program = CompileLibrary("""
            public unsafe int32 Danger() { return 42; }
            public int32 Wrapper() { unsafe { return Danger(); } }
            """);
        Assert.True(Assert.Single(program.Functions, static function => function.Name == "Danger")
            .RequiresUnsafeCaller);
        Assert.False(Assert.Single(program.Functions, static function => function.Name == "Wrapper")
            .RequiresUnsafeCaller);
    }

    [Fact]
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
        Assert.Null(safeCall.Program);
        Assert.Contains(safeCall.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var authorizedCall = Compilation.Create(SyntaxTree.Parse(declaration
            + " int32 Main() { unsafe { require Leaf.TryCreate(42, out var leaf); return leaf.Value; } }"));
        Assert.False(authorizedCall.HasErrors, string.Join(Environment.NewLine, authorizedCall.Diagnostics));
    }

    [Fact]
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
        Assert.Null(safeAccess.Program);
        Assert.Contains(safeAccess.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var authorizedAccess = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { require Box.TryCreate(42, out var box); unsafe { return box.Value; } }"));
        Assert.False(authorizedAccess.HasErrors, string.Join(Environment.NewLine, authorizedAccess.Diagnostics));
    }

    [Fact]
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
        Assert.Null(safeCalls.Program);
        Assert.Equal(2, safeCalls.Diagnostics.Count(static diagnostic => diagnostic.Code == "TRM3028"));

        var authorizedCalls = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { require Box.TryCreate(21, out var box);"
            + " unsafe { return box.Read() + Box.Twice(1); } }"));
        Assert.False(authorizedCalls.HasErrors, string.Join(Environment.NewLine, authorizedCalls.Diagnostics));

        var safeWrapper = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { require Box.TryCreate(21, out var box); return box.SafeRead(); }"));
        Assert.False(safeWrapper.HasErrors, string.Join(Environment.NewLine, safeWrapper.Diagnostics));
        Assert.False(Assert.Single(Assert.IsType<BoundProgram>(safeWrapper.Program).Functions,
            static function => function.Name == "Box.SafeRead").RequiresUnsafeCaller);
    }

    [Fact]
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
        Assert.Null(safeRead.Program);
        Assert.Contains(safeRead.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var authorizedRead = Compilation.CreateLibrary(SyntaxTree.Parse(declaration
            + " int32 Read() { Cell cell = new Cell(); return unsafe(cell.Value); }"));
        Assert.False(authorizedRead.HasErrors, string.Join(Environment.NewLine, authorizedRead.Diagnostics));

        var safeWrite = Compilation.Create(SyntaxTree.Parse(declaration
            + " int32 Main() { mut Cell cell = new Cell(); cell.Value = 42; return 0; }"));
        Assert.Null(safeWrite.Program);
        Assert.Contains(safeWrite.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var authorizedWrite = Compilation.Create(SyntaxTree.Parse(declaration
            + " int32 Main() { mut Cell cell = new Cell(); unsafe { cell.Value = 42; return cell.Value; } }"));
        Assert.False(authorizedWrite.HasErrors, string.Join(Environment.NewLine, authorizedWrite.Diagnostics));

        var unscopedInitialization = Compilation.CreateLibrary(SyntaxTree.Parse(
            "struct Cell { public unsafe int32 Value; public Cell() { Value = 1; } }"));
        Assert.Null(unscopedInitialization.Program);
        Assert.Contains(unscopedInitialization.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3028");

        var duplicateModifier = Compilation.CreateLibrary(SyntaxTree.Parse(
            "struct Cell { public unsafe unsafe int32 Value; public Cell() { unsafe { Value = 1; } } }"));
        Assert.Null(duplicateModifier.Program);
        Assert.Contains(duplicateModifier.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3001");
    }

    [Fact]
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
        Assert.Null(safeAccess.Program);
        Assert.Contains(safeAccess.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var authorizedAccess = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { mut Outer outer = new Outer(); unsafe { outer.Value = new Inner();"
            + " return outer.Value.Value; } }"));
        Assert.False(authorizedAccess.HasErrors, string.Join(Environment.NewLine, authorizedAccess.Diagnostics));

        var safeWrite = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { mut Outer outer = new Outer(); outer.Value = new Inner(); return 0; }"));
        Assert.Null(safeWrite.Program);
        Assert.Contains(safeWrite.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");
    }

    [Fact]
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
        Assert.Null(safeAccess.Program);
        Assert.Contains(safeAccess.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var authorizedAccess = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { require Box.TryCreate(42, out var box); return unsafe(box.Value); }"));
        Assert.False(authorizedAccess.HasErrors, string.Join(Environment.NewLine, authorizedAccess.Diagnostics));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void SemanticModulesRoundTripCallerUnsafeCopyStructFieldPaths(BoundRuntimePolicy policy)
    {
        var producer = ModuleCompiler.CreateProducer(
        CompileLibrary(UnsafeFieldExpressionModuleSource, policy),
        ["MakeOuter", "MakeLeafOuter", "ReadValue"]);
        Assert.Equal(75,BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, sizeof(int))));

        var reference = ModuleCompiler.CreateReference(producer);
        foreach (var (owner, fieldName, expected) in new[]
        {
            ("UnsafeOuter", "Value", true),
            ("UnsafeLeafOuter", "Value", false),
            ("SafeInner", "Value", false),
            ("UnsafeInner", "Value", true),
        })
        {
            var structure = Assert.Single(reference.Structures, item => item.Name == owner);
            Assert.Equal(expected, Assert.Single(structure.Fields, field => field.Name == fieldName)
                .RequiresUnsafeCaller);
        }

        var unscoped = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                UnsafeOuter outer = MakeOuter(40);
                return outer.Value.Value;
            }
            """), reference, policy);
        Assert.Null(unscoped.Program);
        Assert.Contains(unscoped.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var expressionScoped = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                UnsafeOuter outer = MakeOuter(40);
                return unsafe(outer.Value.Value);
            }
            """), reference, policy);
        Assert.False(expressionScoped.HasErrors,
            string.Join(Environment.NewLine, expressionScoped.Diagnostics));
        var expressionProgram = Assert.IsType<BoundProgram>(expressionScoped.Program);
        var expressionMain = expressionProgram.Functions[expressionProgram.EntryPoint];
        var expressionReturn = Assert.IsType<BoundReturn>(expressionMain.Body.Statements[^1]);
        var unsafeRead = Assert.IsType<BoundUnsafeExpression>(expressionReturn.Value);
        var expressionConsumer = ModuleCompiler.CreateConsumer(expressionProgram, producer);
        Assert.Equal(75,BinaryPrimitives.ReadInt32LittleEndian(
            expressionConsumer.AsSpan(4, sizeof(int))));
        foreach (var unsupportedVersion in new[] { 69, 70, 71, 72, 73, 74 })
        {
            var unsupportedConsumer = (byte[])expressionConsumer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(unsupportedConsumer.AsSpan(4, sizeof(int)),
                unsupportedVersion);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(unsupportedConsumer, producer));
        }
        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        Assert.Equal(40, NativeJit.Execute(
            ModuleCompiler.Link(expressionConsumer, producer), runtimePolicy));

        var expressionConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse(UnsafeFieldModuleExpressionConsumerSource), reference, policy);
        Assert.False(expressionConsumerCompilation.HasErrors,
            string.Join(Environment.NewLine, expressionConsumerCompilation.Diagnostics));
        var expressionConsumerProgram = Assert.IsType<BoundProgram>(expressionConsumerCompilation.Program);
        var expressionConsumerModule = ModuleCompiler.CreateConsumer(expressionConsumerProgram, producer);
        Assert.Equal(121, NativeJit.Execute(
            ModuleCompiler.Link(expressionConsumerModule, producer), runtimePolicy));

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
        Assert.True(wrapperOffset >= 0);
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
            Assert.False(mutationCompilation.HasErrors,
                string.Join(Environment.NewLine, mutationCompilation.Diagnostics));
            var mutationProgram = Assert.IsType<BoundProgram>(mutationCompilation.Program);
            var mutationMain = mutationProgram.Functions[mutationProgram.EntryPoint];
            var mutationBlock = Assert.IsType<BoundUnsafe>(mutationMain.Body.Statements[1]);
            var mutationStatement = Assert.IsType<BoundExpressionStatement>(mutationBlock.Body.Statements[0]);
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
            var exception = Record.Exception(
                () => ModuleCompiler.CreateConsumer(forgedMutationProgram, producer));
            Assert.True(exception is ModuleContractException or ArgumentException, exception?.ToString());
        }

        var unrelatedMutationCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                UnsafeOuter outer = MakeOuter(40);
                mut int32 count = 0;
                return unsafe(outer.Value.Value) + count++;
            }
            """), reference, policy);
        Assert.False(unrelatedMutationCompilation.HasErrors,
            string.Join(Environment.NewLine, unrelatedMutationCompilation.Diagnostics));
        var unrelatedMutationConsumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(unrelatedMutationCompilation.Program), producer);
        Assert.Equal(40, NativeJit.Execute(
            ModuleCompiler.Link(unrelatedMutationConsumer, producer), runtimePolicy));

        var wrappedRightCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut UnsafeOuter outer = MakeOuter(40);
                outer.Value = unsafe(new SafeInner(42));
                return 0;
            }
            """), reference, policy);
        Assert.Null(wrappedRightCompilation.Program);
        Assert.Contains(wrappedRightCompilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3028");

        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse(UnsafeFieldModuleConsumerSource), reference, policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumerProgram = Assert.IsType<BoundProgram>(consumerCompilation.Program);
        var main = consumerProgram.Functions[consumerProgram.EntryPoint];
        var unsafeBlock = Assert.IsType<BoundUnsafe>(main.Body.Statements[^1]);
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
        Assert.Equal(75,BinaryPrimitives.ReadInt32LittleEndian(consumer.AsSpan(4, sizeof(int))));
        foreach (var unsupportedVersion in new[] { 69, 70, 71, 72, 73, 74 })
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
        Assert.True(unsafeScopeOffset >= 0);
        Assert.Equal(unsafeScopeOffset, consumer.AsSpan().LastIndexOf(unsafeScope));
        var forgedConsumer = new byte[consumer.Length - unsafeScope.Length];
        consumer.AsSpan(0, unsafeScopeOffset).CopyTo(forgedConsumer);
        consumer.AsSpan(unsafeScopeOffset + unsafeScope.Length)
            .CopyTo(forgedConsumer.AsSpan(unsafeScopeOffset));
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(forgedConsumer, producer));

        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Equal(45, NativeJit.Execute(linked, runtimePolicy));
        Assert.NotEmpty(NativeEmitter.Emit(linked, runtimePolicy));

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
        Assert.False(safeConsumerCompilation.HasErrors,
            string.Join(Environment.NewLine, safeConsumerCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(safeConsumerCompilation.Program), safeReplacement);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
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
        Assert.False(graphCompilation.HasErrors,
            string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(graphCompilation.Program), [provider], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [provider]);
        Assert.Equal(121, await NativeModuleCompiler.RunGraphAsync(
            graphConsumer, [provider], TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

        var helper = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }", policy),
            ["Helper"], "UnsafeFieldExpressionHelper", runtimePolicy);
        var providers = new[] { provider, helper };
        var setCompilation = Compilation.Create(
            SyntaxTree.Parse(UnsafeFieldModuleExpressionConsumerSource),
            NativeModuleCompiler.CreateReferenceSet(providers), policy);
        Assert.False(setCompilation.HasErrors,
            string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            Assert.IsType<BoundProgram>(setCompilation.Program), providers, runtimePolicy);
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, providers);
        Assert.Equal(121, await NativeModuleCompiler.RunConsumerSetAsync(
            consumerSet, providers, TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
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

        var expressionFunction = Assert.Single(
            program.Functions, function => function.Name == "InvokeExpressionScoped");
        var expressionFunctionIndex = program.Functions.IndexOf(expressionFunction);
        var expressionReturn = Assert.IsType<BoundReturn>(expressionFunction.Body.Statements[^1]);
        var expressionScope = Assert.IsType<BoundUnsafeExpression>(expressionReturn.Value);
        var expressionCall = Assert.IsType<BoundCall>(expressionScope.Expression);
        var receiverLoan = Assert.IsType<BoundMutableFieldLoan>(expressionCall.Arguments[0]);

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
        var wrappedReceiverException = Record.Exception(() =>
            ModuleCompiler.CreateProducer(wrappedReceiverProgram, ["InvokeExpressionScoped"]));
        Assert.True(wrappedReceiverException is ModuleContractException or ArgumentException,
            wrappedReceiverException?.ToString());

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
        var remappedReceiverException = Record.Exception(() =>
            ModuleCompiler.CreateProducer(remappedReceiverProgram, ["InvokeExpressionScoped"]));
        Assert.True(remappedReceiverException is ModuleContractException or ArgumentException,
            remappedReceiverException?.ToString());
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateReference(WrapSerializedMutableFieldLoan(producer, receiverLoan)));
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateReference(RemapSerializedCallReceiver(producer, "LoanCounter.NextBy")));

        var unsafeMethodFunction = Assert.Single(
            program.Functions, function => function.Name == "InvokeExpressionScopedUnsafeMethod");
        var unsafeMethodFunctionIndex = program.Functions.IndexOf(unsafeMethodFunction);
        var unsafeMethodReturn = Assert.IsType<BoundReturn>(unsafeMethodFunction.Body.Statements[^1]);
        var unsafeMethodScope = Assert.IsType<BoundUnsafeExpression>(unsafeMethodReturn.Value);
        var unsafeMethodCall = Assert.IsType<BoundCall>(unsafeMethodScope.Expression);
        var unsafeMethodLoan = Assert.IsType<BoundMutableFieldLoan>(unsafeMethodCall.Arguments[0]);
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
        var remappedUnsafeMethodException = Record.Exception(() => ModuleCompiler.CreateProducer(
            remappedUnsafeMethodProgram, ["InvokeExpressionScopedUnsafeMethod"]));
        Assert.True(remappedUnsafeMethodException is ModuleContractException or ArgumentException,
            remappedUnsafeMethodException?.ToString());
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
            var invalidMappingException = Record.Exception(() => ModuleCompiler.CreateProducer(
                invalidMappingProgram, ["InvokeExpressionScopedUnsafeMethod"]));
            Assert.True(invalidMappingException is ModuleContractException or ArgumentException,
                invalidMappingException?.ToString());
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
            var invalidMappingReadException = Record.Exception(
                () => ModuleCompiler.CreateReference(invalidMappingArtifact));
            Assert.True(invalidMappingReadException is ModuleContractException or ModuleFormatException,
                invalidMappingReadException?.ToString());
        }
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateReference(StripSerializedUnsafeExpression(producer, unsafeMethodScope)));
        var unsafeMethodContract = GetModuleStringStartingWith(producer, "LoanCounter.UnsafeNextBy|");
        Assert.Contains("|MutReceiver|", unsafeMethodContract, StringComparison.Ordinal);
        var mismatchedReceiverMarkerException = Record.Exception(() => ModuleCompiler.CreateReference(
            ReplaceModuleString(producer, unsafeMethodContract,
                unsafeMethodContract.Replace("|MutReceiver|", "|", StringComparison.Ordinal))));
        Assert.True(mismatchedReceiverMarkerException is ModuleContractException or ModuleFormatException,
            mismatchedReceiverMarkerException?.ToString());

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
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumerProgram = Assert.IsType<BoundProgram>(consumerCompilation.Program);
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram, producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        Assert.Equal(125, NativeJit.Execute(linked, runtimePolicy));

        var receiverConsumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            struct DiscardedBeforeLoanCounter { public int32 Value; }
            public struct LoanCounter {
                public mut int32 Number;
                public LoanCounter(int32 number) { Number = number; }
                public mut int32 Next() { Number = Number + 1; return Number; }
                public mut int32 NextBy(int32 first, int32 second) {
                    Number = Number + first * 10 + second;
                    return Number;
                }
            }
            public struct UnsafeLeafOwner {
                public unsafe mut LoanCounter Value;
                public UnsafeLeafOwner(LoanCounter value) { unsafe { Value = value; } }
            }
            int32 Main() {
                mut UnsafeLeafOwner owner = new UnsafeLeafOwner(new LoanCounter(40));
                return unsafe(owner.Value.NextBy(second: 1, first: 0));
            }
            """), policy);
        Assert.False(receiverConsumerCompilation.HasErrors,
            string.Join(Environment.NewLine, receiverConsumerCompilation.Diagnostics));
        var receiverConsumerProgram = Assert.IsType<BoundProgram>(receiverConsumerCompilation.Program);
        var unrelatedProducer = ModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }", policy), ["Helper"]);
        var consumerLocalCompilation = Compilation.Create(SyntaxTree.Parse(
            UnsafeMutableFieldLoanModuleSource + """

            int32 Main() {
                mut UnsafeLeafOwner owner = new UnsafeLeafOwner(new LoanCounter(40));
                return unsafe(owner.Value.UnsafeNextBy(second: 1, first: 0));
            }
            """), ModuleCompiler.CreateReference(unrelatedProducer), policy);
        Assert.False(consumerLocalCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerLocalCompilation.Diagnostics));
        var consumerLocalProgram = Assert.IsType<BoundProgram>(consumerLocalCompilation.Program);
        var consumerLocal = ModuleCompiler.CreateConsumer(consumerLocalProgram, unrelatedProducer);
        Assert.Equal(41, NativeJit.Execute(
            ModuleCompiler.Link(consumerLocal, unrelatedProducer), runtimePolicy));
        var consumerLocalMain = Assert.Single(
            consumerLocalProgram.Functions, function => function.Name == "Main");
        var consumerLocalReturn = Assert.IsType<BoundReturn>(consumerLocalMain.Body.Statements[^1]);
        var consumerLocalScope = Assert.IsType<BoundUnsafeExpression>(consumerLocalReturn.Value);
        var strippedConsumerLocal = StripSerializedUnsafeExpression(consumerLocal, consumerLocalScope);
        var strippedConsumerLocalException = Record.Exception(
            () => ModuleCompiler.Link(strippedConsumerLocal, unrelatedProducer));
        Assert.True(strippedConsumerLocalException is ModuleContractException or ModuleFormatException,
            strippedConsumerLocalException?.ToString());
        foreach (var invalidMapping in new[] {
            new[] { 0, 0, 1 },
            new[] { 0, 1, 3 },
        })
        {
            var invalidMappingConsumer = SetSerializedCallParameterMapping(
                consumerLocal, "LoanCounter.UnsafeNextBy", invalidMapping);
            var invalidMappingLinkException = Record.Exception(
                () => ModuleCompiler.Link(invalidMappingConsumer, unrelatedProducer));
            Assert.True(invalidMappingLinkException is ModuleContractException or ModuleFormatException,
                invalidMappingLinkException?.ToString());
        }
        var localReceiverCompilation = Compilation.Create(SyntaxTree.Parse(
            UnsafeMutableFieldLoanModuleSource + """

            int32 Main() {
                mut LoanCounter value = new LoanCounter(40);
                return unsafe(value.UnsafeNextBy(second: 1, first: 0));
            }
            """), ModuleCompiler.CreateReference(unrelatedProducer), policy);
        Assert.False(localReceiverCompilation.HasErrors,
            string.Join(Environment.NewLine, localReceiverCompilation.Diagnostics));
        var localReceiverProgram = Assert.IsType<BoundProgram>(localReceiverCompilation.Program);
        var localReceiverException = Record.Exception(
            () => ModuleCompiler.CreateConsumer(localReceiverProgram, unrelatedProducer));
        Assert.True(localReceiverException is ModuleContractException or ArgumentException,
            localReceiverException?.ToString());
        var receiverConsumer = ModuleCompiler.CreateConsumer(receiverConsumerProgram, unrelatedProducer);
        Assert.Equal(41, NativeJit.Execute(
            ModuleCompiler.Link(receiverConsumer, unrelatedProducer), runtimePolicy));
        var receiverConsumerMain = Assert.Single(
            receiverConsumerProgram.Functions, function => function.Name == "Main");
        var receiverConsumerReturn = Assert.IsType<BoundReturn>(receiverConsumerMain.Body.Statements[^1]);
        var receiverConsumerScope = Assert.IsType<BoundUnsafeExpression>(receiverConsumerReturn.Value);
        var receiverConsumerCall = Assert.IsType<BoundCall>(receiverConsumerScope.Expression);
        var receiverConsumerLoan = Assert.IsType<BoundMutableFieldLoan>(receiverConsumerCall.Arguments[0]);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(
            WrapSerializedMutableFieldLoan(receiverConsumer, receiverConsumerLoan), unrelatedProducer));
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(
            RemapSerializedCallReceiver(receiverConsumer, "LoanCounter.NextBy"), unrelatedProducer));

        foreach (var export in blockExports)
        {
            var function = Assert.Single(program.Functions, function => function.Name == export);
            var functionIndex = program.Functions.IndexOf(function);
            var unsafeBlock = Assert.IsType<BoundUnsafe>(function.Body.Statements[^1]);
            var forgedFunction = function with {
                Body = function.Body with {
                    Statements = function.Body.Statements.SetItem(
                        function.Body.Statements.Length - 1, unsafeBlock.Body),
                },
            };
            var forgedProgram = program with {
                Functions = program.Functions.SetItem(functionIndex, forgedFunction),
            };
            var forgedProgramException = Record.Exception(
                () => ModuleCompiler.CreateProducer(forgedProgram, [export]));
            Assert.True(forgedProgramException is ModuleContractException or ArgumentException,
                forgedProgramException?.ToString());

            var singleExport = ModuleCompiler.CreateProducer(program, [export]);
            var unsafeScope = new byte[1 + (2 * sizeof(int))];
            unsafeScope[0] = 11;
            BinaryPrimitives.WriteInt32LittleEndian(unsafeScope.AsSpan(1), unsafeBlock.Span.Start);
            BinaryPrimitives.WriteInt32LittleEndian(unsafeScope.AsSpan(1 + sizeof(int)),
                unsafeBlock.Span.Length);
            var unsafeScopeOffset = singleExport.AsSpan().IndexOf(unsafeScope);
            Assert.True(unsafeScopeOffset >= 0);
            var forgedArtifact = new byte[singleExport.Length - unsafeScope.Length];
            singleExport.AsSpan(0, unsafeScopeOffset).CopyTo(forgedArtifact);
            singleExport.AsSpan(unsafeScopeOffset + unsafeScope.Length)
                .CopyTo(forgedArtifact.AsSpan(unsafeScopeOffset));
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forgedArtifact));
        }

        foreach (var export in expressionExports)
        {
            var function = Assert.Single(program.Functions, function => function.Name == export);
            var functionIndex = program.Functions.IndexOf(function);
            var returnStatement = Assert.IsType<BoundReturn>(function.Body.Statements[^1]);
            var unsafeExpression = Assert.IsType<BoundUnsafeExpression>(returnStatement.Value);
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
            var forgedProgramException = Record.Exception(
                () => ModuleCompiler.CreateProducer(forgedProgram, [export]));
            Assert.True(forgedProgramException is ModuleContractException or ArgumentException,
                forgedProgramException?.ToString());

            var singleExport = ModuleCompiler.CreateProducer(program, [export]);
            var forgedArtifact = StripSerializedUnsafeExpression(singleExport, unsafeExpression);
            var forgedArtifactException = Record.Exception(
                () => ModuleCompiler.CreateReference(forgedArtifact));
            Assert.True(forgedArtifactException is ModuleFormatException or ModuleContractException,
                forgedArtifactException?.ToString());
        }
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void UnsafeExpressionLoanCannotInvalidateAnAlreadyReservedSibling(BoundRuntimePolicy policy)
    {
        const string Source = """
            public struct LoanCounter {
                public mut int32 Number;
                public LoanCounter(int32 number) { Number = number; }
                public mut int32 Next() { Number = Number + 1; return Number; }
                public mut int32 Add(int32 amount) { Number = Number + amount; return Number; }
            }
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

        Assert.True(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
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
        Assert.False(setCompilation.HasErrors,
            string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            Assert.IsType<BoundProgram>(setCompilation.Program), providers, runtimePolicy);
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, providers);
        Assert.Equal(166, await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

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
        Assert.False(middleCompilation.HasErrors,
            string.Join(Environment.NewLine, middleCompilation.Diagnostics));
        var middle = NativeModuleCompiler.CreateGraphProducer(
            Assert.IsType<BoundProgram>(middleCompilation.Program), ["Forward"],
            "UnsafeLoanMiddle", [provider], runtimePolicy);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Forward(); }"),
            NativeModuleCompiler.CreateGraphReference([middle]), policy);
        Assert.False(rootCompilation.HasErrors,
            string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(rootCompilation.Program), [middle], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(root, [middle, provider]);
        Assert.Equal(166, await NativeModuleCompiler.RunGraphAsync(root, [provider, middle],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
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
        Assert.False(setCompilation.HasErrors,
            string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            Assert.IsType<BoundProgram>(setCompilation.Program), providers, runtimePolicy);
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, providers);
        Assert.Equal(45, await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));
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
        Assert.False(middleCompilation.HasErrors,
            string.Join(Environment.NewLine, middleCompilation.Diagnostics));
        var middle = NativeModuleCompiler.CreateGraphProducer(
            Assert.IsType<BoundProgram>(middleCompilation.Program), ["Forward"],
            "UnsafeFieldMiddle", [provider], runtimePolicy);

        var rootCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Forward(); }"),
            NativeModuleCompiler.CreateGraphReference([middle]), policy);
        Assert.False(rootCompilation.HasErrors,
            string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(rootCompilation.Program), [middle], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(root, [middle, provider]);
        Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateGraph(root, [middle, safeProvider]));
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [provider, middle],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));
    }

    [Fact]
    public void GenericSpecializationPreservesCallerUnsafeRequirement()
    {
        const string declaration = """
            unsafe T Identity<T>(T value) where T : [Copy] { return value; }
            """;

        var safeCall = Compilation.Create(SyntaxTree.Parse(declaration
            + " int32 Main() { return Identity<int32>(42); }"));
        Assert.Null(safeCall.Program);
        Assert.Contains(safeCall.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var authorizedCall = Compilation.Create(SyntaxTree.Parse(declaration
            + " int32 Main() { return unsafe(Identity<int32>(42)); }"));
        Assert.False(authorizedCall.HasErrors, string.Join(Environment.NewLine, authorizedCall.Diagnostics));
    }

    [Fact]
    public void UnsafeStaticOperatorsRequireAuthorizationForBinaryUnaryAndUpdateSyntax()
    {
        const string declaration = """
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
        Assert.Null(safeUse.Program);
        Assert.Equal(3, safeUse.Diagnostics.Count(static diagnostic => diagnostic.Code == "TRM3028"));

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
        Assert.False(authorizedUse.HasErrors, string.Join(Environment.NewLine, authorizedUse.Diagnostics));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task StaticOperatorIdentityAndUnsafeEffectSurviveModuleImportAsync(BoundRuntimePolicy policy)
    {
        const string declaration = """
            public struct Magnitude {
                public int32 Value;
                public Magnitude(int32 value) { Value = value; }
            }
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
        Assert.True(functionNameOffset >= 0);
        using (var reader = new BinaryReader(new MemoryStream(forgedProducer)))
        {
            reader.BaseStream.Position = functionNameOffset + operatorName.Length;
            _ = reader.ReadInt32();
            for (var flag = 0; flag < 4; flag++)
            {
                _ = reader.ReadBoolean();
            }
            var operatorMarkerOffset = (int)reader.BaseStream.Position;
            Assert.True(reader.ReadBoolean());
            forgedProducer[operatorMarkerOffset] = 0;
        }
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forgedProducer));
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.True(reference.Functions.Single(static function => function.Name == "Number.op_LogicalNot").IsOperator);
        Assert.True(reference.Functions.Single(static function => function.Name == "Number.op_Equality").IsOperator);
        Assert.True(reference.Functions.Single(static function => function.Name == "Number.op_Addition").IsOperator);
        Assert.True(reference.Functions.Single(static function => function.Name == "Number.op_Subtraction").IsOperator);
        var unscoped = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                Number left = new Number(40);
                Number right = new Number(2);
                Number difference = left - right;
                return difference.Value;
            }
            """), reference, policy);
        Assert.Null(unscoped.Program);
        Assert.Contains(unscoped.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

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
        Assert.False(client.HasErrors, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(client.Program), producer);
        var malformedConsumer = (byte[])consumer.Clone();
        var canonicalName = Encoding.UTF8.GetBytes("Number.op_Addition");
        var firstNameOffset = malformedConsumer.AsSpan().IndexOf(canonicalName);
        Assert.True(firstNameOffset >= 0);
        var contractNameOffset = firstNameOffset + canonicalName.Length
            + malformedConsumer.AsSpan(firstNameOffset + canonicalName.Length).IndexOf(canonicalName);
        Assert.True(contractNameOffset >= firstNameOffset + canonicalName.Length);
        var malformedName = Encoding.UTF8.GetBytes("Number.op_FakeName");
        Assert.Equal(canonicalName.Length, malformedName.Length);
        malformedName.CopyTo(malformedConsumer.AsSpan(contractNameOffset));
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(malformedConsumer, producer));
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), (NativeRuntimePolicy)policy));

        var runtimePolicy = (NativeRuntimePolicy)policy;
        var nativeProducer = NativeModuleCompiler.CreateProducer(CompileLibrary(declaration, policy),
            ["Magnitude.#ctor", "Number.#ctor", "Number.op_LogicalNot", "Number.op_Equality",
                "Number.op_Addition", "Number.op_Subtraction"], "StaticOperators", runtimePolicy);
        var graphReference = NativeModuleCompiler.CreateGraphReference([nativeProducer]);
        var graphCompilation = Compilation.Create(SyntaxTree.Parse(clientSource), graphReference, policy);
        Assert.False(graphCompilation.HasErrors,
            string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(graphCompilation.Program), [nativeProducer], runtimePolicy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(graphConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

        var helper = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }", policy), ["Helper"],
            "StaticOperatorHelper", runtimePolicy);
        var providers = new[] { nativeProducer, helper };
        var setReference = NativeModuleCompiler.CreateReferenceSet(providers);
        var setCompilation = Compilation.Create(SyntaxTree.Parse(clientSource), setReference, policy);
        Assert.False(setCompilation.HasErrors, string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            Assert.IsType<BoundProgram>(setCompilation.Program), providers, runtimePolicy);
        Assert.Equal(42, await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

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

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CallerUnsafeBinaryOperatorsRoundTripInUnsafeExpressionsAsync(BoundRuntimePolicy policy)
    {
        const string declaration = """
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
        Assert.Null(unscopedCompilation.Program);
        Assert.Contains(unscopedCompilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3028");
        var operandScopedCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                Number left = new Number(1);
                Number right = new Number(2);
                Number result = unsafe(left) + right;
                return result.Value;
            }
            """), reference, policy);
        Assert.Null(operandScopedCompilation.Program);
        Assert.Contains(operandScopedCompilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3028");

        var combine = Assert.Single(providerProgram.Functions, static function => function.Name == "Number.Combine");
        var returned = Assert.IsType<BoundReturn>(combine.Body.Statements[^1]);
        var scope = Assert.IsType<BoundUnsafeExpression>(returned.Value);
        var multiply = Assert.IsType<BoundCall>(scope.Expression);
        Assert.True(providerProgram.Functions[multiply.Function].IsOperator);
        var addition = Assert.IsType<BoundCall>(multiply.Arguments[0]);
        Assert.True(providerProgram.Functions[addition.Function].IsOperator);

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
        Assert.False(semanticCompilation.HasErrors,
            string.Join(Environment.NewLine, semanticCompilation.Diagnostics));
        var semanticConsumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(semanticCompilation.Program), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(semanticConsumer, producer), (NativeRuntimePolicy)policy));

        var runtimePolicy = (NativeRuntimePolicy)policy;
        var nativeProducer = NativeModuleCompiler.CreateProducer(providerProgram, exports,
            "CallerUnsafeBinaryOperators", runtimePolicy);
        var graphCompilation = Compilation.Create(SyntaxTree.Parse(clientSource),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), policy);
        Assert.False(graphCompilation.HasErrors,
            string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(graphCompilation.Program), [nativeProducer], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [nativeProducer]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(
            graphConsumer, [nativeProducer], TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

        var helper = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }", policy), ["Helper"],
            "CallerUnsafeBinaryOperatorHelper", runtimePolicy);
        var providers = new[] { nativeProducer, helper };
        var setCompilation = Compilation.Create(SyntaxTree.Parse(clientSource),
            NativeModuleCompiler.CreateReferenceSet(providers), policy);
        Assert.False(setCompilation.HasErrors,
            string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            Assert.IsType<BoundProgram>(setCompilation.Program), providers, runtimePolicy);
        Assert.Equal(42, await NativeModuleCompiler.RunConsumerSetAsync(
            consumerSet, providers, TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CallerUnsafeOperatorsRoundTripInUnsafeExpressionsAsync(BoundRuntimePolicy policy)
    {
        const string declaration = """
            public struct Magnitude {
                public int32 Value;
                public Magnitude(int32 value) { Value = value; }
            }
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
            public struct SafeHolder {
                public mut Number Value;
                public SafeHolder(Number value) { Value = value; }
            }
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
        var increment = Assert.Single(providerProgram.Functions, static function => function.Name == "Number.Increment");
        var incrementReturn = Assert.IsType<BoundReturn>(increment.Body.Statements[^1]);
        var incrementScope = Assert.IsType<BoundUnsafeExpression>(incrementReturn.Value);
        var incrementUpdate = Assert.IsType<BoundNominalUpdate>(incrementScope.Expression);
        Assert.True(providerProgram.Functions[incrementUpdate.Function].IsOperator);
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
        var forgedUpdateException = Record.Exception(() => ModuleCompiler.CreateReference(
            SetSerializedNominalUpdateOperator(producer, incrementUpdate, TokenKind.MinusMinus)));
        Assert.True(forgedUpdateException is ModuleContractException or ModuleFormatException,
            forgedUpdateException?.ToString());
        var unsafeFieldUpdateException = Record.Exception(() => ModuleCompiler.CreateProducer(
            providerProgram, ["UnsafeHolder.#ctor", "Number.op_Increment", "IncrementUnsafeField"]));
        Assert.True(unsafeFieldUpdateException is ModuleContractException or ModuleFormatException,
            unsafeFieldUpdateException?.ToString());

        var operandScopedCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                Number value = new Number(42);
                Number result = -unsafe(value);
                return result.Value;
            }
            """), reference, policy);
        Assert.Null(operandScopedCompilation.Program);
        Assert.Contains(operandScopedCompilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3028");

        var unscopedUpdate = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut Number value = new Number(41);
                value++;
                return value.Value;
            }
            """), reference, policy);
        Assert.Null(unscopedUpdate.Program);
        Assert.Contains(unscopedUpdate.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3028");

        var semanticCompilation = Compilation.Create(SyntaxTree.Parse(clientSource), reference, policy);
        Assert.False(semanticCompilation.HasErrors,
            string.Join(Environment.NewLine, semanticCompilation.Diagnostics));
        var semanticConsumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(semanticCompilation.Program), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(semanticConsumer, producer), (NativeRuntimePolicy)policy));

        var runtimePolicy = (NativeRuntimePolicy)policy;
        var nativeProducer = NativeModuleCompiler.CreateProducer(providerProgram, exports,
            "CallerUnsafeOperators", runtimePolicy);
        var graphCompilation = Compilation.Create(SyntaxTree.Parse(clientSource),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), policy);
        Assert.False(graphCompilation.HasErrors,
            string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(graphCompilation.Program), [nativeProducer], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [nativeProducer]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(
            graphConsumer, [nativeProducer], TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

        var helper = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }", policy), ["Helper"],
            "CallerUnsafeOperatorHelper", runtimePolicy);
        var providers = new[] { nativeProducer, helper };
        var setCompilation = Compilation.Create(SyntaxTree.Parse(clientSource),
            NativeModuleCompiler.CreateReferenceSet(providers), policy);
        Assert.False(setCompilation.HasErrors,
            string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            Assert.IsType<BoundProgram>(setCompilation.Program), providers, runtimePolicy);
        Assert.Equal(42, await NativeModuleCompiler.RunConsumerSetAsync(
            consumerSet, providers, TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));
    }

    [Fact]
    public void SemanticModulesRoundTripTopLevelCallerUnsafeContracts()
    {
        var providerProgram = CompileLibrary("public unsafe int32 Danger() { return 42; }");
        var producer = ModuleCompiler.CreateProducer(providerProgram, ["Danger"]);
        var reference = ModuleCompiler.CreateReference(producer);

        var safeCall = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Danger(); }"), reference);
        Assert.Null(safeCall.Program);
        Assert.Contains(safeCall.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var scopedCall = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { unsafe { return Danger(); } }"), reference);
        Assert.False(scopedCall.HasErrors, string.Join(Environment.NewLine, scopedCall.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(scopedCall.Program), producer);
        foreach (var unsupportedVersion in new[] { 69, 70, 71, 72, 73, 74 })
        {
            var unsupportedConsumer = (byte[])consumer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(unsupportedConsumer.AsSpan(4, sizeof(int)),
                unsupportedVersion);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(unsupportedConsumer, producer));
        }
        var linked = ModuleCompiler.Link(consumer, producer);
        SemanticVerifier.Verify(linked);
        Assert.Equal(42, NativeJit.Execute(linked, NativeRuntimePolicy.FailFast));
        var nativeProducer = NativeModuleCompiler.CreateProducer(providerProgram, ["Danger"],
            "UnsafeProvider", NativeRuntimePolicy.FailFast);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(scopedCall.Program), nativeProducer, NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.Validate(nativeConsumer, nativeProducer);

        var expressionScopedCall = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return unsafe(Danger() + 1); }"), reference);
        Assert.False(expressionScopedCall.HasErrors,
            string.Join(Environment.NewLine, expressionScopedCall.Diagnostics));
        var expressionConsumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(expressionScopedCall.Program), producer);
        var expressionLinked = ModuleCompiler.Link(expressionConsumer, producer);
        SemanticVerifier.Verify(expressionLinked);
        Assert.Equal(43, NativeJit.Execute(expressionLinked, NativeRuntimePolicy.FailFast));

        var nestedExpressionScope = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return unsafe(Danger()) + 1; }"), reference);
        Assert.False(nestedExpressionScope.HasErrors,
            string.Join(Environment.NewLine, nestedExpressionScope.Diagnostics));
        var nestedExpressionConsumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(nestedExpressionScope.Program), producer);
        Assert.Equal(43, NativeJit.Execute(ModuleCompiler.Link(nestedExpressionConsumer, producer),
            NativeRuntimePolicy.FailFast));

        var leakedExpressionScope = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { int32 value = unsafe(Danger()); return Danger(); }"), reference);
        Assert.Null(leakedExpressionScope.Program);
        Assert.Contains(leakedExpressionScope.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3028");

        var expressionNativeConsumer = NativeModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(expressionScopedCall.Program), nativeProducer, NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.Validate(expressionNativeConsumer, nativeProducer);

        var safeProducer = ModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Danger() { return 42; }"), ["Danger"]);
        var safeReference = ModuleCompiler.CreateReference(safeProducer);
        var safeConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Danger(); }"), safeReference);
        Assert.False(safeConsumerCompilation.HasErrors,
            string.Join(Environment.NewLine, safeConsumerCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(safeConsumerCompilation.Program), safeProducer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(safeConsumer, safeProducer),
            NativeRuntimePolicy.FailFast));
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, safeProducer),
            NativeRuntimePolicy.FailFast));

        var changedUnsafeProducer = ModuleCompiler.CreateProducer(
            CompileLibrary("public unsafe int32 Danger() { return 7; }"), ["Danger"]);
        Assert.Equal(7, NativeJit.Execute(ModuleCompiler.Link(consumer, changedUnsafeProducer),
            NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void SemanticModulesRoundTripCallerUnsafeReadonlyCopyMethods(BoundRuntimePolicy policy)
    {
        const string Source = """
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
        var read = Assert.Single(reference.Functions, static function => function.Name == "Value.Read");
        Assert.True(read.RequiresUnsafeCaller);
        Assert.False(read.IsNoThrow);
        Assert.False(Assert.Single(reference.Functions,
            static function => function.Name == "Value.SafeRead").RequiresUnsafeCaller);

        var unscoped = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Value value = new Value(42); return value.Read(); }"),
            reference, policy);
        Assert.Null(unscoped.Program);
        Assert.Contains(unscoped.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), reference, policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        foreach (var unsupportedVersion in new[] { 69, 70, 71, 72, 73, 74 })
        {
            var unsupportedConsumer = (byte[])consumer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(unsupportedConsumer.AsSpan(4, sizeof(int)),
                unsupportedVersion);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(unsupportedConsumer, producer));
        }

        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Equal(85, NativeJit.Execute(linked, runtimePolicy));

        var safeReplacementSource = Source
            .Replace("public unsafe int32 Read(int32 offset)", "public int32 Read(int32 offset)",
                StringComparison.Ordinal)
            .Replace("return Number + offset;", "return Number + offset + 1;", StringComparison.Ordinal)
            .Replace("unsafe { return Read(0); }", "return Read(0);", StringComparison.Ordinal);
        var safeReplacement = ModuleCompiler.CreateProducer(
            CompileLibrary(safeReplacementSource, policy), exports);
        var compatible = ModuleCompiler.Link(consumer, safeReplacement);
        Assert.Equal(87, NativeJit.Execute(compatible, runtimePolicy));

        var safeProvider = ModuleCompiler.CreateProducer(
            CompileLibrary(safeReplacementSource, policy), exports);
        var safeConsumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Value value = new Value(42); return value.Read(1); }"),
            ModuleCompiler.CreateReference(safeProvider), policy);
        Assert.False(safeConsumerCompilation.HasErrors,
            string.Join(Environment.NewLine, safeConsumerCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(safeConsumerCompilation.Program), safeProvider);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void SemanticModulesRoundTripCallerUnsafeReadonlyCopyInstanceMethodExpressions(
        BoundRuntimePolicy policy)
    {
        const string Source = """
            public struct Pair {
                public int32 Left;
                public int32 Right;
                public Pair(int32 left, int32 right) { Left = left; Right = right; }
            }
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
        Assert.Equal(75, BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, sizeof(int))));
        var computeContract = GetModuleStringStartingWith(producer, "Value.Compute|");
        var forgedProducer = ReplaceModuleString(producer, "Value.Compute", "Value.$producer.Compute");
        forgedProducer = ReplaceModuleString(forgedProducer, computeContract,
            computeContract.Replace("Value.Compute", "Value.$producer.Compute", StringComparison.Ordinal));
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forgedProducer));

        var reference = ModuleCompiler.CreateReference(producer);
        var compute = Assert.Single(reference.Functions, static function => function.Name == "Value.Compute");
        Assert.True(compute.RequiresUnsafeCaller);
        Assert.False(compute.IsStatic);
        Assert.False(compute.Variables[0].IsMutable);
        Assert.Equal(PrimitiveType.Int32, compute.ReturnType);

        var unscoped = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Value value = new Value(1); return value.Compute(2, 3); }"),
            reference, policy);
        Assert.Null(unscoped.Program);
        Assert.Contains(unscoped.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");
        var scopedOnly = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Value value = new Value(1); int32 scoped = unsafe(value.Compute(2, 3)); return value.Compute(2, 3); }"),
            reference, policy);
        Assert.Null(scopedOnly.Program);
        Assert.Contains(scopedOnly.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), reference, policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumerProgram = Assert.IsType<BoundProgram>(consumerCompilation.Program);
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram, producer);
        foreach (var unsupportedVersion in new[] { 69, 70, 71, 72, 73, 74 })
        {
            var unsupportedConsumer = (byte[])consumer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(unsupportedConsumer.AsSpan(4, sizeof(int)),
                unsupportedVersion);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(unsupportedConsumer, producer));
        }

        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Equal(5660, NativeJit.Execute(linked, runtimePolicy));
        Assert.NotEmpty(NativeEmitter.Emit(linked, runtimePolicy));

        var identityCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Value value = new Value(1); return unsafe(value.Compute(second: 3, first: 2)); }"),
            reference, policy);
        Assert.False(identityCompilation.HasErrors,
            string.Join(Environment.NewLine, identityCompilation.Diagnostics));
        var identityProgram = Assert.IsType<BoundProgram>(identityCompilation.Program);
        var identityMain = Assert.Single(identityProgram.Functions, static function => function.Name == "Main");
        var identityReturn = Assert.IsType<BoundReturn>(identityMain.Body.Statements[^1]);
        var identityWrapper = Assert.IsType<BoundUnsafeExpression>(identityReturn.Value);
        var identityCall = Assert.IsType<BoundCall>(identityWrapper.Expression);
        Assert.Equal(3, identityCall.Arguments.Length);
        var computeFunctionIndex = providerProgram.Functions.IndexOf(
            Assert.Single(providerProgram.Functions, static function => function.Name == "Value.Compute"));
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
        Assert.Equal(5762, NativeJit.Execute(ModuleCompiler.Link(consumer, safeReplacement), runtimePolicy));

        var safeConsumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Value value = new Value(1); return value.Compute(2, 3); }"),
            ModuleCompiler.CreateReference(safeReplacement), policy);
        Assert.False(safeConsumerCompilation.HasErrors,
            string.Join(Environment.NewLine, safeConsumerCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(safeConsumerCompilation.Program), safeReplacement);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void SemanticModulesRoundTripCallerUnsafeReadonlyCopyGetters(BoundRuntimePolicy policy)
    {
        const string Source = """
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
        Assert.Equal(75,BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, sizeof(int))));

        var reference = ModuleCompiler.CreateReference(producer);
        var getter = Assert.Single(reference.Functions, static function => function.Name == "Value.#get:Read");
        Assert.Equal("Read", getter.PropertyName);
        Assert.True(getter.RequiresUnsafeCaller);
        Assert.True(getter.IsNoThrow);
        Assert.Null(Assert.Single(reference.Functions,
            static function => function.Name == "Value.get_Read").PropertyName);
        Assert.False(Assert.Single(reference.Functions,
            static function => function.Name == "Value.SafeRead").RequiresUnsafeCaller);

        var unscoped = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Value value = new Value(42); return value.Read; }"), reference, policy);
        Assert.Null(unscoped.Program);
        Assert.Contains(unscoped.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var scopedOnly = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Value value = new Value(42); int32 scoped = unsafe(value.Read); return value.Read; }"),
            reference, policy);
        Assert.Null(scopedOnly.Program);
        Assert.Contains(scopedOnly.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), reference, policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);

        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Equal(169, NativeJit.Execute(linked, runtimePolicy));
        Assert.NotEmpty(NativeEmitter.Emit(linked, runtimePolicy));

        var getterOnlyCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Value value = new Value(42); return unsafe(value.Read); }"),
            reference, policy);
        Assert.False(getterOnlyCompilation.HasErrors,
            string.Join(Environment.NewLine, getterOnlyCompilation.Diagnostics));
        var getterOnlyProgram = Assert.IsType<BoundProgram>(getterOnlyCompilation.Program);
        var getterOnlyMain = Assert.Single(getterOnlyProgram.Functions,
            static function => function.Name == "Main");
        var getterOnlyReturn = Assert.IsType<BoundReturn>(
            getterOnlyMain.Body.Statements[getterOnlyMain.Body.Statements.Length - 1]);
        var getterOnlyWrapper = Assert.IsType<BoundUnsafeExpression>(getterOnlyReturn.Value);
        var getterOnlyConsumer = ModuleCompiler.CreateConsumer(getterOnlyProgram, producer);
        Assert.Equal(42, NativeJit.Execute(
            ModuleCompiler.Link(getterOnlyConsumer, producer), runtimePolicy));

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
        Assert.True(getterWrapperOffset >= 0);
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
        Assert.Equal(172, NativeJit.Execute(ModuleCompiler.Link(consumer, safeReplacement), runtimePolicy));

        var safeConsumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { Value value = new Value(42); return value.Read; }"),
            ModuleCompiler.CreateReference(safeReplacement), policy);
        Assert.False(safeConsumerCompilation.HasErrors,
            string.Join(Environment.NewLine, safeConsumerCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(safeConsumerCompilation.Program), safeReplacement);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void SemanticModulesRoundTripCallerUnsafeStaticCopyGetters(BoundRuntimePolicy policy)
    {
        const string Source = """
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
        Assert.Equal(75,BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, sizeof(int))));

        var reference = ModuleCompiler.CreateReference(producer);
        var getter = Assert.Single(reference.Functions,
            static function => function.Name == "Value.#get:Read");
        Assert.Equal("Read", getter.PropertyName);
        Assert.True(getter.IsStatic);
        Assert.Equal(0, getter.ParameterCount);
        Assert.True(getter.RequiresUnsafeCaller);
        Assert.True(getter.IsNoThrow);

        var unscoped = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Value.Read; }"),
            reference, policy);
        Assert.Null(unscoped.Program);
        Assert.Contains(unscoped.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), reference, policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        foreach (var unsupportedVersion in new[] { 69, 70, 71, 72, 73, 74 })
        {
            var unsupportedConsumer = (byte[])consumer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(unsupportedConsumer.AsSpan(4, sizeof(int)),
                unsupportedVersion);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(unsupportedConsumer, producer));
        }

        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Equal(126, NativeJit.Execute(linked, runtimePolicy));
        Assert.NotEmpty(NativeEmitter.Emit(linked, runtimePolicy));

        var safeReplacementSource = Source
            .Replace("[NoThrow] unsafe get", "[NoThrow] get", StringComparison.Ordinal)
            .Replace("local = 42;", "local = 43;", StringComparison.Ordinal)
            .Replace("unsafe { return Read; }", "return Read;", StringComparison.Ordinal);
        var safeReplacement = ModuleCompiler.CreateProducer(
            CompileLibrary(safeReplacementSource, policy), exports);
        Assert.Equal(129, NativeJit.Execute(ModuleCompiler.Link(consumer, safeReplacement), runtimePolicy));

        var safeConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Value.Read; }"),
            ModuleCompiler.CreateReference(safeReplacement), policy);
        Assert.False(safeConsumerCompilation.HasErrors,
            string.Join(Environment.NewLine, safeConsumerCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(safeConsumerCompilation.Program), safeReplacement);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void SemanticModulesRoundTripCallerUnsafeStaticMethodExpressions(BoundRuntimePolicy policy)
    {
        const string source = """
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
        Assert.Equal(75,BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, sizeof(int))));

        var reference = ModuleCompiler.CreateReference(producer);
        foreach (var name in exports[..3])
        {
            var method = Assert.Single(reference.Functions, function => function.Name == name);
            Assert.True(method.IsStatic);
            Assert.True(method.RequiresUnsafeCaller);
            Assert.Null(method.PropertyName);
            Assert.False(method.IsOperator);
        }

        const string consumerSource = """
            int32 Main() {
                require unsafe(ScalarOps.Echo(0.0)) == float64(0);
                return unsafe(Top(0) + ScalarOps.Add(right: 2, left: 40));
            }
            """;
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(consumerSource), reference, policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumerProgram = Assert.IsType<BoundProgram>(consumerCompilation.Program);
        var main = Assert.Single(consumerProgram.Functions, static function => function.Name == "Main");
        var returned = Assert.IsType<BoundReturn>(main.Body.Statements[1]);
        var unsafeExpression = Assert.IsType<BoundUnsafeExpression>(returned.Value);
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram, producer);
        Assert.Equal(75,BinaryPrimitives.ReadInt32LittleEndian(consumer.AsSpan(4, sizeof(int))));
        foreach (var unsupportedVersion in new[] { 69, 70, 71, 72, 73, 74 })
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
        Assert.True(wrapperOffset >= 0);
        var strippedConsumer = new byte[consumer.Length - wrapperPrefix.Length];
        consumer.AsSpan(0, wrapperOffset).CopyTo(strippedConsumer);
        consumer.AsSpan(wrapperOffset + wrapperPrefix.Length)
            .CopyTo(strippedConsumer.AsSpan(wrapperOffset));
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(strippedConsumer, producer));

        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), runtimePolicy));
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(
            ModuleCompiler.CreateConsumer(
                Assert.IsType<BoundProgram>(Compilation.Create(SyntaxTree.Parse(
                    "int32 Main() { return Forward(40, 2); }"), reference, policy).Program), producer),
            producer), runtimePolicy));

        var safeSource = source
            .Replace("static unsafe int32 Add", "static int32 Add", StringComparison.Ordinal)
            .Replace("static unsafe int32 Zero", "static int32 Zero", StringComparison.Ordinal)
            .Replace("static unsafe float64 Echo", "static float64 Echo", StringComparison.Ordinal)
            .Replace("[NoThrow] unsafe get", "[NoThrow] get", StringComparison.Ordinal)
            .Replace("public unsafe int32 ReadInstance", "public int32 ReadInstance", StringComparison.Ordinal)
            .Replace("public unsafe int32 Top", "public int32 Top", StringComparison.Ordinal);
        var safeProducer = ModuleCompiler.CreateProducer(CompileLibrary(safeSource, policy), exports);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, safeProducer), runtimePolicy));
        var safeConsumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return ScalarOps.Add(40, 2); }"),
            ModuleCompiler.CreateReference(safeProducer), policy);
        Assert.False(safeConsumerCompilation.HasErrors,
            string.Join(Environment.NewLine, safeConsumerCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(safeConsumerCompilation.Program), safeProducer);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void SemanticModulesRoundTripCallerUnsafeCopyValueExpressions(BoundRuntimePolicy policy)
    {
        string[] exports = [
            "MakePair", "MakeEnvelope", "MakeFloatBox", "Value.Transform", "Value.Wrap", "ExpressionOps.CreatePair",
            "ExpressionOps.Sum", "ExpressionOps.ZeroPair", "ExpressionOps.EchoFloat", "Identity",
        ];
        var producer = ModuleCompiler.CreateProducer(
            CompileLibrary(CopyValueExpressionProviderSource, policy), exports);
        Assert.Equal(75,BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, sizeof(int))));
        var reference = ModuleCompiler.CreateReference(producer);
        var compilation = Compilation.Create(
            SyntaxTree.Parse(CopyValueExpressionConsumerSource), reference, policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumerProgram = Assert.IsType<BoundProgram>(compilation.Program);
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram, producer);
        foreach (var unsupportedVersion in new[] { 69, 70, 71, 72, 73, 74 })
        {
            var unsupportedConsumer = (byte[])consumer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(unsupportedConsumer.AsSpan(4, sizeof(int)),
                unsupportedVersion);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(unsupportedConsumer, producer));
        }

        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        Assert.Equal(12436, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), runtimePolicy));

        var safeSource = CopyValueExpressionProviderSource.Replace("unsafe ", "", StringComparison.Ordinal);
        var safeReplacement = ModuleCompiler.CreateProducer(CompileLibrary(safeSource, policy), exports);
        Assert.Equal(12436, NativeJit.Execute(ModuleCompiler.Link(consumer, safeReplacement), runtimePolicy));
        var safeCompilation = Compilation.Create(
            SyntaxTree.Parse(CopyValueExpressionConsumerSource),
            ModuleCompiler.CreateReference(safeReplacement), policy);
        Assert.False(safeCompilation.HasErrors,
            string.Join(Environment.NewLine, safeCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(safeCompilation.Program), safeReplacement);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NativeModuleSetsAndGraphsRoundTripCallerUnsafeStaticMethodExpressionsAsync(
        BoundRuntimePolicy policy)
    {
        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        const string source = """
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
        Assert.False(setCompilation.HasErrors,
            string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            Assert.IsType<BoundProgram>(setCompilation.Program), providers, runtimePolicy);
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, providers);
        Assert.Equal(42, await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

        var middleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Forward() { return unsafe(ScalarOps.Add(40, 2)); }
            """), NativeModuleCompiler.CreateGraphReference([provider]), policy);
        Assert.False(middleCompilation.HasErrors,
            string.Join(Environment.NewLine, middleCompilation.Diagnostics));
        var middle = NativeModuleCompiler.CreateGraphProducer(
            Assert.IsType<BoundProgram>(middleCompilation.Program), ["Forward"],
            "UnsafeStaticMiddle", [provider], runtimePolicy);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Forward(); }"),
            NativeModuleCompiler.CreateGraphReference([middle]), policy);
        Assert.False(rootCompilation.HasErrors,
            string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(rootCompilation.Program), [middle], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(root, [middle, provider]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [provider, middle],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
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
        Assert.False(graphCompilation.HasErrors,
            string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(graphCompilation.Program), [provider], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [provider]);
        Assert.Equal(12436, await NativeModuleCompiler.RunGraphAsync(graphConsumer, [provider],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

        var helper = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }", policy),
            ["Helper"], "CopyValueExpressionHelper", runtimePolicy);
        var providers = new[] { provider, helper };
        var setCompilation = Compilation.Create(
            SyntaxTree.Parse(CopyValueExpressionConsumerSource),
            NativeModuleCompiler.CreateReferenceSet(providers), policy);
        Assert.False(setCompilation.HasErrors,
            string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            Assert.IsType<BoundProgram>(setCompilation.Program), providers, runtimePolicy);
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, providers);
        Assert.Equal(12436, await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

        var safeSource = CopyValueExpressionProviderSource.Replace("unsafe ", "", StringComparison.Ordinal);
        var safeReplacement = NativeModuleCompiler.CreateProducer(
            CompileLibrary(safeSource, policy), exports, "CopyValueExpressionProvider", runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [safeReplacement]);
        Assert.Equal(12436, await NativeModuleCompiler.RunGraphAsync(graphConsumer, [safeReplacement],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));
        var replacementProviders = new[] { safeReplacement, helper };
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, replacementProviders);
        Assert.Equal(12436, await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, replacementProviders,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
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
        Assert.Equal(75,BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, sizeof(int))));
        var reference = ModuleCompiler.CreateReference(producer);

        foreach (var (name, isStatic, parameterCount) in new[]
        {
            ("GetterOwner.#get:InstancePair", false, 1),
            ("GetterOwner.#get:InstanceOdd", false, 1),
            ("GetterOwner.#get:StaticTriple", true, 0),
            ("GetterOwner.#get:StaticNested", true, 0),
        })
        {
            var getter = Assert.Single(reference.Functions, function => function.Name == name);
            Assert.Equal(name[(name.IndexOf("#get:", StringComparison.Ordinal) + 5)..], getter.PropertyName);
            Assert.Equal(isStatic, getter.IsStatic);
            Assert.Equal(parameterCount, getter.ParameterCount);
            Assert.True(getter.RequiresUnsafeCaller, name);
            Assert.False(getter.IsNoThrow);
        }

        foreach (var source in new[]
        {
            "int32 Main() { return GetterOwner.StaticTriple.First; }",
            "int32 Main() { GetterOwner owner = MakeGetterOwner(); return owner.InstanceOdd.First; }",
        })
        {
            var unscoped = Compilation.Create(SyntaxTree.Parse(source), reference, policy);
            Assert.Null(unscoped.Program);
            Assert.Contains(unscoped.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");
        }

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), reference, policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        foreach (var unsupportedVersion in new[] { 69, 70, 71, 72, 73, 74 })
        {
            var unsupportedConsumer = (byte[])consumer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(unsupportedConsumer.AsSpan(4, sizeof(int)),
                unsupportedVersion);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(unsupportedConsumer, producer));
        }

        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        Assert.Equal(142, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), runtimePolicy));

        var safeReplacementSource = CopyValueGetterSource
            .Replace("unsafe get", "get", StringComparison.Ordinal)
            .Replace("new Pair(20, 21)", "new Pair(22, 21)", StringComparison.Ordinal);
        var safeReplacement = ModuleCompiler.CreateProducer(
            CompileLibrary(safeReplacementSource, policy), exports);
        Assert.Equal(144, NativeJit.Execute(ModuleCompiler.Link(consumer, safeReplacement), runtimePolicy));

        var safeConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return GetterOwner.StaticNested.Payload.First; }"),
            ModuleCompiler.CreateReference(safeReplacement), policy);
        Assert.False(safeConsumerCompilation.HasErrors,
            string.Join(Environment.NewLine, safeConsumerCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(safeConsumerCompilation.Program), safeReplacement);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));
    }

    [Fact]
    public void CallerUnsafeGetterPersistenceRejectsNonExportableCopyResultLayouts()
    {
        const string source = """
            public struct Hidden {
                int32 Secret;
                public Hidden(int32 secret) { Secret = secret; }
            }
            public struct Value {
                public int32 Placeholder;
                public Hidden Read { unsafe get { return new Hidden(42); } }
            }
            """;
        var program = CompileLibrary(source);

        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(program, ["Value.#get:Read"]));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void SemanticModulesRoundTripCallerUnsafeStaticCopyMethods(BoundRuntimePolicy policy)
    {
        const string Source = """
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
        var read = Assert.Single(reference.Functions, static function => function.Name == "Value.Read");
        var add = Assert.Single(reference.Functions, static function => function.Name == "Value.Add");
        Assert.True(read.IsStatic);
        Assert.Equal(0, read.ParameterCount);
        Assert.True(read.RequiresUnsafeCaller);
        Assert.True(add.IsStatic);
        Assert.Equal(1, add.ParameterCount);
        Assert.True(add.RequiresUnsafeCaller);
        Assert.False(Assert.Single(reference.Functions,
            static function => function.Name == "Value.SafeAdd").RequiresUnsafeCaller);

        var unscoped = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Value.Read(); }"), reference, policy);
        Assert.Null(unscoped.Program);
        Assert.Contains(unscoped.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), reference, policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        foreach (var unsupportedVersion in new[] { 69, 70, 71, 72, 73, 74 })
        {
            var unsupportedConsumer = (byte[])consumer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(unsupportedConsumer.AsSpan(4, sizeof(int)),
                unsupportedVersion);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(unsupportedConsumer, producer));
        }

        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Equal(85, NativeJit.Execute(linked, runtimePolicy));

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
        Assert.Equal(88, NativeJit.Execute(ModuleCompiler.Link(consumer, safeReplacement), runtimePolicy));

        var safeConsumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Value.Read() + Value.Add(1); }"),
            ModuleCompiler.CreateReference(safeReplacement), policy);
        Assert.False(safeConsumerCompilation.HasErrors,
            string.Join(Environment.NewLine, safeConsumerCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(safeConsumerCompilation.Program), safeReplacement);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void SemanticModulesRoundTripCallerUnsafeCopyValueMethods(BoundRuntimePolicy policy)
    {
        string[] exports = ["MakePair", "MakeValue", "MakeEnvelope", "Value.Echo",
            "Value.Transform", "Value.Wrap", "Value.SafeEcho"];
        var producer = ModuleCompiler.CreateProducer(CompileLibrary(CopyValueMethodSource, policy), exports);
        var reference = ModuleCompiler.CreateReference(producer);
        var echo = Assert.Single(reference.Functions, static function => function.Name == "Value.Echo");
        Assert.False(echo.IsStatic);
        Assert.Equal(2, echo.ParameterCount);
        Assert.Equal(echo.ReturnType, echo.Variables[1].Type);
        Assert.True(echo.RequiresUnsafeCaller);
        var transform = Assert.Single(reference.Functions,
            static function => function.Name == "Value.Transform");
        Assert.True(transform.IsStatic);
        Assert.Equal(7, transform.ParameterCount);
        Assert.True((int)transform.ReturnType >= 100);
        Assert.True((int)transform.Variables[0].Type >= 100);
        Assert.True(transform.RequiresUnsafeCaller);
        Assert.False(Assert.Single(reference.Functions,
            static function => function.Name == "Value.SafeEcho").RequiresUnsafeCaller);

        var compilation = Compilation.Create(SyntaxTree.Parse(CopyValueConsumerSource), reference, policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(compilation.Program), producer);
        foreach (var unsupportedVersion in new[] { 69, 70, 71, 72, 73, 74 })
        {
            var unsupportedConsumer = (byte[])consumer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(unsupportedConsumer.AsSpan(4, sizeof(int)),
                unsupportedVersion);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(unsupportedConsumer, producer));
        }

        var runtimePolicy = policy == BoundRuntimePolicy.CheckedExceptions
            ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        Assert.Equal(23608, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), runtimePolicy));

        var safeReplacement = ModuleCompiler.CreateProducer(
            CompileLibrary(SafeCopyValueMethodSource, policy), exports);
        Assert.Equal(23611, NativeJit.Execute(ModuleCompiler.Link(consumer, safeReplacement), runtimePolicy));

        var safeConsumerCompilation = Compilation.Create(SyntaxTree.Parse(CopyValueConsumerSource),
            ModuleCompiler.CreateReference(safeReplacement), policy);
        Assert.False(safeConsumerCompilation.HasErrors,
            string.Join(Environment.NewLine, safeConsumerCompilation.Diagnostics));
        var safeConsumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(safeConsumerCompilation.Program), safeReplacement);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(safeConsumer, producer));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NativeModuleSetsAndGraphsRoundTripCallerUnsafeReadonlyCopyMethodsAsync(
        BoundRuntimePolicy policy)
    {
        const string Source = """
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
        Assert.False(graphCompilation.HasErrors,
            string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(graphCompilation.Program), [producer], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [producer]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(graphConsumer, [producer],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

        var safeReplacementSource = Source
            .Replace("public unsafe int32 Read(int32 offset)", "public int32 Read(int32 offset)",
                StringComparison.Ordinal)
            .Replace("return Number + offset;", "return Number + offset + 1;", StringComparison.Ordinal);
        var safeReplacement = NativeModuleCompiler.CreateProducer(
            CompileLibrary(safeReplacementSource, policy), ["Value.#ctor", "Value.Read"],
            "UnsafeValue", runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [safeReplacement]);
        Assert.Equal(43, await NativeModuleCompiler.RunGraphAsync(graphConsumer, [safeReplacement],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

        var setReference = NativeModuleCompiler.CreateReferenceSet(providers);
        var setCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), setReference, policy);
        Assert.False(setCompilation.HasErrors,
            string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            Assert.IsType<BoundProgram>(setCompilation.Program), providers, runtimePolicy);
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, providers);
        Assert.Equal(42, await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));
        var replacementProviders = new[] { safeReplacement, helper };
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, replacementProviders);
        Assert.Equal(43, await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, replacementProviders,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NativeModuleSetsAndGraphsRoundTripCallerUnsafeReadonlyCopyGettersAsync(
        BoundRuntimePolicy policy)
    {
        const string Source = """
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
        Assert.False(graphCompilation.HasErrors,
            string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(graphCompilation.Program), [producer], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [producer]);
        Assert.Equal(168, await NativeModuleCompiler.RunGraphAsync(graphConsumer, [producer],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

        var safeReplacementSource = Source.Replace("unsafe get", "get", StringComparison.Ordinal)
            .Replace("unsafe { return Read; }", "return Read;", StringComparison.Ordinal)
            .Replace("return Number;", "return Number + 1;", StringComparison.Ordinal);
        var safeReplacement = NativeModuleCompiler.CreateProducer(
            CompileLibrary(safeReplacementSource, policy), exports, "UnsafeGetterValue", runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [safeReplacement]);
        Assert.Equal(171, await NativeModuleCompiler.RunGraphAsync(graphConsumer, [safeReplacement],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

        var setReference = NativeModuleCompiler.CreateReferenceSet(providers);
        var setCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), setReference, policy);
        Assert.False(setCompilation.HasErrors,
            string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            Assert.IsType<BoundProgram>(setCompilation.Program), providers, runtimePolicy);
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, providers);
        Assert.Equal(168, await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

        var replacementProviders = new[] { safeReplacement, helper };
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, replacementProviders);
        Assert.Equal(171, await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, replacementProviders,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NativeModuleSetsAndGraphsRoundTripCallerUnsafeStaticCopyGettersAsync(
        BoundRuntimePolicy policy)
    {
        const string Source = """
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
        Assert.False(graphCompilation.HasErrors,
            string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(graphCompilation.Program), [producer], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [producer]);
        Assert.Equal(126, await NativeModuleCompiler.RunGraphAsync(graphConsumer, [producer],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

        var safeReplacementSource = Source.Replace("unsafe get", "get", StringComparison.Ordinal)
            .Replace("unsafe { return Read; }", "return Read;", StringComparison.Ordinal)
            .Replace("return 42;", "return 43;", StringComparison.Ordinal);
        var safeReplacement = NativeModuleCompiler.CreateProducer(
            CompileLibrary(safeReplacementSource, policy), exports, "UnsafeStaticGetterValue", runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [safeReplacement]);
        Assert.Equal(129, await NativeModuleCompiler.RunGraphAsync(graphConsumer, [safeReplacement],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

        var setReference = NativeModuleCompiler.CreateReferenceSet(providers);
        var setCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), setReference, policy);
        Assert.False(setCompilation.HasErrors,
            string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            Assert.IsType<BoundProgram>(setCompilation.Program), providers, runtimePolicy);
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, providers);
        Assert.Equal(126, await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

        var replacementProviders = new[] { safeReplacement, helper };
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, replacementProviders);
        Assert.Equal(129, await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, replacementProviders,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
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
        Assert.False(middleCompilation.HasErrors,
            string.Join(Environment.NewLine, middleCompilation.Diagnostics));
        var middle = NativeModuleCompiler.CreateGraphProducer(
            Assert.IsType<BoundProgram>(middleCompilation.Program),
            ["ForwardNested", "ForwardOdd"], "CopyGetterMiddle", [provider], runtimePolicy);
        var providers = new[] { provider, middle };

        var graphCompilation = Compilation.Create(SyntaxTree.Parse(CopyValueGetterConsumerSource),
            NativeModuleCompiler.CreateGraphReference(providers), policy);
        Assert.False(graphCompilation.HasErrors,
            string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(graphCompilation.Program), providers, runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, providers);
        Assert.Equal(209, await NativeModuleCompiler.RunGraphAsync(graphConsumer, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

        var helper = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }", policy),
            ["Helper"], "CopyGetterSetHelper", runtimePolicy);
        var setProviders = new[] { provider, helper };
        var setReference = NativeModuleCompiler.CreateReferenceSet(setProviders);
        var setCompilation = Compilation.Create(SyntaxTree.Parse(SetConsumerSource),
            setReference, policy);
        Assert.False(setCompilation.HasErrors,
            string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            Assert.IsType<BoundProgram>(setCompilation.Program), setProviders, runtimePolicy);
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, setProviders);
        Assert.Equal(142, await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, setProviders,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

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
        Assert.False(directGraphCompilation.HasErrors,
            string.Join(Environment.NewLine, directGraphCompilation.Diagnostics));
        var directGraphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(directGraphCompilation.Program), setProviders, runtimePolicy);
        Assert.Equal(142, await NativeModuleCompiler.RunGraphAsync(directGraphConsumer, setProviders,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));
        NativeModuleCompiler.ValidateGraph(directGraphConsumer, [safeReplacement, helper]);
        Assert.Equal(144, await NativeModuleCompiler.RunGraphAsync(directGraphConsumer,
            [safeReplacement, helper], TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

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
        Assert.False(nestedForwardCompilation.HasErrors,
            string.Join(Environment.NewLine, nestedForwardCompilation.Diagnostics));
        var nestedForwardConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(nestedForwardCompilation.Program), providers, runtimePolicy);
        Assert.Equal(20, await NativeModuleCompiler.RunGraphAsync(nestedForwardConsumer, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));
        Assert.Equal(22, await NativeModuleCompiler.RunGraphAsync(nestedForwardConsumer,
            replacementGraphProviders, TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

        var oddForwardCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                GetterOwner owner = MakeGetterOwner();
                unsafe {
                    Odd value = ForwardOdd(owner);
                    return int32(value.First);
                }
            }
            """), NativeModuleCompiler.CreateGraphReference(providers), policy);
        Assert.False(oddForwardCompilation.HasErrors,
            string.Join(Environment.NewLine, oddForwardCompilation.Diagnostics));
        var oddForwardConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(oddForwardCompilation.Program), providers, runtimePolicy);
        Assert.Equal(5, await NativeModuleCompiler.RunGraphAsync(oddForwardConsumer, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));
        Assert.Equal(5, await NativeModuleCompiler.RunGraphAsync(oddForwardConsumer,
            replacementGraphProviders, TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

        Assert.Equal(211, await NativeModuleCompiler.RunGraphAsync(graphConsumer, replacementGraphProviders,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));
        var replacementSetProviders = new[] { safeReplacement, helper };
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, replacementSetProviders);
        Assert.Equal(144, await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, replacementSetProviders,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NativeModuleSetsAndGraphsRoundTripCallerUnsafeStaticCopyMethodsAsync(
        BoundRuntimePolicy policy)
    {
        const string Source = """
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
        Assert.False(graphCompilation.HasErrors,
            string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(graphCompilation.Program), [producer], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [producer]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(graphConsumer, [producer],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

        var safeReplacement = NativeModuleCompiler.CreateProducer(
            CompileLibrary(Source.Replace("public static unsafe int32 Read()",
                "public static int32 Read()", StringComparison.Ordinal)
                .Replace("return 42;", "return 43;", StringComparison.Ordinal), policy),
            ["Value.Read"], "UnsafeStaticValue", runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [safeReplacement]);
        Assert.Equal(43, await NativeModuleCompiler.RunGraphAsync(graphConsumer, [safeReplacement],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

        var setReference = NativeModuleCompiler.CreateReferenceSet(providers);
        var setCompilation = Compilation.Create(SyntaxTree.Parse(ConsumerSource), setReference, policy);
        Assert.False(setCompilation.HasErrors,
            string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            Assert.IsType<BoundProgram>(setCompilation.Program), providers, runtimePolicy);
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, providers);
        Assert.Equal(42, await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));
        var replacementProviders = new[] { safeReplacement, helper };
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, replacementProviders);
        Assert.Equal(43, await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, replacementProviders,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
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
        Assert.False(graphCompilation.HasErrors,
            string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(graphCompilation.Program), [producer], runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [producer]);
        Assert.Equal(23608, await NativeModuleCompiler.RunGraphAsync(graphConsumer, [producer],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

        var safeReplacement = NativeModuleCompiler.CreateProducer(
            CompileLibrary(SafeCopyValueMethodSource, policy), exports, "UnsafeCopyValue", runtimePolicy);
        NativeModuleCompiler.ValidateGraph(graphConsumer, [safeReplacement]);
        Assert.Equal(23611, await NativeModuleCompiler.RunGraphAsync(graphConsumer, [safeReplacement],
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));

        var setReference = NativeModuleCompiler.CreateReferenceSet(providers);
        var setCompilation = Compilation.Create(
            SyntaxTree.Parse(CopyValueConsumerSource), setReference, policy);
        Assert.False(setCompilation.HasErrors,
            string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var consumerSet = NativeModuleCompiler.CreateConsumerSet(
            Assert.IsType<BoundProgram>(setCompilation.Program), providers, runtimePolicy);
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, providers);
        Assert.Equal(23608, await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, providers,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));
        var replacementProviders = new[] { safeReplacement, helper };
        NativeModuleCompiler.ValidateConsumerSet(consumerSet, replacementProviders);
        Assert.Equal(23611, await NativeModuleCompiler.RunConsumerSetAsync(consumerSet, replacementProviders,
            TimeSpan.FromSeconds(10), runtimePolicy).ConfigureAwait(true));
    }

    [Fact]
    public void PersistedWritersRejectUnsupportedUnsafeMemberKinds()
    {
        var privateLayoutStaticMethod = CompileLibrary("""
            public struct Value {
                private int32 Number;
                public static unsafe int32 Read() { return 42; }
            }
            """);
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(privateLayoutStaticMethod, ["Value.Read"]));

        var uniqueOwnerStaticMethod = CompileLibrary("""
            public unique struct Value {
                public int32 Placeholder;
                public static unsafe int32 Read() { return 42; }
            }
            """);
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(uniqueOwnerStaticMethod, ["Value.Read"]));

        var mutableMethod = CompileLibrary("""
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
            public struct Hidden {
                private mut int32 Number;
                public Hidden(int32 number) { Number = number; }
            }
            public struct Value {
                public int32 Marker;
                public static unsafe int32 Read(Hidden value) { return 42; }
            }
            """,
            """
            public unique struct Owned {
                public mut int32 Number;
                public Owned(int32 number) { Number = number; }
            }
            public struct Value {
                public int32 Marker;
                public static unsafe int32 Read(Owned value) { return value.Number; }
            }
            """,
            """
            public struct Value {
                public int32 Marker;
                public static unsafe int32 Read(OwnedInt32Buffer buffer) { return 42; }
            }
            """,
            """
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

    [Fact]
    public void SemanticModuleReadersReconstructCallerUnsafeAuthorizationFromUnsafeBlocks()
    {
        const string source = "int32 Main() { unsafe { return Danger(); } }";
        var producer = ModuleCompiler.CreateProducer(
            CompileLibrary("public unsafe int32 Danger() { return 42; }"), ["Danger"]);
        var compilation = Compilation.Create(SyntaxTree.Parse(source), ModuleCompiler.CreateReference(producer));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var main = program.Functions[program.EntryPoint];
        var unsafeBlock = Assert.IsType<BoundUnsafe>(Assert.Single(main.Body.Statements));

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
        Assert.True(unsafeScopeOffset >= 0);
        Assert.Equal(unsafeScopeOffset, consumer.AsSpan().LastIndexOf(unsafeScope));

        var forgedConsumer = new byte[consumer.Length - unsafeScope.Length];
        consumer.AsSpan(0, unsafeScopeOffset).CopyTo(forgedConsumer);
        consumer.AsSpan(unsafeScopeOffset + unsafeScope.Length)
            .CopyTo(forgedConsumer.AsSpan(unsafeScopeOffset));
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(forgedConsumer, producer));
    }

    [Fact]
    public async Task SemanticModuleGraphPersistsUnsafeImportsWithinUnsafeBlocksAsync()
    {
        var policy = NativeRuntimePolicy.FailFast;
        var leaf = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public unsafe int32 Danger() { return 42; }"), ["Danger"], "UnsafeLeaf", policy);
        var middleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 SafeWrapper() { unsafe { return Danger(); } }"),
            NativeModuleCompiler.CreateGraphReference([leaf]), (BoundRuntimePolicy)policy);
        Assert.False(middleCompilation.HasErrors,
            string.Join(Environment.NewLine, middleCompilation.Diagnostics));
        var middle = NativeModuleCompiler.CreateGraphProducer(
            Assert.IsType<BoundProgram>(middleCompilation.Program), ["SafeWrapper"], "SafeMiddle", [leaf], policy);

        var rootCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return SafeWrapper(); }"),
            NativeModuleCompiler.CreateGraphReference([middle]), (BoundRuntimePolicy)policy);
        Assert.False(rootCompilation.HasErrors, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(rootCompilation.Program), [middle], policy);
        NativeModuleCompiler.ValidateGraph(root, [middle, leaf]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [leaf, middle],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
    }

    [Fact]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));

        var consumer = NativeModuleCompiler.CreateConsumerSet(
            Assert.IsType<BoundProgram>(compilation.Program), providers, policy);
        NativeModuleCompiler.ValidateConsumerSet(consumer, providers);
        Assert.Contains("UnsafeSetProvider", NativeModuleCompiler.InspectConsumerSet(consumer).Dependencies);
        Assert.Equal(42, await NativeModuleCompiler.RunConsumerSetAsync(consumer, providers,
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
    }

    [Fact]
    public void GraphReadersRejectCallerUnsafeMemberContracts()
    {
        var policy = NativeRuntimePolicy.FailFast;
        var dependency = NativeModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 0; }"), ["Helper"], "SafeDependency", policy);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public struct Box {
                public int32 Placeholder;
                public static bool Danger(out int32 value) { value = 42; return true; }
            }
            """), NativeModuleCompiler.CreateGraphReference([dependency]), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var graph = NativeModuleCompiler.CreateGraphProducer(
            Assert.IsType<BoundProgram>(compilation.Program), ["Box.Danger"], "GraphProvider", [dependency], policy);
        var forged = ForgeCallerUnsafeMemberGraphPackage(graph, "Box.Danger");

        Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.CreateGraphReference([forged]));
        Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.InspectGraphArtifact(forged));

        var uniqueCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public unique struct Owned {
                public mut int32 Number;
                public Owned(int32 number) { Number = number; }
            }
            public struct Box {
                public int32 Placeholder;
                public static int32 Danger(int32 value) {
                    Owned owned = new Owned(value);
                    return owned.Number;
                }
            }
            """), NativeModuleCompiler.CreateGraphReference([dependency]), (BoundRuntimePolicy)policy);
        Assert.False(uniqueCompilation.HasErrors,
            string.Join(Environment.NewLine, uniqueCompilation.Diagnostics));
        var uniqueGraph = NativeModuleCompiler.CreateGraphProducer(
            Assert.IsType<BoundProgram>(uniqueCompilation.Program), ["Box.Danger"],
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

    [Fact]
    public void SemanticReadersBoundRepeatedNestedCopySignatureValidation()
    {
        const int MaximumLayer = 24;
        var layerNames = Enumerable.Range(0, MaximumLayer + 1)
            .Select(static index => "CopyLayer" + index).ToArray();
        var structures = layerNames.Select((name, index) => index == MaximumLayer
            ? $"public struct {name} {{ public mut int32 Left; public mut int32 Right; "
                + $"public {name}(int32 left, int32 right) {{ Left = left; Right = right; }} }}"
            : $"public struct {name} {{ public int32 Left; public int32 Right; }}");
        var touchFunctions = layerNames.Select((name, index) =>
            $"public int32 Touch{index}({name} value) {{ return 0; }}").ToArray();
        var source = string.Join(Environment.NewLine, structures.Concat(touchFunctions)) + """

            public CopyLayer24 MakeLayer() { return new CopyLayer24(1, 2); }

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
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var forged = ForgeRepeatedNestedCopyModule(consumer, layerNames);

        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(forged, producer));
        var repeatedSupportedCopyLayout = ForgeRepeatedNestedCopyModule(
            consumer, layerNames, makeLeafPrivate: false);
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.Link(repeatedSupportedCopyLayout, producer));
    }

    private static byte[] ForgeRepeatedNestedCopyModule(byte[] module, string[] layerNames,
        bool makeLeafPrivate = true)
    {
        var forged = (byte[])module.Clone();
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
            offset += sizeof(int) + (sizeof(byte) * 4);
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

    [Fact]
    public void TrRmPreservesCallerUnsafeObligationsAndRejectsCalls()
    {
        var declaration = CompileLibrary(
            "[NoThrow] public unsafe int32 Danger() { return 42; }"
            + " [DoesNotReturn] public unsafe void Never() { while (true) { } }");
        var loadedUnsafe = TrRmExecutableCompiler.Load(
            TrRmExecutableCompiler.Emit(declaration, "unsafe-contract"));
        var unsafeFunction = Assert.Single(loadedUnsafe.Functions,
            static function => function.Name == "Danger");
        Assert.True(unsafeFunction.IsNoThrow);
        Assert.False(unsafeFunction.DoesNotReturn);
        Assert.True(unsafeFunction.RequiresUnsafeCaller);
        var nonReturningFunction = Assert.Single(loadedUnsafe.Functions,
            static function => function.Name == "Never");
        Assert.False(nonReturningFunction.IsNoThrow);
        Assert.True(nonReturningFunction.DoesNotReturn);
        Assert.True(nonReturningFunction.RequiresUnsafeCaller);

        var safeDeclaration = CompileLibrary("[NoThrow] public int32 Danger() { return 42; }");
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(safeDeclaration, "safe-contract"));
        var function = Assert.Single(loaded.Functions);
        Assert.True(function.IsNoThrow);
        Assert.False(function.RequiresUnsafeCaller);

        var caller = Compilation.Create(SyntaxTree.Parse(
            "unsafe int32 Danger() { return 42; } int32 Main() { return unsafe(Danger()); }"));
        Assert.False(caller.HasErrors, string.Join(Environment.NewLine, caller.Diagnostics));
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(
            Assert.IsType<BoundProgram>(caller.Program), "unsafe-call"));

        var scopedCaller = Compilation.Create(SyntaxTree.Parse(
            "unsafe int32 Danger() { return 42; }"
            + " int32 Main() { unsafe { return Danger(); } }"));
        Assert.False(scopedCaller.HasErrors, string.Join(Environment.NewLine, scopedCaller.Diagnostics));
        var executable = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(
            Assert.IsType<BoundProgram>(scopedCaller.Program), "unsafe-block-call"));
        Assert.Equal(42, NativeJit.Execute(executable, NativeRuntimePolicy.FailFast));
    }

    [Fact]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(
            Assert.IsType<BoundProgram>(compilation.Program), "nested-unsafe-regions");
        Assert.Equal(42, NativeJit.Execute(TrRmExecutableCompiler.Load(artifact),
            NativeRuntimePolicy.FailFast));
    }

    [Fact]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(
            Assert.IsType<BoundProgram>(compilation.Program), "unsafe-nested-loop-transfers");
        Assert.Equal(3, NativeJit.Execute(TrRmExecutableCompiler.Load(artifact),
            NativeRuntimePolicy.FailFast));
    }

    [Fact]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(
            Assert.IsType<BoundProgram>(compilation.Program), "unsafe-terminal-assignment-prefix");
        Assert.Equal(42, NativeJit.Execute(TrRmExecutableCompiler.Load(artifact),
            NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData("break;")]
    [InlineData("continue;")]
    [InlineData("break outer;")]
    [InlineData("continue outer;")]
    public void TrRmRejectsLoopTransfersThatLeaveUnsafeScope(string transfer)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { mut int32 value = 0;"
            + " outer: while (value < 1) { while (value < 1) { unsafe { "
            + transfer + " } } } return 42; }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(
            Assert.IsType<BoundProgram>(compilation.Program), "unsafe-loop-transfer"));
    }

    [Fact]
    public void TrRmUnsafeRegionPreservesReservedBodyLayoutBits()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "unsafe int32 Danger() { return 42; }"
            + " int32 Main() { unsafe { return Danger(); } }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(
            Assert.IsType<BoundProgram>(compilation.Program), "unsafe-region-layout");
        var bodyOffset = BodyRowOffset(artifact, 2);
        Assert.Equal(0x08, artifact[bodyOffset] & 0xFC);

        var invalidForm = (byte[])artifact.Clone();
        invalidForm[bodyOffset] |= 0x04;
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(invalidForm));

        var reservedBit = (byte[])artifact.Clone();
        reservedBit[bodyOffset] |= 0x10;
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(reservedBit));
    }

    [Fact]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(
            Assert.IsType<BoundProgram>(compilation.Program), "unsafe-unscoped-uncalled-indexed");
        Assert.Equal(0x08, artifact[BodyRowOffset(artifact, 2)] & 0xFC);
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
        Assert.NotEqual(0, contractRow);

        var (contractOffset, contractCount) = FindSection(artifact, 0x1B);
        var contractData = contractOffset + ((contractCount + 1) * sizeof(uint));
        var contractRowOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            artifact.AsSpan(contractOffset + ((contractRow - 1) * sizeof(uint)))));
        var effectsMaskOffset = contractData + contractRowOffset + 8;
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(
            artifact.AsSpan(effectsMaskOffset)));
        BinaryPrimitives.WriteUInt32LittleEndian(artifact.AsSpan(effectsMaskOffset), 5);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }

    [Fact]
    public void TrRmUnsafeRegionRequiresAValidExitBlock()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "unsafe int32 Danger() { return 42; }"
            + " int32 Main() { unsafe { return Danger(); } }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(
            Assert.IsType<BoundProgram>(compilation.Program), "unsafe-region-exit");
        var malformed = (byte[])artifact.Clone();
        var regionOffset = RegionTableOffset(malformed, 2);
        var width = 1 << (malformed[BodyRowOffset(malformed, 2)] & 3);
        var header = ReadCompactCount(malformed, regionOffset + 1 + width, width);
        WriteCompactCount(malformed, regionOffset + 1 + (2 * width), width, header);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    [Fact]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(
            Assert.IsType<BoundProgram>(compilation.Program), "unsafe-region-entry");
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

    [Fact]
    public void TrRmUnsafeRegionRejectsEdgesThatBypassItsDesignatedExit()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { mut int32 value = 0; unsafe { value = 1; } return value; }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(
            Assert.IsType<BoundProgram>(compilation.Program), "unsafe-region-designated-exit");
        var malformed = (byte[])artifact.Clone();
        var bodyOffset = BodyRowOffset(malformed, 1);
        var regionOffset = RegionTableOffset(malformed, 1);
        var width = 1 << (malformed[bodyOffset] & 3);
        WriteCompactCount(malformed, regionOffset + 1 + (2 * width), width, 1);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    [Fact]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(
            Assert.IsType<BoundProgram>(compilation.Program), "unsafe-region-parent-exit");
        var malformed = (byte[])artifact.Clone();
        var bodyOffset = BodyRowOffset(malformed, 2);
        var regionOffset = RegionTableOffset(malformed, 2);
        var width = 1 << (malformed[bodyOffset] & 3);
        var nestedUnsafeRegion = regionOffset + 1 + (4 * width);
        Assert.Equal(6, malformed[nestedUnsafeRegion]);
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

    [Fact]
    public void TrRmAndSemanticModulesRejectUnsupportedUnsafeFieldContracts()
    {
        var program = CompileLibrary("""
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
            public struct Counter {
                public mut int32 Number;
                public Counter(int32 number) { Number = number; }
                public mut int32 Next() { Number = Number + 1; return Number; }
            }
            public struct Owner {
                public unsafe mut Counter Value;
                public Owner(Counter value) { unsafe { Value = value; } }
            }
            public int32 Invoke() {
                mut Owner owner = new Owner(new Counter(41));
                unsafe { return owner.Value.Next(); }
            }
            """);
        Assert.NotEmpty(ModuleCompiler.CreateProducer(fieldLoanProgram, ["Invoke"]));

        var safeFieldLoanProgram = CompileLibrary("""
            public struct Counter {
                public mut int32 Number;
                public Counter(int32 number) { Number = number; }
                public mut int32 Next() { Number = Number + 1; return Number; }
            }
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
            public struct Counter {
                public mut int32 Number;
                public Counter(int32 number) { Number = number; }
                public mut int32 Next() { Number = Number + 1; return Number; }
            }
            public struct Inner {
                public mut Counter Value;
                public Inner(Counter value) { unsafe { Value = value; } }
            }
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
            offset += sizeof(int) + (sizeof(byte) * 4);
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
        Assert.True(contractTextStart >= sizeof(int));
        var contractLengthOffset = contractTextStart - sizeof(int);
        var contractTextLength = BinaryPrimitives.ReadInt32LittleEndian(
            contract.AsSpan(contractLengthOffset, sizeof(int)));
        var contractText = Encoding.UTF8.GetString(contract, contractTextStart, contractTextLength);
        if (mutateContract is not null)
        {
            var mutatedContract = mutateContract(contractText);
            Assert.NotEqual(contractText, mutatedContract);
            contractText = mutatedContract;
        }
        var unsafeContractText = Encoding.UTF8.GetBytes(contractText + "|CallerUnsafe");
        var growth = unsafeContractText.Length - contractTextLength;
        var bodyNameStart = contract.AsSpan().LastIndexOf(name);
        Assert.True(bodyNameStart > contractTextStart);
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
