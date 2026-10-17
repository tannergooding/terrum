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
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class FeatureTableValidationComponentTests
{
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            bool valid = TryValidateFeatureTable(Host.Expected[0]);
            return valid == (Host.Expected[1] != 0) ? 0 : 1;
        }
        """;
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task FeatureTableValidationMatchesHostedReader(BoundRuntimePolicy policy)
    {
        var cases = new List<TableCase> {
            Valid([]),
            Valid([1, 1]),
            Valid([1, 6]),
            Valid([1, 7]),
            Valid([1, 8]),
            Valid([0x0100, 1]),
            Valid([0x0102, 1]),
            Valid([0x0103, 1]),
            Valid([0x0100, 1, 0x0101, 1, 0x0104, 1]),
            Valid([0x0100, 1, 0x0101, 1, 0x0102, 1, 0x0103, 1, 0x0104, 1]),
            Rejected([0, 1]),
            Rejected([2, 1]),
            Rejected([1, 0]),
            Rejected([1, 9]),
            Rejected([0x0100, 2]),
            Rejected([0x0105, 1]),
            Rejected([1, 1, 1, 2]),
            Rejected([1, 1, 2]),
            new([], -1, false, true),
            new([], 0x1000000, false, true),
            new([], 1, false, true),
            new([1, 0, 0, 0, 1, 0, 0], 1, false, true),
            new([1, 0, 0, 0, 1, 0, 0, 0, 0], 1, false, true),
            new([256, 0, 0, 0, 1, 0, 0, 0], 1, false, false),
            new([-1, 0, 0, 0, 1, 0, 0, 0], 1, false, false),
        };

        var root = RepositoryRoot();
        var path = SelfHostPaths.ResolveFile(root, "FeatureTableValidation.terrum");
        var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        var components = new[] {
            tree,
            SyntaxTree.Parse(VerificationSource, "feature-table-validation.terrum"),
        };

        for (var caseIndex = 0; caseIndex < cases.Count; caseIndex++)
        {
            var testCase = cases[caseIndex];
            if (testCase.HasHostedOracle)
            {
                Assert.That(ValidateHosted(testCase), Is.EqualTo(testCase.ExpectedValid));
            }

            var compilation = Compilation.Create(components,
            [
                new HostBuffer("Features", PrimitiveType.Int32, [.. testCase.Features], false),
                new HostBuffer("Expected", PrimitiveType.Int32,
                    [testCase.RecordCount, testCase.ExpectedValid ? 1 : 0], false),
            ], policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
            Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
            Assert.That(await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
                .ConfigureAwait(true), Is.EqualTo(0));
        }
    }

    private static TableCase Valid(uint[] rows)
        => new([.. Encode(rows).Select(static value => (int)value)], rows.Length / 2, true, true);

    private static TableCase Rejected(uint[] rows)
        => Valid(rows) with { ExpectedValid = false };

    private static byte[] Encode(uint[] words)
    {
        var bytes = new byte[words.Length * sizeof(uint)];
        for (var index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * sizeof(uint)), words[index]);
        }

        return bytes;
    }

    private static bool ValidateHosted(TableCase testCase)
    {
        try
        {
            _ = new TrRmFeatureTableReader(ToBytes(testCase.Features), testCase.RecordCount);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static byte[] ToBytes(int[] values)
        => values.Select(static value => checked((byte)value)).ToArray();

    private static async Task<int> ExecuteAotAsync(BoundProgram program, NativeRuntimePolicy policy)
    {
        var directory = TestArtifactPaths.CreateDirectory("feature-table-tests");
        var path = Path.Combine(directory, $"feature-table-validation-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy))
                .ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start compiled Feature-table validator.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
            return process.ExitCode;
        }
        finally
        {
            if (File.Exists(path))
            {
                await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
            }
        }
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private sealed record TableCase(int[] Features, int RecordCount, bool ExpectedValid, bool HasHostedOracle);
}
