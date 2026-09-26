// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

namespace Terrum.CodeAnalysis.Modules;

public static partial class NativeModuleCompiler
{
    private static bool AllowsOwnedBufferRuntime(string profile) =>
        profile is "native-15" or "native-16";
}
