// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Text;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmDoForTests
{
    private static BoundProgram Compile(string source,
        BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
    }
    [TestCase("int32 Main() { mut int32 x = 0; do { x++; } while (false); return x == 1 ? 42 : 0; }")]
    [TestCase("int32 Main() { mut int32 x = 0; do { int32 local = x++; if (local == 0) { continue; } } while (x < 2); return x == 2 ? 42 : 0; }")]
    [TestCase("int32 Main() { mut int32 x = 0; do { x++; if (x == 2) { break; } } while (x < 5); return x == 2 ? 42 : 0; }")]
    [TestCase("int32 Main() { mut int32 i = 0; for (i = 0; i < 3; i++) { if (i == 1) { continue; } } return i == 3 ? 42 : 0; }")]
    [TestCase("int32 Main() { mut int32 total = 0; for (mut int32 i = 0; i < 3; i++) { int32 local = i; total += local; } return total == 3 ? 42 : 0; }")]
    [TestCase("int32 Main() { mut int32 result = 0; for (mut int32 i = 0; i < 3; i++) { result += i; if (i == 1) { break; } } return result == 1 ? 42 : 0; }")]
    [TestCase("int32 Main() { mut int32 i = 0; mut int32 total = 0; for (i = 0; i < 3; i++) { mut int32 j = 0; do { j++; if (j == 1) { continue; } total++; } while (j < 2); } return total == 3 ? 42 : 0; }")]
    [TestCase("int32 Main() { mut int32 x = 0; for (mut int32 i = 0; i < 3; i++) { if (i == 1) { continue; } x += i; } return x == 2 ? 42 : 0; }")]
    [TestCase("int32 Main() { mut int32 x = 0; do { bool b = true; char16 c = char16(42); uint32 u = uint32(42); int64 s = 42; uint64 l = 42; x = b && c == char16(42) && u == uint32(42) && s == 42 && l == 42 ? 42 : 0; } while (false); return x; }")]
    [TestCase("int32 Main() { mut int32 i = 0; mut int32 result = 0; for (i = Start(); Check(i); i = Next(i)) { result += i; if (i == 0) { continue; } } return i == 2 && result == 1 ? 42 : 0; } int32 Start() { return 0; } bool Check(int32 x) { return x < 2; } int32 Next(int32 x) { return x + 1; }")]
    [TestCase("int32 Main() { mut int32 x = 0; do { x = Next(x); } while (Check(x)); return x == 2 ? 42 : 0; } int32 Next(int32 x) { return x + 1; } bool Check(int32 x) { return x < 2; }")]
    [TestCase("int32 Main() { mut int32 n = 0; mut int32 seen = 0; for (mut int32 i = 0; i < 3; i++) { do { n++; if (n == 1) { continue; } seen++; } while (false); if (i == 1) { continue; } seen++; } return n == 3 && seen == 4 ? 42 : 0; }")]
    [TestCase("int32 Main() { mut int32 i = 0; mut int32 sum = 0; for (i = 0; i < 3; i++) { mut int32 iteration = i; sum += iteration; } do { int32 iteration = sum; sum += iteration; } while (false); return sum == 6 ? 42 : 0; }")]
    [TestCase("int32 Main() { mut int32 x = 0; mut int32 y = 0; for (x = Step(x); x < 3 && Step(x) < 3; x = Step(x)) { y += Choose(x++, x); if (x == 2) { continue; } } return y == 12 && x == 3 ? 42 : 0; } int32 Choose(int32 a, int32 b) { return a * 10 + b; } int32 Step(int32 a) { return a + 1; }")]
    [TestCase("int32 Main() { mut int32 i = 0; for (i = 0; i < 2; i = Next(i)) { Touch(i); if (i == 0) { continue; } } return i == 2 ? 42 : 0; } void Touch(int32 x) { } int32 Next(int32 x) { return x + 1; }")]
    [TestCase("int32 Main() { mut int32 x = 0; do { x++; } while (x++ < 2 && x < 4); return x == 4 ? 42 : 0; }")]
    [TestCase("int32 Main() { mut int32 x = 0; for (x = Init(); Check(x) && x < 4; x = Next(x)) { Touch(x); if (x == 1) { continue; } } return x == 3 ? 42 : 0; } int32 Init() { return 0; } bool Check(int32 x) { return x < 3; } void Touch(int32 x) { } int32 Next(int32 x) { return x + 1; }")]
    [TestCase("int32 Main() { mut int32 x = 0; for (x = 0; x < 3; x++) { if (x == 2) { return 42; } } return 0; }")]
    [TestCase("int32 Main() { mut int32 x = 0; do { if (x++ == 1) { return 42; } } while (x < 3); return 0; }")]
    [TestCase("int32 Main() { mut int32 count = 0; for (mut int32 i = 0; i < 3; i++) { mut int32 j = 0; while (j < 2) { j++; if (j == 1) { continue; } count++; } if (i == 1) { continue; } count++; } return count == 5 ? 42 : 0; }")]
    [TestCase("int32 Main() { mut int32 x = 0; for (x = 0; x < 2; x = 1 / 0) { if (x == 0) { break; } x++; } return 42; }")]
    [TestCase("int32 Main() { mut int32 x = 0; for (x = 0; x < 2; x = 1 / 0) { if (x == 0) { return 42; } x++; } return 0; }")]
    [TestCase("int32 Main() { mut int32 x = 0; do { x++; } while (false && x / 0 == 0); return x == 1 ? 42 : 0; }")]
    [TestCase("int32 Main() { mut int32 x = 0; for (x = 0; x < 1; x++) { bool b = true; char16 c = char16(42); uint32 u = uint32(42); int64 s = 42; uint64 l = 42; if (b && c == char16(42) && u == uint32(42) && s == 42 && l == 42) { x += 41; } } return x; }")]
    [TestCase("int32 Main() { mut int32 x = 0; for (;;) { if (x++ == 2) { break; } continue; } return x == 3 ? 42 : 0; }")]
    [TestCase("int32 Main() { mut int32 x = 0; for (; x < 3; x = Pair(x++, true ? x++ : x)) { continue; } return x == 12 ? 42 : 0; } int32 Pair(int32 a, int32 b) { return a * 10 + b; }")]
    [TestCase("int32 Main() { mut int32 x = 0; do { { mut int32 local = 1; x += local++; } } while (x < 3); return x == 3 ? 42 : 0; }")]
    [TestCase("int32 Main() { mut int32 x = 0; do { x++; } while (Pair(x++, x) < 23); return x == 4 ? 42 : 0; } int32 Pair(int32 a, int32 b) { return a * 10 + b; }")]
    [TestCase("int32 Main() { mut uint64 x = 18446744073709551613; for (; x < 18446744073709551615; x++) { if (x == 18446744073709551613) { continue; } } return x == 18446744073709551615 ? 42 : 0; }")]
    [TestCase("int32 Main() { mut int64 x = -9223372036854775808; do { x++; } while (x < -9223372036854775806); return x == -9223372036854775806 ? 42 : 0; }")]
    [TestCase("int32 Main() { mut uint32 x = 4294967293; for (; x < 4294967295; x++) { continue; } return x == 4294967295 ? 42 : 0; }")]
    [TestCase("int32 Main() { for (mut bool flag = true; flag; flag = false) { continue; } return 42; }")]
    [TestCase("[NoThrow] int32 Main() { mut bool flag = true; do { flag = false; } while (flag); return 42; }")]
    public void LoopsPreserveNativeAndLoadedScalarBehavior(string source)
    {
        var original = Compile(source);
        var bytes = TrRmExecutableCompiler.Emit(original, "do-for");
        TrRmStructuralMetadataValidator.ValidateCompact(bytes);
        var loaded = TrRmExecutableCompiler.Load(bytes);
        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void CheckedExceptionPolicyIsNotPersisted()
    {
        var original = Compile("int32 Main() { mut int32 x = 0; do { x++; } while (x < 1); return x; }",
            BoundRuntimePolicy.CheckedExceptions);
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(original, "checked-do"));
    }
    [TestCase(42,"int32 Main() { while (false) { } return 42; }")]
    [TestCase(42,"int32 Main() { mut int32 result = 0; for (result = Start(); false; result = Fail()) { } return result; } int32 Start() { return 42; } int32 Fail() { require false; return 0; }")]
    public void EmptyFalsePreTestLoopsEraseWithoutChangingInitializerOrder(int expected, string source)
    {
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "empty-false-loop");
        Assert.That(IndexedLoopRegionCount(artifact, 1), Is.EqualTo(0));
        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }
    [TestCase(42,"int32 Main() { mut int32 value = 0; do { value = 42; } while (false); return value; }")]
    [TestCase(42,"int32 Main() { mut int32 value = 0; do { if (Choose()) { int32 local = Compute(40, 2); value = local; } else { value = 13; } } while (false); return value; } bool Choose() { return true; } int32 Compute(int32 left, int32 right) { return left + right; }")]
    [TestCase(13,"int32 Main() { mut int32 value = 0; do { if (Choose()) { int32 local = Compute(40, 2); value = local; } else { value = 13; } } while (false); return value; } bool Choose() { return false; } int32 Compute(int32 left, int32 right) { return left + right; }")]
    [TestCase(42,"int32 Main() { mut int32 value = 0; do { do { value++; } while (false); value++; } while (false); return value == 2 ? 42 : 0; }")]
    [TestCase(42,"int32 Main() { mut int32 value = 0; do { while (false) { } value = 42; } while (false); return value; }")]
    [TestCase(42,"unsafe int32 Danger() { return 42; } int32 Main() { mut int32 value = 0; unsafe { do { value = Danger(); } while (false); } return value; }")]
    public void LiteralFalseDoLoopsExecuteSupportedBodyOnceWithoutRegions(int expected, string source)
    {
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "false-do-loop");
        Assert.That(IndexedLoopRegionCount(artifact, 1), Is.EqualTo(0));
        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void ReturnOnlyAcyclicLoopsEraseToTheirTerminalReturn()
    {
        string[] sources = [
            "int32 Main() { do { return 42; } while (Fail()); } bool Fail() { require false; return false; }",
            "int32 Main() { while (true) { return 42; } }",
            "int32 Main() { for (;;) { return 42; } }",
            "int32 Main() { do { require Check(); return 42; } while (FailCondition()); } bool Check() { return true; } bool FailCondition() { require false; return false; }",
            "int32 Main() { while (true) { do { return 42; } while (Fail()); } } bool Fail() { require false; return false; }",
            "int32 Main() { do { for (mut int32 value = Start();; value = FailIterator()) { return value; } } while (FailCondition()); } int32 Start() { return 42; } int32 FailIterator() { require false; return 0; } bool FailCondition() { require false; return false; }",
        ];
        foreach (var source in sources)
        {
            var original = Compile(source);
            var artifact = TrRmExecutableCompiler.Emit(original, "acyclic-return");
            Assert.That(IndexedLoopRegionCount(artifact, 1), Is.EqualTo(0));
            var loaded = TrRmExecutableCompiler.Load(artifact);
            Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
            Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
            Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
        }
    }

    [Test]
    public void BreakOnlyAcyclicLoopsEraseToTheirFallthrough()
    {
        string[] sources = [
            "int32 Main() { do { break; } while (Fail()); return 42; } bool Fail() { require false; return false; }",
            "int32 Main() { while (true) { break; } return 42; }",
            "int32 Main() { mut int32 value = 0; for (value = Start();; ) { break; } return value; } int32 Start() { return 42; }",
        ];
        foreach (var source in sources)
        {
            var original = Compile(source);
            var artifact = TrRmExecutableCompiler.Emit(original, "acyclic-break");
            Assert.That(IndexedLoopRegionCount(artifact, 1), Is.EqualTo(0));
            var loaded = TrRmExecutableCompiler.Load(artifact);
            Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
            Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
            Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
        }
    }

    [Test]
    public void NestedBreakLoopCanPrecedeAnOuterTerminalReturn()
    {
        const string source = """
            int32 Main() {
                do {
                    do { break; } while (Fail());
                    return 42;
                } while (Fail());
            }
            bool Fail() { require false; return false; }
            """;
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "nested-acyclic-break");
        Assert.That(IndexedLoopRegionCount(artifact, 1), Is.EqualTo(0));
        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void NestedBreakCarriesOuterSlotValuesToTheTerminalTail()
    {
        const string source = """
            int32 Main() {
                mut int32 result = 0;
                do {
                    do {
                        if (Choose()) {
                            result = 42;
                            break;
                        } else {
                            return 13;
                        }
                    } while (Fail());
                    return result;
                } while (Fail());
            }
            bool Choose() { return true; }
            bool Fail() { require false; return false; }
            """;
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "nested-acyclic-slots");
        Assert.That(IndexedLoopRegionCount(artifact, 1), Is.EqualTo(0));
        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void NestedBreaksKeepTheirLoopExitsDistinct()
    {
        const string source = """
            int32 Main() {
                mut int32 result = 0;
                do {
                    do {
                        result = 40;
                        break;
                    } while (Fail());
                    result += 2;
                    break;
                } while (Fail());
                return result;
            }
            bool Fail() { require false; return false; }
            """;
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "nested-acyclic-exits");
        Assert.That(IndexedLoopRegionCount(artifact, 1), Is.EqualTo(0));
        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }
    [TestCase(42,"int32 Main() { mut int32 x = 0; do { if (x++ == 0) { break; } else { return 13; } } while (Fail()); return x == 1 ? 42 : 0; } bool Fail() { require false; return false; }")]
    [TestCase(42,"int32 Main() { while (true) { if (Check()) { return 42; } else { break; } } return 0; } bool Check() { return true; }")]
    [TestCase(42,"int32 Main() { mut int32 x = 0; for (x = Start();;) { if (x == 42) { break; } else { return 13; } } return x; } int32 Start() { return 42; }")]
    [TestCase(42,"int32 Main() { mut int32 x = 0; for (x = Start();; x = Fail()) { if (x == 42) { break; } else { return 13; } } return x; } int32 Start() { return 42; } int32 Fail() { require false; return 0; }")]
    [TestCase(42,"int32 Main() { do { if (true) { if (false) { break; } else { return 42; } } else { return 13; } } while (Fail()); return 0; } bool Fail() { require false; return false; }")]
    [TestCase(42,"int32 Main() { mut int32 x = 0; do { if (x++ == 1) { return 13; } break; } while (Fail()); return x == 1 ? 42 : 0; } bool Fail() { require false; return false; }")]
    [TestCase(13,"int32 Main() { mut int32 x = 1; do { if (x++ == 1) { return 13; } break; } while (Fail()); return 42; } bool Fail() { require false; return false; }")]
    public void BranchingAcyclicLoopsEraseToExistingReturnsAndFallthrough(int expected, string source)
    {
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "acyclic-branches");
        Assert.That(IndexedLoopRegionCount(artifact, 1), Is.EqualTo(0));
        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }
    [TestCase(42,"int32 Main() { mut int32 x = 0; do { x++; break; } while (Fail()); return x == 1 ? 42 : 0; } bool Fail() { require false; return false; }")]
    [TestCase(42,"int32 Main() { mut int32 x = 0; for (;;) { x++; break; } return x == 1 ? 42 : 0; }")]
    [TestCase(42,"int32 Main() { mut int32 x = 0; do { x = 42; break; } while (Fail()); return x; } bool Fail() { require false; return false; }")]
    [TestCase(42,"int32 Main() { mut int32 result = 0; do { mut int32 local = 41; local++; result = local; break; } while (Fail()); return result == 42 ? 42 : 0; } bool Fail() { require false; return false; }")]
    [TestCase(42,"int32 Main() { mut int32 result = 0; do { result += 41; result += 1; break; } while (Fail()); return result; } bool Fail() { require false; return false; }")]
    [TestCase(42,"int32 Main() { mut uint32 result = 0; do { result += 42; break; } while (false); return result == 42 ? 42 : 0; }")]
    [TestCase(42,"int32 Main() { mut int64 result = 0; do { result += 42; break; } while (false); return result == 42 ? 42 : 0; }")]
    [TestCase(42,"int32 Main() { mut uint64 result = 0; do { result += 42; break; } while (false); return result == 42 ? 42 : 0; }")]
    [TestCase(42,"int32 Main() { mut int32 result = 21; do { result <<= 1; break; } while (false); return result; }")]
    [TestCase(42,"int32 Main() { mut int32 result = 1; do { result += result++; break; } while (false); return result == 2 ? 42 : 0; }")]
    [TestCase(42,"int32 Main() { mut int32 result = 0; do { result = Compute(40, 2); break; } while (Fail()); return result; } int32 Compute(int32 left, int32 right) { return left + right; } bool Fail() { require false; return false; }")]
    [TestCase(42,"int32 Main() { mut int32 result = 1; do { result = result++ == 1 ? Compute(40, 2) : 13; return result; } while (Fail()); } int32 Compute(int32 left, int32 right) { return left + right; } bool Fail() { require false; return false; }")]
    [TestCase(42,"int32 Main() { mut int32 result = 0; do { int32 local = Compute(40, 2); result = local; break; } while (Fail()); return result; } int32 Compute(int32 left, int32 right) { return left + right; } bool Fail() { require false; return false; }")]
    [TestCase(42,"int32 Main() { do { int32 local = Compute(40, 2); return local; } while (Fail()); } int32 Compute(int32 left, int32 right) { return left + right; } bool Fail() { require false; return false; }")]
    [TestCase(42,"int32 Main() { mut int32 result = 0; do { if (Choose()) { { int32 local = Compute(40, 0); result = local; } } else { result = 13; } return result + 2; } while (Fail()); } bool Choose() { return true; } int32 Compute(int32 left, int32 right) { return left + right; } bool Fail() { require false; return false; }")]
    [TestCase(15,"int32 Main() { mut int32 result = 0; do { if (Choose()) { { int32 local = Compute(40, 0); result = local; } } else { result = 13; } return result + 2; } while (Fail()); } bool Choose() { return false; } int32 Compute(int32 left, int32 right) { return left + right; } bool Fail() { require false; return false; }")]
    [TestCase(42,"int32 Main() { do { Touch(); break; } while (false); return 42; } void Touch() { }")]
    [TestCase(42,"int32 Main() { mut int32 result = 0; for (mut int32 index = 0; true; index++) { if (index == 0) { result = 40; } else { result = 13; } return result + 2; } }")]
    [TestCase(42,"int32 Main() { mut int32 result = 0; for (mut int32 i = 0; true; i = Fail()) { int32 local = Compute(40, 2); result = local; break; } return result; } int32 Compute(int32 left, int32 right) { return left + right; } int32 Fail() { require false; return 0; }")]
    public void AcyclicLoopsPreserveSafeTerminalPrefixEffects(int expected, string source)
    {
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "acyclic-effects");
        Assert.That(IndexedLoopRegionCount(artifact, 1), Is.EqualTo(0));
        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }
    [TestCase("int32 Main() { do { Stop(); break; } while (false); return 42; } [DoesNotReturn] void Stop() { require false; }")]
    [TestCase("int32 Main() { do { init int32 value; } while (false); return 42; }")]
    [TestCase("int32 Main() { do { if (true) { init int32 value; } return 42; } while (false); }")]
    public void AcyclicLoopShapesRejectWithoutInventedBackedges(string source)
        => Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(Compile(source), "acyclic-loop"));
    [TestCase("int32 Main() { do { if (false) { init int32 x; } } while (false); return 42; }")]
    [TestCase("int32 Main() { for (; false;) { init int32 x; } return 42; }")]
    public void InactiveUnsupportedLocalsRemainAudited(string source)
        => Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(Compile(source), "inactive-local"));
    [TestCase(false,true)]
    [TestCase(false,false)]
    [TestCase(true,true)]
    [TestCase(true,false)]
    public void NewLoopTargetsRetainTheExactScalarValueBound(bool iterator, bool supported)
    {
        var count = iterator ? 82 : 83;
        var source = new StringBuilder("int32 Main() { mut int32 x = 0; ");
        source.Append(iterator ? "for (; x < 1; x++) { " : "do { ");
        for (var index = 0; index < count; index++)
        {
            source.Append($"{{ int32 local = {index}; x = x + 1; }} ");
        }
        source.Append(iterator ? "continue; } return " : "} while (x < 1); return ");
        // The do graph has six fixed values; the separate iterator graph has eight.
        var unaryCount = (iterator ? 2 : 1) + (supported ? 0 : 1);
        for (var index = 0; index < unaryCount; index++)
        {
            source.Append("+(");
        }
        source.Append('x').Append(')', unaryCount).Append("; }");
        var original = Compile(source.ToString());
        if (!supported)
        {
            Assert.Throws<NotSupportedException>(() =>
                TrRmExecutableCompiler.Emit(original, "257-values"));
            return;
        }
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(original, "256-values"));
        Assert.That(loaded.Functions[0].Variables.Length, Is.EqualTo(256));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(iterator ? count + 1 : count));
    }

    private static int IndexedLoopRegionCount(byte[] artifact, int bodyRow)
    {
        var sectionCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(artifact.AsSpan(12)));
        for (var section = 0; section < sectionCount; section++)
        {
            var entryOffset = 32 + (section * 16);
            if (BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(entryOffset)) != 0x20)
            {
                continue;
            }

            var bodyCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                artifact.AsSpan(entryOffset + 4)));
            if (bodyRow < 1 || bodyRow > bodyCount)
            {
                throw new ArgumentOutOfRangeException(nameof(bodyRow));
            }

            var dataOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                artifact.AsSpan(entryOffset + 8)));
            var bodyOffset = dataOffset + ((bodyCount + 1) * sizeof(uint))
                + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    artifact.AsSpan(dataOffset + ((bodyRow - 1) * sizeof(uint)))));
            var width = 1 << (artifact[bodyOffset] & 3);
            var regionCountOffset = bodyOffset + 1 + (2 * width);
            return width switch {
                1 => artifact[regionCountOffset],
                2 => BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(regionCountOffset)),
                4 => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(artifact.AsSpan(regionCountOffset))),
                _ => throw new InvalidOperationException("Invalid indexed Body compact width."),
            };
        }

        throw new InvalidOperationException("Executable artifact is missing its indexed Body section.");
    }
}
