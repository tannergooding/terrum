// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Modules;

public sealed class NativeParameterExtentPersistenceTests
{
    [Test]
    public void FullUnsignedExtentSurvivesPrivateModuleRoundTrip()
    {
        var source = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("extent.dll", "ObserveValues")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            public extern int32 ObserveValues(ptr<uint32> values);
            """));
        Assert.That(source.HasErrors, Is.False,
            string.Join(Environment.NewLine, source.Diagnostics));

        var artifact = ModuleCompiler.CreateProducer(
            WithExtent(source.Program!, uint.MaxValue), ["ObserveValues"]);
        var reference = ModuleCompiler.CreateReference(artifact);
        var imported = reference.Functions.Single(static function =>
            function.Name == "ObserveValues").DirectNativeImport!;
        Assert.That(imported.Parameters.Single().Extent, Is.EqualTo(uint.MaxValue));
    }

    [Test]
    public void FullUnsignedExtentSurvivesNativeVTablePrivateRoundTrip()
    {
        var source = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            [Terrum.Interop.NativeVTableMethod(3)]
            public extern unsafe uint32 Invoke(ptr<void> receiver, ptr<uint32> values);
            """));
        Assert.That(source.HasErrors, Is.False,
            string.Join(Environment.NewLine, source.Diagnostics));

        var artifact = ModuleCompiler.CreateProducer(
            WithVTableExtent(source.Program!, uint.MaxValue), ["Invoke"]);
        var reference = ModuleCompiler.CreateReference(artifact);
        var method = reference.Functions.Single(static function =>
            function.Name == "Invoke").NativeVTableMethod!;
        Assert.That(method.Parameters.Single().Extent, Is.EqualTo(uint.MaxValue));
    }

    private static BoundProgram WithExtent(BoundProgram program, uint extent)
    {
        var functions = program.Functions.ToBuilder();
        for (var index = 0; index < functions.Count; index++)
        {
            var function = functions[index];
            if (function.Name != "ObserveValues")
            {
                continue;
            }

            var import = function.DirectNativeImport
                ?? throw new AssertionException("Expected a direct NativeImport.");
            var parameters = import.Parameters.ToBuilder();
            parameters[0] = parameters[0] with { Extent = extent };
            functions[index] = function with
            {
                DirectNativeImport = import with
                {
                    Parameters = parameters.MoveToImmutable(),
                },
            };
            return program with { Functions = functions.MoveToImmutable() };
        }

        throw new AssertionException("Expected the bound NativeImport declaration.");
    }

    private static BoundProgram WithVTableExtent(BoundProgram program, uint extent)
    {
        var functions = program.Functions.ToBuilder();
        for (var index = 0; index < functions.Count; index++)
        {
            var function = functions[index];
            if (function.Name != "Invoke")
            {
                continue;
            }

            var method = function.NativeVTableMethod
                ?? throw new AssertionException("Expected a native vtable method.");
            var parameters = method.Parameters.ToBuilder();
            parameters[0] = parameters[0] with { Extent = extent };
            functions[index] = function with
            {
                NativeVTableMethod = method with
                {
                    Parameters = parameters.MoveToImmutable(),
                },
            };
            return program with { Functions = functions.MoveToImmutable() };
        }

        throw new AssertionException("Expected the bound native vtable method.");
    }
}
