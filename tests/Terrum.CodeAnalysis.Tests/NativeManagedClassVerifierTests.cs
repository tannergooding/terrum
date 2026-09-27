// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeManagedClassVerifierTests
{
    private const string Source = """
        mut class Leaf {
            public mut int32 Value;
            public Leaf(int32 value) { Value = value; }
        }
        int32 Main() {
            require Leaf.TryCreate(42, out var leaf);
            leaf.Value = 43;
            return leaf.Value;
        }
        """;

    [Fact]
    public void RejectsForgedClassLayouts()
    {
        var program = Compile();
        var @class = program.Classes[0];
        var field = @class.Fields[0];
        var malformed = program with {
            Classes = [@class with { Fields = [field with { Offset = 8 }] }],
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(malformed));
        malformed = program with {
            Classes = [@class with { Fields = [field with { Type = (PrimitiveType)4096 }] }],
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(malformed));
        malformed = program with { Classes = [] };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(malformed));
        malformed = program with {
            Classes = [@class with { Fields = [field with { Type = (PrimitiveType)1280 }] }],
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(malformed));
        var main = program.Functions[program.EntryPoint];
        malformed = program with {
            Functions = program.Functions.SetItem(program.EntryPoint, main with {
                Variables = main.Variables.SetItem(0, main.Variables[0] with { Type = (PrimitiveType)1280 }),
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(malformed));
    }

    [Fact]
    public void ClassOnlySourceReportsNative19()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal("native-19", Compilation.GetNativeProfileVersion(program));
        Assert.Equal("native-19", compilation.NativeProfileVersion);
    }

    [Fact]
    public void RejectsForgedFactoryAndConstructorMetadata()
    {
        var program = Compile();
        var factoryIndex = FindFactory(program);
        var factory = program.Functions[factoryIndex];
        var malformed = program with {
            Functions = program.Functions.SetItem(factoryIndex, factory with { ClassConstructor = 4096 }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(malformed));
        malformed = program with {
            Functions = program.Functions.SetItem(factoryIndex, factory with { ClassConstructor = factoryIndex }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(malformed));
        malformed = program with {
            Functions = program.Functions.SetItem(factoryIndex, factory with {
                Variables = [],
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(malformed));
        var constructorIndex = factory.ClassConstructor;
        var constructor = program.Functions[constructorIndex];
        malformed = program with {
            Functions = program.Functions.SetItem(constructorIndex, constructor with {
                IsClassConstructor = false,
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(malformed));
        malformed = program with {
            Functions = program.Functions.SetItem(factoryIndex, factory with { Intrinsic = BoundIntrinsic.None }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(malformed));
        malformed = program with {
            Functions = program.Functions.SetItem(constructorIndex, constructor with {
                DeclaringStruct = "Other",
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(malformed));
    }

    [Fact]
    public void ManagedProofExternalsCannotEnterPersistedConsumer()
    {
        var producerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Export() { return 42; }"));
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(producerCompilation.Program), ["Export"]);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            int32 Main() { return Export(); }
            """), ModuleCompiler.CreateReference(producer));
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = Assert.IsType<BoundProgram>(consumerCompilation.Program);
        Assert.NotEmpty(ModuleCompiler.CreateConsumer(consumer, producer));
        var imported = Assert.Single(consumer.Functions, static function => function.IsExternal);
        var malformed = consumer with {
            Functions = consumer.Functions.Add(imported with {
                Name = "UnusedManaged",
                ReturnType = (PrimitiveType)ManagedClassTypes.First,
            }),
        };
        var normalError = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(malformed));
        Assert.Equal("Ownership", normalError.Data["Terrum.InvalidEffect"]);
        var persistedError = Assert.Throws<ArgumentException>(() =>
            ModuleCompiler.CreateConsumer(malformed, producer));
        Assert.Equal("Ownership", persistedError.Data["Terrum.InvalidEffect"]);
        var factoryIndex = FindFactory(consumer);
        var factory = consumer.Functions[factoryIndex];
        malformed = consumer with {
            Functions = consumer.Functions.SetItem(factoryIndex, factory with { ClassConstructor = 4096 }),
        };
        _ = Assert.Throws<ArgumentException>(() =>
            ModuleCompiler.CreateConsumer(malformed, producer));
        malformed = consumer with {
            Functions = consumer.Functions.SetItem(factoryIndex, factory with { Intrinsic = BoundIntrinsic.None }),
        };
        _ = Assert.Throws<ArgumentException>(() =>
            ModuleCompiler.CreateConsumer(malformed, producer));
    }

    [Fact]
    public void ClassDescriptorWithoutConstructionPairCannotEnterLibraryOrProducer()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            public int32 Export() { return 42; }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        SemanticVerifier.VerifyLibrary(program);
        Assert.NotEmpty(ModuleCompiler.CreateProducer(program, ["Export"]));
        var descriptorOnly = program with {
            Functions = [Assert.Single(program.Functions, static function => function.Name == "Export")],
        };
        _ = Assert.Single(descriptorOnly.Classes);
        Assert.Equal(-1, descriptorOnly.EntryPoint);
        Assert.Equal("Export", Assert.Single(descriptorOnly.Functions).Name);
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(descriptorOnly));
        _ = Assert.Throws<ArgumentException>(() => ModuleCompiler.CreateProducer(descriptorOnly, ["Export"]));
    }

    [Theory]
    [InlineData("struct Copy { public int64 Value; public Copy(int64 value) { Value = value; } }", true)]
    [InlineData("struct Copy { public int64 First; public int64 Second; public Copy(int64 value) { First = value; Second = value; } }", true)]
    [InlineData("unique struct Copy { public int32 Value; public Copy(int32 value) { Value = value; } [NoThrow] [Cleanup] public void Close() {} }", false)]
    public void ClassConstructorArgumentsRequireCopyRatherThanOwningValues(string declaration, bool allowed)
    {
        var source = declaration + Environment.NewLine + """
            class Leaf {
                public int32 Value;
                public Leaf(Copy input) { Value = 42; }
            }
            int32 Main() { return 0; }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        if (allowed)
        {
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            SemanticVerifier.Verify(Assert.IsType<BoundProgram>(compilation.Program));
        }
        else
        {
            Assert.Null(compilation.Program);
            Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3011");
        }
    }

    [Fact]
    public void RejectsForgedFactoryParameterLayoutsAndMetadataBeforeEmission()
    {
        var source = """
            class Leaf {
                public int32 Value;
                public Leaf(int32 ignored) { Value = 42; }
            }
            int32 Main() {
                require Leaf.TryCreate(1, out var leaf);
                return leaf.Value;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var factoryIndex = FindFactory(program);
        var factory = program.Functions[factoryIndex];
        var constructorIndex = factory.ClassConstructor;
        var constructor = program.Functions[constructorIndex];
        BoundStruct[] structures = [
            new BoundStruct("Copy8", [new BoundStructField("Value", PrimitiveType.Int64, 0, 8)], 8),
            new BoundStruct("Copy16", [
                new BoundStructField("First", PrimitiveType.Int64, 0, 8),
                new BoundStructField("Second", PrimitiveType.Int64, 8, 8),
            ], 16),
            new BoundStruct("Owner", [new BoundStructField("Value", PrimitiveType.Int32, 0, 4)], 4) {
                IsUnique = true,
            },
        ];
        foreach (var structure in structures)
        {
            var type = (PrimitiveType)100;
            var forgedFactory = factory with {
                Variables = factory.Variables.SetItem(0, factory.Variables[0] with { Type = type }),
            };
            var forgedConstructor = constructor with {
                Variables = constructor.Variables.SetItem(1, constructor.Variables[1] with { Type = type }),
            };
            var malformed = program with {
                Structures = [structure],
                Functions = program.Functions.SetItem(factoryIndex, forgedFactory)
                    .SetItem(constructorIndex, forgedConstructor),
            };
            _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(malformed));
        }

        var invalid = program with {
            Functions = program.Functions.SetItem(factoryIndex, factory with {
                Variables = factory.Variables.SetItem(0, null!),
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(invalid));
        invalid = program with {
            Functions = program.Functions.SetItem(factoryIndex, factory with {
                Variables = factory.Variables.SetItem(0, factory.Variables[0] with { Type = (PrimitiveType)4096 }),
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(invalid));
        invalid = program with {
            Functions = program.Functions.SetItem(constructorIndex, constructor with { Variables = default }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(invalid));
        invalid = program with {
            Functions = program.Functions.SetItem(factoryIndex, factory with {
                IsPrivate = true,
                IsInternal = false,
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(invalid));
    }

    [Fact]
    public void PrivateClassConstructorOnlyExposesFactoryToItsOwnMembers()
    {
        const string Declaration = """
            class Leaf {
                public int32 Value;
                private Leaf(int32 value) { Value = value; }
                public static bool Make(int32 value, out Leaf leaf) {
                    return Leaf.TryCreate(value, out leaf);
                }
            }
            """;
        var allowed = Compilation.Create(SyntaxTree.Parse(Declaration + """
            int32 Main() {
                require Leaf.Make(42, out var leaf);
                return leaf.Value;
            }
            """));
        Assert.False(allowed.HasErrors, string.Join(Environment.NewLine, allowed.Diagnostics));
        var program = Assert.IsType<BoundProgram>(allowed.Program);
        var factory = program.Functions[FindFactory(program)];
        Assert.True(factory.IsPrivate);
        Assert.False(factory.IsInternal);

        var forbidden = Compilation.Create(SyntaxTree.Parse(Declaration + """
            int32 Main() {
                require Leaf.TryCreate(42, out var leaf);
                return leaf.Value;
            }
            """));
        Assert.Null(forbidden.Program);
        Assert.Contains(forbidden.Diagnostics, diagnostic => diagnostic.Code == "TRM3011"
            && diagnostic.Message.Contains("private", StringComparison.Ordinal));
    }

    [Fact]
    public void MutableClassMethodsCanWriteFieldsThroughWritableReceivers()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            mut class Leaf {
                public mut int32 Value;
                public Leaf(int32 value) { Value = value; }
                public mut void Advance() {
                    Value = Value + 1;
                    this.Value = Value + 1;
                }
            }
            int32 Main() {
                require Leaf.TryCreate(40, out var leaf);
                leaf.Advance();
                return leaf.Value;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        _ = Assert.IsType<BoundProgram>(compilation.Program);
    }

    [Theory]
    [InlineData("Value = 0;")]
    [InlineData("this.Value = 0;")]
    public void ClassMethodsCannotWriteReadonlyFields(string write)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
                public void Reset() {
            """ + write + """
                }
            }
            int32 Main() { return 0; }
            """));
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3008");
    }

    [Fact]
    public void RejectsDirectConstructorCallsAndMalformedFieldPaths()
    {
        var program = Compile();
        var factory = program.Functions[FindFactory(program)];
        var main = program.Functions[program.EntryPoint];
        var leaf = Array.FindIndex(main.Variables.ToArray(),
            static variable => variable.Name == "leaf");
        var call = new BoundCall(factory.ClassConstructor,
            [new BoundRead(leaf, (PrimitiveType)ManagedClassTypes.First, main.Span),
                new BoundConstant(43, PrimitiveType.Int32, main.Span)],
            PrimitiveType.Void, main.Span);
        var direct = main with {
            Body = main.Body with {
                Statements = main.Body.Statements.Insert(main.Body.Statements.Length - 1,
                    new BoundExpressionStatement(call, main.Span)),
            },
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, direct),
        }));

        var assignment = Assert.Single(main.Body.Statements.OfType<BoundFieldAssignment>());
        var index = main.Body.Statements.IndexOf(assignment);
        var forged = main with {
            Body = main.Body with {
                Statements = main.Body.Statements.SetItem(index, assignment with { NestedFields = [0] }),
            },
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, forged),
        }));
    }

    [Fact]
    public void PermitsWritesThroughExplicitlyWritableManagedEdges()
    {
        var source = """
            mut class Leaf {
                public mut int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            class Pair {
                public write Leaf Left;
                public Pair(write Leaf left) { Left = left; }
            }
            int32 Main() {
                require Leaf.TryCreate(40, out var leaf);
                require Pair.TryCreate(leaf, out var pair);
                pair.Left.Value = 42;
                return leaf.Value;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.Verify(Assert.IsType<BoundProgram>(compilation.Program));
    }

    [Fact]
    public void ForgedReadonlyProjectionAndReadonlyFieldWriteCannotManufactureAuthority()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            mut class Leaf {
                public mut int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            int32 Observe(Leaf leaf) { return leaf.Value; }
            int32 Main() {
                require Leaf.TryCreate(40, out var leaf);
                Leaf view = leaf;
                leaf.Value = 42;
                return Observe(leaf);
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var main = program.Functions[program.EntryPoint];
        var view = Array.FindIndex(main.Variables.ToArray(),
            static variable => variable.Name == "view");
        var projection = new BoundReadonlyProjection(
            new BoundRead(view, (PrimitiveType)ManagedClassTypes.First, main.Span),
            (PrimitiveType)ManagedClassTypes.First, main.Span);
        var returned = Assert.IsType<BoundReturn>(main.Body.Statements[^1]);
        var call = Assert.IsType<BoundCall>(returned.Value);
        var forged = main with {
            Body = main.Body with {
                Statements = main.Body.Statements.SetItem(main.Body.Statements.Length - 1,
                    returned with { Value = call with { Arguments = call.Arguments.SetItem(0, projection) } }),
            },
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, forged),
        }));

        var assignment = Assert.Single(main.Body.Statements.OfType<BoundFieldAssignment>());
        var index = main.Body.Statements.IndexOf(assignment);
        forged = main with {
            Body = main.Body with {
                Statements = main.Body.Statements.SetItem(index, assignment with { Variable = view }),
            },
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, forged),
        }));
    }

    private static BoundProgram Compile()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    private static int FindFactory(BoundProgram program) =>
        Array.FindIndex(program.Functions.ToArray(),
            static function => function.Intrinsic == BoundIntrinsic.ClassTryCreate);
}
