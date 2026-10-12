using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Lexing;

/// <summary>Owned scanner results, audited v88 program and independently checked comparison summary.</summary>
public sealed record ScalarFrontendInvocationResult(ScannerInvocationResult Scanner,
    ScalarFrontendReadResult Frontend, ImmutableArray<int> Summary);

public static partial class ScalarFrontendHost
{
    /// <summary>The provisional whole-invocation token ceiling, including the scanner's real EOF.</summary>
    public const int MaximumInvocationTokens = 64;

    /// <summary>The source-bounded token ceiling, including the scanner's real EOF.</summary>
    public const int MaximumExpandedInvocationTokens = ScannerHost.MaximumSourceLength + 1;

    /// <summary>The separate provisional v88 publication and summary invocation identity.</summary>
    public static string InvocationProfileIdentity { get; } =
        CreateInvocationProfileIdentity("scalar-frontend-v88-summary-1", MaximumInvocationTokens);

    /// <summary>The separately fingerprinted source-bounded v88 publication and summary invocation identity.</summary>
    public static string ExpandedInvocationProfileIdentity { get; } =
        CreateInvocationProfileIdentity("scalar-frontend-v88-summary-2", MaximumExpandedInvocationTokens);

    /// <summary>Creates isolated fresh buffers for the 64-token complete v88 profile.</summary>
    public static ImmutableArray<HostBuffer> CreateInvocationProfileBuffers(SourceText source) =>
        CreateInvocationProfileBuffers(source, InvocationProfileIdentity);

    /// <summary>Creates isolated fresh buffers for the source-bounded complete v88 profile.</summary>
    public static ImmutableArray<HostBuffer> CreateExpandedInvocationProfileBuffers(SourceText source) =>
        CreateInvocationProfileBuffers(source, ExpandedInvocationProfileIdentity);

    internal static ImmutableArray<HostBuffer> CreateInvocationProfileBuffers(SourceText source, string? identity)
    {
        if (!IsInvocationProfileIdentity(identity))
        {
            throw new ArgumentException("Unsupported scalar frontend invocation profile.", nameof(identity));
        }
        return
        [.. CreateBuffers(source).Select(buffer => buffer with {
            IsInvocationScoped = true,
            InvocationProfileId = identity,
        })];
    }

    internal static bool IsInvocationProfileIdentity(string? identity) =>
        identity is not null
        && (identity == InvocationProfileIdentity
            || identity == ExpandedInvocationProfileIdentity);

    internal static bool IsInvocationProfileBufferSet(ImmutableArray<HostBuffer> buffers, string? identity)
    {
        if (!IsInvocationProfileIdentity(identity) || buffers.IsDefault
            || buffers.Length != ProgramBufferIndex + 1
            || buffers.Any(buffer => buffer is null || buffer.Values.IsDefault
                || !buffer.IsInvocationScoped || buffer.InvocationProfileId != identity))
        {
            return false;
        }
        var frontend = buffers[BufferIndex];
        var program = buffers[ProgramBufferIndex];
        if (frontend.Name != "Frontend" || frontend.ElementType != PrimitiveType.Int32 || !frontend.IsWritable
            || program.Name != "FrontendProgram" || program.ElementType != PrimitiveType.Int32 || !program.IsWritable
            || !buffers.All(static buffer => buffer.Values.IsEmpty)
                && (frontend.Values.Length != WordCount || program.Values.Length != ProgramWordCount
                    || frontend.Values.AsSpan().IndexOfAnyExcept(0) >= 0
                    || program.Values.AsSpan().IndexOfAnyExcept(0) >= 0))
        {
            return false;
        }
        return ScannerHost.IsInvocationProfileBufferSet(
            [.. buffers.Take(BufferIndex).Select(static buffer => buffer with {
                InvocationProfileId = ScannerHost.InvocationProfileIdentity,
            })], ScannerHost.InvocationProfileIdentity);
    }

    internal static ScalarFrontendInvocationResult ReadInvocationResult(SourceText source, int tokenCount,
        NativeExecutionResult execution, ImmutableArray<HostBuffer> buffers, BoundRuntimePolicy policy)
    {
        var identity = buffers.IsDefaultOrEmpty ? null : buffers[0]?.InvocationProfileId;
        var maximumTokens = identity == ExpandedInvocationProfileIdentity
            ? MaximumExpandedInvocationTokens : MaximumInvocationTokens;
        if (tokenCount < 1 || tokenCount > maximumTokens
            || execution.BufferOffsets.Length != ProgramBufferIndex + 1
            || !IsInvocationProfileBufferSet(buffers, identity))
        {
            throw new ArgumentException("Invalid or failed scalar frontend invocation.", nameof(execution));
        }
        var scanner = ScannerHost.ReadInvocationResult(source, tokenCount,
            execution with { BufferOffsets = [.. execution.BufferOffsets.Take(BufferIndex)] },
            [.. buffers.Take(BufferIndex)]);

        ReadOnlySpan<byte> BufferBytes(int index)
        {
            var offset = execution.BufferOffsets[index];
            var length = checked(buffers[index].Values.Length * sizeof(int));
            if (offset < 0 || offset > execution.Data.Length || length > execution.Data.Length - offset)
            {
                throw new ArgumentException("Frontend result lies outside copied invocation data.", nameof(execution));
            }
            return execution.Data.AsSpan(offset, length);
        }

        var programBytes = BufferBytes(ProgramBufferIndex);
        if (programBytes.Length != ProgramWordCount * sizeof(int)
            || BinaryPrimitives.ReadInt32LittleEndian(programBytes) != 88
            || BinaryPrimitives.ReadInt32LittleEndian(programBytes[sizeof(int)..]) != 1)
        {
            throw new ArgumentException("A completed v88 program is required.", nameof(execution));
        }
        // Complete v88 publication requires zero scratch; the decoder replaces it with its summary.
        var frontend = ReadProgram(source, new byte[WordCount * sizeof(int)], programBytes, policy);
        if (!frontend.Projection.IsValid || frontend.Program is null)
        {
            throw new ArgumentException("The frontend program did not pass the host audit.", nameof(execution));
        }
        var expected = new int[WordCount];
        expected[0] = 88;
        expected[1] = 1;
        var functions = frontend.Projection.Functions;
        for (var index = 0; index < functions.Length; index++)
        {
            var function = functions[index];
            var entry = 2 + (index * 7);
            var frame = 4 + (index * ProgramFrameWordCount);
            for (var word = 0; word < 4; word++)
            {
                expected[entry + word] = BinaryPrimitives.ReadInt32LittleEndian(
                    programBytes.Slice((frame + word) * sizeof(int), sizeof(int)));
            }
            var flags = (function.IsPublic ? 1 : 0) | (function.IsNoThrow ? 2 : 0)
                | (function.DoesNotReturn ? 4 : 0) | (function.RequiresUnsafeCaller ? 8 : 0);
            expected[entry + 4] = function.ReturnType | (function.FirstType << 4)
                | (function.SecondType << 8) | (function.ThirdType << 12) | (function.FourthType << 16)
                | (flags << 20) | (function.ParameterCount << 24) | (function.OutParameterMask << 27);
            expected[entry + 5] = function.Nodes.Length;
            expected[entry + 6] = function.Statements.Length | (function.Locals.Length << 4)
                | (function.InitRefParameterMask << 8);
            for (var node = 0; node < function.Nodes.Length; node++)
            {
                if (function.Nodes[node].Operation == 105)
                {
                    expected[SummaryTargetOffset + (index * MaximumNodeCount) + node] =
                        function.CallTargets[node] + 1;
                }
            }
        }
        var summaryBytes = BufferBytes(BufferIndex);
        var summary = ImmutableArray.CreateBuilder<int>(WordCount);
        for (var word = 0; word < WordCount; word++)
        {
            var actual = BinaryPrimitives.ReadInt32LittleEndian(summaryBytes.Slice(word * sizeof(int), sizeof(int)));
            if (actual != expected[word])
            {
                throw new ArgumentException("Frontend summary contradicts the audited program.", nameof(execution));
            }
            summary.Add(actual);
        }
        return new ScalarFrontendInvocationResult(scanner, frontend, summary.MoveToImmutable());
    }

    private static string CreateInvocationProfileIdentity(string profileName, int maximumTokens)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(profileName);
        writer.Write(ScannerHost.InvocationProfileIdentity);
        writer.Write(88);
        writer.Write(WordCount);
        writer.Write(ProgramWordCount);
        writer.Write(ProgramFrameWordCount);
        writer.Write(MaximumFunctions);
        writer.Write(MaximumNodeCount);
        writer.Write(maximumTokens);
        writer.Write($"Frontend:int32:writable:{WordCount};FrontendProgram:int32:writable:{ProgramWordCount}");
        writer.Write($"int32:token-count-on-success;summary:status[0..1];frames[2+7*f];targets[{SummaryTargetOffset}+64*f]=target+1");
        writer.Write("frame:start,end,source-start,source-length;type-nibbles@0,4,8,12,16;flags@20;params@24;out@27");
        writer.Write($"frame:node-count;statements@0,locals@4,init-ref@8;unused=0;v88:frame-{ProgramFrameWordCount},body-{ProgramBodyWordCount},summary-{WordCount},targets-{MaximumNodeCount}");
        return $"{profileName}:{Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant()}";
    }
}
