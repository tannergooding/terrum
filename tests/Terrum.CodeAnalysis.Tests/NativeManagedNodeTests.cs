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
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeManagedNodeTests
{
    private const string Cycle = """
        bool Retain(out write ManagedNode result) {
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
        int32 Inspect(write ManagedNode kept) {
            int32 freed = ManagedHeap.Collect();
            require freed == 2;
            require kept.TryGetNext(out var next);
            next.SetValue(2);
            return kept.Value + next.Value;
        }
        int32 Main() {
            require Retain(out var kept);
            CreateCycle();
            write ManagedNode retained = kept;
            return Inspect(retained);
        }
        """;

    private const string MixedLibrary = """
        [Blittable] [Layout(LayoutKind.Sequential)] public struct CopyValue {
            public int32 Value;
            public CopyValue(int32 value) { Value = value; }
        }
        [Blittable] [Layout(LayoutKind.Sequential)] public unique struct UniqueValue {
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

    [Test]
    public async Task TracesCopiedHandlesAndCollectsUnreachableCyclesAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Cycle));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        Assert.That(Compilation.GetNativeProfileVersion(program), Is.EqualTo("native-18"));
        Assert.That(await RunAsync(program).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public void RuntimeMetadataIsNotReturnedAsHostData()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                require ManagedNode.TryCreate(42, out var node);
                return node.Value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        var code = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast);
        Assert.That(code.ManagedRuntimeOffset >= 0, Is.True);
        Assert.That(code.HostDataLength, Is.EqualTo(0));

        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        var result = NativeJit.ExecuteWithData(program, NativeRuntimePolicy.FailFast);

        Assert.That(result.ExitCode, Is.EqualTo(42));
        Assert.That(result.Data, Is.Empty);
    }

    [Test]
    public async Task ClearingAnEdgeMakesItsChildCollectibleAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            bool Build(out write ManagedNode result) {
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(await RunAsync(((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))()).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public async Task InterleavedManagedAndScalarOutputsKeepTheirRootSlotsAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)] struct LargeOutput {
                public int64 First;
                public int64 Second;
                public int64 Third;
                public LargeOutput() { First = 0; Second = 0; Third = 42; }
            }
            bool Create(out LargeOutput large, out int32 count, out write ManagedNode first, out int32 marker,
                out write ManagedNode second) {
                large = new LargeOutput();
                count = 40;
                marker = 2;
                require ManagedNode.TryCreate(1, out first);
                require ManagedNode.TryCreate(2, out second);
                return true;
            }
            int32 Main() {
                require Create(out var large, out var count, out var first, out var marker, out var second);
                if (large.Third != 42 || marker != 2) { return 0; }
                require ManagedHeap.Collect() == 0;
                return count + first.Value + second.Value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(await RunAsync(((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))()).ConfigureAwait(true), Is.EqualTo(43));
    }

    [Test]
    public async Task ForConditionAndIteratorManagedOutputsRetireAfterTheLoopAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            bool Make(int32 value, out write ManagedNode result) {
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(await RunAsync(((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))()).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public void AcceptsCheckedPolicyAndPrivateNodeProducerModules()
    {
        var checkedCompilation = Compilation.Create(SyntaxTree.Parse(Cycle), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(checkedCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, checkedCompilation.Diagnostics));
        Assert.That(Compilation.GetNativeProfileVersion(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = checkedCompilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))()), Is.EqualTo("native-18"));
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Proof() {
                require ManagedNode.TryCreate(42, out var node);
                return node.Value;
            }
            """));
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = library.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))();
        var module = ModuleCompiler.CreateProducer(program, ["Proof"]);
        Assert.That(ModuleCompiler.GetNativeProfileVersion(module, false), Is.EqualTo("native-18"));
        Assert.That(NativeEmitter.EmitLibrary(program, ["Proof"], NativeRuntimePolicy.FailFast), Is.Not.Empty);
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(program,
            ["ManagedNode.TryCreate"], NativeRuntimePolicy.FailFast))!;
    }
    [TestCase("UniqueReturn",BoundRuntimePolicy.FailFast)]
    [TestCase("OwnedOutput",BoundRuntimePolicy.FailFast)]
    [TestCase("OwnedInput",BoundRuntimePolicy.FailFast)]
    [TestCase("OwnedInput",BoundRuntimePolicy.CheckedExceptions)]
    public void ManagedImageRejectsOwningExportContracts(string name, BoundRuntimePolicy policy)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(MixedLibrary), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = compilation.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))();
        Assert.That(Compilation.GetNativeProfileVersion(program), Is.EqualTo("native-18"));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(program, [name],
            (NativeRuntimePolicy)policy))!;
    }
    [TestCase("Echo",BoundRuntimePolicy.FailFast)]
    [TestCase("Flag",BoundRuntimePolicy.FailFast)]
    [TestCase("Ping",BoundRuntimePolicy.FailFast)]
    [TestCase("CopyReturn",BoundRuntimePolicy.FailFast)]
    [TestCase("CopyInput",BoundRuntimePolicy.FailFast)]
    [TestCase("OutScalar",BoundRuntimePolicy.FailFast)]
    [TestCase("Echo",BoundRuntimePolicy.CheckedExceptions)]
    [TestCase("CopyReturn",BoundRuntimePolicy.CheckedExceptions)]
    [TestCase("CopyInput",BoundRuntimePolicy.CheckedExceptions)]
    [TestCase("OutScalar",BoundRuntimePolicy.CheckedExceptions)]
    public void ManagedImageRetainsCopyScalarAndVoidExports(string name, BoundRuntimePolicy policy)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(MixedLibrary), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = compilation.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))();
        Assert.That(NativeEmitter.EmitLibrary(program, [name], (NativeRuntimePolicy)policy), Is.Not.Empty);
    }
    [TestCase(BoundRuntimePolicy.FailFast,"TRM3022")]
    [TestCase(BoundRuntimePolicy.CheckedExceptions,"TRM3021")]
    public void ManagedRecoveryKeepsCheckedAndUnsafeContracts(BoundRuntimePolicy policy, string diagnostic)
    {
        var source = policy == BoundRuntimePolicy.FailFast
            ? "int32 Main() { require ManagedNode.TryCreate(42, out var node); unsafe { try { require false; } catch { return node.Value; } } return 0; }"
            : "int32 Main() { require ManagedNode.TryCreate(42, out var node); try { require false; } catch { return node.Value; } return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source), policy);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,item => item.Code == diagnostic),Is.True);
    }

    [Test]
    public void CheckedManagedMalformedRootsFailBeforeEmissionOrModuleStorage()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Proof() {
                require ManagedNode.TryCreate(42, out var node);
                return node.Value;
            }
            """), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = compilation.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))();
        Assert.That(ModuleCompiler.CreateProducer(program, ["Proof"]), Is.Not.Empty);
        var functionIndex = program.Functions.IndexOf(global::System.Linq.Enumerable.Single(program.Functions, static item => item.Name == "Proof"));
        var function = program.Functions[functionIndex];
        var forged = program with {
            Functions = program.Functions.SetItem(functionIndex,
                function with { Body = function.Body with { RetiredManagedLocals = [] } }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(forged))!;
        _ = Assert.Throws<ArgumentException>(() => ModuleCompiler.CreateProducer(forged, ["Proof"]))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(forged,
            ["Proof"], NativeRuntimePolicy.CheckedExceptions))!;
    }

    [Test]
    public void ScalarExportsPointToTheirSourceFunctionsWithACollector()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(NativeFixture("managed-node-abi.terrum")));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = compilation.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))();
        var exports = new[] { "RetainAcrossAllocationFailure", "Cycles", "AllocationFailure", "CollectWithScalar" };
        var image = NativeEmitter.EmitLibrary(program, exports, NativeRuntimePolicy.FailFast);
        using var stream = new MemoryStream(image);
        using var reader = new PEReader(stream);
        var headers = reader.PEHeaders;
        var header = ((global::System.Func<PEHeader>)(() => { var __nunitActual11 = headers.PEHeader; Assert.That(__nunitActual11, Is.TypeOf<PEHeader>()); return (PEHeader)__nunitActual11!; }))();
        var text = global::System.Linq.Enumerable.Single(headers.SectionHeaders, static section => section.Name == ".text");
        var pdata = global::System.Linq.Enumerable.Single(headers.SectionHeaders, static section => section.Name == ".pdata");
        var edata = global::System.Linq.Enumerable.Single(headers.SectionHeaders, static section => section.Name == ".edata");
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
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,static function => function.IsExternal),Is.False);
        Assert.That(sourceStarts.Length, Is.EqualTo(program.Functions.Length));

        var exportAt = edata.PointerToRawData;
        var addresses = Read32(exportAt + 28);
        var names = Read32(exportAt + 32);
        Assert.That(Read32(exportAt + 20), Is.EqualTo(exports.Length));
        foreach (var (name, ordinal) in exports.Order(StringComparer.Ordinal).Select((name, index) => (name, index)))
        {
            var nameAt = EdataOffset(Read32(EdataOffset(names) + (ordinal * 4)));
            var nameEnd = Array.IndexOf(image, (byte)0, nameAt);
            Assert.That(Encoding.ASCII.GetString(image, nameAt, nameEnd - nameAt), Is.EqualTo(name));
            var sourceIndex = Array.FindIndex(program.Functions.ToArray(), function => function.Name == name);
            Assert.That(sourceIndex >= 0, Is.True);
            Assert.That(Read32(EdataOffset(addresses) + (ordinal * 4)), Is.EqualTo(sourceStarts[sourceIndex]));
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

    [Test]
    public void CheckedManagedScalarProbeCompilesWithoutSourceErrors()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(NativeFixture("managed-unwind-abi.terrum")),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual12 = compilation.Program; Assert.That(__nunitActual12, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual12!; }))();
        Assert.That(NativeEmitter.EmitLibrary(program, [
            "SameFrame", "EscapingFrame", "LaterArgument", "OutputFailure", "ReturnAcrossCleanup",
            "EscapeToNative", "CollectWithScalar",
        ], NativeRuntimePolicy.CheckedExceptions), Is.Not.Empty);
    }

    [Test]
    public void RejectsManagedGraphLibraryBeforeSerialization()
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Leaf() { return 1; }
            """));
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var leaf = NativeModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual13 = library.Program; Assert.That(__nunitActual13, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual13!; }))(),
            ["Leaf"], "ManagedNodeDependency", NativeRuntimePolicy.FailFast);
        var managed = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Proof() {
                require ManagedNode.TryCreate(42, out var node);
                return node.Value;
            }
            """));
        Assert.That(managed.HasErrors, Is.False, string.Join(Environment.NewLine, managed.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual14 = managed.Program; Assert.That(__nunitActual14, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual14!; }))();
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.CreateGraphProducer(program,
            ["Proof"], "ManagedNodeGraph", [leaf], NativeRuntimePolicy.FailFast))!;
        var consumer = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                require ManagedNode.TryCreate(42, out var node);
                return node.Value;
            }
            """));
        Assert.That(consumer.HasErrors, Is.False, string.Join(Environment.NewLine, consumer.Diagnostics));
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual15 = consumer.Program; Assert.That(__nunitActual15, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual15!; }))(), [leaf], NativeRuntimePolicy.FailFast))!;
    }

    [Test]
    public void MalformedManagedFunctionAndVariableArraysReportArgumentErrors()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                require ManagedNode.TryCreate(42, out var node);
                return node.Value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual16 = compilation.Program; Assert.That(__nunitActual16, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual16!; }))();
        var entry = program.Functions[program.EntryPoint];
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, null!),
        }))!;
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, entry with { Variables = default }),
        }))!;
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint,
                entry with { Variables = entry.Variables.SetItem(0, null!) }),
        }))!;
    }

    [Test]
    public void RejectsManagedFieldsAndForgedRootRetirement()
    {
        var field = Compilation.Create(SyntaxTree.Parse("""
            struct Wrapper {
                ManagedNode Item;
            }
            int32 Main() { return 42; }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(field.Diagnostics,diagnostic => diagnostic.Code == "TRM3023"),Is.True);

        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                require ManagedNode.TryCreate(42, out var node);
                return node.Value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual17 = compilation.Program; Assert.That(__nunitActual17, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual17!; }))();
        var entry = program.Functions[program.EntryPoint];
        var forged = program with {
            Functions = program.Functions.SetItem(program.EntryPoint,
                entry with { Body = entry.Body with { RetiredManagedLocals = [] } }),
        };
        var error = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged))!;
        Assert.That(error.Data["Terrum.InvalidEffect"], Is.EqualTo("Ownership"));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast))!;
    }

    [Test]
    public void ForgedInnerBlockCannotRetireAnOuterManagedRoot()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                require ManagedNode.TryCreate(42, out var kept);
                { int32 unrelated = 0; }
                return kept.Value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual18 = compilation.Program; Assert.That(__nunitActual18, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual18!; }))();
        var entry = program.Functions[program.EntryPoint];
        var slot = global::System.Linq.Enumerable.Single(entry.Variables.Where(static variable => variable.Name == "kept"));
        var root = entry.Body;
        var inner = global::System.Linq.Enumerable.Single(root.Statements.OfType<BoundBlock>());
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
        var error = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged))!;
        Assert.That(error.Data["Terrum.InvalidEffect"], Is.EqualTo("Ownership"));
    }

    private static string NativeFixture(string name)
    {
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(repository, "tests", "native", name));
    }

    private static async Task<int> RunAsync(BoundProgram program)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
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
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }
}
