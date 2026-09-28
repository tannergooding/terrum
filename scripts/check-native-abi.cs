// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.
#:project ../sources/Terrum.CodeAnalysis/Terrum.CodeAnalysis.csproj

using System.Diagnostics;
using System.Globalization;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

if (!OperatingSystem.IsWindows())
{
    throw new PlatformNotSupportedException("The ABI probe requires Windows x64.");
}

var source = File.ReadAllText(Path.Combine("samples", "native", "aggregate-abi.terrum"));
var compilation = Compilation.Create(SyntaxTree.Parse(source));
if (compilation.HasErrors || compilation.Program is not BoundProgram program)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, compilation.Diagnostics));
}

var code = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast);
var image = NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast);
var imagePath = Path.GetFullPath(Path.Combine("artifacts", "aggregate-abi.exe"));
File.WriteAllBytes(imagePath, image);

// Source functions follow callable/process entry and failure helpers.
// Match sorted code ranges rather than PE table slots.
var ranges = code.Functions.OrderBy(static function => function.Start).ToArray();
var sourceStart = ranges.Length - program.Functions.Length;
var start = new ProcessStartInfo(Path.GetFullPath(Path.Combine("artifacts", "abi-probe.exe"))) {
    UseShellExecute = false,
    CreateNoWindow = true,
};
start.ArgumentList.Add(imagePath);
foreach (var name in new[] {
    "MakePair", "ReadPair", "TryPair", "TryInt32", "TryChar16", "TryBool", "ReadChar16", "ReadBool",
    "MakeNested", "ReadNested", "TryNested", "MakeAligned", "ReadAligned",
    "MakeThree", "ReadThree", "MakeSix", "ReadSix", "MakeTwelve", "ReadMixed",
    "MakeWide", "ReadWide", "EchoWide", "RecurseWide", "TryWide", "ForwardWide",
    "TryThree", "TrySix", "TryTwelve", "MakeShiftedWide",
    "SumInt64", "EchoUInt64", "MakeWideIntegerPair", "ReadWideIntegerPair", "TryWideIntegerPair",
    "SmallOwner.Read", "WordOwner.Read", "LargeOwner.Read", "SmallOwner.Pack",
    "EchoBoolCarrier", "EchoCharCarrier", "EchoIntCarrier", "EchoUIntCarrier",
    "EchoLongCarrier", "EchoULongCarrier",
})
{
    var index = -1;
    for (var i = 0; i < program.Functions.Length; i++)
    {
        if (program.Functions[i].Name == name)
        {
            index = i;
            break;
        }
    }
    if (index < 0)
    {
        throw new InvalidOperationException($"Missing ABI fixture function: {name}");
    }

    start.ArgumentList.Add((0x1000 + ranges[sourceStart + index].Start).ToString(CultureInfo.InvariantCulture));
}

using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start ABI probe.");
if (!process.WaitForExit(10_000))
{
    process.Kill(entireProcessTree: true);
    process.WaitForExit();
    throw new TimeoutException("Native ABI probe exceeded its deadline.");
}
if (process.ExitCode != 0)
{
    throw new InvalidOperationException($"Independent C ABI probe failed with status {process.ExitCode}.");
}
