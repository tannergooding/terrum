// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

namespace Terrum.CodeAnalysis.Modules;

public static partial class NativeModuleCompiler
{
    // Private managed-node allocation uses the same bounded KERNEL32 heap import set as owned buffers.
    private static bool AllowsOwnedBufferRuntime(string profile) =>
        profile is "native-15" or "native-16" or "native-17" or "native-18";

    private static bool AllowsManagedRuntimeData(string profile) => profile == "native-18";
}
