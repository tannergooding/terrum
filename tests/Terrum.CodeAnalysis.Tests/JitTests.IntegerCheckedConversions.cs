// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [TestCase((ushort)0)]
    [TestCase((ushort)1)]
    [TestCase((ushort)0xD7FF)]
    [TestCase((ushort)0xD800)]
    [TestCase((ushort)0xDFFF)]
    [TestCase((ushort)0xE000)]
    [TestCase(ushort.MaxValue)]
    public async Task CheckedChar16WideningsAlwaysPublishCodeUnitAcrossAotAndJitAsync(ushort codeUnit)
    {
        var component = await File.ReadAllTextAsync(
            Path.Combine(RepositoryRoot(), "self-host", "IntegerCheckedConversions.terrum"))
            .ConfigureAwait(true);
        var source = component + $$"""

            int32 Main() {
                require TryConvertChar16ToInt32(char16({{codeUnit}}), init var signed32);
                require TryConvertChar16ToUInt32(char16({{codeUnit}}), init var unsigned32);
                require TryConvertChar16ToInt64(char16({{codeUnit}}), init var signed64);
                require TryConvertChar16ToUInt64(char16({{codeUnit}}), init var unsigned64);
                if (signed32 != {{codeUnit}} || unsigned32 != {{codeUnit}}
                    || signed64 != {{codeUnit}} || unsigned64 != {{codeUnit}}) { return 1; }
                return 42;
            }
            """;
        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }
}
