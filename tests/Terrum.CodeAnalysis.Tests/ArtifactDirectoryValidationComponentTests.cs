// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ArtifactDirectoryValidationComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            if (!TryGetArtifactSection(
                Host.Expected[1], init var found, init var sectionStart,
                init var sectionLength, init var recordCount, init var sectionFlags)) {
                return Host.Expected[0] == 0 ? 0 : 1;
            }
            if (Host.Expected[0] == 0) { return 1; }
            if (found != Host.Expected[2]
                || sectionStart != Host.Expected[3]
                || sectionLength != Host.Expected[4]
                || recordCount != Host.Expected[5]
                || sectionFlags != Host.Expected[6]) {
                return 2;
            }
            return 0;
        }
        """;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ArtifactDirectoryValidationMatchesHostedIndex(BoundRuntimePolicy policy)
    {
        byte[] minimal = MinimalArtifact();
        byte[] extensions = ExtensionArtifact();
        byte[] outOfOrder = OutOfOrderArtifact();
        var cases = new List<TestCase> {
            NewCase(minimal, 0x0002),
            NewCase(minimal, 0x0010),
            InvalidQuery(minimal, -1),
            InvalidQuery(minimal, 0x1_0000),
            NewCase(extensions, 0x8001),
            NewCase(WithZeroGap(minimal), 0x0002),
            Invalid(ChangeByte(minimal, 0, (byte)'X')),
            Invalid(ChangeHalfWord(minimal, 4, 2)),
            Invalid(ChangeWord(minimal, 12, 0)),
            Invalid(ChangeWord(minimal, 16, (uint)(minimal.Length + 1))),
            Invalid(ChangeWord(minimal, 20, 36)),
            Invalid(ChangeByte(minimal, 24, 1)),
            Invalid(ChangeHalfWord(minimal, 34, 1)),
            Invalid(ChangeHalfWord(minimal, 48, 5)),
            Invalid(ChangeHalfWord(minimal, 48, 1)),
            Invalid(ChangeHalfWord(extensions, 66, 0)),
            Invalid(ChangeWord(minimal, 52, 0)),
            Invalid(ChangeWord(minimal, 36, 2)),
            Invalid(ChangeWord(minimal, 56, 63)),
            Invalid(ChangeWord(minimal, 56, 104)),
            Invalid(ChangeWord(minimal, 60, (uint)(ReadWord(minimal, 60) + 1))),
            Invalid(ChangeStringStart(minimal, 1)),
            Invalid(ChangeByte(WithZeroGap(minimal), 109, 1)),
            Invalid(AppendTrailingByte(minimal)),
            Invalid(NoModuleArtifact()),
            InvalidWithHostedAccepted(outOfOrder, 0x8001),
            InvalidIntByte(minimal, 64, 256),
        };

        var root = RepositoryRoot();
        var components = new List<SyntaxTree>();
        var path = Path.Combine(root, "self-host", "ArtifactDirectoryValidation.terrum");
        var source = await File.ReadAllTextAsync(path).ConfigureAwait(true);
        var tree = SyntaxTree.Parse(source, path);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        components.Add(tree);
        components.Add(SyntaxTree.Parse(VerificationSource, "artifact-directory-validation.terrum"));

        for (var caseIndex = 0; caseIndex < cases.Count; caseIndex++)
        {
            var testCase = cases[caseIndex];
            if (testCase.HostedArtifact is not null)
            {
                Assert.Equal(testCase.ExpectedHostedValid, ValidateHosted(testCase.HostedArtifact));
            }

            var entryCount = testCase.Artifact[12] | (testCase.Artifact[13] << 8)
                | (testCase.Artifact[14] << 16) | (testCase.Artifact[15] << 24);
            int[] artifact = testCase.Artifact;
            int[] expected = [
                testCase.ExpectedCandidateValid ? 1 : 0,
                testCase.QueryKind,
                testCase.ExpectedFound,
                testCase.ExpectedStart,
                testCase.ExpectedLength,
                testCase.ExpectedCount,
                testCase.ExpectedFlags,
            ];
            var scratchLength = entryCount + 1;
            var compilation = Compilation.Create(components,
            [
                new HostBuffer("Artifact", PrimitiveType.Int32, [.. artifact], false),
                new HostBuffer("SectionKinds", PrimitiveType.Int32, [.. new int[scratchLength]], true),
                new HostBuffer("SectionFlags", PrimitiveType.Int32, [.. new int[scratchLength]], true),
                new HostBuffer("SectionCounts", PrimitiveType.Int32, [.. new int[scratchLength]], true),
                new HostBuffer("SectionStarts", PrimitiveType.Int32, [.. new int[scratchLength]], true),
                new HostBuffer("SectionLengths", PrimitiveType.Int32, [.. new int[scratchLength]], true),
                new HostBuffer("Expected", PrimitiveType.Int32, [.. expected], false),
            ], policy);
            Assert.False(compilation.HasErrors,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = Assert.IsType<BoundProgram>(compilation.Program);
            int jitResult = NativeJit.Execute(program, (NativeRuntimePolicy)policy);
            Assert.True(jitResult == 0,
                $"JIT differential case {caseIndex} returned {jitResult}.");
            int aotResult = await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
                .ConfigureAwait(true);
            Assert.True(aotResult == 0,
                $"AOT differential case {caseIndex} returned {aotResult}.");
        }
    }

    private static TestCase NewCase(byte[] artifact, int queryKind)
        => CreateCase(artifact, queryKind, expectedCandidateValid: true,
            expectedHostedValid: true);

    private static TestCase Invalid(byte[] artifact)
        => CreateCase(artifact, 0x0002, expectedCandidateValid: false,
            expectedHostedValid: false);

    private static TestCase InvalidQuery(byte[] artifact, int queryKind)
        => CreateCase(artifact, queryKind, expectedCandidateValid: false,
            expectedHostedValid: true);

    private static TestCase InvalidWithHostedAccepted(byte[] artifact, int queryKind)
        => CreateCase(artifact, queryKind, expectedCandidateValid: false,
            expectedHostedValid: true);

    private static TestCase InvalidIntByte(byte[] artifact, int index, int value)
    {
        int[] values = [.. artifact.Select(static item => (int)item)];
        values[index] = value;
        return new TestCase(values, null, 0x0002, false, false, 0, 0, 0, 0, 0);
    }

    private static TestCase CreateCase(byte[] artifact, int queryKind,
        bool expectedCandidateValid, bool expectedHostedValid)
    {
        int found = 0;
        int start = 0;
        int length = 0;
        int count = 0;
        int flags = 0;
        var entryCount = ReadWord(artifact, 12);
        for (var entry = 0; entry < entryCount; entry++)
        {
            var row = 32 + entry * 16;
            if (ReadHalfWord(artifact, row) == queryKind)
            {
                found = 1;
                flags = ReadHalfWord(artifact, row + 2);
                count = checked((int)ReadWord(artifact, row + 4));
                start = checked((int)ReadWord(artifact, row + 8));
                length = checked((int)ReadWord(artifact, row + 12));
                break;
            }
        }

        return new TestCase([.. artifact.Select(static item => (int)item)], artifact,
            queryKind, expectedCandidateValid, expectedHostedValid,
            found, start, length, count, flags);
    }

    private static bool ValidateHosted(byte[] artifact)
    {
        try
        {
            _ = new TrRmArtifactIndex(artifact);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static byte[] MinimalArtifact()
        => TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, new byte[44]),
            new TrRmSection(0x0002, 0, 1,
                TrRmStringTableWriter.EncodeNames([("A", 0)])),
        ]);

    private static byte[] ExtensionArtifact()
        => TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, new byte[44]),
            new TrRmSection(0x0002, 0, 1,
                TrRmStringTableWriter.EncodeNames([("A", 0)])),
            new TrRmSection(0x8001, 3, 1, new byte[] { 0xA5 }),
        ]);

    private static byte[] OutOfOrderArtifact()
    {
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, new byte[44]),
            new TrRmSection(0x8001, 1, 1, new byte[] { 0x11 }),
            new TrRmSection(0x8002, 1, 1, new byte[] { 0x22 }),
        ]);
        var firstStart = ReadWord(artifact, 56);
        var secondStart = ReadWord(artifact, 72);
        WriteWord(artifact, 56, secondStart);
        WriteWord(artifact, 72, firstStart);
        return artifact;
    }

    private static byte[] WithZeroGap(byte[] artifact)
    {
        var stringStart = checked((int)ReadWord(artifact, 48 + 8));
        var result = new byte[artifact.Length + 4];
        artifact.AsSpan(0, stringStart).CopyTo(result);
        artifact.AsSpan(stringStart).CopyTo(result.AsSpan(stringStart + 4));
        WriteWord(result, 16, (uint)result.Length);
        WriteWord(result, 48 + 8, (uint)(stringStart + 4));
        return result;
    }

    private static byte[] AppendTrailingByte(byte[] artifact)
    {
        var result = new byte[artifact.Length + 1];
        artifact.CopyTo(result, 0);
        WriteWord(result, 16, (uint)result.Length);
        return result;
    }

    private static byte[] NoModuleArtifact()
    {
        var strings = TrRmStringTableWriter.EncodeNames([("A", 0)]);
        var result = new byte[48 + strings.Length];
        "TRRM"u8.CopyTo(result);
        WriteHalfWord(result, 4, 1);
        WriteWord(result, 8, 32);
        WriteWord(result, 12, 1);
        WriteWord(result, 16, (uint)result.Length);
        WriteWord(result, 20, 32);
        WriteHalfWord(result, 32, 0x0002);
        WriteWord(result, 36, 1);
        WriteWord(result, 40, 48);
        WriteWord(result, 44, (uint)strings.Length);
        strings.CopyTo(result.AsSpan(48));
        return result;
    }

    private static byte[] ChangeStringStart(byte[] artifact, int delta)
    {
        var result = (byte[])artifact.Clone();
        WriteWord(result, 48 + 8, checked(ReadWord(result, 48 + 8) + (uint)delta));
        WriteWord(result, 48 + 12, checked(ReadWord(result, 48 + 12) - (uint)delta));
        return result;
    }

    private static byte[] ChangeByte(byte[] artifact, int index, byte value)
    {
        var result = (byte[])artifact.Clone();
        result[index] = value;
        return result;
    }

    private static byte[] ChangeHalfWord(byte[] artifact, int index, ushort value)
    {
        var result = (byte[])artifact.Clone();
        WriteHalfWord(result, index, value);
        return result;
    }

    private static byte[] ChangeWord(byte[] artifact, int index, uint value)
    {
        var result = (byte[])artifact.Clone();
        WriteWord(result, index, value);
        return result;
    }

    private static ushort ReadHalfWord(byte[] bytes, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));

    private static uint ReadWord(byte[] bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private static void WriteHalfWord(byte[] bytes, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);

    private static void WriteWord(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(Path.GetTempPath(), $"artifact-directory-validation-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled artifact directory validator.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
            return process.ExitCode;
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private sealed record TestCase(
        int[] Artifact,
        byte[]? HostedArtifact,
        int QueryKind,
        bool ExpectedCandidateValid,
        bool ExpectedHostedValid,
        int ExpectedFound,
        int ExpectedStart,
        int ExpectedLength,
        int ExpectedCount,
        int ExpectedFlags);
}
