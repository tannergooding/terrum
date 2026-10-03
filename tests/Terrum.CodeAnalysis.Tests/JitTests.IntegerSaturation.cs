// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task SaturatingIntegerConversionsClampAcrossIndependentModuleAotAndJitAsync(
        NativeRuntimePolicy policy)
    {
        var checks = new[] {
            "SaturateInt32ToInt64(int32.MinValue) != int64(int32.MinValue)",
            "SaturateInt32ToInt64(42) != 42",
            "SaturateInt32ToInt64(int32.MaxValue) != int64(int32.MaxValue)",
            "SaturateInt32ToUInt64(int32.MinValue) != 0",
            "SaturateInt32ToUInt64(-1) != 0",
            "SaturateInt32ToUInt64(0) != 0",
            "SaturateInt32ToUInt64(42) != 42",
            "SaturateInt32ToUInt64(int32.MaxValue) != uint64(int32.MaxValue)",
            "SaturateUInt32ToInt64(0) != 0",
            "SaturateUInt32ToInt64(42) != 42",
            "SaturateUInt32ToInt64(uint32.MaxValue) != int64(uint32.MaxValue)",
            "SaturateUInt32ToUInt64(0) != 0",
            "SaturateUInt32ToUInt64(42) != 42",
            "SaturateUInt32ToUInt64(uint32.MaxValue) != uint64(uint32.MaxValue)",
            "SaturateUInt32ToInt32(0) != 0",
            "SaturateUInt32ToInt32(42) != 42",
            "SaturateUInt32ToInt32(uint32(int32.MaxValue)) != int32.MaxValue",
            "SaturateUInt32ToInt32(uint32(int32.MaxValue) + 1) != int32.MaxValue",
            "SaturateUInt32ToInt32(uint32.MaxValue) != int32.MaxValue",
            "SaturateInt32ToUInt32(int32.MinValue) != 0",
            "SaturateInt32ToUInt32(-1) != 0",
            "SaturateInt32ToUInt32(0) != 0",
            "SaturateInt32ToUInt32(42) != 42",
            "SaturateInt32ToUInt32(int32.MaxValue) != uint32(int32.MaxValue)",
            "SaturateInt64ToInt32(int64.MinValue) != int32.MinValue",
            "SaturateInt64ToInt32(int64(int32.MinValue) - 1) != int32.MinValue",
            "SaturateInt64ToInt32(int64(int32.MinValue)) != int32.MinValue",
            "SaturateInt64ToInt32(int64(int32.MinValue) + 1) != int32.MinValue + 1",
            "SaturateInt64ToInt32(-42) != -42",
            "SaturateInt64ToInt32(int64(int32.MaxValue) + 1) != int32.MaxValue",
            "SaturateInt64ToInt32(int64(int32.MaxValue)) != int32.MaxValue",
            "SaturateInt64ToInt32(int64.MaxValue) != int32.MaxValue",
            "SaturateInt64ToUInt32(int64.MinValue) != 0",
            "SaturateInt64ToUInt32(-1) != 0",
            "SaturateInt64ToUInt32(0) != 0",
            "SaturateInt64ToUInt32(42) != 42",
            "SaturateInt64ToUInt32(int64(uint32.MaxValue)) != uint32.MaxValue",
            "SaturateInt64ToUInt32(int64(uint32.MaxValue) + 1) != uint32.MaxValue",
            "SaturateInt64ToUInt32(int64.MaxValue) != uint32.MaxValue",
            "SaturateUInt64ToInt32(0) != 0",
            "SaturateUInt64ToInt32(42) != 42",
            "SaturateUInt64ToInt32(uint64(int32.MaxValue)) != int32.MaxValue",
            "SaturateUInt64ToInt32(uint64(int32.MaxValue) + 1) != int32.MaxValue",
            "SaturateUInt64ToInt32(uint64.MaxValue) != int32.MaxValue",
            "SaturateUInt64ToUInt32(0) != 0",
            "SaturateUInt64ToUInt32(42) != 42",
            "SaturateUInt64ToUInt32(uint64(uint32.MaxValue)) != uint32.MaxValue",
            "SaturateUInt64ToUInt32(uint64(uint32.MaxValue) + 1) != uint32.MaxValue",
            "SaturateUInt64ToUInt32(uint64.MaxValue) != uint32.MaxValue",
            "SaturateUInt64ToInt64(0) != 0",
            "SaturateUInt64ToInt64(42) != 42",
            "SaturateUInt64ToInt64(uint64(int64.MaxValue)) != int64.MaxValue",
            "SaturateUInt64ToInt64(uint64(int64.MaxValue) + 1) != int64.MaxValue",
            "SaturateUInt64ToInt64(uint64.MaxValue) != int64.MaxValue",
            "SaturateInt64ToUInt64(int64.MinValue) != 0",
            "SaturateInt64ToUInt64(-1) != 0",
            "SaturateInt64ToUInt64(0) != 0",
            "SaturateInt64ToUInt64(42) != 42",
            "SaturateInt64ToUInt64(int64.MaxValue) != uint64(int64.MaxValue)",
        };
        var narrowingChecks = new[] {
            "SaturateInt32ToChar16(int32.MinValue) != char16(0)",
            "SaturateInt32ToChar16(-1) != char16(0)",
            "SaturateInt32ToChar16(0) != char16(0)",
            "SaturateInt32ToChar16(1) != char16(1)",
            "SaturateInt32ToChar16(55295) != char16(55295)",
            "SaturateInt32ToChar16(55296) != char16(55296)",
            "SaturateInt32ToChar16(57343) != char16(57343)",
            "SaturateInt32ToChar16(57344) != char16(57344)",
            "SaturateInt32ToChar16(65534) != char16(65534)",
            "SaturateInt32ToChar16(65535) != char16(65535)",
            "SaturateInt32ToChar16(65536) != char16(65535)",
            "SaturateInt32ToChar16(int32.MaxValue) != char16(65535)",
            "SaturateUInt32ToChar16(0) != char16(0)",
            "SaturateUInt32ToChar16(1) != char16(1)",
            "SaturateUInt32ToChar16(55295) != char16(55295)",
            "SaturateUInt32ToChar16(55296) != char16(55296)",
            "SaturateUInt32ToChar16(57343) != char16(57343)",
            "SaturateUInt32ToChar16(57344) != char16(57344)",
            "SaturateUInt32ToChar16(65534) != char16(65534)",
            "SaturateUInt32ToChar16(65535) != char16(65535)",
            "SaturateUInt32ToChar16(uint32(65536)) != char16(65535)",
            "SaturateUInt32ToChar16(uint32.MaxValue) != char16(65535)",
            "SaturateInt64ToChar16(int64.MinValue) != char16(0)",
            "SaturateInt64ToChar16(-1) != char16(0)",
            "SaturateInt64ToChar16(0) != char16(0)",
            "SaturateInt64ToChar16(1) != char16(1)",
            "SaturateInt64ToChar16(55295) != char16(55295)",
            "SaturateInt64ToChar16(55296) != char16(55296)",
            "SaturateInt64ToChar16(57343) != char16(57343)",
            "SaturateInt64ToChar16(57344) != char16(57344)",
            "SaturateInt64ToChar16(65534) != char16(65534)",
            "SaturateInt64ToChar16(65535) != char16(65535)",
            "SaturateInt64ToChar16(65536) != char16(65535)",
            "SaturateInt64ToChar16(int64.MaxValue) != char16(65535)",
            "SaturateUInt64ToChar16(0) != char16(0)",
            "SaturateUInt64ToChar16(1) != char16(1)",
            "SaturateUInt64ToChar16(55295) != char16(55295)",
            "SaturateUInt64ToChar16(55296) != char16(55296)",
            "SaturateUInt64ToChar16(57343) != char16(57343)",
            "SaturateUInt64ToChar16(57344) != char16(57344)",
            "SaturateUInt64ToChar16(65534) != char16(65534)",
            "SaturateUInt64ToChar16(65535) != char16(65535)",
            "SaturateUInt64ToChar16(uint64(65536)) != char16(65535)",
            "SaturateUInt64ToChar16(uint64.MaxValue) != char16(65535)",
        };
        ushort[] codeUnits = [0, 1, 0xD7FF, 0xD800, 0xDFFF, 0xE000, ushort.MaxValue];
        var wideningChecks = codeUnits.SelectMany(value => new[] {
            $"SaturateChar16ToInt32(char16({value})) != {value}",
            $"SaturateChar16ToUInt32(char16({value})) != {value}",
            $"SaturateChar16ToInt64(char16({value})) != {value}",
            $"SaturateChar16ToUInt64(char16({value})) != {value}",
        });
        var assertions = string.Join(Environment.NewLine,
            checks.Concat(narrowingChecks).Concat(wideningChecks).Select((check, index) =>
            $"if ({check}) {{ return {index + 1}; }}"));
        var componentPath = Path.Combine(RepositoryRoot(), "self-host", "IntegerSaturation.terrum");
        var component = await File.ReadAllTextAsync(componentPath).ConfigureAwait(true);
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(component, componentPath),
            (BoundRuntimePolicy)policy);
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var exports = new[] {
            "SaturateInt32ToInt64", "SaturateInt32ToUInt64",
            "SaturateUInt32ToInt64", "SaturateUInt32ToUInt64",
            "SaturateUInt32ToInt32", "SaturateInt32ToUInt32",
            "SaturateInt64ToInt32", "SaturateInt64ToUInt32",
            "SaturateUInt64ToInt32", "SaturateUInt64ToUInt32",
            "SaturateUInt64ToInt64", "SaturateInt64ToUInt64",
            "SaturateInt32ToChar16", "SaturateUInt32ToChar16",
            "SaturateInt64ToChar16", "SaturateUInt64ToChar16",
            "SaturateChar16ToInt32", "SaturateChar16ToUInt32",
            "SaturateChar16ToInt64", "SaturateChar16ToUInt64",
        }.Select(name => $"Terrum.CodeAnalysis.Numerics64.{name}").ToArray();
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(library.Program), exports);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerSource = "using Terrum.CodeAnalysis.Numerics64;\nint32 Main() {\n"
            + assertions + "\nreturn 42;\n}";
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(consumerSource), reference,
            (BoundRuntimePolicy)policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumerProgram = Assert.IsType<BoundProgram>(consumerCompilation.Program);
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram, producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Equal(42, NativeJit.Execute(linked, policy));

        AssertWindowsX64();
        var directory = Directory.CreateTempSubdirectory("terrum-saturation-module-").FullName;
        var image = Path.Combine(directory, "consumer.exe");
        try
        {
            await File.WriteAllBytesAsync(image, NativeEmitter.Emit(linked, policy)).ConfigureAwait(true);
            var (exitCode, _, _) = await RunAsync(new ProcessStartInfo(image) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }, []).ConfigureAwait(true);
            Assert.Equal(42, exitCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
