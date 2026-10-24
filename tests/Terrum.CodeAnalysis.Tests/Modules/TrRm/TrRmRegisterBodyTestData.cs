// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

internal static class TrRmRegisterBodyTestData
{
    internal static byte[] MinimalReturnBody()
        =>
        [
            0x01, 0x01, 0, 0, 0,
            1, 0, 0, 0,
            1, 0, 0, 0,
            1, 0, 0, 0,
            0, 0, 0, 0,
            1, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0,
            8, 0, 0, 0,
            1, 0, 0, 0,
            0, 0, 0, 0,
            0,
        ];
}
