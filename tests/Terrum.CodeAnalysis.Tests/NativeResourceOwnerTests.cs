// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Text;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeResourceOwnerTests
{
    private const string OwnerSource = """
        [Terrum.Interop.NativeImport("fixture.dll", "AcquireResource")]
        [NoThrow]
        extern unsafe int32 AcquireResource();

        [Terrum.Interop.NativeImport("fixture.dll", "ReleaseResource")]
        [NoThrow]
        extern unsafe void ReleaseResource(int32 handle);

        public unique struct ResourceOwner {
            private int32 Handle;

            public ResourceOwner(int32 handle) {
                Handle = handle;
            }

            [NoThrow] [Cleanup]
            public void Close() {
                unsafe { ReleaseResource(Handle); }
            }
        }

        int32 Main() {
            ResourceOwner owner = new ResourceOwner(unsafe(AcquireResource()));
            return 0;
        }
        """;

    [Test]
    public void GenericUniqueCleanupOwnsAnOpaqueNativeResource()
    {
        var compilation = Compilation.Create(
            SyntaxTree.Parse(OwnerSource), BoundRuntimePolicy.CheckedExceptions);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
        Assert.That(Encoding.ASCII.GetString(NativeEmitter.EmitWithNativeImports(
            compilation.Program!, NativeRuntimePolicy.CheckedExceptions)),
            Does.Contain("ReleaseResource"));
    }

    [Test]
    public void NativeCallableForwardsOpaquePointerToAnUnrelatedImport()
    {
        var source = SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("fixture.dll", "Observe")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            [NoThrow]
            extern unsafe int64 Observe(ptr<void> context, uint32 message,
                uint64 wParam, int64 lParam);

            [NoThrow] [Terrum.Interop.NativeCallable]
            unsafe int64 GenericCallback(ptr<void> context,
                uint32 message, uint64 wParam, int64 lParam) {
                unsafe { return Observe(context, message, wParam, lParam); }
            }

            int32 Main() { return 0; }
            """);
        var compilation = Compilation.Create(
            source, BoundRuntimePolicy.CheckedExceptions);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var image = NativeEmitter.EmitWithNativeImports(
            compilation.Program!, NativeRuntimePolicy.CheckedExceptions);
        Assert.That(image, Is.Not.Empty);
        Assert.That(Encoding.ASCII.GetString(image), Does.Contain("Observe"));
    }
}
