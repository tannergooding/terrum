// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.
#:project ../sources/Terrum.CodeAnalysis/Terrum.CodeAnalysis.csproj

using System.Diagnostics;
using System.Globalization;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

if (!OperatingSystem.IsWindows())
{
    throw new PlatformNotSupportedException("The independent relocation probe requires Windows x64.");
}

const string Source = """
    public struct Pair {
        public int32 First;
        public int32 Second;
        public Pair(int32 first, int32 second) { First = first; Second = second; }
    }
    public Pair MakePair(int32 first, int32 second) { return new Pair(first, second); }
    public int32 UsePair(Pair value, int32 a, int32 b, int32 c, int32 d) {
        return value.First + value.Second + a + b + c + d;
    }
    public int32 ReadData() { return Host.Input[0] + Host.Input[1]; }
    public int32 WriteData(int32 value) {
        Host.Output[0] = value;
        return Host.Output[0] + Host.Input[0];
    }
    public int32 Recursive(int32 depth) {
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
var buffers = new[] {
    new HostBuffer("Input", PrimitiveType.Int32, [3, 4], IsWritable: false),
    new HostBuffer("Output", PrimitiveType.Int32, [0], IsWritable: true),
};
var compilation = Compilation.Create(SyntaxTree.Parse(Source), [.. buffers]);
if (compilation.HasErrors || compilation.Program is not BoundProgram program)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, compilation.Diagnostics));
}
var secondCompilation = Compilation.Create(SyntaxTree.Parse(Source.Replace("Recursive(5)", "Recursive(4)", StringComparison.Ordinal)),
    [.. buffers]);
if (secondCompilation.HasErrors || secondCompilation.Program is not BoundProgram secondProgram)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, secondCompilation.Diagnostics));
}

var code = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast);
var image = NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast);
var secondCode = NativeEmitter.EmitCode(secondProgram, NativeRuntimePolicy.FailFast);
var secondImage = NativeEmitter.Emit(secondProgram, NativeRuntimePolicy.FailFast);
var binaryDirectory = Path.GetFullPath(Path.Combine("artifacts", "bin", "native-probes", "native-relocation"));
Directory.CreateDirectory(binaryDirectory);
var imagePath = Path.Combine(binaryDirectory, "relocation-proof.exe");
var secondPath = Path.Combine(binaryDirectory, "relocation-proof-second.exe");
var failurePath = Path.Combine(binaryDirectory, "relocation-failure.exe");
var probePath = Path.Combine(binaryDirectory, "relocation-probe.exe");
if (!File.Exists(probePath))
{
    throw new FileNotFoundException("Build tests\\native\\relocation-probe.c into artifacts\\bin\\native-probes\\native-relocation before running the relocation check.", probePath);
}
try
{
    await File.WriteAllBytesAsync(imagePath, image).ConfigureAwait(false);
    await File.WriteAllBytesAsync(secondPath, secondImage).ConfigureAwait(false);
    var failedCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { require false; return 0; }"));
    if (failedCompilation.HasErrors || failedCompilation.Program is not BoundProgram failedProgram)
    {
        throw new InvalidOperationException(string.Join(Environment.NewLine, failedCompilation.Diagnostics));
    }
    await File.WriteAllBytesAsync(failurePath, NativeEmitter.Emit(failedProgram, NativeRuntimePolicy.FailFast))
        .ConfigureAwait(false);
    var start = new ProcessStartInfo(probePath) {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    start.ArgumentList.Add(imagePath);
    start.ArgumentList.Add(secondPath);
    foreach (var (currentCode, currentProgram) in new[] { (code, program), (secondCode, secondProgram) })
    {
        var ranges = currentCode.Functions.OrderBy(static range => range.Start).ToArray();
        var sourceStart = ranges.Length - currentProgram.Functions.Length;
        foreach (var name in new[] { "MakePair", "UsePair", "ReadData", "WriteData", "Recursive" })
        {
            var index = -1;
            for (var i = 0; i < currentProgram.Functions.Length; i++)
            {
                if (currentProgram.Functions[i].Name == name)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                throw new InvalidOperationException($"Missing relocation fixture function: {name}");
            }
            start.ArgumentList.Add((0x1000 + ranges[sourceStart + index].Start).ToString(CultureInfo.InvariantCulture));
        }
    }
    await RunProbe(start).ConfigureAwait(false);
    foreach (var (path, expected) in new[] {
        (imagePath, 42u),
        (secondPath, 42u),
        (failurePath, 0xE0540003u),
    })
    {
        var processStart = new ProcessStartInfo(probePath) {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        processStart.ArgumentList.Add("--process");
        processStart.ArgumentList.Add(path);
        processStart.ArgumentList.Add(expected.ToString(CultureInfo.InvariantCulture));
        await RunProbe(processStart).ConfigureAwait(false);
    }
}
finally
{
    File.Delete(imagePath);
    File.Delete(secondPath);
    File.Delete(failurePath);
}

static async Task RunProbe(ProcessStartInfo start)
{
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start relocation probe.");
    var output = process.StandardOutput.ReadToEndAsync();
    var error = process.StandardError.ReadToEndAsync();
    try
    {
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    }
    catch (TimeoutException)
    {
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync().ConfigureAwait(false);
        throw;
    }
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"Windows relocation probe failed with status {process.ExitCode}: {await error.ConfigureAwait(false)}");
    }
    Console.Write(await output.ConfigureAwait(false));
}
