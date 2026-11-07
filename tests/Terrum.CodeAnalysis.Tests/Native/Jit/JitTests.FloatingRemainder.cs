// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

internal sealed partial class JitTests
{
    [Test]
    public async Task FloatingRemainderExecutesAcrossAotAndJitAsync()
    {
        const string source = """
            float32 Remainder32(float32 left, float32 right) { return left % right; }
            float32 Divide32(float32 left, float32 right) { return left / right; }
            float64 Remainder64(float64 left, float64 right) { return left % right; }
            float64 Divide64(float64 left, float64 right) { return left / right; }
            int32 Main() {
                float32 positive = Remainder32(float32(5.5), float32(2));
                float32 negative = Remainder32(float32(-5.5), float32(2));
                float32 largeQuotient = Remainder32(float32(16777216), float32(3));
                float32 negativeZero = Remainder32(float32(-0.0), float32(2));
                float32 infinity32 = Divide32(float32(1), float32(0));
                float32 nan32 = Remainder32(Divide32(float32(0), float32(0)), float32(2));
                float32 zeroDivisor32 = Remainder32(float32(1), float32(0));
                mut float32 current32 = float32(5.5);
                float32 previous32 = current32++;
                current32 %= float32(2);
                current32 += float32(1);
                float64 largeExponentGap = Remainder64(
                    float64(1.7976931348623157e308), float64(5e-324));
                float64 infinity64 = Divide64(float64(1), float64(0));
                mut float64 current64 = float64(-4);
                float64 previous64 = current64--;
                current64 /= float64(2);
                current64 *= float64(3);
                if (positive != float32(1.5) || negative != float32(-1.5)
                    || largeQuotient != float32(1) || negativeZero != float32(0)
                    || !(float32(1) / negativeZero < float32(0))
                    || Remainder32(float32(5.5), infinity32) != float32(5.5)
                    || nan32 == nan32
                    || (zeroDivisor32 == zeroDivisor32)
                    || previous32 != float32(5.5) || current32 != float32(1.5)
                    || largeExponentGap != float64(0)
                    || Remainder64(float64(5.5), infinity64) != float64(5.5)
                    || previous64 != float64(-4) || current64 != float64(-7.5)) {
                    return 0;
                }
                return 42;
            }
            """;

        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }
}
