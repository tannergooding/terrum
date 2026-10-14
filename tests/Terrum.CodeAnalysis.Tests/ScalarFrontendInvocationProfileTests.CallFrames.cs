using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class ScalarFrontendInvocationProfileTests
{
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SummaryReaderIndependentlyValidatesSelectedCallFramesAsync(BoundRuntimePolicy policy)
    {
        var writerTrees = await ReadComponentsAsync(s_frontendComponents, """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (!TryPublishScalarFrontend()) { return -1; }
                return 42;
            }
            """).ConfigureAwait(true);
        var readerTrees = await ReadComponentsAsync(["ScalarFrontendProgramReader"], """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (!TryDecodeScalarFrontendProgram(
                    Host.FrontendProgram.Length, Host.Tokens.Length / 3 - 1)) { return 2; }
                return 42;
            }
            """).ConfigureAwait(true);

        var nestedNamespaceNames = Enumerable.Range(0, 8)
            .Select(static index => $"Scope{index}").ToArray();
        var nestedNamespaceSource = string.Join(' ',
                nestedNamespaceNames.Select(static name => $"namespace {name} {{"))
            + " alias Scalar = int32; Scalar Main() => Next(); "
            + "Scalar Next() => 42; Scalar Other() => 43;"
            + string.Concat(Enumerable.Repeat(" }", nestedNamespaceNames.Length));
        foreach (var text in new[] {
            "int32 Main() => Next(); int32 Next() => 42; int32 Other() => 43;",
            "namespace Demo.Tools; int32 Main() => Next(); "
                + "int32 Next() => 42; int32 Other() => 43;",
            "int32 Main() => Next(41); int32 Next(int32 value) => value; int32 Other() => 0;",
            "bool Main() => Pick('x', 2.0, true); "
                + "bool Pick(char16 a, float64 b, bool c) => c; bool Other() => false;",
            "int32 Main() => Pick(1, true, 'x', 2.0); "
                + "int32 Pick(int32 a, bool b, char16 c, float64 d) => a; "
                + "int32 Other(int32 a, bool b, char16 c, int32 d) => a;",
            "int32 Main() => Pick(d: 2.0, c: 'x', a: 1, b: true); "
                + "int32 Pick(int32 a, bool b, char16 c, float64 d) => a; "
                + "int32 Other(int32 a, bool b, char16 c, int32 d) => a;",
            "int32 Main() => Pick(1, b: true, d: 2.0, c: 'x'); "
                + "int32 Pick(int32 a, bool b, char16 c, float64 d) => a; "
                + "int32 Other(int32 a, bool b, char16 c, int32 d) => a;",
            "int32 Main() { mut int32 a = 0; Fill(second: init var b, first: out a); return a; } "
                + "void Fill(out int32 first, init ref<bool> second) { first = 42; second = true; } "
                + "void Other() { }",
            "namespace Demo.Tools; "
                + "int32 Main() { mut int32 a = 0; Fill(second: init var b, first: out a); return a; } "
                + "void Fill(out int32 first, init ref<bool> second) { first = 42; second = true; } "
                + "void Other() { }",
            "namespace Demo.Tools { "
                + "int32 Main() { mut int32 a = 0; Fill(second: init var b, first: out a); return a; } "
                + "void Fill(out int32 first, init ref<bool> second) { first = 42; second = true; } "
                + "void Other() { } }",
            nestedNamespaceSource,
            "int32 Main() => Next(Id(41)); int32 Next(int32 value) => value; "
                + "int32 Id(int32 value) => value;",
        })
        {
            var source = SourceText.From(text);
            var buffers = ScalarFrontendHost.CreateBuffers(source);
            var writerCompilation = Compilation.Create(writerTrees, buffers, policy);
            Assert.That(writerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, writerCompilation.Diagnostics));
            var writer = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = writerCompilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
            var readerCompilation = Compilation.Create(readerTrees, buffers, policy);
            Assert.That(readerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, readerCompilation.Diagnostics));
            var reader = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = readerCompilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
            var publication = NativeJit.ExecuteWithData(writer, (NativeRuntimePolicy)policy);
            Assert.That(publication.ExitCode, Is.EqualTo(42));
            buffers = WithPublishedTokens(buffers, publication);
            var programBytes = publication.Data.AsSpan(
                publication.BufferOffsets[ScalarFrontendHost.ProgramBufferIndex],
                ScalarFrontendHost.ProgramWordCount * sizeof(int)).ToArray();
            var words = new int[ScalarFrontendHost.ProgramWordCount];
            for (var word = 0; word < words.Length; word++)
            {
                words[word] = BinaryPrimitives.ReadInt32LittleEndian(
                    programBytes.AsSpan(word * sizeof(int), sizeof(int)));
            }
            var originalPublication = new byte[ScalarFrontendHost.WordCount * sizeof(int)];
            var audited = ScalarFrontendHost.ReadProgram(source, originalPublication, programBytes, policy);
            AssertReaderResult(words, expectedSuccess: true);

            var caller = audited.Projection.Functions[0];
            var node = caller.Nodes.Last(static node => node.Operation == 105);
            var call = caller.Nodes.IndexOf(node);
            Assert.That(call >= 0, Is.True);
            var target = caller.CallTargets[call];
            Assert.That(target > 0, Is.True);
            var callee = 8 + target * ScalarFrontendHost.ProgramFrameWordCount;
            var callOffset = NodeOffset(call);
            var targetOffset = 8 + ScalarFrontendHost.ProgramBodyWordCount + call;
            var hasBooleanLiteral = false;
            for (var functionIndex = 0; functionIndex < audited.Projection.Functions.Length; functionIndex++)
            {
                var function = audited.Projection.Functions[functionIndex];
                for (var nodeIndex = 0; nodeIndex < function.Nodes.Length; nodeIndex++)
                {
                    var operation = function.Nodes[nodeIndex].Operation;
                    if (operation is 20 or 21)
                    {
                        hasBooleanLiteral = true;
                        var frameOffset = 8
                            + functionIndex * ScalarFrontendHost.ProgramFrameWordCount;
                        var nodeOffset = frameOffset + 4 + (nodeIndex < 32
                            ? 42 + nodeIndex * 8 : 392 + (nodeIndex - 32) * 8);
                        AssertRejected((nodeOffset, 3));
                    }
                }
            }
            Assert.That(hasBooleanLiteral, Is.EqualTo(text.Contains("true", StringComparison.Ordinal)
                    || text.Contains("false", StringComparison.Ordinal)));

            if (node.High == 0)
            {
                var compatibleTarget = words.ToArray();
                compatibleTarget[targetOffset] = 2;
                AssertReaderResult(compatibleTarget, expectedSuccess: true);
                AssertHostedRejection(compatibleTarget);
            }
            else
            {
                AssertRejected((targetOffset, 0));
                if (node.High == 4)
                {
                    AssertRejected((targetOffset, 2));
                }
            }
            AssertRejected((callee + 6, node.High == 0 ? 1 : 0),
                (callee + 18, 0), (callee + 19, 0), (callee + 20, 0));
            AssertRejected((callee + 5, node.Type == 1 ? 5 : 1));
            AssertRejected((callOffset + 1, node.Type == 1 ? 5 : 1));

            int[] roots = [node.Left, node.Right, node.Low, node.FourthArgumentRoot];
            for (var ordinal = 0; ordinal < node.High; ordinal++)
            {
                var parameter = node.ArgumentParameterIndices.IsDefault
                    ? ordinal : node.ArgumentParameterIndices[ordinal];
                var field = parameter switch { 0 => 7, 1 => 8, 2 => 34, _ => 18 };
                var argumentOffset = NodeOffset(roots[ordinal]);
                var differentType = words[callee + field] == 1 ? 5 : 1;
                AssertRejected((callee + field, differentType));
                AssertRejected((argumentOffset + 1, differentType));
                foreach (var invalidType in new[] { 0, 9 })
                {
                    AssertRejected((callee + field, invalidType),
                        (argumentOffset + 1, invalidType));
                }
                AssertRejected((callee + 16, words[callee + 16] ^ (1 << parameter)));
                AssertRejected((callee + 17, words[callee + 17] ^ (1 << parameter)));
            }
            if (!node.ArgumentParameterIndices.IsDefault)
            {
                var mapping = node.ArgumentParameterIndices;
                var encoded = words[callOffset + 7] & ~(255 << 3);
                for (var ordinal = 0; ordinal < mapping.Length; ordinal++)
                {
                    var parameter = ordinal switch { 0 => mapping[1], 1 => mapping[0], _ => mapping[ordinal] };
                    encoded |= parameter << (3 + ordinal * 2);
                }
                AssertRejected((callOffset + 7, encoded));
            }
            if (text.Contains("void Fill(", StringComparison.Ordinal))
            {
                var calleeBody = 8 + ScalarFrontendHost.ProgramFrameWordCount;
                foreach (var statement in Enumerable.Range(0, words[calleeBody + 298])
                    .Where(statement => words[calleeBody + 332 + statement * 5] == 2))
                {
                    var offset = calleeBody + 332 + statement * 5;
                    var root = words[offset + 2];
                    var typeOffset = NodeOffset(root)
                        + ScalarFrontendHost.ProgramFrameWordCount + 1;
                    var mismatchedParameterAssignment = words.ToArray();
                    mismatchedParameterAssignment[typeOffset] =
                        words[typeOffset] == 1 ? 5 : 1;
                    AssertReaderResult(mismatchedParameterAssignment, expectedSuccess: false);
                    var missingParameterSlot = words.ToArray();
                    missingParameterSlot[offset + 1] = 0;
                    AssertReaderResult(missingParameterSlot, expectedSuccess: false);
                }
            }

            void AssertReaderResult(int[] candidate, bool expectedSuccess)
            {
                var execution = NativeJit.ExecuteWithData(reader with {
                    Buffers = buffers.SetItem(ScalarFrontendHost.ProgramBufferIndex,
                        buffers[ScalarFrontendHost.ProgramBufferIndex] with {
                            Values = ImmutableArray.CreateRange(candidate),
                        }),
                }, (NativeRuntimePolicy)policy);
                Assert.That(execution.ExitCode, Is.EqualTo(expectedSuccess ? 42 : 2));
                var summary = execution.Data.AsSpan(execution.BufferOffsets[ScalarFrontendHost.BufferIndex],
                    ScalarFrontendHost.WordCount * sizeof(int));
                if (expectedSuccess)
                {
                    Assert.That(BinaryPrimitives.ReadInt32LittleEndian(summary), Is.EqualTo(88));
                    Assert.That(BinaryPrimitives.ReadInt32LittleEndian(summary[sizeof(int)..]), Is.EqualTo(1));
                }
                else
                {
                    {var __nunitItems0 = summary.ToArray();                    Assert.Multiple(() => { foreach (var __nunitItem2 in __nunitItems0) { Assert.That(__nunitItem2, Is.EqualTo(0)); } });}
                }
                var retained = execution.Data.AsSpan(
                    execution.BufferOffsets[ScalarFrontendHost.ProgramBufferIndex],
                    ScalarFrontendHost.ProgramWordCount * sizeof(int));
                for (var word = 0; word < candidate.Length; word++)
                {
                    Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
                        retained.Slice(word * sizeof(int), sizeof(int))), Is.EqualTo(candidate[word]));
                }
            }

            void AssertHostedRejection(int[] candidate)
            {
                var forged = new byte[programBytes.Length];
                for (var word = 0; word < candidate.Length; word++)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(
                        forged.AsSpan(word * sizeof(int), sizeof(int)), candidate[word]);
                }
                Assert.Throws<ArgumentException>(() =>
                    ScalarFrontendHost.ReadProgram(source, originalPublication, forged, policy));
            }

            void AssertRejected(params (int Word, int Value)[] changes)
            {
                var candidate = words.ToArray();
                foreach (var (word, value) in changes)
                {
                    candidate[word] = value;
                }
                AssertReaderResult(candidate, expectedSuccess: false);
                AssertHostedRejection(candidate);
            }

            static int NodeOffset(int node) => 8 + (node < 32
                ? 42 + node * 8 : 392 + (node - 32) * 8);
        }
    }
    [TestCase(BoundRuntimePolicy.FailFast,"namespace Demo.Tools; int32 Main() => 42;")]
    [TestCase(BoundRuntimePolicy.CheckedExceptions,"namespace Demo.Tools; int32 Main() => 42;")]
    [TestCase(BoundRuntimePolicy.FailFast,"namespace Demo.Tools { int32 Main() => 42; }")]
    [TestCase(BoundRuntimePolicy.CheckedExceptions,"namespace Demo.Tools { int32 Main() => 42; }")]
    [TestCase(BoundRuntimePolicy.FailFast,"namespace Demo.Tools.Inner; int32 Main() => 42;")]
    [TestCase(BoundRuntimePolicy.CheckedExceptions,"namespace Demo.Tools.Inner; int32 Main() => 42;")]
    [TestCase(BoundRuntimePolicy.FailFast,        "namespace /* header */ Demo.\U00010400 /* end */; int32 Main() => 42;")]
    [TestCase(BoundRuntimePolicy.CheckedExceptions,        "namespace /* header */ Demo.\U00010400 /* end */; int32 Main() => 42;")]
    [TestCase(BoundRuntimePolicy.FailFast,        "namespace Demo.Tools { namespace Inner { int32 Main() => 42; } }")]
    [TestCase(BoundRuntimePolicy.CheckedExceptions,        "namespace Demo.Tools { namespace Inner { int32 Main() => 42; } }")]
    public async Task SummaryReaderValidatesNamespaceTokenSpansAsync(
        BoundRuntimePolicy policy, string sourceText)
    {
        var writerTrees = await ReadComponentsAsync(s_frontendComponents, """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (!TryPublishScalarFrontend()) { return -1; }
                return 42;
            }
            """).ConfigureAwait(true);
        var readerTrees = await ReadComponentsAsync(["ScalarFrontendProgramReader"], """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (!TryDecodeScalarFrontendProgram(
                    Host.FrontendProgram.Length, 63)) { return 2; }
                return 42;
            }
            """).ConfigureAwait(true);
        var source = SourceText.From(sourceText);
        var buffers = ScalarFrontendHost.CreateBuffers(source);
        var writerCompilation = Compilation.Create(writerTrees, buffers, policy);
        Assert.That(writerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, writerCompilation.Diagnostics));
        var writer = ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = writerCompilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))();
        var readerCompilation = Compilation.Create(readerTrees, buffers, policy);
        Assert.That(readerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, readerCompilation.Diagnostics));
        var reader = ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = readerCompilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))();
        var publication = NativeJit.ExecuteWithData(writer, (NativeRuntimePolicy)policy);
        Assert.That(publication.ExitCode, Is.EqualTo(42));
        buffers = WithPublishedTokens(buffers, publication);
        var programBytes = publication.Data.AsSpan(
            publication.BufferOffsets[ScalarFrontendHost.ProgramBufferIndex],
            ScalarFrontendHost.ProgramWordCount * sizeof(int)).ToArray();
        var words = new int[ScalarFrontendHost.ProgramWordCount];
        for (var word = 0; word < words.Length; word++)
        {
            words[word] = BinaryPrimitives.ReadInt32LittleEndian(
                programBytes.AsSpan(word * sizeof(int), sizeof(int)));
        }
        var originalPublication = new byte[ScalarFrontendHost.WordCount * sizeof(int)];
        AssertReaderResult(words, expectedSuccess: true);
        var namespaceOffset = 8 + 21;
        var nameStart = words[namespaceOffset];
        var nameLength = words[namespaceOffset + 1];
        Assert.That(nameLength > 1, Is.True);
        AssertRejected((namespaceOffset, nameStart + 1),
            (namespaceOffset + 1, nameLength - 1));
        AssertRejected((namespaceOffset + 1, nameLength - 1));
        AssertRejected((namespaceOffset, 0), (namespaceOffset + 1, 0));
        var tokenWords = buffers[3].Values;
        AssertRejected((5, tokenWords.Length / 3 + 1));
        var terminatorToken = Enumerable.Range(0, tokenWords.Length / 3).First(token =>
            (tokenWords[token * 3] is 48 or 52)
            && tokenWords[token * 3 + 1] >= nameStart + nameLength);
        var forgedTokens = tokenWords.ToArray();
        forgedTokens[terminatorToken * 3 + 1] = nameStart + nameLength - 1;
        AssertReaderResult(words, expectedSuccess: false, forgedTokens);
        var terminatorBeforeSource = tokenWords.ToArray();
        terminatorBeforeSource[terminatorToken * 3 + 1] = -1;
        AssertReaderResult(words, expectedSuccess: false, terminatorBeforeSource);
        var terminatorAfterSource = tokenWords.ToArray();
        terminatorAfterSource[terminatorToken * 3 + 1] = source.Length;
        AssertReaderResult(words, expectedSuccess: false, terminatorAfterSource);
        var namespaceCloseTokens = Enumerable.Range(0, tokenWords.Length / 3).Where(token =>
            tokenWords[token * 3] == 49
            && tokenWords[token * 3 + 1] >= nameStart + nameLength).ToArray();
        if (namespaceCloseTokens.Length != 0)
        {
            var namespaceCloseAfterSource = tokenWords.ToArray();
            namespaceCloseAfterSource[namespaceCloseTokens[^1] * 3 + 1] = source.Length;
            AssertReaderResult(words, expectedSuccess: false, namespaceCloseAfterSource);
        }
        var namespaceToken = Enumerable.Range(0, tokenWords.Length / 3).First(token =>
            tokenWords[token * 3] == 6);
        var namespaceKeywordOverlap = tokenWords.ToArray();
        namespaceKeywordOverlap[namespaceToken * 3 + 1] = nameStart;
        AssertReaderResult(words, expectedSuccess: false, namespaceKeywordOverlap);
        var namespaceKeywordAfterSource = tokenWords.ToArray();
        namespaceKeywordAfterSource[namespaceToken * 3 + 1] = source.Length;
        AssertReaderResult(words, expectedSuccess: false, namespaceKeywordAfterSource);
        var namespaceNameTokens = Enumerable.Range(namespaceToken + 1,
            terminatorToken - namespaceToken - 1)
            .Where(token => tokenWords[token * 3] == 2)
            .ToArray();
        var dotToken = Enumerable.Range(namespaceToken + 1,
            terminatorToken - namespaceToken - 1).First(token =>
                tokenWords[token * 3] == 54);
        var namespaceDotLength = tokenWords.ToArray();
        namespaceDotLength[dotToken * 3 + 2] = 2;
        AssertReaderResult(words, expectedSuccess: false, namespaceDotLength);
        if (namespaceNameTokens.Length > 2)
        {
            var middleNameOutOfSource = tokenWords.ToArray();
            middleNameOutOfSource[namespaceNameTokens[1] * 3 + 1] = source.Length;
            AssertReaderResult(words, expectedSuccess: false, middleNameOutOfSource);
        }
        var eofToken = Enumerable.Range(0, tokenWords.Length / 3).First(token =>
            tokenWords[token * 3] == 0);
        var forgedEof = tokenWords.ToArray();
        forgedEof[eofToken * 3 + 1] = source.Length - 1;
        AssertReaderResult(words, expectedSuccess: false, forgedEof);
        Assert.That(eofToken + 1 < tokenWords.Length / 3, Is.True);
        var tokenAfterEof = tokenWords.ToArray();
        tokenAfterEof[(eofToken + 1) * 3] = 2;
        AssertReaderResult(words, expectedSuccess: false, tokenAfterEof);

        void AssertReaderResult(int[] candidate, bool expectedSuccess, int[]? tokenCandidate = null)
        {
            var candidateBuffers = buffers.SetItem(ScalarFrontendHost.ProgramBufferIndex,
                buffers[ScalarFrontendHost.ProgramBufferIndex] with {
                    Values = ImmutableArray.CreateRange(candidate),
                });
            if (tokenCandidate is not null)
            {
                candidateBuffers = candidateBuffers.SetItem(3, buffers[3] with {
                    Values = ImmutableArray.CreateRange(tokenCandidate),
                });
            }
            var execution = NativeJit.ExecuteWithData(reader with {
                Buffers = candidateBuffers,
            }, (NativeRuntimePolicy)policy);
            Assert.That(execution.ExitCode, Is.EqualTo(expectedSuccess ? 42 : 2));
            var summary = execution.Data.AsSpan(execution.BufferOffsets[ScalarFrontendHost.BufferIndex],
                ScalarFrontendHost.WordCount * sizeof(int));
            if (expectedSuccess)
            {
                Assert.That(BinaryPrimitives.ReadInt32LittleEndian(summary), Is.EqualTo(88));
                Assert.That(BinaryPrimitives.ReadInt32LittleEndian(summary[sizeof(int)..]), Is.EqualTo(1));
            }
            else
            {
                {var __nunitItems1 = summary.ToArray();                Assert.Multiple(() => { foreach (var __nunitItem5 in __nunitItems1) { Assert.That(__nunitItem5, Is.EqualTo(0)); } });}
            }
        }

        void AssertRejected(params (int Word, int Value)[] changes)
        {
            var candidate = words.ToArray();
            foreach (var (word, value) in changes)
            {
                candidate[word] = value;
            }
            AssertReaderResult(candidate, expectedSuccess: false);
            var forged = new byte[programBytes.Length];
            for (var word = 0; word < candidate.Length; word++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    forged.AsSpan(word * sizeof(int), sizeof(int)), candidate[word]);
            }
            Assert.Throws<ArgumentException>(() =>
                ScalarFrontendHost.ReadProgram(source, originalPublication, forged, policy));
        }
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SummaryReaderAcceptsElseAndElseIfStatementLayoutsAsync(BoundRuntimePolicy policy)
    {
        var writerTrees = await ReadComponentsAsync(s_frontendComponents, """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (!TryPublishScalarFrontend()) { return -1; }
                return 42;
            }
            """).ConfigureAwait(true);
        var readerTrees = await ReadComponentsAsync(["ScalarFrontendProgramReader"], """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (!TryDecodeScalarFrontendProgram(
                    Host.FrontendProgram.Length, Host.Tokens.Length / 3 - 1)) { return 2; }
                return 42;
            }
            """).ConfigureAwait(true);
        var source = SourceText.From(
            "int32 Main() { if (true) { } else if (false) { } else { } return 42; } "
                + "int32 Other() => 0;");
        var buffers = ScalarFrontendHost.CreateBuffers(source);
        var writerCompilation = Compilation.Create(writerTrees, buffers, policy);
        Assert.That(writerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, writerCompilation.Diagnostics));
        var writer = ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = writerCompilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))();
        var readerCompilation = Compilation.Create(readerTrees, buffers, policy);
        Assert.That(readerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, readerCompilation.Diagnostics));
        var reader = ((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = readerCompilation.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))();
        var publication = NativeJit.ExecuteWithData(writer, (NativeRuntimePolicy)policy);
        Assert.That(publication.ExitCode, Is.EqualTo(42));
        buffers = WithPublishedTokens(buffers, publication);
        var programBytes = publication.Data.AsSpan(
            publication.BufferOffsets[ScalarFrontendHost.ProgramBufferIndex],
            ScalarFrontendHost.ProgramWordCount * sizeof(int));
        var words = new int[ScalarFrontendHost.ProgramWordCount];
        for (var word = 0; word < words.Length; word++)
        {
            words[word] = BinaryPrimitives.ReadInt32LittleEndian(
                programBytes.Slice(word * sizeof(int), sizeof(int)));
        }
        Assert.That(words[2], Is.EqualTo(2));
        var body = 8;
        var statementKinds = Enumerable.Range(0, words[body + 298])
            .Select(statement => words[body + 332 + statement * 5]).ToArray();
        Assert.That(statementKinds, Does.Contain(5));
        Assert.That(statementKinds, Does.Contain(23));

        var originalPublication = new byte[ScalarFrontendHost.WordCount * sizeof(int)];
        ScalarFrontendHost.ReadProgram(source, originalPublication, programBytes.ToArray(), policy);
        var decodingProgram = reader with {
            Buffers = buffers.SetItem(ScalarFrontendHost.ProgramBufferIndex,
                buffers[ScalarFrontendHost.ProgramBufferIndex] with {
                    Values = ImmutableArray.CreateRange(words),
                }),
        };
        var decoding = NativeJit.ExecuteWithData(decodingProgram, (NativeRuntimePolicy)policy);
        Assert.That(decoding.ExitCode, Is.EqualTo(42));

        foreach (var statement in Enumerable.Range(0, statementKinds.Length)
            .Where(statement => statementKinds[statement] is 5 or 23))
        {
            foreach (var invalidSlot in new[] { statement, statementKinds.Length + 1 })
            {
                var candidate = words.ToArray();
                candidate[body + 333 + statement * 5] = invalidSlot;
                var candidateReader = reader with {
                    Buffers = buffers.SetItem(ScalarFrontendHost.ProgramBufferIndex,
                        buffers[ScalarFrontendHost.ProgramBufferIndex] with {
                            Values = ImmutableArray.CreateRange(candidate),
                        }),
                };
                var rejected = NativeJit.ExecuteWithData(candidateReader, (NativeRuntimePolicy)policy);
                Assert.That(rejected.ExitCode, Is.EqualTo(2));
                var summary = rejected.Data.AsSpan(
                    rejected.BufferOffsets[ScalarFrontendHost.BufferIndex],
                    ScalarFrontendHost.WordCount * sizeof(int));
                {var __nunitItems2 = summary.ToArray();                Assert.Multiple(() => { foreach (var __nunitItem8 in __nunitItems2) { Assert.That(__nunitItem8, Is.EqualTo(0)); } });}

                var forgedProgram = new byte[candidate.Length * sizeof(int)];
                for (var word = 0; word < candidate.Length; word++)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(
                        forgedProgram.AsSpan(word * sizeof(int), sizeof(int)), candidate[word]);
                }
                Assert.Throws<ArgumentException>(() =>
                    ScalarFrontendHost.ReadProgram(source,
                        new byte[ScalarFrontendHost.WordCount * sizeof(int)], forgedProgram, policy));
            }
        }
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SummaryReaderRejectsMismatchedReturnAndConditionTypesBeforePublishingAsync(
        BoundRuntimePolicy policy)
    {
        var writerTrees = await ReadComponentsAsync(s_frontendComponents, """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (!TryPublishScalarFrontend()) { return -1; }
                return 42;
            }
            """).ConfigureAwait(true);
        var readerTrees = await ReadComponentsAsync(["ScalarFrontendProgramReader"], """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (!TryDecodeScalarFrontendProgram(
                    Host.FrontendProgram.Length, Host.Tokens.Length / 3 - 1)) { return 2; }
                return 42;
            }
            """).ConfigureAwait(true);
        var source = SourceText.From(
            "int32 Main() { return 42; } "
                + "int32 Other(bool condition) { if (condition) { return Helper(); } return 0; } "
                + "int32 Statements(bool condition) { "
                    + "if (condition) { int32 value = Helper(); } "
                    + "while (condition) { } do { } while (condition); "
                    + "for (; condition;) { } for (;;) { } return 0; } "
                + "int32 Proofs() { static require true; static ensure true; return 0; } "
                + "int32 LabeledWhile(bool condition) { a: while (condition) { } return 0; } "
                + "int32 LabeledDo(bool condition) { a: do { } while (condition); return 0; } "
                + "int32 LabeledFor(bool condition) { a: for (; condition;) { } return 0; } "
                + "int32 LabeledForWithoutCondition() { a: for (;;) { } return 0; } "
                + "void Empty() { return; } "
                + "int32 Helper() => 43; "
                + "int32 Four(bool first, int32 second, uint32 third, float64 fourth) => 0; "
                + "void CallEmpty() { Empty(); return; } "
                + "int32 Loose() { mut int32 value = 1 + 2; "
                    + "value = value + 1; value += 1; return 43; } "
                + "int32 NodeTypes(int32 input) { return input + 1; } "
                + "int32 ConstantLocals() { const int32 literal = 42; "
                    + "const int32 expression = 1 + 2; return literal + expression; } "
                + "void BareReturn() { return; } "
                + "void OutAssign(out int32 output, int32 ordinary) { output = 1; return; } "
                + "void OutCompound(out int32 output, int32 ordinary) { "
                    + "output = 0; output += 1; return; } "
                + "int32 SwitchRecords(int32 value) { "
                    + "switch (value) { 1 => { return 11; }, _ => { return 33; } } } "
                + "int32 SwitchTransfer() { "
                    + "switch (1) { 1 => { continue _; }, _ => { return 33; } } return 11; } "
                + "bool Negated(bool input) { return !input; }");
        var buffers = ScalarFrontendHost.CreateBuffers(source);
        var writerCompilation = Compilation.Create(writerTrees, buffers, policy);
        Assert.That(writerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, writerCompilation.Diagnostics));
        var writer = ((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = writerCompilation.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))();
        var readerCompilation = Compilation.Create(readerTrees, buffers, policy);
        Assert.That(readerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, readerCompilation.Diagnostics));
        var reader = ((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = readerCompilation.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))();
        var publication = NativeJit.ExecuteWithData(writer, (NativeRuntimePolicy)policy);
        Assert.That(publication.ExitCode, Is.EqualTo(42));
        buffers = WithPublishedTokens(buffers, publication);
        var programBytes = publication.Data.AsSpan(
            publication.BufferOffsets[ScalarFrontendHost.ProgramBufferIndex],
            ScalarFrontendHost.ProgramWordCount * sizeof(int));
        var words = new int[ScalarFrontendHost.ProgramWordCount];
        for (var word = 0; word < words.Length; word++)
        {
            words[word] = BinaryPrimitives.ReadInt32LittleEndian(
                programBytes.Slice(word * sizeof(int), sizeof(int)));
        }

        const int firstFrame = 4;
        const int body = firstFrame + 4;
        const int otherBody = firstFrame + ScalarFrontendHost.ProgramFrameWordCount + 4;
        Assert.That(words[otherBody + 37] > 0, Is.True, $"functionCount={words[2]}, nodeCount={words[otherBody + 37]}, "
                + $"frameStart={words[firstFrame + ScalarFrontendHost.ProgramFrameWordCount]}");
        Assert.That(global::System.Linq.Enumerable.Any(Enumerable.Range(0, words[otherBody + 37]),node => words[NodeOffset(otherBody, node)] == 105),Is.True);
        var root = words[body + 39];
        Assert.That(root, Is.InRange(0, words[body + 37] - 1));
        var rootTypeOffset = NodeOffset(body, root) + 1;
        Assert.That(words[rootTypeOffset], Is.EqualTo(words[body + 5]));
        foreach (var unsupportedOperation in new[] { 64, int.MaxValue })
        {
            var forgedOperation = words.ToArray();
            forgedOperation[NodeOffset(body, root)] = unsupportedOperation;
            AssertReaderResult(forgedOperation, expectedSuccess: false);
        }
        var binaryBody = firstFrame + 12 * ScalarFrontendHost.ProgramFrameWordCount + 4;
        var binaryNode = 8;
        Assert.That(words[NodeOffset(binaryBody, binaryNode)], Is.EqualTo(91));
        var binaryOffset = NodeOffset(binaryBody, binaryNode);
        foreach (var (field, value) in new[] {
            (4, binaryNode),
            (5, binaryNode),
            (6, 1),
        })
        {
            var forgedChild = words.ToArray();
            forgedChild[binaryOffset + field] = value;
            AssertReaderResult(forgedChild, expectedSuccess: false);
        }
        var forgedBinaryType = words.ToArray();
        forgedBinaryType[binaryOffset + 1] = 2;
        AssertReaderResult(forgedBinaryType, expectedSuccess: false);
        var unaryBody = firstFrame + 20 * ScalarFrontendHost.ProgramFrameWordCount + 4;
        var unaryOperations = Enumerable.Range(0, words[unaryBody + 37])
            .Select(node => words[NodeOffset(unaryBody, node)]).ToArray();
        Assert.That(unaryOperations, Does.Contain(62));
        var unaryNode = Array.IndexOf(unaryOperations, 62);
        var forgedUnaryChild = words.ToArray();
        forgedUnaryChild[NodeOffset(unaryBody, unaryNode) + 4] = unaryNode;
        AssertReaderResult(forgedUnaryChild, expectedSuccess: false);
        var forgedUnaryType = words.ToArray();
        forgedUnaryType[NodeOffset(unaryBody, unaryNode) + 1] = 1;
        AssertReaderResult(forgedUnaryType, expectedSuccess: false);
        var negativeReturnSpan = words.ToArray();
        negativeReturnSpan[NodeOffset(body, root) + 2] = -1;
        AssertReaderResult(negativeReturnSpan, expectedSuccess: false);
        var conditionalFrame = firstFrame + ScalarFrontendHost.ProgramFrameWordCount;
        var conditionalBody = conditionalFrame + 4;
        Assert.That(words[conditionalBody + 15], Is.InRange(1, 2));
        var conditionRoot = words[conditionalBody + 38];
        var conditionTypeOffset = NodeOffset(conditionalBody, conditionRoot) + 1;
        Assert.That(words[conditionTypeOffset], Is.EqualTo(5));
        var statementFrame = conditionalFrame + ScalarFrontendHost.ProgramFrameWordCount;
        var statementBody = statementFrame + 4;
        var statementOffset = statementBody + 332;
        var emptyStatementSpan = words.ToArray();
        emptyStatementSpan[statementOffset + 4] = 0;
        AssertReaderResult(emptyStatementSpan, expectedSuccess: false);
        var negativeStatementSpan = words.ToArray();
        negativeStatementSpan[statementOffset + 3] = -1;
        AssertReaderResult(negativeStatementSpan, expectedSuccess: false);
        var outOfRangeStatementSpan = words.ToArray();
        outOfRangeStatementSpan[statementOffset + 3] = source.Length;
        AssertReaderResult(outOfRangeStatementSpan, expectedSuccess: false);
        var crossFrameStatementSpan = words.ToArray();
        crossFrameStatementSpan[statementOffset + 3] = words[firstFrame + 2];
        crossFrameStatementSpan[statementOffset + 4] = 1;
        AssertReaderResult(crossFrameStatementSpan, expectedSuccess: false);
        ValidateStatementConditions(statementBody, [3, 4, 11, 12], optionalForCondition: true);
        ValidateStatementConditions(
            firstFrame + 3 * ScalarFrontendHost.ProgramFrameWordCount + 4,
            [35, 36]);
        ValidateStatementConditions(
            firstFrame + 4 * ScalarFrontendHost.ProgramFrameWordCount + 4,
            [16]);
        ValidateStatementConditions(
            firstFrame + 5 * ScalarFrontendHost.ProgramFrameWordCount + 4,
            [17]);
        ValidateStatementConditions(
            firstFrame + 6 * ScalarFrontendHost.ProgramFrameWordCount + 4,
            [18]);
        ValidateStatementConditions(
            firstFrame + 7 * ScalarFrontendHost.ProgramFrameWordCount + 4,
            [18],
            optionalForCondition: true);

        AssertReaderResult(words, expectedSuccess: true);
        var voidBody = firstFrame + 8 * ScalarFrontendHost.ProgramFrameWordCount + 4;
        foreach (var invalidReturnType in new[] { -1, 9 })
        {
            var forgedReturnType = words.ToArray();
            forgedReturnType[voidBody + 5] = invalidReturnType;
            AssertReaderResult(forgedReturnType, expectedSuccess: false);
        }
        var fourParameterBody = firstFrame + 10 * ScalarFrontendHost.ProgramFrameWordCount + 4;
        foreach (var (nameStart, nameLength) in new[] {
            (body + 9, body + 10),
            (fourParameterBody + 11, fourParameterBody + 12),
            (fourParameterBody + 13, fourParameterBody + 14),
            (fourParameterBody + 35, fourParameterBody + 36),
            (fourParameterBody + 19, fourParameterBody + 20),
        })
        {
            var emptyName = words.ToArray();
            emptyName[nameLength] = 0;
            AssertReaderResult(emptyName, expectedSuccess: false);

            var outOfFrameName = words.ToArray();
            outOfFrameName[nameStart] = source.Length;
            AssertReaderResult(outOfFrameName, expectedSuccess: false);
        }
        var partialFunctionName = words.ToArray();
        partialFunctionName[body + 9] = partialFunctionName[body + 9] + 1;
        partialFunctionName[body + 10] = 1;
        AssertReaderResult(partialFunctionName, expectedSuccess: false);
        var partialParameterName = words.ToArray();
        partialParameterName[fourParameterBody + 11] =
            partialParameterName[fourParameterBody + 11] + 1;
        partialParameterName[fourParameterBody + 12] = 1;
        AssertReaderResult(partialParameterName, expectedSuccess: false);
        foreach (var unusedParameterNameLength in new[] {
            body + 12, body + 14, body + 36, body + 20,
        })
        {
            var forgedUnusedName = words.ToArray();
            forgedUnusedName[unusedParameterNameLength] = 1;
            AssertReaderResult(forgedUnusedName, expectedSuccess: false);
        }
        foreach (var parameterField in new[] { 7, 8, 34, 18 })
        {
            var forgedParameterType = words.ToArray();
            forgedParameterType[fourParameterBody + parameterField] = 0;
            AssertReaderResult(forgedParameterType, expectedSuccess: false);

            var forgedUnusedParameterType = words.ToArray();
            forgedUnusedParameterType[body + parameterField] = 1;
            AssertReaderResult(forgedUnusedParameterType, expectedSuccess: false);
        }
        var looseBody = firstFrame + 12 * ScalarFrontendHost.ProgramFrameWordCount + 4;
        Assert.That(words[looseBody + 299], Is.EqualTo(1));
        foreach (var invalidLocalType in new[] { -1, 0, 9 })
        {
            var forgedLocalType = words.ToArray();
            forgedLocalType[looseBody + 300] = invalidLocalType;
            AssertReaderResult(forgedLocalType, expectedSuccess: false);
        }
        foreach (var invalidLocalFlags in new[] { -1, 3, 16 })
        {
            var forgedLocalFlags = words.ToArray();
            forgedLocalFlags[looseBody + 301] = invalidLocalFlags;
            AssertReaderResult(forgedLocalFlags, expectedSuccess: false);
        }
        var emptyLocalName = words.ToArray();
        emptyLocalName[looseBody + 303] = 0;
        AssertReaderResult(emptyLocalName, expectedSuccess: false);
        var negativeLocalName = words.ToArray();
        negativeLocalName[looseBody + 302] = -1;
        AssertReaderResult(negativeLocalName, expectedSuccess: false);
        var outOfRangeLocalName = words.ToArray();
        outOfRangeLocalName[looseBody + 302] = source.Length;
        AssertReaderResult(outOfRangeLocalName, expectedSuccess: false);
        var crossFrameLocalName = words.ToArray();
        crossFrameLocalName[looseBody + 302] = words[firstFrame + 2];
        crossFrameLocalName[looseBody + 303] = 1;
        AssertReaderResult(crossFrameLocalName, expectedSuccess: false);
        var forgedMismatchedLocalType = words.ToArray();
        forgedMismatchedLocalType[looseBody + 300] = 2;
        AssertReaderResult(forgedMismatchedLocalType, expectedSuccess: false);
        var sourceText = source.ToString();
        var mutableKeywordStart = sourceText.IndexOf("mut int32 value", StringComparison.Ordinal);
        Assert.That(mutableKeywordStart >= 0, Is.True);
        var keywordAsLocalName = words.ToArray();
        keywordAsLocalName[looseBody + 302] = mutableKeywordStart;
        keywordAsLocalName[looseBody + 303] = 3;
        AssertReaderResult(keywordAsLocalName, expectedSuccess: false);
        var syntheticSwitchBody = firstFrame + 18 * ScalarFrontendHost.ProgramFrameWordCount + 4;
        Assert.That(words[syntheticSwitchBody + 301], Is.EqualTo(8));
        var switchNameStart = words[syntheticSwitchBody + 302];
        var returnKeywordStart = sourceText.IndexOf("return", switchNameStart, StringComparison.Ordinal);
        Assert.That(returnKeywordStart >= switchNameStart, Is.True);
        var forgedSyntheticLocalName = words.ToArray();
        forgedSyntheticLocalName[syntheticSwitchBody + 302] = returnKeywordStart;
        AssertReaderResult(forgedSyntheticLocalName, expectedSuccess: false);
        var constantBody = firstFrame + 14 * ScalarFrontendHost.ProgramFrameWordCount + 4;
        foreach (var kind in new[] { 27, 28 })
        {
            var statement = Enumerable.Range(0, words[constantBody + 298])
                .Single(index => words[constantBody + 332 + index * 5] == kind);
            var slot = words[constantBody + 332 + statement * 5 + 1];
            Assert.That(slot, Is.InRange(1, words[constantBody + 6] + words[constantBody + 299]));
            var localTypeOffset = constantBody + 300 + (slot - words[constantBody + 6] - 1) * 4;
            var forgedConstantType = words.ToArray();
            forgedConstantType[localTypeOffset] = 2;
            AssertReaderResult(forgedConstantType, expectedSuccess: false);
        }
        var bareReturnBody = firstFrame + 15 * ScalarFrontendHost.ProgramFrameWordCount + 4;
        var bareReturnKinds = Enumerable.Range(0, words[bareReturnBody + 298])
            .Select(statement => words[bareReturnBody + 332 + statement * 5]).ToArray();
        Assert.That(bareReturnKinds, Does.Contain(22));
        var bareReturnStatement = Array.IndexOf(bareReturnKinds, 22);
        var bareReturnOffset = bareReturnBody + 332 + bareReturnStatement * 5;
        Assert.That(words[bareReturnOffset + 1], Is.EqualTo(0));
        Assert.That(words[bareReturnOffset + 2], Is.EqualTo(-1));
        var slottedBareReturn = words.ToArray();
        slottedBareReturn[bareReturnOffset + 1] = 1;
        AssertReaderResult(slottedBareReturn, expectedSuccess: false);
        var nonVoidBareReturn = words.ToArray();
        nonVoidBareReturn[bareReturnBody + 5] = 1;
        AssertReaderResult(nonVoidBareReturn, expectedSuccess: false);
        var rootedBareReturn = words.ToArray();
        rootedBareReturn[bareReturnOffset + 2] = 0;
        AssertReaderResult(rootedBareReturn, expectedSuccess: false);
        var outAssignBody = firstFrame + 16 * ScalarFrontendHost.ProgramFrameWordCount + 4;
        var outAssignStatement = Enumerable.Range(0, words[outAssignBody + 298])
            .Single(statement => words[outAssignBody + 332 + statement * 5] == 2);
        var forgedOutAssignSlot = words.ToArray();
        forgedOutAssignSlot[outAssignBody + 332 + outAssignStatement * 5 + 1] = 2;
        AssertReaderResult(forgedOutAssignSlot, expectedSuccess: false);
        var outCompoundBody = firstFrame + 17 * ScalarFrontendHost.ProgramFrameWordCount + 4;
        var outCompoundStatement = Enumerable.Range(0, words[outCompoundBody + 298])
            .Single(statement => words[outCompoundBody + 332 + statement * 5] == 9);
        var forgedOutCompoundSlot = words.ToArray();
        forgedOutCompoundSlot[outCompoundBody + 332 + outCompoundStatement * 5 + 1] = 2;
        AssertReaderResult(forgedOutCompoundSlot, expectedSuccess: false);
        var switchBody = firstFrame + 18 * ScalarFrontendHost.ProgramFrameWordCount + 4;
        var switchCount = words[switchBody + 298];
        foreach (var statement in Enumerable.Range(0, switchCount)
            .Where(statement => words[switchBody + 332 + statement * 5] is 38 or 39))
        {
            var offset = switchBody + 332 + statement * 5;
            foreach (var invalidEnd in new[] { statement, switchCount + 1 })
            {
                var forgedSwitchEnd = words.ToArray();
                forgedSwitchEnd[offset + 1] = invalidEnd;
                AssertReaderResult(forgedSwitchEnd, expectedSuccess: false);
            }
            if (words[offset] == 39 && words[offset + 2] >= 0)
            {
                var forgedSwitchRoot = words.ToArray();
                forgedSwitchRoot[offset + 2] = words[switchBody + 37];
                AssertReaderResult(forgedSwitchRoot, expectedSuccess: false);
            }
        }
        var switchTransferBody = firstFrame + 19 * ScalarFrontendHost.ProgramFrameWordCount + 4;
        var switchTransferStatement = Enumerable.Range(0, words[switchTransferBody + 298])
            .Single(statement => words[switchTransferBody + 332 + statement * 5] == 40);
        var switchTransferOffset = switchTransferBody + 332 + switchTransferStatement * 5;
        Assert.That(words[switchTransferOffset + 1], Is.InRange(0, switchTransferStatement - 1));
        foreach (var invalidTarget in new[] { -1, switchTransferStatement })
        {
            var forgedTransferTarget = words.ToArray();
            forgedTransferTarget[switchTransferOffset + 1] = invalidTarget;
            AssertReaderResult(forgedTransferTarget, expectedSuccess: false);
        }
        var assignmentBody = looseBody;
        foreach (var kind in new[] { 2, 9 })
        {
            var assignmentStatement = Enumerable.Range(0, words[assignmentBody + 298])
                .Single(statement => words[assignmentBody + 332 + statement * 5] == kind);
            var assignmentOffset = assignmentBody + 332 + assignmentStatement * 5;
            var assignmentSlot = words[assignmentOffset + 1];
            var assignmentRoot = words[assignmentOffset + 2];
            Assert.That(assignmentRoot, Is.InRange(0, words[assignmentBody + 37] - 1));
            var assignmentTypeOffset = NodeOffset(assignmentBody, assignmentRoot) + 1;
            var mismatchedAssignmentType = words.ToArray();
            mismatchedAssignmentType[assignmentTypeOffset] =
                words[assignmentTypeOffset] == 1 ? 5 : 1;
            AssertReaderResult(mismatchedAssignmentType, expectedSuccess: false);
            var invalidAssignmentSlot = words.ToArray();
            invalidAssignmentSlot[assignmentOffset + 1] = assignmentSlot + 1;
            AssertReaderResult(invalidAssignmentSlot, expectedSuccess: false);
            var missingAssignmentRoot = words.ToArray();
            missingAssignmentRoot[assignmentOffset + 2] = -1;
            AssertReaderResult(missingAssignmentRoot, expectedSuccess: false);
        }
        var initializerStatement = Enumerable.Range(0, words[looseBody + 298])
            .Single(statement => words[looseBody + 332 + statement * 5] == 1);
        var initializerOffset = looseBody + 332 + initializerStatement * 5;
        var initializerRoot = words[initializerOffset + 2];
        Assert.That(words[NodeOffset(looseBody, initializerRoot)], Is.Not.EqualTo(105));
        foreach (var invalidLocalSlot in new[] { 0, 2 })
        {
            var forgedLocalSlot = words.ToArray();
            forgedLocalSlot[initializerOffset + 1] = invalidLocalSlot;
            AssertReaderResult(forgedLocalSlot, expectedSuccess: false);
        }
        var forgedLocalRoot = words.ToArray();
        forgedLocalRoot[initializerOffset + 2] = -1;
        AssertReaderResult(forgedLocalRoot, expectedSuccess: false);
        var nodeTypesBody = firstFrame + 13 * ScalarFrontendHost.ProgramFrameWordCount + 4;
        var returnRoot = words[nodeTypesBody + 39];
        var nonRootNode = Enumerable.Range(0, words[nodeTypesBody + 37])
            .First(node => node != returnRoot
                && words[NodeOffset(nodeTypesBody, node)] != 105);
        Assert.That(words[nodeTypesBody + 15], Is.EqualTo(0));
        foreach (var invalidNodeType in new[] { -1, 0, 9 })
        {
            var forgedNodeType = words.ToArray();
            forgedNodeType[NodeOffset(nodeTypesBody, nonRootNode) + 1] = invalidNodeType;
            AssertReaderResult(forgedNodeType, expectedSuccess: false);
        }
        var nonRootNodeOffset = NodeOffset(nodeTypesBody, nonRootNode);
        var emptyNodeSpan = words.ToArray();
        emptyNodeSpan[nonRootNodeOffset + 3] = 0;
        AssertReaderResult(emptyNodeSpan, expectedSuccess: false);
        var outOfRangeNodeSpan = words.ToArray();
        outOfRangeNodeSpan[nonRootNodeOffset + 2] = source.Length;
        AssertReaderResult(outOfRangeNodeSpan, expectedSuccess: false);
        var crossFrameNodeSpan = words.ToArray();
        crossFrameNodeSpan[nonRootNodeOffset + 2] = words[firstFrame + 2];
        crossFrameNodeSpan[nonRootNodeOffset + 3] = 1;
        AssertReaderResult(crossFrameNodeSpan, expectedSuccess: false);
        var callEmptyBody = firstFrame + 11 * ScalarFrontendHost.ProgramFrameWordCount + 4;
        var voidCallNode = Enumerable.Range(0, words[callEmptyBody + 37])
            .Single(node => words[NodeOffset(callEmptyBody, node)] == 105
                && words[NodeOffset(callEmptyBody, node) + 1] == 0);
        var voidCallStatement = Enumerable.Range(0, words[callEmptyBody + 298])
            .Single(statement => words[callEmptyBody + 332 + statement * 5] == 37
                && words[callEmptyBody + 332 + statement * 5 + 2] == voidCallNode);
        var forgedVoidCallKind = words.ToArray();
        forgedVoidCallKind[callEmptyBody + 332 + voidCallStatement * 5] = 1;
        AssertReaderResult(forgedVoidCallKind, expectedSuccess: false);
        var forgedVoidCallRoot = words.ToArray();
        forgedVoidCallRoot[callEmptyBody + 332 + voidCallStatement * 5 + 2] = -1;
        AssertReaderResult(forgedVoidCallRoot, expectedSuccess: false);
        var forged = words.ToArray();
        forged[rootTypeOffset] = forged[rootTypeOffset] == 1 ? 5 : 1;
        AssertReaderResult(forged, expectedSuccess: false);
        var forgedCondition = words.ToArray();
        forgedCondition[conditionTypeOffset] = 1;
        AssertReaderResult(forgedCondition, expectedSuccess: false);

        void ValidateStatementConditions(
            int statementBody,
            int[] expectedKinds,
            bool optionalForCondition = false)
        {
            var count = words[statementBody + 298];
            var kinds = Enumerable.Range(0, count)
                .Select(statement => words[statementBody + 332 + statement * 5])
                .ToArray();
            foreach (var kind in expectedKinds)
            {
                Assert.That(kinds, Does.Contain(kind));
            }
            if (expectedKinds.Contains(35))
            {
                foreach (var statement in Enumerable.Range(0, count)
                    .Where(statement => kinds[statement] is 35 or 36))
                {
                    var forgedStaticProofSlot = words.ToArray();
                    forgedStaticProofSlot[statementBody + 332 + statement * 5 + 1] = 1;
                    AssertReaderResult(forgedStaticProofSlot, expectedSuccess: false);
                }
            }
            if (expectedKinds.Contains(3))
            {
                for (var statement = 0; statement < count; statement++)
                {
                    var statementOffset = statementBody + 332 + statement * 5;
                    foreach (var invalidKind in new[] { 0, 41 })
                    {
                        var forgedKind = words.ToArray();
                        forgedKind[statementOffset] = invalidKind;
                        AssertReaderResult(forgedKind, expectedSuccess: false);
                    }
                }
            }
            if (optionalForCondition)
            {
                Assert.That(global::System.Linq.Enumerable.Any(Enumerable.Range(0, count),statement =>
                    (kinds[statement] is 12 or 18)
                        && words[statementBody + 332 + statement * 5 + 2] == -1),Is.True);
            }
            for (var statement = 0; statement < count; statement++)
            {
                var kind = kinds[statement];
                if (kind is 3 or 4 or 11 or 12 or 14 or 16 or 17 or 18)
                {
                    var statementOffset = statementBody + 332 + statement * 5;
                    foreach (var invalidEnd in new[] { statement, count + 1 })
                    {
                        var forgedStatementEnd = words.ToArray();
                        forgedStatementEnd[statementOffset + 1] = invalidEnd;
                        AssertReaderResult(forgedStatementEnd, expectedSuccess: false);
                    }
                }
                if (kind is not (3 or 4 or 11 or 12 or 16 or 17 or 18 or 35 or 36))
                {
                    continue;
                }
                var conditionRoot = words[statementBody + 332 + statement * 5 + 2];
                if (conditionRoot == -1)
                {
                    Assert.That(kind is 12 or 18, Is.True);
                    continue;
                }
                Assert.That(conditionRoot, Is.InRange(0, words[statementBody + 37] - 1));
                var typeOffset = NodeOffset(statementBody, conditionRoot) + 1;
                Assert.That(words[typeOffset], Is.EqualTo(5));
                var forgedStatementCondition = words.ToArray();
                forgedStatementCondition[typeOffset] = 1;
                AssertReaderResult(forgedStatementCondition, expectedSuccess: false);
                if (kind is not (12 or 18))
                {
                    var forgedMissingCondition = words.ToArray();
                    forgedMissingCondition[statementBody + 332 + statement * 5 + 2] = -1;
                    AssertReaderResult(forgedMissingCondition, expectedSuccess: false);
                }
            }
        }

        void AssertReaderResult(int[] candidate, bool expectedSuccess)
        {
            var execution = NativeJit.ExecuteWithData(reader with {
                Buffers = buffers.SetItem(ScalarFrontendHost.ProgramBufferIndex,
                    buffers[ScalarFrontendHost.ProgramBufferIndex] with {
                        Values = ImmutableArray.CreateRange(candidate),
                    }),
            }, (NativeRuntimePolicy)policy);
            Assert.That(execution.ExitCode, Is.EqualTo(expectedSuccess ? 42 : 2));
            var summary = execution.Data.AsSpan(execution.BufferOffsets[ScalarFrontendHost.BufferIndex],
                ScalarFrontendHost.WordCount * sizeof(int));
            if (expectedSuccess)
            {
                Assert.That(BinaryPrimitives.ReadInt32LittleEndian(summary), Is.EqualTo(88));
                Assert.That(BinaryPrimitives.ReadInt32LittleEndian(summary[sizeof(int)..]), Is.EqualTo(1));
            }
            else
            {
                {var __nunitItems3 = summary.ToArray();                Assert.Multiple(() => { foreach (var __nunitItem11 in __nunitItems3) { Assert.That(__nunitItem11, Is.EqualTo(0)); } });}
            }
            var retained = execution.Data.AsSpan(
                execution.BufferOffsets[ScalarFrontendHost.ProgramBufferIndex],
                ScalarFrontendHost.ProgramWordCount * sizeof(int));
            for (var word = 0; word < candidate.Length; word++)
            {
                Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
                    retained.Slice(word * sizeof(int), sizeof(int))), Is.EqualTo(candidate[word]));
            }
        }

        static int NodeOffset(int body, int node) => body + (node < 32
            ? 42 + node * 8 : 392 + (node - 32) * 8);
    }

    private static ImmutableArray<HostBuffer> WithPublishedTokens(
        ImmutableArray<HostBuffer> buffers, NativeExecutionResult execution)
    {
        const int tokenBufferIndex = 3;
        var tokenBuffer = buffers[tokenBufferIndex];
        var tokenBytes = execution.Data.AsSpan(execution.BufferOffsets[tokenBufferIndex],
            tokenBuffer.Values.Length * sizeof(int));
        var tokens = ImmutableArray.CreateBuilder<int>(tokenBuffer.Values.Length);
        for (var word = 0; word < tokenBuffer.Values.Length; word++)
        {
            tokens.Add(BinaryPrimitives.ReadInt32LittleEndian(
                tokenBytes.Slice(word * sizeof(int), sizeof(int))));
        }
        return buffers.SetItem(tokenBufferIndex, tokenBuffer with {
            Values = tokens.MoveToImmutable(),
        });
    }
}
