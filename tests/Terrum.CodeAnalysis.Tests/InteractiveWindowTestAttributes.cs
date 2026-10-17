// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;

namespace Terrum.CodeAnalysis.Tests;

internal sealed class InteractiveWindowTestAttribute : ConditionalSkipTestAttribute
{
    protected override string? GetSkipReason() => InteractiveWindowIntegration.GetSkipReason();
}

internal static class InteractiveWindowIntegration
{
    internal static string? GetSkipReason()
    {
        if (Environment.GetEnvironmentVariable("TERRUM_RUN_INTERACTIVE_WINDOW_TESTS") != "1")
        {
            return "Not exercised: opt in with TERRUM_RUN_INTERACTIVE_WINDOW_TESTS=1.";
        }
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            return "Not exercised: requires Windows x64.";
        }
        if (!Environment.UserInteractive)
        {
            return "Not exercised: no interactive desktop.";
        }

        var desktop = OpenInputDesktop(0, false, 1); // DESKTOP_READOBJECTS
        if (desktop == IntPtr.Zero)
        {
            return $"Not exercised: input desktop unavailable (Win32 {Marshal.GetLastPInvokeError()}).";
        }
        if (!CloseDesktop(desktop))
        {
            return $"Not exercised: could not close input desktop handle (Win32 {Marshal.GetLastPInvokeError()}).";
        }
        return null;
    }

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr OpenInputDesktop(uint flags,
        [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);
}
