// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong CreateFormattingWorkspaceExport(int capacity);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong ConsumeFormattingWorkspaceExport(ulong buffer, int start, ref int length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong FormatUInt64Export(ulong value, ulong workspace);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong FormatInt64Export(long value, ulong workspace);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong FormatUInt32Export(uint value, ulong workspace);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong FormatInt32Export(int value, ulong workspace);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong FormatChar16Export(ushort value, ulong workspace);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong FormatUInt64BaseExport(ulong value, uint radix, ulong workspace);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong FormatInt64BaseExport(long value, uint radix, ulong workspace);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong FormatUInt32BaseExport(uint value, uint radix, ulong workspace);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong FormatInt32BaseExport(int value, uint radix, ulong workspace);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong FormatChar16BaseExport(ushort value, uint radix, ulong workspace);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong FormatUInt64BaseUpperExport(ulong value, uint radix, ulong workspace);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong FormatInt64BaseUpperExport(long value, uint radix, ulong workspace);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong FormatUInt32BaseUpperExport(uint value, uint radix, ulong workspace);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong FormatInt32BaseUpperExport(int value, uint radix, ulong workspace);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong FormatChar16BaseUpperExport(ushort value, uint radix, ulong workspace);

    private delegate ulong FormattingCall(ulong workspace);

    private static void AssertIntegerFormattingBufferExports(IntPtr handle)
    {
        var createWorkspace = Marshal.GetDelegateForFunctionPointer<CreateFormattingWorkspaceExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.CreateFormattingWorkspaceForAbiTest"));
        var consumeWorkspace = Marshal.GetDelegateForFunctionPointer<ConsumeFormattingWorkspaceExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.ConsumeFormattingWorkspaceForAbiTest"));
        var formatUInt64 = Marshal.GetDelegateForFunctionPointer<FormatUInt64Export>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.FormatUInt64"));
        var formatInt64 = Marshal.GetDelegateForFunctionPointer<FormatInt64Export>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.FormatInt64"));
        var formatUInt32 = Marshal.GetDelegateForFunctionPointer<FormatUInt32Export>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.FormatUInt32"));
        var formatInt32 = Marshal.GetDelegateForFunctionPointer<FormatInt32Export>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.FormatInt32"));
        var formatChar16 = Marshal.GetDelegateForFunctionPointer<FormatChar16Export>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.FormatChar16"));
        var formatUInt64Base = Marshal.GetDelegateForFunctionPointer<FormatUInt64BaseExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.FormatUInt64Base"));
        var formatInt64Base = Marshal.GetDelegateForFunctionPointer<FormatInt64BaseExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.FormatInt64Base"));
        var formatUInt32Base = Marshal.GetDelegateForFunctionPointer<FormatUInt32BaseExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.FormatUInt32Base"));
        var formatInt32Base = Marshal.GetDelegateForFunctionPointer<FormatInt32BaseExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.FormatInt32Base"));
        var formatChar16Base = Marshal.GetDelegateForFunctionPointer<FormatChar16BaseExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.FormatChar16Base"));
        var formatUInt64BaseUpper = Marshal.GetDelegateForFunctionPointer<FormatUInt64BaseUpperExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.FormatUInt64BaseUpper"));
        var formatInt64BaseUpper = Marshal.GetDelegateForFunctionPointer<FormatInt64BaseUpperExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.FormatInt64BaseUpper"));
        var formatUInt32BaseUpper = Marshal.GetDelegateForFunctionPointer<FormatUInt32BaseUpperExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.FormatUInt32BaseUpper"));
        var formatInt32BaseUpper = Marshal.GetDelegateForFunctionPointer<FormatInt32BaseUpperExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.FormatInt32BaseUpper"));
        var formatChar16BaseUpper = Marshal.GetDelegateForFunctionPointer<FormatChar16BaseUpperExport>(
            NativeLibrary.GetExport(handle, "Terrum.CodeAnalysis.Numerics64.FormatChar16BaseUpper"));

        foreach (var value in new ulong[] { 0, 1, 123456789, ulong.MaxValue })
        {
            AssertFormattedDigits(createWorkspace, consumeWorkspace, "FormatUInt64",
                value.ToString(CultureInfo.InvariantCulture),
                workspace => formatUInt64(value, workspace));
        }
        foreach (var value in new long[] { long.MinValue, -1, 0, 1, long.MaxValue })
        {
            AssertFormattedDigits(createWorkspace, consumeWorkspace, "FormatInt64",
                value.ToString(CultureInfo.InvariantCulture),
                workspace => formatInt64(value, workspace));
        }
        foreach (var value in new uint[] { 0, 1, 123456789, uint.MaxValue })
        {
            AssertFormattedDigits(createWorkspace, consumeWorkspace, "FormatUInt32",
                value.ToString(CultureInfo.InvariantCulture),
                workspace => formatUInt32(value, workspace));
        }
        foreach (var value in new int[] { int.MinValue, -1, 0, 1, int.MaxValue })
        {
            AssertFormattedDigits(createWorkspace, consumeWorkspace, "FormatInt32",
                value.ToString(CultureInfo.InvariantCulture),
                workspace => formatInt32(value, workspace));
        }
        foreach (var value in new ushort[] { 0, 9, 10, 0xD800, 0xFFFF })
        {
            AssertFormattedDigits(createWorkspace, consumeWorkspace, "FormatChar16",
                value.ToString(CultureInfo.InvariantCulture),
                workspace => formatChar16(value, workspace));
        }

        ulong[] unsigned64Values = [0, 1, ulong.MaxValue];
        long[] signed64Values = [long.MinValue, -1, 0, 1, long.MaxValue];
        uint[] unsigned32Values = [0, 1, uint.MaxValue];
        int[] signed32Values = [int.MinValue, -1, 0, 1, int.MaxValue];
        ushort[] char16Values = [0, 0xD800, 0xFFFF];

        for (uint radix = 2; radix <= 36; radix++)
        {
            foreach (var value in unsigned64Values)
            {
                var expected = FormatBaseValue(new BigInteger(value), radix, uppercase: false);
                AssertFormattedDigits(createWorkspace, consumeWorkspace, "FormatUInt64Base", expected,
                    workspace => formatUInt64Base(value, radix, workspace));
                expected = FormatBaseValue(new BigInteger(value), radix, uppercase: true);
                AssertFormattedDigits(createWorkspace, consumeWorkspace, "FormatUInt64BaseUpper", expected,
                    workspace => formatUInt64BaseUpper(value, radix, workspace));
            }
            foreach (var value in signed64Values)
            {
                var expected = FormatBaseValue(new BigInteger(value), radix, uppercase: false);
                AssertFormattedDigits(createWorkspace, consumeWorkspace, "FormatInt64Base", expected,
                    workspace => formatInt64Base(value, radix, workspace));
                expected = FormatBaseValue(new BigInteger(value), radix, uppercase: true);
                AssertFormattedDigits(createWorkspace, consumeWorkspace, "FormatInt64BaseUpper", expected,
                    workspace => formatInt64BaseUpper(value, radix, workspace));
            }
            foreach (var value in unsigned32Values)
            {
                var expected = FormatBaseValue(new BigInteger(value), radix, uppercase: false);
                AssertFormattedDigits(createWorkspace, consumeWorkspace, "FormatUInt32Base", expected,
                    workspace => formatUInt32Base(value, radix, workspace));
                expected = FormatBaseValue(new BigInteger(value), radix, uppercase: true);
                AssertFormattedDigits(createWorkspace, consumeWorkspace, "FormatUInt32BaseUpper", expected,
                    workspace => formatUInt32BaseUpper(value, radix, workspace));
            }
            foreach (var value in signed32Values)
            {
                var expected = FormatBaseValue(new BigInteger(value), radix, uppercase: false);
                AssertFormattedDigits(createWorkspace, consumeWorkspace, "FormatInt32Base", expected,
                    workspace => formatInt32Base(value, radix, workspace));
                expected = FormatBaseValue(new BigInteger(value), radix, uppercase: true);
                AssertFormattedDigits(createWorkspace, consumeWorkspace, "FormatInt32BaseUpper", expected,
                    workspace => formatInt32BaseUpper(value, radix, workspace));
            }
            foreach (var value in char16Values)
            {
                var expected = FormatBaseValue(new BigInteger(value), radix, uppercase: false);
                AssertFormattedDigits(createWorkspace, consumeWorkspace, "FormatChar16Base", expected,
                    workspace => formatChar16Base(value, radix, workspace));
                expected = FormatBaseValue(new BigInteger(value), radix, uppercase: true);
                AssertFormattedDigits(createWorkspace, consumeWorkspace, "FormatChar16BaseUpper", expected,
                    workspace => formatChar16BaseUpper(value, radix, workspace));
            }
        }

        var reusedWorkspace = createWorkspace(5);
        reusedWorkspace = formatUInt32(12345, reusedWorkspace);
        reusedWorkspace = formatInt32Base(-6789, 16, reusedWorkspace);
        AssertPackedDigits(consumeWorkspace, reusedWorkspace, 0, "-1a85");
    }

    private static void AssertFormattedDigits(
        CreateFormattingWorkspaceExport createWorkspace,
        ConsumeFormattingWorkspaceExport consumeWorkspace,
        string operation,
        string expected,
        FormattingCall format)
    {
        for (var start = 0; start < expected.Length; start += 8)
        {
            var workspace = createWorkspace(expected.Length);
            workspace = format(workspace);
            AssertPackedDigits(consumeWorkspace, workspace, start, expected, operation);
        }
    }

    private static void AssertPackedDigits(
        ConsumeFormattingWorkspaceExport consumeWorkspace,
        ulong workspace,
        int start,
        string expected,
        string operation = "formatter")
    {
        var guardedLength = new GuardedInt32 {
            Before = 0x13579BDF,
            Value = -1,
            After = 0x2468ACE0,
        };
        var packed = consumeWorkspace(workspace, start, ref guardedLength.Value);

        Assert.That(guardedLength.Value, Is.EqualTo(expected.Length));
        Assert.That(guardedLength.Before, Is.EqualTo(0x13579BDF));
        Assert.That(guardedLength.After, Is.EqualTo(0x2468ACE0));
        for (var offset = 0; offset < 8 && start + offset < expected.Length; offset++)
        {
            var actual = (char)((packed >> (offset * 8)) & 0xFF);
            Assert.That(expected[start + offset] == actual, Is.True, $"{operation} at output position {start + offset}: expected '{expected[start + offset]}', got '{actual}'.");
        }
    }

    private static string FormatBaseValue(BigInteger value, uint radix, bool uppercase)
    {
        const string lowercaseDigits = "0123456789abcdefghijklmnopqrstuvwxyz";
        const string uppercaseDigits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var digits = uppercase ? uppercaseDigits : lowercaseDigits;
        var remaining = BigInteger.Abs(value);
        var buffer = new char[65];
        var length = 0;
        do
        {
            var remainder = (int)(remaining % radix);
            buffer[length++] = digits[remainder];
            remaining /= radix;
        }
        while (!remaining.IsZero);

        if (value.Sign < 0)
        {
            buffer[length++] = '-';
        }
        Array.Reverse(buffer, 0, length);
        return new string(buffer, 0, length);
    }
}
