// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Infrastructure;

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
        return GetDesktopSkipReason();
    }

    internal static string? GetDesktopSkipReason()
    {
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

internal sealed class HardwareIntegrationTestAttribute : ConditionalSkipTestAttribute
{
    protected override string? GetSkipReason()
    {
        if (Environment.GetEnvironmentVariable("TERRUM_RUN_DX11_HARDWARE") != "1")
        {
            return "Not exercised: opt in with TERRUM_RUN_DX11_HARDWARE=1.";
        }
        var desktopReason = InteractiveWindowIntegration.GetDesktopSkipReason();
        if (desktopReason is not null)
        {
            return desktopReason;
        }
        if (!OperatingSystem.IsWindows())
        {
            return "Not exercised: requires Windows.";
        }
        var device = IntPtr.Zero;
        var context = IntPtr.Zero;
        try
        {
            var result = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0, [0xB000], 1, 7,
                out device, out var selected, out context);
            if (result == unchecked((int)0x887A0004))
            {
                return "Not exercised: hardware feature level 11_0 unavailable (DXGI_ERROR_UNSUPPORTED).";
            }
            Assert.That(result, Is.GreaterThanOrEqualTo(0), $"Hardware preflight failed: 0x{result:X8}.");
            Assert.That(device, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(context, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(selected, Is.EqualTo(0xB000U));
            return null;
        }
        finally
        {
            if (context != IntPtr.Zero)
            {
                _ = Marshal.Release(context);
            }
            if (device != IntPtr.Zero)
            {
                _ = Marshal.Release(device);
            }
        }
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int D3D11CreateDevice(IntPtr adapter, int driver, IntPtr software, uint flags,
        [In] uint[] levels, uint levelCount, uint sdk, out IntPtr device, out uint selected, out IntPtr context);
}
