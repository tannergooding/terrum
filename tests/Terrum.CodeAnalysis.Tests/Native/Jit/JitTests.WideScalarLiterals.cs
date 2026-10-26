// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

public sealed partial class JitTests
{
    [Test]
    public async Task WideScalarLiteralConstantsExecuteAcrossAotAndJitAsync()
    {
        const string source = """
            int128 Signed128() { return int128(-170141183460469231731687303715884105728); }
            uint128 Unsigned128() { return uint128(0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF); }
            float16 Half() { return float16(0x1p-24); }
            bfloat16 BFloat() { return bfloat16(0x1p-133); }
            float128 Quad() { return float128(1.5); }
            decimal32 Decimal32Value() { return decimal32(1.20); }
            decimal64 Decimal64Value() { return decimal64(1.20); }
            decimal128 Decimal128Value() { return decimal128(1.20); }
            [ConstEval] int128 Constant128() { return int128(-170141183460469231731687303715884105728); }
            [ConstEval] float16 ConstantHalf() { return float16(1.5); }
            [ConstEval] decimal128 ConstantDecimal() { return decimal128(1.20); }
            int32 Main() {
                const int128 signed = Constant128();
                const float16 half = ConstantHalf();
                const decimal128 decimal = ConstantDecimal();
                _ = signed;
                _ = half;
                _ = decimal;
                _ = Signed128();
                _ = Unsigned128();
                _ = Half();
                _ = BFloat();
                _ = Quad();
                _ = Decimal32Value();
                _ = Decimal64Value();
                _ = Decimal128Value();
                return 42;
            }
            """;

        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }
}
