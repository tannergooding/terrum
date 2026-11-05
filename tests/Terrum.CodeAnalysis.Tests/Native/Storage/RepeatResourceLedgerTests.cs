using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using NUnit.Framework;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Tests.Native.Storage;

[TestFixture]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class RepeatResourceLedgerTests
{
    [Test]
    public void FramesReserveWorkDepthAndStateAtomically()
    {
        var exactLedger = new RepeatResourceLedger();
        using (exactLedger.BeginCallable())
        {
            exactLedger.ConsumeWork(RepeatResourceLedger.WorkLimit - 7);
            using (exactLedger.EnterFrame())
            {
                Assert.That(exactLedger.WorkUsed,
                    Is.EqualTo(RepeatResourceLedger.WorkLimit));
                Assert.That(exactLedger.ActiveDepth, Is.EqualTo(1));
                Assert.That(exactLedger.LiveStateBytes, Is.EqualTo(48));
            }
            Assert.That(exactLedger.ActiveDepth, Is.Zero);
            Assert.That(exactLedger.LiveStateBytes, Is.Zero);
        }

        var shortLedger = new RepeatResourceLedger();
        using (shortLedger.BeginCallable())
        {
            shortLedger.ConsumeWork(RepeatResourceLedger.WorkLimit - 6);
            var exception = Assert.Throws<SemanticRepeatResourceLimitException>(
                () => shortLedger.EnterFrame());
            Assert.That(exception!.Dimension,
                Is.EqualTo(SemanticResourceDimension.Work));
            Assert.That(exception.Required,
                Is.EqualTo((BigInteger)RepeatResourceLedger.WorkLimit + 1));
            Assert.That(shortLedger.ActiveDepth, Is.Zero);
            Assert.That(shortLedger.LiveStateBytes, Is.Zero);
        }

        var stateLedger = new RepeatResourceLedger();
        using (stateLedger.BeginCallable())
        using (stateLedger.ReserveState(
            RepeatResourceLedger.LiveStateByteLimit))
        {
            var exception = Assert.Throws<SemanticRepeatResourceLimitException>(
                () => stateLedger.EnterFrame());
            Assert.That(exception!.Dimension,
                Is.EqualTo(SemanticResourceDimension.State));
            Assert.That(exception.Required,
                Is.EqualTo((BigInteger)RepeatResourceLedger.LiveStateByteLimit + 48));
            Assert.That(stateLedger.WorkUsed, Is.Zero);
            Assert.That(stateLedger.ActiveDepth, Is.Zero);
            Assert.That(stateLedger.LiveStateBytes,
                Is.EqualTo(RepeatResourceLedger.LiveStateByteLimit));
            var oneOver = Assert.Throws<SemanticRepeatResourceLimitException>(
                () => stateLedger.ReserveState(1));
            Assert.That(oneOver!.Required,
                Is.EqualTo((BigInteger)RepeatResourceLedger.LiveStateByteLimit + 1));
        }

        var nonwrappingLedger = new RepeatResourceLedger();
        using (nonwrappingLedger.BeginCallable())
        {
            var required = (BigInteger)ulong.MaxValue + 1;
            var exception = Assert.Throws<SemanticRepeatResourceLimitException>(
                () => nonwrappingLedger.ReserveState(required));
            Assert.That(exception!.Dimension,
                Is.EqualTo(SemanticResourceDimension.State));
            Assert.That(exception.Required, Is.EqualTo(required));
            Assert.That(nonwrappingLedger.LiveStateBytes, Is.Zero);
        }

        var depthLedger = new RepeatResourceLedger();
        using (depthLedger.BeginCallable())
        {
            var frames = new List<RepeatResourceLedger.FrameLease>();
            try
            {
                for (var depth = 0UL;
                    depth < RepeatResourceLedger.DepthLimit;
                    depth++)
                {
                    frames.Add(depthLedger.EnterFrame());
                }

                using var state = depthLedger.ReserveState(
                    RepeatResourceLedger.LiveStateByteLimit
                    - depthLedger.LiveStateBytes);
                var exception = Assert.Throws<SemanticRepeatResourceLimitException>(
                    () => depthLedger.EnterFrame());
                Assert.That(exception!.Dimension,
                    Is.EqualTo(SemanticResourceDimension.Depth));
                Assert.That(depthLedger.WorkUsed,
                    Is.EqualTo(RepeatResourceLedger.DepthLimit * 7));
                Assert.That(depthLedger.ActiveDepth,
                    Is.EqualTo(RepeatResourceLedger.DepthLimit));
                Assert.That(depthLedger.LiveStateBytes,
                    Is.EqualTo(RepeatResourceLedger.LiveStateByteLimit));
            }
            finally
            {
                for (var index = frames.Count - 1; index >= 0; index--)
                {
                    frames[index].Dispose();
                }
            }
        }

        var priorityLedger = new RepeatResourceLedger();
        using (priorityLedger.BeginCallable())
        {
            var frames = new List<RepeatResourceLedger.FrameLease>();
            try
            {
                for (var depth = 0UL;
                    depth < RepeatResourceLedger.DepthLimit;
                    depth++)
                {
                    frames.Add(priorityLedger.EnterFrame());
                }

                using var state = priorityLedger.ReserveState(
                    RepeatResourceLedger.LiveStateByteLimit
                    - priorityLedger.LiveStateBytes);
                priorityLedger.ConsumeWork(
                    RepeatResourceLedger.WorkLimit
                    - priorityLedger.WorkUsed - 6);
                var exception = Assert.Throws<SemanticRepeatResourceLimitException>(
                    () => priorityLedger.EnterFrame());
                Assert.That(exception!.Dimension,
                    Is.EqualTo(SemanticResourceDimension.Work));
                Assert.That(priorityLedger.ActiveDepth,
                    Is.EqualTo(RepeatResourceLedger.DepthLimit));
                Assert.That(priorityLedger.LiveStateBytes,
                    Is.EqualTo(RepeatResourceLedger.LiveStateByteLimit));
            }
            finally
            {
                for (var index = frames.Count - 1; index >= 0; index--)
                {
                    frames[index].Dispose();
                }
            }
        }
    }

    [Test]
    public void StateLeaseAddsLogicalOutputBeforeAppend()
    {
        var ledger = new RepeatResourceLedger();
        using (ledger.BeginCallable())
        {
            using var output = ledger.ReserveState(
                RepeatResourceLedger.LiveStateByteLimit - 1);
            output.Add(1);
            Assert.That(ledger.LiveStateBytes,
                Is.EqualTo(RepeatResourceLedger.LiveStateByteLimit));

            var exception = Assert.Throws<SemanticRepeatResourceLimitException>(
                () => output.Add(1));
            Assert.That(exception!.Dimension,
                Is.EqualTo(SemanticResourceDimension.State));
            Assert.That(exception.Required,
                Is.EqualTo((BigInteger)RepeatResourceLedger.LiveStateByteLimit + 1));
            Assert.That(ledger.LiveStateBytes,
                Is.EqualTo(RepeatResourceLedger.LiveStateByteLimit));
        }

        Assert.That(ledger.LiveStateBytes, Is.Zero);
    }

    [Test]
    public void StateLeaseTransfersScratchToOutputAtomically()
    {
        var ledger = new RepeatResourceLedger();
        using (ledger.BeginCallable())
        {
            using var scratch = ledger.ReserveState(8);
            using var output = ledger.ReserveState(BigInteger.Zero);
            output.TransferFrom(scratch, 8, 48);
            Assert.That(ledger.LiveStateBytes, Is.EqualTo(56));
            scratch.Dispose();
            Assert.That(ledger.LiveStateBytes, Is.EqualTo(56));
        }

        Assert.That(ledger.LiveStateBytes, Is.Zero);

        var fullLedger = new RepeatResourceLedger();
        using (fullLedger.BeginCallable())
        {
            using var baseline = fullLedger.ReserveState(
                RepeatResourceLedger.LiveStateByteLimit - 8);
            using var scratch = fullLedger.ReserveState(8);
            using var output = fullLedger.ReserveState(BigInteger.Zero);
            var exception = Assert.Throws<SemanticRepeatResourceLimitException>(
                () => output.TransferFrom(scratch, 8, 1));
            Assert.That(exception!.Dimension,
                Is.EqualTo(SemanticResourceDimension.State));
            Assert.That(exception.Required,
                Is.EqualTo((BigInteger)RepeatResourceLedger.LiveStateByteLimit + 1));
            Assert.That(ledger.LiveStateBytes, Is.Zero);
            Assert.That(fullLedger.LiveStateBytes,
                Is.EqualTo(RepeatResourceLedger.LiveStateByteLimit));
        }

        Assert.That(fullLedger.LiveStateBytes, Is.Zero);
    }

    [Test]
    public void StateTreeReservationsRequireAnActiveCallable()
    {
        var ledger = new RepeatResourceLedger();

        _ = Assert.Throws<InvalidOperationException>(
            () => ledger.BeginStateTreeReservation());
        Assert.That(ledger.IsStateTreeReservationActive, Is.False);
    }

    [Test]
    public void CallableCannotExitWithLiveStateReservations()
    {
        var ledger = new RepeatResourceLedger();
        var callable = ledger.BeginCallable();
        var state = ledger.ReserveState(1);

        _ = Assert.Throws<InvalidOperationException>(() => callable.Dispose());
        Assert.That(ledger.LiveStateBytes, Is.EqualTo(1));

        state.Dispose();
        callable.Dispose();
        Assert.That(ledger.LiveStateBytes, Is.Zero);
        Assert.That(ledger.ActiveDepth, Is.Zero);
    }

    [Test]
    public void PersistentStateLeaseLivesUntilClearedOrCallableExit()
    {
        var ledger = new RepeatResourceLedger();
        using (ledger.BeginCallable())
        {
            var cleared = ledger.ReservePersistentState(48);
            Assert.That(ledger.LiveStateBytes, Is.EqualTo(48));
            cleared.Dispose();
            Assert.That(ledger.LiveStateBytes, Is.Zero);

            _ = ledger.ReservePersistentState(56);
            Assert.That(ledger.LiveStateBytes, Is.EqualTo(56));
        }

        Assert.That(ledger.LiveStateBytes, Is.Zero);
    }

    [Test]
    public void PersistentStateLeaseCanReceiveAndReleaseOwnedBytes()
    {
        var ledger = new RepeatResourceLedger();
        using (ledger.BeginCallable())
        {
            using var scratch = ledger.ReserveState(8);
            using var output = ledger.ReservePersistentState(16);
            output.TransferFrom(scratch, 8, 32);
            Assert.That(ledger.LiveStateBytes, Is.EqualTo(56));
            Assert.That(output.ReservedBytes,
                Is.EqualTo(new BigInteger(56)));

            scratch.Dispose();
            output.Remove(8);
            Assert.That(ledger.LiveStateBytes, Is.EqualTo(48));
            Assert.That(output.ReservedBytes,
                Is.EqualTo(new BigInteger(48)));
        }

        Assert.That(ledger.LiveStateBytes, Is.Zero);
    }
}
