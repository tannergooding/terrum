// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MutableFieldViewThroughUniqueOwnerUsesOriginalAllocationAsync(bool checkedFailures)
    {
        const string Source = """
            int32 Once(bool appended, int32 count) { return count; }
            unique struct Child {
                public mut OwnedInt32Buffer Words;
                public Child(OwnedInt32Buffer words) { Words = move words; }
            }
            unique struct Owner {
                public mut Child Nested;
                public Owner(Child child) { Nested = move child; }
                public mut void Forward() {
                    mut MutableInt32BufferView view = Nested.Words.SliceMutable(1, 1);
                    view.Set(0, 42);
                }
            }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(3, out var raw);
                mut OwnedInt32Buffer words = move raw;
                require words.TryAppend(40);
                require words.TryAppend(1);
                mut Owner owner = new Owner(new Child(move words));
                require OwnedInt32Buffer.TryCreate(2, out var extra);
                mut OwnedInt32Buffer other = move extra;
                {
                    mut MutableInt32BufferView empty = owner.Nested.Words.SliceMutable(
                        owner.Nested.Words.Count, Once(other.TryAppend(1), other.Count) - 1);
                    if (empty.Length != 0 || other.Count != 1) { return 1; }
                }
                {
                    mut MutableInt32BufferView view = owner.Nested.Words.SliceMutable(1, 1);
                    view.Set(0, view.Read(0) + 1);
                }
                owner.Forward();
                if (owner.Nested.Words.Count != 2 || owner.Nested.Words.Read(0) != 40) { return 2; }
                return owner.Nested.Words.Read(1);
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Fact]
    public Task CheckedFieldViewFailureReleasesLoanAndRetainsBufferAsync()
    {
        const string Source = """
            unique struct Owner {
                public mut OwnedInt32Buffer Words;
                public Owner(OwnedInt32Buffer words) { Words = move words; }
            }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                mut Owner owner = new Owner(move raw);
                require owner.Words.TryAppend(1);
                unsafe {
                    try {
                        mut MutableInt32BufferView view = owner.Words.SliceMutable(0, 1);
                        view.Set(0, 42);
                        view.Set(1, 99);
                    }
                    catch {
                        if (owner.Words.Count != 1) { return 0; }
                        return owner.Words.Read(0);
                    }
                }
                return 0;
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }

    [Theory]
    [InlineData("-1", "0")]
    [InlineData("0", "-1")]
    [InlineData("2", "0")]
    [InlineData("1", "1")]
    [InlineData("2147483647", "2147483647")]
    public Task CheckedFieldViewRejectsInvalidInitializedPrefixAsync(string start, string length)
    {
        var source = $$"""
            unique struct Owner {
                public mut OwnedInt32Buffer Words;
                public Owner(OwnedInt32Buffer words) { Words = move words; }
            }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(2, out var raw);
                mut Owner owner = new Owner(move raw);
                require owner.Words.TryAppend(42);
                unsafe {
                    try {
                        mut MutableInt32BufferView view = owner.Words.SliceMutable({{start}}, {{length}});
                        return 0;
                    }
                    catch {
                        if (owner.Words.Count != 1) { return 0; }
                        return owner.Words.Read(0);
                    }
                }
            }
            """;
        return AssertCheckedExecutionAsync(source, 42);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwningBufferFieldSampleAgreesBetweenAotAndJitAsync(bool checkedFailures)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "samples", "native",
            "owned-buffer.terrum")).ConfigureAwait(true);
        if (checkedFailures)
        {
            await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
        }
        else
        {
            await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task OwningBufferFieldMutationsPreserveOwnerAndForwardedThisAsync(bool checkedFailures)
    {
        const string Source = """
            unique struct Storage {
                public mut OwnedInt32Buffer Words;
                public Storage(OwnedInt32Buffer words) { Words = move words; }
                public mut bool Append(int32 value) { return Words.TryAppend(value); }
                public mut void Reset() { Words.Clear(); }
                [NoThrow] [Cleanup] public void Close() {}
            }
            unique struct Owner {
                public mut Storage Nested;
                public Owner(Storage nested) { Nested = move nested; }
                public mut bool Forward(int32 value) { return Nested.Append(value); }
                [NoThrow] [Cleanup] public void Close() {}
            }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(2, out var raw);
                mut Owner owner = new Owner(new Storage(move raw));
                require owner.Nested.Words.TryAppend(20);
                require owner.Forward(22);
                if (owner.Nested.Words.TryAppend(99)) { return 1; }
                owner.Nested.Words.Set(0, owner.Nested.Words.Read(0));
                int32 result = owner.Nested.Words.Read(0) + owner.Nested.Words.Read(1);
                owner.Nested.Reset();
                require owner.Nested.Words.TryAppend(result);
                return owner.Nested.Words.Read(0);
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Fact]
    public Task CheckedBufferFieldIndexFailurePreservesPreviouslyPublishedWritesAsync()
    {
        const string Source = """
            unique struct Owner {
                public mut OwnedInt32Buffer Words;
                public Owner(OwnedInt32Buffer words) { Words = move words; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(2, out var raw);
                mut Owner owner = new Owner(move raw);
                require owner.Words.TryAppend(41);
                unsafe {
                    try {
                        owner.Words.Set(0, 42);
                        owner.Words.Set(1, 99);
                    }
                    catch {
                        if (owner.Words.Count != 1) { return 0; }
                        return owner.Words.Read(0);
                    }
                }
                return 0;
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }
}
