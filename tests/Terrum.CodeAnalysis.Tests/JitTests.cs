// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    private const string OwnedBufferProgram = """
        int32 Main() {
            require OwnedInt32Buffer.TryCreate(3, out var created);
            mut OwnedInt32Buffer buffer = move created;
            require buffer.TryAppend(20);
            require buffer.TryAppend(22);
            require buffer.TryAppend(buffer.Read(0));
            if (buffer.TryAppend(99) || buffer.Count != 3) { return 1; }
            buffer.Set(2, 0);
            return buffer.Read(0) + buffer.Read(1) + buffer.Read(2);
        }
        """;

    private const string ReadonlyBufferViewProgram = """
        int32 Main() {
            require OwnedInt32Buffer.TryCreate(2, out var raw);
            mut OwnedInt32Buffer buffer = move raw;
            require buffer.TryAppend(20);
            require buffer.TryAppend(22);
            ReadonlyInt32BufferView slice = buffer.Slice(0, buffer.Count);
            return slice.Read(0) + slice.Read(1);
        }
        """;

    private const string ReadonlyBufferViewFailure = """
        int32 Main() {
            require OwnedInt32Buffer.TryCreate(1, out var raw);
            mut OwnedInt32Buffer buffer = move raw;
            require buffer.TryAppend(42);
            ReadonlyInt32BufferView slice = buffer.Slice(0, buffer.Count);
            return slice.Read(1);
        }
        """;

    [Theory]
    [InlineData(OwnedBufferProgram, 42)]
    [InlineData(ReadonlyBufferViewProgram, 42)]
    [InlineData(ReadonlyBufferViewFailure, unchecked((int)0xE0540003))]
    [InlineData("int32 Main() { return 42; }", 42)]
    [InlineData("void Main() { }", 0)]
    [InlineData("int32 Main() { mut int32 y = 9; bool result = true || Try(out y); return y; } bool Try(out int32 y) { return false; }", 9)]
    [InlineData("int32 Main() { require Fill(out var old); require Take(out var flag); if (flag == true) { return 0; } return 1; } bool Fill(out int32 value) { value = 2147483647; return true; } bool Take(out bool value) { value = true; return true; }", 0)]
    [InlineData("int32 Main() { require Fill(out var old); require Take(out var unit); return int32(unit); } bool Fill(out int32 value) { value = 2147483647; return true; } bool Take(out char16 value) { value = char16(42); return true; }", 42)]
    [InlineData("int32 Main() { return Factorial(6); } int32 Factorial(int32 n) { if (n <= 1) { return 1; } return n * Factorial(n - 1); }", 720)]
    [InlineData("int32 Main() { return Mix(1, 2, 3, 4, 5, 6); } int32 Mix(int32 a, int32 b, int32 c, int32 d, int32 e, int32 f) { return a + b * 2 + c * 3 + d * 4 + e * 5 + f * 6; }", 91)]
    [InlineData("int32 Main() { return 2147483647 + 1; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { return 1 / 0; }", unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { require false; return 1; }", unchecked((int)0xE0540003))]
    [InlineData("int32 Main() { uint32 n = 4000000000; if (n > 2147483647 && n / 1000000000 == 4) { return int32(n - 3999999958); } return 0; }", 42)]
    [InlineData("int32 Main() { return int32(uint32.MaxValue); }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { return int32(uint32.MaxValue + 1); }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { char16 high = char16(uint32(55296)); return int32(high); }", 55296)]
    [InlineData("int32 Main() { return int32(char16(uint32(65536))); }", unchecked((int)0xE0540001))]
    [InlineData("""
        struct Tag {
            public char16 Unit;
            public bool Valid;
            public Tag(char16 unit, bool valid) {
                this.Unit = unit;
                this.Valid = valid;
            }
        }
        Tag CreateTag() { return new Tag(char16(uint32(55296)), true); }
        int32 ReadTag(Tag tag) { if (tag.Valid) { return int32(tag.Unit); } return 0; }
        int32 Main() { return ReadTag(CreateTag()); }
        """, 55296)]
    [InlineData("int32 Main() { require Try(out var x) && x == 7; return x; } bool Try(out int32 x) { x = 7; return true; }", 7)]
    [InlineData("int32 Main() { require Try(out var x); return x; } bool Try(out int32 x) { x = 7; return false; }", unchecked((int)0xE0540003))]
    [InlineData("int32 Main() { if (Try(out var x) || false) { return x; } return 0; } bool Try(out int32 x) { x = 7; return true; }", 7)]
    [InlineData("int32 Main() { mut int32 sum = 0; for (mut int32 i = 0; i < 10; i = i + 1) { sum = sum + i; } return sum; }", 45)]
    [InlineData("int32 Main() { mut int32 i = 0; for (; i < 7;) { i = i + 1; } return i; }", 7)]
    [InlineData("int32 Main() { for (;;) { return 42; } return 0; }", 42)]
    [InlineData("int32 Main() { mut int32 i = 3; for (i = 9; false; i = i / 0) { require false; } return i; }", 9)]
    [InlineData("int32 Main() { for (mut int32 i = 0; i < 1; i = i / 0) { return 42; } return 0; }", 42)]
    [InlineData("int32 Main() { mut int32 sum = 0; for (mut int32 i = 1; i < 4; i = i + 1) { for (mut int32 j = 1; j < 4; j = j + 1) { sum = sum + i * j; } } return sum; }", 36)]
    [InlineData("int32 Main() { for (mut int32 i = 0; i < 3; i = i + 1) { } int32 i = 42; return i; }", 42)]
    [InlineData("int32 Main() { mut int32 sum = 0; for (mut int32 i = 0; Next(i, out var value); i = i + 1) { sum = sum + value; } return sum; } bool Next(int32 i, out int32 value) { if (i == 4) { return false; } value = i; return true; }", 6)]
    [InlineData("int32 Main() { for (mut int32 i = 0; i < 1; i = i / 0) { } return 0; }", unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { for (mut int32 i = int32.MaxValue; i > 0; i = i + 1) { } return 0; }", unchecked((int)0xE0540001))]
    [InlineData("struct S { public int32 Value; public S() { Value = 1; this.Value = 42; } } int32 Main() { S value = new S(); return value.Value; }", 42)]
    [InlineData("struct Inner { public mut int32 Value; public Inner() { Value = 1; } } struct Outer { public Inner Value; public Outer() { Value = new Inner(); Value.Value = 42; } } int32 Main() { Outer value = new Outer(); return value.Value.Value; }", 42)]
    [InlineData("struct S { public int32 Value; public S(int32 value) { Value = value; } } int32 Main() { mut S value = new S(1); S copy = value; value = new S(41); return value.Value + copy.Value; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 1; try { x = 42; } finally { x = 43; } return x; }", 43)]
    [InlineData("int32 Main() { mut int32 x = 42; try { return x; } finally { x = 0; } }", 42)]
    [InlineData("int32 Main() { mut int32 x = 42; try { try { return x; } finally { x = 1; } } finally { x = 2; } }", 42)]
    [InlineData("int32 Main() { mut int32 x = 42; try { if (x == 42) { return x; } x = 1; } finally { x = 0; } return x; }", 42)]
    [InlineData("int32 Choose(bool first) { mut int32 x = 42; try { if (first) { return x; } x = 7; return x; } finally { int32 local = 1; x = local; } } int32 Main() { return Choose(true) & 42; }", 42)]
    [InlineData("int32 Main() { int64 result = Read(4294967338); if (result == 4294967338) { return 42; } return 0; } int64 Read(int64 x) { mut int64 local = x; try { return local; } finally { local = 0; Clobber(); } } [NoThrow] void Clobber() { mut int64 x = 1; x = 2; }", 42)]
    [InlineData("struct V { public int32 A; public int32 B; public int32 C; [NoThrow] public V(int32 a) { A = a; B = 2; C = 3; } } V Take(V v) { mut V local = v; try { return local; } finally { local = new V(0); } } int32 Main() { V v = Take(new V(42)); return v.A; }", 42)]
    [InlineData("struct V { public int32 A; public int32 B; public int32 C; public int32 D; [NoThrow] public V(int32 a) { A = a; B = 2; C = 3; D = 4; } } V Take(V v) { mut V local = v; try { return local; } finally { local = new V(0); } } int32 Main() { V v = Take(new V(42)); return v.A; }", 42)]
    [InlineData("struct V { public mut int32 A; public int32 B; public int32 C; public int32 D; [NoThrow] public V(int32 a) { A = a; B = 2; C = 3; D = 4; } } V Take(V v) { mut V local = v; try { return local; } finally { local.A = 0; Clobber(1, 2, 3, 4, 5, 6); } } [NoThrow] void Clobber(int32 a, int32 b, int32 c, int32 d, int32 e, int32 f) { } int32 Main() { V v = Take(new V(42)); return v.A; }", 42)]
    [InlineData("struct V { public int32 A; public int32 B; public V() { try { A = 42; } finally { B = 0; } } } int32 Main() { V value = new V(); return value.A; }", 42)]
    [InlineData("int32 Main() { mut int32 state = 0; try { try { state = 0; } finally { state = 1; } } finally { if (state == 1) { state = 42; } else { state = 0; } } return state; }", 42)]
    [InlineData("bool Take(out int32 x) { x = 42; try { return true; } finally { Clobber(); } } [NoThrow] void Clobber() { mut int32 x = 1; x = 0; } int32 Main() { require Take(out var x); return x; }", 42)]
    [InlineData("bool Take(out int32 x) { try { return true; } finally { x = 42; } } int32 Main() { require Take(out var x); return x; }", 42)]
    [InlineData("bool Take(out int32 x) { try { return false; } finally { x = 42; } } int32 Main() { mut int32 x = 9; bool ok = Take(out x); if (ok) { return 0; } return 42; }", 42)]
    [InlineData("bool Take(out int32 x) { x = 42; mut bool ok = true; try { return ok; } finally { ok = false; } } int32 Main() { require Take(out var x); return x; }", 42)]
    [InlineData("int32 Main() { mut int32 value = 0; try { try { value = 1; } finally { value = Sixth(1, 2, 3, 4, 5, 42); } } finally { value = Sixth(1, 2, 3, 4, 5, value); } return value; } [NoThrow] int32 Sixth(int32 a, int32 b, int32 c, int32 d, int32 e, int32 f) { return f; }", 42)]
    [InlineData("struct Value { public int32 A; public int32 B; public int32 C; public Value(int32 a) { A = a; B = 2; C = 3; } } int32 Mix(int32 a, int32 b, int32 c, int32 d, int32 e, int32 f) { return a + b + c + d + e + f; } int32 Main() { Value value = new Value(42); return Mix(value.A, 2, 3, 4, 5, 6); }", 62)]
    [InlineData("[NoThrow] int32 Value() { return 42; } bool Take(out int32 result) { try { return true; } finally { result = Value(); } } int32 Main() { require Take(out var result); return result; }", 42)]
    [InlineData("int32 Main() { mut int32 value = 0; try { value = 1; } finally { try { value = 2; } finally { value = 42; } } return value; }", 42)]
    [InlineData("unique struct Item { public int32 Value; public Item(int32 value) { Value = value; } [NoThrow] [Cleanup] public void Close() {} [NoThrow] public int32 Read() { return Value; } } int32 Main() { Item item = new Item(42); return item.Read(); }", 42)]
    [InlineData("unique struct Item { public int64 Value; public int32 Tag; public Item(int64 value) { Value = value; Tag = 1; } [NoThrow] [Cleanup] public void Close() {} } Item Take() { Item item = new Item(42); return move item; } int32 Main() { Item item = Take(); return int32(item.Value); }", 42)]
    [InlineData("unique struct Item { public int32 Value; public Item(int32 value) { Value = value; } [NoThrow] [Cleanup] public void Close() {} } int32 Take(int32 a, int32 b, int32 c, int32 d, int32 e, Item item) { return item.Value; } int32 Main() { Item item = new Item(42); return Take(1, 2, 3, 4, 5, move item); }", 42)]
    [InlineData("unique struct Item { public int32 Value; public Item(int32 value) { Value = value; } [NoThrow] [Cleanup] public void Close() {} } bool Fill(out Item item) { item = new Item(42); return true; } int32 Main() { require Fill(out var item); return item.Value; }", 42)]
    [InlineData("""
        struct Info {
            public int32 Offset;
            public int32 Check;
            public Info() { Offset = 2; Check = 1; }
        }
        unique struct Token {
            public int32 Id;
            public Token(int32 id) { Id = id; }
            [NoThrow] [Cleanup] public void Close() {}
            [NoThrow] public int32 Read() { return Id; }
        }
        unique struct Result {
            public Info Data;
            public Token Child;
            public int32 Tail;
            public Result(int32 id) {
                Child = new Token(id);
                Data = new Info();
                Tail = 0;
                require Child.Read() == id;
            }
            [NoThrow] [Cleanup] public void Close() {}
            [NoThrow] public int32 Read() { return Child.Read(); }
        }
        Result Relay(Result value) { return move value; }
        bool Fill(out Result value) { value = Relay(new Result(40)); return true; }
        int32 Main() { require Fill(out var result); return result.Read() + result.Data.Offset; }
        """, 42)]
    public Task WorkerAndNativeExecutableAgreeAsync(string source, int expected) =>
        AssertFailFastExecutionAsync(source, expected);

    private static async Task AssertFailFastExecutionAsync(string source, int expected, params string[] sourcePaths)
    {
        AssertWindowsX64();
        var compilation = Compilation.Create(await ReadExecutionTreesAsync(source, sourcePaths).ConfigureAwait(true));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var root = RepositoryRoot();
        var directory = Path.Combine(root, "artifacts", "jit-tests");
        _ = Directory.CreateDirectory(directory);
        var image = Path.Combine(directory, $"jit-{Guid.NewGuid():N}.exe");

        try
        {
            await File.WriteAllBytesAsync(image, NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast)).ConfigureAwait(true);
            (var nativeExit, _, _) = await RunAsync(new ProcessStartInfo(image) { UseShellExecute = false, CreateNoWindow = true }, []).ConfigureAwait(true);
            var jit = CliStart("jit", "-", "--runtime", "fail-fast");
            foreach (var path in sourcePaths)
            {
                jit.ArgumentList.Add("--source");
                jit.ArgumentList.Add(path);
            }
            (var jitExit, var jitOutput, var jitError) = await RunAsync(jit, Encoding.UTF8.GetBytes(source)).ConfigureAwait(true);
            Assert.Equal(expected, nativeExit);
            Assert.Equal(nativeExit, jitExit);
            Assert.Equal("", jitOutput);
            Assert.Equal("", jitError);
        }
        finally
        {
            File.Delete(image);
        }
    }

    [Theory]
    [InlineData("int32 Main() { unsafe { try { return Divide(42, 0); } catch { return 42; } } } int32 Divide(int32 a, int32 b) { return a / b; }", 42)]
    [InlineData("int32 Main() { mut int32 value = 0; unsafe { try { try { value = Divide(42, 0); } finally { value = 1; } } catch { if (value == 1) { return 42; } return 0; } } return 0; } int32 Divide(int32 a, int32 b) { return a / b; }", 42)]
    [InlineData("int32 Main() { mut int32 value = 0; unsafe { try { try { try { require false; } finally { value = 1; } } finally { value = 3; } } catch { return value + 39; } } return 0; }", 42)]
    [InlineData("int32 Main() { mut int32 value = 0; unsafe { try { require false; } catch { value = 1; } finally { value = 42; } } return value; }", 42)]
    [InlineData("int32 Main() { mut int32 value = 0; unsafe { try { try { require false; } catch { require false; } finally { value = 1; } } catch { return value + 41; } } return 0; }", 42)]
    [InlineData("int32 Main() { mut int32 value = 0; unsafe { try { value = 41; } catch { value = 0; } finally { value = 42; } } return value; }", 42)]
    [InlineData("int32 Main() { unsafe { try { require Fail(); } catch { return 42; } } return 0; } bool Fail() { return false; }", 42)]
    [InlineData("int32 Main() { unsafe { try { return int32(uint32.MaxValue); } catch { return 42; } } }", 42)]
    [InlineData("int32 Main() { mut int64 value = 42; unsafe { try { return int32(Take(value)); } catch { return 0; } } } int64 Take(int64 value) { try { return value; } finally { int64 ignored = 0; } }", 42)]
    [InlineData("struct V { public int32 A; public int32 B; public int32 C; [NoThrow] public V(int32 x) { A = x; B = 1; C = 2; } } V Take(V value) { mut V local = value; try { return local; } finally { local = new V(0); } } int32 Main() { V value = Take(new V(42)); return value.A; }", 42)]
    [InlineData("struct V { public int32 A; public int32 B; public int32 C; public int32 D; [NoThrow] public V(int32 x) { A = x; B = 1; C = 2; D = 3; } } V Take(V value) { mut V local = value; try { return local; } finally { local = new V(0); } } int32 Main() { V value = Take(new V(42)); return value.A; }", 42)]
    [InlineData("bool Take(bool fail, out int32 x) { unsafe { try { x = 42; require !fail; return true; } catch { return false; } } } int32 Main() { require Take(false, out var x); return x; }", 42)]
    [InlineData("bool Take(bool fail, out int32 x) { unsafe { try { x = 42; require !fail; return true; } catch { return false; } } } int32 Main() { mut int32 x = 9; bool ok = Take(true, out x); if (ok) { return 0; } return 42; }", 42)]
    [InlineData("[NoThrow] bool Initialize(out int32 value) { value = 42; return true; } bool Fail() { require false; return false; } bool Take(bool enter, out int32 value) { unsafe { try { if (enter && Initialize(out value) && Fail()) { return true; } return false; } catch { return value == 42; } } } int32 Main() { require Take(true, out var value); if (Take(false, out var absent)) { return 1; } return value; }", 42)]
    [InlineData("[NoThrow] int32 Sixth(int32 a, int32 b, int32 c, int32 d, int32 e, int32 f) { return f; } bool Take(out int32 value) { unsafe { try { try { require false; } finally { value = Sixth(1, 2, 3, 4, 5, 42); } } catch { return true; } } return false; } int32 Main() { require Take(out var value); return value; }", 42)]
    [InlineData("struct Value { public int32 A; public int32 B; public int32 C; public Value(int32 a, bool fail) { A = a; require !fail; B = 2; C = 3; } } int32 Main() { mut Value value = new Value(42, false); unsafe { try { value = new Value(0, true); } catch { return value.A; } } return 0; }", 42)]
    [InlineData("int32 Main() { return 2147483647 + 1; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { return 42 / 0; }", unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { require false; return 0; }", unchecked((int)0xE0540003))]
    [InlineData("unique struct Item { public int32 Value; public Item(int32 value) { Value = value; } [NoThrow] [Cleanup] public void Close() {} [NoThrow] public int32 Read() { return Value; } } int32 Main() { mut Item item = new Item(42); unsafe { try { item = new Item(42 / 0); } catch { return item.Read(); } } return 0; }", 42)]
    [InlineData("unique struct Item { public int32 Value; public Item(int32 value) { Value = value; } [NoThrow] [Cleanup] public void Close() {} } int32 Main() { mut Item item = new Item(7); item = new Item(42); return item.Value; }", 42)]
    [InlineData("unique struct Item { public int32 Value; public Item(int32 value) { Value = value; } [NoThrow] [Cleanup] public void Close() {} } bool Fill(bool accepted, out Item item) { item = new Item(42); return accepted; } int32 Main() { bool rejected = Fill(false, out var absent); if (rejected) { return 0; } require Fill(true, out var item); return item.Value; }", 42)]
    [InlineData("unique struct Item { public int32 Value; public Item(int32 value) { Value = value; } [NoThrow] [Cleanup] public void Close() {} } void Take(Item first, int32 second) {} int32 Main() { unsafe { try { Take(new Item(7), 1 / 0); } catch { return 42; } } return 0; }", 42)]
    [InlineData("unique struct Item { public int32 Value; public Item(int32 value) { Value = value; } [NoThrow] [Cleanup] public void Close() {} [NoThrow] public int32 Read() { return Value; } } bool Fill(out Item output) { output = new Item(42); return output.Value == 42 && output.Read() == 42; } int32 Main() { require Fill(out var item); return item.Value; }", 42)]
    [InlineData("unique struct Item { public int32 A; public int32 B; public int32 C; public Item(int32 a) { A = a; B = a + 1; C = a + 2; } [NoThrow] [Cleanup] public void Close() {} [NoThrow] public int32 Read() { return C; } } bool Refill(out Item output) { output = new Item(7); Item moved = move output; output = new Item(40); return output.C == 42 && output.Read() == 42; } int32 Main() { require Refill(out var item); return item.C; }", 42)]
    [InlineData(OwnedBufferProgram, 42)]
    [InlineData(ReadonlyBufferViewProgram, 42)]
    [InlineData(ReadonlyBufferViewFailure, unchecked((int)0xE0540003))]
    [InlineData("""
        int32 Read() {
            require OwnedInt32Buffer.TryCreate(1, out var raw);
            mut OwnedInt32Buffer buffer = move raw;
            require buffer.TryAppend(42);
            ReadonlyInt32BufferView slice = buffer.Slice(0, buffer.Count);
            return slice.Read(1);
        }
        int32 Main() {
            unsafe {
                try { return Read(); }
                catch { return 42; }
            }
        }
        """, 42)]
    public Task CheckedExceptionWorkerAndExecutableAgreeAsync(string source, int expected) =>
        AssertCheckedExecutionAsync(source, expected);

    private static async Task AssertCheckedExecutionAsync(string source, int expected, params string[] sourcePaths)
    {
        AssertWindowsX64();
        var compilation = Compilation.Create(await ReadExecutionTreesAsync(source, sourcePaths).ConfigureAwait(true),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var directory = Path.Combine(RepositoryRoot(), "artifacts", "jit-tests");
        _ = Directory.CreateDirectory(directory);
        var image = Path.Combine(directory, $"checked-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(image, NativeEmitter.Emit(program, NativeRuntimePolicy.CheckedExceptions)).ConfigureAwait(true);
            (var nativeExit, _, _) = await RunAsync(new ProcessStartInfo(image) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }, []).ConfigureAwait(true);
            var jit = CliStart("jit", "-", "--runtime", "checked-exceptions");
            foreach (var path in sourcePaths)
            {
                jit.ArgumentList.Add("--source");
                jit.ArgumentList.Add(path);
            }
            (var jitExit, var output, var error) = await RunAsync(jit, Encoding.UTF8.GetBytes(source)).ConfigureAwait(true);
            Assert.Equal(expected, nativeExit);
            Assert.Equal(nativeExit, jitExit);
            Assert.Equal("", output);
            Assert.Equal("", error);
        }
        finally
        {
            File.Delete(image);
        }
    }

    private static async Task<SyntaxTree[]> ReadExecutionTreesAsync(string source, string[] sourcePaths)
    {
        var trees = new SyntaxTree[sourcePaths.Length + 1];
        trees[0] = SyntaxTree.Parse(source);
        for (var index = 0; index < sourcePaths.Length; index++)
        {
            var path = sourcePaths[index];
            trees[index + 1] = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
        }
        return trees;
    }

    [Fact]
    public async Task CheckedBoundsFailureAgreesBetweenHostBufferPeAndJitWorkerAsync()
    {
        AssertWindowsX64();
        var source = "int32 Main() { return int32(Host.Source[42]); }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            [new HostBuffer("Source", PrimitiveType.Char16, [97], false)], BoundRuntimePolicy.CheckedExceptions);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var directory = Path.Combine(RepositoryRoot(), "artifacts", "jit-tests");
        _ = Directory.CreateDirectory(directory);
        var image = Path.Combine(directory, $"checked-bounds-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(image, NativeEmitter.Emit(
                Assert.IsType<BoundProgram>(compilation.Program), NativeRuntimePolicy.CheckedExceptions)).ConfigureAwait(true);
            (var nativeExit, _, _) = await RunAsync(new ProcessStartInfo(image) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }, []).ConfigureAwait(true);
            var scanner = Path.Combine(RepositoryRoot(), "samples", "native", "checked-bounds.terrum");
            (var jitExit, _, _) = await RunAsync(
                CliStart("scan-component", scanner, "-", "--runtime", "checked-exceptions"),
                Encoding.UTF8.GetBytes("a")).ConfigureAwait(true);
            Assert.Equal(unchecked((int)0xE0540004), nativeExit);
            Assert.Equal(nativeExit, jitExit);
        }
        finally
        {
            File.Delete(image);
        }
    }

    [Theory]
    [InlineData("int32 Main() { return unknown; }", "TRM")]
    [InlineData("int32 Main() { mut int32 x = 9; if (Try(out x) && x == 5) { return 1; } return x; } bool Try(out int32 x) { x = 5; return false; }", "TRM3016")]
    [InlineData("int32 Main() { mut int32 y = 9; bool result = false || Try(out y); return y; } bool Try(out int32 y) { return false; }", "TRM")]
    [InlineData("int32 Main() { mut int32 y = 9; Try(out y); return y; } bool Try(out int32 y) { return false; }", "TRM")]
    [InlineData("int32 Main() { mut int32 y = 9; while (y > 0) { Try(out y); } return 0; } bool Try(out int32 y) { return false; }", "TRM")]
    [InlineData("int32 Main() { return 1", "TRM")]
    public async Task InvalidSourceDoesNotExecuteAsync(string source, string diagnosticPrefix)
    {
        (var exit, var output, _) = await RunAsync(CliStart("jit", "-", "--runtime", "fail-fast", "--json"), Encoding.UTF8.GetBytes(source)).ConfigureAwait(true);
        Assert.Equal(1, exit);
        using var document = JsonDocument.Parse(output);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.StartsWith(diagnosticPrefix, document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidUtf8AndMissingRuntimePolicyAreDiagnosedAsync()
    {
        (var invalidExit, var invalidOutput, _) = await RunAsync(CliStart("jit", "-", "--runtime", "fail-fast", "--json"), [0xFF]).ConfigureAwait(true);
        Assert.Equal(2, invalidExit);
        using (var document = JsonDocument.Parse(invalidOutput))
        {
            Assert.Equal("TRM2001", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
        }

        (var missingExit, var missingOutput, _) = await RunAsync(CliStart("jit", "-", "--json"), []).ConfigureAwait(true);
        Assert.Equal(2, missingExit);
        using var missingDocument = JsonDocument.Parse(missingOutput);
        Assert.Equal("TRM2000", missingDocument.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("alpha 42", 5, 6, 8)]
    [InlineData("\U00010400 42", 2, 3, 5)]
    public async Task ScannerWorkerReturnsCopiedWritableTokenDataAsync(string source, int identifierLength, int integerStart, int sourceLength)
    {
        AssertWindowsX64();
        var scanner = Path.Combine(RepositoryRoot(), "self-host", "Scanner.terrum");
        (var exit, var output, var error) = await RunAsync(
            CliStart("scan-component", scanner, "-", "--runtime", "fail-fast", "--json"),
            Encoding.UTF8.GetBytes(source)).ConfigureAwait(true);
        Assert.Equal(0, exit);
        Assert.Equal("", error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(8, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("scanner-8", document.RootElement.GetProperty("component").GetString());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(0, document.RootElement.GetProperty("diagnostics").GetArrayLength());
        var tokens = document.RootElement.GetProperty("tokens");
        Assert.Equal(3, tokens.GetArrayLength());
        Assert.Equal("Identifier", tokens[0].GetProperty("kind").GetString());
        Assert.Equal(0, tokens[0].GetProperty("start").GetInt32());
        Assert.Equal(identifierLength, tokens[0].GetProperty("length").GetInt32());
        Assert.Equal("NumericLiteral", tokens[1].GetProperty("kind").GetString());
        Assert.Equal(integerStart, tokens[1].GetProperty("start").GetInt32());
        Assert.Equal(2, tokens[1].GetProperty("length").GetInt32());
        Assert.Equal("EndOfFile", tokens[2].GetProperty("kind").GetString());
        Assert.Equal(sourceLength, tokens[2].GetProperty("start").GetInt32());
    }

    [Theory]
    [InlineData("/* comment */ 1.25e+3f64 \"a\\U00010400\" // end")]
    [InlineData("1e+ \"\\q\"")]
    [InlineData("/* unterminated")]
    [InlineData("\"unterminated")]
    [InlineData("name \0 42")]
    [InlineData("public bool Scan() { /* first */ return true && false; }\r\n// eof")]
    [InlineData(" \t// only trivia\r\n/* end */")]
    public async Task ScannerWorkerReportsLexicalDiagnosticsAndTokenSpansAsync(string source)
    {
        var expected = Lexer.Lex(SourceText.From(source));
        var scanner = Path.Combine(RepositoryRoot(), "self-host", "Scanner.terrum");
        var (exit, output, error) = await RunAsync(
            CliStart("scan-component", scanner, "-", "--runtime", "fail-fast", "--json"),
            Encoding.UTF8.GetBytes(source)).ConfigureAwait(true);
        Assert.Equal(expected.Diagnostics.IsEmpty ? 0 : 1, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(8, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(expected.Diagnostics.IsEmpty, document.RootElement.GetProperty("success").GetBoolean());
        var diagnostics = document.RootElement.GetProperty("diagnostics");
        Assert.Equal(expected.Diagnostics.Length, diagnostics.GetArrayLength());
        for (var index = 0; index < expected.Diagnostics.Length; index++)
        {
            Assert.Equal(expected.Diagnostics[index].Code, diagnostics[index].GetProperty("code").GetString());
            Assert.Equal(expected.Diagnostics[index].Span.Start, diagnostics[index].GetProperty("start").GetInt32());
            Assert.Equal(expected.Diagnostics[index].Span.Length, diagnostics[index].GetProperty("length").GetInt32());
        }
        var tokens = document.RootElement.GetProperty("tokens");
        Assert.Equal(expected.Tokens.Length, tokens.GetArrayLength());
        for (var index = 0; index < expected.Tokens.Length; index++)
        {
            Assert.Equal(expected.Tokens[index].Kind.ToString(), tokens[index].GetProperty("kind").GetString());
            Assert.Equal(expected.Tokens[index].Span.Start, tokens[index].GetProperty("start").GetInt32());
            Assert.Equal(expected.Tokens[index].Span.Length, tokens[index].GetProperty("length").GetInt32());
            var leading = tokens[index].GetProperty("leadingTrivia");
            Assert.Equal(expected.Tokens[index].LeadingTrivia.Length, leading.GetArrayLength());
            for (var triviaIndex = 0; triviaIndex < expected.Tokens[index].LeadingTrivia.Length; triviaIndex++)
            {
                var trivia = expected.Tokens[index].LeadingTrivia[triviaIndex];
                Assert.Equal(trivia.Kind.ToString(), leading[triviaIndex].GetProperty("kind").GetString());
                Assert.Equal(trivia.Span.Start, leading[triviaIndex].GetProperty("start").GetInt32());
                Assert.Equal(trivia.Span.Length, leading[triviaIndex].GetProperty("length").GetInt32());
            }
        }
    }

    private static void AssertWindowsX64()
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "JIT execution requires Windows x64.");
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static ProcessStartInfo CliStart(params string[] args)
    {
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name ?? throw new InvalidOperationException("Missing build configuration.");
        var cli = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc", configuration, framework.Name, "terrumc.dll");
        Assert.True(File.Exists(cli), $"CLI was not built: {cli}");
        var start = new ProcessStartInfo("dotnet") {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(cli);
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        return start;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(ProcessStartInfo start, byte[] input)
    {
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start test process.");
        var output = start.RedirectStandardOutput ? process.StandardOutput.ReadToEndAsync() : Task.FromResult("");
        var error = start.RedirectStandardError ? process.StandardError.ReadToEndAsync() : Task.FromResult("");
        if (start.RedirectStandardInput)
        {
            await process.StandardInput.BaseStream.WriteAsync(input).ConfigureAwait(true);
            process.StandardInput.Close();
        }
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(true);
            }
            throw;
        }
        return (process.ExitCode, await output.ConfigureAwait(true), await error.ConfigureAwait(true));
    }
}
