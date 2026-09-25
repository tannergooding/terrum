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
using Xunit;

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

    [Fact]
    public void ImagesHaveDeterministicRelocatableRvaLayout()
    {
        var first = NativeEmitter.Emit(Compile(Source), NativeRuntimePolicy.FailFast);
        Assert.Equal(first, NativeEmitter.Emit(Compile(Source), NativeRuntimePolicy.FailFast));
        var second = NativeEmitter.Emit(Compile(Source.Replace("Recursive(5)", "Recursive(4)", StringComparison.Ordinal)),
            NativeRuntimePolicy.FailFast);
        Assert.NotEqual(first, second);
        ValidateLayout(first);
        ValidateLayout(second);
    }

    [Fact]
    public async Task FreshProcessesExecuteReadOnlyWritableUnwindAndCliJitPathsAsync()
    {
        var program = Compile(Source);
        var prefix = Path.Combine(AppContext.BaseDirectory, $"relocation-{Guid.NewGuid():N}");
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
            Assert.False(unused.HasErrors, string.Join(Environment.NewLine, unused.Diagnostics));
            var producerBytes = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(unused.Program), ["Unused"]);
            var moduleCompilation = Compilation.Create(SyntaxTree.Parse(ModuleSource));
            Assert.False(moduleCompilation.HasErrors, string.Join(Environment.NewLine, moduleCompilation.Diagnostics));
            await File.WriteAllBytesAsync(producer, producerBytes).ConfigureAwait(true);
            await File.WriteAllBytesAsync(consumer, ModuleCompiler.CreateConsumer(
                Assert.IsType<BoundProgram>(moduleCompilation.Program), producerBytes)).ConfigureAwait(true);
            var framework = new DirectoryInfo(AppContext.BaseDirectory);
            var configuration = framework.Parent?.Name ?? throw new InvalidOperationException("Missing configuration.");
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
            var cli = Path.Combine(root, "artifacts", "bin", "sources", "terrumc", configuration, framework.Name, "terrumc.dll");
            await RunAsync("dotnet", [cli, "run-module", consumer, producer, "--runtime", "fail-fast"], 42).ConfigureAwait(true);
        }
        finally
        {
            File.Delete(executable);
            File.Delete(failure);
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    private static void ValidateLayout(byte[] image)
    {
        using var stream = new MemoryStream(image);
        using var reader = new PEReader(stream);
        Assert.Equal(Machine.Amd64, reader.PEHeaders.CoffHeader.Machine);
        Assert.Equal(0, (int)reader.PEHeaders.CoffHeader.Characteristics & 1);
        var header = Assert.IsType<PEHeader>(reader.PEHeaders.PEHeader);
        Assert.Equal(PEMagic.PE32Plus, header.Magic);
        Assert.Equal(0x140000000UL, header.ImageBase);
        Assert.Equal(0x1000, header.SectionAlignment);
        Assert.Equal(0x200, header.FileAlignment);
        Assert.Equal(0x60, (int)header.DllCharacteristics & 0x60);
        Assert.Equal(0, header.BaseRelocationTableDirectory.RelativeVirtualAddress);
        Assert.Equal(0, header.BaseRelocationTableDirectory.Size);
        Assert.NotEqual(0, header.ImportTableDirectory.Size);
        Assert.NotEqual(0, header.ImportAddressTableDirectory.Size);
        Assert.NotEqual(0, header.ExceptionTableDirectory.Size);
        Assert.Equal(0, header.CorHeaderTableDirectory.Size);
        Assert.DoesNotContain(reader.PEHeaders.SectionHeaders, static section => section.Name == ".reloc");
        foreach (var section in reader.PEHeaders.SectionHeaders)
        {
            Assert.Equal(0, section.VirtualAddress % header.SectionAlignment);
            Assert.Equal(0, section.PointerToRawData % header.FileAlignment);
            Assert.InRange((long)section.VirtualAddress + section.VirtualSize, 0, header.SizeOfImage);
            Assert.InRange((long)section.PointerToRawData + section.SizeOfRawData, 0, image.Length);
        }
        Assert.Contains(reader.PEHeaders.SectionHeaders, static section => section.Name == ".rdata");
        Assert.Contains(reader.PEHeaders.SectionHeaders, static section => section.Name == ".data");
        Assert.Contains(reader.PEHeaders.SectionHeaders, static section => section.Name == ".pdata");
        Assert.Contains(reader.PEHeaders.SectionHeaders, static section => section.Name == ".xdata");
        AssertDirectoryInSection(header.ImportTableDirectory, ".idata");
        AssertDirectoryInSection(header.ImportAddressTableDirectory, ".idata");
        AssertDirectoryInSection(header.ExceptionTableDirectory, ".pdata");

        void AssertDirectoryInSection(DirectoryEntry directory, string name)
        {
            var section = Assert.Single(reader.PEHeaders.SectionHeaders, section => section.Name == name);
            Assert.InRange(directory.RelativeVirtualAddress, section.VirtualAddress,
                section.VirtualAddress + section.VirtualSize - directory.Size);
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
        Assert.Equal(expected, process.ExitCode);
        Assert.Empty(await error.ConfigureAwait(true));
    }
}
