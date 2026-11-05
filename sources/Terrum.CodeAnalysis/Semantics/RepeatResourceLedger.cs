// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Terrum.CodeAnalysis.Semantics;

internal enum SemanticResourceDimension : byte
{
    Work,
    Depth,
    State,
}

[SuppressMessage("Design", "CA1032:Implement standard exception constructors",
    Justification = "Construction is internal to the source verification boundary.")]
internal sealed class SemanticRepeatResourceLimitException : NotSupportedException
{
    internal SemanticResourceDimension Dimension { get; }
    internal ulong Limit { get; }
    internal BigInteger Required { get; }

    internal SemanticRepeatResourceLimitException(
        SemanticResourceDimension dimension, ulong limit,
        BigInteger required)
        : base($"The Repeat semantic {DimensionName(dimension)} "
            + $"limit is {limit}; {required} units are required.")
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(required,
            new BigInteger(limit), nameof(required));

        Dimension = dimension;
        Limit = limit;
        Required = required;
    }

    private static string DimensionName(SemanticResourceDimension dimension)
        => dimension switch {
            SemanticResourceDimension.Work => "work",
            SemanticResourceDimension.Depth => "depth",
            SemanticResourceDimension.State => "state",
            _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
        };
}

internal sealed class RepeatResourceLedger
{
    internal const ulong WorkLimit = 1_048_576;
    internal const ulong LiveStateByteLimit = 16_777_216;
    internal const ulong DepthLimit = SemanticVerifier.MaximumDepth;
    private const ulong RepeatFrameWork = 7;
    private const ulong RepeatFrameExitWork = 1;
    private const ulong RepeatFrameBytes = 48;

    private ulong _work;
    private ulong _liveStateBytes;
    private ulong _depth;
    private bool _callableActive;
    private int _stateTreeReservationDepth;
    private readonly HashSet<StateLease> _persistentStateLeases = [];

    internal ulong WorkUsed => _work;
    internal ulong LiveStateBytes => _liveStateBytes;
    internal ulong ActiveDepth => _depth;
    internal ulong PeakWork { get; private set; }
    internal ulong PeakLiveStateBytes { get; private set; }
    internal ulong PeakDepth { get; private set; }
    internal bool IsStateTreeReservationActive
        => _stateTreeReservationDepth != 0;

    internal CallableLease BeginCallable()
    {
        if (_callableActive || _liveStateBytes != 0 || _depth != 0
            || _stateTreeReservationDepth != 0
            || _persistentStateLeases.Count != 0)
        {
            throw new InvalidOperationException(
                "A Repeat resource callable scope is already active.");
        }

        _work = 0;
        _callableActive = true;
        return new CallableLease(this);
    }

    internal void ConsumeWork(ulong units = 1)
    {
        EnsureCallable();
        var required = new BigInteger(_work) + units;
        if (required > WorkLimit)
        {
            throw new SemanticRepeatResourceLimitException(
                SemanticResourceDimension.Work, WorkLimit, required);
        }

        _work = checked((ulong)required);
        PeakWork = Math.Max(PeakWork, _work);
    }

    internal StateLease ReserveState(ulong bytes)
        => ReserveState(new BigInteger(bytes));

    internal StateLease ReserveState(BigInteger bytes)
    {
        EnsureCallable();
        ArgumentOutOfRangeException.ThrowIfLessThan(bytes, BigInteger.Zero,
            nameof(bytes));
        AddState(bytes);
        return new StateLease(this, bytes);
    }

    internal StateLease ReservePersistentState(BigInteger bytes)
    {
        EnsureCallable();
        ArgumentOutOfRangeException.ThrowIfLessThan(bytes, BigInteger.Zero,
            nameof(bytes));
        AddState(bytes);
        var lease = new StateLease(this, bytes, persistent: true);
        _ = _persistentStateLeases.Add(lease);
        return lease;
    }

    private void AddState(BigInteger bytes)
    {
        EnsureCallable();
        var required = new BigInteger(_liveStateBytes) + bytes;
        if (required > LiveStateByteLimit)
        {
            throw new SemanticRepeatResourceLimitException(
                SemanticResourceDimension.State, LiveStateByteLimit,
                required);
        }
        _liveStateBytes = checked((ulong)required);
        PeakLiveStateBytes = Math.Max(PeakLiveStateBytes, _liveStateBytes);
    }

    internal FrameLease EnterFrame()
    {
        EnsureCallable();
        var requiredWork = new BigInteger(_work) + RepeatFrameWork;
        if (requiredWork > WorkLimit)
        {
            throw new SemanticRepeatResourceLimitException(
                SemanticResourceDimension.Work, WorkLimit, requiredWork);
        }

        var requiredDepth = new BigInteger(_depth) + 1;
        if (requiredDepth > DepthLimit)
        {
            throw new SemanticRepeatResourceLimitException(
                SemanticResourceDimension.Depth, DepthLimit, requiredDepth);
        }

        var requiredState = new BigInteger(_liveStateBytes)
            + RepeatFrameBytes;
        if (requiredState > LiveStateByteLimit)
        {
            throw new SemanticRepeatResourceLimitException(
                SemanticResourceDimension.State, LiveStateByteLimit,
                requiredState);
        }

        _work = checked((ulong)requiredWork);
        _depth = checked((ulong)requiredDepth);
        _liveStateBytes = checked((ulong)requiredState);
        PeakWork = Math.Max(PeakWork, _work);
        PeakDepth = Math.Max(PeakDepth, _depth);
        PeakLiveStateBytes = Math.Max(PeakLiveStateBytes, _liveStateBytes);
        return new FrameLease(this);
    }

    internal StateTreeLease BeginStateTreeReservation()
    {
        EnsureCallable();
        _stateTreeReservationDepth = checked(_stateTreeReservationDepth + 1);
        return new StateTreeLease(this);
    }

    private void EnsureCallable()
    {
        if (!_callableActive)
        {
            throw new InvalidOperationException(
                "Repeat resource work requires an active callable scope.");
        }
    }

    private void ExitCallable()
    {
        EnsureCallable();
        while (_persistentStateLeases.Count != 0)
        {
            foreach (var lease in _persistentStateLeases)
            {
                lease.Dispose();
                break;
            }
        }
        if (_liveStateBytes != 0 || _depth != 0
            || _stateTreeReservationDepth != 0)
        {
            throw new InvalidOperationException(
                "A Repeat resource callable exited with active frames.");
        }
        _callableActive = false;
    }

    private void ReleaseState(BigInteger bytes)
    {
        EnsureCallable();
        if (bytes > _liveStateBytes)
        {
            throw new InvalidOperationException(
                "Repeat resource state accounting underflowed.");
        }
        _liveStateBytes -= checked((ulong)bytes);
    }

    private void ReleasePersistentState(StateLease lease,
        BigInteger bytes)
    {
        if (!_persistentStateLeases.Remove(lease))
        {
            throw new InvalidOperationException(
                "The Repeat resource ledger lost a persistent state lease.");
        }
        ReleaseState(bytes);
    }

    private void ExitFrame(bool completed)
    {
        if (_depth == 0 || RepeatFrameBytes > _liveStateBytes)
        {
            throw new InvalidOperationException(
                "The Repeat resource ledger has no active frame.");
        }

        if (completed)
        {
            ConsumeWork(RepeatFrameExitWork);
        }

        _depth--;
        _liveStateBytes -= RepeatFrameBytes;
    }

    private void ExitStateTreeReservation()
    {
        if (_stateTreeReservationDepth == 0)
        {
            throw new InvalidOperationException(
                "The Repeat resource ledger has no active state-tree reservation.");
        }
        _stateTreeReservationDepth--;
    }

    internal sealed class CallableLease : IDisposable
    {
        private RepeatResourceLedger? _owner;

        internal CallableLease(RepeatResourceLedger owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            var owner = _owner;
            if (owner is null)
            {
                return;
            }
            owner.ExitCallable();
            _owner = null;
            GC.SuppressFinalize(this);
        }
    }

    internal sealed class StateLease : IDisposable
    {
        private RepeatResourceLedger? _owner;
        private BigInteger _bytes;
        private readonly bool _persistent;
        internal BigInteger ReservedBytes => _bytes;
        internal bool IsPersistent => _persistent;
        internal bool IsOwnedBy(RepeatResourceLedger owner)
            => ReferenceEquals(_owner, owner);

        internal StateLease(RepeatResourceLedger owner, BigInteger bytes,
            bool persistent = false)
        {
            _owner = owner;
            _bytes = bytes;
            _persistent = persistent;
        }

        internal void Add(BigInteger bytes)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(bytes, BigInteger.Zero,
                nameof(bytes));
            var owner = _owner ?? throw new ObjectDisposedException(
                nameof(StateLease));
            owner.AddState(bytes);
            _bytes += bytes;
        }

        internal void Remove(BigInteger bytes)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(bytes,
                BigInteger.Zero, nameof(bytes));
            var owner = _owner ?? throw new ObjectDisposedException(
                nameof(StateLease));
            if (bytes > _bytes)
            {
                throw new InvalidOperationException(
                    "Repeat resource state reservation underflowed.");
            }

            owner.ReleaseState(bytes);
            _bytes -= bytes;
        }

        internal void TransferFrom(StateLease source, BigInteger bytes,
            BigInteger additionalBytes)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentOutOfRangeException.ThrowIfLessThan(bytes,
                BigInteger.Zero, nameof(bytes));
            ArgumentOutOfRangeException.ThrowIfLessThan(additionalBytes,
                BigInteger.Zero, nameof(additionalBytes));
            var owner = _owner ?? throw new ObjectDisposedException(
                nameof(StateLease));
            if (!ReferenceEquals(owner, source._owner)
                || ReferenceEquals(this, source)
                || source._persistent || bytes > source._bytes)
            {
                throw new InvalidOperationException(
                    "Repeat resource state reservations cannot be transferred.");
            }

            owner.AddState(additionalBytes);
            source._bytes -= bytes;
            _bytes += bytes + additionalBytes;
        }

        public void Dispose()
        {
            var owner = _owner;
            _owner = null;
            if (owner is not null)
            {
                if (_persistent)
                {
                    owner.ReleasePersistentState(this, _bytes);
                }
                else
                {
                    owner.ReleaseState(_bytes);
                }
            }
            GC.SuppressFinalize(this);
        }
    }

    internal sealed class FrameLease : IDisposable
    {
        private RepeatResourceLedger? _owner;

        internal FrameLease(RepeatResourceLedger owner)
        {
            _owner = owner;
        }

        internal void Complete()
        {
            var owner = _owner ?? throw new ObjectDisposedException(
                nameof(FrameLease));
            owner.ExitFrame(completed: true);
            _owner = null;
        }

        public void Dispose()
        {
            var owner = _owner;
            _owner = null;
            owner?.ExitFrame(completed: false);
            GC.SuppressFinalize(this);
        }
    }

    internal sealed class StateTreeLease : IDisposable
    {
        private RepeatResourceLedger? _owner;

        internal StateTreeLease(RepeatResourceLedger owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            var owner = _owner;
            _owner = null;
            owner?.ExitStateTreeReservation();
            GC.SuppressFinalize(this);
        }
    }
}
