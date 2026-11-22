// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Tests.Semantics;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Tests.Semantics.Frontend;

internal sealed partial class ScalarFrontendInvocationProfileTests
{
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ScalarFrontendRejectsUnsupportedFloatToChar16ConversionAsync(
        BoundRuntimePolicy policy)
    {
        var trees = await ReadComponentsAsync(s_frontendComponents, """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (!TryPublishScalarFrontend()) { return -1; }
                return 42;
            }
            """).ConfigureAwait(true);
        var source = SourceText.From(
            "int32 Main() { return 42; } "
            + "char16 Convert(float64 value) { return char16(value); }");
        var compilation = CreateWithLayoutCore(trees,
            ScalarFrontendHost.CreateBuffers(source), policy);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var result = NativeJit.ExecuteWithData(program,
            (NativeRuntimePolicy)policy);
        Assert.That(result.ExitCode, Is.EqualTo(42));
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            result.Data.AsSpan(result.BufferOffsets[
                ScalarFrontendHost.BufferIndex] + sizeof(int), sizeof(int))),
            Is.EqualTo(2));
        Assert.That(result.Data.AsSpan(result.BufferOffsets[
                ScalarFrontendHost.ProgramBufferIndex],
                ScalarFrontendHost.ProgramWordCount * sizeof(int))
            .IndexOfAnyExcept((byte)0), Is.EqualTo(-1));
    }

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ScalarFrontendReaderRejectsUnsupportedConversionPairsAsync(
        BoundRuntimePolicy policy)
    {
        var writerTrees = await ReadComponentsAsync(s_frontendComponents, """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (!TryPublishScalarFrontend()) { return -1; }
                return 42;
            }
            """).ConfigureAwait(true);
        var readerTrees = await ReadComponentsAsync(
            ["Semantics/ScalarFrontendProgramReader"], """
                namespace Terrum.CodeAnalysis;
                int32 Main() {
                    if (!TryDecodeScalarFrontendProgram(
                        Host.FrontendProgram.Length,
                        Host.Tokens.Length / 3 - 1)) { return 2; }
                    return 42;
                }
                """).ConfigureAwait(true);
        var source = SourceText.From(
            "int32 Main() { return 42; } "
            + "int32 Convert(float64 value) { return int32(value); }");
        var buffers = ScalarFrontendHost.CreateBuffers(source);
        var writerCompilation = CreateWithLayoutCore(writerTrees, buffers,
            policy);
        Assert.That(writerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                writerCompilation.Diagnostics));
        var readerCompilation = CreateWithLayoutCore(readerTrees, buffers,
            policy);
        Assert.That(readerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                readerCompilation.Diagnostics));
        var publication = NativeJit.ExecuteWithData(
            writerCompilation.Program!, (NativeRuntimePolicy)policy);
        Assert.That(publication.ExitCode, Is.EqualTo(42));
        var programBytes = publication.Data.AsSpan(
            publication.BufferOffsets[
                ScalarFrontendHost.ProgramBufferIndex],
            ScalarFrontendHost.ProgramWordCount * sizeof(int)).ToArray();
        var words = new int[ScalarFrontendHost.ProgramWordCount];
        for (var word = 0; word < words.Length; word++)
        {
            words[word] = BinaryPrimitives.ReadInt32LittleEndian(
                programBytes.AsSpan(word * sizeof(int), sizeof(int)));
        }
        buffers = WithPublishedTokens(buffers, publication);
        var reader = readerCompilation.Program!;
        var publishedBuffers = buffers.SetItem(
            ScalarFrontendHost.ProgramBufferIndex,
            buffers[ScalarFrontendHost.ProgramBufferIndex] with {
                Values = [.. words],
            });
        var validExecution = NativeJit.ExecuteWithData(reader with {
            Buffers = publishedBuffers,
        }, (NativeRuntimePolicy)policy);
        Assert.That(validExecution.ExitCode, Is.EqualTo(42));

        var conversionBody = -1;
        var conversionNodeOffset = -1;
        for (var function = 0; function < words[2]; function++)
        {
            var body = 4 + function
                * ScalarFrontendHost.ProgramFrameWordCount + 4;
            var nodeCount = words[body + 37];
            for (var node = 0; node < nodeCount; node++)
            {
                var nodeOffset = body + (node < 32
                    ? 42 + node * 8 : 392 + (node - 32) * 8);
                if (words[nodeOffset] == 83)
                {
                    conversionBody = body;
                    conversionNodeOffset = nodeOffset;
                    break;
                }
            }

            if (conversionNodeOffset >= 0)
            {
                break;
            }
        }

        Assert.That(conversionNodeOffset, Is.GreaterThanOrEqualTo(0));
        var malformedWords = words.ToArray();
        malformedWords[conversionBody + 5] = 6;
        malformedWords[conversionNodeOffset + 1] = 6;
        var malformedBytes = new byte[malformedWords.Length * sizeof(int)];
        for (var word = 0; word < malformedWords.Length; word++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                malformedBytes.AsSpan(word * sizeof(int), sizeof(int)),
                malformedWords[word]);
        }
        _ = Assert.Throws<ArgumentException>(() =>
            ScalarFrontendHost.ReadProgram(source,
                new byte[ScalarFrontendHost.WordCount * sizeof(int)],
                malformedBytes, policy));

        var malformedBuffers = buffers.SetItem(
            ScalarFrontendHost.ProgramBufferIndex,
            buffers[ScalarFrontendHost.ProgramBufferIndex] with {
                Values = [.. malformedWords],
            });
        var rejected = NativeJit.ExecuteWithData(reader with {
            Buffers = malformedBuffers,
        }, (NativeRuntimePolicy)policy);
        Assert.That(rejected.ExitCode, Is.EqualTo(2));
        Assert.That(rejected.Data.AsSpan(
            rejected.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int))
            .IndexOfAnyExcept((byte)0), Is.EqualTo(-1));
    }
}
