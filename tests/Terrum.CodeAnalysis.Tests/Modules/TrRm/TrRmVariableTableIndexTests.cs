// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmVariableTableIndexTests
{
    [Test]
    public void A3VariableIndexConstructorAndRowSelectionReserveExactWork()
    {
        byte[] section =
        [
            0, 0, 0, 0,
            1, 0, 0, 0,
            2, 0, 0, 0,
            0x11, 0x22,
        ];

        TrRmA3ResourceLedgerTestHelper.AssertExactWork(ledger =>
        {
            var index = new TrRmVariableTableIndex(section, 2, 1, 1,
                ledger);
            Assert.That(index.GetRow(2)[0], Is.EqualTo((byte)0x22));
        });
    }
}
