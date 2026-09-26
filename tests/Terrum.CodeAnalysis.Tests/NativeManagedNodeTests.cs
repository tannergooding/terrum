// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeManagedNodeTests
{
    private const string Cycle = """
        bool Retain(out ManagedNode result) {
            require ManagedNode.TryCreate(40, out var head);
            require ManagedNode.TryCreate(2, out var child);
            head.SetNext(child);
            result = head;
            return true;
        }
        void CreateCycle() {
            require ManagedNode.TryCreate(1, out var first);
            require ManagedNode.TryCreate(2, out var second);
            first.SetNext(second);
            second.SetNext(first);
        }
        int32 Inspect(ManagedNode kept) {
            int32 freed = ManagedHeap.Collect();
            require freed == 2;
            require kept.TryGetNext(out var next);
            next.SetValue(2);
            return kept.Value + next.Value;
        }
        int32 Main() {
            require Retain(out var kept);
            CreateCycle();
            ManagedNode alias = kept;
            return Inspect(alias);
        }
        """;

    private const string MixedLibrary = """
        public struct CopyValue {
            public int32 Value;
            public CopyValue(int32 value) { Value = value; }
        }
        public unique struct UniqueValue {
            public int32 Value;
            public UniqueValue(int32 value) { Value = value; }
            [NoThrow] [Cleanup] public void Close() {}
        }
        int32 PrivateProof() {
            require ManagedNode.TryCreate(42, out var node);
            return node.Value;
        }
        public CopyValue CopyReturn() { return new CopyValue(42); }
        public int32 CopyInput(CopyValue value) { return value.Value; }
        public UniqueValue UniqueReturn() { return new UniqueValue(42); }
        public bool OwnedOutput(out OwnedInt32Buffer result) {
            return OwnedInt32Buffer.TryCreate(1, out result);
        }
        public int32 OwnedInput(OwnedInt32Buffer value) { return value.Read(0); }
        public bool OutScalar(out int32 result) { result = 42; return true; }
        public uint64 Echo(uint64 value) { return value; }
        public bool Flag(bool value) { return value; }
        public void Ping() {}
        """;

    [Fact]
    public async Task TracesCopiedHandlesAndCollectsUnreachableCyclesAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Cycle));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal("native-18", Compilation.GetNativeProfileVersion(program));
        Assert.Equal(42, await RunAsync(program).ConfigureAwait(true));
    }

    [Fact]
    public async Task ClearingAnEdgeMakesItsChildCollectibleAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            bool Build(out ManagedNode result) {
                require ManagedNode.TryCreate(40, out var parent);
                require ManagedNode.TryCreate(2, out var child);
                parent.SetNext(child);
                result = parent;
                return true;
            }
            int32 Main() {
                require Build(out var parent);
                parent.ClearNext();
                int32 freed = ManagedHeap.Collect();
                if (freed != 1) { return 0; }
                if (parent.TryGetNext(out var absent)) { return 0; }
                return parent.Value + freed + 1;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(42, await RunAsync(Assert.IsType<BoundProgram>(compilation.Program)).ConfigureAwait(true));
    }

    [Fact]
    public async Task ForConditionAndIteratorManagedOutputsRetireAfterTheLoopAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            bool Make(int32 value, out ManagedNode result) {
                if (value == 1) { return false; }
                return ManagedNode.TryCreate(value, out result);
            }
            int32 Main() {
                mut int32 iteration = 0;
                for (; Make(iteration, out var current); ManagedNode.TryCreate(2, out var updated)) {
                    if (ManagedHeap.Collect() != 0) { return 1; }
                    iteration = iteration + 1;
                }
                if (ManagedHeap.Collect() != 2) { return 2; }
                return 42;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(42, await RunAsync(Assert.IsType<BoundProgram>(compilation.Program)).ConfigureAwait(true));
    }

    [Fact]
    public void RejectsCheckedPolicyAndPersistedModules()
    {
        var checkedCompilation = Compilation.Create(SyntaxTree.Parse(Cycle), BoundRuntimePolicy.CheckedExceptions);
        Assert.Contains(checkedCompilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Proof() {
                require ManagedNode.TryCreate(42, out var node);
                return node.Value;
            }
            """));
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var program = Assert.IsType<BoundProgram>(library.Program);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateProducer(program, ["Proof"]));
        Assert.NotEmpty(NativeEmitter.EmitLibrary(program, ["Proof"], NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(program,
            ["ManagedNode.TryCreate"], NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData("CopyReturn")]
    [InlineData("CopyInput")]
    [InlineData("UniqueReturn")]
    [InlineData("OwnedOutput")]
    [InlineData("OwnedInput")]
    [InlineData("OutScalar")]
    public void ManagedImageRejectsNonScalarExportContracts(string name)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(MixedLibrary));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal("native-18", Compilation.GetNativeProfileVersion(program));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(program, [name],
            NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData("Echo")]
    [InlineData("Flag")]
    [InlineData("Ping")]
    public void ManagedImageRetainsScalarAndVoidExports(string name)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(MixedLibrary));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.NotEmpty(NativeEmitter.EmitLibrary(program, [name], NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void ScalarExportsPointToTheirSourceFunctionsWithACollector()
    {
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(repository, "tests", "native", "managed-node-abi.terrum"));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var exports = new[] { "RetainAcrossAllocationFailure", "Cycles", "AllocationFailure", "CollectWithScalar" };
        var image = NativeEmitter.EmitLibrary(program, exports, NativeRuntimePolicy.FailFast);
        using var stream = new MemoryStream(image);
        using var reader = new PEReader(stream);
        var headers = reader.PEHeaders;
        var header = Assert.IsType<PEHeader>(headers.PEHeader);
        var text = Assert.Single(headers.SectionHeaders, static section => section.Name == ".text");
        var pdata = Assert.Single(headers.SectionHeaders, static section => section.Name == ".pdata");
        var edata = Assert.Single(headers.SectionHeaders, static section => section.Name == ".edata");
        var sourceStarts = Enumerable.Range(0, header.ExceptionTableDirectory.Size / 12)
            .Select(index => Read32(pdata.PointerToRawData + (12 * index)))
            .Where(rva => {
                var offset = text.PointerToRawData + rva - text.VirtualAddress;
                return image[offset] == 0x55 && image[offset + 1] == 0x48
                    && image[offset + 2] == 0x81 && image[offset + 3] == 0xEC
                    && image[offset + 8] == 0x48 && image[offset + 9] == 0x89
                    && image[offset + 10] == 0xE5;
            })
            .ToArray();
        Assert.DoesNotContain(program.Functions, static function => function.IsExternal);
        Assert.Equal(program.Functions.Length, sourceStarts.Length);

        var exportAt = edata.PointerToRawData;
        var addresses = Read32(exportAt + 28);
        var names = Read32(exportAt + 32);
        Assert.Equal(exports.Length, Read32(exportAt + 20));
        foreach (var (name, ordinal) in exports.Order(StringComparer.Ordinal).Select((name, index) => (name, index)))
        {
            var nameAt = EdataOffset(Read32(EdataOffset(names) + (ordinal * 4)));
            var nameEnd = Array.IndexOf(image, (byte)0, nameAt);
            Assert.Equal(name, Encoding.ASCII.GetString(image, nameAt, nameEnd - nameAt));
            var sourceIndex = Array.FindIndex(program.Functions.ToArray(), function => function.Name == name);
            Assert.True(sourceIndex >= 0);
            Assert.Equal(sourceStarts[sourceIndex], Read32(EdataOffset(addresses) + (ordinal * 4)));
        }

        int Read32(int offset)
        {
            return BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(offset, 4));
        }

        int EdataOffset(int rva)
        {
            return checked(edata.PointerToRawData + rva - edata.VirtualAddress);
        }
    }

    [Fact]
    public void RejectsManagedGraphLibraryBeforeSerialization()
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Leaf() { return 1; }
            """));
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var leaf = NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(library.Program),
            ["Leaf"], "ManagedNodeDependency", NativeRuntimePolicy.FailFast);
        var managed = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Proof() {
                require ManagedNode.TryCreate(42, out var node);
                return node.Value;
            }
            """));
        Assert.False(managed.HasErrors, string.Join(Environment.NewLine, managed.Diagnostics));
        var program = Assert.IsType<BoundProgram>(managed.Program);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.CreateGraphProducer(program,
            ["Proof"], "ManagedNodeGraph", [leaf], NativeRuntimePolicy.FailFast));
        var consumer = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                require ManagedNode.TryCreate(42, out var node);
                return node.Value;
            }
            """));
        Assert.False(consumer.HasErrors, string.Join(Environment.NewLine, consumer.Diagnostics));
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(consumer.Program), [leaf], NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void MalformedManagedFunctionAndVariableArraysReportArgumentErrors()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                require ManagedNode.TryCreate(42, out var node);
                return node.Value;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var entry = program.Functions[program.EntryPoint];
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, null!),
        }));
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, entry with { Variables = default }),
        }));
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint,
                entry with { Variables = entry.Variables.SetItem(0, null!) }),
        }));
    }

    [Fact]
    public void RejectsManagedFieldsAndForgedRootRetirement()
    {
        var field = Compilation.Create(SyntaxTree.Parse("""
            struct Wrapper {
                ManagedNode Item;
            }
            int32 Main() { return 42; }
            """));
        Assert.Contains(field.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");

        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                require ManagedNode.TryCreate(42, out var node);
                return node.Value;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var entry = program.Functions[program.EntryPoint];
        var forged = program with {
            Functions = program.Functions.SetItem(program.EntryPoint,
                entry with { Body = entry.Body with { RetiredManagedLocals = [] } }),
        };
        var error = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
        Assert.Equal("Ownership", error.Data["Terrum.InvalidEffect"]);
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void ForgedInnerBlockCannotRetireAnOuterManagedRoot()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                require ManagedNode.TryCreate(42, out var kept);
                { int32 unrelated = 0; }
                return kept.Value;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var entry = program.Functions[program.EntryPoint];
        var slot = Assert.Single(entry.Variables.Where(static variable => variable.Name == "kept"));
        var root = entry.Body;
        var inner = Assert.Single(root.Statements.OfType<BoundBlock>());
        var forged = program with {
            Functions = program.Functions.SetItem(program.EntryPoint,
                entry with {
                    Body = root with {
                        RetiredManagedLocals = [],
                        Statements = root.Statements.Replace(inner, inner with {
                            Span = root.Span,
                            RetiredManagedLocals = [entry.Variables.IndexOf(slot)],
                        }),
                    },
                }),
        };
        var error = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
        Assert.Equal("Ownership", error.Data["Terrum.InvalidEffect"]);
    }

    private static async Task<int> RunAsync(BoundProgram program)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var path = Path.Combine(Directory.GetCurrentDirectory(), $"terrum-managed-nodes-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Managed-node test process did not start.");
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
            return process.ExitCode;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
