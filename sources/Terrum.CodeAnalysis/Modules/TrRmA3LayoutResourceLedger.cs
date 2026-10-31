// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Classifies a host admission failure from the explicitly selected A3 fixed-array profile.</summary>
public enum TrRmA3AdmissionFailureKind
{
    /// <summary>No marked A3 admission failure is present.</summary>
    None = 0,
    /// <summary>The selected artifact or retained binding is invalid.</summary>
    InvalidArtifact = 1,
    /// <summary>Target layout arithmetic exceeded the representable range.</summary>
    TargetLayoutOverflow = 2,
    /// <summary>A structurally understood feature is outside supported A3 admission.</summary>
    UnsupportedAdmission = 3,
    /// <summary>Deterministic A3 layout processing capacity was exhausted.</summary>
    ResourceLimit = 4,
}

/// <summary>Reports deterministic capacity exhaustion while validating the selected A3 fixed-array profile.</summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors",
    Justification = "Construction is internal to the selected A3 host boundary.")]
public sealed class TrRmResourceLimitException : NotSupportedException
{
    internal TrRmResourceLimitException(string operation, string scope,
        string dimension, ulong limit, BigInteger required,
        string? moduleIdentity)
        : base(CreateMessage(operation, scope, dimension, limit, required,
            moduleIdentity))
    {
        if (string.IsNullOrWhiteSpace(operation))
        {
            throw new ArgumentException("The resource operation is required.",
                nameof(operation));
        }
        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new ArgumentException("The resource scope is required.",
                nameof(scope));
        }
        if (moduleIdentity is not null
            && string.IsNullOrWhiteSpace(moduleIdentity))
        {
            throw new ArgumentException(
                "The selected Module identity must be nonempty.",
                nameof(moduleIdentity));
        }
        if (dimension is not ("work" or "depth"))
        {
            throw new ArgumentOutOfRangeException(nameof(dimension));
        }
        if (required < BigInteger.Zero || required <= limit)
        {
            throw new ArgumentOutOfRangeException(nameof(required));
        }

        Operation = operation;
        Scope = scope;
        Dimension = dimension;
        Limit = limit;
        Required = required;
        ModuleIdentity = moduleIdentity;
    }

    /// <summary>Gets the selected host operation whose deterministic capacity was exhausted.</summary>
    public string Operation { get; }

    /// <summary>Gets the selected aggregate scope whose deterministic capacity was exhausted.</summary>
    public string Scope { get; }

    /// <summary>Gets the exhausted resource dimension: <c>work</c> or <c>depth</c>.</summary>
    public string Dimension { get; }

    /// <summary>Gets the inclusive limit of the exhausted resource dimension.</summary>
    public ulong Limit { get; }

    /// <summary>Gets the nonnegative cumulative amount that failed reservation.</summary>
    public BigInteger Required { get; }

    /// <summary>Gets the selected root Module identity when the request established one.</summary>
    public string? ModuleIdentity { get; }

    private static string CreateMessage(string operation, string scope,
        string dimension, ulong limit, BigInteger required,
        string? moduleIdentity)
        => $"TRRM resource limit exceeded for '{operation}' in '{scope}': "
            + $"{dimension} requires {required}, limit is {limit}."
            + (moduleIdentity is null
                ? string.Empty
                : $" Selected Module: '{moduleIdentity}'.");
}

internal sealed class TrRmA3AdmissionScope
{
    private const string A3SchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1";
    private readonly object _identity = new();
    private int _executionHandoff;

    internal void Mark(Exception exception, TrRmA3AdmissionFailureKind kind)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Volatile.Read(ref _executionHandoff) != 0)
        {
            return;
        }

        TrRmA3AdmissionFailureMarkers.Mark(exception, _identity,
            A3SchemaIdentity, kind);
    }

    internal void EnterExecutionHandoff()
    {
        if (Interlocked.Exchange(ref _executionHandoff, 1) != 0)
        {
            throw new InvalidOperationException(
                "The A3 execution handoff was already entered.");
        }
    }

    internal T ExecuteAfterHandoff<T>(Func<T> invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        EnterExecutionHandoff();
        try
        {
            return invocation();
        }
        catch (Exception exception)
        {
            TrRmA3AdmissionFailureMarkers.Remove(exception);
            throw;
        }
    }

    internal bool IsBeforeExecutionHandoff
        => Volatile.Read(ref _executionHandoff) == 0;
}

internal static class TrRmA3AdmissionFailureMarkers
{
    private sealed record Marker(object ScopeIdentity, string SchemaIdentity,
        TrRmA3AdmissionFailureKind Kind, bool PreExecution);

    private static readonly ConditionalWeakTable<Exception, Marker> s_markers = [];
    private static readonly Lock s_gate = new();

    internal static void Mark(Exception exception, object scopeIdentity,
        string schemaIdentity, TrRmA3AdmissionFailureKind kind)
    {
        lock (s_gate)
        {
            if (s_markers.TryGetValue(exception, out var existing))
            {
                if (!ReferenceEquals(existing.ScopeIdentity, scopeIdentity))
                {
                    _ = s_markers.Remove(exception);
                }

                return;
            }

            if (kind == TrRmA3AdmissionFailureKind.None)
            {
                return;
            }

            s_markers.Add(exception, new Marker(scopeIdentity, schemaIdentity,
                kind, PreExecution: true));
        }
    }

    internal static bool TryGet(Exception exception,
        out TrRmA3AdmissionFailureKind kind)
    {
        if (s_markers.TryGetValue(exception, out var marker)
            && marker.PreExecution
            && StringComparer.Ordinal.Equals(marker.SchemaIdentity,
                "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1"))
        {
            kind = marker.Kind;
            return true;
        }

        kind = TrRmA3AdmissionFailureKind.None;
        return false;
    }

    internal static void Remove(Exception exception)
    {
        lock (s_gate)
        {
            _ = s_markers.Remove(exception);
        }
    }
}

internal sealed class TrRmA3LayoutResourceLedger
{
    internal const ulong WorkLimit = 100_000;
    internal const ulong DepthLimit = 128;
    private const string OperationId = "trrm.a3-fixed-array-layout/1";
    private const string RootScope = "root-request";

    internal TrRmA3AdmissionScope AdmissionScope { get; } = new();
    private readonly string? _moduleIdentity;
    private BigInteger _work;
    private ulong _depth;
    private ulong _depthContext;

    internal TrRmA3LayoutResourceLedger(string? moduleIdentity = null)
    {
        if (moduleIdentity is not null
            && string.IsNullOrWhiteSpace(moduleIdentity))
        {
            throw new ArgumentException(
                "The selected Module identity must be nonempty.",
                nameof(moduleIdentity));
        }

        _moduleIdentity = moduleIdentity;
    }

    internal ulong WorkRemaining
        => WorkLimit - checked((ulong)_work);

    internal void ConsumeWork(ulong units = 1)
    {
        var required = _work + units;
        EnsureWork(required);
        _work = required;
    }

    internal void ReserveWorkAtDepth(ulong units, ulong requiredDepth)
    {
        var requiredWork = _work + units;
        EnsureWork(requiredWork);
        CheckDepth(_depthContext + _depth + requiredDepth);
        _work = requiredWork;
    }

    internal FrameLease EnterFrame()
    {
        var requiredWork = _work + 1;
        EnsureWork(requiredWork);
        var requiredDepth = _depth + 1;
        CheckDepth(_depthContext + requiredDepth);
        _work = requiredWork;
        _depth = requiredDepth;
        return new FrameLease(this);
    }

    internal void CheckDepth(ulong requiredDepth)
    {
        if (requiredDepth > DepthLimit)
        {
            var exception = CreateLimit("depth", DepthLimit, requiredDepth);
            AdmissionScope.Mark(exception,
                TrRmA3AdmissionFailureKind.ResourceLimit);
            throw exception;
        }
    }

    internal void CheckDepthFromCurrent(ulong relativeDepth)
        => CheckDepth(_depthContext + _depth + relativeDepth);

    internal DepthContextLease EnterDepthContext(ulong logicalDepth)
    {
        ArgumentOutOfRangeException.ThrowIfZero(logicalDepth,
            nameof(logicalDepth));

        var previousDepthContext = _depthContext;
        var nextDepthContext = logicalDepth - 1;
        CheckDepth(nextDepthContext + _depth);
        _depthContext = nextDepthContext;
        return new DepthContextLease(this, previousDepthContext);
    }

    private void ExitDepthContext(ulong previousDepthContext)
        => _depthContext = previousDepthContext;

    private void ExitFrame()
    {
        if (_depth == 0)
        {
            throw new InvalidOperationException(
                "The A3 layout resource ledger has no active frame.");
        }

        _depth--;
    }

    private TrRmResourceLimitException CreateLimit(string dimension,
        ulong limit, BigInteger required)
        => new(OperationId, RootScope, dimension, limit, required,
            _moduleIdentity);

    private void EnsureWork(BigInteger required)
    {
        if (required <= WorkLimit)
        {
            return;
        }

        var exception = CreateLimit("work", WorkLimit, required);
        AdmissionScope.Mark(exception,
            TrRmA3AdmissionFailureKind.ResourceLimit);
        throw exception;
    }

    internal readonly struct FrameLease(TrRmA3LayoutResourceLedger? owner)
        : IDisposable
    {
        private readonly TrRmA3LayoutResourceLedger? _owner = owner;

        public void Dispose() => _owner?.ExitFrame();
    }

    internal readonly struct DepthContextLease(
        TrRmA3LayoutResourceLedger? owner, ulong previousDepthContext)
        : IDisposable
    {
        private readonly TrRmA3LayoutResourceLedger? _owner = owner;
        private readonly ulong _previousDepthContext = previousDepthContext;

        public void Dispose()
            => _owner?.ExitDepthContext(_previousDepthContext);
    }
}
