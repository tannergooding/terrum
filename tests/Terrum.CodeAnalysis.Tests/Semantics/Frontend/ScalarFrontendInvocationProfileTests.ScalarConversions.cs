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
    public async Task ScalarFrontendSourceProducerAcceptsEveryAllowedConversionPairAsync(
        BoundRuntimePolicy policy)
    {
        var compilerSource = SourceText.From("int32 Main() { return 42; }");
        var trees = await ReadComponentsAsync(s_frontendComponents, """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (!TryPublishScalarFrontend()) { return -1; }
                return 42;
            }
            """).ConfigureAwait(true);
        var compilation = CreateWithLayoutCore(trees,
            ScalarFrontendHost.CreateBuffers(compilerSource), policy);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var compiler = compilation.Program
            ?? throw new AssertionException(
                "Expected a bound standalone frontend.");
        var integerTypes = new[] { 1, 2, 3, 4, 6 };
        var floatingTypes = new[] { 7, 8 };
        var acceptedPairs = integerTypes.SelectMany(sourceType =>
                integerTypes.Concat(floatingTypes).Select(targetType =>
                    (SourceType: sourceType, TargetType: targetType)))
            .Concat(floatingTypes.SelectMany(sourceType =>
                integerTypes.Take(4).Concat(floatingTypes).Select(targetType =>
                    (SourceType: sourceType, TargetType: targetType))))
            .ToArray();

        for (var batchStart = 0; batchStart < acceptedPairs.Length;
            batchStart += 31)
        {
            var batch = acceptedPairs.Skip(batchStart).Take(31).ToArray();
            var functions = batch.Select((pair, ordinal) =>
                $"{TypeName(pair.TargetType)} Convert{batchStart + ordinal}"
                + $"({TypeName(pair.SourceType)} value) {{ "
                + $"return {TypeName(pair.TargetType)}(value); }}");
            var source = SourceText.From(
                "int32 Main() { return 42; } "
                + string.Join(' ', functions));
            var invocation = NativeJit.ExecuteWithScalarFrontendProfile(
                compiler, source, (NativeRuntimePolicy)policy);
            Assert.That(invocation.Frontend.Projection.IsValid, Is.True,
                source.ToString());
            var subject = invocation.Frontend.Program
                ?? throw new AssertionException(
                    "The conversion matrix produced no bound subject.");
            for (var ordinal = 0; ordinal < batch.Length; ordinal++)
            {
                var pair = batch[ordinal];
                var function = subject.Functions.Single(candidate =>
                    candidate.Name == $"Convert{batchStart + ordinal}");
                Assert.That(function.ReturnType,
                    Is.EqualTo(PrimitiveTypeFor(pair.TargetType)));
                var returned = function.Body.Statements.Single()
                    as BoundReturn
                    ?? throw new AssertionException(
                        $"Convert{batchStart + ordinal} has no return.");
                var cast = returned.Value as BoundCast
                    ?? throw new AssertionException(
                        $"Convert{batchStart + ordinal} has no cast.");
                Assert.That(cast.Type,
                    Is.EqualTo(PrimitiveTypeFor(pair.TargetType)));
                Assert.That(cast.Operand.Type,
                    Is.EqualTo(PrimitiveTypeFor(pair.SourceType)));
            }
        }

        static string TypeName(int type) => type switch {
            1 => "int32",
            2 => "uint32",
            3 => "int64",
            4 => "uint64",
            6 => "char16",
            7 => "float32",
            8 => "float64",
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

        static PrimitiveType PrimitiveTypeFor(int type) => type switch {
            1 => PrimitiveType.Int32,
            2 => PrimitiveType.UInt32,
            3 => PrimitiveType.Int64,
            4 => PrimitiveType.UInt64,
            6 => PrimitiveType.Char16,
            7 => PrimitiveType.Float32,
            8 => PrimitiveType.Float64,
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ScalarFrontendProgramReaderMatchesConversionPairMatrixAsync(
        BoundRuntimePolicy policy)
    {
        var trees = await ReadComponentsAsync(
            ["Semantics/ScalarFrontendProgramReader"], """
                namespace Terrum.CodeAnalysis;
                int32 Main() {
                    for (mut int32 sourceType = 1; sourceType <= 8;
                        sourceType = sourceType + 1) {
                        for (mut int32 targetType = 1; targetType <= 8;
                            targetType = targetType + 1) {
                            // Bit n represents target type code n.
                            int32 targetMask = 0;
                            if (sourceType == 1 || sourceType == 2
                                || sourceType == 3 || sourceType == 4
                                || sourceType == 6) {
                                targetMask = 0x1DE;
                            } else if (sourceType == 7 || sourceType == 8) {
                                targetMask = 0x19E;
                            }
                            bool expected
                                = (targetMask & (1 << targetType)) != 0;
                            if (ScalarProgramReaderSupportedConversion(
                                    sourceType, targetType) != expected) {
                                return 2;
                            }
                        }
                    }
                    return 42;
                }
                """).ConfigureAwait(true);
        var source = SourceText.From("int32 Main() => 42;");
        var compilation = CreateWithLayoutCore(trees,
            ScalarFrontendHost.CreateBuffers(source), policy);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.ExecuteWithData(compilation.Program!,
            (NativeRuntimePolicy)policy).ExitCode, Is.EqualTo(42));
    }

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
    public async Task ScalarFrontendReaderRejectsInvalidConversionsAndRetiredEffectsAsync(
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

        var retiredNoThrowWords = words.ToArray();
        var mainBody = 4 + 4;
        retiredNoThrowWords[mainBody + 2] |= 2;
        var retiredNoThrowBuffers = buffers.SetItem(
            ScalarFrontendHost.ProgramBufferIndex,
            buffers[ScalarFrontendHost.ProgramBufferIndex] with {
                Values = [.. retiredNoThrowWords],
            });
        var retiredNoThrow = NativeJit.ExecuteWithData(reader with {
            Buffers = retiredNoThrowBuffers,
        }, (NativeRuntimePolicy)policy);
        Assert.That(retiredNoThrow.ExitCode, Is.EqualTo(2));
        Assert.That(retiredNoThrow.Data.AsSpan(
            retiredNoThrow.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int))
            .IndexOfAnyExcept((byte)0), Is.EqualTo(-1));
    }
}
