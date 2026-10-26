// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

public sealed partial class JitTests
{
    [Test]
    public async Task FloatingNarrowSignsAndComparisonsExecuteAcrossAotAndJitAsync()
    {
        const string source = """
            float16 Negate16(float16 value) { return -value; }
            float16 Add16(float16 left, float16 right) { return left + right; }
            float16 Subtract16(float16 left, float16 right) { return left - right; }
            float16 Multiply16(float16 left, float16 right) { return left * right; }
            float16 Divide16(float16 left, float16 right) { return left / right; }
            float16 Remainder16(float16 left, float16 right) { return left % right; }
            bool Equal16(float16 left, float16 right) { return left == right; }
            bool NotEqual16(float16 left, float16 right) { return left != right; }
            bool Less16(float16 left, float16 right) { return left < right; }
            bool Greater16(float16 left, float16 right) { return left > right; }
            bfloat16 NegateBFloat(bfloat16 value) { return -value; }
            bfloat16 AddBFloat(bfloat16 left, bfloat16 right) { return left + right; }
            bfloat16 SubtractBFloat(bfloat16 left, bfloat16 right) { return left - right; }
            bfloat16 MultiplyBFloat(bfloat16 left, bfloat16 right) { return left * right; }
            bfloat16 DivideBFloat(bfloat16 left, bfloat16 right) { return left / right; }
            bfloat16 RemainderBFloat(bfloat16 left, bfloat16 right) { return left % right; }
            bool EqualBFloat(bfloat16 left, bfloat16 right) { return left == right; }
            bool NotEqualBFloat(bfloat16 left, bfloat16 right) { return left != right; }
            bool LessBFloat(bfloat16 left, bfloat16 right) { return left < right; }
            bool GreaterBFloat(bfloat16 left, bfloat16 right) { return left > right; }
            int32 Main() {
                float16 nan16 = float16(0) / float16(0);
                bfloat16 nanBFloat = bfloat16(0) / bfloat16(0);
                float16 infinity16 = float16(1) / float16(0);
                float16 minimumSubnormal16 = float16(5.960464477539063e-8);
                float16 threeMinimumSubnormals16 = float16(1.78813934326171875e-7);
                mut float16 update16 = float16(2);
                float16 previous16 = update16++;
                float16 prefix16 = --update16;
                update16 += float16(1.5);
                update16 -= float16(0.5);
                update16 *= float16(2);
                update16 /= float16(2);
                update16 %= float16(2);
                float16 negativeZero16 = Negate16(float16(0));
                bfloat16 negativeZeroBFloat = NegateBFloat(bfloat16(0));
                mut bfloat16 updateBFloat = bfloat16(4);
                bfloat16 previousBFloat = updateBFloat--;
                bfloat16 prefixBFloat = ++updateBFloat;
                updateBFloat += bfloat16(2);
                updateBFloat -= bfloat16(1);
                updateBFloat *= bfloat16(2);
                updateBFloat /= bfloat16(2);
                updateBFloat %= bfloat16(3);
                if (Negate16(float16(1.5)) != float16(-1.5)) { return 1; }
                if (Add16(float16(2048), float16(1)) != float16(2048)) { return 2; }
                if (Add16(float16(1.5), float16(1.5)) != float16(3)) { return 20; }
                if (Multiply16(float16(1.5), float16(2)) != float16(3)) { return 22; }
                if (Multiply16(float16(0.5), float16(0.5)) != float16(0.25)) { return 23; }
                if (Add16(minimumSubnormal16, minimumSubnormal16)
                    != float16(1.1920928955078125e-7)) { return 3; }
                if (Multiply16(minimumSubnormal16, float16(0.5)) != float16(0)) { return 24; }
                if (Divide16(float16(1), float16(3)) != float16(0.333251953125)) { return 26; }
                if (Divide16(minimumSubnormal16, float16(2)) != float16(0)) { return 27; }
                if (Divide16(threeMinimumSubnormals16, float16(2))
                    != float16(1.1920928955078125e-7)) { return 28; }
                if (Divide16(float16(65504), float16(0.5)) != infinity16) { return 29; }
                if (Divide16(float16(0), float16(0)) == Divide16(float16(0), float16(0))) { return 30; }
                if (previous16 != float16(2) || prefix16 != float16(2)
                    || update16 != float16(1)) { return 43; }
                if (Remainder16(float16(5.5), float16(2)) != float16(1.5)) { return 33; }
                if (Remainder16(float16(-5.5), float16(2)) != float16(-1.5)) { return 34; }
                if (Remainder16(float16(1), float16(3)) != float16(1)) { return 35; }
                if (Remainder16(float16(65504), minimumSubnormal16) != float16(0)) { return 36; }
                if (Remainder16(float16(5.5), infinity16) != float16(5.5)) { return 37; }
                if (Remainder16(infinity16, float16(2))
                    == Remainder16(infinity16, float16(2))) { return 38; }
                if (Remainder16(float16(1), float16(0))
                    == Remainder16(float16(1), float16(0))) { return 39; }
                if (Divide16(float16(1), Remainder16(float16(-0.0), float16(2)))
                    != -infinity16) { return 40; }
                if (Subtract16(float16(1.5), float16(0.5)) != float16(1)) { return 4; }
                if (Add16(float16(65504), float16(65504)) != infinity16) { return 5; }
                if (Add16(infinity16, -infinity16) == Add16(infinity16, -infinity16)) { return 6; }
                if (!Equal16(negativeZero16, float16(0))) { return 7; }
                if (!Less16(Negate16(float16(2)), Negate16(float16(1)))) { return 8; }
                if (!Greater16(float16(2), float16(1))) { return 9; }
                if (Equal16(nan16, nan16)) { return 10; }
                if (!NotEqual16(nan16, nan16)) { return 11; }
                if (NegateBFloat(bfloat16(1.5)) != bfloat16(-1.5)) { return 12; }
                if (AddBFloat(bfloat16(256), bfloat16(1)) != bfloat16(256)) { return 13; }
                if (AddBFloat(bfloat16(1.5), bfloat16(1.5)) != bfloat16(3)) { return 21; }
                if (MultiplyBFloat(bfloat16(1.5), bfloat16(2)) != bfloat16(3)) { return 25; }
                if (SubtractBFloat(bfloat16(1.5), bfloat16(0.5)) != bfloat16(1)) { return 14; }
                if (DivideBFloat(bfloat16(1), bfloat16(3)) != bfloat16(0.333984375)) { return 31; }
                if (DivideBFloat(bfloat16(0), bfloat16(0))
                    == DivideBFloat(bfloat16(0), bfloat16(0))) { return 32; }
                if (RemainderBFloat(bfloat16(5.5), bfloat16(2)) != bfloat16(1.5)) { return 41; }
                if (RemainderBFloat(bfloat16(-5.5), bfloat16(2)) != bfloat16(-1.5)) { return 42; }
                if (previousBFloat != bfloat16(4) || prefixBFloat != bfloat16(4)
                    || updateBFloat != bfloat16(2)) { return 44; }
                if (!EqualBFloat(negativeZeroBFloat, bfloat16(0))) { return 15; }
                if (!LessBFloat(NegateBFloat(bfloat16(2)), NegateBFloat(bfloat16(1)))) { return 16; }
                if (!GreaterBFloat(bfloat16(2), bfloat16(1))) { return 17; }
                if (EqualBFloat(nanBFloat, nanBFloat)) { return 18; }
                if (!NotEqualBFloat(nanBFloat, nanBFloat)) { return 19; }
                return 42;
            }
            """;

        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }
}
