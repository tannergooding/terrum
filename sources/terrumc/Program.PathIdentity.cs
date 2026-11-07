// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Terrum.Tools;

internal static partial class Program
{
    private readonly record struct FileIdentity(uint VolumeSerialNumber,
        uint FileIndexHigh, uint FileIndexLow);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        internal uint FileAttributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName,
        uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes,
        IntPtr templateFile);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file, out ByHandleFileInformation information);

    private static bool PathsAlias(string left, string right)
    {
        var leftPath = ResolveParentLinks(Path.GetFullPath(left));
        var rightPath = ResolveParentLinks(Path.GetFullPath(right));
        if (StringComparer.OrdinalIgnoreCase.Equals(leftPath, rightPath))
        {
            return true;
        }

        var leftIdentity = GetFileIdentity(leftPath);
        var rightIdentity = GetFileIdentity(rightPath);
        return leftIdentity is FileIdentity leftFile
            && rightIdentity is FileIdentity rightFile
            && leftFile == rightFile;
    }

    private static string ResolveParentLinks(string fullPath)
    {
        var directoryPath = Path.GetDirectoryName(fullPath);
        if (directoryPath is null)
        {
            return fullPath;
        }
        var directory = new DirectoryInfo(directoryPath);
        var resolvedDirectory = directory.ResolveLinkTarget(returnFinalTarget: true);
        var parentPath = resolvedDirectory?.FullName ?? directory.FullName;
        return Path.GetFullPath(Path.Combine(parentPath,
            Path.GetFileName(fullPath)));
    }

    private static FileIdentity? GetFileIdentity(string path)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path))
        {
            return null;
        }

        using var file = CreateFile(path, 0,
            shareMode: 1 | 2 | 4, IntPtr.Zero,
            creationDisposition: 3, flagsAndAttributes: 0,
            IntPtr.Zero);
        if (file.IsInvalid)
        {
            throw new IOException(
                $"Could not inspect file identity for '{path}'.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
        if (!GetFileInformationByHandle(file, out var information))
        {
            throw new IOException(
                $"Could not inspect file identity for '{path}'.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
        return new FileIdentity(information.VolumeSerialNumber,
            information.FileIndexHigh, information.FileIndexLow);
    }
}
