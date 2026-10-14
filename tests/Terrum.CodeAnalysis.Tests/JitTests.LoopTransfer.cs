// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [TestCase(false)]
    [TestCase(true)]
    public Task PostTestedAndNamedTransfersKeepConditionAndIteratorOrderAsync(bool checkedExceptions)
    {
        const string Source = """
            int32 Main() {
                mut int32 result = 0;
                outer: for (mut int32 i = 0; i < 5; i = i + 1) {
                    do {
                        if (i == 0) { continue outer; }
                        if (i == 3) { break outer; }
                        result = result + 20;
                        break;
                    } while (true);
                }
                return result + 2;
            }
            """;
        return checkedExceptions ? AssertCheckedExecutionAsync(Source, 42) : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task ContinueInPostTestedLoopChecksConditionAndUnwindsCleanupAsync(bool checkedExceptions)
    {
        const string Source = """
            int32 Main() {
                mut int32 value = 0;
                outer: do {
                    try { value = value + 1; continue outer; }
                    finally {
                        if (value == 1) { value = 11; }
                        else if (value == 12) { value = 22; }
                        else if (value == 23) { value = 33; }
                        else { value = 44; }
                    }
                } while (value < 42);
                return value - 2;
            }
            """;
        return checkedExceptions ? AssertCheckedExecutionAsync(Source, 42) : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task NamedOuterTransferRetiresInnerManagedRootsAsync(bool checkedExceptions)
    {
        const string Source = """
            class Leaf { public int32 Value; public Leaf(int32 value) { Value = value; } }
            int32 Main() {
                outer: for (mut int32 i = 0; i < 3; i = i + 1) {
                    do {
                        require Leaf.TryCreate(i, out var leaf);
                        if (i == 0) { continue outer; }
                        if (leaf.Value == 1) { break outer; }
                    } while (false);
                }
                if (ManagedHeap.Collect() != 2) { return 1; }
                return 42;
            }
            """;
        return checkedExceptions ? AssertCheckedExecutionAsync(Source, 42) : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task NamedBreakUnwindsNestedFinallyBeforeOuterExitAsync(bool checkedExceptions)
    {
        const string Source = """
            int32 Main() {
                mut int32 result = 0;
                outer: while (true) {
                    do {
                        try { result = 1; break outer; }
                        finally { if (result == 1) { result = 42; } }
                    } while (true);
                }
                return result;
            }
            """;
        return checkedExceptions ? AssertCheckedExecutionAsync(Source, 42) : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task ForContinueRunsIteratorAndBreakSkipsItAsync(bool checkedExceptions)
    {
        const string Source = """
            int32 Main() {
                mut int32 value = 0;
                for (mut int32 i = 0; i < 5; i = i + 1) {
                    if (i == 2) { continue; }
                    if (i == 4) { break; }
                    value = value * 10 + i;
                }
                return value + 29;
            }
            """;
        return checkedExceptions ? AssertCheckedExecutionAsync(Source, 42) : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task IteratorSideEffectsDistinguishContinueFromBreakAsync(bool checkedExceptions)
    {
        const string Source = """
            int32 Main() {
                mut int32 steps = 0;
                for (mut int32 i = 0; i < 5; steps = steps + 1) {
                    if (i == 2) { i = i + 1; continue; }
                    if (i == 4) { break; }
                    i = i + 1;
                }
                if (steps != 4) { return 1; }
                return 42;
            }
            """;
        return checkedExceptions ? AssertCheckedExecutionAsync(Source, 42) : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task NestedLoopsTransferOnlyToNearestTargetAsync(bool checkedExceptions)
    {
        const string Source = """
            int32 Main() {
                mut int32 count = 0;
                for (mut int32 outer = 0; outer < 3; outer = outer + 1) {
                    for (mut int32 inner = 0; inner < 4; inner = inner + 1) {
                        if (inner == 1) { continue; }
                        if (inner == 2) { break; }
                        count = count + 1;
                    }
                    if (outer == 1) { continue; }
                }
                return count + 39;
            }
            """;
        return checkedExceptions ? AssertCheckedExecutionAsync(Source, 42) : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task BreakCarriesConditionalOutputFactsToLoopExitAsync(bool checkedExceptions)
    {
        const string Source = """
            bool Take(out int32 value) { value = 42; return true; }
            int32 Main() {
                mut int32 value = 0;
                while (true) {
                    if (Take(out value)) { break; }
                }
                return value;
            }
            """;
        return checkedExceptions ? AssertCheckedExecutionAsync(Source, 42) : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task NestedFinallyOnLoopExitRunsOnceInsideOutAsync(bool checkedExceptions)
    {
        const string Source = """
            [NoThrow] void Touch() { }
            int32 Main() {
                mut int32 value = 0;
                while (true) {
                    try {
                        try { value = 1; break; }
                        finally { if (value == 1) { value = 12; } Touch(); }
                    }
                    finally { if (value == 12) { value = 123; } }
                }
                if (value != 123) { return 1; }
                return 42;
            }
            """;
        return checkedExceptions ? AssertCheckedExecutionAsync(Source, 42) : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task ContinueRunsFinallyBeforeForIteratorAsync(bool checkedExceptions)
    {
        const string Source = """
            int32 Main() {
                mut int32 value = 0;
                for (mut int32 i = 0; i < 2; i = i + 1) {
                    try {
                        if (i == 0) { value = 1; continue; }
                        if (value != 12) { return 1; }
                        value = 20;
                        continue;
                    }
                    finally {
                        if (value == 1) { value = 12; }
                        else { if (value == 20) { value = 42; } }
                    }
                }
                return value;
            }
            """;
        return checkedExceptions ? AssertCheckedExecutionAsync(Source, 42) : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task LoopLocalRootsRetireAcrossContinueAndBreakAsync(bool checkedExceptions)
    {
        const string Source = """
            class Leaf { public int32 Value; public Leaf(int32 value) { Value = value; } }
            int32 Main() {
                mut int32 sum = 0;
                for (mut int32 i = 0; i < 3; i = i + 1) {
                    require Leaf.TryCreate(i, out var leaf);
                    if (i == 1) { continue; }
                    if (i == 2) { break; }
                    sum = sum + leaf.Value;
                }
                if (sum != 0 || ManagedHeap.Collect() != 3) { return 1; }
                return 42;
            }
            """;
        return checkedExceptions ? AssertCheckedExecutionAsync(Source, 42) : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task LoopWithinFinallyCanExitLocallyAsync(bool checkedExceptions)
    {
        const string Source = """
            int32 Main() {
                mut int32 result = 0;
                try { result = 40; }
                finally { while (true) { break; } result = 42; }
                return result;
            }
            """;
        return checkedExceptions ? AssertCheckedExecutionAsync(Source, 42) : AssertFailFastExecutionAsync(Source, 42);
    }

    [Test]
    public Task CheckedCatchBreakRunsEnclosingFinallyOnceAsync()
    {
        const string Source = """
            int32 Main() {
                mut int32 value = 0;
                unsafe {
                    while (true) {
                        try { require false; }
                        catch { value = 40; break; }
                        finally { if (value == 40) { value = 42; } }
                    }
                }
                return value;
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }
}
