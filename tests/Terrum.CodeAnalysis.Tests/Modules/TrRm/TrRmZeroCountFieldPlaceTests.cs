// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

internal sealed class TrRmZeroCountFieldPlaceTests
{
    [Test]
    public void RejectsSourceFieldPlaceSelectionIntoZeroCountArray()
    {
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Item {
                public mut uint8 Value;

                internal Item() {
                    Value = uint8(0);
                }
            }

            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Packet {
                public mut [Item; 0] Items;

                internal Packet() {
                    Items = [];
                }
            }

            int32 Main() {
                mut Packet packet = new Packet();
                packet.Items[0].Value = uint8(1);
                return 0;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });

        Assert.That(compilation.Diagnostics.Select(static diagnostic
                => diagnostic.Code),
            Is.EqualTo(new[] { "TRM3030" }));
        Assert.That(compilation.Diagnostics.Single().Message,
            Does.Contain("Fixed-array element index must be in [0, 0)."));
    }
}
