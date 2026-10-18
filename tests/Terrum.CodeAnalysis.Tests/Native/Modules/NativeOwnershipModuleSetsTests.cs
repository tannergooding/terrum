// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Native.Modules;

public sealed class NativeOwnershipModuleSetsTests
{
    private const string First = """
        [Blittable] [Layout(LayoutKind.Sequential)] public unique struct FirstTicket {
            public int32 Value;
            [NoThrow] public FirstTicket(int32 value) { Value = value; }
            [NoThrow] [Cleanup] public void Close() {}
        }
        public FirstTicket CreateFirst() { return new FirstTicket(20); }
        public int32 ConsumeFirst(FirstTicket ticket) { return ticket.Value; }
        """;

    private const string Second = """
        [Blittable] [Layout(LayoutKind.Sequential)] public unique struct SecondTicket {
            public int64 Value;
            public uint64 Marker;
            [NoThrow] public SecondTicket(int64 value) {
                Value = value;
                Marker = uint64.MaxValue;
            }
            [NoThrow] [Cleanup] public void Close() {}
        }
        public bool TrySecond(bool success, out SecondTicket ticket) {
            ticket = new SecondTicket(22);
            return success;
        }
        public bool TrySecondInit(bool success, init ref<SecondTicket> ticket) {
            ticket = new SecondTicket(2);
            return success;
        }
        public SecondTicket CreateSecond() { return new SecondTicket(0); }
        public int32 ConsumeSecond(SecondTicket ticket) {
            require ticket.Marker == uint64.MaxValue;
            return int32(ticket.Value);
        }
        """;
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task OwnedCallsAndOutputsSurviveIndependentProviderReplacementAsync(NativeRuntimePolicy policy)
    {
        var first = Producer(First, ["CreateFirst", "ConsumeFirst", "FirstTicket.Close"], "First", policy);
        string[] secondExports = ["TrySecond", "TrySecondInit", "CreateSecond", "ConsumeSecond", "SecondTicket.Close"];
        var second = Producer(Second, secondExports, "Second", policy);
        var boundPolicy = policy == NativeRuntimePolicy.FailFast
            ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            bool RejectLocal(out SecondTicket ticket) {
                ticket = CreateSecond();
                require ticket.Marker == uint64.MaxValue;
                SecondTicket staged = move ticket;
                ticket = move staged;
                return false;
            }
            int32 Main() {
                require !RejectLocal(out var absent);
                FirstTicket first = CreateFirst();
                mut SecondTicket second = CreateSecond();
                require !TrySecond(false, out second);
                require TrySecond(true, out second);
                if (TrySecondInit(false, init var initialized)) { return 1; }
                require TrySecondInit(true, init initialized);
                return ConsumeFirst(move first) + ConsumeSecond(move second)
                    + ConsumeSecond(move initialized) - 2;
            }
            """), NativeModuleCompiler.CreateReferenceSet([second, first]), boundPolicy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumerSet(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(),
            [first, second], policy);
        var original = (byte[])consumer.Clone();
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumer, [second, first],
            TimeSpan.FromSeconds(10), policy), Is.EqualTo(42));

        var firstReplacement = Producer(First.Replace("new FirstTicket(20)", "new FirstTicket(21)", StringComparison.Ordinal),
            ["CreateFirst", "ConsumeFirst", "FirstTicket.Close"], "First", policy);
        var secondReplacement = Producer(Second.Replace("new SecondTicket(22)", "new SecondTicket(23)", StringComparison.Ordinal),
            secondExports, "Second", policy);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumer, [firstReplacement, second],
            TimeSpan.FromSeconds(10), policy), Is.EqualTo(43));
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumer, [secondReplacement, first],
            TimeSpan.FromSeconds(10), policy), Is.EqualTo(43));
        Assert.That(consumer, Is.EqualTo(original));
    }

    private static byte[] Producer(string source, string[] exports, string identity, NativeRuntimePolicy policy)
    {
        var boundPolicy = policy == NativeRuntimePolicy.FailFast
            ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), boundPolicy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(),
            exports, identity, policy);
    }
}
