// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private const string Vertex3ImportKey = "test.application.native-import-abi.NativeFfiVertex3";
    private const int Vertex3ArrayCount = 3;
    private const int Vertex3ArrayStride = Vertex3ArrayCount * sizeof(float);
    private const int Vertex3ArrayAlignment = sizeof(float);
    private const int Vertex3ArraySize = Vertex3ArrayCount * Vertex3ArrayStride;
    private static ImmutableArray<NativeFixedArrayFieldLayoutDefinition> Vertex3FieldLayout { get; } = [
        new("X", nameof(PrimitiveType.Float32), 0, sizeof(float)),
        new("Y", nameof(PrimitiveType.Float32), sizeof(float), sizeof(float)),
        new("Z", nameof(PrimitiveType.Float32), 2 * sizeof(float), sizeof(float)),
    ];
    private static NativeFixedArrayStorageLayoutDefinition Vertex3StorageLayout(string elementTypeName) =>
        new(elementTypeName, Vertex3ArrayCount, Vertex3ArrayStride, Vertex3ArrayAlignment, Vertex3ArraySize) {
            ElementFields = Vertex3FieldLayout,
        };
    private const string Vertex3Initializer = """
        InlineArray<Vertex, 3> vertices = [
            new Vertex(0, 0.6, 0.5), new Vertex(0.6, -0.5, 0.5), new Vertex(-0.6, -0.5, 0.5)];
        """;
    private const string Vertex3Call = """
        NativeFfiVertex3(&vertices[0], 324478056, 81985529216486895,
            -1234567890123, 610800471, &vertices[0])
        """;
    private static readonly string Vertex3Declarations = $$"""
        [Blittable] [Layout(Sequential)]
        struct Vertex {
            float32 X;
            float32 Y;
            float32 Z;
            public Vertex(float32 x, float32 y, float32 z) {
                X = x;
                Y = y;
                Z = z;
            }
            public float32 GetX() { return X; }
            public float32 GetY() { return Y; }
            public float32 GetZ() { return Z; }
        }

        [Terrum.Interop.NativeImport("{{Vertex3ImportKey}}")]
        [NoThrow]
        extern int32 NativeFfiVertex3(Terrum.Interop.NativeStorageAddress first,
            uint32 second, uint64 third, int64 fourth, uint32 fifth,
            Terrum.Interop.NativeStorageAddress sixth);

        int32 VertexIndex(int32 value) { return value; }

        int32 CheckVertex3InitializationOrder() {
            mut int32 index = 0;
            InlineArray<Vertex, 3> vertices = [
                new Vertex(float32(index++), 0, 0), new Vertex(float32(index++), 0, 0),
                new Vertex(float32(index++), 0, 0)];
            if (index != 3 || vertices[0].GetX() != 0 || vertices[1].GetX() != 1
                || vertices[2].GetX() != 2) {
                return 1;
            }
            return 0;
        }

        int32 CheckVertex3() {
            {{Vertex3Initializer}}
            if (vertices[VertexIndex(0)].GetX() != 0
                || vertices[VertexIndex(0)].GetY() != 0.6
                || vertices[VertexIndex(0)].GetZ() != 0.5
                || vertices[VertexIndex(1)].GetX() != 0.6
                || vertices[VertexIndex(1)].GetY() != -0.5
                || vertices[VertexIndex(1)].GetZ() != 0.5
                || vertices[VertexIndex(2)].GetX() != -0.6
                || vertices[VertexIndex(2)].GetY() != -0.5
                || vertices[VertexIndex(2)].GetZ() != 0.5) {
                return 20;
            }
            mut int32 index = 0;
            if (vertices[index++].GetY() != 0.6 || index != 1) {
                return 21;
            }
            index = 0;
            int32 result = NativeFfiVertex3(sixth: &vertices[0], fifth: 610800471,
                fourth: -1234567890123, third: 81985529216486895,
                second: 324478056, first: &vertices[0]);
            if (index != 0) {
                return 22;
            }
            if (result != 0) {
                return result;
            }
            return {{Vertex3Call}};
        }
        """;

    private static NativeImportDefinition CreateVertex3ImportDefinition() =>
        new(Vertex3ImportKey, "native-import-abi-probe.dll", "NativeFfiVertex3",
            [PrimitiveType.NativeStorageAddress, PrimitiveType.UInt32, PrimitiveType.UInt64,
                PrimitiveType.Int64, PrimitiveType.UInt32, PrimitiveType.NativeStorageAddress],
            PrimitiveType.Int32, true, true, false, NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [
                NativeImportParameterContract.ReadonlyNativeStorageNoEscape,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.ReadonlyNativeStorageNoEscape,
            ],
            StorageParameters = [
                new(0, PrimitiveType.NativeStorageAddress, PrimitiveType.NativeFixedArray,
                    Vertex3ArraySize, IsReadonly: true, RequiredStartIndex: 0,
                    ElementTypeName: "Vertex", ElementCount: Vertex3ArrayCount,
                    ElementStride: Vertex3ArrayStride, ElementAlignment: Vertex3ArrayAlignment) {
                    ElementFields = Vertex3FieldLayout,
                },
                new(5, PrimitiveType.NativeStorageAddress, PrimitiveType.NativeFixedArray,
                    Vertex3ArraySize, IsReadonly: true, RequiredStartIndex: 0,
                    ElementTypeName: "Vertex", ElementCount: Vertex3ArrayCount,
                    ElementStride: Vertex3ArrayStride, ElementAlignment: Vertex3ArrayAlignment) {
                    ElementFields = Vertex3FieldLayout,
                },
            ],
        };

    private static Compilation CompileVertex3(string body,
        BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast, string? declarations = null) =>
        Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse((declarations ?? Vertex3Declarations) + "\nint32 Main() {\n" + body + "\n}")],
            NativeImportCatalog.CreateForTesting("test-native-vertex3-v1", [CreateVertex3ImportDefinition()]),
            policy);

    [TestCase("utf16z")]
    [TestCase("vertex3")]
    public void NativeInitializerNamesDoNotHideInferredGenericCallsOutsideNativeProfiles(string name)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            [NoThrow]
            T {{name}}<T>(T value) where T : [Copy] { return value; }
            int32 Main() { return {{name}}(42); }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        Assert.That(NativeJit.Execute(((BoundProgram)__nunitAssertValue0),
                    NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void Vertex3StorageHasExactLayoutAndIsTestOnlyAot()
    {
        var compilation = CompileVertex3(Vertex3Initializer + "\nreturn " + Vertex3Call + ";");
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue1 = compilation.Program;
        Assert.That(__nunitAssertValue1, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue1);
        var __nunitAssertValue2 = global::System.Linq.Enumerable.ToArray(program.Structures);
        Assert.That(__nunitAssertValue2, Has.Exactly(1).Items);
        var vertex = __nunitAssertValue2[0];
        Assert.That(vertex.Size, Is.EqualTo(12));
        Assert.That(ValueLayout.Alignment((PrimitiveType)100, program.Structures), Is.EqualTo(4));
        Assert.That(vertex.Fields.Select(static field => field.Offset), Is.EqualTo(new[] { 0, 4, 8 }));
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue3 = main.Body.Statements[0];
        Assert.That(__nunitAssertValue3, Is.TypeOf<BoundLocal>());
        var local = ((BoundLocal)__nunitAssertValue3);
        var __nunitAssertValue4 = local.Initializer;
        Assert.That(__nunitAssertValue4, Is.TypeOf<BoundNativeFixedArrayInitialization>());
        var initializer = ((BoundNativeFixedArrayInitialization)__nunitAssertValue4);
        Assert.That(initializer.Type, Is.EqualTo(PrimitiveType.NativeFixedArray));
        Assert.That(initializer.Layout.ElementCount, Is.EqualTo(3));
        Assert.That(initializer.Layout.ElementStride, Is.EqualTo(12));
        Assert.That(initializer.Layout.ByteExtent, Is.EqualTo(36));
        Assert.That(initializer.Layout.Alignment, Is.EqualTo(4));
        Assert.That(initializer.Elements.Length, Is.EqualTo(3));
        Assert.Multiple(() => {
            foreach (var element in initializer.Elements)
                Assert.That(element.Type, Is.EqualTo((PrimitiveType)100));
        });
        _ = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() => NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() =>
            ModuleCompiler.CreateProducer(program with { EntryPoint = -1 }, ["CheckVertex3"]));
        _ = Assert.Throws<NotSupportedException>(() => NativeModuleCompiler.CreateProducer(
            program with { EntryPoint = -1 }, ["CheckVertex3"], "vertex3", NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(program with { EntryPoint = -1 }, "vertex3"));

        var ordinary = Compilation.Create(SyntaxTree.Parse(Vertex3Declarations + "int32 Main() { return 0; }"));
        Assert.That(ordinary.Program, Is.Null);
        var production = Compilation.CreateWithNativeImports(
            SyntaxTree.Parse(Vertex3Declarations + "int32 Main() { return 0; }"));
        Assert.That(production.Program, Is.Null);
        Assert.That(NativeImportCatalog.WindowsX64.SupportsFixedArrayStorage(
            PrimitiveType.NativeFixedArray), Is.True);
        Assert.That(NativeImportCatalog.WindowsX64.SupportsCataloguedFixedArrayStorage, Is.False);
        Assert.That(CompileVertex3("return CheckVertex3();", BoundRuntimePolicy.CheckedExceptions).Program, Is.Null);

        var stripped = program with {
            NativeImportCatalog = null,
            NativeImportCatalogId = null,
            Functions = program.Functions.Select(static function =>
                function with { NativeImportKey = null }).ToImmutableArray(),
        };
        _ = Assert.Throws<NotSupportedException>(() => SemanticVerifier.RejectSourceNativeImports(stripped, "JIT"));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(stripped, NativeRuntimePolicy.FailFast));
    }

    [TestCase("InlineArray<Vertex, 3> incomplete = [new Vertex(0, 0, 0), new Vertex(1, 1, 1)]; return 0;")]
    [TestCase("InlineArray<Vertex, 3> oversized = [new Vertex(0, 0, 0), new Vertex(1, 1, 1), new Vertex(2, 2, 2), new Vertex(3, 3, 3)]; return 0;")]
    [TestCase("init Vertex vacant; InlineArray<Vertex, 3> incomplete = [new Vertex(0, 0, 0), vacant, new Vertex(2, 2, 2)]; return 0;")]
    [TestCase("init InlineArray<Vertex, 3> vacant; return 0;")]
    [TestCase("mut InlineArray<Vertex, 3> mutable = [new Vertex(0, 0, 0), new Vertex(1, 1, 1), new Vertex(2, 2, 2)]; return 0;")]
    [TestCase("mut InlineArray<Vertex, 3> copy = vertices; return 0;")]
    [TestCase("InlineArray<Vertex, 3> copy = vertices; return 0;")]
    [TestCase("return vertices[-1].GetX() == 0 ? 0 : 1;")]
    [TestCase("return vertices[3].GetX() == 0 ? 0 : 1;")]
    [TestCase("return NativeFfiVertex3(&vertices[1], 0, 0, 0, 0, &vertices[0]);")]
    [TestCase("return NativeFfiVertex3(&vertices[-1], 0, 0, 0, 0, &vertices[0]);")]
    [TestCase("return NativeFfiVertex3(&vertices[3], 0, 0, 0, 0, &vertices[0]);")]
    [TestCase("return NativeFfiVertex3(&vertices, 0, 0, 0, 0, &vertices[0]);")]
    [TestCase("Terrum.Interop.NativeStorageAddress pointer = &vertices[0]; return 0;")]
    [TestCase("return NativeFfiVertex3(true ? &vertices[0] : &vertices[0], 0, 0, 0, 0, &vertices[0]);")]
    [TestCase("return NativeFfiVertex3(&vertices[0] + 1, 0, 0, 0, 0, &vertices[0]);")]
    [TestCase("vertices[0] = new Vertex(1, 1, 1); return 0;")]
    [TestCase("vertices!; return NativeFfiVertex3(&vertices[0], 0, 0, 0, 0, &vertices[0]);")]
    [TestCase("init Vertex vacant; return NativeFfiVertex3(&vacant, 0, 0, 0, 0, &vertices[0]);")]
    [TestCase("return NativeFfiVertex3(0, 0, 0, 0, 0, &vertices[0]);")]
    [TestCase("return NativeFfiVertex3(null, 0, 0, 0, 0, &vertices[0]);")]
    [TestCase("InlineArray<float32, 4> values = [0, 0, 0, 0]; return 0;")]
    [TestCase("return NativeFfiVertex3(&vertices[0].GetX(), 0, 0, 0, 0, &vertices[0]);")]
    [TestCase("require ManagedNode.TryCreate(0, out var node); return NativeFfiVertex3(&node, 0, 0, 0, 0, &vertices[0]);")]
    [TestCase("InlineArray<Vertex, 3>? optional = vertices; return 0;")]
    [TestCase("Terrum.Interop.NativeStorageAddress? optional = &vertices[0]; return 0;")]
    public void Vertex3RejectsIncompleteStorageBoundsAndAddressEscape(string body)
    {
        var compilation = CompileVertex3(Vertex3Initializer + "\n" + body);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("float32 X;\n    float32 Y;", "float32 Y;\n    float32 X;")]
    [TestCase("float32 Z;", "float32 W;")]
    [TestCase("float32 Z;", "float64 Z;")]
    [TestCase("[Layout(Sequential)]", "")]
    [TestCase("Z = z;", "")]
    public void Vertex3RejectsWrongShapeAndIncompleteVertexConstruction(string oldValue, string newValue)
    {
        var original = Vertex3Declarations.ReplaceLineEndings("\n");
        var declarations = original.Replace(oldValue, newValue, StringComparison.Ordinal);
        Assert.That(declarations, Is.Not.EqualTo(original));
        var compilation = CompileVertex3("return CheckVertex3();", declarations: declarations);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [Test]
    public void Vertex3RejectsWrongNominalIdentityAndMutableElementReceiver()
    {
        var wrongName = Vertex3Declarations.Replace("struct Vertex", "struct OtherVertex", StringComparison.Ordinal)
            .Replace("public Vertex(", "public OtherVertex(", StringComparison.Ordinal)
            .Replace("new Vertex(", "new OtherVertex(", StringComparison.Ordinal);
        Assert.That(CompileVertex3("return CheckVertex3();", declarations: wrongName).Program, Is.Null);

        var mutableFields = Vertex3Declarations.Replace("float32 X;", "mut float32 X;", StringComparison.Ordinal)
            .Replace("public float32 GetX()", "public mut void SetX(float32 x) { X = x; }\npublic float32 GetX()",
                StringComparison.Ordinal);
        var valid = CompileVertex3("return CheckVertex3();", declarations: mutableFields);
        Assert.That(valid.HasErrors, Is.False, string.Join(Environment.NewLine, valid.Diagnostics));
        var invalid = CompileVertex3(Vertex3Initializer + "\nvertices[0].SetX(1); return 0;",
            declarations: mutableFields);
        Assert.That(invalid.Program, Is.Null);
        Assert.That(invalid.Diagnostics, Is.Not.Empty);
    }

    [TestCase("struct Holder { Terrum.Interop.Dx11.Vertex3 Value; }")]
    [TestCase("mut class Holder { Terrum.Interop.Dx11.Vertex3 Value; }")]
    [TestCase("int32 Forward(Terrum.Interop.Dx11.Vertex3 value) { return 0; }")]
    [TestCase("int32 Forward(Terrum.Interop.Dx11.ReadonlyVertex3 value) { return 0; }")]
    [TestCase("Terrum.Interop.Dx11.Vertex3 Escape() { " + Vertex3Initializer + " return vertices; }")]
    [TestCase("Terrum.Interop.Dx11.ReadonlyVertex3 Escape() { " + Vertex3Initializer + " return &vertices[0]; }")]
    [TestCase("struct Holder { Terrum.Interop.Dx11.ReadonlyVertex3 Value; }")]
    [TestCase("mut class Holder { Terrum.Interop.Dx11.ReadonlyVertex3 Value; }")]
    public void Vertex3RejectsStorageForwardingAndAuthorityEscalation(string declaration)
    {
        var compilation = CompileVertex3("return CheckVertex3();", declarations: Vertex3Declarations + declaration);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [Test]
    public void Vertex3StorageAddressRejectsSpecialParameterQualifiers()
    {
        foreach (var qualifier in new[] { "mut ", "out ", "init ", "write " })
        {
            var source = Vertex3Declarations.Replace("Terrum.Interop.NativeStorageAddress first",
                qualifier + "Terrum.Interop.NativeStorageAddress first", StringComparison.Ordinal);
            Assert.That(CompileVertex3("return CheckVertex3();", declarations: source).Program, Is.Null);
        }
    }

    [Test]
    public void Vertex3MandatoryVerificationRejectsForgedInitializationAndLoans()
    {
        var compilation = CompileVertex3(Vertex3Initializer + "\nreturn " + Vertex3Call + ";");
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue5 = compilation.Program;
        Assert.That(__nunitAssertValue5, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue5);
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue6 = main.Body.Statements[0];
        Assert.That(__nunitAssertValue6, Is.TypeOf<BoundLocal>());
        var local = ((BoundLocal)__nunitAssertValue6);
        var __nunitAssertValue7 = local.Initializer;
        Assert.That(__nunitAssertValue7, Is.TypeOf<BoundNativeFixedArrayInitialization>());
        var vertices = ((BoundNativeFixedArrayInitialization)__nunitAssertValue7);
        var fixedArrayLayout = main.Variables[local.Variable].NativeFixedArrayLayout
            ?? throw new InvalidOperationException("The verified local must carry its layout.");
        var __nunitAssertValue8 = main.Body.Statements[1];
        Assert.That(__nunitAssertValue8, Is.TypeOf<BoundReturn>());
        var result = ((BoundReturn)__nunitAssertValue8);
        var __nunitAssertValue9 = result.Value;
        Assert.That(__nunitAssertValue9, Is.TypeOf<BoundCall>());
        var call = ((BoundCall)__nunitAssertValue9);
        var __nunitAssertValue10 = call.Arguments[0];
        Assert.That(__nunitAssertValue10, Is.TypeOf<BoundNativeStorageAddress>());
        var address = ((BoundNativeStorageAddress)__nunitAssertValue10);
        Assert.That(address.CarrierType, Is.EqualTo(PrimitiveType.NativeFixedArray));
        Assert.That(address.AddressType, Is.EqualTo(PrimitiveType.NativeStorageAddress));
        Assert.That(address.ElementCount, Is.EqualTo(Vertex3ArrayCount));
        Assert.That(address.ElementStride, Is.EqualTo(Vertex3ArrayStride));
        Assert.That(address.ByteExtent, Is.EqualTo(Vertex3ArraySize));
        Assert.That(address.IsReadonly, Is.True);

        void Reject(BoundFunction forged)
        {
            _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithNativeImportsForTesting(
                program with { Functions = program.Functions.SetItem(program.EntryPoint, forged) },
                NativeRuntimePolicy.FailFast));
        }
        foreach (var elements in new[] {
            default(ImmutableArray<BoundExpression>),
            ImmutableArray<BoundExpression>.Empty,
            vertices.Elements.RemoveAt(2),
            vertices.Elements.Add(vertices.Elements[0]),
            vertices.Elements.SetItem(2, new BoundConstant(0, vertices.Layout.ElementType, vertices.Span)),
        })
        {
            Reject(main with {
                Body = main.Body with {
                    Statements = main.Body.Statements.SetItem(0, local with {
                        Initializer = vertices with { Elements = elements },
                    }),
                }
            });
        }
        foreach (var index in new[] { -1, 1, 3, int.MaxValue })
        {
            var forgedCall = call with {
                Arguments = call.Arguments.SetItem(0,
                address with { Index = new BoundConstant(index, PrimitiveType.Int32, address.Span) })
            };
            Reject(main with {
                Body = main.Body with {
                    Statements = main.Body.Statements.SetItem(1, result with { Value = forgedCall }),
                }
            });
        }
        foreach (var forgedAddress in new[] {
            address with { CarrierType = PrimitiveType.NativeDx11BufferDesc },
            address with { ElementType = PrimitiveType.Float32 },
            address with { AddressType = PrimitiveType.NativeDx11BufferDescAddress },
            address with { ElementCount = Vertex3ArrayCount + 1 },
            address with { ElementStride = Vertex3ArrayStride + 1 },
            address with { ByteExtent = Vertex3ArraySize - 1 },
            address with { IsReadonly = false },
        })
        {
            Reject(main with {
                Body = main.Body with {
                    Statements = main.Body.Statements.SetItem(1, result with {
                        Value = call with {
                            Arguments = call.Arguments.SetItem(0, forgedAddress),
                        }
                    }),
                }
            });
        }
        Reject(main with {
            Variables = main.Variables.SetItem(local.Variable,
            main.Variables[local.Variable] with { IsMutable = true })
        });
        Reject(main with {
            Variables = main.Variables.SetItem(local.Variable,
            main.Variables[local.Variable] with {
                NativeFixedArrayLayout = fixedArrayLayout with { ElementType = PrimitiveType.Float32 },
            })
        });
        Reject(main with { Body = main.Body with { Statements = main.Body.Statements.RemoveAt(0) } });
        Reject(main with {
            Body = main.Body with {
                Statements = main.Body.Statements.SetItem(1, result with {
                    Value = call with {
                        Arguments = call.Arguments.SetItem(0, new BoundConstant(0,
                    PrimitiveType.NativeStorageAddress, address.Span)),
                    }
                }),
            }
        });
        Reject(main with {
            Body = main.Body with {
                Statements = main.Body.Statements.SetItem(1, result with {
                    Value = call with {
                        Arguments = call.Arguments.SetItem(1, address),
                    }
                }),
            }
        });
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithNativeImportsForTesting(
            program with {
                Structures = program.Structures.SetItem(0, program.Structures[0] with {
                    Fields = program.Structures[0].Fields.SetItem(1, program.Structures[0].Fields[1] with { Name = "W" }),
                })
            }, NativeRuntimePolicy.FailFast));
    }

    [Test]
    public void Vertex3MandatoryVerificationRejectsForgedElementBoundsAndType()
    {
        var compilation = CompileVertex3(Vertex3Initializer + "\nreturn vertices[0].GetX() == 0 ? 0 : 1;");
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue11 = compilation.Program;
        Assert.That(__nunitAssertValue11, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue11);
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue12 = main.Body.Statements[1];
        Assert.That(__nunitAssertValue12, Is.TypeOf<BoundReturn>());
        var result = ((BoundReturn)__nunitAssertValue12);
        var __nunitAssertValue13 = result.Value;
        Assert.That(__nunitAssertValue13, Is.TypeOf<BoundConditional>());
        var conditional = ((BoundConditional)__nunitAssertValue13);
        var __nunitAssertValue14 = conditional.Condition;
        Assert.That(__nunitAssertValue14, Is.TypeOf<BoundBinary>());
        var comparison = ((BoundBinary)__nunitAssertValue14);
        var __nunitAssertValue15 = comparison.Left;
        Assert.That(__nunitAssertValue15, Is.TypeOf<BoundCall>());
        var getter = ((BoundCall)__nunitAssertValue15);
        var __nunitAssertValue16 = global::System.Linq.Enumerable.ToArray(getter.Arguments);
        Assert.That(__nunitAssertValue16, Has.Exactly(1).Items);
        var __nunitAssertValue17 = __nunitAssertValue16[0];
        Assert.That(__nunitAssertValue17, Is.TypeOf<BoundNativeFixedArrayElement>());
        var vertex = ((BoundNativeFixedArrayElement)__nunitAssertValue17);
        foreach (var forged in new[] {
            vertex with { Index = new BoundConstant(-1, PrimitiveType.Int32, vertex.Span) },
            vertex with { Index = new BoundConstant(3, PrimitiveType.Int32, vertex.Span) },
            vertex with { Index = new BoundConstant(0, PrimitiveType.UInt32, vertex.Span) },
            vertex with { Variable = main.Variables.Length },
            vertex with { Type = PrimitiveType.Float32 },
            vertex with { Layout = vertex.Layout with { ElementStride = sizeof(float) } },
        })
        {
            var forgedMain = main with {
                Body = main.Body with {
                    Statements = main.Body.Statements.SetItem(1, result with {
                        Value = conditional with {
                            Condition = comparison with { Left = getter with { Arguments = [forged] } },
                        }
                    }),
                }
            };
            _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithNativeImportsForTesting(
                program with { Functions = program.Functions.SetItem(program.EntryPoint, forgedMain) },
                NativeRuntimePolicy.FailFast));
        }
    }

    [Test]
    public void Vertex3NoThrowRequiresStaticallySafeBounds()
    {
        var declarations = Vertex3Declarations.Replace("public Vertex(", "[NoThrow] public Vertex(",
            StringComparison.Ordinal);
        var possiblyFailingFunctions = new[] {
            "[NoThrow] int32 ReadVertex(int32 index) { " + Vertex3Initializer
                + " Vertex copy = vertices[index]; return 0; }",
            "[NoThrow] int32 LoanVertex3(int32 index) { " + Vertex3Initializer
                + " return NativeFfiVertex3(&vertices[index], 324478056, 81985529216486895, "
                + "-1234567890123, 610800471, &vertices[0]); }",
        };
        foreach (var function in possiblyFailingFunctions)
        {
            var compilation = CompileVertex3("return 0;", declarations: declarations + function);
            Assert.That(compilation.Program, Is.Null);
            Assert.That(compilation.Diagnostics, Is.Not.Empty);
        }

        var safeFunctions = new[] {
            "[NoThrow] int32 ReadVertex() { " + Vertex3Initializer
                + " Vertex copy = vertices[0]; return 0; }",
            "[NoThrow] int32 LoanVertex3() { " + Vertex3Initializer
                + " return NativeFfiVertex3(&vertices[0], 324478056, 81985529216486895, "
                + "-1234567890123, 610800471, &vertices[0]); }",
        };
        foreach (var function in safeFunctions)
        {
            var compilation = CompileVertex3("return 0;", declarations: declarations + function);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
    }

    [Test]
    public void Vertex3MandatoryVerificationRejectsForgedNoThrowDynamicBounds()
    {
        var declarations = Vertex3Declarations.Replace("public Vertex(", "[NoThrow] public Vertex(",
            StringComparison.Ordinal);
        var source = Vertex3Initializer + """
            int32 index = 0;
            Vertex copy = vertices[0];
            return NativeFfiVertex3(&vertices[0], 324478056, 81985529216486895,
                -1234567890123, 610800471, &vertices[0]);
            """;
        var compilation = CompileVertex3(source, declarations: declarations);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue18 = compilation.Program;
        Assert.That(__nunitAssertValue18, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue18);
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue19 = main.Body.Statements[1];
        Assert.That(__nunitAssertValue19, Is.TypeOf<BoundLocal>());
        var index = ((BoundLocal)__nunitAssertValue19);
        var __nunitAssertValue20 = main.Body.Statements[2];
        Assert.That(__nunitAssertValue20, Is.TypeOf<BoundLocal>());
        var copy = ((BoundLocal)__nunitAssertValue20);
        var __nunitAssertValue21 = copy.Initializer;
        Assert.That(__nunitAssertValue21, Is.TypeOf<BoundNativeFixedArrayElement>());
        var vertex = ((BoundNativeFixedArrayElement)__nunitAssertValue21);
        var __nunitAssertValue22 = main.Body.Statements[3];
        Assert.That(__nunitAssertValue22, Is.TypeOf<BoundReturn>());
        var result = ((BoundReturn)__nunitAssertValue22);
        var __nunitAssertValue23 = result.Value;
        Assert.That(__nunitAssertValue23, Is.TypeOf<BoundCall>());
        var call = ((BoundCall)__nunitAssertValue23);
        var __nunitAssertValue24 = call.Arguments[0];
        Assert.That(__nunitAssertValue24, Is.TypeOf<BoundNativeStorageAddress>());
        var address = ((BoundNativeStorageAddress)__nunitAssertValue24);
        var dynamicIndex = new BoundRead(index.Variable, PrimitiveType.Int32, index.Span);

        void Reject(BoundFunction forged)
        {
            _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithNativeImportsForTesting(
                program with { Functions = program.Functions.SetItem(program.EntryPoint, forged) },
                NativeRuntimePolicy.FailFast));
        }

        Reject(main with {
            IsNoThrow = true,
            Body = main.Body with {
                Statements = main.Body.Statements.SetItem(2, copy with {
                    Initializer = vertex with { Index = dynamicIndex },
                }),
            }
        });
        Reject(main with {
            IsNoThrow = true,
            Body = main.Body with {
                Statements = main.Body.Statements.SetItem(3, result with {
                    Value = call with {
                        Arguments = call.Arguments.SetItem(0, address with { Index = dynamicIndex }),
                    }
                }),
            }
        });
    }

    private static async Task ValidateVertex3RuntimeBoundsAsync(string directory)
    {
        var variant = 0;
        foreach (var body in new[] {
            "return vertices[VertexIndex(-1)].GetX() == 0 ? 0 : 1;",
            "return vertices[VertexIndex(3)].GetX() == 0 ? 0 : 1;",
            "return vertices[VertexIndex(2147483647)].GetX() == 0 ? 0 : 1;",
        })
        {
            var compilation = CompileVertex3(Vertex3Initializer + "\n" + body);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var __nunitAssertValue25 = compilation.Program;
            Assert.That(__nunitAssertValue25, Is.TypeOf<BoundProgram>());
            var program = ((BoundProgram)__nunitAssertValue25);
            var path = Path.Combine(directory, $"vertex3-bounds-{variant++}.exe");
            await File.WriteAllBytesAsync(path,
                NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast))
                .ConfigureAwait(false);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory,
            }) ?? throw new InvalidOperationException("Vertex3 bounds executable did not start.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
                throw;
            }
            Assert.That(process.ExitCode, Is.EqualTo(unchecked((int)0xE0540004)));
        }
    }
}
