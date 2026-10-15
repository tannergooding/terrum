// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Text;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeHandleOwnerContractTests
{
    private const string OwnerSource = """
        namespace Example.Windowing;

        [Terrum.Interop.NativeImport("windows-x64.system32.kernel32.GetModuleHandleW")]
        [NoThrow]
        extern Terrum.Interop.Windows.HINSTANCE GetModuleHandleW(
            Terrum.Interop.Windows.NativeNullPointer moduleName);

        [Terrum.Interop.NativeImport("windows-x64.system32.user32.CreateWindowExW")]
        [NoThrow]
        extern Terrum.Interop.Windows.HWND CreateWindowExW(
            uint32 exStyle, ptr<char16> className, ptr<char16> windowName, uint32 style,
            int32 x, int32 y, int32 width, int32 height, Terrum.Interop.Windows.HWND parent,
            Terrum.Interop.Windows.HMENU menu, Terrum.Interop.Windows.HINSTANCE instance,
            Terrum.Interop.Windows.LPVOID parameter);

        [Terrum.Interop.NativeImport("windows-x64.system32.user32.DestroyWindow")]
        [NoThrow]
        extern int32 DestroyWindow(Terrum.Interop.Windows.HWND window);

        public unique struct SurfaceOwner {
            private Terrum.Interop.Windows.HWND Handle;

            public SurfaceOwner() {
                unsafe {
                    Handle = CreateWindowExW(
                        0, utf16z("Example.Window.Class"), utf16z("Example Window"), 0,
                        0, 0, 1, 1, Terrum.Interop.Windows.HWND.NoOwner, null,
                        GetModuleHandleW(null), null);
                }
            }

            [NoThrow] [Cleanup]
            public void Close() {
                if (Handle != Terrum.Interop.Windows.HWND.NoOwner) {
                    unsafe { DestroyWindow(Handle); }
                }
            }
        }

        int32 Main() {
            unsafe {
                Terrum.Interop.Windows.HWND window = CreateWindowExW(
                    0, utf16z("Another.Window.Class"), utf16z("Temporary Window"), 0,
                    0, 0, 1, 1, Terrum.Interop.Windows.HWND.NoOwner, null,
                    GetModuleHandleW(null), null);
                if (window != Terrum.Interop.Windows.HWND.NoOwner) {
                    DestroyWindow(window);
                }
            }
            SurfaceOwner surface = new SurfaceOwner();
            return 0;
        }
        """;

    [Test]
    public void GenericUniqueCleanupOwnsAWindowHandleWithoutSampleProtocolRecognition()
    {
        var compilation = Compilation.CreateWithNativeImports(
            SyntaxTree.Parse(OwnerSource), BoundRuntimePolicy.CheckedExceptions);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
        Assert.That(Encoding.ASCII.GetString(NativeEmitter.EmitWithNativeImports(
            compilation.Program!, NativeRuntimePolicy.CheckedExceptions)),
            Does.Contain("DestroyWindow"));
    }

    [Test]
    public void WindowHandleStorageRequiresAUniqueCleanupOwner()
    {
        var invalidSources = new[] {
            OwnerSource.Replace("public unique struct SurfaceOwner", "struct SurfaceOwner",
                StringComparison.Ordinal),
            OwnerSource.Replace("[NoThrow] [Cleanup]", "[NoThrow]", StringComparison.Ordinal),
            OwnerSource.Replace("private Terrum.Interop.Windows.HWND Handle;",
                "public Terrum.Interop.Windows.HWND Handle;", StringComparison.Ordinal),
        };
        foreach (var source in invalidSources)
        {
            var compilation = Compilation.CreateWithNativeImports(
                SyntaxTree.Parse(source), BoundRuntimePolicy.CheckedExceptions);

            Assert.That(compilation.Program, Is.Null);
            Assert.That(compilation.HasErrors, Is.True);
        }
    }

    [Test]
    public void WindowCallbackHandleUseDependsOnTheCallbackSignatureNotAnOwnerName()
    {
        var source = SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("windows-x64.system32.user32.DefWindowProcW")]
            [NoThrow]
            extern unsafe int64 DefWindowProcW(Terrum.Interop.Windows.HWND window,
                uint32 message, uint64 wParam, int64 lParam);

            [NoThrow]
            unsafe int64 GenericWindowProcedure(Terrum.Interop.Windows.HWND window,
                uint32 message, uint64 wParam, int64 lParam) {
                unsafe { return DefWindowProcW(window, message, wParam, lParam); }
            }

            int32 Main() { return 0; }
            """);
        var compilation = Compilation.CreateWithNativeImports(
            source, BoundRuntimePolicy.CheckedExceptions);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeEmitter.EmitWithNativeImports(
            compilation.Program!, NativeRuntimePolicy.CheckedExceptions), Is.Not.Empty);
    }
}
