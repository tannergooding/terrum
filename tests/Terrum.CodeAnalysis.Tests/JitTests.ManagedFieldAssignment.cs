// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    private const string ManagedFieldAssignmentDeclarations = """
        mut class Leaf {
            public mut int32 Value;
            public Leaf(int32 value) { Value = value; }
        }
        mut class Pair {
            public mut write Leaf Left;
            public Pair(write Leaf left) { Left = left; }
        }
        write Pair Build() {
            require Leaf.TryCreate(1, out var leaf);
            require Pair.TryCreate(leaf, out var pair);
            return pair;
        }
        int32 ReplaceLeft(write Pair pair, write Leaf replacement) {
            pair.Left = replacement;
            require ManagedHeap.Collect() == 0;
            return 42;
        }
        """;
    [TestCase(false)]
    [TestCase(true)]
    public Task ManagedFieldAssignmentPreservesSelectedReceiverAcrossReplacingRhsAsync(bool checkedExceptions)
    {
        var source = ManagedFieldAssignmentDeclarations + """
            int32 Main() {
                write Pair pair = Build();
                require Leaf.TryCreate(7, out var replacement);
                pair.Left.Value = ReplaceLeft(pair, replacement);
                require pair.Left.Value == 7;
                require ManagedHeap.Collect() == 1;
                return 42;
            }
            """;
        return checkedExceptions
            ? AssertCheckedExecutionAsync(source, 42)
            : AssertFailFastExecutionAsync(source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task ManagedFieldAssignmentWritesOriginalReceiverAcrossReplacingRhsAsync(bool checkedExceptions)
    {
        var source = ManagedFieldAssignmentDeclarations + """
            int32 Main() {
                write Pair pair = Build();
                Leaf original = pair.Left;
                require Leaf.TryCreate(7, out var replacement);
                pair.Left.Value = ReplaceLeft(pair, replacement);
                require pair.Left.Value == 7;
                return original.Value;
            }
            """;
        return checkedExceptions
            ? AssertCheckedExecutionAsync(source, 42)
            : AssertFailFastExecutionAsync(source, 42);
    }

    [Test]
    public Task CheckedFieldAssignmentRetiresSelectedReceiverAfterRhsThrowsAsync()
    {
        var source = ManagedFieldAssignmentDeclarations + """
            int32 FailAfterReplace(write Pair pair, write Leaf replacement) {
                pair.Left = replacement;
                require false;
                return 42;
            }
            int32 Main() {
                write Pair pair = Build();
                require Leaf.TryCreate(7, out var replacement);
                unsafe {
                    try {
                        pair.Left.Value = FailAfterReplace(pair, replacement);
                    }
                    catch {
                        require ManagedHeap.Collect() == 1;
                        return pair.Left.Value + 35;
                    }
                }
                return 0;
            }
            """;
        return AssertCheckedExecutionAsync(source, 42);
    }
}
