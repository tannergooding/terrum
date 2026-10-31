// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Numerics;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

internal static class TrRmA3ResourceLedgerTestHelper
{
    internal static void AssertExactWork(
        Action<TrRmA3LayoutResourceLedger> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var measuredLedger = new TrRmA3LayoutResourceLedger();
        var workBefore = measuredLedger.WorkRemaining;
        action(measuredLedger);
        var work = workBefore - measuredLedger.WorkRemaining;
        Assert.That(work, Is.GreaterThan(0));

        var exactLedger = new TrRmA3LayoutResourceLedger();
        exactLedger.ConsumeWork(exactLedger.WorkRemaining - work);
        action(exactLedger);
        Assert.That(exactLedger.WorkRemaining, Is.Zero);

        var shortLedger = new TrRmA3LayoutResourceLedger();
        shortLedger.ConsumeWork(shortLedger.WorkRemaining - (work - 1));
        TrRmResourceLimitException? exception = null;
        try
        {
            action(shortLedger);
        }
        catch (TrRmResourceLimitException caught)
        {
            exception = caught;
        }

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.Required,
            Is.EqualTo(new BigInteger(
                TrRmA3LayoutResourceLedger.WorkLimit + 1)));
    }
}
