// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class PeRelocationTests
{
    private const string Source = """
        struct Pair {
            public int32 First;
            public int32 Second;
            public Pair(int32 first, int32 second) { First = first; Second = second; }
        }
        Pair MakePair(int32 first, int32 second) { return new Pair(first, second); }
        int32 UsePair(Pair value, int32 a, int32 b, int32 c, int32 d) {
            return value.First + value.Second + a + b + c + d;
        }
        int32 ReadData() { return Host.Input[0] + Host.Input[1]; }
        int32 WriteData(int32 value) {
            Host.Output[0] = value;
            return Host.Output[0] + Host.Input[0];
        }
        int32 Recursive(int32 depth) {
            if (depth == 0) { return 42; }
            return Recursive(depth - 1);
        }
        int32 Main() {
            require ReadData() == 7;
            require WriteData(39) == 42;
            Pair value = MakePair(17, 25);
            require UsePair(value, 1, 2, 3, 11) == 59;
            return Recursive(5);
        }
        """;
    private const string ModuleSource = """
        struct Pair {
            public int32 First;
            public int32 Second;
            public Pair(int32 first, int32 second) { First = first; Second = second; }
        }
        Pair MakePair(int32 first, int32 second) { return new Pair(first, second); }
        int32 UsePair(Pair value, int32 a, int32 b, int32 c, int32 d) {
            return value.First + value.Second + a + b + c + d;
        }
        int32 Recursive(int32 depth) {
            if (depth == 0) { return 42; }
            return Recursive(depth - 1);
        }
        int32 Main() {
            require UsePair(MakePair(17, 25), 1, 2, 3, 11) == 59;
            return Recursive(5);
        }
        """;

    [Test]
    public void ImagesHaveDeterministicRelocatableRvaLayout()
    {
        var first = NativeEmitter.Emit(Compile(Source), NativeRuntimePolicy.FailFast);
        Assert.That(NativeEmitter.Emit(Compile(Source), NativeRuntimePolicy.FailFast), Is.EqualTo(first));
        var second = NativeEmitter.Emit(Compile(Source.Replace("Recursive(5)", "Recursive(4)", StringComparison.Ordinal)),
            NativeRuntimePolicy.FailFast);
        Assert.That(second, Is.Not.EqualTo(first));
        ValidateLayout(first);
        ValidateLayout(second);
    }

    [Test]
    public async Task FreshProcessesExecuteReadOnlyWritableUnwindAndCliJitPathsAsync()
    {
        var program = Compile(Source);
        var prefix = Path.Combine(TestArtifactPaths.DirectoryPath, $"relocation-{Guid.NewGuid():N}");
        var executable = prefix + ".exe";
        var failure = prefix + "-failure.exe";
        var consumer = prefix + ".consumer.trm";
        var producer = prefix + ".producer.trm";
        try
        {
            await File.WriteAllBytesAsync(executable, NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast)).ConfigureAwait(true);
            await RunAsync(executable, [], 42).ConfigureAwait(true);
            var failed = Compile("int32 Main() { require false; return 0; }");
            await File.WriteAllBytesAsync(failure, NativeEmitter.Emit(failed, NativeRuntimePolicy.FailFast)).ConfigureAwait(true);
            await RunAsync(failure, [], unchecked((int)0xE0540003)).ConfigureAwait(true);

            var unused = Compilation.CreateLibrary(SyntaxTree.Parse("public int32 Unused() { return 0; }"));
            Assert.That(unused.HasErrors, Is.False, string.Join(Environment.NewLine, unused.Diagnostics));
            var producerBytes = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = unused.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(), ["Unused"]);
            var moduleCompilation = Compilation.Create(SyntaxTree.Parse(ModuleSource));
            Assert.That(moduleCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, moduleCompilation.Diagnostics));
            await File.WriteAllBytesAsync(producer, producerBytes).ConfigureAwait(true);
            await File.WriteAllBytesAsync(consumer, ModuleCompiler.CreateConsumer(
                ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = moduleCompilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), producerBytes)).ConfigureAwait(true);
            var framework = new DirectoryInfo(AppContext.BaseDirectory);
            var configuration = framework.Parent?.Name ?? throw new InvalidOperationException("Missing configuration.");
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
            var cli = Path.Combine(root, "artifacts", "bin", "sources", "terrumc", configuration, framework.Name, "terrumc.dll");
            await RunAsync("dotnet", [cli, "run-module", consumer, producer, "--runtime", "fail-fast"], 42).ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(failure).ConfigureAwait(true);
            File.Delete(consumer);
            File.Delete(producer);
        }
    }

    private static BoundProgram Compile(string source)
    {
        ImmutableArray<HostBuffer> buffers = [
            new HostBuffer("Input", PrimitiveType.Int32, [3, 4], IsWritable: false),
            new HostBuffer("Output", PrimitiveType.Int32, [0], IsWritable: true),
        ];
        var compilation = Compilation.Create(SyntaxTree.Parse(source), buffers);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
    }

    private static void ValidateLayout(byte[] image)
    {
        using var stream = new MemoryStream(image);
        using var reader = new PEReader(stream);
        Assert.That(reader.PEHeaders.CoffHeader.Machine, Is.EqualTo(Machine.Amd64));
        Assert.That((int)reader.PEHeaders.CoffHeader.Characteristics & 1, Is.EqualTo(0));
        var header = ((global::System.Func<PEHeader>)(() => { var __nunitActual3 = reader.PEHeaders.PEHeader; Assert.That(__nunitActual3, Is.TypeOf<PEHeader>()); return (PEHeader)__nunitActual3!; }))();
        Assert.That(header.Magic, Is.EqualTo(PEMagic.PE32Plus));
        Assert.That(header.ImageBase, Is.EqualTo(0x140000000UL));
        Assert.That(header.SectionAlignment, Is.EqualTo(0x1000));
        Assert.That(header.FileAlignment, Is.EqualTo(0x200));
        Assert.That((int)header.DllCharacteristics & 0x60, Is.EqualTo(0x60));
        Assert.That(header.BaseRelocationTableDirectory.RelativeVirtualAddress, Is.EqualTo(0));
        Assert.That(header.BaseRelocationTableDirectory.Size, Is.EqualTo(0));
        Assert.That(header.ImportTableDirectory.Size, Is.Not.EqualTo(0));
        Assert.That(header.ImportAddressTableDirectory.Size, Is.Not.EqualTo(0));
        Assert.That(header.ExceptionTableDirectory.Size, Is.Not.EqualTo(0));
        Assert.That(header.CorHeaderTableDirectory.Size, Is.EqualTo(0));
        Assert.That(global::System.Linq.Enumerable.Any(reader.PEHeaders.SectionHeaders,static section => section.Name == ".reloc"),Is.False);
        foreach (var section in reader.PEHeaders.SectionHeaders)
        {
            Assert.That(section.VirtualAddress % header.SectionAlignment, Is.EqualTo(0));
            Assert.That(section.PointerToRawData % header.FileAlignment, Is.EqualTo(0));
            Assert.That((long)section.VirtualAddress + section.VirtualSize, Is.InRange(0, header.SizeOfImage));
            Assert.That((long)section.PointerToRawData + section.SizeOfRawData, Is.InRange(0, image.Length));
        }
        Assert.That(global::System.Linq.Enumerable.Any(reader.PEHeaders.SectionHeaders,static section => section.Name == ".rdata"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(reader.PEHeaders.SectionHeaders,static section => section.Name == ".data"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(reader.PEHeaders.SectionHeaders,static section => section.Name == ".pdata"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(reader.PEHeaders.SectionHeaders,static section => section.Name == ".xdata"),Is.True);
        AssertDirectoryInSection(header.ImportTableDirectory, ".idata");
        AssertDirectoryInSection(header.ImportAddressTableDirectory, ".idata");
        AssertDirectoryInSection(header.ExceptionTableDirectory, ".pdata");

        void AssertDirectoryInSection(DirectoryEntry directory, string name)
        {
            var section = global::System.Linq.Enumerable.Single(reader.PEHeaders.SectionHeaders, section => section.Name == name);
            Assert.That(directory.RelativeVirtualAddress, Is.InRange(section.VirtualAddress, section.VirtualAddress + section.VirtualSize - directory.Size));
        }
    }

    private static async Task RunAsync(string executable, ImmutableArray<string> arguments, int expected)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start native worker.");
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().ConfigureAwait(true);
            throw;
        }
        Assert.That(process.ExitCode, Is.EqualTo(expected));
        Assert.That(await error.ConfigureAwait(true), Is.Empty);
    }
}
